namespace ScreenTimer.Core;

public enum Phase { Session, Break }

public enum ExtendResult { Ok, WrongCode, NotInBreak, NoCodeSet, LockedOut, InvalidMinutes, UnknownAccount }

/// <summary>Per-account timer state. Persisted so a break survives logoff and reboot.</summary>
public sealed class AccountState
{
    public string Sid { get; set; } = "";
    public string Name { get; set; } = "";
    public Phase Phase { get; set; } = Phase.Session;
    public double SessionRemainingSec { get; set; }
    public DateTime? BreakEndsUtc { get; set; }
    public DateTime LastTickUtc { get; set; }

    /// <summary>When the account stopped being active (locked, logged off, asleep); null while active.</summary>
    public DateTime? InactiveSinceUtc { get; set; }

    public int FailedAttempts { get; set; }
    public DateTime? LockedOutUntilUtc { get; set; }
}

public sealed record ObservedAccount(string Name, bool Active);

public sealed record StatusDto(
    bool Known,
    Phase Phase,
    double RemainingSec,
    double TotalSec,
    DateTime? BreakEndsUtc,
    int WarningSec,
    bool CodeConfigured,
    int[] ExtensionMinutes,
    double LockoutSec,
    DateTime ServerUtc);

/// <summary>
/// Pure time-keeping logic with an injected clock. Session time only runs while the account is
/// active; a break always runs on the wall clock, including while logged off or powered down.
/// </summary>
public sealed class TimerEngine
{
    /// <summary>A tick interval longer than this means the machine slept or the service was down.</summary>
    public const double MaxTickGapSec = 5;

    readonly object gate = new();
    readonly Func<AppConfig> config;
    readonly Dictionary<string, AccountState> accounts = new(StringComparer.OrdinalIgnoreCase);

    public TimerEngine(Func<AppConfig> config, IEnumerable<AccountState>? restored = null)
    {
        this.config = config;
        if (restored is null) return;
        foreach (var s in restored)
            if (!string.IsNullOrEmpty(s.Sid)) accounts[s.Sid] = s;
    }

    public void Tick(DateTime nowUtc, IReadOnlyDictionary<string, ObservedAccount> observed)
    {
        lock (gate)
        {
            var cfg = config();
            foreach (var (sid, obs) in observed)
            {
                if (!accounts.TryGetValue(sid, out var s))
                {
                    s = NewAccount(sid, obs.Name, nowUtc, cfg, obs.Active);
                    accounts[sid] = s;
                    continue;
                }
                s.Name = obs.Name;
                Advance(s, nowUtc, obs.Active, cfg);
            }

            foreach (var s in accounts.Values)
                if (!observed.ContainsKey(s.Sid))
                    Advance(s, nowUtc, active: false, cfg);
        }
    }

    public StatusDto GetStatus(string sid, DateTime nowUtc)
    {
        lock (gate)
        {
            var cfg = config();
            if (!accounts.TryGetValue(sid, out var s))
                return new StatusDto(false, Phase.Session, 0, 0, null, 0, cfg.HasCode, cfg.ExtensionMinutes, 0, nowUtc);

            var remaining = s.Phase == Phase.Break
                ? Math.Max(0, ((s.BreakEndsUtc ?? nowUtc) - nowUtc).TotalSeconds)
                : Math.Max(0, s.SessionRemainingSec);
            var total = s.Phase == Phase.Break ? cfg.BreakMinutes * 60.0 : cfg.SessionMinutes * 60.0;
            var lockout = s.LockedOutUntilUtc is { } until ? Math.Max(0, (until - nowUtc).TotalSeconds) : 0;

            return new StatusDto(true, s.Phase, remaining, total, s.BreakEndsUtc, (int)(cfg.WarningMinutes * 60),
                cfg.HasCode, cfg.ExtensionMinutes, lockout, nowUtc);
        }
    }

