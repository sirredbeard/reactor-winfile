namespace WinFile.Core;

/// <summary>
/// The directory index behind File &gt; Goto (Ctrl+G). The original splits paths into words
/// with a configurable punctuation set and matches a query against those words.
/// </summary>
public sealed class GotoIndex
{
    private readonly List<(string Path, string[] Words)> _entries = [];
    private readonly string _punctuation;

    public GotoIndex(string punctuation = "-_") => _punctuation = punctuation;

    public int Count => _entries.Count;
    public IReadOnlyList<string> AllPaths => _entries.Select(e => e.Path).ToList();

    public void Add(string directoryPath)
    {
        if (_entries.Any(e => string.Equals(e.Path, directoryPath, StringComparison.OrdinalIgnoreCase))) return;
        _entries.Add((directoryPath, SplitWords(directoryPath)));
    }

    public void AddRange(IEnumerable<string> paths)
    {
        foreach (var path in paths) Add(path);
    }

    /// <summary>Builds the index by walking every ready fixed and removable drive, as "Create Goto Index on Launch" does.</summary>
    public static GotoIndex BuildFromDrives(string punctuation = "-_", CancellationToken cancellationToken = default)
    {
        var index = new GotoIndex(punctuation);
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            index.Add(drive.RootDirectory.FullName);
            Walk(drive.RootDirectory.FullName, index, cancellationToken);
        }
        return index;
    }

    public static GotoIndex BuildFromRoots(IEnumerable<string> roots, string punctuation = "-_", CancellationToken cancellationToken = default)
    {
        var index = new GotoIndex(punctuation);
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            index.Add(root);
            Walk(root, index, cancellationToken);
        }
        return index;
    }

    private static void Walk(string root, GotoIndex index, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            string[] dirs;
            try { dirs = Directory.GetDirectories(current); }
            catch { continue; }
            foreach (var dir in dirs)
            {
                index.Add(dir);
                pending.Push(dir);
            }
        }
    }

    /// <summary>Splits a path into searchable words on directory separators and the configured punctuation.</summary>
    public string[] SplitWords(string path)
    {
        // Both slash kinds always split: index entries are Windows paths even when tests run elsewhere.
        var separators = new List<char> { '\\', '/', ' ', '.', ':' };
        separators.AddRange(_punctuation);
        return path.Split(separators.ToArray(), StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Finds directories whose words start with every query word, in order of closeness.
    /// A query that is itself an existing path resolves directly, like the original dialog.
    /// </summary>
    public IReadOnlyList<string> Query(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var trimmed = text.Trim();
        if (Directory.Exists(trimmed)) return [Path.GetFullPath(trimmed)];

        var queryWords = SplitWords(trimmed);
        if (queryWords.Length == 0) return [];

        var scored = new List<(string Path, int Score)>();
        foreach (var (path, words) in _entries)
        {
            var matched = 0;
            foreach (var q in queryWords)
            {
                if (words.Any(w => w.StartsWith(q, StringComparison.OrdinalIgnoreCase))) matched++;
            }
            if (matched == queryWords.Length)
                scored.Add((path, path.Length));
        }
        // Shorter paths are usually the intended target, so they rank first.
        return scored.OrderBy(s => s.Score).Select(s => s.Path).ToList();
    }
}

/// <summary>
/// Copy and cut state for the clipboard commands. The app binds this to the real Windows clipboard;
/// the model holds the semantics: paste after cut moves and clears, paste after copy keeps the source list.
/// </summary>
public sealed class ClipboardModel
{
    private readonly FileOperations _ops;

    public ClipboardModel(FileOperations? ops = null) => _ops = ops ?? new FileOperations();

    public IReadOnlyList<string> Paths { get; private set; } = [];
    public bool IsCut { get; private set; }
    public bool HasContent => Paths.Count > 0;

    public void Copy(IReadOnlyList<string> paths)
    {
        Paths = paths.ToList();
        IsCut = false;
    }

    public void Cut(IReadOnlyList<string> paths)
    {
        Paths = paths.ToList();
        IsCut = true;
    }

    public void Clear()
    {
        Paths = [];
        IsCut = false;
    }

    public OperationResult Paste(string destinationDirectory, ConflictAction onConflict = ConflictAction.Fail)
    {
        if (!HasContent) return OperationResult.Empty;
        var sources = Paths.ToList();
        if (IsCut)
        {
            var moved = _ops.Move(sources, destinationDirectory, onConflict);
            if (!moved.HasFailures) Clear();
            return moved;
        }
        // FileOperations.Copy applies the "- Copy" rule when the destination is the source directory.
        return _ops.Copy(sources, destinationDirectory, onConflict);
    }
}
