using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using ScreenTimer.Client;
using ScreenTimer.Core;
using Forms = System.Windows.Forms;

namespace ScreenTimer.Ui;

/// <summary>
/// The per-user face of the app: polls the service once a second, shows the remaining time in the
/// tray icon, raises the two-minute warning and covers the screen during a break.
/// </summary>
internal sealed class TrayController : IDisposable
{
    readonly IStateClient client;
    readonly Forms.NotifyIcon tray;
    readonly Forms.ToolStripMenuItem statusItem;
    readonly DispatcherTimer timer;
    readonly OverlayManager overlays;

    WarningWindow? warning;
    bool warned;
    bool polling;
    StatusDto? last;
    DateTime lastReplyUtc = DateTime.MinValue;
    string shownIconKey = "";

    public TrayController(IStateClient client, bool dev)
    {
        this.client = client;

        statusItem = new Forms.ToolStripMenuItem("Łączenie z usługą…") { Enabled = false };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Ustawienia (administrator)…", null, (_, _) => OpenSettings());
        menu.Items.Add("O programie", null, (_, _) => Forms.MessageBox.Show(
            "Limit czasu przy komputerze\n\nSesja trwa 30 minut, potem następuje 30-minutowa przerwa.\n" +
            "Ten program nie może zostać zamknięty z poziomu konta użytkownika.",
            "O programie", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Information));

        tray = new Forms.NotifyIcon { ContextMenuStrip = menu, Visible = true, Text = "Limit czasu przy komputerze" };
        SetIcon("…", TrayIconRenderer.Idle);

        overlays = new OverlayManager(dev);
        overlays.ExtendRequested += OnExtendRequested;

        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) => await Poll();
    }

    public void Start()
    {
        timer.Start();
        _ = Poll();
    }

    async System.Threading.Tasks.Task Poll()
    {
        if (polling) return;
        polling = true;
        try
        {
            var status = await client.GetStatusAsync();
            if (status is null) OnDisconnected();
            else Apply(status);
        }
        finally
        {
            polling = false;
        }
    }

    void Apply(StatusDto status)
    {
        last = status;
        lastReplyUtc = DateTime.UtcNow;

        if (!status.Known)
        {
            overlays.Hide();
            CloseWarning();
            warned = false;
            SetIcon("–", TrayIconRenderer.Idle);
            SetTooltip("Brak limitu czasu dla tego konta");
            return;
        }

        if (status.Phase == Phase.Break)
        {
            CloseWarning();
            warned = false;
            overlays.Show();
            overlays.Update(status, status.RemainingSec);
            SetIcon(IconText(status.RemainingSec), TrayIconRenderer.Break);
            SetTooltip($"Przerwa: {TimeFormat.Clock(status.RemainingSec)} do końca");
            return;
        }

        overlays.Hide();
        var color = status.RemainingSec <= status.WarningSec ? TrayIconRenderer.Urgent
            : status.RemainingSec <= 5 * 60 ? TrayIconRenderer.Soon
            : TrayIconRenderer.Normal;
        SetIcon(IconText(status.RemainingSec), color);
        SetTooltip($"Pozostało: {TimeFormat.Clock(status.RemainingSec)}");
        HandleWarning(status);
    }

    void HandleWarning(StatusDto status)
    {
        if (status.WarningSec <= 0 || status.RemainingSec > status.WarningSec)
        {
            warned = false;
            return;
        }

        if (!warned)
        {
            warned = true;
            warning = new WarningWindow();
            warning.Closed += (_, _) => warning = null;
            warning.Show();
        }
        warning?.Update(status.RemainingSec);
    }

    void CloseWarning()
    {
        warning?.Close();
        warning = null;
    }

    /// <summary>The service is unreachable: keep an active break covered using the last known end time.</summary>
    void OnDisconnected()
    {
        if (last is { Phase: Phase.Break, BreakEndsUtc: { } end } && overlays.IsVisible)
        {
            var remaining = (end - DateTime.UtcNow).TotalSeconds;
            if (remaining <= 0) overlays.Hide();
            else overlays.Update(last, remaining);
        }

        if (DateTime.UtcNow - lastReplyUtc > TimeSpan.FromSeconds(5))
        {
            SetIcon("!", TrayIconRenderer.Idle);
            SetTooltip("Brak połączenia z usługą limitu czasu");
        }
    }

    async void OnExtendRequested(int minutes, string code)
    {
        overlays.SetBusy(true);
        var outcome = await client.ExtendAsync(minutes, code);
        overlays.SetBusy(false);

        switch (outcome?.Result)
        {
            case ExtendResult.Ok:
            case ExtendResult.NotInBreak:
                overlays.Hide();
                await Poll();
                break;
            case ExtendResult.WrongCode:
                overlays.ShowMessage("Nieprawidłowy kod.");
                break;
            case ExtendResult.LockedOut:
                overlays.ShowMessage($"Zbyt wiele błędnych prób. Spróbuj ponownie za {Math.Ceiling(outcome.LockoutSec)} s.");
                break;
            case ExtendResult.NoCodeSet:
                overlays.ShowMessage("Administrator nie ustawił jeszcze kodu blokady.");
                break;
            case null:
                overlays.ShowMessage("Brak połączenia z usługą. Spróbuj ponownie.");
                break;
            default:
                overlays.ShowMessage("Nie można przedłużyć czasu.");
                break;
        }
    }

    static string IconText(double remainingSec) =>
        remainingSec >= 60 ? ((int)Math.Ceiling(remainingSec / 60)).ToString() : ((int)Math.Ceiling(remainingSec)).ToString();

    void SetIcon(string text, Color color)
    {
        var key = text + color.ToArgb();
        if (key == shownIconKey) return;
        shownIconKey = key;

        var old = tray.Icon;
        tray.Icon = TrayIconRenderer.Render(text, color);
        old?.Dispose();
    }

    void SetTooltip(string text)
    {
        tray.Text = text.Length > 63 ? text[..63] : text;
        statusItem.Text = text;
    }

    static void OpenSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--settings")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Win32Exception)
        {
            // The user cancelled the elevation prompt.
        }
    }

    public void Dispose()
    {
        timer.Stop();
        overlays.Dispose();
        CloseWarning();
        tray.Visible = false;
        tray.Icon?.Dispose();
        tray.Dispose();
    }
}
