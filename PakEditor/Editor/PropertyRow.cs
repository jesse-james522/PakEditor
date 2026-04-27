using System;
using System.Globalization;
using UAssetAPI;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace PakEditor.Editor;

/// <summary>
/// One row in the property DataGrid.  Mirrors UAssetGUI's per-row approach:
/// a fixed set of string value slots (V0–V5) that are filled depending on
/// property type, plus tooltips describing what each slot means.
///
/// Edits are written back to <see cref="Source"/> via <see cref="Commit"/>.
/// </summary>
public class PropertyRow : ViewModelBase
{
    // ── Metadata (read-only in the grid) ─────────────────────────────────────
    public PropertyData Source   { get; init; } = null!;
    public string       Name     { get; init; } = string.Empty;
    public string       TypeName { get; init; } = string.Empty;
    /// <summary>Extra type hint (e.g. struct type name, array element type).</summary>
    public string       Extra    { get; init; } = string.Empty;

    // ── Value slots ───────────────────────────────────────────────────────────
    private string _v0 = string.Empty;
    private string _v1 = string.Empty;
    private string _v2 = string.Empty;
    private string _v3 = string.Empty;
    private string _v4 = string.Empty;
    private string _v5 = string.Empty;

    public string V0 { get => _v0; set { if (SetProperty(ref _v0, value)) IsDirty = true; } }
    public string V1 { get => _v1; set { if (SetProperty(ref _v1, value)) IsDirty = true; } }
    public string V2 { get => _v2; set { if (SetProperty(ref _v2, value)) IsDirty = true; } }
    public string V3 { get => _v3; set { if (SetProperty(ref _v3, value)) IsDirty = true; } }
    public string V4 { get => _v4; set { if (SetProperty(ref _v4, value)) IsDirty = true; } }
    public string V5 { get => _v5; set { if (SetProperty(ref _v5, value)) IsDirty = true; } }

    // ── Tooltips (what each V slot means) ────────────────────────────────────
    public string T0 { get; private set; } = string.Empty;
    public string T1 { get; private set; } = string.Empty;
    public string T2 { get; private set; } = string.Empty;
    public string T3 { get; private set; } = string.Empty;
    public string T4 { get; private set; } = string.Empty;
    public string T5 { get; private set; } = string.Empty;

    // ── State ─────────────────────────────────────────────────────────────────
    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    public static PropertyRow FromPropertyData(PropertyData pd, UAsset asset, int arrayIndex = -1)
    {
        string name = arrayIndex >= 0 ? $"[{arrayIndex}]" : pd.Name.ToString();
        string typeName = pd.PropertyType.ToString();

        var row = new PropertyRow
        {
            Source   = pd,
            Name     = name,
            TypeName = typeName,
            Extra    = GetExtra(pd),
        };

        row.PopulateValues(pd, asset);
        row.IsDirty = false; // reset after initial population
        return row;
    }

    private static string GetExtra(PropertyData pd)
    {
        try
        {
            return pd.PropertyType.Value switch
            {
                "StructProperty" or "ClothLODData"
                    => (pd as StructPropertyData)?.StructType?.ToString() ?? string.Empty,
                "ArrayProperty" or "SetProperty"
                    => (pd as ArrayPropertyData)?.ArrayType?.ToString() ?? string.Empty,
                "EnumProperty"
                    => (pd as EnumPropertyData)?.EnumType?.ToString() ?? string.Empty,
                "ByteProperty"
                    => (pd as BytePropertyData)?.GetEnumBase()?.ToString() ?? string.Empty,
                _ => string.Empty,
            };
        }
        catch { return string.Empty; }
    }

    // ── Populate ──────────────────────────────────────────────────────────────

    private void PopulateValues(PropertyData pd, UAsset asset)
    {
        try { PopulateValuesInner(pd, asset); }
        catch (Exception ex) { _v0 = $"[{ex.GetType().Name}] {ex.Message}"; }
    }

