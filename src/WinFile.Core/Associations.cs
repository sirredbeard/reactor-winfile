using Microsoft.Win32;

namespace WinFile.Core;

/// <summary>Where file associations live. The registry is the real store on Windows. Tests use the in-memory store.</summary>
public interface IAssociationStore
{
    string? GetCommand(string extension);
    void SetCommand(string extension, string openCommand);
    void Remove(string extension);
    IReadOnlyDictionary<string, string> GetAll();
}

/// <summary>HKEY_CLASSES_ROOT based associations: .ext points through a ProgID to a shell open command.</summary>
public sealed class RegistryAssociationStore : IAssociationStore
{
    public string? GetCommand(string extension)
    {
        if (!OperatingSystem.IsWindows()) return null;
        extension = Normalize(extension);
        using var extKey = Registry.ClassesRoot.OpenSubKey(extension);
        var progId = extKey?.GetValue(null) as string;
        if (string.IsNullOrEmpty(progId)) return null;
        using var commandKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
        return commandKey?.GetValue(null) as string;
    }

    public void SetCommand(string extension, string openCommand)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Registry associations are only available on Windows.");
        extension = Normalize(extension);
        var progId = "WinFile" + extension.TrimStart('.').ToUpperInvariant() + "File";
        using (var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}"))
            extKey.SetValue(null, progId);
        using var commandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\shell\open\command");
        commandKey.SetValue(null, openCommand);
    }

    public void Remove(string extension)
    {
        if (!OperatingSystem.IsWindows()) return;
        extension = Normalize(extension);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{extension}", throwOnMissingSubKey: false);
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows()) return all;
        using var root = Registry.ClassesRoot;
        foreach (var name in root.GetSubKeyNames())
        {
            if (!name.StartsWith('.')) continue;
            var command = GetCommand(name);
            if (command is not null) all[name] = command;
        }
        return all;
    }

    internal static string Normalize(string extension) =>
        extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
}

public sealed class InMemoryAssociationStore : IAssociationStore
{
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public string? GetCommand(string extension) =>
        _map.TryGetValue(RegistryAssociationStore.Normalize(extension), out var command) ? command : null;

    public void SetCommand(string extension, string openCommand) =>
        _map[RegistryAssociationStore.Normalize(extension)] = openCommand;

    public void Remove(string extension) => _map.Remove(RegistryAssociationStore.Normalize(extension));

    public IReadOnlyDictionary<string, string> GetAll() => new Dictionary<string, string>(_map, StringComparer.OrdinalIgnoreCase);
}

/// <summary>File &gt; Associate. Resolves and sets the program that opens a file type.</summary>
public sealed class AssociationService
{
    private readonly IAssociationStore _store;

    public AssociationService(IAssociationStore? store = null) => _store = store ?? new RegistryAssociationStore();

    public string? GetOpenCommand(string pathOrExtension)
    {
        var ext = pathOrExtension.Contains('.') && !pathOrExtension.StartsWith('.')
            ? Path.GetExtension(pathOrExtension)
            : pathOrExtension;
        if (string.IsNullOrEmpty(ext)) return null;
        return _store.GetCommand(ext);
    }

    /// <summary>Builds a standard open command: "program" "%1".</summary>
    public void Associate(string extension, string programPath) =>
        _store.SetCommand(extension, $"\"{programPath}\" \"%1\"");

    public void AssociateWithCommand(string extension, string openCommand) => _store.SetCommand(extension, openCommand);

    public void Remove(string extension) => _store.Remove(extension);

    public IReadOnlyDictionary<string, string> GetAll() => _store.GetAll();

    /// <summary>Expands an open command template for one concrete file, replacing %1.</summary>
    public static (string FileName, string Arguments) ExpandCommand(string openCommand, string filePath)
    {
        var expanded = openCommand.Replace("%1", filePath, StringComparison.OrdinalIgnoreCase);
        var trimmed = expanded.Trim();
        if (trimmed.StartsWith('"'))
        {
            var close = trimmed.IndexOf('"', 1);
            if (close > 0)
                return (trimmed[1..close], trimmed[(close + 1)..].Trim());
        }
        var space = trimmed.IndexOf(' ');
        return space < 0 ? (trimmed, string.Empty) : (trimmed[..space], trimmed[(space + 1)..].Trim());
    }
}
