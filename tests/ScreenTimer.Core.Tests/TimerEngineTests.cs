using ScreenTimer.Core;

namespace ScreenTimer.Core.Tests;

public class TimerEngineTests
{
    const string Sid = "S-1-5-21-1";
    static readonly DateTime T0 = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

    // PBKDF2 is deliberately slow, so build the configs once rather than on every tick.
    static readonly AppConfig WithCode = Create(withCode: true);
    static readonly AppConfig WithoutCode = Create(withCode: false);

    static AppConfig Create(bool withCode)
    {
        var cfg = new AppConfig();
        if (withCode) cfg.SetCode("1234");
        return cfg;
    }

    static AppConfig Config(bool withCode = true) => withCode ? WithCode : WithoutCode;

    static Dictionary<string, ObservedAccount> Obs(bool active) =>
        new() { [Sid] = new ObservedAccount("PC\\kid", active) };

    /// <summary>Ticks once per second for the given time, like the service does.</summary>
    static DateTime Run(TimerEngine e, DateTime from, double seconds, bool active)
    {
        var now = from;
        for (var i = 0; i < seconds; i++)
        {
            now = now.AddSeconds(1);
            e.Tick(now, Obs(active));
        }
        return now;
    }

    [Fact]
    public void New_account_starts_a_full_session()
    {
        var e = new TimerEngine(() => Config());
        e.Tick(T0, Obs(true));

        var s = e.GetStatus(Sid, T0);
        Assert.Equal(Phase.Session, s.Phase);
        Assert.Equal(30 * 60, s.RemainingSec);
    }

    [Fact]
    public void Session_counts_down_while_active()
    {
        var e = new TimerEngine(() => Config());
        e.Tick(T0, Obs(true));
        var now = Run(e, T0, 60, active: true);

        Assert.Equal(29 * 60, e.GetStatus(Sid, now).RemainingSec, 1);
    }

    [Fact]
    public void Break_starts_after_thirty_minutes()
    {
        var e = new TimerEngine(() => Config());
        e.Tick(T0, Obs(true));
        var now = Run(e, T0, 30 * 60, active: true);

        var s = e.GetStatus(Sid, now);
        Assert.Equal(Phase.Break, s.Phase);
        Assert.Equal(30 * 60, s.RemainingSec, 1);
    }

    [Fact]
    public void Break_ends_and_a_new_session_starts()
    {
        var e = new TimerEngine(() => Config());
        e.Tick(T0, Obs(true));
        var now = Run(e, T0, 30 * 60, active: true);
        now = Run(e, now, 30 * 60 + 1, active: true);

        var s = e.GetStatus(Sid, now);
        Assert.Equal(Phase.Session, s.Phase);
        Assert.Equal(30 * 60 - 1, s.RemainingSec, 2);
    }

    [Fact]
    public void Session_time_pauses_while_inactive()
    {
        var e = new TimerEngine(() => Config());
        e.Tick(T0, Obs(true));
        var now = Run(e, T0, 10 * 60, active: true);
        now = Run(e, now, 10 * 60, active: false);
        now = Run(e, now, 1, active: true);

        Assert.Equal(20 * 60, e.GetStatus(Sid, now).RemainingSec, 2);
    }

    [Fact]
    public void Absence_as_long_as_a_break_starts_a_fresh_session()
    {
        var e = new TimerEngine(() => Config());
        e.Tick(T0, Obs(true));
        var now = Run(e, T0, 10 * 60, active: true);
        now = Run(e, now, 30 * 60, active: false);
        now = Run(e, now, 1, active: true);

        Assert.Equal(30 * 60, e.GetStatus(Sid, now).RemainingSec, 2);
    }

    [Fact]
    public void Break_keeps_running_while_logged_off_and_blocks_on_return()
    {
        var e = new TimerEngine(() => Config());
        e.Tick(T0, Obs(true));
        var now = Run(e, T0, 30 * 60, active: true);
        now = Run(e, now, 10 * 60, active: false);
        now = Run(e, now, 1, active: true);

        var s = e.GetStatus(Sid, now);
        Assert.Equal(Phase.Break, s.Phase);
        Assert.Equal(20 * 60 - 1, s.RemainingSec, 2);
    }

