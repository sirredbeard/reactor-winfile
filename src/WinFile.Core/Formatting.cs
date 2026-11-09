using System.Globalization;

namespace WinFile.Core;

/// <summary>Number formatting in the style of the original numfmt.c: grouped integers, compact sizes.</summary>
public static class Formatting
{
    public static string FormatLong(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "0 bytes";
        string[] units = ["bytes", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? $"{bytes} {units[unit]}"
            : $"{value.ToString(value >= 100 ? "0" : "0.0", CultureInfo.CurrentCulture)} {units[unit]}";
    }

    /// <summary>The status bar line of the original: "C:\path   12,345,678 bytes free of 98,765,432".</summary>
    public static string FormatStatusLine(string directoryPath, int objectCount, long selectedBytes, DriveEntry? drive)
    {
        var objects = objectCount == 1 ? "1 object" : $"{objectCount} objects";
        var selected = selectedBytes > 0 ? $" ({Formatting.FormatLong(selectedBytes)} bytes selected)" : string.Empty;
        var disk = drive is { IsReady: true }
            ? $"   {Formatting.FormatLong(drive.FreeSpace)} bytes free of {Formatting.FormatLong(drive.TotalSize)}"
            : string.Empty;
        return $"{directoryPath}   {objects}{selected}{disk}";
    }

    /// <summary>
    /// The display form of a name under the original's LowerCase attribute.
    /// UppercaseNames lowercases a name that is stored all-uppercase (the
    /// directory entry flag the original read as ATTR_LOWERCASE, approximated
    /// here as: at least one letter and no lowercase letters). All lowercases
    /// every name. Display only.
    /// </summary>
    public static string DisplayName(string name, LowerCaseMode mode)
    {
        if (mode == LowerCaseMode.Off || name.Length == 0) return name;
        if (mode == LowerCaseMode.All) return name.ToLowerInvariant();
        var hasLetter = false;
        foreach (var ch in name)
        {
            if (char.IsLower(ch)) return name;
            if (char.IsLetter(ch)) hasLetter = true;
        }
        return hasLetter ? name.ToLowerInvariant() : name;
    }

    /// <summary>Long path helper. The original handled paths up to 1024 characters once Windows long paths were on.</summary>
    public static string ToLongPath(string path)
    {
        if (!OperatingSystem.IsWindows()) return path;
        var full = Path.GetFullPath(path);
        if (full.Length < 260 || full.StartsWith(@"\\?\", StringComparison.Ordinal)) return full;
        return full.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + full[2..]
            : @"\\?\" + full;
    }

    public static string StripLongPathPrefix(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + path[8..];
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path[4..];
        return path;
    }
}

/// <summary>Natural (logical) sort, the way Explorer and the enhanced WinFile order "file2" before "file10".</summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
            {
                var startX = ix;
                var startY = iy;
                while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                while (iy < y.Length && char.IsDigit(y[iy])) iy++;
                var numX = x.AsSpan(startX, ix - startX).TrimStart('0');
                var numY = y.AsSpan(startY, iy - startY).TrimStart('0');
                if (numX.Length != numY.Length) return numX.Length.CompareTo(numY.Length);
                var byDigits = numX.CompareTo(numY, StringComparison.Ordinal);
                if (byDigits != 0) return byDigits;
                // Equal numeric value: fewer leading zeros wins, then keep scanning.
                var zeros = (ix - startX - numX.Length).CompareTo(iy - startY - numY.Length);
                if (zeros != 0) return zeros;
            }
            else
            {
                var cx = char.ToUpperInvariant(x[ix]);
                var cy = char.ToUpperInvariant(y[iy]);
                if (cx != cy) return cx.CompareTo(cy);
                ix++;
                iy++;
            }
        }
        return (x.Length - ix).CompareTo(y.Length - iy);
    }
}

/// <summary>The View &gt; By File Type include filter. Patterns are separated by semicolons, for example "*.txt;*.md".</summary>
public static class FileTypeFilter
{
    public static IReadOnlyList<string> ParsePatterns(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return ["*.*"];
        return filter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static bool Matches(string fileName, IReadOnlyList<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (WildcardMatch(fileName, pattern)) return true;
        }
        return false;
    }

    public static bool MatchesFilter(string fileName, string? filter) => Matches(fileName, ParsePatterns(filter));

    /// <summary>Windows-style wildcard match: * matches any run, ? matches one character, "*.*" matches extensionless files too.</summary>
    public static bool WildcardMatch(string value, string pattern)
    {
        if (pattern is "*" or "*.*") return true;
        if (pattern == "*." && !value.Contains('.')) return true;
        int v = 0, p = 0, starP = -1, starV = 0;
        while (v < value.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(value[v])))
            {
                v++;
                p++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                starP = p++;
                starV = v;
            }
            else if (starP >= 0)
            {
                p = starP + 1;
                v = ++starV;
            }
            else
            {
                return false;
            }
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    /// <summary>Applies the include filter to directory entries. Directories always pass so navigation keeps working.</summary>
    public static IEnumerable<FileEntry> Apply(IEnumerable<FileEntry> entries, WinFileSettings settings)
    {
        var patterns = ParsePatterns(settings.IncludeFilter);
        foreach (var entry in entries)
        {
            if (!settings.ShowHiddenAndSystem && (entry.IsHidden || entry.IsSystem) && !IsDotPrefixedSystemEntry(entry))
                continue;
            if (entry.IsDirectory || Matches(entry.Name, patterns))
                yield return entry;
        }
    }

    // Hidden files on non-Windows test hosts start with a dot. Those are not the Windows hidden attribute case.
    private static bool IsDotPrefixedSystemEntry(FileEntry entry) => false;
}

/// <summary>Entry ordering for every sort mode on the View menu. Directories always come first, as in the original.</summary>
public static class EntrySorter
{
    public static IReadOnlyList<FileEntry> Sort(IEnumerable<FileEntry> entries, SortMode mode)
    {
        var list = entries.ToList();
        list.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
            var primary = mode switch
            {
                SortMode.Type => CompareType(a, b),
                SortMode.Size => a.Size.CompareTo(b.Size),
                SortMode.Date => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc),
                SortMode.DateForward => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc),
                _ => 0,
            };
            return primary != 0 ? primary : NaturalStringComparer.Instance.Compare(a.Name, b.Name);
        });
        return list;
    }

    private static int CompareType(FileEntry a, FileEntry b)
    {
        var byExt = NaturalStringComparer.Instance.Compare(a.Extension, b.Extension);
        return byExt;
    }
}
