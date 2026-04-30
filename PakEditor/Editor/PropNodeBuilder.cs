using System;
using System.Collections.Generic;
using System.Globalization;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace PakEditor.Editor;

public static class PropNodeBuilder
{
    /// <summary>
    /// Returns a "Table Info (N)" container node whose children are the named DataTable rows.
    /// </summary>
    public static PropNode BuildDataTableRows(UAssetAPI.ExportTypes.UDataTable table, UAsset asset, Action onDirty)
    {
        var tableNode = new PropNode
        {
            Name     = "Table Info",
            TypeName = $"({table.Data.Count})",
            IsLeaf   = false,
        };

        foreach (var row in table.Data)
        {
            var rowNode = new PropNode
            {
                Name     = row.Name.ToString(),
                TypeName = row.StructType?.ToString() ?? string.Empty,
                IsLeaf   = false,
            };
            if (row.Value is not null)
            {
                foreach (var p in row.Value)
                {
                    if (p is null) continue;
                    try
                    {
                        var propNode = Make(p, asset);
                        propNode.WireChildren(onDirty);
                        rowNode.Children.Add(propNode);
                    }
                    catch (Exception ex)
                    {
                        rowNode.Children.Add(new PropNode
                        {
                            Name     = p.Name.ToString(),
                            TypeName = p.PropertyType.Value,
                            IsLeaf   = true,
                            Value    = $"[ERROR] {ex.Message}",
                            SourcePD = p,
                        });
                    }
                }
            }
            tableNode.Children.Add(rowNode);
        }

        return tableNode;
    }

    public static List<PropNode> BuildExport(NormalExport ne, UAsset asset, Action onDirty)
    {
        var nodes = new List<PropNode>();
        foreach (var pd in ne.Data)
        {
            if (pd is null) continue;
            try
            {
                var node = Make(pd, asset);
                node.WireChildren(onDirty);
                nodes.Add(node);
            }
            catch (Exception ex)
            {
                nodes.Add(new PropNode
                {
                    Name     = pd.Name.ToString(),
                    TypeName = pd.PropertyType.Value,
                    IsLeaf   = true,
                    Value    = $"[ERROR] {ex.Message}",
                    SourcePD = pd,
                });
            }
        }
        return nodes;
    }

    // ── Node builders ─────────────────────────────────────────────────────────

    private static PropNode Make(PropertyData pd, UAsset asset, int arrayIdx = -1)
    {
        var name = arrayIdx >= 0 ? $"[{arrayIdx}]" : pd.Name.ToString();

        return pd.PropertyType.Value switch
        {
            "StructProperty" or "ClothLODData"
                => MakeStruct((StructPropertyData)pd, name, asset),
            "ArrayProperty" or "SetProperty"
                => MakeArray((ArrayPropertyData)pd, name, asset),
            "MapProperty"
                => MakeMap((MapPropertyData)pd, name),
            "Vector"
                => MakeVectorLeaf((VectorPropertyData)pd, name),
            "Vector2D"
                => MakeVector2DLeaf((Vector2DPropertyData)pd, name),
            "Vector4"
                => MakeVector4Leaf((Vector4PropertyData)pd, name),
            "Rotator"
                => MakeRotatorLeaf((RotatorPropertyData)pd, name),
            "Quat"
                => MakeQuatLeaf((QuatPropertyData)pd, name),
            "LinearColor"
                => MakeLinearColorLeaf((LinearColorPropertyData)pd, name),
            "Color"
                => MakeColorLeaf((ColorPropertyData)pd, name),
            _
                => MakeLeaf(pd, name, asset),
        };
    }

    // ── Container types ───────────────────────────────────────────────────────

    private static PropNode MakeStruct(StructPropertyData spd, string name, UAsset asset)
    {
        var n = new PropNode
        {
            Name       = name,
            TypeName   = "StructProperty",
            Variant    = spd.StructType?.ToString() ?? string.Empty,
            IsLeaf     = false,
            SourcePD   = spd,
            ArrayIndex = spd.ArrayIndex,
            Offset     = spd.Offset,
            IsZero     = spd.IsZero,
        };
        foreach (var p in spd.Value)
            if (p is not null) n.Children.Add(Make(p, asset));
        return n;
    }