    [Fact]
    public void Break_ending_while_logged_off_gives_a_fresh_session()
    {
        var e = new TimerEngine(() => Config());
        e.Tick(T0, Obs(true));
        var now = Run(e, T0, 30 * 60, active: true);
        now = Run(e, now, 31 * 60, active: false);
        now = Run(e, now, 1, active: true);

        var s = e.GetStatus(Sid, now);
        Assert.Equal(Phase.Session, s.Phase);
        Assert.Equal(30 * 60, s.RemainingSec, 2);
    }

    [Fact]
    public void Sleep_gap_is_treated_as_inactivity_not_used_time()
    {
        var e = new TimerEngine(() => Config());
        e.Tick(T0, Obs(true));
        var now = Run(e, T0, 5 * 60, active: true);

        now = now.AddMinutes(10); // laptop slept; no ticks
        e.Tick(now, Obs(true));
        now = Run(e, now, 1, active: true);

        Assert.Equal(25 * 60 - 1, e.GetStatus(Sid, now).RemainingSec, 2);
    }

    [Fact]
    public void Break_survives_service_restart_via_persisted_state()
    {
        var e = new TimerEngine(() => Config());
        e.Tick(T0, Obs(true));
        var now = Run(e, T0, 30 * 60, active: true);

        var restarted = new TimerEngine(() => Config(), e.Snapshot());
        now = now.AddMinutes(5);
        restarted.Tick(now, Obs(true));

        var s = restarted.GetStatus(Sid, now);
        Assert.Equal(Phase.Break, s.Phase);
        Assert.Equal(25 * 60, s.RemainingSec, 2);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    public void Correct_code_extends_a_break_into_a_session(int minutes)
    {
        var cfg = Config();
        var e = new TimerEngine(() => cfg);
        e.Tick(T0, Obs(true));
        var now = Run(e, T0, 30 * 60, active: true);

        var result = e.TryExtend(Sid, minutes, "1234", now, out _);

        Assert.Equal(ExtendResult.Ok, result);
        var s = e.GetStatus(Sid, now);
        Assert.Equal(Phase.Session, s.Phase);
        Assert.Equal(minutes * 60, s.RemainingSec, 1);
    }

    [Fact]
    public void Extended_time_is_followed_by_a_new_break()
    {
        var cfg = Config();
        var e = new TimerEngine(() => cfg);
        e.Tick(T0, Obs(true));
        var now = Run(e, T0, 30 * 60, active: true);
        e.TryExtend(Sid, 5, "1234", now, out _);
        now = Run(e, now, 5 * 60, active: true);

        Assert.Equal(Phase.Break, e.GetStatus(Sid, now).Phase);
    }

    [Fact]
    public void Wrong_code_is_rejected_and_break_stays()
    {
        var cfg = Config();
        var e = new TimerEngine(() => cfg);
        e.Tick(T0, Obs(true));
        var now = Run(e, T0, 30 * 60, active: true);

        Assert.Equal(ExtendResult.WrongCode, e.TryExtend(Sid, 5, "0000", now, out _));
        Assert.Equal(Phase.Break, e.GetStatus(Sid, now).Phase);
    }

    [Fact]
    public void Repeated_wrong_codes_lock_out_even_the_right_code()
    {
        var cfg = Config();
        var e = new TimerEngine(() => cfg);
        e.Tick(T0, Obs(true));
        var now = Run(e, T0, 30 * 60, active: true);

        for (var i = 0; i < 3; i++) e.TryExtend(Sid, 5, "0000", now, out _);

        Assert.Equal(ExtendResult.LockedOut, e.TryExtend(Sid, 5, "1234", now, out var wait));
        Assert.True(wait > 0);

        now = now.AddSeconds(wait + 1);
        Assert.Equal(ExtendResult.Ok, e.TryExtend(Sid, 5, "1234", now, out _));
    }

    [Fact]
    public void Cannot_extend_without_a_configured_code()
    {
        var cfg = Config(withCode: false);
        var e = new TimerEngine(() => cfg);
        e.Tick(T0, Obs(true));
        var now = Run(e, T0, 30 * 60, active: true);

        Assert.Equal(ExtendResult.NoCodeSet, e.TryExtend(Sid, 5, "1234", now, out _));
    }

    [Fact]
    public void Cannot_extend_outside_a_break_or_with_an_unlisted_length()
    {
        var cfg = Config();
        var e = new TimerEngine(() => cfg);
        e.Tick(T0, Obs(true));
        Assert.Equal(ExtendResult.NotInBreak, e.TryExtend(Sid, 5, "1234", T0, out _));

        var now = Run(e, T0, 30 * 60, active: true);
        Assert.Equal(ExtendResult.InvalidMinutes, e.TryExtend(Sid, 60, "1234", now, out _));
    }

    [Fact]
    public void Pruned_account_starts_a_fresh_session_when_added_again()
    {
        var e = new TimerEngine(() => Config());
        e.Tick(T0, Obs(true));
        var now = Run(e, T0, 30 * 60, active: true);
        Assert.Equal(Phase.Break, e.GetStatus(Sid, now).Phase);

        e.Prune([]);
        Assert.False(e.GetStatus(Sid, now).Known);

        e.Tick(now.AddSeconds(1), Obs(true));
        var s = e.GetStatus(Sid, now.AddSeconds(1));
        Assert.Equal(Phase.Session, s.Phase);
        Assert.Equal(30 * 60, s.RemainingSec, 1);
    }

    [Fact]
    public void Accounts_are_tracked_independently()
    {
        var e = new TimerEngine(() => Config());
        var both = new Dictionary<string, ObservedAccount>
        {
            ["A"] = new("PC\\a", true),
            ["B"] = new("PC\\b", false),
        };
        var now = T0;
        e.Tick(now, both);
        for (var i = 0; i < 600; i++) { now = now.AddSeconds(1); e.Tick(now, both); }

        Assert.Equal(20 * 60, e.GetStatus("A", now).RemainingSec, 2);
        Assert.Equal(30 * 60, e.GetStatus("B", now).RemainingSec, 2);
    }
}

public class AppConfigTests
{
    [Fact]
    public void Code_is_hashed_and_verified()
    {
        var cfg = new AppConfig();
        cfg.SetCode("tajne-haslo");

        Assert.DoesNotContain("tajne-haslo", cfg.CodeHash);
        Assert.True(cfg.VerifyCode("tajne-haslo"));
        Assert.False(cfg.VerifyCode("tajne-hasl0"));
        Assert.False(cfg.VerifyCode(null));
    }