    private void PopulateValuesInner(PropertyData pd, UAsset asset)
    {
        switch (pd.PropertyType.Value)
        {
            // ── Primitives ───────────────────────────────────────────────────
            case "BoolProperty":
                Set(0, ((BoolPropertyData)pd).Value.ToString()); break;

            case "IntProperty":
                Set(0, ((IntPropertyData)pd).Value.ToString()); break;
            case "Int8Property":
                Set(0, ((Int8PropertyData)pd).Value.ToString()); break;
            case "Int16Property":
                Set(0, ((Int16PropertyData)pd).Value.ToString()); break;
            case "Int64Property":
                Set(0, ((Int64PropertyData)pd).Value.ToString()); break;
            case "UInt16Property":
                Set(0, ((UInt16PropertyData)pd).Value.ToString()); break;
            case "UInt32Property":
                Set(0, ((UInt32PropertyData)pd).Value.ToString()); break;
            case "UInt64Property":
                Set(0, ((UInt64PropertyData)pd).Value.ToString()); break;

            case "FloatProperty":
                Set(0, ((FloatPropertyData)pd).Value.ToString("G", CultureInfo.InvariantCulture)); break;
            case "DoubleProperty":
                Set(0, ((DoublePropertyData)pd).Value.ToString("G", CultureInfo.InvariantCulture)); break;

            // ── Strings ──────────────────────────────────────────────────────
            case "StrProperty":
                Set(0, ((StrPropertyData)pd).Value?.ToString() ?? FString.NullCase); break;
            case "NameProperty":
                Set(0, ((NamePropertyData)pd).Value?.ToString() ?? FString.NullCase); break;
            case "TextProperty":
                PopulateText((TextPropertyData)pd); break;

            // ── Enum / Byte ───────────────────────────────────────────────────
            case "EnumProperty":
                var ep = (EnumPropertyData)pd;
                Set(0, ep.Value?.ToString() ?? FString.NullCase,  "Value"); break;

            case "ByteProperty":
                var bp = (BytePropertyData)pd;
                if (bp.ByteType == BytePropertyType.Byte)
                    Set(0, bp.Value.ToString(), "Byte");
                else
                    Set(0, bp.GetEnumFull()?.ToString() ?? FString.NullCase, "EnumValue");
                break;

            // ── Object references ────────────────────────────────────────────
            case "ObjectProperty":
            case "WeakObjectProperty":
            case "InterfaceProperty":
                var objP = (ObjectPropertyData)pd;
                int idx = objP.Value?.Index ?? 0;
                Set(0, idx.ToString(), "Index");
                if (idx != 0)
                {
                    string resolved = idx > 0
                        ? asset.Exports[idx - 1].ObjectName.ToString()
                        : asset.Imports[-idx - 1].ObjectName.ToString();
                    Set(1, resolved, "Resolved Name");
                }
                break;

            case "SoftObjectProperty":
                var sop = (SoftObjectPropertyData)pd;
                Set(0, sop.Value.AssetPath.PackageName?.ToString() ?? FString.NullCase, "PackageName");
                Set(1, sop.Value.AssetPath.AssetName?.ToString()   ?? FString.NullCase, "AssetName");
                Set(2, sop.Value.SubPathString?.ToString()          ?? FString.NullCase, "SubPath");
                break;

            // ── Vectors / Math ────────────────────────────────────────────────
            case "Vector":
                var v3 = (VectorPropertyData)pd;
                Set(0, G(v3.Value.X), "X");
                Set(1, G(v3.Value.Y), "Y");
                Set(2, G(v3.Value.Z), "Z");
                break;
            case "Vector2D":
                var v2 = (Vector2DPropertyData)pd;
                Set(0, G(v2.Value.X), "X");
                Set(1, G(v2.Value.Y), "Y");
                break;
            case "Vector4":
                var v4 = (Vector4PropertyData)pd;
                Set(0, G(v4.Value.X), "X");
                Set(1, G(v4.Value.Y), "Y");
                Set(2, G(v4.Value.Z), "Z");
                Set(3, G(v4.Value.W), "W");
                break;
            case "Rotator":
                var rot = (RotatorPropertyData)pd;
                Set(0, G(rot.Value.Pitch), "Pitch");
                Set(1, G(rot.Value.Yaw),   "Yaw");
                Set(2, G(rot.Value.Roll),  "Roll");
                break;
            case "Quat":
                var q = (QuatPropertyData)pd;
                Set(0, G(q.Value.X), "X");
                Set(1, G(q.Value.Y), "Y");
                Set(2, G(q.Value.Z), "Z");
                Set(3, G(q.Value.W), "W");
                break;

            // ── Colors ───────────────────────────────────────────────────────
            case "LinearColor":
                var lc = (LinearColorPropertyData)pd;
                Set(0, G(lc.Value.R), "R");
                Set(1, G(lc.Value.G), "G");
                Set(2, G(lc.Value.B), "B");
                Set(3, G(lc.Value.A), "A");
                break;
            case "Color":
                var c = (ColorPropertyData)pd;
                Set(0, c.Value.R.ToString(), "R");
                Set(1, c.Value.G.ToString(), "G");
                Set(2, c.Value.B.ToString(), "B");
                Set(3, c.Value.A.ToString(), "A");
                break;

            // ── Containers (shown in tree, summary row only) ─────────────────
            case "StructProperty":
            case "ClothLODData":
                Set(0, $"({((StructPropertyData)pd).Value.Count} props)", "Count"); break;
            case "ArrayProperty":
            case "SetProperty":
                Set(0, $"[{((ArrayPropertyData)pd).Value.Length}]", "Count"); break;
            case "MapProperty":
                Set(0, $"{{{((MapPropertyData)pd).Value.Keys.Count}}}", "Count"); break;

            // ── RichCurveKey ──────────────────────────────────────────────────
            case "RichCurveKey":
                var rck = (RichCurveKeyPropertyData)pd;
                Set(0, rck.Value.InterpMode.ToString(),                         "InterpMode");
                Set(1, rck.Value.TangentMode.ToString(),                        "TangentMode");
                Set(2, G(rck.Value.Time),                                       "Time");
                Set(3, G(rck.Value.Value),                                      "Value");
                Set(4, G(rck.Value.ArriveTangent),                              "ArriveTangent");
                Set(5, G(rck.Value.LeaveTangent),                               "LeaveTangent");
                break;

            // ── Unknown / raw ─────────────────────────────────────────────────
            default:
                if (pd is UnknownPropertyData unk)
                    Set(0, BitConverter.ToString(unk.Value).Replace("-", " "), "Raw Bytes");
                else
                    Set(0, pd.RawValue?.ToString() ?? string.Empty);
                break;
        }
    }

