using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;

namespace UEpaker;

public static class PropertyTreeBuilder
{
    public static IReadOnlyList<PropertyTreeNode> Build(UObject export)
    {
        var nodes = new List<PropertyTreeNode>();
        foreach (var tag in export.Properties)
            nodes.Add(FromTag(tag));
        return nodes;
    }

    private static PropertyTreeNode FromTag(FPropertyTag tag)
    {
        var label = tag.ArrayIndex > 0
            ? $"{tag.Name.Text}[{tag.ArrayIndex}]"
            : tag.Name.Text;
        return FromTagType(label, tag.PropertyType.Text, tag.TagData, tag.Tag);
    }

    private static PropertyTreeNode FromTagType(
        string label, string typeName, FPropertyTagData? tagData, FPropertyTagType? tag)
    {
        if (tag is null)
            return new PropertyTreeNode(label, ShortType(typeName), "<unreadable>");

        return tag switch
        {
            StructProperty s => BuildStruct(label, tagData, s),
            ArrayProperty  a => BuildArray(label, tagData, a),
            MapProperty    m => BuildMap(label, tagData, m),
            SetProperty    s => BuildSet(label, tagData, s),
            _                => new PropertyTreeNode(label, ShortType(typeName),
                                    tag.GenericValue?.ToString() ?? string.Empty),
        };
    }

    private static PropertyTreeNode BuildStruct(string label, FPropertyTagData? tagData, StructProperty prop)
    {
        var structName = tagData?.StructType ?? "Struct";
        var node = new PropertyTreeNode(label, structName, string.Empty);

        if (prop.Value?.StructType is FStructFallback fallback)
            foreach (var child in fallback.Properties)
                node.Children.Add(FromTag(child));
        else if (prop.Value?.StructType is not null)
            // Native struct (FVector, FLinearColor, etc.) — show via ToString
            node.Children.Add(new PropertyTreeNode("value", structName,
                prop.Value.StructType.ToString() ?? string.Empty));

        return node;
    }

    private static PropertyTreeNode BuildArray(string label, FPropertyTagData? tagData, ArrayProperty prop)
    {
        var items = prop.Value?.Properties;
        var node = new PropertyTreeNode(label, "Array", $"[{items?.Count ?? 0}]");

        if (items is not null)
        {
            var innerType = tagData?.InnerType ?? "element";
            var innerTagData = tagData?.InnerTypeData;
            for (var i = 0; i < items.Count; i++)
                node.Children.Add(FromTagType($"[{i}]", innerType, innerTagData, items[i]));
        }

        return node;
    }

    private static PropertyTreeNode BuildMap(string label, FPropertyTagData? tagData, MapProperty prop)
    {
        var pairs = prop.Value?.Properties;
        var node = new PropertyTreeNode(label, "Map", $"[{pairs?.Count ?? 0}]");

        if (pairs is not null)
        {
            var valueType = tagData?.ValueType ?? "value";
            var i = 0;
            foreach (var (k, v) in pairs)
            {
                var keyText = k.GenericValue?.ToString() ?? $"[{i}]";
                var pairNode = new PropertyTreeNode(keyText, "key", string.Empty);
                pairNode.Children.Add(FromTagType("value", valueType, null, v));
                node.Children.Add(pairNode);
                i++;
            }
        }

        return node;
    }

    private static PropertyTreeNode BuildSet(string label, FPropertyTagData? tagData, SetProperty prop)
    {
        var items = prop.Value?.Properties;
        var node = new PropertyTreeNode(label, "Set", $"[{items?.Count ?? 0}]");

        if (items is not null)
        {
            var innerType = tagData?.InnerType ?? "element";
            for (var i = 0; i < items.Count; i++)
                node.Children.Add(FromTagType($"[{i}]", innerType, null, items[i]));
        }

        return node;
    }

    private static string ShortType(string typeName) =>
        typeName.EndsWith("Property") ? typeName[..^8] : typeName;
}