    private static PropNode MakeArray(ArrayPropertyData apd, string name, UAsset asset)
    {
        var n = new PropNode
        {
            Name       = name,
            TypeName   = "ArrayProperty",
            Variant    = apd.ArrayType?.ToString() ?? string.Empty,
            IsLeaf     = false,
            SourcePD   = apd,
            ArrayIndex = apd.ArrayIndex,
            Offset     = apd.Offset,
            IsZero     = apd.IsZero,
        };
        for (int i = 0; i < apd.Value.Length; i++)
        {
            if (apd.Value[i] is null) continue;
            var child = Make(apd.Value[i], asset, i);
            child.ParentArray = apd;
            n.Children.Add(child);
        }
        return n;
    }

    private static PropNode MakeMap(MapPropertyData mpd, string name)
        => new()
        {
            Name       = name,
            TypeName   = "MapProperty",
            Variant    = "Map",
            IsLeaf     = true,
            Value      = $"{mpd.Value.Keys.Count} entries",
            SourcePD   = mpd,
            ArrayIndex = mpd.ArrayIndex,
            Offset     = mpd.Offset,
            IsZero     = mpd.IsZero,
        };

    // ── Multi-value leaf types (inline V1-V4, no child nodes) ─────────────────

    private static string G(double v)  => v.ToString("G", CultureInfo.InvariantCulture);
    private static string Gf(float v)  => v.ToString("G", CultureInfo.InvariantCulture);

    private static PropNode MakeVectorLeaf(VectorPropertyData v, string name)
        => new()
        {
            Name       = name,
            TypeName   = "Vector",
            Variant    = string.Empty,
            IsLeaf     = true,
            Value      = G(v.Value.X),
            V2         = G(v.Value.Y),
            V3         = G(v.Value.Z),
            WriteFn    = s => v.Value = new FVector(double.Parse(s, CultureInfo.InvariantCulture), v.Value.Y, v.Value.Z),
            WriteFn2   = s => v.Value = new FVector(v.Value.X, double.Parse(s, CultureInfo.InvariantCulture), v.Value.Z),
            WriteFn3   = s => v.Value = new FVector(v.Value.X, v.Value.Y, double.Parse(s, CultureInfo.InvariantCulture)),
            SourcePD   = v,
            ArrayIndex = v.ArrayIndex,
            Offset     = v.Offset,
            IsZero     = v.IsZero,
        };

    private static PropNode MakeVector2DLeaf(Vector2DPropertyData v, string name)
        => new()
        {
            Name       = name,
            TypeName   = "Vector2D",
            Variant    = string.Empty,
            IsLeaf     = true,
            Value      = G(v.Value.X),
            V2         = G(v.Value.Y),
            WriteFn    = s => v.Value = new FVector2D(double.Parse(s, CultureInfo.InvariantCulture), v.Value.Y),
            WriteFn2   = s => v.Value = new FVector2D(v.Value.X, double.Parse(s, CultureInfo.InvariantCulture)),
            SourcePD   = v,
            ArrayIndex = v.ArrayIndex,
            Offset     = v.Offset,
            IsZero     = v.IsZero,
        };

    private static PropNode MakeVector4Leaf(Vector4PropertyData v, string name)
        => new()
        {
            Name       = name,
            TypeName   = "Vector4",
            Variant    = string.Empty,
            IsLeaf     = true,
            Value      = G(v.Value.X),
            V2         = G(v.Value.Y),
            V3         = G(v.Value.Z),
            V4         = G(v.Value.W),
            WriteFn    = s => v.Value = new FVector4(double.Parse(s, CultureInfo.InvariantCulture), v.Value.Y, v.Value.Z, v.Value.W),
            WriteFn2   = s => v.Value = new FVector4(v.Value.X, double.Parse(s, CultureInfo.InvariantCulture), v.Value.Z, v.Value.W),
            WriteFn3   = s => v.Value = new FVector4(v.Value.X, v.Value.Y, double.Parse(s, CultureInfo.InvariantCulture), v.Value.W),
            WriteFn4   = s => v.Value = new FVector4(v.Value.X, v.Value.Y, v.Value.Z, double.Parse(s, CultureInfo.InvariantCulture)),
            SourcePD   = v,
            ArrayIndex = v.ArrayIndex,
            Offset     = v.Offset,
            IsZero     = v.IsZero,
        };

