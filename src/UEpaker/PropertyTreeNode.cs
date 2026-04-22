using System.Collections.ObjectModel;

namespace UEpaker;

public class PropertyTreeNode
{
    public string Label { get; }
    public string TypeName { get; }
    public string ValueText { get; }
    public bool HasChildren => Children.Count > 0;
    public ObservableCollection<PropertyTreeNode> Children { get; } = new();

    public PropertyTreeNode(string label, string typeName, string valueText)
    {
        Label = label;
        TypeName = typeName;
        ValueText = valueText;
    }

    public string DisplayValue => string.IsNullOrEmpty(ValueText) ? string.Empty : ValueText;
}
