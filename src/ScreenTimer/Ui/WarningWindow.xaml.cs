using System;
using System.Media;
using System.Windows;

namespace ScreenTimer.Ui;

public partial class WarningWindow : Window
{
    public WarningWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 16;
            Top = area.Bottom - ActualHeight - 16;
            SystemSounds.Exclamation.Play();
        };
    }

    internal void Update(double remainingSec) =>
        Body.Text = $"Za {TimeFormat.Clock(remainingSec)} ekran zostanie zablokowany na przerwę.";

    void OnDismiss(object sender, RoutedEventArgs e) => Close();
}
