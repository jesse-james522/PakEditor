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
        CmbVersion.SelectedItem = _settings.UeVersion;
        if (CmbVersion.SelectedItem is null) CmbVersion.SelectedIndex = 0;
    }

    private void RestoreSettingsToUi()
    {
        TxtPakDir.Text = _settings.PakDirectory;
        TxtAesKey.Text = _settings.AesKey;
        TxtUsmap.Text = _settings.MappingsPath;
        TxtLooseDir.Text = _settings.LooseDirectory;
        CmbVersion.SelectedItem = _settings.UeVersion;
        ChkHeavyAssets.IsChecked = _settings.PreviewHeavyAssets;
    }

    private void SaveSettingsFromUi()
    {
        _settings.PakDirectory = TxtPakDir.Text.Trim();
        _settings.AesKey = TxtAesKey.Text.Trim();
        _settings.MappingsPath = TxtUsmap.Text.Trim();
        _settings.LooseDirectory = TxtLooseDir.Text.Trim();
        _settings.UeVersion = CmbVersion.SelectedItem is EGame v ? v : EGame.GAME_UE5_6;
        _settings.PreviewHeavyAssets = ChkHeavyAssets.IsChecked == true;
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

    private void BtnBrowseLooseDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select loose files directory" };
        if (dlg.ShowDialog() == true)
            TxtLooseDir.Text = dlg.FolderName;
    }

    private void BtnClearLooseDir_Click(object sender, RoutedEventArgs e)
    {
        TxtLooseDir.Text = string.Empty;
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
            SetStatus(p.Message, p.Percent));

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
            var pakFiles = _provider.GetAllFiles();
            var looseDir = _settings.LooseDirectory;

            _allRoots = await Task.Run(() =>
            {
                var roots = new List<AssetTreeNode>(
                    AssetTreeNode.BuildTree(pakFiles));

                if (!string.IsNullOrWhiteSpace(looseDir) && Directory.Exists(looseDir))
                {
                    var looseFiles = Directory.EnumerateFiles(looseDir, "*.*",
                            SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                                 || f.EndsWith(".umap", StringComparison.OrdinalIgnoreCase));

                    var relativeFiles = looseFiles.Select(f =>
                        "Loose/" + Path.GetRelativePath(looseDir, f).Replace('\\', '/'));

                    var looseRoots = AssetTreeNode.BuildTree(relativeFiles);
                    roots.InsertRange(0, looseRoots);
                }

                return (IReadOnlyList<AssetTreeNode>)roots;
            }, ct);

            AssetTree.ItemsSource = _allRoots;
            var fileCount = pakFiles.Count();
            SetStatus($"Ready — {fileCount:N0} pak files loaded.", 100);
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

    private void MenuOpenForEditing_Click(object sender, RoutedEventArgs e)
    {
        if (AssetTree.SelectedItem is AssetTreeNode node && !node.IsFolder)
            _ = OpenForEditingAsync(node);
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
        PreviewTabs.Visibility = Visibility.Collapsed;
        TxtPreviewLoading.Visibility = Visibility.Visible;
        RtbJson.Document = new System.Windows.Documents.FlowDocument();
        ImportListBox.ItemsSource = null;

        try
        {
            // Loose files (prefixed "Loose/") are on disk — not in the pak provider
            if (node.FullPath.StartsWith("Loose/", StringComparison.OrdinalIgnoreCase))
            {
                await ShowLooseFilePreviewAsync(node, ct);
                return;
            }

            var preview = await _provider!.LoadPreviewAsync(
                node.FullPath, _settings.PreviewHeavyAssets, ct);

            if (ct.IsCancellationRequested) return;

            switch (preview.Result)
            {
                case PreviewResult.Json:
                    JsonSyntaxColorizer.Apply(RtbJson, preview.Json!);
                    ImportListBox.ItemsSource = preview.Imports;
                    PreviewTabs.Visibility = Visibility.Visible;
                    break;

                case PreviewResult.HeavyAsset:
                    TxtPreviewHint.Text =
                        "Heavy asset — enable \"Load meshes, textures…\" in Settings to preview.";
                    TxtPreviewHint.Visibility = Visibility.Visible;
                    break;

                case PreviewResult.Unsupported:
                    TxtPreviewHint.Text = "This file type cannot be previewed.";
                    TxtPreviewHint.Visibility = Visibility.Visible;
                    break;

                case PreviewResult.Error:
                    TxtPreviewHint.Text = $"Could not load asset: {preview.ErrorMessage}";
                    TxtPreviewHint.Visibility = Visibility.Visible;
                    break;
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

    private async Task ShowLooseFilePreviewAsync(AssetTreeNode node, CancellationToken ct)
    {
        var looseDir = _settings.LooseDirectory;
        var relPath = node.FullPath["Loose/".Length..].Replace('/', Path.DirectorySeparatorChar);
        var diskPath = Path.Combine(looseDir, relPath);

        if (!File.Exists(diskPath))
        {
            TxtPreviewHint.Text = $"File not found on disk: {diskPath}";
            TxtPreviewHint.Visibility = Visibility.Visible;
            TxtPreviewLoading.Visibility = Visibility.Collapsed;
            return;
        }

        // Show raw hex summary for loose files — full editor via "Open for editing"
        var size = new FileInfo(diskPath).Length;
        TxtPreviewHint.Text =
            $"Loose file — {size / 1024.0:F1} KB\n{diskPath}\n\nRight-click → Open for editing to inspect properties.";
        TxtPreviewHint.Visibility = Visibility.Visible;
        TxtPreviewLoading.Visibility = Visibility.Collapsed;

        await Task.CompletedTask;
    }

    private void ClearPreview()
    {
        _previewCts?.Cancel();
        TxtPreviewPath.Text = string.Empty;
        TxtPreviewHint.Text = "Select an asset to preview its content.";
        TxtPreviewHint.Visibility = Visibility.Visible;
        PreviewTabs.Visibility = Visibility.Collapsed;
        RtbJson.Document = new System.Windows.Documents.FlowDocument();
        ImportListBox.ItemsSource = null;
        TxtPreviewLoading.Visibility = Visibility.Collapsed;
    }

    // ── Editor ────────────────────────────────────────────────────────────────

    private async Task OpenForEditingAsync(AssetTreeNode node)
    {
        MainTabs.SelectedItem = TabEditor;
        TxtEditorHint.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;
        TxtEditorPath.Text = node.FullPath;
        TxtEditorStaging.Visibility = Visibility.Visible;
        PropertyTree.ItemsSource = null;
        BtnSaveAsset.IsEnabled = false;
        TxtDirtyIndicator.Text = "Staging…";

        try
        {
            var stagedPath = await AssetBridge.AssetStager.StageAsync(
                node.FullPath,
                _settings.LooseDirectory,
                _settings.PakDirectory,
                _provider,
                _settings.UeVersion);

            TxtEditorStaging.Visibility = Visibility.Collapsed;

            var editorVm = Editor.AssetEditorVm.Load(stagedPath, _settings.UeVersion);
            PropertyTree.ItemsSource = editorVm.Roots;
            editorVm.DirtyChanged += dirty =>
            {
                BtnSaveAsset.IsEnabled = dirty;
                TxtDirtyIndicator.Text = dirty ? "Unsaved changes" : "No unsaved changes";
            };

            TxtDirtyIndicator.Text = "No unsaved changes";
            _currentEditorVm = editorVm;
        }
        catch (Exception ex)
        {
            TxtEditorStaging.Visibility = Visibility.Collapsed;
            TxtEditorPath.Text = $"Error: {ex.Message}";
            TxtDirtyIndicator.Text = "Failed to stage asset.";
        }
    }

    private Editor.AssetEditorVm? _currentEditorVm;

    private void BtnSaveAsset_Click(object sender, RoutedEventArgs e)
    {
        if (_currentEditorVm is null) return;
        try
        {
            _currentEditorVm.Save();
            TxtDirtyIndicator.Text = "Saved.";
            BtnSaveAsset.IsEnabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed: {ex.Message}", "UEpaker",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
