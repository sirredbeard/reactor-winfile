namespace WinFile.Core;

/// <summary>How the directory pane sorts its entries. Mirrors the View menu of the original.</summary>
public enum SortMode
{
    Name,
    Type,
    Size,
    Date,
    DateForward,
}

/// <summary>How much detail the directory pane shows per entry. Mirrors View &gt; Name / All File Details / Partial Details.</summary>
public enum ViewDetailMode
{
    NameOnly,
    AllDetails,
    PartialDetails,
}

/// <summary>Which panes a child window shows. Mirrors View &gt; Tree and Directory / Tree Only / Directory Only.</summary>
public enum PaneView
{
    TreeAndDirectory,
    TreeOnly,
    DirectoryOnly,
}

/// <summary>Which shell the File &gt; Start submenu launches.</summary>
public enum ShellKind
{
    Cmd,
    PowerShell,
    Bash,
    Explorer,
}

/// <summary>One file or directory as the directory pane displays it.</summary>
public sealed record FileEntry(
    string FullPath,
    string Name,
    bool IsDirectory,
    long Size,
    DateTime LastWriteTimeUtc,
    DateTime CreationTimeUtc,
    FileAttributes Attributes,
    bool IsReparsePoint,
    string? LinkTarget)
{
    public string Extension => IsDirectory ? string.Empty : Path.GetExtension(Name);

    /// <summary>The type column of the original: the extension for files, a folder marker for directories.</summary>
    public string TypeName => IsDirectory
        ? (IsReparsePoint ? "Directory (link)" : "Directory")
        : string.IsNullOrEmpty(Extension) ? "File" : Extension.TrimStart('.').ToUpperInvariant() + " File";

    public bool IsHidden => (Attributes & FileAttributes.Hidden) != 0;
    public bool IsSystem => (Attributes & FileAttributes.System) != 0;
    public bool IsReadOnly => (Attributes & FileAttributes.ReadOnly) != 0;
    public bool IsArchive => (Attributes & FileAttributes.Archive) != 0;
    public bool IsCompressed => (Attributes & FileAttributes.Compressed) != 0;

    public static FileEntry FromFileSystemInfo(FileSystemInfo info)
    {
        var isDir = (info.Attributes & FileAttributes.Directory) != 0;
        string? target = null;
        var reparse = (info.Attributes & FileAttributes.ReparsePoint) != 0;
        if (reparse)
        {
            try { target = info.LinkTarget; } catch { /* link target is best effort */ }
        }
        return new FileEntry(
            info.FullName,
            info.Name,
            isDir,
            isDir ? 0 : ((FileInfo)info).Length,
            info.LastWriteTimeUtc,
            info.CreationTimeUtc,
            info.Attributes,
            reparse,
            target);
    }
}

/// <summary>One logical drive as the drive bar displays it.</summary>
public sealed record DriveEntry(
    string RootPath,
    string Name,
    string Label,
    DriveType Type,
    long TotalSize,
    long FreeSpace,
    bool IsReady)
{
    public string Letter => RootPath.Length >= 2 && RootPath[1] == ':' ? RootPath[..1].ToUpperInvariant() : RootPath;

    public static DriveEntry FromDriveInfo(DriveInfo drive)
    {
        var ready = drive.IsReady;
        return new DriveEntry(
            drive.RootDirectory.FullName,
            drive.Name,
            ready ? drive.VolumeLabel : string.Empty,
            drive.DriveType,
            ready ? drive.TotalSize : 0,
            ready ? drive.AvailableFreeSpace : 0,
            ready);
    }
}

/// <summary>The five confirmation toggles from Options &gt; Confirmation.</summary>
public sealed record ConfirmationSettings(
    bool OnDelete = true,
    bool OnReplace = true,
    bool OnMouseAction = true,
    bool OnCopy = false,
    bool OnMove = false)
{
    public static readonly ConfirmationSettings Default = new();
}

/// <summary>Partial Details choices from View &gt; Partial Details.</summary>
public sealed record PartialDetailSettings(
    bool ShowSize = true,
    bool ShowDate = true,
    bool ShowTime = false,
    bool ShowAttributes = false)
{
    public static readonly PartialDetailSettings Default = new();
}

/// <summary>Every persisted setting, stored in Winfile.ini format exactly like the original.</summary>
public sealed class WinFileSettings
{
    public string EditorPath { get; set; } = string.Empty;
    public string CachedPath { get; set; } = string.Empty;
    public string GotoCachePunctuation { get; set; } = "-_";
    public bool ScrollOnExpand { get; set; } = true;
    public ConfirmationSettings Confirmations { get; set; } = ConfirmationSettings.Default;
    public PartialDetailSettings PartialDetails { get; set; } = PartialDetailSettings.Default;
    public bool ShowToolbar { get; set; } = true;
    public bool ShowDrivebar { get; set; } = true;
    public bool ShowStatusBar { get; set; } = true;
    public bool MinimizeOnUse { get; set; }
    public bool IndexOnLaunch { get; set; }
    public bool SaveSettingsOnExit { get; set; } = true;
    public SortMode Sort { get; set; } = SortMode.Name;
    public ViewDetailMode ViewDetails { get; set; } = ViewDetailMode.AllDetails;
    public PaneView Pane { get; set; } = PaneView.TreeAndDirectory;
    /// <summary>The View &gt; By File Type include filter, for example "*.txt;*.md" or "*.*".</summary>
    public string IncludeFilter { get; set; } = "*.*";
    public bool ShowHiddenAndSystem { get; set; }
    public string FontName { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 11;
    public string LastDirectory { get; set; } = string.Empty;
    public double SplitPosition { get; set; } = 0.35;
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    /// <summary>Toolbar button ids in display order, from Options &gt; Customize Toolbar.</summary>
    public List<string> ToolbarButtons { get; set; } = ["open", "copy", "move", "delete", "rename", "search", "properties"];
}

/// <summary>Result of a copy or move pass, including every file that failed and why.</summary>
public sealed record OperationResult(int Succeeded, IReadOnlyList<OperationFailure> Failures)
{
    public bool HasFailures => Failures.Count > 0;
    public static readonly OperationResult Empty = new(0, []);
}

public sealed record OperationFailure(string SourcePath, string Message);

/// <summary>Progress for a long copy or move, shaped like the original copy dialog.</summary>
public sealed record CopyProgress(string CurrentFile, int FilesCompleted, int FilesTotal, long BytesCompleted, long BytesTotal);
