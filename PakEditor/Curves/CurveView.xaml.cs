using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
            DefaultTrackerTemplate = (ControlTemplate)Resources["FModelTrackerTemplate"],
            Controller             = BuildSafeController(),
        };
        PlotHost.Content = _plotView;

        DataContextChanged += OnDataContextChanged;
    }

    // ── Controller ───────────────────────────────────────────────────────────

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
                case nameof(CurveViewModel.IsVisible):
                    UpdateControlBar(vm);
                    break;
            }
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
        ControlBar.Visibility    = vm.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        SeriesSection.Visibility = vm.AvailableSeries.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        if (SeriesList.ItemsSource    != vm.AvailableSeries) SeriesList.ItemsSource    = vm.AvailableSeries;
        if (MultiplierList.ItemsSource != vm.Multipliers)    MultiplierList.ItemsSource = vm.Multipliers;
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
        if (e.Key is not (Key.Enter or Key.Return)) return;
        if (sender is TextBox tb)
        {
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Keyboard.ClearFocus();
        }
        e.Handled = true;
    }
}
