using System.Collections.ObjectModel;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;

namespace PakEditor.Editor;

/// <summary>
/// Node type — mirrors UAssetGUI's PointingTreeNodeType so the same
/// display/populate logic can be applied.
/// </summary>
public enum AssetTreeNodeType
{
    Root,
    SectionHeader,   // "Export Data", "Name Map", "Import Data" etc.
    Export,          // top-level per-export grouping
    NormalExport,    // the actual NormalExport data bucket
    StructData,
    EnumData,
    ClassData,
    PropertyArray,   // ArrayProperty / SetProperty
    PropertyStruct,  // StructProperty
    PropertyMap,     // MapProperty
    ByteArray,       // raw/extra bytes
    Other,
}

/// <summary>
/// WPF-friendly tree node for the asset editor's TreeView.
/// Carries a pointer to the underlying UAssetAPI object so we know
/// what to show in the DataGrid when the node is selected.
/// </summary>
public class AssetTreeNode : ViewModelBase
{
    // ── Display ──────────────────────────────────────────────────────────────
    private string _label = string.Empty;
    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    // ── Data ─────────────────────────────────────────────────────────────────
    public object?          Pointer    { get; init; }
    public AssetTreeNodeType NodeType   { get; init; }
    public int              ExportIndex { get; init; } = -1;

    public ObservableCollection<AssetTreeNode> Children { get; } = new();

    // ── Helpers ───────────────────────────────────────────────────────────────

    public bool HasProperties =>
        NodeType is AssetTreeNodeType.NormalExport
                 or AssetTreeNodeType.PropertyStruct
                 or AssetTreeNodeType.PropertyArray
                 or AssetTreeNodeType.StructData;

    /// <summary>Build the child nodes for this node (lazy).</summary>
    public void EnsureChildren(UAssetAPI.UAsset asset)
    {
        if (Children.Count > 0) return; // already populated

        switch (Pointer)
        {
            case NormalExport export:
                foreach (var prop in export.Data)
                    AddPropertyNode(prop, asset);
                break;

            case StructPropertyData spd:
                foreach (var prop in spd.Value)
                    AddPropertyNode(prop, asset);
                break;

            case ArrayPropertyData apd:
                for (int i = 0; i < apd.Value.Length; i++)
                    AddPropertyNode(apd.Value[i], asset, index: i);
                break;
        }
    }

    private void AddPropertyNode(
        UAssetAPI.PropertyTypes.Objects.PropertyData prop,
        UAssetAPI.UAsset asset,
        int index = -1)
    {
        if (prop == null) return;
        string name = index >= 0 ? $"[{index}]" : prop.Name.ToString();

        switch (prop.PropertyType.Value)
        {
            case "StructProperty":
            case "ClothLODData":
                var spd = (StructPropertyData)prop;
                var structNode = new AssetTreeNode
                {
                    Label       = $"{name}  ({spd.StructType}  {spd.Value.Count})",
                    Pointer     = spd,
                    NodeType    = AssetTreeNodeType.PropertyStruct,
                    ExportIndex = ExportIndex,
                };
                Children.Add(structNode);
                break;

            case "ArrayProperty":
            case "SetProperty":
                var apd = (ArrayPropertyData)prop;
                var arrNode = new AssetTreeNode
                {
                    Label       = $"{name}  [{apd.Value.Length}]",
                    Pointer     = apd,
                    NodeType    = AssetTreeNodeType.PropertyArray,
                    ExportIndex = ExportIndex,
                };
                Children.Add(arrNode);
                break;

            case "MapProperty":
                var mpd = (MapPropertyData)prop;
                var mapNode = new AssetTreeNode
                {
                    Label       = $"{name}  ({{{mpd.Value.Keys.Count}}})",
                    Pointer     = mpd,
                    NodeType    = AssetTreeNodeType.PropertyMap,
                    ExportIndex = ExportIndex,
                };
                Children.Add(mapNode);
                break;

            // All leaf / inline types — don't add tree nodes; they show in the grid.
            default:
                break;
        }
    }
}