    private static PropNode MakeRotatorLeaf(RotatorPropertyData r, string name)
        => new()
        {
            Name       = name,
            TypeName   = "Rotator",
            Variant    = string.Empty,
            IsLeaf     = true,
            // UAssetGUI order: Roll, Pitch, Yaw
            Value      = G(r.Value.Roll),
            V2         = G(r.Value.Pitch),
            V3         = G(r.Value.Yaw),
            WriteFn    = s => r.Value = new FRotator(r.Value.Pitch, r.Value.Yaw, double.Parse(s, CultureInfo.InvariantCulture)),
            WriteFn2   = s => r.Value = new FRotator(double.Parse(s, CultureInfo.InvariantCulture), r.Value.Yaw, r.Value.Roll),
            WriteFn3   = s => r.Value = new FRotator(r.Value.Pitch, double.Parse(s, CultureInfo.InvariantCulture), r.Value.Roll),
            SourcePD   = r,
            ArrayIndex = r.ArrayIndex,
            Offset     = r.Offset,
            IsZero     = r.IsZero,
        };

    private static PropNode MakeQuatLeaf(QuatPropertyData q, string name)
        => new()
        {
            Name       = name,
            TypeName   = "Quat",
            Variant    = string.Empty,
            IsLeaf     = true,
            Value      = G(q.Value.X),
            V2         = G(q.Value.Y),
            V3         = G(q.Value.Z),
            V4         = G(q.Value.W),
            WriteFn    = s => q.Value = new FQuat(double.Parse(s, CultureInfo.InvariantCulture), q.Value.Y, q.Value.Z, q.Value.W),
            WriteFn2   = s => q.Value = new FQuat(q.Value.X, double.Parse(s, CultureInfo.InvariantCulture), q.Value.Z, q.Value.W),
            WriteFn3   = s => q.Value = new FQuat(q.Value.X, q.Value.Y, double.Parse(s, CultureInfo.InvariantCulture), q.Value.W),
            WriteFn4   = s => q.Value = new FQuat(q.Value.X, q.Value.Y, q.Value.Z, double.Parse(s, CultureInfo.InvariantCulture)),
            SourcePD   = q,
            ArrayIndex = q.ArrayIndex,
            Offset     = q.Offset,
            IsZero     = q.IsZero,
        };

    private static PropNode MakeLinearColorLeaf(LinearColorPropertyData lc, string name)
        => new()
        {
            Name       = name,
            TypeName   = "LinearColor",
            Variant    = string.Empty,
            IsLeaf     = true,
            Value      = Gf(lc.Value.R),
            V2         = Gf(lc.Value.G),
            V3         = Gf(lc.Value.B),
            V4         = Gf(lc.Value.A),
            WriteFn    = s => lc.Value = new FLinearColor(float.Parse(s, CultureInfo.InvariantCulture), lc.Value.G, lc.Value.B, lc.Value.A),
            WriteFn2   = s => lc.Value = new FLinearColor(lc.Value.R, float.Parse(s, CultureInfo.InvariantCulture), lc.Value.B, lc.Value.A),
            WriteFn3   = s => lc.Value = new FLinearColor(lc.Value.R, lc.Value.G, float.Parse(s, CultureInfo.InvariantCulture), lc.Value.A),
            WriteFn4   = s => lc.Value = new FLinearColor(lc.Value.R, lc.Value.G, lc.Value.B, float.Parse(s, CultureInfo.InvariantCulture)),
            SourcePD   = lc,
            ArrayIndex = lc.ArrayIndex,
            Offset     = lc.Offset,
            IsZero     = lc.IsZero,
        };

