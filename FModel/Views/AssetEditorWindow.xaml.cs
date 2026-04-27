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
    private string? _stagedPath;
    private bool    _isDirty;

    private static readonly string StagingRoot =
        Path.Combine(Path.GetTempPath(), "PakEditor", "Staged");

    // Persistent edit folder — files here are never overwritten by a fresh extract.
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
        => _ = InitAsync();

    private async Task InitAsync()
    {
        _stagedPath = Path.Combine(StagingRoot,
            _gf.Path.Replace('/', Path.DirectorySeparatorChar));

        string? loadWarning = null;

        try
        {
            if (!IsValidStagedFile(_stagedPath))
            {
                await StageAssetAsync();
                if (!File.Exists(_stagedPath))
                    return;
            }
            else
            {
                SetStatus("Using cached staged file…");
            }

            SetStatus("Loading with UAssetAPI…");
            var ev    = MapEGame(_provider.Versions.Game);
            var usmap = GetUsmap();
            await Task.Run(() =>
            {
                try
                {
                    _asset = new UAsset(_stagedPath, ev, mappings: usmap);
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
                : "Expand exports to view and edit properties. Save writes back to the staged file.");
        }
        catch (Exception ex)
        {
            TxtLoading.Visibility = Visibility.Collapsed;
            SetStatus($"Error: {ex.Message}");
        }
    }

    // ── Staging ───────────────────────────────────────────────────────────────

    private async Task StageAssetAsync()
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

                SetStatus("Staging legacy asset…");
                foreach (var (k, b) in dict)
                {
                    var dest = Path.Combine(StagingRoot, k.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    await File.WriteAllBytesAsync(dest, b);
                }
                return;
            }
        }

        SetStatus("Staging IoStore asset via retoc…");

        var paksDir = UserSettings.Default.CurrentDir.GameDirectory;
        var aesKey  = UserSettings.Default.CurrentDir.AesKeys?.MainKey;
        var version = MapEGameToRetocVersion(_provider.Versions.Game);
        var filter  = Path.ChangeExtension(_gf.Path, null);

        Directory.CreateDirectory(StagingRoot);

        var result = await RetocService.ToLegacyAsync(
            input:         paksDir,
            outputDir:     StagingRoot,
            aesKey:        aesKey,
            engineVersion: version,
            filter:        filter);

        if (!result.Success || !File.Exists(_stagedPath))
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
        if (_asset is null || _stagedPath is null) return;
        try
        {
            _asset.Write(_stagedPath);
            _isDirty          = false;
            BtnSave.IsEnabled = false;
            SetStatus($"Saved → {_stagedPath}");
        }
        catch (Exception ex)
        {
            SetStatus($"Save failed: {ex.Message}");
        }
    }

    // ── Open in UAssetGUI ─────────────────────────────────────────────────────

    private async void BtnOpenUAssetGui_Click(object sender, RoutedEventArgs e)
        => await OpenInUAssetGuiAsync();

    private async Task OpenInUAssetGuiAsync()
    {
        if (!File.Exists(UAssetGuiExe))
        {
            SetStatus($"UAssetGUI.exe not found — place it at: {UAssetGuiExe}");
            return;
        }

        // Ensure the asset is staged to temp first.
        if (!IsValidStagedFile(_stagedPath))
        {
            SetStatus("Re-staging asset…");
            await StageAssetAsync();
            if (!IsValidStagedFile(_stagedPath))
            {
                SetStatus("Staging failed — cannot open in UAssetGUI.");
                return;
            }
        }

        // Compute the persistent edit path (exe/EditedAssets/<virtual path>).
        var editPath = Path.Combine(EditRoot,
            _gf.Path.Replace('/', Path.DirectorySeparatorChar));

        // Only copy from temp if the edit file doesn't exist yet (preserve prior edits).
        if (!File.Exists(editPath))
        {
            SetStatus("Copying to EditedAssets…");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(editPath)!);

                // Copy .uasset and any companion files (.uexp, .ubulk, .uptnl, etc.)
                var stagedBase = Path.ChangeExtension(_stagedPath, null);
                var editBase   = Path.ChangeExtension(editPath,    null);
                var stagedDir  = Path.GetDirectoryName(_stagedPath)!;
                var stem       = Path.GetFileNameWithoutExtension(_stagedPath);

                foreach (var src in Directory.GetFiles(stagedDir, stem + ".*"))
                {
                    var dst = editBase + Path.GetExtension(src);
                    File.Copy(src, dst, overwrite: false);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Copy failed: {ex.Message}");
                return;
            }
        }
        else
        {
            SetStatus("Using existing edited file (not overwriting).");
        }

        // Copy usmap into portable Data folder + write config.
        TryWriteUAssetGuiConfig();

        // Show setup reminder popup before launching.
        var versionStr = GetShortVersionString(_provider.Versions.Game);
        var verArg     = MapEGame(_provider.Versions.Game).ToString(); // e.g. "VER_UE5_6"
        var mapDisplay = _mappingFileName != null ? _mappingFileName + ".usmap" : "(none)";

        MessageBox.Show(
            $"UAssetGUI is about to open.\n\n" +
            $"Engine version: UE {versionStr}\n" +
            $"Mappings: {mapDisplay}\n\n" +
            $"Settings written to:\n" +
            $"  - UAssetGUI\\Data\\config.json  (portable mode)\n" +
            $"  - %LocalAppData%\\UAssetGUI\\config.json  (standard mode)\n\n" +
            $"⚠  If mappings don't appear on first launch, close and reopen UAssetGUI once.",
            "UAssetGUI — Opening",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        // Launch: file + engine version only — mappings are set via config, not CLI arg.
        try
        {
            Process.Start(new ProcessStartInfo(UAssetGuiExe, $"\"{editPath}\" {verArg}")
            {
                UseShellExecute = false,
            });
            SetStatus($"Opened in UAssetGUI -> {editPath}");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to launch UAssetGUI: {ex.Message}");
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

    private static bool IsValidStagedFile(string path)
    {
        if (!File.Exists(path)) return false;
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