    [Fact]
    public void Too_short_code_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new AppConfig().SetCode("12"));
    }

    [Fact]
    public void Config_round_trips_through_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var cfg = new AppConfig { SessionMinutes = 45 };
            cfg.SetCode("1234");
            ConfigStore.Save(cfg, path);

            var loaded = ConfigStore.Load(path);
            Assert.Equal(45, loaded.SessionMinutes);
            Assert.True(loaded.VerifyCode("1234"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Nobody_is_limited_until_accounts_are_chosen()
    {
        Assert.False(new AppConfig().IsLimited("S-1-5-21-1"));
    }

    [Fact]
    public void Only_chosen_accounts_are_limited_matched_by_sid()
    {
        var cfg = new AppConfig { LimitedAccounts = [new AccountRef { Sid = "S-1-5-21-9", Name = "PC\\kid" }] };

        Assert.True(cfg.IsLimited("S-1-5-21-9"));
        Assert.True(cfg.IsLimited("s-1-5-21-9"));
        Assert.False(cfg.IsLimited("S-1-5-21-1"));
        Assert.False(cfg.IsLimited(null));
    }

    [Fact]
    public void Chosen_accounts_round_trip_through_disk_and_duplicates_collapse()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            ConfigStore.Save(new AppConfig
            {
                LimitedAccounts =
                [
                    new AccountRef { Sid = "S-1-5-21-9", Name = "PC\\kid" },
                    new AccountRef { Sid = "s-1-5-21-9", Name = "PC\\kid" },
                    new AccountRef { Sid = " ", Name = "blank" },
                ],
            }, path);

            var loaded = ConfigStore.Load(path);
            var only = Assert.Single(loaded.LimitedAccounts);
            Assert.Equal("PC\\kid", only.Name);
        }
        finally { File.Delete(path); }
    }
}
