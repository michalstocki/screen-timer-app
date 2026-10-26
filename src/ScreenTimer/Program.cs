using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ScreenTimer.Client;
using ScreenTimer.Service;
using ScreenTimer.Ui;

namespace ScreenTimer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var flags = new HashSet<string>(args, StringComparer.OrdinalIgnoreCase);

        if (flags.Contains("--service"))
        {
            ServiceBase.Run(new TimerService());
            return 0;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += OnUnhandled;

        return flags.Contains("--settings") ? RunSettings(app) : RunTray(app, args, flags);
    }

    static int RunSettings(Application app)
    {
        var window = new SettingsWindow();
        window.Closed += (_, _) => app.Shutdown();
        window.Show();
        return app.Run();
    }

    static int RunTray(Application app, string[] args, HashSet<string> flags)
    {
        var dev = flags.Contains("--dev");

        using var singleInstance = new Mutex(true, "Local\\ScreenTimer.Tray", out var first);
        if (!first) return 0;

        if (!dev)
        {
            try { ProcessProtection.ProtectCurrent(); }
            catch (Exception ex) { LogLocal($"Process protection failed: {ex.Message}"); }
        }

        IStateClient client = dev ? new DevStateClient() : new PipeStateClient();
        using var tray = new TrayController(client, dev);
        tray.Start();

        var exitAfter = ArgValue(args, "--exit-after");
        if (dev && exitAfter is > 0)
        {
            var stop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(exitAfter.Value) };
            stop.Tick += (_, _) => app.Shutdown();
            stop.Start();
        }

        return app.Run();
    }

    static int? ArgValue(string[] args, string name)
    {
        var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : null;
    }

    static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogLocal(e.Exception.ToString());
        e.Handled = true; // the lock screen must survive a UI glitch
    }

    static void LogLocal(string message)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenTimer");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "app.log"), $"{DateTime.Now:s} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Nowhere left to report to.
        }
    }
}
