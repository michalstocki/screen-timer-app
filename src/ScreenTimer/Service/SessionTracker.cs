using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using ScreenTimer.Native;

namespace ScreenTimer.Service;

internal sealed record SessionInfo(int Id, string Name, string Sid, WtsState State, bool Locked)
{
    /// <summary>True when somebody is actually at the screen of this session.</summary>
    public bool Active => State == WtsState.Active && !Locked;
}

/// <summary>Knows which Windows sessions exist, who owns them and whether they are locked.</summary>
internal sealed class SessionTracker
{
    readonly object gate = new();
    readonly HashSet<int> locked = [];
    readonly Dictionary<string, string> sidCache = new(StringComparer.OrdinalIgnoreCase);
    bool initialLockStateRead;

    public void OnSessionChange(SessionChangeReason reason, int sessionId)
    {
        lock (gate)
        {
            switch (reason)
            {
                case SessionChangeReason.SessionLock:
                    locked.Add(sessionId);
                    break;
                case SessionChangeReason.SessionUnlock:
                case SessionChangeReason.SessionLogon:
                    locked.Remove(sessionId);
                    break;
                case SessionChangeReason.SessionLogoff:
                    locked.Remove(sessionId);
                    sidCache.Clear();
                    break;
            }
        }
    }

    public List<SessionInfo> Snapshot()
    {
        var result = new List<SessionInfo>();
        if (!NativeMethods.WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var buffer, out var count))
            return result;

        try
        {
            var size = Marshal.SizeOf<NativeMethods.WtsSessionInfo>();
            for (var i = 0; i < count; i++)
            {
                var raw = Marshal.PtrToStructure<NativeMethods.WtsSessionInfo>(buffer + i * size);
                if (raw.SessionId == 0) continue; // services session

                var user = QueryString(raw.SessionId, NativeMethods.WTSUserName);
                if (string.IsNullOrEmpty(user)) continue;
                var domain = QueryString(raw.SessionId, NativeMethods.WTSDomainName);
                var name = string.IsNullOrEmpty(domain) ? user : $"{domain}\\{user}";

                var sid = ResolveSid(raw.SessionId, name);
                if (sid is null) continue;

                bool isLocked;
                lock (gate)
                {
                    if (!initialLockStateRead && ReadLockFlag(raw.SessionId) == true)
                        locked.Add(raw.SessionId);
                    isLocked = locked.Contains(raw.SessionId);
                }
                result.Add(new SessionInfo(raw.SessionId, name, sid, (WtsState)raw.State, isLocked));
            }
        }
        finally
        {
            NativeMethods.WTSFreeMemory(buffer);
        }

        lock (gate) initialLockStateRead = true;
        return result;
    }

    string? ResolveSid(int sessionId, string name)
    {
        var key = $"{sessionId}|{name}";
        lock (gate)
            if (sidCache.TryGetValue(key, out var cached)) return cached;

        if (!NativeMethods.WTSQueryUserToken(sessionId, out var token)) return null;
        try
        {
            using var identity = new WindowsIdentity(token);
            var sid = identity.User?.Value;
            if (sid is not null)
                lock (gate) sidCache[key] = sid;
            return sid;
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    static string QueryString(int sessionId, int infoClass)
    {
        if (!NativeMethods.WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out _))
            return "";
        try { return Marshal.PtrToStringUni(buffer) ?? ""; }
        finally { NativeMethods.WTSFreeMemory(buffer); }
    }

    /// <summary>
    /// WTSINFOEX level 1 on a 64-bit OS: the data union is 8-byte aligned, so SessionId is at offset 8,
    /// SessionState at 12 and SessionFlags at 16 (0 = locked, 1 = unlocked).
    /// </summary>
    static bool? ReadLockFlag(int sessionId)
    {
        if (!NativeMethods.WTSQuerySessionInformation(IntPtr.Zero, sessionId, NativeMethods.WTSSessionInfoEx, out var buffer, out var bytes))
            return null;
        try
        {
            if (bytes < 20 || Marshal.ReadInt32(buffer, 0) != 1) return null;
            return Marshal.ReadInt32(buffer, 16) switch { 0 => true, 1 => false, _ => null };
        }
        finally { NativeMethods.WTSFreeMemory(buffer); }
    }
}
