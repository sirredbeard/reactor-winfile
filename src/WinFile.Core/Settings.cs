using System.Globalization;

namespace WinFile.Core;

/// <summary>
/// A small INI reader and writer. WinFile settings live in Winfile.ini, so the
/// format matters. The file is kept as raw lines: Set replaces a key's line in
/// place or inserts it into its section, Remove deletes lines, and everything
/// else, comments above all, survives a load and save cycle as written.
/// </summary>
public sealed class IniFile
{
    private readonly List<string> _lines = [];

    public IniFile() { }

    public static IniFile Parse(string text)
    {
        var ini = new IniFile();
        foreach (var raw in text.Split('\n'))
            ini._lines.Add(raw.TrimEnd('\r'));
        while (ini._lines.Count > 0 && ini._lines[^1].Length == 0)
            ini._lines.RemoveAt(ini._lines.Count - 1);
        return ini;
    }

    public static IniFile Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : new IniFile();

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, ToString());
    }

    private static bool IsSectionHeader(string line, out string name)
    {
        var t = line.Trim();
        if (t.Length >= 2 && t.StartsWith('[') && t.EndsWith(']'))
        {
            name = t[1..^1].Trim();
            return true;
        }
        name = string.Empty;
        return false;
    }

    private IEnumerable<(int Index, string Section, string Key, string Value)> KeyLines()
    {
        var section = string.Empty;
        for (var i = 0; i < _lines.Count; i++)
        {
            if (IsSectionHeader(_lines[i], out var name))
            {
                section = name;
                continue;
            }
            var t = _lines[i].Trim();
            if (t.Length == 0 || t.StartsWith(';') || t.StartsWith('#')) continue;
            var eq = t.IndexOf('=');
            if (eq < 0) continue;
            yield return (i, section, t[..eq].Trim(), t[(eq + 1)..].Trim());
        }
    }

    public string? Get(string section, string key) =>
        KeyLines().FirstOrDefault(e =>
            string.Equals(e.Section, section, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    public string Get(string section, string key, string fallback) => Get(section, key) ?? fallback;

    public bool GetBool(string section, string key, bool fallback)
    {
        var value = Get(section, key);
        if (value is null) return fallback;
        if (bool.TryParse(value, out var b)) return b;
        // The original writes its booleans as integers (GetPrivateProfileInt), so any nonzero value is true.
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i != 0 : fallback;
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
        var inTarget = false;
        var headerIndex = -1;
        var insertIndex = -1;
        for (var i = 0; i < _lines.Count; i++)
        {
            if (IsSectionHeader(_lines[i], out var name))
            {
                if (inTarget) break;
                inTarget = string.Equals(name, section, StringComparison.OrdinalIgnoreCase);
                if (inTarget)
                {
                    headerIndex = i;
                    insertIndex = i + 1;
                }
                continue;
            }
            if (!inTarget) continue;
            var t = _lines[i].Trim();
            if (t.Length == 0) continue;
            insertIndex = i + 1;
            if (t.StartsWith(';') || t.StartsWith('#')) continue;
            var eq = t.IndexOf('=');
            if (eq >= 0 && string.Equals(t[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                _lines[i] = $"{key}={value}";
                return;
            }
        }
        if (headerIndex >= 0)
        {
            _lines.Insert(insertIndex, $"{key}={value}");
            return;
        }
        if (_lines.Count > 0) _lines.Add(string.Empty);
        _lines.Add($"[{section}]");
        _lines.Add($"{key}={value}");
    }

    public void SetBool(string section, string key, bool value) => Set(section, key, value ? "1" : "0");

    public void Remove(string section, string key)
    {
        var matches = KeyLines()
            .Where(e => string.Equals(e.Section, section, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Index)
            .OrderByDescending(i => i)
            .ToList();
        foreach (var i in matches) _lines.RemoveAt(i);
        if (matches.Count == 0) return;
        // A section whose last key went away loses its header too, so a
        // migrated file does not keep empty legacy sections behind.
        if (KeyLines().Any(e => string.Equals(e.Section, section, StringComparison.OrdinalIgnoreCase))) return;
        for (var i = 0; i < _lines.Count; i++)
        {
            if (IsSectionHeader(_lines[i], out var name) &&
                string.Equals(name, section, StringComparison.OrdinalIgnoreCase))
            {
                _lines.RemoveAt(i);
                break;
            }
        }
    }

    public override string ToString() =>
        _lines.Count == 0 ? string.Empty : string.Join("\r\n", _lines) + "\r\n";
}

/// <summary>
/// Loads and saves WinFileSettings in the original's Winfile.ini layout: one
/// [Settings] section carrying the original's own key names (winfile.h in
/// microsoft/winfile), so the same Winfile.ini works in both programs. Two
/// rules keep that true. Load reads the original's keys first and falls back
/// to this app's first-release layout for anything missing. Save merges into
/// the file already on disk, so sections and keys this app does not own,
/// [AddOns] above all, survive untouched.
/// </summary>
public sealed class SettingsStore
{
    public const string SettingsSection = "Settings";
    private const string LegacyWindowSection = "WinFile";
    private const string LegacyConfirmationSection = "Confirmation";
    private const string PartialDetailsSection = "PartialDetails";

    /// <summary>The original moved its ini to %AppData%\Roaming\Microsoft\WinFile in the enhanced release.</summary>
    public static string DefaultIniPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "WinFile", "Winfile.ini");

    public WinFileSettings Load(string path)
    {
        var ini = IniFile.Load(path);
        var s = new WinFileSettings();
        if (!File.Exists(path)) return s;

        // Keys the original names the same way this app always did.
        s.EditorPath = ini.Get(SettingsSection, "EditorPath", s.EditorPath);
        s.CachedPath = ini.Get(SettingsSection, "CachedPath", s.CachedPath);
        s.GotoCachePunctuation = ini.Get(SettingsSection, "GotoCachePunctuation", s.GotoCachePunctuation);
        s.ScrollOnExpand = ini.GetBool(SettingsSection, "ScrollOnExpand", s.ScrollOnExpand);
        s.IndexOnLaunch = ini.GetBool(SettingsSection, "IndexOnLaunch",
            ini.GetBool(LegacyWindowSection, "IndexOnLaunch", s.IndexOnLaunch));
        s.IndexHiddenSystem = ini.GetBool(SettingsSection, "IndexHiddenSystem", s.IndexHiddenSystem);
        s.NewWindowOnConnect = ini.GetBool(SettingsSection, "NewWinOnNetConnect", s.NewWindowOnConnect);
        s.ChangeNotifyTimeMs = ini.GetInt(SettingsSection, "ChangeNotifyTime", s.ChangeNotifyTimeMs);

        // The original's spellings, with the first-release layout as fallback.
        s.ShowToolbar = ini.GetBool(SettingsSection, "ToolBar",
            ini.GetBool(LegacyWindowSection, "Toolbar", s.ShowToolbar));
        s.ShowDrivebar = ini.GetBool(SettingsSection, "DriveBar",
            ini.GetBool(LegacyWindowSection, "Drivebar", s.ShowDrivebar));
        s.ShowStatusBar = ini.GetBool(SettingsSection, "StatusBar",
            ini.GetBool(LegacyWindowSection, "StatusBar", s.ShowStatusBar));
        s.MinimizeOnUse = ini.GetBool(SettingsSection, "MinOnRun",
            ini.GetBool(LegacyWindowSection, "MinimizeOnUse", s.MinimizeOnUse));
        s.SaveSettingsOnExit = ini.GetBool(SettingsSection, "Save Settings",
            ini.GetBool(LegacyWindowSection, "SaveSettingsOnExit", s.SaveSettingsOnExit));
        s.FontName = ini.Get(SettingsSection, "Face",
            ini.Get(SettingsSection, "FontName", s.FontName));
        s.FontSize = ini.GetDouble(SettingsSection, "Size",
            ini.GetDouble(SettingsSection, "FontSize", s.FontSize));
        s.FontWeight = ini.GetInt(SettingsSection, "FaceWeight", s.FontWeight);

        // The original stores the text attribute bits under "LowerCase".
        var lowerBits = ini.GetInt(SettingsSection, "LowerCase", (int)s.LowerCase);
        s.LowerCase = (lowerBits & (int)LowerCaseMode.All) != 0 ? LowerCaseMode.All
            : (lowerBits & (int)LowerCaseMode.UppercaseNames) != 0 ? LowerCaseMode.UppercaseNames
            : LowerCaseMode.Off;

        s.Confirmations = LoadConfirmations(ini);
        LoadWindowPlacement(ini, s);

        // Settings only this app has. The original ignores keys it does not know.
        s.IncludeFilter = ini.Get(SettingsSection, "IncludeFilter", s.IncludeFilter);
        s.ShowHiddenAndSystem = ini.GetBool(SettingsSection, "ShowHiddenAndSystem", s.ShowHiddenAndSystem);
        s.LastDirectory = ini.Get(SettingsSection, "LastDirectory", s.LastDirectory);
        s.SplitPosition = ini.GetDouble(SettingsSection, "SplitPosition", s.SplitPosition);
        if (Enum.TryParse<SortMode>(ini.Get(SettingsSection, "Sort", string.Empty), out var sort)) s.Sort = sort;
        if (Enum.TryParse<ViewDetailMode>(ini.Get(SettingsSection, "ViewDetails", string.Empty), out var vd)) s.ViewDetails = vd;
        if (Enum.TryParse<PaneView>(ini.Get(SettingsSection, "Pane", string.Empty), out var pane)) s.Pane = pane;
        var toolbar = ini.Get(SettingsSection, "ToolbarButtons",
            ini.Get(LegacyWindowSection, "ToolbarButtons", string.Empty));
        if (!string.IsNullOrWhiteSpace(toolbar))
            s.ToolbarButtons = toolbar.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var partial = ini.Get(PartialDetailsSection, "Flags", string.Empty);
        if (!string.IsNullOrEmpty(partial))
        {
            var parts = partial.Split(',');
            bool Flag(int i, bool fallback) => parts.Length > i && bool.TryParse(parts[i], out var b) ? b : fallback;
            s.PartialDetails = new PartialDetailSettings(Flag(0, true), Flag(1, true), Flag(2, false), Flag(3, false));
        }
        return s;
    }

    private static ConfirmationSettings LoadConfirmations(IniFile ini)
    {
        var d = ConfirmationSettings.Default;
        // Each of the original's keys stands on its own: a sparse ini that sets
        // only ConfirmFormat must not fall through to defaults for that key.
        string[] keys = ["ConfirmDelete", "ConfirmSubDel", "ConfirmReplace", "ConfirmMouse", "ConfirmFormat", "ConfirmSystemHiddenReadOnly"];
        if (keys.Any(k => ini.Get(SettingsSection, k) is not null))
        {
            return new ConfirmationSettings(
                ini.GetBool(SettingsSection, "ConfirmDelete", d.OnDelete),
                ini.GetBool(SettingsSection, "ConfirmSubDel", d.OnSubtreeDelete),
                ini.GetBool(SettingsSection, "ConfirmReplace", d.OnReplace),
                ini.GetBool(SettingsSection, "ConfirmMouse", d.OnMouseAction),
                ini.GetBool(SettingsSection, "ConfirmFormat", d.OnFormat),
                ini.GetBool(SettingsSection, "ConfirmSystemHiddenReadOnly", d.OnReadOnlySystemHidden));
        }
        // First-release layout: one Flags list (delete, replace, mouse, copy, move).
        var legacy = ini.Get(LegacyConfirmationSection, "Flags", string.Empty);
        if (!string.IsNullOrEmpty(legacy))
        {
            var parts = legacy.Split(',');
            bool Flag(int i, bool fallback) => parts.Length > i && bool.TryParse(parts[i], out var b) ? b : fallback;
            return new ConfirmationSettings(
                Flag(0, d.OnDelete), d.OnSubtreeDelete, Flag(1, d.OnReplace), Flag(2, d.OnMouseAction),
                d.OnFormat, d.OnReadOnlySystemHidden);
        }
        return d;
    }

    private static void LoadWindowPlacement(IniFile ini, WinFileSettings s)
    {
        // The original writes "Window" in wfdlgs.c as "%ld,%ld,%ld,%ld, , ,%u":
        // left, top, width, height, two empty fields, and the show command.
        // Read the coordinates and the show command; Save writes the same shape.
        var window = ini.Get(SettingsSection, "Window", string.Empty);
        if (!string.IsNullOrWhiteSpace(window))
        {
            var parts = window.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            int? Field(int i) => parts.Length > i && int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
            s.WindowX = Field(0);
            s.WindowY = Field(1);
            s.WindowWidth = Field(2);
            s.WindowHeight = Field(3);
            if (Field(4) is { } show) s.WindowShowCommand = show;
            if (s.WindowX is not null) return;
        }
        var x = ini.Get(LegacyWindowSection, "WindowX", string.Empty);
        s.WindowX = string.IsNullOrEmpty(x) ? null : ini.GetInt(LegacyWindowSection, "WindowX", 0);
        var y = ini.Get(LegacyWindowSection, "WindowY", string.Empty);
        s.WindowY = string.IsNullOrEmpty(y) ? null : ini.GetInt(LegacyWindowSection, "WindowY", 0);
        var w = ini.Get(LegacyWindowSection, "WindowWidth", string.Empty);
        s.WindowWidth = string.IsNullOrEmpty(w) ? null : ini.GetInt(LegacyWindowSection, "WindowWidth", 0);
        var h = ini.Get(LegacyWindowSection, "WindowHeight", string.Empty);
        s.WindowHeight = string.IsNullOrEmpty(h) ? null : ini.GetInt(LegacyWindowSection, "WindowHeight", 0);
    }

    public void Save(string path, WinFileSettings s)
    {
        // Merge into the file on disk: keys and sections this app does not own stay as they are.
        var ini = File.Exists(path) ? IniFile.Load(path) : new IniFile();

        ini.Set(SettingsSection, "EditorPath", s.EditorPath);
        ini.Set(SettingsSection, "CachedPath", s.CachedPath);
        ini.Set(SettingsSection, "GotoCachePunctuation", s.GotoCachePunctuation);
        ini.SetBool(SettingsSection, "ScrollOnExpand", s.ScrollOnExpand);
        ini.SetBool(SettingsSection, "IndexOnLaunch", s.IndexOnLaunch);
        ini.SetBool(SettingsSection, "IndexHiddenSystem", s.IndexHiddenSystem);
        ini.SetBool(SettingsSection, "NewWinOnNetConnect", s.NewWindowOnConnect);
        ini.Set(SettingsSection, "ChangeNotifyTime", s.ChangeNotifyTimeMs.ToString(CultureInfo.InvariantCulture));
        ini.SetBool(SettingsSection, "ToolBar", s.ShowToolbar);
        ini.SetBool(SettingsSection, "DriveBar", s.ShowDrivebar);
        ini.SetBool(SettingsSection, "StatusBar", s.ShowStatusBar);
        ini.SetBool(SettingsSection, "MinOnRun", s.MinimizeOnUse);
        ini.SetBool(SettingsSection, "Save Settings", s.SaveSettingsOnExit);
        ini.Set(SettingsSection, "Face", s.FontName);
        ini.Set(SettingsSection, "Size", ((int)Math.Round(s.FontSize)).ToString(CultureInfo.InvariantCulture));
        ini.Set(SettingsSection, "FaceWeight", s.FontWeight.ToString(CultureInfo.InvariantCulture));
        ini.Set(SettingsSection, "LowerCase", ((int)s.LowerCase).ToString(CultureInfo.InvariantCulture));

        var c = s.Confirmations;
        ini.SetBool(SettingsSection, "ConfirmDelete", c.OnDelete);
        ini.SetBool(SettingsSection, "ConfirmSubDel", c.OnSubtreeDelete);
        ini.SetBool(SettingsSection, "ConfirmReplace", c.OnReplace);
        ini.SetBool(SettingsSection, "ConfirmMouse", c.OnMouseAction);
        ini.SetBool(SettingsSection, "ConfirmFormat", c.OnFormat);
        ini.SetBool(SettingsSection, "ConfirmSystemHiddenReadOnly", c.OnReadOnlySystemHidden);

        if (s.WindowX is { } wx && s.WindowY is { } wy && s.WindowWidth is { } ww && s.WindowHeight is { } wh)
            ini.Set(SettingsSection, "Window", $"{wx},{wy},{ww},{wh}, , ,{s.WindowShowCommand}");

        ini.Set(SettingsSection, "IncludeFilter", s.IncludeFilter);
        ini.SetBool(SettingsSection, "ShowHiddenAndSystem", s.ShowHiddenAndSystem);
        ini.Set(SettingsSection, "LastDirectory", s.LastDirectory);
        ini.Set(SettingsSection, "SplitPosition", s.SplitPosition.ToString(CultureInfo.InvariantCulture));
        ini.Set(SettingsSection, "Sort", s.Sort.ToString());
        ini.Set(SettingsSection, "ViewDetails", s.ViewDetails.ToString());
        ini.Set(SettingsSection, "Pane", s.Pane.ToString());
        ini.Set(SettingsSection, "ToolbarButtons", string.Join(",", s.ToolbarButtons));
        var p = s.PartialDetails;
        ini.Set(PartialDetailsSection, "Flags", $"{p.ShowSize},{p.ShowDate},{p.ShowTime},{p.ShowAttributes}");

        RemoveLegacyKeys(ini);
        ini.Save(path);
    }

    // First-release keys, superseded by the original's layout above. Removing
    // them on save converges an old file instead of leaving two sources of truth.
    private static void RemoveLegacyKeys(IniFile ini)
    {
        foreach (var key in new[] { "Toolbar", "Drivebar", "StatusBar", "MinimizeOnUse", "IndexOnLaunch",
                     "SaveSettingsOnExit", "WindowX", "WindowY", "WindowWidth", "WindowHeight", "ToolbarButtons" })
            ini.Remove(LegacyWindowSection, key);
        ini.Remove(LegacyConfirmationSection, "Flags");
        ini.Remove(SettingsSection, "FontName");
        ini.Remove(SettingsSection, "FontSize");
    }
}
