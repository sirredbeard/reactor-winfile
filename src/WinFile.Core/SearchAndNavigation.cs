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
        var results = new List<FileEntry>();

        // Walk directory by directory on the scanner's find-data enumeration.
        // Entries arrive with size and times attached, so a search over a big
        // tree costs no per-file stat, and a directory that cannot be read is
        // skipped instead of ending the search: enumeration here is tolerant
        // by construction (the previous lazy EnumerateFiles fallback could
        // never catch the errors it was written for, because they surface
        // during enumeration, outside its try block).
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var entry in DirectoryScanner.EnumerateEntries(current))
            {
                // Per entry, not per directory: one directory can hold more
                // files than the whole rest of the tree.
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsDirectory)
                {
                    if (criteria.IncludeSubdirectories) pending.Push(entry.FullPath);
                    continue;
                }
                if (!FileTypeFilter.Matches(entry.Name, patterns)) continue;
                if (criteria.ModifiedAfterUtc is { } after && entry.LastWriteTimeUtc <= after) continue;
                results.Add(entry);
            }
        }

        // The enhanced WinFile sorts search output by date, newest first.
        results.Sort((a, b) =>
        {
            var byDate = b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc);
            return byDate != 0 ? byDate : NaturalStringComparer.Instance.Compare(a.Name, b.Name);
        });
        return results;
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
