namespace WinFile.Core;

public enum ConflictAction
{
    Fail,
    Overwrite,
    Skip,
}

/// <summary>
/// Copy, move, delete, rename, and create operations. The managed equivalent of wfcopy.c and wffile.c.
/// Directories copy recursively. A copy into the same directory gets the "- Copy" suffix the enhanced WinFile added.
/// </summary>
public sealed class FileOperations
{
    public OperationResult Copy(
        IReadOnlyList<string> sources,
        string destinationDirectory,
        ConflictAction onConflict = ConflictAction.Fail,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        var failures = new List<OperationFailure>();
        var totalFiles = sources.Sum(CountFiles);
        long bytesTotal = sources.Sum(CountBytes);
        var done = 0;
        long bytesDone = 0;

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var dest = Path.Combine(destinationDirectory, Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                if (SamePath(source, dest))
                    dest = DuplicateName(source);
                CopyOne(source, dest, onConflict, ref done, ref bytesDone, totalFiles, bytesTotal, progress, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new OperationFailure(source, ex.Message));
            }
        }
        return new OperationResult(done, failures);
    }

    public OperationResult Move(
        IReadOnlyList<string> sources,
        string destinationDirectory,
        ConflictAction onConflict = ConflictAction.Fail,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        var failures = new List<OperationFailure>();
        var done = 0;
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var dest = Path.Combine(destinationDirectory, Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                if (SamePath(source, dest))
                    throw new IOException($"Source and destination are the same: {source}");
                if (Directory.Exists(source))
                {
                    if (Directory.Exists(dest))
                    {
                        if (onConflict == ConflictAction.Skip) continue;
                        if (onConflict == ConflictAction.Fail) throw new IOException($"Destination already exists: {dest}");
                        Directory.Delete(dest, recursive: true);
                    }
                    Directory.Move(source, dest);
                }
                else
                {
                    if (File.Exists(dest))
                    {
                        if (onConflict == ConflictAction.Skip) continue;
                        if (onConflict == ConflictAction.Fail) throw new IOException($"Destination already exists: {dest}");
                        File.Delete(dest);
                    }
                    try
                    {
                        File.Move(source, dest);
                    }
                    catch (IOException)
                    {
                        // Cross-volume move: copy then delete, like the original.
                        File.Copy(source, dest, overwrite: true);
                        File.Delete(source);
                    }
                }
                done += Math.Max(1, 1);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new OperationFailure(source, ex.Message));
            }
        }
        return new OperationResult(done, failures);
    }

    public OperationResult Delete(IReadOnlyList<string> sources, bool recursive = true)
    {
        var failures = new List<OperationFailure>();
        var done = 0;
        foreach (var source in sources)
        {
            try
            {
                if (Directory.Exists(source))
                {
                    ClearReadOnly(new DirectoryInfo(source));
                    Directory.Delete(source, recursive);
                }
                else if (File.Exists(source))
                {
                    File.SetAttributes(source, FileAttributes.Normal);
                    File.Delete(source);
                }
                else
                {
                    failures.Add(new OperationFailure(source, "Path not found."));
                    continue;
                }
                done++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new OperationFailure(source, ex.Message));
            }
        }
        return new OperationResult(done, failures);
    }

    /// <summary>File &gt; Rename. Accepts a bare name or a full path in the same directory.</summary>
    public string Rename(string sourcePath, string newName)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var dest = Path.IsPathFullyQualified(newName) ? newName : Path.Combine(directory, newName);
        if (SamePath(sourcePath, dest)) return dest;
        if (File.Exists(dest) || Directory.Exists(dest))
            throw new IOException($"A file or directory named {Path.GetFileName(dest)} already exists.");
        if (Directory.Exists(sourcePath)) Directory.Move(sourcePath, dest);
        else File.Move(sourcePath, dest);
        return dest;
    }

    public string CreateDirectory(string parentPath, string name)
    {
        var path = Path.Combine(parentPath, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public string CreateFile(string parentPath, string name)
    {
        var path = Path.Combine(parentPath, name);
        using (File.Create(path)) { }
        return path;
    }

    /// <summary>"name - Copy.ext", then "name - Copy (2).ext". Matches the enhanced WinFile same-directory copy rule.</summary>
    public static string DuplicateName(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var candidate = Path.Combine(directory, $"{stem} - Copy{ext}");
        var n = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{stem} - Copy ({n}){ext}");
            n++;
        }
        return candidate;
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void CopyOne(
        string source, string dest, ConflictAction onConflict,
        ref int done, ref long bytesDone, int totalFiles, long bytesTotal,
        IProgress<CopyProgress>? progress, CancellationToken cancellationToken)
    {
        if (Directory.Exists(source))
        {
            Directory.CreateDirectory(dest);
            foreach (var child in Directory.EnumerateFileSystemEntries(source))
                CopyOne(child, Path.Combine(dest, Path.GetFileName(child)), onConflict, ref done, ref bytesDone, totalFiles, bytesTotal, progress, cancellationToken);
            // Preserve directory timestamps after children are in place.
            Directory.SetLastWriteTimeUtc(dest, Directory.GetLastWriteTimeUtc(source));
            return;
        }

        if (File.Exists(dest))
        {
            if (onConflict == ConflictAction.Skip) return;
            if (onConflict == ConflictAction.Fail) throw new IOException($"Destination already exists: {dest}");
        }
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1_048_576, FileOptions.SequentialScan))
        using (var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 1_048_576, FileOptions.SequentialScan))
        {
            var buffer = new byte[1_048_576];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
                bytesDone += read;
            }
        }
        File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(source));
        done++;
        progress?.Report(new CopyProgress(source, done, totalFiles, bytesDone, bytesTotal));
    }

    private static int CountFiles(string path)
    {
        if (File.Exists(path)) return 1;
        if (!Directory.Exists(path)) return 0;
        try { return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count(); }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static long CountBytes(string path)
    {
        if (File.Exists(path)) return new FileInfo(path).Length;
        if (!Directory.Exists(path)) return 0;
        try { return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } }); }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static void ClearReadOnly(DirectoryInfo dir)
    {
        try
        {
            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { file.Attributes &= ~FileAttributes.ReadOnly; } catch { }
            }
        }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>File.Select Files pattern logic, shared by the dialog and tests.</summary>
