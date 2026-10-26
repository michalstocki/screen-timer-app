using System;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media;
using ScreenTimer.Core;
using ScreenTimer.Service;

namespace ScreenTimer.Ui;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        if (!IsAdministrator())
        {
            Message.Text = "Zmiana kodu wymaga uprawnień administratora systemu. Uruchom ustawienia ponownie jako administrator.";
            NewCode.IsEnabled = RepeatCode.IsEnabled = SaveButton.IsEnabled = false;
        }
        RefreshStatus();
    }

    static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    void RefreshStatus()
    {
        try
        {
            CodeStatus.Text = ConfigStore.Load().HasCode
                ? "Kod jest ustawiony. Wpisz nowy, aby go zmienić."
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

        try
        {
            SecureStorage.EnsureDirectory();
            var config = ConfigStore.Load();
            config.SetCode(code);
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
        Message.Text = "Kod został zapisany.";
        RefreshStatus();
    }

    void Fail(string text)
    {
        Message.Foreground = (Brush)FindResource("Danger");
        Message.Text = text;
    }

    void OnClose(object sender, RoutedEventArgs e) => Close();
}