    private static PropNode MakeColorLeaf(ColorPropertyData c, string name)
        => new()
        {
            Name       = name,
            TypeName   = "Color",
            Variant    = string.Empty,
            IsLeaf     = true,
            Value      = c.Value.R.ToString(),
            V2         = c.Value.G.ToString(),
            V3         = c.Value.B.ToString(),
            V4         = c.Value.A.ToString(),
            WriteFn    = s => c.Value = System.Drawing.Color.FromArgb(c.Value.A, byte.Parse(s), c.Value.G, c.Value.B),
            WriteFn2   = s => c.Value = System.Drawing.Color.FromArgb(c.Value.A, c.Value.R, byte.Parse(s), c.Value.B),
            WriteFn3   = s => c.Value = System.Drawing.Color.FromArgb(c.Value.A, c.Value.R, c.Value.G, byte.Parse(s)),
            WriteFn4   = s => c.Value = System.Drawing.Color.FromArgb(byte.Parse(s), c.Value.R, c.Value.G, c.Value.B),
            SourcePD   = c,
            ArrayIndex = c.ArrayIndex,
            Offset     = c.Offset,
            IsZero     = c.IsZero,
        };

    // ── Scalar leaf types ──────────────────────────────────────────────────────

    private static PropNode MakeLeaf(PropertyData pd, string name, UAsset asset)
    {
        var (value, writeFn, variant) = GetValueWriter(pd, asset);
        return new PropNode
        {
            Name       = name,
            TypeName   = pd.PropertyType.Value,
            Variant    = variant,
            IsLeaf     = true,
            Value      = value,
            WriteFn    = writeFn,
            SourcePD   = pd,
            ArrayIndex = pd.ArrayIndex,
            Offset     = pd.Offset,
            IsZero     = pd.IsZero,
        };
    }

    // ── Value/writer extraction ────────────────────────────────────────────────