    private void PopulateText(TextPropertyData td)
    {
        Set(0, td.HistoryType.ToString(), "HistoryType");
        switch (td.HistoryType)
        {
            case TextHistoryType.None:
                Set(1, td.CultureInvariantString?.ToString() ?? FString.NullCase, "CultureInvariantString");
                break;
            case TextHistoryType.Base:
                Set(1, td.Namespace?.ToString()              ?? FString.NullCase, "Namespace");
                Set(2, td.Value?.ToString()                  ?? FString.NullCase, "Key");
                Set(3, td.CultureInvariantString?.ToString() ?? FString.NullCase, "CultureInvariantString");
                break;
            case TextHistoryType.RawText:
                Set(1, td.Value?.ToString() ?? FString.NullCase, "Value");
                break;
            case TextHistoryType.StringTableEntry:
                Set(1, td.TableId?.ToString() ?? FString.NullCase, "TableId");
                Set(2, td.Value?.ToString()    ?? FString.NullCase, "Key");
                break;
        }
    }

    // ── Write-back ────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse the current V0–V5 strings and write them back to <see cref="Source"/>.
    /// Returns true if any value changed.
    /// </summary>
    public bool Commit(UAsset asset)
    {
        if (!IsDirty) return false;
        try   { ApplyValues(asset); }
        catch { /* leave dirty so user can see something is wrong */ return false; }
        IsDirty = false;
        return true;
    }

