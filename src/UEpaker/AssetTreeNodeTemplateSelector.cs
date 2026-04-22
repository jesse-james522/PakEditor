using System.Windows;
using System.Windows.Controls;

namespace UEpaker;

public class AssetTreeNodeTemplateSelector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is AssetTreeNode node && container is FrameworkElement fe)
        {
            var key = node.IsFolder ? "FolderTemplate" : "FileTemplate";
            return fe.FindResource(key) as DataTemplate;
        }
        return base.SelectTemplate(item, container);
    }
}
