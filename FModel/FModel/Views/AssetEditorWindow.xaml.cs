using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Windows;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.FileProvider.Vfs;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Versions;
using FModel.Settings;
using PakEditor.Editor;
using PakEditor.Packer;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.Unversioned;
using UAssetAPI.UnrealTypes;

namespace FModel.Views;

public partial class AssetEditorWindow : AdonisUI.Controls.AdonisWindow
{
    private readonly GameFile                _gf;
    private readonly AbstractVfsFileProvider _provider;

    private UAsset? _asset;
    private string? _editPath;
    private bool    _isDirty;

    // Persistent edit folder — files here survive across sessions and are never re-extracted if present.
    private static readonly string EditRoot =
        Path.Combine(AppContext.BaseDirectory, "EditedAssets");

    // Place UAssetGUI.exe here.
    private static readonly string UAssetGuiExe =
        Path.Combine(AppContext.BaseDirectory, "UAssetGUI", "UAssetGUI.exe");

    public AssetEditorWindow(GameFile gf, AbstractVfsFileProvider provider)
    {
        _gf       = gf;
        _provider = provider;
        InitializeComponent();
        TxtPath.Text = gf.Path;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BtnOpenExternal.Content = UserSettings.Default.AssetEditorMode == EAssetEditorMode.JsonEditor
            ? "Open in JSON Editor"
            : "Open in Branch View";
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        _editPath = Path.Combine(EditRoot,
            _gf.Path.Replace('/', Path.DirectorySeparatorChar));

        string? loadWarning = null;

        try
        {
            if (IsValidAssetFile(_editPath))
            {
                SetStatus("Using existing edited file…");
            }
            else
            {
                await ExtractToEditedAsync();
                if (!File.Exists(_editPath))
                    return;
            }

            SetStatus("Loading with UAssetAPI…");
            var ev    = MapEGame(_provider.Versions.Game);
            var usmap = GetUsmap();
            await Task.Run(() =>
            {
                try
                {
                    _asset = new UAsset(_editPath, ev, mappings: usmap);
                }
                catch (Exception ex)
                {
                    loadWarning = $"Partial load ({ex.GetType().Name}: {ex.Message})";
                }
            });

            BuildTree();
            TxtLoading.Visibility = Visibility.Collapsed;
            SetStatus(loadWarning != null
                ? $"⚠ {loadWarning} — some exports may be missing."
                : "Expand exports to view and edit properties. Save writes back to EditedAssets.");
        }
        catch (Exception ex)
        {
            TxtLoading.Visibility = Visibility.Collapsed;
            SetStatus($"Error: {ex.Message}");
        }
    }

    // ── Extraction ────────────────────────────────────────────────────────────

    private async Task ExtractToEditedAsync()
    {
        IReadOnlyDictionary<string, byte[]>? dict = null;
        await Task.Run(() => _provider.TrySavePackage(_gf, out dict));

        if (dict is not null && dict.Count > 0)
        {
            foreach (var (key, bytes) in dict)
            {
                if (!key.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) &&
                    !key.EndsWith(".umap",   StringComparison.OrdinalIgnoreCase))
                    continue;

                uint magic = bytes.Length >= 4 ? BitConverter.ToUInt32(bytes, 0) : 0;
                if (magic != UAsset.UASSET_MAGIC)
                    break;

                SetStatus("Extracting legacy asset…");
                foreach (var (k, b) in dict)
                {
                    var dest = Path.Combine(EditRoot, k.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    await File.WriteAllBytesAsync(dest, b);
                }
                return;
            }
        }

        SetStatus("Extracting IoStore asset via retoc…");

        var paksDir = UserSettings.Default.CurrentDir.GameDirectory;
        var aesKey  = UserSettings.Default.CurrentDir.AesKeys?.MainKey;
        var version = MapEGameToRetocVersion(_provider.Versions.Game);
        var filter  = Path.ChangeExtension(_gf.Path, null);

        Directory.CreateDirectory(EditRoot);

        var result = await RetocService.ToLegacyAsync(
            input:         paksDir,
            outputDir:     EditRoot,
            aesKey:        aesKey,
            engineVersion: version,
            filter:        filter);

        if (!result.Success || !File.Exists(_editPath))
        {
            TxtLoading.Visibility = Visibility.Collapsed;
            SetStatus($"retoc failed (exit {result.ExitCode}): {result.Errors.Trim()}");
        }
    }

