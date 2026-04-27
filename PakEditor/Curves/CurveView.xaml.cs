using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using OxyPlot;
using OxyPlot.Wpf;

namespace PakEditor.Curves;

public partial class CurveView : UserControl
{
    private PlotView?       _plotView;
    private CurveViewModel? _vm;

    public CurveView()
    {
        InitializeComponent();

        _plotView = new PlotView
        {
            Background             = Brushes.Transparent,
            MinHeight              = 80,
            DefaultTrackerTemplate = BuildDarkTrackerTemplate(),
            Controller             = BuildSafeController(),
        };
        PlotHost.Content = _plotView;

        DataContextChanged += OnDataContextChanged;
    }

    // ── Tracker + controller ─────────────────────────────────────────────────

    private static ControlTemplate BuildDarkTrackerTemplate()
    {
        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetBinding(TextBlock.TextProperty, new Binding("Text"));
        textFactory.SetValue(TextBlock.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)));
        textFactory.SetValue(TextBlock.FontSizeProperty, 11.0);
        textFactory.SetValue(TextBlock.LineHeightProperty, 16.0);
        textFactory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);

        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(0xEE, 0x1E, 0x1E, 0x2E)));
        borderFactory.SetValue(Border.BorderBrushProperty,
            new SolidColorBrush(Color.FromArgb(0xFF, 0x4A, 0x90, 0xD9)));
        borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        borderFactory.SetValue(Border.PaddingProperty, new Thickness(8, 4, 8, 4));
        borderFactory.AppendChild(textFactory);

        var template = new ControlTemplate(typeof(TrackerControl));
        template.VisualTree = borderFactory;
        template.Seal();
        return template;
    }

    private static PlotController BuildSafeController()
    {
        var ctrl = new PlotController();
        ctrl.UnbindMouseDown(OxyMouseButton.Right); // prevents crash on right-click
        return ctrl;
    }

    // ── DataContext wiring ───────────────────────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CurveViewModel oldVm)
            oldVm.PropertyChanged -= OnVmPropertyChanged;

        _vm = e.NewValue as CurveViewModel;

        if (_vm != null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            ApplyViewModel(_vm);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CurveViewModel vm) return;
        Dispatcher.InvokeAsync(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(CurveViewModel.PlotModel):
                    if (_plotView != null) _plotView.Model = vm.PlotModel;
                    break;
                case nameof(CurveViewModel.Headline):
                    HeadlineText.Text = vm.Headline;
                    break;
            }
            UpdateControlBar(vm);
        });
    }

    private void ApplyViewModel(CurveViewModel vm)
    {
        HeadlineText.Text = vm.Headline;
        if (_plotView != null) _plotView.Model = vm.PlotModel;
        UpdateControlBar(vm);
    }

    private void UpdateControlBar(CurveViewModel vm)
    {
        // Show control bar whenever a curve is loaded (always has ≥1 multiplier).
        ControlBar.Visibility = vm.IsVisible ? Visibility.Visible : Visibility.Collapsed;

        // Series section: only useful when there are multiple series to toggle.
        SeriesSection.Visibility = vm.AvailableSeries.Count > 1
            ? Visibility.Visible : Visibility.Collapsed;

        if (SeriesList.ItemsSource   != vm.AvailableSeries)  SeriesList.ItemsSource   = vm.AvailableSeries;
        if (MultiplierList.ItemsSource != vm.Multipliers)     MultiplierList.ItemsSource = vm.Multipliers;
    }

    // ── Multiplier button handlers ───────────────────────────────────────────

    private void OnAddMultiplierClick(object sender, RoutedEventArgs e)
        => _vm?.AddMultiplier(1.0);

    private void OnRemoveMultiplierClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MultiplierEntry entry })
            _vm?.RemoveMultiplier(entry);
    }

    private void OnMultiplierEntryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            // Force the binding to commit then move focus away.
            if (sender is TextBox tb)
            {
                var binding = tb.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();
                Keyboard.ClearFocus();
            }
            e.Handled = true;
        }
    }
}
