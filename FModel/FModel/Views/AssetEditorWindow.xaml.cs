using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.Unversioned;
using UAssetAPI.UnrealTypes;

namespace FModel.Views;

public partial class AssetEditorWindow : AdonisUI.Controls.AdonisWindow
{
    private readonly GameFile                _gf;
    private readonly AbstractVfsFileProvider _provider;
    private readonly bool                    _autoLaunchUAssetGui;

    private UAsset?  _asset;
    private string?  _editPath;
    private bool     _isDirty;
    private Process? _externalProcess;

    // Persistent edit folder — files here survive across sessions and are never re-extracted if present.
    internal static readonly string EditRoot =
        Path.Combine(AppContext.BaseDirectory, "EditedAssets");

    // Place UAssetGUI.exe here.
    private static readonly string UAssetGuiExe =
        Path.Combine(AppContext.BaseDirectory, "UAssetGUI", "UAssetGUI.exe");

    public AssetEditorWindow(GameFile gf, AbstractVfsFileProvider provider, bool autoLaunchUAssetGui = false)
    {
        _gf                  = gf;
        _provider            = provider;
        _autoLaunchUAssetGui = autoLaunchUAssetGui;
        InitializeComponent();
        TxtPath.Text = gf.Path;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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
                // Apply any pending JSON sidecar edits before loading.
                var (ok, err) = TryApplyJsonSidecar(_editPath);
                if (!ok) SetStatus($"⚠ JSON→uasset conversion failed: {err}");
                else     SetStatus("Using existing edited file…");
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

            if (_autoLaunchUAssetGui)
            {
                await OpenInBranchViewAsync();
                return;
            }

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

        var expanded = SnapshotExpanded();

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
                    foreach (var p in PropNodeBuilder.BuildExport(dte, _asset, OnPropChanged))
                        header.Children.Add(p);

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
        RestoreExpanded(roots, string.Empty, expanded);
    }

    // ── Expansion preservation ────────────────────────────────────────────────

    private HashSet<string> SnapshotExpanded()
    {
        var set = new HashSet<string>();
        if (PropTree.ItemsSource is IEnumerable<PropNode> roots)
            CollectExpanded(roots, string.Empty, set);
        return set;
    }

    private static void CollectExpanded(IEnumerable<PropNode> nodes, string prefix, HashSet<string> set)
    {
        foreach (var n in nodes)
        {
            var path = prefix + "/" + n.Name;
            if (n.IsExpanded) set.Add(path);
            CollectExpanded(n.Children, path, set);
        }
    }

    private static void RestoreExpanded(IEnumerable<PropNode> nodes, string prefix, HashSet<string> expanded)
    {
        foreach (var n in nodes)
        {
            var path = prefix + "/" + n.Name;
            if (expanded.Contains(path)) n.IsExpanded = true;
            RestoreExpanded(n.Children, path, expanded);
        }
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

    // ── Context menu ─────────────────────────────────────────────────────────

    private void PropTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var node = PropTree.SelectedItem as PropNode;
        if (node is null)
        {
            e.Handled = true;
            return;
        }

        bool isArray   = node.SourcePD is ArrayPropertyData;
        bool isElement = node.ParentArray is not null;

        // "Add element": shown when the selected node is an array itself OR an element inside one
        MenuAddElement.Visibility       = (isArray || isElement) ? Visibility.Visible : Visibility.Collapsed;
        // "Remove element": shown only for elements inside an array
        MenuRemoveElement.Visibility    = isElement              ? Visibility.Visible : Visibility.Collapsed;
        // "Duplicate element": shown only for elements inside an array
        MenuDuplicateElement.Visibility = isElement              ? Visibility.Visible : Visibility.Collapsed;

        // If nothing is applicable, cancel the menu
        if (!isArray && !isElement)
            e.Handled = true;
    }

    // ── Array add / remove ────────────────────────────────────────────────────

    private void AddArrayElement_Click(object sender, RoutedEventArgs e)
    {
        if (_asset is null) return;
        var node = PropTree.SelectedItem as PropNode;
        if (node is null) return;

        ArrayPropertyData? apd = null;

        if (node.SourcePD is ArrayPropertyData directApd)
        {
            // Selected node is the array itself — append to end
            apd = directApd;
        }
        else if (node.ParentArray is not null)
        {
            // Selected node is an element — append after this element's index
            apd = node.ParentArray;
        }

        if (apd is null) return;
        if (apd.ArrayType is null) return;

        var newElem = CreateDefaultElement(apd.ArrayType, _asset);
        if (newElem is null)
        {
            SetStatus($"Cannot create default element for type '{apd.ArrayType}'.");
            return;
        }

        apd.Value = apd.Value.Concat(new[] { newElem }).ToArray();
        OnPropChanged();
        BuildTree();
    }

