using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace WinFile.Core;

/// <summary>One access control entry, flattened for display in the Security dialogs.</summary>
public sealed record AclEntry(string Identity, string Rights, string Type, bool IsInherited);

/// <summary>Everything the Security menu shows for one file or directory.</summary>
public sealed record SecurityInfo(string Path, string Owner, IReadOnlyList<AclEntry> Entries);

/// <summary>Security &gt; Permissions, Auditing, and Owner. ACL reads work through the .NET access control APIs.</summary>
public sealed class SecurityService
{
    public SecurityInfo GetPermissions(string path)
    {
        if (Directory.Exists(path))
        {
            var security = new DirectoryInfo(path).GetAccessControl();
            return Build(path, security);
        }
        var fileSecurity = new FileInfo(path).GetAccessControl();
        return Build(path, fileSecurity);
    }

    private static SecurityInfo Build(string path, FileSystemSecurity security)
    {
        var owner = SafeOwner(security);
        var entries = new List<AclEntry>();
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(NTAccount)))
        {
            entries.Add(new AclEntry(
                rule.IdentityReference.Value,
                rule.FileSystemRights.ToString(),
                rule.AccessControlType.ToString(),
                rule.IsInherited));
        }
        return new SecurityInfo(path, owner, entries);
    }

    private static string SafeOwner(FileSystemSecurity security)
    {
        try { return security.GetOwner(typeof(NTAccount))?.Value ?? string.Empty; }
        catch { return string.Empty; }
    }

    public void SetOwner(string path, string accountName)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Setting an owner requires Windows.");
        var account = new NTAccount(accountName);
        if (Directory.Exists(path))
        {
            var dir = new DirectoryInfo(path);
            var security = dir.GetAccessControl();
            security.SetOwner(account);
            dir.SetAccessControl(security);
        }
        else
        {
            var file = new FileInfo(path);
            var security = file.GetAccessControl();
            security.SetOwner(account);
            file.SetAccessControl(security);
        }
    }

    public void Grant(string path, string accountName, FileSystemRights rights, bool allow = true)
    {
        var rule = new FileSystemAccessRule(
            new NTAccount(accountName), rights,
            allow ? AccessControlType.Allow : AccessControlType.Deny);
        if (Directory.Exists(path))
        {
            var dir = new DirectoryInfo(path);
            var security = dir.GetAccessControl();
            security.AddAccessRule(rule);
            dir.SetAccessControl(security);
        }
        else
        {
            var file = new FileInfo(path);
            var security = file.GetAccessControl();
            security.AddAccessRule(rule);
            file.SetAccessControl(security);
        }
    }

    /// <summary>Auditing rules (SACL). Reading them needs SeSecurityPrivilege, so failures come back as an empty list with the reason.</summary>
    public (IReadOnlyList<AclEntry> Entries, string? Error) GetAuditing(string path)
    {
        try
        {
            FileSystemSecurity security = Directory.Exists(path)
                ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Audit)
                : new FileInfo(path).GetAccessControl(AccessControlSections.Audit);
            var entries = new List<AclEntry>();
            foreach (FileSystemAuditRule rule in security.GetAuditRules(true, true, typeof(NTAccount)))
            {
                entries.Add(new AclEntry(rule.IdentityReference.Value, rule.FileSystemRights.ToString(), rule.AuditFlags.ToString(), rule.IsInherited));
            }
            return (entries, null);
        }
        catch (Exception ex)
        {
            return ([], ex.Message);
        }
    }
}

/// <summary>Disk &gt; Label Disk, Format Drive, Copy Disk, and Select Drive.</summary>
public sealed class DiskService
{
    public string GetLabel(string rootPath) => new DriveInfo(rootPath).VolumeLabel;

    public void SetLabel(string rootPath, string label)
    {
        new DriveInfo(rootPath).VolumeLabel = label;
    }

    /// <summary>Runs the Windows format command the way the original ran the format API: explicit, confirmed by the caller.</summary>
    public async Task<int> FormatAsync(string rootPath, string fileSystem = "NTFS", string? label = null, bool quick = true, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Format is only available on Windows.");
        var letter = rootPath.TrimEnd('\\', '/');
        var args = $"{letter} /FS:{fileSystem} /Y" + (quick ? " /Q" : string.Empty) + (label is null ? string.Empty : $" /V:{label}");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "format.com",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = false,
        }) ?? throw new InvalidOperationException("format.com did not start.");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    /// <summary>
    /// Disk copy on modern media: the original copied floppies sector by sector.
    /// Removable drives now hold ordinary file systems, so the copy is a full recursive file copy.
    /// </summary>
    public OperationResult CopyDisk(string sourceRoot, string destinationRoot, IProgress<CopyProgress>? progress = null)
    {
        var ops = new FileOperations();
        var sources = Directory.EnumerateFileSystemEntries(sourceRoot).ToList();
        return ops.Copy(sources, destinationRoot, ConflictAction.Overwrite, progress);
    }
}