    // ── Tree ─────────────────────────────────────────────────────────────────

    private void BuildTree()
    {
        if (_asset is null) return;

        var roots = new ObservableCollection<PropNode>();

        for (int i = 0; i < _asset.Exports.Count; i++)
        {
            var export = _asset.Exports[i];

            string typeName = string.Empty;
            if (export.ClassIndex.IsImport())
            {
                var ii = -export.ClassIndex.Index - 1;
                if (ii >= 0 && ii < _asset.Imports.Count)
                    typeName = _asset.Imports[ii].ObjectName.ToString();
            }

            var header = new PropNode
            {
                Name     = $"[{i}] {export.ObjectName}",
                TypeName = typeName,
                IsLeaf   = false,
            };

            if (export is DataTableExport dte)
            {
                try
                {
                    // NormalExport props (e.g. RowStruct reference)
                    foreach (var p in PropNodeBuilder.BuildExport(dte, _asset, OnPropChanged))
                        header.Children.Add(p);

                    // DataTable rows
                    if (dte.Table?.Data is { Count: > 0 })
                        header.Children.Add(
                            PropNodeBuilder.BuildDataTableRows(dte.Table, _asset, OnPropChanged));
                }
                catch { }
            }
            else if (export is NormalExport ne)
            {
                try
                {
                    foreach (var p in PropNodeBuilder.BuildExport(ne, _asset, OnPropChanged))
                        header.Children.Add(p);
                }
                catch { /* ignore per-export parse failures */ }
            }

            roots.Add(header);
        }

        PropTree.ItemsSource = roots;
    }

    // ── Single-click editing ──────────────────────────────────────────────────

