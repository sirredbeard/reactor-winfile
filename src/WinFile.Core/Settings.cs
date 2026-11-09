using System.Globalization;

namespace WinFile.Core;

/// <summary>A small INI reader and writer. WinFile settings live in Winfile.ini, so the format matters.</summary>
public sealed class IniFile
{
    private readonly List<(string Section, string Key, string Value)> _entries = [];
    private readonly List<string> _sectionOrder = [];

    public static IniFile Parse(string text)
    {
        var ini = new IniFile();
        var section = string.Empty;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                if (!ini._sectionOrder.Contains(section, StringComparer.OrdinalIgnoreCase))
                    ini._sectionOrder.Add(section);
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            ini.Set(section, line[..eq].Trim(), line[(eq + 1)..].Trim());
        }
        return ini;
    }

    public static IniFile Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : new IniFile();

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, ToString());
    }

    public string? Get(string section, string key) =>
        _entries.FirstOrDefault(e =>
            string.Equals(e.Section, section, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    public string Get(string section, string key, string fallback) => Get(section, key) ?? fallback;

    public bool GetBool(string section, string key, bool fallback)
    {
        var value = Get(section, key);
        if (value is null) return fallback;
        return value is "1" || bool.TryParse(value, out var b) && b;
    }

    public int GetInt(string section, string key, int fallback)
    {
        var value = Get(section, key);
        return value is not null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;
    }

    public double GetDouble(string section, string key, double fallback)
    {
        var value = Get(section, key);
        return value is not null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;
    }

    public void Set(string section, string key, string value)
    {
        if (!_sectionOrder.Contains(section, StringComparer.OrdinalIgnoreCase))
            _sectionOrder.Add(section);
        var index = _entries.FindIndex(e =>
            string.Equals(e.Section, section, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _entries[index] = (_entries[index].Section, _entries[index].Key, value);
        else _entries.Add((section, key, value));
    }

    public void SetBool(string section, string key, bool value) => Set(section, key, value ? "1" : "0");

    public override string ToString()
    {
        var lines = new List<string>();
        foreach (var section in _sectionOrder)
        {
            lines.Add($"[{section}]");
            foreach (var (s, k, v) in _entries.Where(e => string.Equals(e.Section, section, StringComparison.OrdinalIgnoreCase)))
                lines.Add($"{k}={v}");
            lines.Add(string.Empty);
        }
        return string.Join("\r\n", lines);
    }
}

/// <summary>Loads and saves WinFileSettings in the original Winfile.ini layout.</summary>
public sealed class SettingsStore
{
    public const string SettingsSection = "Settings";
    public const string WindowSection = "WinFile";

    /// <summary>The original moved its ini to %AppData%\Roaming\Microsoft\WinFile in the enhanced release.</summary>
    public static string DefaultIniPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "WinFile", "Winfile.ini");

    public WinFileSettings Load(string path)
    {
        var ini = IniFile.Load(path);
        var s = new WinFileSettings();
        if (!File.Exists(path)) return s;

        s.EditorPath = ini.Get(SettingsSection, "EditorPath", s.EditorPath);
        s.CachedPath = ini.Get(SettingsSection, "CachedPath", s.CachedPath);
        s.GotoCachePunctuation = ini.Get(SettingsSection, "GotoCachePunctuation", s.GotoCachePunctuation);
        s.ScrollOnExpand = ini.GetBool(SettingsSection, "ScrollOnExpand", s.ScrollOnExpand);
        s.IncludeFilter = ini.Get(SettingsSection, "IncludeFilter", s.IncludeFilter);
        s.ShowHiddenAndSystem = ini.GetBool(SettingsSection, "ShowHiddenAndSystem", s.ShowHiddenAndSystem);
        s.FontName = ini.Get(SettingsSection, "FontName", s.FontName);
        s.FontSize = ini.GetDouble(SettingsSection, "FontSize", s.FontSize);
        s.LastDirectory = ini.Get(SettingsSection, "LastDirectory", s.LastDirectory);
        s.SplitPosition = ini.GetDouble(SettingsSection, "SplitPosition", s.SplitPosition);
        if (Enum.TryParse<SortMode>(ini.Get(SettingsSection, "Sort", string.Empty), out var sort)) s.Sort = sort;
        if (Enum.TryParse<ViewDetailMode>(ini.Get(SettingsSection, "ViewDetails", string.Empty), out var vd)) s.ViewDetails = vd;
        if (Enum.TryParse<PaneView>(ini.Get(SettingsSection, "Pane", string.Empty), out var pane)) s.Pane = pane;

        var confirm = ini.Get("Confirmation", "Flags", string.Empty);
        if (!string.IsNullOrEmpty(confirm))
        {
            var parts = confirm.Split(',');
            bool Flag(int i, bool fallback) => parts.Length > i && bool.TryParse(parts[i], out var b) ? b : fallback;
            s.Confirmations = new ConfirmationSettings(
                Flag(0, true), Flag(1, true), Flag(2, true), Flag(3, false), Flag(4, false));
        }

        var partial = ini.Get("PartialDetails", "Flags", string.Empty);
        if (!string.IsNullOrEmpty(partial))
        {
            var parts = partial.Split(',');
            bool Flag(int i, bool fallback) => parts.Length > i && bool.TryParse(parts[i], out var b) ? b : fallback;
            s.PartialDetails = new PartialDetailSettings(Flag(0, true), Flag(1, true), Flag(2, false), Flag(3, false));
        }

        s.ShowToolbar = ini.GetBool(WindowSection, "Toolbar", s.ShowToolbar);
        s.ShowDrivebar = ini.GetBool(WindowSection, "Drivebar", s.ShowDrivebar);
        s.ShowStatusBar = ini.GetBool(WindowSection, "StatusBar", s.ShowStatusBar);
        s.MinimizeOnUse = ini.GetBool(WindowSection, "MinimizeOnUse", s.MinimizeOnUse);
        s.IndexOnLaunch = ini.GetBool(WindowSection, "IndexOnLaunch", s.IndexOnLaunch);
        s.SaveSettingsOnExit = ini.GetBool(WindowSection, "SaveSettingsOnExit", s.SaveSettingsOnExit);
        var x = ini.Get(WindowSection, "WindowX", string.Empty);
        s.WindowX = string.IsNullOrEmpty(x) ? null : ini.GetInt(WindowSection, "WindowX", 0);
        var y = ini.Get(WindowSection, "WindowY", string.Empty);
        s.WindowY = string.IsNullOrEmpty(y) ? null : ini.GetInt(WindowSection, "WindowY", 0);
        var w = ini.Get(WindowSection, "WindowWidth", string.Empty);
        s.WindowWidth = string.IsNullOrEmpty(w) ? null : ini.GetInt(WindowSection, "WindowWidth", 0);
        var h = ini.Get(WindowSection, "WindowHeight", string.Empty);
        s.WindowHeight = string.IsNullOrEmpty(h) ? null : ini.GetInt(WindowSection, "WindowHeight", 0);
        var toolbar = ini.Get(WindowSection, "ToolbarButtons", string.Empty);
        if (!string.IsNullOrWhiteSpace(toolbar))
            s.ToolbarButtons = toolbar.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return s;
    }

    public void Save(string path, WinFileSettings s)
    {
        var ini = new IniFile();
        ini.Set(SettingsSection, "EditorPath", s.EditorPath);
        ini.Set(SettingsSection, "CachedPath", s.CachedPath);
        ini.Set(SettingsSection, "GotoCachePunctuation", s.GotoCachePunctuation);
        ini.SetBool(SettingsSection, "ScrollOnExpand", s.ScrollOnExpand);
        ini.Set(SettingsSection, "IncludeFilter", s.IncludeFilter);
        ini.SetBool(SettingsSection, "ShowHiddenAndSystem", s.ShowHiddenAndSystem);
        ini.Set(SettingsSection, "FontName", s.FontName);
        ini.Set(SettingsSection, "FontSize", s.FontSize.ToString(CultureInfo.InvariantCulture));
        ini.Set(SettingsSection, "LastDirectory", s.LastDirectory);
        ini.Set(SettingsSection, "SplitPosition", s.SplitPosition.ToString(CultureInfo.InvariantCulture));
        ini.Set(SettingsSection, "Sort", s.Sort.ToString());
        ini.Set(SettingsSection, "ViewDetails", s.ViewDetails.ToString());
        ini.Set(SettingsSection, "Pane", s.Pane.ToString());
        var c = s.Confirmations;
        ini.Set("Confirmation", "Flags", $"{c.OnDelete},{c.OnReplace},{c.OnMouseAction},{c.OnCopy},{c.OnMove}");
        var p = s.PartialDetails;
        ini.Set("PartialDetails", "Flags", $"{p.ShowSize},{p.ShowDate},{p.ShowTime},{p.ShowAttributes}");
        ini.SetBool(WindowSection, "Toolbar", s.ShowToolbar);
        ini.SetBool(WindowSection, "Drivebar", s.ShowDrivebar);
        ini.SetBool(WindowSection, "StatusBar", s.ShowStatusBar);
        ini.SetBool(WindowSection, "MinimizeOnUse", s.MinimizeOnUse);
        ini.SetBool(WindowSection, "IndexOnLaunch", s.IndexOnLaunch);
        ini.SetBool(WindowSection, "SaveSettingsOnExit", s.SaveSettingsOnExit);
        if (s.WindowX is { } wx) ini.Set(WindowSection, "WindowX", wx.ToString(CultureInfo.InvariantCulture));
        if (s.WindowY is { } wy) ini.Set(WindowSection, "WindowY", wy.ToString(CultureInfo.InvariantCulture));
        if (s.WindowWidth is { } ww) ini.Set(WindowSection, "WindowWidth", ww.ToString(CultureInfo.InvariantCulture));
        if (s.WindowHeight is { } wh) ini.Set(WindowSection, "WindowHeight", wh.ToString(CultureInfo.InvariantCulture));
        ini.Set(WindowSection, "ToolbarButtons", string.Join(",", s.ToolbarButtons));
        ini.Save(path);
    }
}
