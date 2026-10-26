using System.ComponentModel;
using System.Runtime.InteropServices;
using ScreenTimer.Native;

namespace ScreenTimer.Ui;

internal static class ProcessProtection
{
    // SYSTEM and (elevated) Administrators keep full control. Everyone else may only query the
    // process; OWNER RIGHTS limited to READ_CONTROL stops the account that owns the process from
    // rewriting this ACL or terminating it, which is what a standard user would try first.
    const string Sddl = "D:P(A;;0x1FFFFF;;;SY)(A;;0x1FFFFF;;;BA)(A;;0x101400;;;WD)(A;;0x20000;;;OW)";

    /// <summary>Makes the current process impossible to end from a non-elevated account.</summary>
    public static void ProtectCurrent()
    {
        if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptor(Sddl, 1, out var descriptor, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!NativeMethods.SetKernelObjectSecurity(NativeMethods.GetCurrentProcess(),
                    NativeMethods.DaclSecurityInformation, descriptor))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            NativeMethods.LocalFree(descriptor);
        }
    }
}
