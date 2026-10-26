using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using ScreenTimer.Core;

namespace ScreenTimer.Service;

internal static class SecureStorage
{
    /// <summary>
    /// Creates the data folder readable and writable only by SYSTEM and Administrators, so the
    /// code hash and the break state cannot be read or edited from a standard account.
    /// </summary>
    public static void EnsureDirectory()
    {
        Directory.CreateDirectory(ConfigStore.Directory);

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }
        new DirectoryInfo(ConfigStore.Directory).SetAccessControl(security);
    }
}
