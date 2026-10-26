using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ScreenTimer.Native;

namespace ScreenTimer.Service;

/// <summary>
/// Keeps the tray app alive in every user session. The app proves it is alive by polling the
/// service over the pipe; when it stops doing so the service starts it again.
/// </summary>
internal sealed class AppLauncher
{
    static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(6);
    static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    readonly string exePath;
    readonly ConcurrentDictionary<int, DateTime> lastHeartbeat = new();
    readonly Dictionary<int, DateTime> lastLaunch = [];

    public AppLauncher(string exePath) => this.exePath = exePath;

    public void Heartbeat(int sessionId) => lastHeartbeat[sessionId] = DateTime.UtcNow;

    public void Ensure(SessionInfo session, DateTime nowUtc)
    {
        if (lastHeartbeat.TryGetValue(session.Id, out var seen) && nowUtc - seen < HeartbeatTimeout) return;
        if (lastLaunch.TryGetValue(session.Id, out var launched) && nowUtc - launched < RetryInterval) return;

        lastLaunch[session.Id] = nowUtc;
        Launch(session);
    }

    public void Forget(int sessionId)
    {
        lastHeartbeat.TryRemove(sessionId, out _);
        lastLaunch.Remove(sessionId);
    }

    void Launch(SessionInfo session)
    {
        if (!NativeMethods.WTSQueryUserToken(session.Id, out var token))
        {
            ServiceLog.Write($"Launch for session {session.Id}: no user token ({new Win32Exception().Message})");
            return;
        }

        var env = IntPtr.Zero;
        try
        {
            NativeMethods.CreateEnvironmentBlock(out env, token, false);

            var startup = new NativeMethods.StartupInfo { lpDesktop = @"winsta0\default" };
            startup.cb = System.Runtime.InteropServices.Marshal.SizeOf(startup);

            var command = new StringBuilder($"\"{exePath}\" --tray");
            var dir = System.IO.Path.GetDirectoryName(exePath);

            if (!NativeMethods.CreateProcessAsUser(token, null, command, IntPtr.Zero, IntPtr.Zero, false,
                    NativeMethods.CreateUnicodeEnvironment, env, dir, ref startup, out var info))
            {
                ServiceLog.Write($"Launch for session {session.Id} ({session.Name}) failed: {new Win32Exception().Message}");
                return;
            }

            NativeMethods.CloseHandle(info.hThread);
            NativeMethods.CloseHandle(info.hProcess);
            ServiceLog.Write($"Started tray app for {session.Name} in session {session.Id} (pid {info.dwProcessId})");
        }
        finally
        {
            if (env != IntPtr.Zero) NativeMethods.DestroyEnvironmentBlock(env);
            NativeMethods.CloseHandle(token);
        }
    }

    /// <summary>Task Manager would let a standard user end the app during a break.</summary>
    public static void KillTaskManager(int sessionId)
    {
        foreach (var p in Process.GetProcessesByName("Taskmgr"))
        {
            try
            {
                if (p.SessionId == sessionId) p.Kill();
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                // Already gone or inaccessible.
            }
            finally
            {
                p.Dispose();
            }
        }
    }
}
