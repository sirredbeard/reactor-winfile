namespace WinFile.Core;

/// <summary>File &gt; Search criteria. The enhanced WinFile added the modified-after date and sorted output by date.</summary>
public sealed record SearchCriteria(
    string RootPath,
    string Pattern = "*.*",
    bool IncludeSubdirectories = true,
    DateTime? ModifiedAfterUtc = null);

/// <summary>Recursive file search. The managed equivalent of wfsearch.c.</summary>
public sealed class SearchService
{
    public IReadOnlyList<FileEntry> Search(SearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        var root = Formatting.StripLongPathPrefix(criteria.RootPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Directory not found: {criteria.RootPath}");
        var patterns = FileTypeFilter.ParsePatterns(criteria.Pattern);
        var option = criteria.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var results = new List<FileEntry>();

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", option);
        }
        catch (UnauthorizedAccessException)
        {
            files = EnumerateTolerant(root, criteria.IncludeSubdirectories);
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!FileTypeFilter.Matches(Path.GetFileName(file), patterns)) continue;
            FileEntry entry;
            try { entry = FileEntry.FromFileSystemInfo(new FileInfo(file)); }
            catch { continue; }
            if (criteria.ModifiedAfterUtc is { } after && entry.LastWriteTimeUtc <= after) continue;
            results.Add(entry);
        }

        // The enhanced WinFile sorts search output by date, newest first.
        results.Sort((a, b) =>
        {
            var byDate = b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc);
            return byDate != 0 ? byDate : NaturalStringComparer.Instance.Compare(a.Name, b.Name);
        });
        return results;
    }

    private static IEnumerable<string> EnumerateTolerant(string root, bool recursive)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] files = [];
            string[] dirs = [];
            try { files = Directory.GetFiles(current); } catch { }
            foreach (var f in files) yield return f;
            if (!recursive) yield break;
            try { dirs = Directory.GetDirectories(current); } catch { }
            foreach (var d in dirs) pending.Push(d);
        }
    }
}

/// <summary>Back and forward stacks for the child window navigation the enhanced WinFile added.</summary>
public sealed class NavigationHistory
{
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();

    public NavigationHistory(string initialPath) => Current = initialPath;

    public string Current { get; private set; }
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    public void NavigateTo(string path)
    {
        if (string.Equals(Current, path, StringComparison.OrdinalIgnoreCase)) return;
        _back.Push(Current);
        _forward.Clear();
        Current = path;
    }

    public string? GoBack()
    {
        if (!CanGoBack) return null;
        _forward.Push(Current);
        Current = _back.Pop();
        return Current;
    }

    public string? GoForward()
    {
        if (!CanGoForward) return null;
        _back.Push(Current);
        Current = _forward.Pop();
        return Current;
    }
}
