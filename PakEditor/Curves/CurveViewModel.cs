using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Engine.Curves;
using Newtonsoft.Json;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace PakEditor.Curves;

// ─────────────────────────────────────────────────────────────────────────────
// MultiplierEntry  –  one row in the × list
// ─────────────────────────────────────────────────────────────────────────────
public class MultiplierEntry : ViewModelBase
{
    private double _value = 1.0;
    public double Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                _displayText = value.ToString("G", CultureInfo.InvariantCulture);
                RaisePropertyChanged(nameof(DisplayText));
                OnChanged?.Invoke();
            }
        }
    }

    // String representation kept separately so the TextBox can edit freely.
    private string _displayText = "1";
    public string DisplayText
    {
        get => _displayText;
        set
        {
            if (!SetProperty(ref _displayText, value)) return;
            if (double.TryParse(value, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var d) && d != 0)
            {
                _value = d;
                RaisePropertyChanged(nameof(Value));
                OnChanged?.Invoke();
            }
        }
    }

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { if (SetProperty(ref _isEnabled, value)) OnChanged?.Invoke(); }
    }

    internal Action? OnChanged { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// PlotSeriesInfo  –  one toggle-able series checkbox
// ─────────────────────────────────────────────────────────────────────────────
public class PlotSeriesInfo : ViewModelBase
{
    public string    Label { get; init; } = string.Empty;
    public OxyColor  Color { get; init; }

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { if (SetProperty(ref _isEnabled, value)) OnToggled?.Invoke(); }
    }

    internal Action? OnToggled { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// CurveViewModel
// ─────────────────────────────────────────────────────────────────────────────
public class CurveViewModel : ViewModelBase
{
    // ── Line styles cycled per multiplier ─────────────────────────────────────
    private static readonly LineStyle[] MultiplierStyles =
    [
        LineStyle.Solid, LineStyle.Dash, LineStyle.DashDot,
        LineStyle.Dot,   LineStyle.LongDash, LineStyle.DashDotDot,
    ];

    // ── Persistence ───────────────────────────────────────────────────────────
    private static readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PakEditor", "CurveSeries.json");

    private sealed class SavedData
    {
        public Dictionary<string, HashSet<string>> Enabled     { get; set; } = new();
        public Dictionary<string, List<double>>    Multipliers { get; set; } = new();
    }

    private SavedData _saved = new();

    // ── Bound properties ──────────────────────────────────────────────────────
    private PlotModel? _plotModel;
    public PlotModel? PlotModel
    {
        get => _plotModel;
        private set => SetProperty(ref _plotModel, value);
    }

    private bool _isVisible;
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    private string _headline = string.Empty;
    public string Headline
    {
        get => _headline;
        private set => SetProperty(ref _headline, value);
    }

    public ObservableCollection<PlotSeriesInfo>  AvailableSeries  { get; } = new();
    public ObservableCollection<MultiplierEntry> Multipliers       { get; } = new();

    // ── Raw data ──────────────────────────────────────────────────────────────
    private record RawSeries(string Label, OxyColor Color, FRichCurveKey[] Keys);
    private List<RawSeries> _rawSeries        = new();
    private string          _currentAssetName = string.Empty;

    // ── Rebuild debounce (50 ms) ──────────────────────────────────────────────
    private DispatcherTimer? _rebuildTimer;

    // ── Construction ──────────────────────────────────────────────────────────
    public CurveViewModel()
    {
        LoadSettings();
    }

    // ── Public API ────────────────────────────────────────────────────────────
    public void Clear()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsVisible = false;
            PlotModel = null;
            Headline  = string.Empty;
            AvailableSeries.Clear();
            Multipliers.Clear();
            _rawSeries.Clear();
            _currentAssetName = string.Empty;
        });
    }

    public void LoadColorCurve(UCurveLinearColor curve, string assetName)
    {
        var fc = curve.FloatCurves;
        var channels = new (string Label, OxyColor Color, FRichCurve? Curve)[]
        {
            ("R", OxyColor.FromRgb(220, 60,  60),  fc.Length > 0 ? fc[0] : null),
            ("G", OxyColor.FromRgb(60,  200, 60),  fc.Length > 1 ? fc[1] : null),
            ("B", OxyColor.FromRgb(60,  120, 220), fc.Length > 2 ? fc[2] : null),
            ("A", OxyColor.FromRgb(160, 160, 160), fc.Length > 3 ? fc[3] : null),
        };
        var raw = new List<RawSeries>();
        foreach (var (label, color, richCurve) in channels)
            if (richCurve?.Keys is { Length: > 0 } keys)
                raw.Add(new RawSeries(label, color, keys));
        CommitSeries(assetName, raw);
    }

    public void LoadFloatCurve(UCurveFloat curve, string assetName)
    {
        var fallback = curve.GetOrDefault<FStructFallback>("FloatCurve");
        if (fallback == null) return;
        var richCurve = new FRichCurve(fallback);
        if (richCurve.Keys is not { Length: > 0 } keys) return;
        CommitSeries(assetName,
        [
            new RawSeries("Value", OxyColor.FromRgb(100, 180, 255), keys)
        ]);
    }

    public void LoadDataTableCurves(UDataTable dataTable, string assetName)
    {
        var palette = new[]
        {
            OxyColor.FromRgb(100, 180, 255), OxyColor.FromRgb(60,  200, 60),
            OxyColor.FromRgb(220, 60,  60),  OxyColor.FromRgb(220, 160, 60),
            OxyColor.FromRgb(160, 60,  220), OxyColor.FromRgb(60,  200, 200),
            OxyColor.FromRgb(220, 100, 180), OxyColor.FromRgb(180, 220, 60),
        };
        var raw = new List<RawSeries>();
        int ci = 0;
        foreach (var (rowName, rowData) in dataTable.RowMap)
        {
            foreach (var prop in rowData.Properties)
            {
                if (prop.Tag?.GenericValue is FStructFallback curveFallback)
                {
                    var keys = curveFallback.GetOrDefault<FRichCurveKey[]>("Keys");
                    if (keys is { Length: > 0 })
                        raw.Add(new RawSeries(
                            $"{rowName}.{prop.Name}",
                            palette[ci++ % palette.Length],
                            keys));
                }
            }
        }
        if (raw.Count > 0)
            CommitSeries(assetName, raw);
    }

    /// <summary>Add a new multiplier row. Called by the + button.</summary>
    public void AddMultiplier(double value = 1.0)
    {
        var entry = MakeEntry(value);
        Multipliers.Add(entry);
        Persist();
        ScheduleRebuild();
    }

    /// <summary>Remove a multiplier row. Called by the ✕ button.</summary>
    public void RemoveMultiplier(MultiplierEntry entry)
    {
        if (Multipliers.Count <= 1) return; // always keep at least one
        Multipliers.Remove(entry);
        Persist();
        ScheduleRebuild();
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Debounced rebuild: coalesces rapid toggles/edits into a single redraw
    /// after 50 ms of idle, preventing janky multi-rebuild during quick clicks.
    /// Must be called on the UI thread.
    /// </summary>
    private void ScheduleRebuild()
    {
        if (_rebuildTimer == null)
        {
            _rebuildTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(50),
            };
            _rebuildTimer.Tick += (_, _) => { _rebuildTimer.Stop(); RebuildPlot(); };
        }
        // Reset the countdown on every call so only the last change triggers a rebuild.
        _rebuildTimer.Stop();
        _rebuildTimer.Start();
    }

    private MultiplierEntry MakeEntry(double value)
    {
        var e = new MultiplierEntry { Value = value };
        e.OnChanged = () => { Persist(); ScheduleRebuild(); };
        return e;
    }

    private void CommitSeries(string assetName, List<RawSeries> raw)
    {
        if (raw.Count == 0) return;

        _currentAssetName = assetName;
        _rawSeries        = raw;

        _saved.Enabled.TryGetValue(assetName, out var enabledSet);
        var savedMults = _saved.Multipliers.TryGetValue(assetName, out var sm) && sm.Count > 0
            ? sm
            : new List<double> { 1.0 };

        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // ── Series ──
            AvailableSeries.Clear();
            foreach (var s in raw)
            {
                var info = new PlotSeriesInfo
                {
                    Label     = s.Label,
                    Color     = s.Color,
                    IsEnabled = enabledSet == null || enabledSet.Contains(s.Label),
                };
                info.OnToggled = () => { Persist(); ScheduleRebuild(); };
                AvailableSeries.Add(info);
            }

            // ── Multipliers ──
            Multipliers.Clear();
            foreach (var v in savedMults)
                Multipliers.Add(MakeEntry(v));

            RebuildPlot();
        });
    }

    private void RebuildPlot()
    {
        var enabledSeries = AvailableSeries.Where(s => s.IsEnabled).ToList();
        var enabledMults  = Multipliers.Where(m => m.IsEnabled).ToList();

        bool multiMult = enabledMults.Count > 1;

        var model = new PlotModel
        {
            Background          = OxyColor.FromRgb(30, 30, 30),
            PlotAreaBorderColor = OxyColor.FromRgb(60, 60, 60),
            TextColor           = OxyColor.FromRgb(200, 200, 200),
            TitleFontSize       = 12,
            TitleColor          = OxyColor.FromRgb(160, 160, 180),
            Title               = _currentAssetName,
        };

        model.Axes.Add(new LinearAxis
        {
            Position           = AxisPosition.Bottom,
            Title              = "Time",
            TitleColor         = OxyColor.FromRgb(160, 160, 160),
            AxislineColor      = OxyColor.FromRgb(80,  80,  80),
            TicklineColor      = OxyColor.FromRgb(80,  80,  80),
            TextColor          = OxyColor.FromRgb(180, 180, 180),
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(50, 50, 50),
        });

        model.Axes.Add(new LinearAxis
        {
            Position           = AxisPosition.Left,
            Title              = "Value",
            TitleColor         = OxyColor.FromRgb(160, 160, 160),
            AxislineColor      = OxyColor.FromRgb(80,  80,  80),
            TicklineColor      = OxyColor.FromRgb(80,  80,  80),
            TextColor          = OxyColor.FromRgb(180, 180, 180),
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(50, 50, 50),
        });

        for (int mi = 0; mi < enabledMults.Count; mi++)
        {
            var mult      = enabledMults[mi];
            var lineStyle = MultiplierStyles[mi % MultiplierStyles.Length];
            var m         = mult.Value;

            foreach (var si in enabledSeries)
            {
                var rawEntry = _rawSeries.FirstOrDefault(r => r.Label == si.Label);
                if (rawEntry == null) continue;

                var (times, values) = CurveProcessor.ProcessCurve(rawEntry.Keys);
                if (times.Length == 0) continue;

                string title = multiMult
                    ? $"{si.Label}  ×{mult.DisplayText}"
                    : si.Label;

                var line = new LineSeries
                {
                    Title           = title,
                    Color           = si.Color,
                    LineStyle       = lineStyle,
                    StrokeThickness = lineStyle == LineStyle.Solid ? 2.0 : 1.5,
                    MarkerType      = MarkerType.None,
                };
                for (int i = 0; i < times.Length; i++)
                    line.Points.Add(new DataPoint(times[i], values[i] * m));
                model.Series.Add(line);

                var scatter = new ScatterSeries
                {
                    Title                 = title,
                    TrackerFormatString   = $"{title}\nt={{2:F3}}  v={{4:F4}}",
                    MarkerType            = MarkerType.Circle,
                    MarkerSize            = lineStyle == LineStyle.Solid ? 4 : 3,
                    MarkerFill            = si.Color,
                    MarkerStroke          = OxyColor.FromRgb(20, 20, 20),
                    MarkerStrokeThickness = 1,
                };
                foreach (var k in rawEntry.Keys)
                    scatter.Points.Add(new ScatterPoint(k.Time, k.Value * m));
                model.Series.Add(scatter);
            }
        }

        Headline  = _currentAssetName;
        PlotModel = model;
        IsVisible = true;
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
                _saved = JsonConvert.DeserializeObject<SavedData>(
                    File.ReadAllText(_settingsPath)) ?? new();
        }
        catch { _saved = new(); }
    }

    internal void Persist()
    {
        if (string.IsNullOrEmpty(_currentAssetName)) return;

        _saved.Enabled[_currentAssetName] = AvailableSeries
            .Where(s => s.IsEnabled).Select(s => s.Label).ToHashSet();

        _saved.Multipliers[_currentAssetName] = Multipliers
            .Select(m => m.Value).ToList();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath,
                JsonConvert.SerializeObject(_saved, Formatting.Indented));
        }
        catch { /* non-fatal */ }
    }
}
