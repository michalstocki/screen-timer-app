using System;
using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using System.Security.Principal;
using Microsoft.Win32;

namespace ScreenTimer.Ui;

internal sealed record AccountEntry(string Sid, string Name, string DisplayName);

/// <summary>Lists the accounts an administrator can choose from.</summary>
internal static class AccountDirectory
{
    public static List<AccountEntry> Enumerate()
    {
        var found = new Dictionary<string, AccountEntry>(StringComparer.OrdinalIgnoreCase);
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var searcher = new PrincipalSearcher(new UserPrincipal(context));
            foreach (var user in searcher.FindAll().OfType<UserPrincipal>())
            {
                using (user)
                {
                    var sid = user.Sid?.Value;
                    if (sid is null) continue;
                    skip.Add(sid);
                    if (user.Enabled == false) continue; // built-in Administrator, Guest and similar
                    found[sid] = new AccountEntry(sid, $"{Environment.MachineName}\\{user.SamAccountName}", user.DisplayName ?? "");
                }
            }
        }
        catch (Exception ex) when (ex is PrincipalException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Fall through to the profile list below.
        }

        // Domain and Azure AD accounts do not appear locally until they have signed in once.
        using var profiles = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        foreach (var sid in profiles?.GetSubKeyNames() ?? [])
        {
            if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) || skip.Contains(sid) || found.ContainsKey(sid)) continue;
            try
            {
                var name = new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
                found[sid] = new AccountEntry(sid, name, "");
            }
            catch (IdentityNotMappedException)
            {
                // Profile of an account that no longer exists.
            }
        }

        return found.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
