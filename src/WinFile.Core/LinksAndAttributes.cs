using System.Runtime.InteropServices;

namespace WinFile.Core;

/// <summary>
/// Symbolic links, hard links, and junctions. The enhanced WinFile creates symlinks with Ctrl+Shift drag,
/// hard links and junctions with Ctrl+Shift+Alt drag, and all three from the File menu (F11 / Shift+F11).
/// </summary>
public static class LinkService
{
    public static void CreateSymbolicLink(string linkPath, string targetPath, bool targetIsDirectory)
    {
        if (targetIsDirectory) Directory.CreateSymbolicLink(linkPath, targetPath);
        else File.CreateSymbolicLink(linkPath, targetPath);
    }

    /// <summary>Hard links need the Win32 CreateHardLink API. There is no managed equivalent.</summary>
    public static void CreateHardLink(string linkPath, string targetFilePath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Hard links are only supported on Windows.");
        if (!NativeMethods.CreateHardLinkW(linkPath, targetFilePath, IntPtr.Zero))
            throw new IOException($"CreateHardLink failed for {linkPath}: {Marshal.GetLastWin32Error()}");
    }

    /// <summary>
    /// A junction is a reparse point that redirects one directory to another local directory.
    /// Managed code has no junction API, so this writes the reparse data with FSCTL_SET_REPARSE_POINT.
    /// </summary>
    public static void CreateJunction(string junctionPath, string targetDirectory)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Junctions are only supported on Windows.");
        Directory.CreateDirectory(junctionPath);
        var target = Path.GetFullPath(targetDirectory);
        var ntPath = @"\??\" + target.TrimEnd('\\');

        using var handle = NativeMethods.CreateFileW(
            junctionPath,
            NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_OPEN_REPARSE_POINT | NativeMethods.FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException($"Opening the junction directory failed: {Marshal.GetLastWin32Error()}");

        var substitute = System.Text.Encoding.Unicode.GetBytes(ntPath);
        var printName = System.Text.Encoding.Unicode.GetBytes(target);
        // REPARSE_DATA_BUFFER for IO_REPARSE_TAG_MOUNT_POINT:
        // tag (4) + length (2) + reserved (2) + substitute offset (2) + substitute length (2)
        // + print offset (2) + print length (2) + path buffer.
        var pathBufferLength = substitute.Length + 2 + printName.Length + 2;
        var bufferSize = 16 + pathBufferLength;
        var buffer = new byte[bufferSize];
        BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), 0xA0000003u); // IO_REPARSE_TAG_MOUNT_POINT
        BitConverter.TryWriteBytes(buffer.AsSpan(4, 2), (ushort)(pathBufferLength + 8));
        BitConverter.TryWriteBytes(buffer.AsSpan(8, 2), (ushort)0); // substitute name offset
        BitConverter.TryWriteBytes(buffer.AsSpan(10, 2), (ushort)substitute.Length);
        BitConverter.TryWriteBytes(buffer.AsSpan(12, 2), (ushort)(substitute.Length + 2)); // print name offset
        BitConverter.TryWriteBytes(buffer.AsSpan(14, 2), (ushort)printName.Length);
        substitute.CopyTo(buffer.AsSpan(16));
        printName.CopyTo(buffer.AsSpan(16 + substitute.Length + 2));

        var ok = NativeMethods.DeviceIoControl(
            handle, NativeMethods.FSCTL_SET_REPARSE_POINT, buffer, bufferSize,
            IntPtr.Zero, 0, out _, IntPtr.Zero);
        if (!ok)
        {
            var error = Marshal.GetLastWin32Error();
            try { Directory.Delete(junctionPath); } catch { }
            throw new IOException($"Setting the junction reparse point failed: {error}");
        }
    }

    public static bool IsJunction(string path)
    {
        var info = new DirectoryInfo(path);
        return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0 && info.LinkTarget is not null;
    }
}

/// <summary>File &gt; Properties attribute editing. The managed equivalent of the original attributes dialog.</summary>
public static class AttributesService
{
    public static FileAttributes Get(string path) => File.GetAttributes(path);

    public static void Set(string path, bool? readOnly = null, bool? hidden = null, bool? archive = null, bool? system = null)
    {
        var attrs = File.GetAttributes(path);
        attrs = Apply(attrs, FileAttributes.ReadOnly, readOnly);
        attrs = Apply(attrs, FileAttributes.Hidden, hidden);
        attrs = Apply(attrs, FileAttributes.Archive, archive);
        attrs = Apply(attrs, FileAttributes.System, system);
        File.SetAttributes(path, attrs);
    }

    private static FileAttributes Apply(FileAttributes attrs, FileAttributes flag, bool? value) => value switch
    {
        true => attrs | flag,
        false => attrs & ~flag,
        null => attrs,
    };

    /// <summary>The attribute letters the original shows in details view: R, H, A, S, C, and L for links.</summary>
    public static string ToLetters(FileEntry entry)
    {
        Span<char> letters = stackalloc char[6];
        letters[0] = entry.IsReadOnly ? 'R' : '-';
        letters[1] = entry.IsHidden ? 'H' : '-';
        letters[2] = entry.IsArchive ? 'A' : '-';
        letters[3] = entry.IsSystem ? 'S' : '-';
        letters[4] = entry.IsCompressed ? 'C' : '-';
        letters[5] = entry.IsReparsePoint ? 'L' : '-';
        return new string(letters);
    }
}

/// <summary>File &gt; Compress and Uncompress. NTFS compression runs through compact.exe, as the shell itself does.</summary>
public sealed class CompressionService
{
    public bool IsCompressed(string path) => (File.GetAttributes(path) & FileAttributes.Compressed) != 0;

    public void Compress(string path) => RunCompact(path, compress: true);
    public void Uncompress(string path) => RunCompact(path, compress: false);

    private static void RunCompact(string path, bool compress)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("NTFS compression is only available on Windows.");
        var args = compress ? $"/C /S:\"{path}\"" : $"/U /S:\"{path}\"";
        if (File.Exists(path)) args = compress ? $"/C \"{path}\"" : $"/U \"{path}\"";
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "compact.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("compact.exe did not start.");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException($"compact.exe exited with code {process.ExitCode}.");
    }
}

internal static partial class NativeMethods
{
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    public const uint FSCTL_SET_REPARSE_POINT = 0x000900A4;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeviceIoControl(
        Microsoft.Win32.SafeHandles.SafeFileHandle hDevice, uint dwIoControlCode,
        byte[] lpInBuffer, int nInBufferSize, IntPtr lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);
}