    private static (string value, Action<string>? write, string variant) GetValueWriter(PropertyData pd, UAsset asset)
    {
        static string Gdbl(double v) => v.ToString("G", CultureInfo.InvariantCulture);
        static string Gflt(float  v) => v.ToString("G", CultureInfo.InvariantCulture);

        switch (pd.PropertyType.Value)
        {
            case "BoolProperty":
            {
                var p = (BoolPropertyData)pd;
                return (p.Value.ToString().ToLowerInvariant(), s => p.Value = bool.Parse(s), string.Empty);
            }
            case "IntProperty":    { var p = (IntPropertyData)pd;    return (p.Value.ToString(), s => p.Value = int.Parse(s),    string.Empty); }
            case "Int8Property":   { var p = (Int8PropertyData)pd;   return (p.Value.ToString(), s => p.Value = sbyte.Parse(s),  string.Empty); }
            case "Int16Property":  { var p = (Int16PropertyData)pd;  return (p.Value.ToString(), s => p.Value = short.Parse(s),  string.Empty); }
            case "Int64Property":  { var p = (Int64PropertyData)pd;  return (p.Value.ToString(), s => p.Value = long.Parse(s),   string.Empty); }
            case "UInt16Property": { var p = (UInt16PropertyData)pd; return (p.Value.ToString(), s => p.Value = ushort.Parse(s), string.Empty); }
            case "UInt32Property": { var p = (UInt32PropertyData)pd; return (p.Value.ToString(), s => p.Value = uint.Parse(s),   string.Empty); }
            case "UInt64Property": { var p = (UInt64PropertyData)pd; return (p.Value.ToString(), s => p.Value = ulong.Parse(s),  string.Empty); }
            case "FloatProperty":
            {
                var p = (FloatPropertyData)pd;
                return (Gflt(p.Value), s => p.Value = float.Parse(s, CultureInfo.InvariantCulture), string.Empty);
            }
            case "DoubleProperty":
            {
                var p = (DoublePropertyData)pd;
                return (Gdbl(p.Value), s => p.Value = double.Parse(s, CultureInfo.InvariantCulture), string.Empty);
            }
            case "StrProperty":
            {
                var p = (StrPropertyData)pd;
                var variant = p.Value?.Encoding?.HeaderName ?? string.Empty;
                return (p.Value?.ToString() ?? string.Empty, s => p.Value = new FString(s), variant);
            }
            case "NameProperty":
            {
                var p = (NamePropertyData)pd;
                return (p.Value?.ToString() ?? string.Empty, s => p.Value = FName.FromString(asset, s), string.Empty);
            }
            case "TextProperty":
            {
                var p = (TextPropertyData)pd;
                var variant = p.HistoryType.ToString();
                var display = p.CultureInvariantString?.ToString() ?? p.Value?.ToString() ?? string.Empty;
                return (display, s =>
                {
                    if (p.CultureInvariantString != null)
                        p.CultureInvariantString = new FString(s);
                    else
                        p.Value = new FString(s);
                }, variant);
            }
            case "EnumProperty":
            {
                var p = (EnumPropertyData)pd;
                var variant = p.EnumType?.ToString() ?? string.Empty;
                return (p.Value?.ToString() ?? string.Empty, s => p.Value = FName.FromString(asset, s), variant);
            }
            case "ByteProperty":
            {
                var p = (BytePropertyData)pd;
                var variant = p.GetEnumBase()?.Value.Value ?? string.Empty;
                if (p.ByteType == BytePropertyType.Byte)
                    return (p.Value.ToString(), s => p.Value = byte.Parse(s), variant);
                return (p.GetEnumFull()?.ToString() ?? string.Empty, s => p.EnumValue = FName.FromString(asset, s), variant);
            }
            case "ObjectProperty":
            case "WeakObjectProperty":
            case "InterfaceProperty":
            {
                var p   = (ObjectPropertyData)pd;
                var idx = p.Value?.Index ?? 0;
                string display;
                if (idx == 0)                        display = "0 (null)";
                else if (idx > 0 && idx - 1  < asset.Exports.Count) display = $"{idx} ({asset.Exports[idx - 1].ObjectName})";
                else if (idx < 0 && -idx - 1 < asset.Imports.Count) display = $"{idx} ({asset.Imports[-idx - 1].ObjectName})";
                else                                 display = idx.ToString();
                return (display, s => p.Value = new FPackageIndex(int.Parse(s.Split(' ')[0].Trim())), string.Empty);
            }
            case "SoftObjectProperty":
            {
                var p = (SoftObjectPropertyData)pd;
                var pkg   = p.Value.AssetPath.PackageName?.ToString() ?? string.Empty;
                var aName = p.Value.AssetPath.AssetName?.ToString()   ?? string.Empty;
                var display = aName.Length > 0 ? $"{pkg}.{aName}" : pkg;
                return (display, s =>
                {
                    var dot = s.LastIndexOf('.');
                    FName pkgFn, assetFn;
                    if (dot > 0)
                    {
                        pkgFn   = FName.FromString(asset, s[..dot]);
                        assetFn = FName.FromString(asset, s[(dot + 1)..]);
                    }
                    else
                    {
                        pkgFn   = FName.FromString(asset, s);
                        assetFn = p.Value.AssetPath.AssetName;
                    }
                    p.Value = new FSoftObjectPath(pkgFn, assetFn, p.Value.SubPathString);
                }, string.Empty);
            }
            case "RichCurveKey":
            {
                var p = (RichCurveKeyPropertyData)pd;
                return ($"t={Gdbl(p.Value.Time)}  v={Gdbl(p.Value.Value)}", null, string.Empty);
            }
            default:
                if (pd is UnknownPropertyData unk)
                    return (BitConverter.ToString(unk.Value).Replace("-", " "), null, string.Empty);
                return (pd.RawValue?.ToString() ?? string.Empty, null, string.Empty);
        }
    }
}
