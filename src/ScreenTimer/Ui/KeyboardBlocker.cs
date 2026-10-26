using System;
using System.Runtime.InteropServices;
using ScreenTimer.Native;

namespace ScreenTimer.Ui;

/// <summary>
/// Swallows the shortcuts that would leave the lock screen: Win key, Alt+Tab, Alt+Esc, Ctrl+Esc,
/// Ctrl+Shift+Esc and Alt+F4. Ctrl+Alt+Del is handled by Windows and cannot be intercepted.
/// </summary>
internal sealed class KeyboardBlocker : IDisposable
{
    const int WhKeyboardLl = 13;
    const int LlkhfAltDown = 0x20;
    const int VkControl = 0x11;

    readonly NativeMethods.LowLevelKeyboardProc proc; // kept as a field so the GC cannot collect the delegate
    IntPtr hook;

    public KeyboardBlocker()
    {
        proc = Callback;
        hook = NativeMethods.SetWindowsHookEx(WhKeyboardLl, proc, NativeMethods.GetModuleHandle(null), 0);
    }

    IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && ShouldBlock(Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam)))
            return (IntPtr)1;
        return NativeMethods.CallNextHookEx(hook, code, wParam, lParam);
    }

    static bool ShouldBlock(NativeMethods.KbdLlHookStruct key)
    {
        var alt = (key.flags & LlkhfAltDown) != 0;
        var ctrl = (NativeMethods.GetAsyncKeyState(VkControl) & 0x8000) != 0;
        return key.vkCode switch
        {
            0x5B or 0x5C => true,       // left / right Windows key
            0x09 => alt,                // Alt+Tab
            0x1B => alt || ctrl,        // Alt+Esc, Ctrl+Esc, Ctrl+Shift+Esc
            0x73 => alt,                // Alt+F4
            _ => false,
        };
    }

    public void Dispose()
    {
        if (hook == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }
}