    private void PropValue_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var tb = (TextBox)sender;
        if (tb.IsReadOnly) return;
        if (!tb.IsKeyboardFocusWithin)
        {
            tb.Focus();
            e.Handled = true;
        }
    }

    private void PropValue_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.SelectAll();
    }

    // ── Dirty tracking ────────────────────────────────────────────────────────

    private void OnPropChanged()
    {
        if (_isDirty) return;
        _isDirty = true;
        BtnSave.IsEnabled = true;
        SetStatus("Unsaved changes.");
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_asset is null || _editPath is null) return;
        try
        {
            _asset.Write(_editPath);
            _isDirty          = false;
            BtnSave.IsEnabled = false;
            SetStatus($"Saved → {_editPath}");
        }
        catch (Exception ex)
        {
            SetStatus($"Save failed: {ex.Message}");
        }
    }

    // ── Open in UAssetGUI ─────────────────────────────────────────────────────

    private async void BtnOpenExternal_Click(object sender, RoutedEventArgs e)
    {
        if (UserSettings.Default.AssetEditorMode == EAssetEditorMode.JsonEditor)
            OpenJsonEditor();
        else
            await OpenInBranchViewAsync();
    }

    private void OpenJsonEditor()
    {
        if (_asset is null || _editPath is null)
        {
            SetStatus("Asset not loaded yet.");
            return;
        }
        var win = new JsonEditorWindow(_editPath, _asset) { Owner = this };
        win.Show();
        SetStatus("JSON editor opened.");
    }

    private async Task OpenInBranchViewAsync()
    {
        if (!File.Exists(UAssetGuiExe))
        {
            SetStatus($"UAssetGUI.exe not found — place it at: {UAssetGuiExe}");
            return;
        }

        // Ensure the asset exists in EditedAssets.
        if (!IsValidAssetFile(_editPath))
        {
            SetStatus("Extracting asset to EditedAssets…");
            await ExtractToEditedAsync();
            if (!IsValidAssetFile(_editPath))
            {
                SetStatus("Extraction failed — cannot open in Branch View.");
                return;
            }
        }

        TryWriteUAssetGuiConfig();

        var versionStr = GetShortVersionString(_provider.Versions.Game);
        var verArg     = MapEGame(_provider.Versions.Game).ToString();
        var mapDisplay = _mappingFileName != null ? _mappingFileName + ".usmap" : "(none)";

        MessageBox.Show(
            $"Branch View (UAssetGUI) is about to open.\n\n" +
            $"Engine version: UE {versionStr}\n" +
            $"Mappings: {mapDisplay}\n\n" +
            $"Settings written to:\n" +
            $"  - UAssetGUI\\Data\\config.json  (portable mode)\n" +
            $"  - %LocalAppData%\\UAssetGUI\\config.json  (standard mode)\n\n" +
            $"⚠  If mappings don't appear on first launch, close and reopen UAssetGUI once.",
            "Branch View — Opening",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        try
        {
            Process.Start(new ProcessStartInfo(UAssetGuiExe, $"\"{_editPath}\" {verArg}")
            {
                UseShellExecute = false,
            });
            SetStatus($"Opened in Branch View → {_editPath}");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to launch Branch View: {ex.Message}");
        }
    }

    /// <summary>
    /// Mapping file stem WITHOUT the .usmap extension.
    /// Written to PreferredMappings in the portable config; not passed as a CLI arg.
    /// </summary>
    private string? _mappingFileName;

    private void TryWriteUAssetGuiConfig()
    {
        // ── Determine both config locations ──────────────────────────────────
        // Portable: Data/ folder next to UAssetGUI.exe (presence of folder = portable mode)
        // Standard: %LocalAppData%\UAssetGUI\  (used when not in portable mode)
        // We write to both so mappings/version are set regardless of which mode is active.
        var portableDataDir = Path.Combine(Path.GetDirectoryName(UAssetGuiExe)!, "Data");
        var appDataDir      = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UAssetGUI");

        // ── Resolve mappings file ─────────────────────────────────────────────
        _mappingFileName = null;
        if (_provider.MappingsContainer is FileUsmapTypeMappingsProvider fp
            && File.Exists(fp.FilePath))
        {
            var gameName = UserSettings.Default.CurrentDir.GameName
                           ?? Path.GetFileNameWithoutExtension(fp.FilePath);
            _mappingFileName = $"{gameName}-Mapping"; // no extension

            // Copy into portable Mappings folder
            try
            {
                var portableMappingsDir = Path.Combine(portableDataDir, "Mappings");
                Directory.CreateDirectory(portableMappingsDir);
                File.Copy(fp.FilePath,
                          Path.Combine(portableMappingsDir, _mappingFileName + ".usmap"),
                          overwrite: true);
            }
            catch { /* non-fatal */ }

            // Copy into %LocalAppData%\UAssetGUI\Mappings
            try
            {
                var appDataMappingsDir = Path.Combine(appDataDir, "Mappings");
                Directory.CreateDirectory(appDataMappingsDir);
                File.Copy(fp.FilePath,
                          Path.Combine(appDataMappingsDir, _mappingFileName + ".usmap"),
                          overwrite: true);
            }
            catch { /* non-fatal */ }
        }

        var verString = MapEGame(_provider.Versions.Game).ToString(); // e.g. "VER_UE5_6"

        // ── Write config to portable Data/ ────────────────────────────────────
        try
        {
            Directory.CreateDirectory(portableDataDir);
            var configPath = Path.Combine(portableDataDir, "config.json");
            WriteUAssetGuiConfigFile(configPath, verString, _mappingFileName);
        }
        catch { /* non-fatal */ }

        // ── Write config to %LocalAppData%\UAssetGUI ──────────────────────────
        try
        {
            Directory.CreateDirectory(appDataDir);
            var configPath = Path.Combine(appDataDir, "config.json");
            WriteUAssetGuiConfigFile(configPath, verString, _mappingFileName);
        }
        catch { /* non-fatal */ }
    }

    private static void WriteUAssetGuiConfigFile(
        string configPath, string verString, string? mappingFileName)
    {
        var json = new System.Text.Json.Nodes.JsonObject();
        if (File.Exists(configPath))
        {
            try
            {
                json = System.Text.Json.Nodes.JsonNode
                           .Parse(File.ReadAllText(configPath))!
                           .AsObject();
            }
            catch { /* corrupt — start fresh */ }
        }

        json["PreferredVersion"] = verString;
        if (mappingFileName != null)
            json["PreferredMappings"] = mappingFileName;

        File.WriteAllText(configPath,
            json.ToJsonString(new System.Text.Json.JsonSerializerOptions
                { WriteIndented = true }));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetStatus(string msg)
        => TxtStatus.Text = msg;

    private Usmap? GetUsmap()
    {
        if (_provider.MappingsContainer is FileUsmapTypeMappingsProvider fp)
        {
            try { return new Usmap(fp.FilePath); }
            catch { }
        }
        return null;
    }

    private static bool IsValidAssetFile(string? path)
    {
        if (path == null || !File.Exists(path)) return false;
        try
        {
            using var fs = File.OpenRead(path);
            if (fs.Length < 4) return false;
            var buf = new byte[4];
            fs.ReadExactly(buf);
            return BitConverter.ToUInt32(buf) == UAsset.UASSET_MAGIC;
        }
        catch { return false; }
    }

    private static EngineVersion MapEGame(EGame game) => game switch
    {
        EGame.GAME_UE5_6 or EGame.GAME_UE5_7 => EngineVersion.VER_UE5_6,
        EGame.GAME_UE5_5  => EngineVersion.VER_UE5_5,
        EGame.GAME_UE5_4  => EngineVersion.VER_UE5_4,
        EGame.GAME_UE5_3  => EngineVersion.VER_UE5_3,
        EGame.GAME_UE5_2  => EngineVersion.VER_UE5_2,
        EGame.GAME_UE5_1  => EngineVersion.VER_UE5_1,
        EGame.GAME_UE5_0  => EngineVersion.VER_UE5_0,
        EGame.GAME_UE4_27 => EngineVersion.VER_UE4_27,
        EGame.GAME_UE4_26 => EngineVersion.VER_UE4_26,
        EGame.GAME_UE4_25 => EngineVersion.VER_UE4_25,
        EGame.GAME_UE4_24 => EngineVersion.VER_UE4_24,
        EGame.GAME_UE4_23 => EngineVersion.VER_UE4_23,
        _                 => EngineVersion.UNKNOWN,
    };

    // "VER_UE5_6" → "5.6",  "VER_UE4_27" → "4.27"
    private static string GetShortVersionString(EGame game)
    {
        var ev   = MapEGame(game).ToString(); // e.g. "VER_UE5_6"
        var part = ev.StartsWith("VER_UE") ? ev[6..].Replace("_", ".") : "5.6";
        return part;
    }

    private static string MapEGameToRetocVersion(EGame game) => game switch
    {
        EGame.GAME_UE5_6 or EGame.GAME_UE5_7 => "UE5_6",
        EGame.GAME_UE5_5  => "UE5_5",
        EGame.GAME_UE5_4  => "UE5_4",
        EGame.GAME_UE5_3  => "UE5_3",
        EGame.GAME_UE5_2  => "UE5_2",
        EGame.GAME_UE5_1  => "UE5_1",
        EGame.GAME_UE5_0  => "UE5_0",
        EGame.GAME_UE4_27 => "UE4_27",
        EGame.GAME_UE4_26 => "UE4_26",
        EGame.GAME_UE4_25 => "UE4_25",
        _                 => "UE5_6",
    };
}
