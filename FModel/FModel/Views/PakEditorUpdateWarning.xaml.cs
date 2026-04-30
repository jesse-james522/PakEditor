using System;
using System.Windows;
using FModel.Settings;

namespace FModel.Views;

public partial class PakEditorUpdateWarning
{
    public bool ShouldUpdate { get; private set; }

    public PakEditorUpdateWarning()
    {
        InitializeComponent();
    }

    private void OnYes(object sender, RoutedEventArgs e)
    {
        ApplyDoNotAsk();
        ShouldUpdate = true;
        Close();
    }

    private void OnNo(object sender, RoutedEventArgs e)
    {
        ApplyDoNotAsk();
        ShouldUpdate = false;
        Close();
    }

    private void ApplyDoNotAsk()
    {
        if (DoNotAskCheckBox.IsChecked == true)
            UserSettings.Default.NextUpdateCheck = DateTime.MaxValue;
    }
}