/// <summary>Network drive connections. The modern tree also reaches UNC paths directly, like the enhanced WinFile.</summary>
public sealed class NetworkDriveService
{
    public IReadOnlyList<string> GetMappedDrives() =>
        DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Network)
            .Select(d => d.RootDirectory.FullName)
            .ToList();

    public void Connect(string driveLetter, string uncPath, string? userName = null, string? password = null)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Mapping a drive requires Windows.");
        var resource = new NetResource
        {
            ResourceType = 1, // RESOURCETYPE_DISK
            LocalName = driveLetter.TrimEnd('\\'),
            RemoteName = uncPath,
        };
        var result = NetworkNative.WNetAddConnection2(ref resource, password, userName, 0);
        if (result != 0) throw new IOException($"WNetAddConnection2 failed: {result}");
    }

    public void Disconnect(string driveLetter, bool force = false)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Unmapping a drive requires Windows.");
        var result = NetworkNative.WNetCancelConnection2(driveLetter.TrimEnd('\\'), 0, force);
        if (result != 0) throw new IOException($"WNetCancelConnection2 failed: {result}");
    }

    public static bool IsUncPath(string path) => path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int Scope;
        public int ResourceType;
        public int DisplayType;
        public int Usage;
        public string LocalName;
        public string RemoteName;
        public string Comment;
        public string Provider;
    }

    private static class NetworkNative
    {
        [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int WNetAddConnection2(ref NetResource netResource, string? password, string? userName, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int WNetCancelConnection2(string name, int flags, bool force);
    }
}

/// <summary>Launches files and programs. The managed equivalent of the original run, open, print, and shell code.</summary>
public sealed class ShellService
{
    public void Open(string path) => Start(new ProcessStartInfo
    {
        FileName = Formatting.StripLongPathPrefix(path),
        UseShellExecute = true,
    });

    /// <summary>Ctrl+Enter in the enhanced WinFile: open as administrator.</summary>
    public void OpenElevated(string path) => Start(new ProcessStartInfo
    {
        FileName = Formatting.StripLongPathPrefix(path),
        UseShellExecute = true,
        Verb = "runas",
    });

    /// <summary>F12 in the enhanced WinFile: open in the configured editor, Notepad by default.</summary>
    public void Edit(string path, string editorPath)
    {
        var editor = string.IsNullOrWhiteSpace(editorPath) ? "notepad.exe" : editorPath;
        Start(new ProcessStartInfo
        {
            FileName = editor,
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }

    /// <summary>File &gt; Run. Parses a command line the way the original dialog did: first token is the program.</summary>
    public void Run(string commandLine, string workingDirectory, bool elevated = false)
    {
        var (fileName, arguments) = AssociationService.ExpandCommand(commandLine.Trim(), string.Empty);
        // ExpandCommand replaces %1 with an empty string here, so a trailing quoted path survives intact.
        Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments.Trim(),
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            Verb = elevated ? "runas" : string.Empty,
        });
    }

    public void Print(string path) => Start(new ProcessStartInfo
    {
        FileName = Formatting.StripLongPathPrefix(path),
        UseShellExecute = true,
        Verb = "print",
    });

    /// <summary>File &gt; Start: shells and Explorer rooted at the current directory. Ctrl+K, Ctrl+P, Ctrl+B, Ctrl+E.</summary>
    public void StartShell(ShellKind kind, string directory, bool elevated = false)
    {
        var (fileName, args) = kind switch
        {
            ShellKind.Cmd => ("cmd.exe", string.Empty),
            ShellKind.PowerShell => ("powershell.exe", "-NoExit"),
            ShellKind.Bash => ("bash.exe", string.Empty),
            ShellKind.Explorer => ("explorer.exe", $"\"{directory}\""),
            _ => ("cmd.exe", string.Empty),
        };
        Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = directory,
            UseShellExecute = true,
            Verb = elevated ? "runas" : string.Empty,
        });
    }

    private static void Start(ProcessStartInfo info) => Process.Start(info);
}

/// <summary>Send to the Recycle Bin instead of a permanent delete, through the shell.</summary>
public sealed class RecycleBinService
{
    public void SendToRecycleBin(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The Recycle Bin is a Windows feature.");
        // SHFileOperation with FO_DELETE and FOF_ALLOWUNDO is the API the original era used.
        var from = path + '\0' + '\0';
        var op = new ShFileOpStruct
        {
            WFunc = 0x0003, // FO_DELETE
            PFrom = from,
            Flags = 0x0040 | 0x0010 | 0x0004, // ALLOWUNDO | NOCONFIRMATION | SILENT is not set on purpose
        };
        var result = ShellNative.SHFileOperation(ref op);
        if (result != 0) throw new IOException($"Recycle Bin operation failed: {result}");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr Hwnd;
        public uint WFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string PFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? PTo;
        public ushort Flags;
        public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ProgressTitle;
    }

    private static class ShellNative
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHFileOperation(ref ShFileOpStruct op);
    }
}
