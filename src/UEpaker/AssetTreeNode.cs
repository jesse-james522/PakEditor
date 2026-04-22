using System.Collections.ObjectModel;

namespace UEpaker;

public class AssetTreeNode
{
    private static readonly AssetTreeNode LoadingPlaceholder =
        new("<loading>", "<loading>", isFolder: false);

    public string Name { get; }
    public string FullPath { get; }
    public bool IsFolder { get; }
    public ObservableCollection<AssetTreeNode> Children { get; } = new();

    private readonly Func<IEnumerable<AssetTreeNode>>? _childLoader;
    private bool _childrenLoaded;

    private AssetTreeNode(string name, string fullPath, bool isFolder)
    {
        Name = name;
        FullPath = fullPath;
        IsFolder = isFolder;
    }

    public AssetTreeNode(string name, string fullPath, Func<IEnumerable<AssetTreeNode>> childLoader)
    {
        Name = name;
        FullPath = fullPath;
        IsFolder = true;
        _childLoader = childLoader;
        Children.Add(LoadingPlaceholder);
    }

    public AssetTreeNode(string name, string fullPath)
    {
        Name = name;
        FullPath = fullPath;
        IsFolder = false;
    }

    public bool IsLoadingPlaceholder => ReferenceEquals(this, LoadingPlaceholder);

    /// <summary>
    /// Called when a folder node is expanded. Swaps the placeholder for real children.
    /// </summary>
    public void EnsureChildrenLoaded()
    {
        if (_childrenLoaded || _childLoader is null) return;
        _childrenLoaded = true;

        Children.Clear();
        foreach (var child in _childLoader())
            Children.Add(child);
    }

    /// <summary>
    /// Builds a lazy tree from a flat list of virtual file paths.
    /// Each folder node loads its children on first expand.
    /// </summary>
    public static IReadOnlyList<AssetTreeNode> BuildTree(IEnumerable<string> filePaths)
    {
        // index: folderPath -> (subFolders sorted, files sorted)
        var folders = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

        folders[""] = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        files[""] = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in filePaths)
        {
            var normalized = path.Replace('\\', '/');
            var slash = normalized.LastIndexOf('/');

            string dir = slash < 0 ? "" : normalized[..slash];
            string name = slash < 0 ? normalized : normalized[(slash + 1)..];

            EnsureAncestors(dir, folders, files);

            if (!files.TryGetValue(dir, out var fileSet))
                files[dir] = fileSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            fileSet.Add(name);
        }

        return BuildChildren("", folders, files);
    }

    private static void EnsureAncestors(
        string dir,
        Dictionary<string, SortedSet<string>> folders,
        Dictionary<string, SortedSet<string>> files)
    {
        if (folders.ContainsKey(dir)) return;

        folders[dir] = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        files[dir] = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        var slash = dir.LastIndexOf('/');
        var parent = slash < 0 ? "" : dir[..slash];
        var segment = slash < 0 ? dir : dir[(slash + 1)..];

        EnsureAncestors(parent, folders, files);
        folders[parent].Add(segment);
    }

    private static IReadOnlyList<AssetTreeNode> BuildChildren(
        string dirPath,
        Dictionary<string, SortedSet<string>> folders,
        Dictionary<string, SortedSet<string>> files)
    {
        var result = new List<AssetTreeNode>();

        if (folders.TryGetValue(dirPath, out var subDirs))
        {
            foreach (var sub in subDirs)
            {
                var fullSubPath = dirPath.Length == 0 ? sub : $"{dirPath}/{sub}";
                var captured = fullSubPath;
                result.Add(new AssetTreeNode(sub, fullSubPath,
                    () => BuildChildren(captured, folders, files)));
            }
        }

        if (files.TryGetValue(dirPath, out var fileSet))
        {
            foreach (var f in fileSet)
            {
                var fullFilePath = dirPath.Length == 0 ? f : $"{dirPath}/{f}";
                result.Add(new AssetTreeNode(f, fullFilePath));
            }
        }

        return result;
    }
}