    private void ApplyValues(UAsset asset)
    {
        switch (Source.PropertyType.Value)
        {
            case "BoolProperty":
                ((BoolPropertyData)Source).Value = bool.Parse(_v0); break;

            case "IntProperty":
                ((IntPropertyData)Source).Value   = int.Parse(_v0);   break;
            case "Int8Property":
                ((Int8PropertyData)Source).Value  = sbyte.Parse(_v0); break;
            case "Int16Property":
                ((Int16PropertyData)Source).Value = short.Parse(_v0); break;
            case "Int64Property":
                ((Int64PropertyData)Source).Value = long.Parse(_v0);  break;
            case "UInt16Property":
                ((UInt16PropertyData)Source).Value= ushort.Parse(_v0);break;
            case "UInt32Property":
                ((UInt32PropertyData)Source).Value= uint.Parse(_v0);  break;
            case "UInt64Property":
                ((UInt64PropertyData)Source).Value= ulong.Parse(_v0); break;

            case "FloatProperty":
                ((FloatPropertyData)Source).Value  = float.Parse(_v0,  CultureInfo.InvariantCulture); break;
            case "DoubleProperty":
                ((DoublePropertyData)Source).Value = double.Parse(_v0, CultureInfo.InvariantCulture); break;

            case "StrProperty":
                ((StrPropertyData)Source).Value   = new FString(_v0); break;
            case "NameProperty":
                ((NamePropertyData)Source).Value  = FName.FromString(asset, _v0); break;

            case "EnumProperty":
                ((EnumPropertyData)Source).Value  = FName.FromString(asset, _v0); break;
            case "ByteProperty":
                var bp = (BytePropertyData)Source;
                if (bp.ByteType == BytePropertyType.Byte)
                    bp.Value = byte.Parse(_v0);
                else
                    bp.EnumValue = FName.FromString(asset, _v0);
                break;

            case "ObjectProperty":
            case "WeakObjectProperty":
            case "InterfaceProperty":
                ((ObjectPropertyData)Source).Value = new FPackageIndex(int.Parse(_v0)); break;

            case "Vector":
                var v3s = (VectorPropertyData)Source;
                v3s.Value = new UAssetAPI.UnrealTypes.FVector(
                    double.Parse(_v0, CultureInfo.InvariantCulture),
                    double.Parse(_v1, CultureInfo.InvariantCulture),
                    double.Parse(_v2, CultureInfo.InvariantCulture));
                break;
            case "Vector2D":
                var v2s = (Vector2DPropertyData)Source;
                v2s.Value = new UAssetAPI.UnrealTypes.FVector2D(
                    double.Parse(_v0, CultureInfo.InvariantCulture),
                    double.Parse(_v1, CultureInfo.InvariantCulture));
                break;
            case "Vector4":
                var v4s = (Vector4PropertyData)Source;
                v4s.Value = new UAssetAPI.UnrealTypes.FVector4(
                    double.Parse(_v0, CultureInfo.InvariantCulture),
                    double.Parse(_v1, CultureInfo.InvariantCulture),
                    double.Parse(_v2, CultureInfo.InvariantCulture),
                    double.Parse(_v3, CultureInfo.InvariantCulture));
                break;
            case "Rotator":
                var rs = (RotatorPropertyData)Source;
                rs.Value = new UAssetAPI.UnrealTypes.FRotator(
                    double.Parse(_v0, CultureInfo.InvariantCulture),
                    double.Parse(_v1, CultureInfo.InvariantCulture),
                    double.Parse(_v2, CultureInfo.InvariantCulture));
                break;
            case "Quat":
                var qs = (QuatPropertyData)Source;
                qs.Value = new UAssetAPI.UnrealTypes.FQuat(
                    double.Parse(_v0, CultureInfo.InvariantCulture),
                    double.Parse(_v1, CultureInfo.InvariantCulture),
                    double.Parse(_v2, CultureInfo.InvariantCulture),
                    double.Parse(_v3, CultureInfo.InvariantCulture));
                break;
            case "LinearColor":
                var lcs = (LinearColorPropertyData)Source;
                lcs.Value = new UAssetAPI.UnrealTypes.FLinearColor(
                    float.Parse(_v0, CultureInfo.InvariantCulture),
                    float.Parse(_v1, CultureInfo.InvariantCulture),
                    float.Parse(_v2, CultureInfo.InvariantCulture),
                    float.Parse(_v3, CultureInfo.InvariantCulture));
                break;
            case "Color":
                var cs = (ColorPropertyData)Source;
                cs.Value = System.Drawing.Color.FromArgb(
                    byte.Parse(_v3), byte.Parse(_v0),
                    byte.Parse(_v1), byte.Parse(_v2));
                break;
        }
    }

    // ── Utilities ──────────────────────────────────────────────────────────────
    private void Set(int slot, string value, string tooltip = "")
    {
        switch (slot)
        {
            case 0: _v0 = value; T0 = tooltip; break;
            case 1: _v1 = value; T1 = tooltip; break;
            case 2: _v2 = value; T2 = tooltip; break;
            case 3: _v3 = value; T3 = tooltip; break;
            case 4: _v4 = value; T4 = tooltip; break;
            case 5: _v5 = value; T5 = tooltip; break;
        }
    }

    private static string G(double v) => v.ToString("G", CultureInfo.InvariantCulture);
    private static string G(float  v) => v.ToString("G", CultureInfo.InvariantCulture);
}