    public ExtendResult TryExtend(string sid, int minutes, string? code, DateTime nowUtc, out double lockoutSec)
    {
        lock (gate)
        {
            lockoutSec = 0;
            var cfg = config();
            if (!accounts.TryGetValue(sid, out var s)) return ExtendResult.UnknownAccount;
            if (s.Phase != Phase.Break) return ExtendResult.NotInBreak;
            if (!cfg.HasCode) return ExtendResult.NoCodeSet;
            if (!cfg.ExtensionMinutes.Contains(minutes)) return ExtendResult.InvalidMinutes;

            if (s.LockedOutUntilUtc is { } until && until > nowUtc)
            {
                lockoutSec = (until - nowUtc).TotalSeconds;
                return ExtendResult.LockedOut;
            }

            if (!cfg.VerifyCode(code))
            {
                s.FailedAttempts++;
                if (s.FailedAttempts >= 3)
                {
                    lockoutSec = Math.Min(300, 15 * Math.Pow(2, s.FailedAttempts - 3));
                    s.LockedOutUntilUtc = nowUtc.AddSeconds(lockoutSec);
                }
                return ExtendResult.WrongCode;
            }

            s.FailedAttempts = 0;
            s.LockedOutUntilUtc = null;
            s.Phase = Phase.Session;
            s.SessionRemainingSec = minutes * 60.0;
            s.BreakEndsUtc = null;
            s.InactiveSinceUtc = null;
            s.LastTickUtc = nowUtc;
            return ExtendResult.Ok;
        }
    }

    public List<AccountState> Snapshot()
    {
        lock (gate)
            return accounts.Values.Select(s => new AccountState
            {
                Sid = s.Sid,
                Name = s.Name,
                Phase = s.Phase,
                SessionRemainingSec = s.SessionRemainingSec,
                BreakEndsUtc = s.BreakEndsUtc,
                LastTickUtc = s.LastTickUtc,
                InactiveSinceUtc = s.InactiveSinceUtc,
                FailedAttempts = s.FailedAttempts,
                LockedOutUntilUtc = s.LockedOutUntilUtc,
            }).ToList();
    }

    public Phase? GetPhase(string sid)
    {
        lock (gate) return accounts.TryGetValue(sid, out var s) ? s.Phase : null;
    }

    static AccountState NewAccount(string sid, string name, DateTime now, AppConfig cfg, bool active) => new()
    {
        Sid = sid,
        Name = name,
        Phase = Phase.Session,
        SessionRemainingSec = cfg.SessionMinutes * 60.0,
        LastTickUtc = now,
        InactiveSinceUtc = active ? null : now,
    };

    static void Advance(AccountState s, DateTime now, bool active, AppConfig cfg)
    {
        var prev = s.LastTickUtc;
        var dt = Math.Max(0, (now - prev).TotalSeconds); // a clock moved backwards adds nothing
        s.LastTickUtc = now;

        // After sleep or a service restart the elapsed time was not observed: count it as inactivity.
        if (dt > MaxTickGapSec && s.InactiveSinceUtc is null)
            s.InactiveSinceUtc = prev;
        var wasActive = s.InactiveSinceUtc is null;

        var breakJustEnded = false;
        if (s.Phase == Phase.Break && s.BreakEndsUtc is { } end && now >= end)
        {
            breakJustEnded = true;
            s.Phase = Phase.Session;
            s.SessionRemainingSec = cfg.SessionMinutes * 60.0;
            s.BreakEndsUtc = null;
            s.FailedAttempts = 0;
            s.LockedOutUntilUtc = null;
        }

        if (!active)
        {
            if (wasActive) s.InactiveSinceUtc = now;
            return;
        }

        if (!wasActive)
        {
            // Away at least as long as a break counts as having taken one.
            if (s.Phase == Phase.Session && now - s.InactiveSinceUtc!.Value >= TimeSpan.FromMinutes(cfg.BreakMinutes))
                s.SessionRemainingSec = cfg.SessionMinutes * 60.0;
            s.InactiveSinceUtc = null;
        }
        else if (s.Phase == Phase.Session && !breakJustEnded)
        {
            s.SessionRemainingSec -= dt;
        }

        if (s.Phase == Phase.Session && s.SessionRemainingSec <= 0)
        {
            s.Phase = Phase.Break;
            s.SessionRemainingSec = 0;
            s.BreakEndsUtc = now.AddMinutes(cfg.BreakMinutes);
        }
    }
}
