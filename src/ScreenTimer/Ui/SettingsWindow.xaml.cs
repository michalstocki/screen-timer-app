using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media;
using ScreenTimer.Core;
using ScreenTimer.Service;

namespace ScreenTimer.Ui;

public partial class SettingsWindow : Window
{
    sealed class AccountItem
    {
        public string Sid { get; init; } = "";
        public string Name { get; init; } = "";
        public string Label { get; init; } = "";
        public bool IsSelected { get; set; }
    }

    readonly List<AccountItem> items = [];

    public SettingsWindow()
    {
        InitializeComponent();

        if (!IsAdministrator())
        {
            Message.Text = "Zmiana ustawień wymaga uprawnień administratora systemu. Uruchom ustawienia ponownie jako administrator.";
            AccountList.IsEnabled = CodeButton.IsEnabled = SaveButton.IsEnabled = false;
            SessionBox.IsEnabled = BreakBox.IsEnabled = WarningBox.IsEnabled = ExtensionsBox.IsEnabled = false;
        }

        LoadAccounts();
        LoadTimings();
        RefreshCodeStatus();
    }

    void LoadTimings()
    {
        var config = ConfigStore.Load();
        SessionBox.Text = TimingInput.FormatMinutes(config.SessionMinutes);
        BreakBox.Text = TimingInput.FormatMinutes(config.BreakMinutes);
        WarningBox.Text = TimingInput.FormatMinutes(config.WarningMinutes);
        ExtensionsBox.Text = TimingInput.FormatExtensions(config.ExtensionMinutes);
    }

    internal static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    void LoadAccounts()
    {
        var selected = ConfigStore.Load().LimitedAccounts;
        var known = AccountDirectory.Enumerate();

        foreach (var account in known)
        {
            var label = string.IsNullOrWhiteSpace(account.DisplayName) || account.DisplayName == account.Name
                ? account.Name
                : $"{account.Name}  ({account.DisplayName})";
            items.Add(new AccountItem
            {
                Sid = account.Sid,
                Name = account.Name,
                Label = label,
                IsSelected = selected.Any(s => s.Sid.Equals(account.Sid, StringComparison.OrdinalIgnoreCase)),
            });
        }

        // Keep accounts that are saved but no longer listed, so saving never drops them silently.
        foreach (var missing in selected.Where(s => !known.Any(k => k.Sid.Equals(s.Sid, StringComparison.OrdinalIgnoreCase))))
        {
            items.Add(new AccountItem
            {
                Sid = missing.Sid,
                Name = missing.Name,
                Label = $"{(string.IsNullOrEmpty(missing.Name) ? missing.Sid : missing.Name)}  (nie znaleziono w systemie)",
                IsSelected = true,
            });
        }

        AccountList.ItemsSource = items;
    }

    bool HasCode()
    {
        try { return ConfigStore.Load().HasCode; }
        catch (UnauthorizedAccessException) { return false; }
    }

    void RefreshCodeStatus()
    {
        var hasCode = HasCode();
        CodeStatus.Text = hasCode
            ? "Kod jest ustawiony."
            : "Kod nie jest jeszcze ustawiony. Bez niego nie można przedłużyć czasu na ekranie blokady.";
        CodeStatus.Foreground = (Brush)FindResource(hasCode ? "TextMuted" : "Danger");
        CodeButton.Content = hasCode ? "Zmień kod…" : "Ustaw kod…";
    }

    void OnChangeCode(object sender, RoutedEventArgs e)
    {
        var dialog = new CodeWindow(HasCode()) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        RefreshCodeStatus();
        Message.Foreground = (Brush)FindResource("Accent");
        Message.Text = "Kod został zapisany.";
    }

    void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            SecureStorage.EnsureDirectory();
            var config = ConfigStore.Load();
            if (!TimingInput.TryApply(config, SessionBox.Text, BreakBox.Text, WarningBox.Text,
                    ExtensionsBox.Text, out var timingError))
            {
                Fail(timingError);
                return;
            }

            config.LimitedAccounts = items
                .Where(i => i.IsSelected)
                .Select(i => new AccountRef { Sid = i.Sid, Name = i.Name })
                .ToArray();
            ConfigStore.Save(config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail($"Nie udało się zapisać ustawień: {ex.Message}");
            return;
        }

        LoadTimings(); // show the values as saved (duplicates dropped, sorted)
        Message.Foreground = (Brush)FindResource("Accent");
        Message.Text = items.Any(i => i.IsSelected)
            ? "Ustawienia zostały zapisane."
            : "Ustawienia zapisane. Żadne konto nie jest zaznaczone, więc limit czasu jest wyłączony.";
    }

    void Fail(string text)
    {
        Message.Foreground = (Brush)FindResource("Danger");
        Message.Text = text;
    }

    void OnClose(object sender, RoutedEventArgs e) => Close();
}
