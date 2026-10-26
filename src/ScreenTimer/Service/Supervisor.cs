using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using ScreenTimer.Core;

namespace ScreenTimer.Service;

/// <summary>
/// The authoritative side of the app. Once a second it learns who is at the screen, advances the
/// timers, keeps the tray app running in each session and removes Task Manager during a break.
/// </summary>
internal sealed class Supervisor
{
    static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(5);

    readonly SessionTracker sessions = new();
    readonly AppLauncher launcher;
    readonly TimerEngine engine;
    readonly PipeServer pipe;
    readonly CancellationTokenSource cts = new();
    readonly object configGate = new();

    AppConfig config = new();
    DateTime configStamp = DateTime.MinValue;
    volatile bool saveRequested;
    DateTime lastSave = DateTime.MinValue;
    Thread? thread;

    public Supervisor()
    {
        SecureStorage.EnsureDirectory();
        ReloadConfigIfChanged();

        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown process path");
        launcher = new AppLauncher(exe);
        engine = new TimerEngine(() => CurrentConfig(), StateStore.Load());
        pipe = new PipeServer(engine, launcher, sid => CurrentConfig().IsLimited(sid), () => saveRequested = true);
    }

    public void Start()
    {
        ServiceLog.Write("Service starting");
        pipe.Start();
        thread = new Thread(Run) { IsBackground = true, Name = "ScreenTimer supervisor" };
        thread.Start();
    }

    public void Stop()
    {
        cts.Cancel();
        thread?.Join(TimeSpan.FromSeconds(3));
        pipe.Stop();
        Save();
        ServiceLog.Write("Service stopped");
    }

    public void OnSessionChange(SessionChangeReason reason, int sessionId)
    {
        sessions.OnSessionChange(reason, sessionId);
        if (reason == SessionChangeReason.SessionLogoff) launcher.Forget(sessionId);
    }

    AppConfig CurrentConfig()
    {
        lock (configGate) return config;
    }

    void ReloadConfigIfChanged()
    {
        var stamp = File.Exists(ConfigStore.ConfigPath) ? File.GetLastWriteTimeUtc(ConfigStore.ConfigPath) : DateTime.MinValue;
        lock (configGate)
        {
            if (stamp == configStamp) return;
            config = ConfigStore.Load();
            configStamp = stamp;
        }
        ServiceLog.Write("Configuration loaded");
    }

    void Run()
    {
        while (!cts.IsCancellationRequested)
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                ServiceLog.Write($"Tick failed: {ex}");
            }
            cts.Token.WaitHandle.WaitOne(1000);
        }
    }

    void Tick()
    {
        var now = DateTime.UtcNow;
        ReloadConfigIfChanged();
        var cfg = CurrentConfig();

        var limited = sessions.Snapshot().Where(s => cfg.IsLimited(s.Sid)).ToList();
        engine.Prune(cfg.LimitedAccounts.Select(a => a.Sid));

        var observed = new Dictionary<string, ObservedAccount>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in limited)
        {
            var active = s.Active || (observed.TryGetValue(s.Sid, out var prior) && prior.Active);
            observed[s.Sid] = new ObservedAccount(s.Name, active);
        }
        engine.Tick(now, observed);

        foreach (var s in limited)
        {
            if (s.State == Native.WtsState.Active) launcher.Ensure(s, now);
            if (engine.GetPhase(s.Sid) == Phase.Break) AppLauncher.KillTaskManager(s.Id);
        }

        if (saveRequested || now - lastSave >= SaveInterval) Save();
    }

    void Save()
    {
        saveRequested = false;
        lastSave = DateTime.UtcNow;
        try
        {
            StateStore.Save(engine.Snapshot());
        }
        catch (Exception ex)
        {
            ServiceLog.Write($"Saving state failed: {ex.Message}");
        }
    }
}