    private void RemoveArrayElement_Click(object sender, RoutedEventArgs e)
    {
        if (_asset is null) return;
        var node = PropTree.SelectedItem as PropNode;
        if (node?.ParentArray is null || node.SourcePD is null) return;

        var apd = node.ParentArray;
        var pd  = node.SourcePD;

        apd.Value = apd.Value.Where(x => !ReferenceEquals(x, pd)).ToArray();
        OnPropChanged();
        BuildTree();
    }

    private void DuplicateArrayElement_Click(object sender, RoutedEventArgs e)
    {
        if (_asset is null) return;
        var node = PropTree.SelectedItem as PropNode;
        if (node?.ParentArray is null || node.SourcePD is null) return;

        var apd = node.ParentArray;
        var pd  = node.SourcePD;

        // Find the index of the source element and insert a clone right after it
        var list = apd.Value.ToList();
        var idx  = list.FindIndex(x => ReferenceEquals(x, pd));
        if (idx < 0) return;

        // Clone by round-tripping through JSON serialization isn't available without full context,
        // so fall back to creating a default element of the same type as a "duplicate".
        if (apd.ArrayType is null) return;
        var newElem = CreateDefaultElement(apd.ArrayType, _asset);
        if (newElem is null)
        {
            SetStatus($"Cannot duplicate element of type '{apd.ArrayType}'.");
            return;
        }

        list.Insert(idx + 1, newElem);
        apd.Value = list.ToArray();
        OnPropChanged();
        BuildTree();
    }

    // ── Default element factory ───────────────────────────────────────────────

    private static PropertyData? CreateDefaultElement(FName arrayType, UAsset asset)
    {
        var dummyName = FName.DefineDummy(asset, "None");
        return arrayType.Value.Value switch
        {
            "BoolProperty"   => new BoolPropertyData(dummyName)   { Value = false },
            "IntProperty"    => new IntPropertyData(dummyName)    { Value = 0 },
            "Int8Property"   => new Int8PropertyData(dummyName)   { Value = 0 },
            "Int16Property"  => new Int16PropertyData(dummyName)  { Value = 0 },
            "Int64Property"  => new Int64PropertyData(dummyName)  { Value = 0L },
            "UInt16Property" => new UInt16PropertyData(dummyName) { Value = 0 },
            "UInt32Property" => new UInt32PropertyData(dummyName) { Value = 0u },
            "UInt64Property" => new UInt64PropertyData(dummyName) { Value = 0ul },
            "FloatProperty"  => new FloatPropertyData(dummyName)  { Value = 0f },
            "DoubleProperty" => new DoublePropertyData(dummyName) { Value = 0.0 },
            "StrProperty"    => new StrPropertyData(dummyName)    { Value = new FString(string.Empty) },
            "NameProperty"   => new NamePropertyData(dummyName)   { Value = FName.DefineDummy(asset, "None") },
            "ObjectProperty" => new ObjectPropertyData(dummyName) { Value = new FPackageIndex(0) },
            "StructProperty" => new StructPropertyData(dummyName) { Value = new List<PropertyData>() },
            _ => null,
        };
    }

    // ── Dirty tracking ────────────────────────────────────────────────────────

    private void OnPropChanged()
    {
        _isDirty = true;
        UpdateEditLock();
        if (BtnSave.IsEnabled)
            SetStatus("Unsaved changes.");
    }

    // ── External-process lock ─────────────────────────────────────────────────

    private bool IsExternalLocked => _externalProcess is { HasExited: false };

