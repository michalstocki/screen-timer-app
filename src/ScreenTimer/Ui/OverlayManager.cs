using System;
using System.Collections.Generic;
using System.Windows.Threading;
using Microsoft.Win32;
using ScreenTimer.Core;
using Forms = System.Windows.Forms;

namespace ScreenTimer.Ui;

/// <summary>One cover window per monitor; only the one on the primary monitor takes input.</summary>
internal sealed class OverlayManager : IDisposable
{
    readonly bool dev;
    readonly DispatcherTimer reassert;
    readonly List<OverlayWindow> windows = [];
    KeyboardBlocker? keyboard;
    StatusDto? lastShown;

    public bool IsVisible => windows.Count > 0;

    public event Action<int, string>? ExtendRequested;

    public OverlayManager(bool dev)
    {
        this.dev = dev;
        reassert = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        reassert.Tick += (_, _) => Reassert();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
    }

    public void Show()
    {
        if (IsVisible) return;

        foreach (var screen in Forms.Screen.AllScreens)
        {
            var window = new OverlayWindow(interactive: screen.Primary, dev) { Target = screen.Bounds };
            window.ExtendRequested += (minutes, code) => ExtendRequested?.Invoke(minutes, code);
            windows.Add(window);
            window.Show();
        }

        if (!dev) keyboard = new KeyboardBlocker();
        reassert.Start();
        Reassert();
    }

    public void Hide()
    {
        reassert.Stop();
        keyboard?.Dispose();
        keyboard = null;
        foreach (var window in windows) window.ForceClose();
        windows.Clear();
        lastShown = null;
    }

    public void Update(StatusDto status, double remainingSec)
    {
        lastShown = status;
        foreach (var window in windows)
            window.Update(remainingSec, status.ExtensionMinutes, status.CodeConfigured, status.LockoutSec);
    }

    public void SetBusy(bool busy)
    {
        foreach (var window in windows) window.SetBusy(busy);
    }

    public void ShowMessage(string text)
    {
        foreach (var window in windows) window.ShowMessage(text);
    }

    void Reassert()
    {
        foreach (var window in windows) window.ApplyBounds();
    }

    void OnDisplayChanged(object? sender, EventArgs e)
    {
        if (!IsVisible) return;
        var status = lastShown;
        Hide();
        Show();
        if (status is not null) Update(status, status.RemainingSec);
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        Hide();
    }
}
