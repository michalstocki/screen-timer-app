using System;
using System.IO;
using System.Windows;
using ScreenTimer.Core;
using ScreenTimer.Service;

namespace ScreenTimer.Ui;

/// <summary>Pop-up for setting or changing the lock screen code; saves the code as soon as it is accepted.</summary>
public partial class CodeWindow : Window
{
    public CodeWindow(bool hasCode)
    {
        InitializeComponent();
        Hint.Text = hasCode
            ? "Wpisz nowy kod, który zastąpi obecny."
            : "Bez kodu nie można przedłużyć czasu na ekranie blokady.";
        Loaded += (_, _) => NewCode.Focus();
    }

    void OnSave(object sender, RoutedEventArgs e)
    {
        var code = NewCode.Password;
        if (code.Length < AppConfig.MinCodeLength)
        {
            Message.Text = $"Kod musi mieć co najmniej {AppConfig.MinCodeLength} znaki.";
            return;
        }
        if (code != RepeatCode.Password)
        {
            Message.Text = "Wpisane kody nie są takie same.";
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
            Message.Text = $"Nie udało się zapisać kodu: {ex.Message}";
            return;
        }

        DialogResult = true;
    }
}
