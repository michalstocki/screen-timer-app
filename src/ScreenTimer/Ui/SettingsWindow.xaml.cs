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
            AccountList.IsEnabled = NewCode.IsEnabled = RepeatCode.IsEnabled = SaveButton.IsEnabled = false;
        }

        LoadAccounts();
        RefreshCodeStatus();
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

    void RefreshCodeStatus()
    {
        try
        {
            CodeStatus.Text = ConfigStore.Load().HasCode
                ? "Kod jest ustawiony. Wpisz nowy, aby go zmienić; zostaw puste, aby go nie zmieniać."
                : "Kod nie jest jeszcze ustawiony. Bez niego nie można przedłużyć czasu na ekranie blokady.";
        }
        catch (UnauthorizedAccessException)
        {
            CodeStatus.Text = "Brak dostępu do pliku ustawień.";
        }
    }

    void OnSave(object sender, RoutedEventArgs e)
    {
        var code = NewCode.Password;
        var changeCode = code.Length > 0 || RepeatCode.Password.Length > 0;
        if (changeCode)
        {
            if (code.Length < AppConfig.MinCodeLength)
            {
                Fail($"Kod musi mieć co najmniej {AppConfig.MinCodeLength} znaki.");
                return;
            }
            if (code != RepeatCode.Password)
            {
                Fail("Wpisane kody nie są takie same.");
                return;
            }
        }

        try
        {
            SecureStorage.EnsureDirectory();
            var config = ConfigStore.Load();
            config.LimitedAccounts = items
                .Where(i => i.IsSelected)
                .Select(i => new AccountRef { Sid = i.Sid, Name = i.Name })
                .ToArray();
            if (changeCode) config.SetCode(code);
            ConfigStore.Save(config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail($"Nie udało się zapisać ustawień: {ex.Message}");
            return;
        }

        NewCode.Clear();
        RepeatCode.Clear();
        Message.Foreground = (Brush)FindResource("Accent");
        Message.Text = items.Any(i => i.IsSelected)
            ? "Ustawienia zostały zapisane."
            : "Ustawienia zapisane. Żadne konto nie jest zaznaczone, więc limit czasu jest wyłączony.";
        RefreshCodeStatus();
    }

    void Fail(string text)
    {
        Message.Foreground = (Brush)FindResource("Danger");
        Message.Text = text;
    }

    void OnClose(object sender, RoutedEventArgs e) => Close();
}