public static class SelectionService
{
    public static IReadOnlyList<FileEntry> SelectByPattern(IEnumerable<FileEntry> entries, string pattern)
    {
        var patterns = FileTypeFilter.ParsePatterns(pattern);
        return entries.Where(e => FileTypeFilter.Matches(e.Name, patterns)).ToList();
    }

    /// <summary>
    /// Which confirmation, if any, a delete of these entries needs under the
    /// original's rules. A protected entry (system, hidden, or read-only)
    /// outranks a directory delete, which outranks a plain file delete.
    /// </summary>
    public static DeleteConfirmReason DeleteConfirmationFor(IEnumerable<FileEntry> selected, ConfirmationSettings confirmations)
    {
        var any = false;
        var anyDirectory = false;
        var anyProtected = false;
        foreach (var entry in selected)
        {
            any = true;
            anyDirectory |= entry.IsDirectory;
            anyProtected |= entry.IsReadOnly || entry.IsSystem || entry.IsHidden;
        }
        if (!any) return DeleteConfirmReason.None;
        if (anyProtected && confirmations.OnReadOnlySystemHidden) return DeleteConfirmReason.ProtectedFile;
        if (anyDirectory && confirmations.OnSubtreeDelete) return DeleteConfirmReason.SubtreeDelete;
        return confirmations.OnDelete ? DeleteConfirmReason.Delete : DeleteConfirmReason.None;
    }

    /// <summary>The Select Files dialog's Deselect button: the current selection minus every entry the pattern matches.</summary>
    public static IReadOnlyList<string> DeselectByPattern(IEnumerable<FileEntry> entries, string pattern, IEnumerable<string> currentSelection)
    {
        var patterns = FileTypeFilter.ParsePatterns(pattern);
        var remove = entries
            .Where(e => FileTypeFilter.Matches(e.Name, patterns))
            .Select(e => e.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return currentSelection.Where(p => !remove.Contains(p)).ToList();
    }
}
