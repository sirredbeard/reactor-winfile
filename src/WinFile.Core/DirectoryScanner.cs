namespace WinFile.Core;

/// <summary>Reads directories, drives, and directory trees. The managed equivalent of wfdir.c, wfdrives.c, and wftree.c.</summary>
public sealed class DirectoryScanner
{
    /// <summary>Lists one directory, filtered and sorted per the current settings.</summary>
    public IReadOnlyList<FileEntry> ListDirectory(string path, WinFileSettings settings)
    {
        var dir = new DirectoryInfo(Formatting.StripLongPathPrefix(path));
        if (!dir.Exists) throw new DirectoryNotFoundException($"Directory not found: {path}");
        var entries = dir.EnumerateFileSystemInfos().Select(FileEntry.FromFileSystemInfo);
        return EntrySorter.Sort(FileTypeFilter.Apply(entries, settings), settings.Sort);
    }

    /// <summary>Lists one directory with no filtering, for callers that filter themselves.</summary>
    public IReadOnlyList<FileEntry> ListDirectoryRaw(string path)
    {
        var dir = new DirectoryInfo(Formatting.StripLongPathPrefix(path));
        if (!dir.Exists) throw new DirectoryNotFoundException($"Directory not found: {path}");
        return dir.EnumerateFileSystemInfos().Select(FileEntry.FromFileSystemInfo).ToList();
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
        var root = Path.GetPathRoot(Path.GetFullPath(Formatting.StripLongPathPrefix(path)));
        if (root is null) return null;
        return GetDrives().FirstOrDefault(d => string.Equals(d.RootPath, root, StringComparison.OrdinalIgnoreCase));
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

/// <summary>Watches one directory and signals when the pane should refresh. The managed wfchgnot.c.</summary>
public sealed class DirectoryWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;

    public DirectoryWatcher(string path)
    {
        _watcher = new FileSystemWatcher(Formatting.StripLongPathPrefix(path))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Created += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _watcher.Deleted += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _watcher.Renamed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _watcher.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;

    public void Dispose() => _watcher.Dispose();
}
