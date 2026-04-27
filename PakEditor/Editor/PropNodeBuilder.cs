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
            "ArrayProperty"  or "SetProperty"
                => MakeArray((ArrayPropertyData)pd, name, asset),
            "MapProperty"
                => MakeMap((MapPropertyData)pd, name),
            "Vector"
                => MakeVec3((VectorPropertyData)pd, name),
            "Vector2D"
                => MakeVec2((Vector2DPropertyData)pd, name),
            "Vector4"
                => MakeVec4((Vector4PropertyData)pd, name),
            "Rotator"
                => MakeRotator((RotatorPropertyData)pd, name),
            "Quat"
                => MakeQuat((QuatPropertyData)pd, name),
            "LinearColor"
                => MakeLinearColor((LinearColorPropertyData)pd, name),
            "Color"
                => MakeColor((ColorPropertyData)pd, name),
            _
                => MakeLeaf(pd, name, asset),
        };
    }

    private static PropNode MakeStruct(StructPropertyData spd, string name, UAsset asset)
    {
        var n = Container(name, spd.StructType?.ToString() ?? "Struct");
        foreach (var p in spd.Value)
            if (p is not null) n.Children.Add(Make(p, asset));
        return n;
    }

    private static PropNode MakeArray(ArrayPropertyData apd, string name, UAsset asset)
    {
        var n = Container(name, $"Array[{apd.Value.Length}]");
        for (int i = 0; i < apd.Value.Length; i++)
            if (apd.Value[i] is not null) n.Children.Add(Make(apd.Value[i], asset, i));
        return n;
    }

    private static PropNode MakeMap(MapPropertyData mpd, string name)
        => new() { Name = name, TypeName = $"Map{{{mpd.Value.Keys.Count}}}", IsLeaf = true,
                   Value = $"{mpd.Value.Keys.Count} entries" };

    private static PropNode MakeVec3(VectorPropertyData v, string name)
    {
        var n = Container(name, "Vector");
        n.Children.Add(F64("X", () => v.Value.X, x => v.Value = new FVector(x, v.Value.Y, v.Value.Z)));
        n.Children.Add(F64("Y", () => v.Value.Y, y => v.Value = new FVector(v.Value.X, y, v.Value.Z)));
        n.Children.Add(F64("Z", () => v.Value.Z, z => v.Value = new FVector(v.Value.X, v.Value.Y, z)));
        return n;
    }

    private static PropNode MakeVec2(Vector2DPropertyData v, string name)
    {
        var n = Container(name, "Vector2D");
        n.Children.Add(F64("X", () => v.Value.X, x => v.Value = new FVector2D(x, v.Value.Y)));
        n.Children.Add(F64("Y", () => v.Value.Y, y => v.Value = new FVector2D(v.Value.X, y)));
        return n;
    }

    private static PropNode MakeVec4(Vector4PropertyData v, string name)
    {
        var n = Container(name, "Vector4");
        n.Children.Add(F64("X", () => v.Value.X, x => v.Value = new FVector4(x, v.Value.Y, v.Value.Z, v.Value.W)));
        n.Children.Add(F64("Y", () => v.Value.Y, y => v.Value = new FVector4(v.Value.X, y, v.Value.Z, v.Value.W)));
        n.Children.Add(F64("Z", () => v.Value.Z, z => v.Value = new FVector4(v.Value.X, v.Value.Y, z, v.Value.W)));
        n.Children.Add(F64("W", () => v.Value.W, w => v.Value = new FVector4(v.Value.X, v.Value.Y, v.Value.Z, w)));
        return n;
    }

    private static PropNode MakeRotator(RotatorPropertyData r, string name)
    {
        var n = Container(name, "Rotator");
        n.Children.Add(F64("Pitch", () => r.Value.Pitch, v => r.Value = new FRotator(v, r.Value.Yaw,   r.Value.Roll)));
        n.Children.Add(F64("Yaw",   () => r.Value.Yaw,   v => r.Value = new FRotator(r.Value.Pitch, v, r.Value.Roll)));
        n.Children.Add(F64("Roll",  () => r.Value.Roll,  v => r.Value = new FRotator(r.Value.Pitch, r.Value.Yaw, v)));
        return n;
    }

    private static PropNode MakeQuat(QuatPropertyData q, string name)
    {
        var n = Container(name, "Quat");
        n.Children.Add(F64("X", () => q.Value.X, v => q.Value = new FQuat(v, q.Value.Y, q.Value.Z, q.Value.W)));
        n.Children.Add(F64("Y", () => q.Value.Y, v => q.Value = new FQuat(q.Value.X, v, q.Value.Z, q.Value.W)));
        n.Children.Add(F64("Z", () => q.Value.Z, v => q.Value = new FQuat(q.Value.X, q.Value.Y, v, q.Value.W)));
        n.Children.Add(F64("W", () => q.Value.W, v => q.Value = new FQuat(q.Value.X, q.Value.Y, q.Value.Z, v)));
        return n;
    }

    private static PropNode MakeLinearColor(LinearColorPropertyData lc, string name)
    {
        var n = Container(name, "LinearColor");
        n.Children.Add(F32("R", () => lc.Value.R, v => lc.Value = new FLinearColor(v, lc.Value.G, lc.Value.B, lc.Value.A)));
        n.Children.Add(F32("G", () => lc.Value.G, v => lc.Value = new FLinearColor(lc.Value.R, v, lc.Value.B, lc.Value.A)));
        n.Children.Add(F32("B", () => lc.Value.B, v => lc.Value = new FLinearColor(lc.Value.R, lc.Value.G, v, lc.Value.A)));
        n.Children.Add(F32("A", () => lc.Value.A, v => lc.Value = new FLinearColor(lc.Value.R, lc.Value.G, lc.Value.B, v)));
        return n;
    }

    private static PropNode MakeColor(ColorPropertyData c, string name)
    {
        var n = Container(name, "Color");
        n.Children.Add(Byte("R", () => c.Value.R, v => c.Value = System.Drawing.Color.FromArgb(c.Value.A, v, c.Value.G, c.Value.B)));
        n.Children.Add(Byte("G", () => c.Value.G, v => c.Value = System.Drawing.Color.FromArgb(c.Value.A, c.Value.R, v, c.Value.B)));
        n.Children.Add(Byte("B", () => c.Value.B, v => c.Value = System.Drawing.Color.FromArgb(c.Value.A, c.Value.R, c.Value.G, v)));
        n.Children.Add(Byte("A", () => c.Value.A, v => c.Value = System.Drawing.Color.FromArgb(v, c.Value.R, c.Value.G, c.Value.B)));
        return n;
    }

    private static PropNode MakeLeaf(PropertyData pd, string name, UAsset asset)
    {
        var (value, writeFn) = GetValueWriter(pd, asset);
        return new PropNode
        {
            Name     = name,
            TypeName = pd.PropertyType.Value,
            IsLeaf   = true,
            Value    = value,
            WriteFn  = writeFn,
        };
    }

    // ── Value/writer extraction ───────────────────────────────────────────────

    private static (string value, Action<string>? write) GetValueWriter(PropertyData pd, UAsset asset)
    {
        static string G(double v)  => v.ToString("G", CultureInfo.InvariantCulture);
        static string Gf(float  v) => v.ToString("G", CultureInfo.InvariantCulture);

        switch (pd.PropertyType.Value)
        {
            case "BoolProperty":
            {
                var p = (BoolPropertyData)pd;
                return (p.Value.ToString().ToLowerInvariant(), s => p.Value = bool.Parse(s));
            }
            case "IntProperty":    { var p = (IntPropertyData)pd;    return (p.Value.ToString(), s => p.Value = int.Parse(s)); }
            case "Int8Property":   { var p = (Int8PropertyData)pd;   return (p.Value.ToString(), s => p.Value = sbyte.Parse(s)); }
            case "Int16Property":  { var p = (Int16PropertyData)pd;  return (p.Value.ToString(), s => p.Value = short.Parse(s)); }
            case "Int64Property":  { var p = (Int64PropertyData)pd;  return (p.Value.ToString(), s => p.Value = long.Parse(s)); }
            case "UInt16Property": { var p = (UInt16PropertyData)pd; return (p.Value.ToString(), s => p.Value = ushort.Parse(s)); }
            case "UInt32Property": { var p = (UInt32PropertyData)pd; return (p.Value.ToString(), s => p.Value = uint.Parse(s)); }
            case "UInt64Property": { var p = (UInt64PropertyData)pd; return (p.Value.ToString(), s => p.Value = ulong.Parse(s)); }
            case "FloatProperty":
            {
                var p = (FloatPropertyData)pd;
                return (Gf(p.Value), s => p.Value = float.Parse(s, CultureInfo.InvariantCulture));
            }
            case "DoubleProperty":
            {
                var p = (DoublePropertyData)pd;
                return (G(p.Value), s => p.Value = double.Parse(s, CultureInfo.InvariantCulture));
            }
            case "StrProperty":
            {
                var p = (StrPropertyData)pd;
                return (p.Value?.ToString() ?? string.Empty, s => p.Value = new FString(s));
            }
            case "NameProperty":
            {
                var p = (NamePropertyData)pd;
                return (p.Value?.ToString() ?? string.Empty, s => p.Value = FName.FromString(asset, s));
            }
            case "TextProperty":
            {
                var p = (TextPropertyData)pd;
                var display = p.Value?.ToString() ?? p.CultureInvariantString?.ToString() ?? string.Empty;
                return (display, null);
            }
            case "EnumProperty":
            {
                var p = (EnumPropertyData)pd;
                return (p.Value?.ToString() ?? string.Empty, s => p.Value = FName.FromString(asset, s));
            }
            case "ByteProperty":
            {
                var p = (BytePropertyData)pd;
                if (p.ByteType == BytePropertyType.Byte)
                    return (p.Value.ToString(), s => p.Value = byte.Parse(s));
                return (p.GetEnumFull()?.ToString() ?? string.Empty, s => p.EnumValue = FName.FromString(asset, s));
            }
            case "ObjectProperty":
            case "WeakObjectProperty":
            case "InterfaceProperty":
            {
                var p   = (ObjectPropertyData)pd;
                var idx = p.Value?.Index ?? 0;
                string display;
                if (idx == 0)                         display = "0 (null)";
                else if (idx > 0  && idx - 1   < asset.Exports.Count) display = $"{idx} ({asset.Exports[idx - 1].ObjectName})";
                else if (idx < 0  && -idx - 1  < asset.Imports.Count) display = $"{idx} ({asset.Imports[-idx - 1].ObjectName})";
                else                                  display = idx.ToString();
                return (display, s => p.Value = new FPackageIndex(int.Parse(s.Split(' ')[0].Trim())));
            }
            case "SoftObjectProperty":
            {
                var p = (SoftObjectPropertyData)pd;
                return (p.Value.AssetPath.PackageName?.ToString() ?? string.Empty, null);
            }
            case "RichCurveKey":
            {
                var p = (RichCurveKeyPropertyData)pd;
                return ($"t={G(p.Value.Time)}  v={G(p.Value.Value)}", null);
            }
            default:
                if (pd is UnknownPropertyData unk)
                    return (BitConverter.ToString(unk.Value).Replace("-", " "), null);
                return (pd.RawValue?.ToString() ?? string.Empty, null);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PropNode Container(string name, string typeName)
        => new() { Name = name, TypeName = typeName, IsLeaf = false };

    private static PropNode F64(string name, Func<double> get, Action<double> set)
        => new() { Name = name, TypeName = "float64", IsLeaf = true,
                   Value = get().ToString("G", CultureInfo.InvariantCulture),
                   WriteFn = s => set(double.Parse(s, CultureInfo.InvariantCulture)) };

    private static PropNode F32(string name, Func<float> get, Action<float> set)
        => new() { Name = name, TypeName = "float32", IsLeaf = true,
                   Value = get().ToString("G", CultureInfo.InvariantCulture),
                   WriteFn = s => set(float.Parse(s, CultureInfo.InvariantCulture)) };

    private static PropNode Byte(string name, Func<byte> get, Action<byte> set)
        => new() { Name = name, TypeName = "byte", IsLeaf = true,
                   Value = get().ToString(),
                   WriteFn = s => set(byte.Parse(s)) };
}
