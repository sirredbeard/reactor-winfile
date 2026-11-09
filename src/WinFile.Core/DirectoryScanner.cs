using System.IO.Enumeration;

namespace WinFile.Core;

/// <summary>Reads directories, drives, and directory trees. The managed equivalent of wfdir.c, wfdrives.c, and wftree.c.</summary>
public sealed class DirectoryScanner
{
    /// <summary>
    /// Enumerates one directory straight from the file system's find data.
    /// Sizes, times, and attributes arrive with the enumeration, so listing a
    /// large directory costs no per-file stat call: the difference between
    /// FileInfo-per-file and this path is the difference between two system
    /// calls per entry and one pass over the directory.
    /// </summary>
    internal static IEnumerable<FileEntry> EnumerateEntries(string path)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
        };
        return new FileSystemEnumerable<FileEntry>(
            Path.GetFullPath(Formatting.StripLongPathPrefix(path)),
            static (ref FileSystemEntry entry) => FileEntry.FromEntry(entry),
            options);
    }

    /// <summary>Lists one directory, filtered and sorted per the current settings.</summary>
    public IReadOnlyList<FileEntry> ListDirectory(string path, WinFileSettings settings)
    {
        if (!Directory.Exists(Formatting.StripLongPathPrefix(path)))
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        return EntrySorter.Sort(FileTypeFilter.Apply(EnumerateEntries(path), settings), settings.Sort);
    }

    /// <summary>Lists one directory with no filtering, for callers that filter themselves.</summary>
    public IReadOnlyList<FileEntry> ListDirectoryRaw(string path)
    {
        if (!Directory.Exists(Formatting.StripLongPathPrefix(path)))
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        return EnumerateEntries(path).ToList();
    }

    public IReadOnlyList<DriveEntry> GetDrives() =>
        DriveInfo.GetDrives().Select(TryFromDrive).OfType<DriveEntry>().ToList();

    // Some mounts error when probed (stale network mounts, restricted system mounts). The drive bar skips those.
    private static DriveEntry? TryFromDrive(DriveInfo drive)
    {
        try { return DriveEntry.FromDriveInfo(drive); }
        catch { return null; }
    }

    public DriveEntry? GetDriveForPath(string path)
    {
        // Probe the one drive the path lives on. Enumerating every drive here
        // made each refresh pay for every stale network mount on the machine.
        var root = Path.GetPathRoot(Path.GetFullPath(Formatting.StripLongPathPrefix(path)));
        if (root is null) return null;
        return TryFromDrive(new DriveInfo(root));
    }

    /// <summary>Subdirectories of one path, natural sorted. Used by the tree pane.</summary>
    public IReadOnlyList<string> GetSubdirectories(string path)
    {
        try
        {
            var dirs = new DirectoryInfo(Formatting.StripLongPathPrefix(path))
                .EnumerateDirectories()
                .Where(d => (d.Attributes & FileAttributes.System) == 0 || (d.Attributes & FileAttributes.Hidden) == 0)
                .Select(d => d.FullName)
                .ToList();
            dirs.Sort((a, b) => NaturalStringComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));
            return dirs;
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public bool HasSubdirectories(string path)
    {
        try
        {
            return new DirectoryInfo(Formatting.StripLongPathPrefix(path)).EnumerateDirectories().Any();
        }
        catch
        {
            return false;
        }
    }

    public long GetDirectorySize(string path)
    {
        long total = 0;
        try
        {
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { total += file.Length; } catch { /* skip files that vanish mid-scan */ }
            }
        }
        catch (UnauthorizedAccessException) { /* partial totals are fine for a properties dialog */ }
        return total;
    }
}

/// <summary>
/// Watches one directory and signals when the pane should refresh. The managed
/// wfchgnot.c. With a debounce interval (the original's ChangeNotifyTime), a
/// burst of changes raises Changed once, after the burst settles, instead of
/// once per file: a 1,000-file copy is one refresh, not a thousand.
/// </summary>
public sealed class DirectoryWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Timer? _debounceTimer;
    private readonly TimeSpan _debounceInterval;

    public DirectoryWatcher(string path, TimeSpan debounce = default)
    {
        _debounceInterval = debounce;
        _watcher = new FileSystemWatcher(Formatting.StripLongPathPrefix(path))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        if (debounce > TimeSpan.Zero)
        {
            _debounceTimer = new Timer(_ => Changed?.Invoke(this, EventArgs.Empty), null,
                Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _watcher.Created += OnRawChange;
            _watcher.Deleted += OnRawChange;
            _watcher.Renamed += OnRawChange;
            _watcher.Changed += OnRawChange;
        }
        else
        {
            _watcher.Created += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            _watcher.Deleted += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            _watcher.Renamed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            _watcher.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnRawChange(object sender, FileSystemEventArgs e) =>
        _debounceTimer?.Change(_debounceInterval, Timeout.InfiniteTimeSpan);

    public event EventHandler? Changed;

    public void Dispose()
    {
        _debounceTimer?.Dispose();
        _watcher.Dispose();
    }
}
