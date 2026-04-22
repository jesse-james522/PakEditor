using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CUE4Parse.UE4.Versions;
using Microsoft.Win32;
using Viewer;

namespace UEpaker;

public partial class MainWindow : Window
{
    private readonly ProviderSettings _settings;
    private GameProvider? _provider;
    private IReadOnlyList<AssetTreeNode>? _allRoots;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _previewCts;

    public MainWindow()
    {
        InitializeComponent();
        _settings = ProviderSettings.Load();
        PopulateVersionCombo();
        RestoreSettingsToUi();
    }

    // ── Settings UI ──────────────────────────────────────────────────────────

    private void PopulateVersionCombo()
    {
        var versions = new[]
        {
            EGame.GAME_UE5_6,
            EGame.GAME_UE5_5,
            EGame.GAME_UE5_4,
            EGame.GAME_UE5_3,
            EGame.GAME_UE5_2,
            EGame.GAME_UE5_1,
            EGame.GAME_UE5_0,
        };
        CmbVersion.ItemsSource = versions;
        CmbVersion.DisplayMemberPath = null;
        CmbVersion.SelectedItem = _settings.UeVersion;
        if (CmbVersion.SelectedItem is null) CmbVersion.SelectedIndex = 0;
    }

    private void RestoreSettingsToUi()
    {
        TxtPakDir.Text = _settings.PakDirectory;
        TxtAesKey.Text = _settings.AesKey;
        TxtUsmap.Text = _settings.MappingsPath;
        CmbVersion.SelectedItem = _settings.UeVersion;
    }

    private void SaveSettingsFromUi()
    {
        _settings.PakDirectory = TxtPakDir.Text.Trim();
        _settings.AesKey = TxtAesKey.Text.Trim();
        _settings.MappingsPath = TxtUsmap.Text.Trim();
        _settings.UeVersion = CmbVersion.SelectedItem is EGame v ? v : EGame.GAME_UE5_6;
        _settings.Save();
    }

    private void BtnBrowsePak_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select pak directory" };
        if (dlg.ShowDialog() == true)
            TxtPakDir.Text = dlg.FolderName;
    }

    private void BtnBrowseUsmap_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select mappings file",
            Filter = "Usmap files (*.usmap)|*.usmap|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            TxtUsmap.Text = dlg.FileName;
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    private async void BtnLoad_Click(object sender, RoutedEventArgs e)
    {
        var pakDir = TxtPakDir.Text.Trim();
        if (!Directory.Exists(pakDir))
        {
            MessageBox.Show("Pak directory does not exist.", "UEpaker",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SaveSettingsFromUi();

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        BtnLoad.IsEnabled = false;
        AssetTree.ItemsSource = null;
        _allRoots = null;
        ClearPreview();
        SetStatus("Loading…", 0);
        PrgLoad.Visibility = Visibility.Visible;

        _provider?.Dispose();
        _provider = new GameProvider();

        var progress = new Progress<(int Percent, string Message)>(p =>
        {
            SetStatus(p.Message, p.Percent);
        });

        try
        {
            await _provider.InitializeAsync(
                pakDir,
                _settings.UeVersion,
                _settings.AesKey,
                _settings.MappingsPath.Length > 0 ? _settings.MappingsPath : null,
                progress,
                ct);

            if (_provider.MappingsWarning is { } warn)
                MessageBox.Show(warn, "Mappings warning", MessageBoxButton.OK, MessageBoxImage.Warning);

            SetStatus("Building asset tree…", 100);
            _allRoots = await Task.Run(() =>
                AssetTreeNode.BuildTree(_provider.GetAllFiles()), ct);

            AssetTree.ItemsSource = _allRoots;
            SetStatus($"Ready — {_provider.GetAllFiles().Count()} files loaded.", 100);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Load cancelled.", 0);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load: {ex.Message}", "UEpaker",
                MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Load failed.", 0);
        }
        finally
        {
            BtnLoad.IsEnabled = true;
            PrgLoad.Visibility = Visibility.Collapsed;
        }
    }

    // ── Tree ─────────────────────────────────────────────────────────────────

    private void TreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem tvi &&
            tvi.DataContext is AssetTreeNode node)
        {
            node.EnsureChildrenLoaded();
        }
    }

    private void AssetTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is AssetTreeNode node && !node.IsFolder)
            _ = ShowPreviewAsync(node);
        else
            ClearPreview();
    }

    // ── Search ────────────────────────────────────────────────────────────────

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = TxtSearch.Text.Trim();
        TxtSearchHint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_allRoots is null) return;

        if (query.Length == 0)
        {
            AssetTree.ItemsSource = _allRoots;
            return;
        }

        // Flat search: show matching file nodes directly (skip folder hierarchy)
        var matches = _provider?.GetAllFiles()
            .Where(p => p.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p)
            .Select(p => new AssetTreeNode(p, p))
            ?? Enumerable.Empty<AssetTreeNode>();

        AssetTree.ItemsSource = matches.ToList();
    }

    // ── Preview ───────────────────────────────────────────────────────────────

    private async Task ShowPreviewAsync(AssetTreeNode node)
    {
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        TxtPreviewPath.Text = node.FullPath;
        TxtPreviewHint.Visibility = Visibility.Collapsed;
        TxtPreviewJson.Visibility = Visibility.Collapsed;
        TxtPreviewLoading.Visibility = Visibility.Visible;

        try
        {
            var json = await _provider!.LoadPackageJsonAsync(node.FullPath, ct);

            if (ct.IsCancellationRequested) return;

            if (json is null)
            {
                TxtPreviewHint.Text = "This file type cannot be previewed.";
                TxtPreviewHint.Visibility = Visibility.Visible;
            }
            else
            {
                TxtPreviewJson.Text = json;
                TxtPreviewJson.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                TxtPreviewHint.Text = $"Could not load asset: {ex.Message}";
                TxtPreviewHint.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                TxtPreviewLoading.Visibility = Visibility.Collapsed;
        }
    }

    private void ClearPreview()
    {
        _previewCts?.Cancel();
        TxtPreviewPath.Text = string.Empty;
        TxtPreviewHint.Text = "Select an asset to preview its content.";
        TxtPreviewHint.Visibility = Visibility.Visible;
        TxtPreviewJson.Visibility = Visibility.Collapsed;
        TxtPreviewJson.Text = string.Empty;
        TxtPreviewLoading.Visibility = Visibility.Collapsed;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetStatus(string message, int percent)
    {
        TxtStatus.Text = message;
        PrgLoad.Value = percent;
    }

    protected override void OnClosed(EventArgs e)
    {
        _loadCts?.Cancel();
        _previewCts?.Cancel();
        _provider?.Dispose();
        base.OnClosed(e);
    }
}
