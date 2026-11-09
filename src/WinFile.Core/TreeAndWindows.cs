namespace WinFile.Core;

/// <summary>One node in the tree pane. Children load on demand, like the original tree control.</summary>
public sealed class DirectoryTreeNode
{
    private readonly DirectoryScanner _scanner;
    private List<DirectoryTreeNode>? _children;

    public DirectoryTreeNode(string path, DirectoryScanner? scanner = null)
    {
        Path = path;
        _scanner = scanner ?? new DirectoryScanner();
        var root = System.IO.Path.GetPathRoot(path);
        Name = string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
            ? path
            : System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
    }

    public string Path { get; }
    public string Name { get; }
    public bool IsExpanded { get; private set; }

    /// <summary>Tree &gt; Indicate Expandable Branches shows a plus only when this is true.</summary>
    public bool HasExpandableChildren => _scanner.HasSubdirectories(Path);

    public IReadOnlyList<DirectoryTreeNode> Children
    {
        get
        {
            _children ??= LoadChildren();
            return _children;
        }
    }

    private List<DirectoryTreeNode> LoadChildren() =>
        _scanner.GetSubdirectories(Path).Select(p => new DirectoryTreeNode(p, _scanner)).ToList();

    public void Refresh() => _children = null;

    /// <summary>Tree &gt; Expand One Level (+).</summary>
    public void ExpandOneLevel()
    {
        IsExpanded = true;
        _ = Children;
    }

    /// <summary>Tree &gt; Expand Branch (*): this node and every descendant.</summary>
    public void ExpandBranch()
    {
        ExpandOneLevel();
        foreach (var child in Children) child.ExpandBranch();
    }

    /// <summary>Tree &gt; Collapse Branch (-).</summary>
    public void CollapseBranch()
    {
        IsExpanded = false;
        foreach (var child in _children ?? []) child.CollapseBranch();
    }

    /// <summary>Flattens the visible tree in display order, honoring expansion state.</summary>
    public IEnumerable<(DirectoryTreeNode Node, int Depth)> FlattenVisible(int depth = 0)
    {
        yield return (this, depth);
        if (!IsExpanded) yield break;
        foreach (var child in Children)
        {
            foreach (var row in child.FlattenVisible(depth + 1))
                yield return row;
        }
    }

    public DirectoryTreeNode? Find(string path)
    {
        if (FileOperations.SamePath(Path, path)) return this;
        foreach (var child in Children)
        {
            var found = child.Find(path);
            if (found is not null) return found;
        }
        return null;
    }
}

/// <summary>Builds the root nodes of the tree pane, one per drive.</summary>
public sealed class DirectoryTree
{
    private readonly DirectoryScanner _scanner;

    public DirectoryTree(DirectoryScanner? scanner = null)
    {
        _scanner = scanner ?? new DirectoryScanner();
        Roots = _scanner.GetDrives().Select(d => new DirectoryTreeNode(d.RootPath, _scanner)).ToList();
    }

    public IReadOnlyList<DirectoryTreeNode> Roots { get; }

    /// <summary>Tree &gt; Expand All (Ctrl+*).</summary>
    public void ExpandAll()
    {
        foreach (var root in Roots) root.ExpandBranch();
    }

    public DirectoryTreeNode? Find(string path)
    {
        foreach (var root in Roots)
        {
            if (path.StartsWith(root.Path, StringComparison.OrdinalIgnoreCase))
                return root.Find(path);
        }
        return null;
    }
}

public readonly record struct WindowRect(double X, double Y, double Width, double Height);

/// <summary>Window &gt; Cascade, Tile Horizontally, Tile Vertically, and Arrange Icons geometry.</summary>
public static class WindowLayout
{
    public static IReadOnlyList<WindowRect> Cascade(int count, WindowRect area, double offset = 24)
    {
        if (count <= 0) return [];
        var width = Math.Max(200, area.Width - offset * (count - 1));
        var height = Math.Max(150, area.Height - offset * (count - 1));
        var rects = new List<WindowRect>();
        for (var i = 0; i < count; i++)
            rects.Add(new WindowRect(area.X + offset * i, area.Y + offset * i, width, height));
        return rects;
    }

    public static IReadOnlyList<WindowRect> TileHorizontal(int count, WindowRect area)
    {
        if (count <= 0) return [];
        var height = area.Height / count;
        return Enumerable.Range(0, count)
            .Select(i => new WindowRect(area.X, area.Y + height * i, area.Width, height))
            .ToList();
    }

    public static IReadOnlyList<WindowRect> TileVertical(int count, WindowRect area)
    {
        if (count <= 0) return [];
        var width = area.Width / count;
        return Enumerable.Range(0, count)
            .Select(i => new WindowRect(area.X + width * i, area.Y, width, area.Height))
            .ToList();
    }

    /// <summary>Arrange Icons: a left-to-right grid along the bottom of the area, as MDI did it.</summary>
    public static IReadOnlyList<WindowRect> ArrangeIcons(int count, WindowRect area, double iconWidth = 72, double iconHeight = 48)
    {
        if (count <= 0) return [];
        var perRow = Math.Max(1, (int)(area.Width / iconWidth));
        var rects = new List<WindowRect>();
        for (var i = 0; i < count; i++)
        {
            var row = i / perRow;
            var col = i % perRow;
            rects.Add(new WindowRect(
                area.X + col * iconWidth,
                area.Y + area.Height - (row + 1) * iconHeight,
                iconWidth, iconHeight));
        }
        return rects;
    }
}

/// <summary>One MDI child window in the original. The Reactor app renders these as tabs over this model.</summary>
public sealed class ChildWindow
{
    public ChildWindow(string directoryPath)
    {
        DirectoryPath = directoryPath;
        History = new NavigationHistory(directoryPath);
    }

    public string DirectoryPath { get; set; }
    public NavigationHistory History { get; }
    public List<string> Selection { get; } = [];
    public bool IsMinimized { get; set; }
    public WindowRect Bounds { get; set; }
    public string Title => DirectoryPath;
}

/// <summary>The MDI frame state: child windows, the active one, and Window menu operations.</summary>
public sealed class MdiManager
{
    private readonly List<ChildWindow> _windows = [];

    public IReadOnlyList<ChildWindow> Windows => _windows;
    public ChildWindow? Active { get; private set; }

    public ChildWindow NewWindow(string directoryPath)
    {
        var window = new ChildWindow(directoryPath);
        _windows.Add(window);
        Active = window;
        return window;
    }

    public void Activate(ChildWindow window)
    {
        if (_windows.Contains(window)) Active = window;
    }

    public void Close(ChildWindow window)
    {
        _windows.Remove(window);
        if (Active == window) Active = _windows.LastOrDefault();
    }

    public void ApplyCascade(WindowRect area)
    {
        var rects = WindowLayout.Cascade(_windows.Count, area);
        for (var i = 0; i < _windows.Count; i++) _windows[i].Bounds = rects[i];
    }

    public void ApplyTileHorizontal(WindowRect area)
    {
        var rects = WindowLayout.TileHorizontal(_windows.Count, area);
        for (var i = 0; i < _windows.Count; i++) _windows[i].Bounds = rects[i];
    }

    public void ApplyTileVertical(WindowRect area)
    {
        var rects = WindowLayout.TileVertical(_windows.Count, area);
        for (var i = 0; i < _windows.Count; i++) _windows[i].Bounds = rects[i];
    }
}