    private void UpdateEditLock()
    {
        var locked = IsExternalLocked;
        BtnSave.IsEnabled = _isDirty && !locked;
        BtnSave.ToolTip   = locked ? "Close Branch View before saving here." : null;
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_asset is null || _editPath is null) return;
        if (IsExternalLocked)
        {
            SetStatus("Close Branch View first — saving now would overwrite its changes.");
            return;
        }
        var tmp = _editPath + ".tmp";
        try
        {
            _asset.Write(tmp);
            File.Move(tmp, _editPath, overwrite: true);
            _isDirty          = false;
            BtnSave.IsEnabled = false;
            SetStatus($"Saved → {_editPath}");
        }
        catch (Exception ex)
        {
            SetStatus($"Save failed ({ex.GetType().Name}): {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    // ── Open in UAssetGUI ─────────────────────────────────────────────────────

    private async void BtnOpenExternal_Click(object sender, RoutedEventArgs e)
        => await OpenInBranchViewAsync();

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

        // Flush any JSON edits before launching the external tool.
        var (sidecarOk, sidecarErr) = TryApplyJsonSidecar(_editPath!);
        if (!sidecarOk)
        {
            var proceed = MessageBox.Show(
                $"JSON→.uasset conversion failed:\n{sidecarErr}\n\nOpen Branch View anyway (with stale .uasset)?",
                "Conversion Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (proceed != MessageBoxResult.Yes) return;
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
            _externalProcess?.Dispose();
            _externalProcess = Process.Start(new ProcessStartInfo(UAssetGuiExe, $"\"{_editPath}\" {verArg}")
            {
                UseShellExecute = false,
            });

            if (_externalProcess != null)
            {
                _externalProcess.EnableRaisingEvents = true;
                _externalProcess.Exited += (_, _) => Dispatcher.Invoke(() =>
                {
                    _externalProcess = null;
                    _isDirty         = false; // external tool may have rewritten the file
                    UpdateEditLock();
                    SetStatus("Branch View closed. Reopen the editor to pick up external changes.");
                });
            }

            UpdateEditLock();
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

    // ── JSON sidecar helpers ──────────────────────────────────────────────────

    /// <summary>
    /// If a JSON sidecar (.uasset.json) exists alongside the asset, convert it
    /// back to binary .uasset.  Returns (true, null) on success or no sidecar.
    /// </summary>
    public static (bool Ok, string? Error) TryApplyJsonSidecar(string editPath)
    {
        var jsonPath = editPath + ".json";
        if (!File.Exists(jsonPath)) return (true, null);

        var tmp = editPath + ".tmp";
        try
        {
            var json  = File.ReadAllText(jsonPath);
            var patch = UAsset.DeserializeJson(json);
            patch.Write(tmp);
            File.Move(tmp, editPath, overwrite: true);
            return (true, null);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return (false, ex.Message);
        }
    }

    // ── Engine version mapping ────────────────────────────────────────────────

    public static EngineVersion MapEGame(EGame game) => game switch
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

    // ── Static JSON extraction helper (used by RightClickMenuCommand) ─────────

    public static Task ExtractAndLoadJsonAsync(
        GameFile gf,
        AbstractVfsFileProvider provider,
        Action<string, string> onSuccess,
        Action<string> onError)
    {
        return Task.Run(async () =>
        {
            var editPath = Path.Combine(EditRoot, gf.Path.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                if (!IsValidAssetFile(editPath))
                {
                    await ExtractStaticAsync(gf, provider, editPath);
                    if (!IsValidAssetFile(editPath))
                    {
                        onError("Extraction failed — asset not found after retoc.");
                        return;
                    }
                }

                var ev    = MapEGame(provider.Versions.Game);
                var usmap = GetUsmapStatic(provider);
                string json = null;
                var asset = new UAsset(editPath, ev, mappings: usmap);
                json = asset.SerializeJson(true);
                onSuccess(editPath, json);
            }
            catch (Exception ex)
            {
                onError(ex.Message);
            }
        });
    }

    private static async Task ExtractStaticAsync(GameFile gf, AbstractVfsFileProvider provider, string editPath)
    {
        IReadOnlyDictionary<string, byte[]>? dict = null;
        await Task.Run(() => provider.TrySavePackage(gf, out dict));

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

                foreach (var (k, b) in dict)
                {
                    var dest = Path.Combine(EditRoot, k.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    await File.WriteAllBytesAsync(dest, b);
                }
                return;
            }
        }

        // IoStore path via retoc
        var paksDir = UserSettings.Default.CurrentDir.GameDirectory;
        var aesKey  = UserSettings.Default.CurrentDir.AesKeys?.MainKey;
        var version = MapEGameToRetocVersion(provider.Versions.Game);
        var filter  = Path.ChangeExtension(gf.Path, null);

        Directory.CreateDirectory(EditRoot);
        await RetocService.ToLegacyAsync(
            input:         paksDir,
            outputDir:     EditRoot,
            aesKey:        aesKey,
            engineVersion: version,
            filter:        filter);
    }

    private static Usmap? GetUsmapStatic(AbstractVfsFileProvider provider)
    {
        if (provider.MappingsContainer is FileUsmapTypeMappingsProvider fp)
        {
            try { return new Usmap(fp.FilePath); }
            catch { }
        }
        return null;
    }
}
