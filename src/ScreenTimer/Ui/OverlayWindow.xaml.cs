using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ScreenTimer.Native;

namespace ScreenTimer.Ui;

public partial class OverlayWindow : Window
{
    readonly bool interactive;
    readonly bool dev;
    int[] builtOptions = [];
    bool allowClose;
    bool lockoutShown;
    bool busy;
    bool canExtend;

    internal Rectangle Target { get; set; }

    /// <summary>Raised with the chosen extension length and the typed code.</summary>
    internal event Action<int, string>? ExtendRequested;

    internal OverlayWindow(bool interactive, bool dev)
    {
        this.interactive = interactive;
        this.dev = dev;
        InitializeComponent();

        if (!interactive)
        {
            ExtendPanel.Visibility = Visibility.Collapsed;
            SwitchButton.Visibility = Visibility.Collapsed;
        }

        SourceInitialized += (_, _) => ApplyBounds();
        Loaded += (_, _) => FocusCode();
        Activated += (_, _) => FocusCode();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    internal void ForceClose()
    {
        allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!allowClose) e.Cancel = true;
        base.OnClosing(e);
    }

    /// <summary>Re-asserts position and z-order; other windows may have crept above us.</summary>
    internal void ApplyBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || Target.IsEmpty) return;

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopmost, Target.X, Target.Y, Target.Width, Target.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        if (interactive && !IsActive) Activate();
    }

    internal void Update(double remainingSec, int[] options, bool codeConfigured, double lockoutSec)
    {
        Countdown.Text = TimeFormat.Clock(remainingSec);
        if (!interactive) return;

        canExtend = codeConfigured;
        if (!options.SequenceEqual(builtOptions)) BuildOptions(options);

        if (!codeConfigured)
        {
            Message.Text = "Administrator nie ustawił jeszcze kodu blokady.";
            lockoutShown = false;
        }
        else if (lockoutSec > 0)
        {
            Message.Text = $"Zbyt wiele błędnych prób. Spróbuj ponownie za {Math.Ceiling(lockoutSec)} s.";
            lockoutShown = true;
        }
        else if (lockoutShown)
        {
            Message.Text = "";
            lockoutShown = false;
        }

        SetOptionsEnabled(canExtend && lockoutSec <= 0 && !busy);
    }

    internal void SetBusy(bool value)
    {
        busy = value;
        SetOptionsEnabled(canExtend && !value);
    }

    internal void ShowMessage(string text)
    {
        lockoutShown = false;
        Message.Text = text;
        CodeBox.Clear();
        CodeBox.Focus();
    }

    void BuildOptions(int[] options)
    {
        builtOptions = options;
        OptionsPanel.Children.Clear();
        foreach (var minutes in options)
        {
            var button = new Button
            {
                Content = $"+{minutes} min",
                Style = (Style)FindResource("PrimaryButton"),
                Tag = minutes,
            };
            button.Click += (_, _) => ExtendRequested?.Invoke(minutes, CodeBox.Password);
            OptionsPanel.Children.Add(button);
        }
    }

    void SetOptionsEnabled(bool enabled)
    {
        foreach (var child in OptionsPanel.Children.OfType<Button>()) child.IsEnabled = enabled;
    }

    void FocusCode()
    {
        if (interactive && CodeBox.IsEnabled) CodeBox.Focus();
    }

    void OnSwitchAccount(object sender, RoutedEventArgs e) => NativeMethods.LockWorkStation();

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Only the --dev demo can be dismissed from the keyboard.
        if (dev && e.Key == Key.Escape) Application.Current.Shutdown();
    }
}
