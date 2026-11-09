using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using static Microsoft.UI.Reactor.Factories;
using WinFile.Core;

namespace ReactorWinFile;

/// <summary>Which dialog the frame is showing, if any. Dialogs render as an inline panel under the menu bar.</summary>
public enum DialogKind
{
    None, Move, Copy, Rename, CreateDirectory, Search, Goto, Run, Associate, Properties,
    SelectFiles, Label, Format, CopyDisk, ConnectNetwork, DisconnectNetwork, SelectDrive,
    Font, CustomizeToolbar, Preferences, ByFileType, Split, Permissions, Auditing, Owner,
    Message, About, HelpContents, HelpSearch, HowToUseHelp, Link, Confirm,
}

/// <summary>All mutable frame state. The component owns one instance and re-renders by bumping a version counter.</summary>
public sealed class FrameModel
{
    public DirectoryScanner Scanner { get; } = new();
    public FileOperations Ops { get; } = new();
    public SearchService Search { get; } = new();
    public ShellService Shell { get; } = new();
    public DiskService Disk { get; } = new();
    public NetworkDriveService Network { get; } = new();
    public SecurityService Security { get; } = new();
    public AssociationService Associations { get; } = new();
    public CompressionService Compression { get; } = new();
    public SettingsStore SettingsStore { get; } = new();
    public ClipboardModel Clipboard { get; } = new();
    public MdiManager Mdi { get; } = new();

    public WinFileSettings Settings { get; set; }
    public string SettingsPath { get; }
    public ChildWindow Window { get; set; }
    public IReadOnlyList<FileEntry> Entries { get; set; } = [];
    public DirectoryTreeNode? TreeRoot { get; set; }
    public HashSet<string> ExpandedTreePaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IndicateExpandable { get; set; } = true;
    public string StatusText { get; set; } = string.Empty;
    public bool SearchMode { get; set; }
    public GotoIndex? GotoIndex { get; set; }

    public DialogKind Dialog { get; set; } = DialogKind.None;
    /// <summary>0 for a symbolic link, 1 for a hard link, 2 for a junction. Set when the Link dialog opens.</summary>
    public int LinkMode { get; set; }
    public string DialogInput1 { get; set; } = string.Empty;
    public string DialogInput2 { get; set; } = string.Empty;
    public string DialogBody { get; set; } = string.Empty;

    public FrameModel()
    {
        SettingsPath = SettingsStore.DefaultIniPath;
        Settings = SettingsStore.Load(SettingsPath);
        var start = WinFileApp.StartDirectory
            ?? (Settings.LastDirectory.Length > 0 && Directory.Exists(Settings.LastDirectory)
                ? Settings.LastDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Window = Mdi.NewWindow(start);
        if (Settings.IndexOnLaunch) EnsureGotoIndex();
        RefreshTreeRoots();
        Refresh();
    }

    public string CurrentPath => Window.DirectoryPath;
    public IReadOnlyList<string> Selection => Window.Selection;

    public string? SelectedPath => Window.Selection.Count > 0 ? Window.Selection[^1] : null;

    public void NavigateTo(string path)
    {
        Window.History.NavigateTo(path);
        Window.DirectoryPath = path;
        Window.Selection.Clear();
        SearchMode = false;
        Refresh();
    }

    public void Refresh()
    {
        try
        {
            Entries = SearchMode ? Entries : Scanner.ListDirectory(CurrentPath, Settings);
            var drive = Scanner.GetDriveForPath(CurrentPath);
            long selectedBytes = 0;
            foreach (var path in Window.Selection)
            {
                var entry = Entries.FirstOrDefault(e => string.Equals(e.FullPath, path, StringComparison.OrdinalIgnoreCase));
                if (entry is not null) selectedBytes += entry.Size;
            }
            StatusText = Formatting.FormatStatusLine(CurrentPath, Entries.Count, selectedBytes, drive);
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Entries = [];
        }
    }

    public void RefreshTreeRoots()
    {
        TreeRoot = null;
    }

    public DirectoryTreeNode TreeForCurrentDrive()
    {
        var rootPath = Path.GetPathRoot(CurrentPath) ?? CurrentPath;
        TreeRoot ??= new DirectoryTreeNode(rootPath, Scanner);
        if (!string.Equals(TreeRoot.Path, rootPath, StringComparison.OrdinalIgnoreCase))
            TreeRoot = new DirectoryTreeNode(rootPath, Scanner);
        return TreeRoot;
    }

    public void EnsureGotoIndex()
    {
        GotoIndex ??= GotoIndex.BuildFromDrives(Settings.GotoCachePunctuation,
            includeHiddenSystem: Settings.IndexHiddenSystem);
    }

    public void SaveSettings()
    {
        Settings.LastDirectory = CurrentPath;
        SettingsStore.Save(SettingsPath, Settings);
    }

    public void Report(OperationResult result, string verb)
    {
        if (result.HasFailures)
        {
            OpenDialog(DialogKind.Message, $"{verb} finished with errors",
                string.Join("\n", result.Failures.Select(f => $"{f.SourcePath}: {f.Message}")));
        }
        Refresh();
    }

    public void OpenDialog(DialogKind kind, string title, string body = "", string input1 = "", string input2 = "")
    {
        Dialog = kind;
        DialogTitle = title;
        DialogBody = body;
        DialogInput1 = input1;
        DialogInput2 = input2;
    }

    /// <summary>The action a Confirm dialog runs when the user picks OK. Set by OpenConfirm.</summary>
    public Action? PendingConfirm { get; set; }

    public void OpenConfirm(string title, string body, Action action)
    {
        PendingConfirm = action;
        OpenDialog(DialogKind.Confirm, title, body);
    }

    public string DialogTitle { get; set; } = string.Empty;
}

/// <summary>
/// The Reactor frame: menu bar, toolbar, drive bar, tree pane, directory pane, and status bar.
/// Every menu item from the original File Manager is here and wired to WinFile.Core.
/// </summary>
public sealed class WinFileApp : Component
{
    public static string? StartDirectory { get; set; }

    private readonly FrameModel _model = new();
    private DirectoryWatcher? _watcher;
    private string? _watchedPath;
    private Action? _bump;

    public override Element Render()
    {
        var (version, setVersion) = UseState(0);
        void Bump() => setVersion(version + 1);
        _bump = Bump;
        void Act(Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                _model.OpenDialog(DialogKind.Message, "File Manager", ex.Message);
            }
            Bump();
        }

        EnsureWatcher(Bump);

        var children = new List<Element>
        {
            (TitleBar("Windows File Manager") with { Subtitle = _model.CurrentPath }).Flex(shrink: 0),
            BuildMenuBar(Act),
        };
        if (_model.Settings.ShowToolbar) children.Add(BuildToolbar(Act));
        if (_model.Settings.ShowDrivebar) children.Add(BuildDrivebar(Act));
        if (_model.Dialog != DialogKind.None) children.Add(BuildDialog(Act, Bump));
        if (_model.Mdi.Windows.Count > 1) children.Add(BuildWindowTabs(Act));
        children.Add(BuildPanes(Act).Flex(grow: 1));
        if (_model.Settings.ShowStatusBar) children.Add(TextBlock(_model.StatusText).Margin(horizontal: 8, vertical: 4));

        return FlexColumn(children.ToArray());
    }

    private void EnsureWatcher(Action bump)
    {
        if (_watchedPath == _model.CurrentPath && _watcher is not null) return;
        _watcher?.Dispose();
        _watchedPath = _model.CurrentPath;
        try
        {
            _watcher = new DirectoryWatcher(_model.CurrentPath,
                TimeSpan.FromMilliseconds(Math.Max(0, _model.Settings.ChangeNotifyTimeMs)));
            _watcher.Changed += (_, _) =>
            {
                if (!_model.SearchMode) _model.Refresh();
                bump();
            };
        }
        catch
        {
            _watcher = null;
        }
    }

    // ─── Menu bar ────────────────────────────────────────────────────────────

    private Element BuildMenuBar(Action<Action> act)
    {
        var s = _model.Settings;
        var sel = _model.SelectedPath;
        return MenuBar(
            Menu("File",
                MenuItem("Open\tEnter", () => act(() => OpenSelected(elevated: false))),
                MenuItem("Edit\tF12", () => act(() => { if (sel is not null) _model.Shell.Edit(sel, s.EditorPath); })),
                MenuItem("Move...\tF7", () => act(() => RequireSelection(sel, p =>
                    _model.OpenDialog(DialogKind.Move, "Move", $"Move {p} to a directory:", Path.GetDirectoryName(p) ?? string.Empty)))),
                MenuItem("Copy...\tF8", () => act(() => RequireSelection(sel, p =>
                    _model.OpenDialog(DialogKind.Copy, "Copy", $"Copy {p} to a directory:", Path.GetDirectoryName(p) ?? string.Empty)))),
                MenuItem("Symlink...\tF11", () => act(() => RequireSelection(sel, p =>
                {
                    _model.LinkMode = 0;
                    _model.OpenDialog(DialogKind.Link, "Create Symbolic Link", $"Link path for a link to {p}:",
                        Path.Combine(Path.GetDirectoryName(p) ?? string.Empty, Path.GetFileName(p) + " - Link"));
                }))),
                MenuItem("Hardlink...\tShift+F11", () => act(() => RequireSelection(sel, p =>
                {
                    _model.LinkMode = Directory.Exists(p) ? 2 : 1;
                    _model.OpenDialog(DialogKind.Link, Directory.Exists(p) ? "Create Junction" : "Create Hard Link",
                        $"Link path for a link to {p}:",
                        Path.Combine(Path.GetDirectoryName(p) ?? string.Empty, Path.GetFileName(p) + " - Link"));
                }))),
                MenuItem("Copy to Clipboard\tCtrl+C", () => act(() => { _model.Clipboard.Copy(_model.Selection.ToList()); })),
                MenuItem("Cut to Clipboard\tCtrl+X", () => act(() => { _model.Clipboard.Cut(_model.Selection.ToList()); })),
                MenuItem("Paste\tCtrl+V", () => act(() => _model.Report(_model.Clipboard.Paste(_model.CurrentPath, ConflictChoice()), "Paste"))),
                MenuItem("Delete...\tDel", () => act(() => RunDelete())),
                MenuItem("Rename...\tF2", () => act(() => RequireSelection(sel, p =>
                    _model.OpenDialog(DialogKind.Rename, "Rename", $"Rename {p} to:", Path.GetFileName(p))))),
                MenuItem("Properties...\tAlt+Enter", () => act(() => RequireSelection(sel, ShowProperties))),
                MenuSeparator(),
                MenuItem("Compress...", () => act(() => RequireSelection(sel, p => _model.Compression.Compress(p)))),
                MenuItem("Uncompress...", () => act(() => RequireSelection(sel, p => _model.Compression.Uncompress(p)))),
                MenuSeparator(),
                MenuItem("Run...", () => act(() => _model.OpenDialog(DialogKind.Run, "Run", "Command line:", string.Empty))),
                MenuItem("Print...", () => act(() => RequireSelection(sel, p => _model.Shell.Print(p)))),
                MenuItem("Associate...", () => act(() => _model.OpenDialog(DialogKind.Associate, "Associate",
                    "Extension and program, for example .md and notepad.exe:", ".txt", "notepad.exe"))),
                MenuSeparator(),
                MenuItem("Create Directory...\tCtrl+Shift+N", () => act(() =>
                    _model.OpenDialog(DialogKind.CreateDirectory, "Create Directory", "Name of the new directory:", "New Folder"))),
                MenuItem("Search...\tCtrl+F", () => act(() =>
                    _model.OpenDialog(DialogKind.Search, "Search", "File pattern to search for under the current directory:", "*.*"))),
                MenuItem("Select Files...", () => act(() =>
                    _model.OpenDialog(DialogKind.SelectFiles, "Select Files", "Select entries matching this pattern:", "*.*"))),
                MenuSubItem("Start",
                    MenuItem("Bash Shell...\tCtrl+B", () => act(() => _model.Shell.StartShell(ShellKind.Bash, _model.CurrentPath))),
                    MenuItem("Cmd Shell...\tCtrl+K", () => act(() => _model.Shell.StartShell(ShellKind.Cmd, _model.CurrentPath))),
                    MenuItem("PowerShell...\tCtrl+P", () => act(() => _model.Shell.StartShell(ShellKind.PowerShell, _model.CurrentPath))),
                    MenuItem("Explorer...\tCtrl+E", () => act(() => _model.Shell.StartShell(ShellKind.Explorer, _model.CurrentPath))),
                    MenuItem("Goto Directory...\tCtrl+G", () => act(() =>
                        _model.OpenDialog(DialogKind.Goto, "Goto Directory", "Type a few words from the path:", string.Empty)))),
                MenuSeparator(),
                MenuItem("Exit", () => act(() =>
                {
                    if (_model.Settings.SaveSettingsOnExit) _model.SaveSettings();
                    Microsoft.UI.Xaml.Application.Current.Exit();
                }))),
            Menu("Disk",
                MenuItem("Copy Disk...", () => act(() =>
                    _model.OpenDialog(DialogKind.CopyDisk, "Copy Disk", "Destination root for a full copy of the current drive:", string.Empty))),
                MenuItem("Label Disk...", () => act(() =>
                    _model.OpenDialog(DialogKind.Label, "Label Disk", "Volume label:", _model.Disk.GetLabel(Path.GetPathRoot(_model.CurrentPath) ?? _model.CurrentPath)))),
                MenuSeparator(),
                MenuItem("Format Drive...", () => act(() =>
                    _model.OpenDialog(DialogKind.Format, "Format Drive", "File system and optional volume label for the current drive root. This erases the drive:", "NTFS"))),
                MenuSeparator(),
                MenuItem("Connect Network Drive...", () => act(() =>
                    _model.OpenDialog(DialogKind.ConnectNetwork, "Connect Network Drive", "Drive letter and UNC path:", "Z:", @"\\server\share"))),
                MenuItem("Disconnect Network Drive...", () => act(() =>
                    _model.OpenDialog(DialogKind.DisconnectNetwork, "Disconnect Network Drive", "Drive letter to disconnect:", "Z:"))),
                MenuItem("Select Drive...", () => act(() =>
                    _model.OpenDialog(DialogKind.SelectDrive, "Select Drive",
                        string.Join("\n", _model.Scanner.GetDrives().Select(d => $"{d.RootPath}  {d.Label}")), _model.Scanner.GetDrives().FirstOrDefault()?.RootPath ?? string.Empty)))),
            Menu("Tree",
                MenuItem("Expand One Level\t+", () => act(() => WithTreeNode(n => n.ExpandOneLevel()))),
                MenuItem("Expand Branch\t*", () => act(() => WithTreeNode(n => n.ExpandBranch()))),
                MenuItem("Expand All\tCtrl+*", () => act(() => _model.TreeForCurrentDrive().ExpandBranch())),
                MenuItem("Collapse Branch\t-", () => act(() => WithTreeNode(n => n.CollapseBranch()))),
                MenuSeparator(),
                ToggleMenuItem("Indicate Expandable Branches", _model.IndicateExpandable, v => act(() => _model.IndicateExpandable = v))),
            Menu("View",
                RadioMenuItem("Tree and Directory", "pane", s.Pane == PaneView.TreeAndDirectory, () => act(() => s.Pane = PaneView.TreeAndDirectory)),
                RadioMenuItem("Tree Only", "pane", s.Pane == PaneView.TreeOnly, () => act(() => s.Pane = PaneView.TreeOnly)),
                RadioMenuItem("Directory Only", "pane", s.Pane == PaneView.DirectoryOnly, () => act(() => s.Pane = PaneView.DirectoryOnly)),
                MenuSeparator(),
                MenuItem("Split...", () => act(() =>
                    _model.OpenDialog(DialogKind.Split, "Split", "Tree pane share of the width, 0.1 to 0.9:", s.SplitPosition.ToString("0.00")))),
                MenuSeparator(),
                RadioMenuItem("Name", "details", s.ViewDetails == ViewDetailMode.NameOnly, () => act(() => s.ViewDetails = ViewDetailMode.NameOnly)),
                RadioMenuItem("All File Details", "details", s.ViewDetails == ViewDetailMode.AllDetails, () => act(() => s.ViewDetails = ViewDetailMode.AllDetails)),
                RadioMenuItem("Partial Details", "details", s.ViewDetails == ViewDetailMode.PartialDetails, () => act(() => s.ViewDetails = ViewDetailMode.PartialDetails)),
                MenuSubItem("Partial Details Choices",
                    ToggleMenuItem("Size", s.PartialDetails.ShowSize, v => act(() => s.PartialDetails = s.PartialDetails with { ShowSize = v })),
                    ToggleMenuItem("Date", s.PartialDetails.ShowDate, v => act(() => s.PartialDetails = s.PartialDetails with { ShowDate = v })),
                    ToggleMenuItem("Time", s.PartialDetails.ShowTime, v => act(() => s.PartialDetails = s.PartialDetails with { ShowTime = v })),
                    ToggleMenuItem("Attributes", s.PartialDetails.ShowAttributes, v => act(() => s.PartialDetails = s.PartialDetails with { ShowAttributes = v }))),
                MenuSubItem("Lowercase",
                    RadioMenuItem("Off", "lowercase", s.LowerCase == LowerCaseMode.Off, () => act(() => { s.LowerCase = LowerCaseMode.Off; _model.Refresh(); })),
                    RadioMenuItem("Uppercase Names", "lowercase", s.LowerCase == LowerCaseMode.UppercaseNames, () => act(() => { s.LowerCase = LowerCaseMode.UppercaseNames; _model.Refresh(); })),
                    RadioMenuItem("All Names", "lowercase", s.LowerCase == LowerCaseMode.All, () => act(() => { s.LowerCase = LowerCaseMode.All; _model.Refresh(); }))),
                MenuSeparator(),
                RadioMenuItem("Sort by Name", "sort", s.Sort == SortMode.Name, () => act(() => { s.Sort = SortMode.Name; _model.Refresh(); })),
                RadioMenuItem("Sort by Type", "sort", s.Sort == SortMode.Type, () => act(() => { s.Sort = SortMode.Type; _model.Refresh(); })),
                RadioMenuItem("Sort by Size", "sort", s.Sort == SortMode.Size, () => act(() => { s.Sort = SortMode.Size; _model.Refresh(); })),
                RadioMenuItem("Sort by Date", "sort", s.Sort == SortMode.Date, () => act(() => { s.Sort = SortMode.Date; _model.Refresh(); })),
                RadioMenuItem("Sort forward Date", "sort", s.Sort == SortMode.DateForward, () => act(() => { s.Sort = SortMode.DateForward; _model.Refresh(); })),
                MenuSeparator(),
                MenuItem("By File Type...", () => act(() =>
                    _model.OpenDialog(DialogKind.ByFileType, "By File Type", "Include only these patterns, separated by semicolons:", s.IncludeFilter)))),
            Menu("Options",
                MenuSubItem("Confirmation",
                    ToggleMenuItem("Confirm on File Delete", s.Confirmations.OnDelete, v => act(() => s.Confirmations = s.Confirmations with { OnDelete = v })),
                    ToggleMenuItem("Confirm on Directory Delete", s.Confirmations.OnSubtreeDelete, v => act(() => s.Confirmations = s.Confirmations with { OnSubtreeDelete = v })),
                    ToggleMenuItem("Confirm on File Replace", s.Confirmations.OnReplace, v => act(() => s.Confirmations = s.Confirmations with { OnReplace = v })),
                    ToggleMenuItem("Confirm on Mouse Action", s.Confirmations.OnMouseAction, v => act(() => s.Confirmations = s.Confirmations with { OnMouseAction = v })),
                    ToggleMenuItem("Confirm on Format", s.Confirmations.OnFormat, v => act(() => s.Confirmations = s.Confirmations with { OnFormat = v })),
                    ToggleMenuItem("Confirm on System, Hidden, or Read-Only", s.Confirmations.OnReadOnlySystemHidden, v => act(() => s.Confirmations = s.Confirmations with { OnReadOnlySystemHidden = v }))),
                MenuItem("Font...", () => act(() =>
                    _model.OpenDialog(DialogKind.Font, "Font", "Font name and size:", s.FontName, s.FontSize.ToString("0")))),
                MenuItem("Customize Toolbar...", () => act(() =>
                    _model.OpenDialog(DialogKind.CustomizeToolbar, "Customize Toolbar", "Button ids, comma separated. Known ids: open, copy, move, delete, rename, search, properties, refresh, back, forward:", string.Join(",", s.ToolbarButtons)))),
                MenuItem("Additional Preferences...", () => act(() =>
                    _model.OpenDialog(DialogKind.Preferences, "Additional Preferences", "Editor path and Goto punctuation:", s.EditorPath, s.GotoCachePunctuation))),
                MenuSeparator(),
                ToggleMenuItem("Toolbar", s.ShowToolbar, v => act(() => s.ShowToolbar = v)),
                ToggleMenuItem("Drivebar", s.ShowDrivebar, v => act(() => s.ShowDrivebar = v)),
                ToggleMenuItem("Status Bar", s.ShowStatusBar, v => act(() => s.ShowStatusBar = v)),
                MenuSeparator(),
                ToggleMenuItem("Minimize on Use", s.MinimizeOnUse, v => act(() => s.MinimizeOnUse = v)),
                ToggleMenuItem("Create Goto Index on Launch", s.IndexOnLaunch, v => act(() => { s.IndexOnLaunch = v; if (v) _model.EnsureGotoIndex(); })),
                ToggleMenuItem("Save Settings on Exit", s.SaveSettingsOnExit, v => act(() => s.SaveSettingsOnExit = v)),
                ToggleMenuItem("Scroll Tree on Expand", s.ScrollOnExpand, v => act(() => s.ScrollOnExpand = v)),
                ToggleMenuItem("Show Hidden and System Files", s.ShowHiddenAndSystem, v => act(() => { s.ShowHiddenAndSystem = v; _model.Refresh(); })),
                MenuSeparator(),
                MenuItem("Save Settings Now", () => act(() => _model.SaveSettings()))),
            Menu("Security",
                MenuItem("Permissions...", () => act(() => RequireSelection(sel, ShowPermissions))),
                MenuItem("Auditing...", () => act(() => RequireSelection(sel, ShowAuditing))),
                MenuItem("Owner...", () => act(() => RequireSelection(sel, ShowOwner)))),
            Menu("Window",
                MenuItem("New Window\tCtrl+Shift+Enter", () => act(() =>
                {
                    _model.Window = _model.Mdi.NewWindow(_model.CurrentPath);
                    _model.Refresh();
                })),
                MenuItem("Cascade\tShift+F5", () => act(() => _model.Mdi.ApplyCascade(new WindowRect(0, 0, 1100, 700)))),
                MenuItem("Tile Horizontally", () => act(() => _model.Mdi.ApplyTileHorizontal(new WindowRect(0, 0, 1100, 700)))),
                MenuItem("Tile Vertically\tShift+F4", () => act(() => _model.Mdi.ApplyTileVertical(new WindowRect(0, 0, 1100, 700)))),
                MenuItem("Arrange Icons", () => act(() => { /* Icon positions are computed by WindowLayout.ArrangeIcons in Core. */ })),
                MenuItem("Refresh\tF5", () => act(() => { _model.SearchMode = false; _model.TreeForCurrentDrive().Refresh(); _model.Refresh(); }))),
            Menu("Help",
                MenuItem("Contents", () => act(() => _model.OpenDialog(DialogKind.HelpContents, "Help Contents", HelpText.Contents))),
                MenuItem("Search for Help on...", () => act(() => _model.OpenDialog(DialogKind.HelpSearch, "Search for Help on", "Help in this build lives in docs/FEATURES.md in the repository, and in the Contents page here."))),
                MenuItem("How to Use Help", () => act(() => _model.OpenDialog(DialogKind.HowToUseHelp, "How to Use Help", "Pick Contents for the feature map. Every menu entry names its shortcut, and console mode prints its own help with: winfile help"))),
                MenuSeparator(),
                MenuItem("About File Manager...", () => act(() => _model.OpenDialog(DialogKind.About, "About File Manager",
                    "Reactor WinFile 1.0.0\nA .NET 11 reimplementation of Microsoft Windows File Manager.\nOriginal: https://github.com/microsoft/winfile (MIT)\nUI: Microsoft.UI.Reactor over WinUI 3. Core: WinFile.Core, Native AOT."))))
        );
    }

    private ConflictAction ConflictChoice() =>
        _model.Settings.Confirmations.OnReplace ? ConflictAction.Fail : ConflictAction.Overwrite;

    private void RequireSelection(string? selected, Action<string> action)
    {
        if (selected is null)
        {
            _model.OpenDialog(DialogKind.Message, "File Manager", "Select a file or directory first.");
            return;
        }
        action(selected);
    }

    private void OpenSelected(bool elevated)
    {
        var sel = _model.SelectedPath;
        if (sel is null) return;
        if (Directory.Exists(sel)) _model.NavigateTo(sel);
        else if (elevated) _model.Shell.OpenElevated(sel);
        else _model.Shell.Open(sel);
    }

    private void WithTreeNode(Action<DirectoryTreeNode> action)
    {
        var root = _model.TreeForCurrentDrive();
        var node = root.Find(_model.CurrentPath) ?? root;
        action(node);
        _model.Refresh();
    }

    private void ShowProperties(string path)
    {
        var info = Directory.Exists(path) ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
        var entry = FileEntry.FromFileSystemInfo(info);
        var size = entry.IsDirectory ? _model.Scanner.GetDirectorySize(path) : entry.Size;
        var body =
            $"Name: {entry.Name}\nPath: {entry.FullPath}\nType: {entry.TypeName}\n" +
            $"Size: {Formatting.FormatLong(size)} bytes\n" +
            $"Modified: {entry.LastWriteTimeUtc.ToLocalTime()}\nCreated: {entry.CreationTimeUtc.ToLocalTime()}\n" +
            $"Attributes: {AttributesService.ToLetters(entry)}  (R read-only, H hidden, A archive, S system, C compressed, L link)\n" +
            (entry.IsReparsePoint ? $"Link target: {entry.LinkTarget}\n" : string.Empty) +
            "Set attributes with comma flags in the second box, for example R,H or -R:";
        if (_model.Window.Selection.Count > 1)
            body = $"{_model.Window.Selection.Count} objects selected. The attribute flags apply to all of them.\n" + body;
        _model.OpenDialog(DialogKind.Properties, "Properties", body, path,
            $"{(entry.IsReadOnly ? "R" : "-R")},{(entry.IsHidden ? "H" : "-H")}");
    }

    private void ShowPermissions(string path)
    {
        var info = _model.Security.GetPermissions(path);
        var body = $"Owner: {info.Owner}\n" + string.Join("\n",
            info.Entries.Select(e => $"{e.Identity}  {e.Rights}  {e.Type}{(e.IsInherited ? " (inherited)" : string.Empty)}")) +
            "\n\nGrant a right with account and rights in the boxes, for example DOMAIN\\user and Read:";
        _model.OpenDialog(DialogKind.Permissions, "Permissions", body, string.Empty, "Read");
    }

    private void ShowAuditing(string path)
    {
        var (entries, error) = _model.Security.GetAuditing(path);
        var body = error is not null
            ? $"Auditing rules could not be read: {error}"
            : entries.Count == 0 ? "No auditing rules are set." :
              string.Join("\n", entries.Select(e => $"{e.Identity}  {e.Rights}  {e.Type}"));
        _model.OpenDialog(DialogKind.Auditing, "Auditing", body, path);
    }

    private void ShowOwner(string path)
    {
        var info = _model.Security.GetPermissions(path);
        _model.OpenDialog(DialogKind.Owner, "Owner", $"Current owner: {info.Owner}\nSet a new owner by typing the account name:", info.Owner);
    }

    // ─── Toolbar, drive bar, window tabs ─────────────────────────────────────

    private Element BuildToolbar(Action<Action> act)
    {
        var buttons = new List<Element>();
        foreach (var id in _model.Settings.ToolbarButtons)
        {
            buttons.Add(id switch
            {
                "open" => Button("Open", () => act(() => OpenSelected(false))),
                "copy" => Button("Copy", () => act(() => { if (_model.SelectedPath is { } p) _model.OpenDialog(DialogKind.Copy, "Copy", $"Copy {p} to:", Path.GetDirectoryName(p) ?? string.Empty); })),
                "move" => Button("Move", () => act(() => { if (_model.SelectedPath is { } p) _model.OpenDialog(DialogKind.Move, "Move", $"Move {p} to:", Path.GetDirectoryName(p) ?? string.Empty); })),
                "delete" => Button("Delete", () => act(() => RunDelete())),
                "rename" => Button("Rename", () => act(() => { if (_model.SelectedPath is { } p) _model.OpenDialog(DialogKind.Rename, "Rename", $"Rename {p} to:", Path.GetFileName(p)); })),
                "search" => Button("Search", () => act(() => _model.OpenDialog(DialogKind.Search, "Search", "Pattern:", "*.*"))),
                "properties" => Button("Properties", () => act(() => { if (_model.SelectedPath is { } p) ShowProperties(p); })),
                "refresh" => Button("Refresh", () => act(() => _model.Refresh())),
                "back" => Button("Back", () => act(() => { if (_model.Window.History.GoBack() is { } p) { _model.Window.DirectoryPath = p; _model.Refresh(); } })),
                "forward" => Button("Forward", () => act(() => { if (_model.Window.History.GoForward() is { } p) { _model.Window.DirectoryPath = p; _model.Refresh(); } })),
                _ => Button(id, () => { }),
            });
        }
        return HStack(4, buttons.ToArray()).Margin(horizontal: 8, vertical: 4);
    }

    private Element BuildDrivebar(Action<Action> act)
    {
        var drives = _model.Scanner.GetDrives();
        var buttons = drives.Select(d => (Element)Button(
            d.Label.Length > 0 ? $"{d.Letter}: {d.Label}" : $"{d.Letter}:",
            () => act(() => { if (d.IsReady) _model.NavigateTo(d.RootPath); }))).ToArray();
        return HStack(4, buttons).Margin(horizontal: 8, vertical: 2);
    }

    private Element BuildWindowTabs(Action<Action> act)
    {
        var tabs = _model.Mdi.Windows.Select(w => (Element)Button(
            (w == _model.Mdi.Active ? "* " : string.Empty) + w.Title,
            () => act(() => { _model.Mdi.Activate(w); _model.Window = w; _model.Refresh(); }))).ToArray();
        return HStack(4, tabs).Margin(horizontal: 8, vertical: 2);
    }

    // ─── Panes ───────────────────────────────────────────────────────────────

    private Element BuildPanes(Action<Action> act)
    {
        var pane = _model.Settings.Pane;
        if (pane == PaneView.DirectoryOnly) return BuildDirectoryPane(act);
        if (pane == PaneView.TreeOnly) return BuildTreePane(act);
        return HStack(8, BuildTreePane(act), BuildDirectoryPane(act));
    }

    private Element BuildTreePane(Action<Action> act)
    {
        var root = _model.TreeForCurrentDrive();
        var rows = new List<Element>
        {
            HStack(4,
                Button("-", () => act(() => { root.CollapseBranch(); _model.Refresh(); })),
                Button("+", () => act(() => { root.ExpandOneLevel(); _model.Refresh(); })),
                Button(root.Path, () => act(() => _model.NavigateTo(root.Path)))),
        };
        foreach (var (node, depth) in root.FlattenVisible().Skip(1))
        {
            var captured = node;
            var hasKids = _model.IndicateExpandable && captured.HasExpandableChildren;
            var label = $"{new string(' ', depth * 3)}{(captured.IsExpanded ? "- " : hasKids ? "+ " : "  ")}{captured.Name}";
            rows.Add(Button(label, () => act(() =>
            {
                if (captured.IsExpanded) captured.CollapseBranch(); else captured.ExpandOneLevel();
                _model.NavigateTo(captured.Path);
            })));
        }
        return ScrollView(VStack(2, rows.ToArray())).Flex(grow: 1);
    }

    private Element BuildDirectoryPane(Action<Action> act)
    {
        var rows = new List<Element>();
        var parent = Path.GetDirectoryName(_model.CurrentPath);
        if (parent is not null && !_model.SearchMode)
        {
            rows.Add(Button("..", () => act(() => _model.NavigateTo(parent))));
        }
        foreach (var entry in _model.Entries)
        {
            var captured = entry;
            var selected = _model.Window.Selection.Contains(captured.FullPath);
            var label = FormatRow(captured, selected);
            rows.Add(Button(label, () => act(() =>
            {
                if (selected)
                {
                    // Second click on a selected row opens it, like a double click.
                    if (captured.IsDirectory) _model.NavigateTo(captured.FullPath);
                    else _model.Shell.Open(captured.FullPath);
                }
                else
                {
                    _model.Window.Selection.Clear();
                    _model.Window.Selection.Add(captured.FullPath);
                    _model.Refresh();
                }
            })));
        }
        if (rows.Count == 0) rows.Add(TextBlock("(empty)"));
        return ScrollView(VStack(2, rows.ToArray())).Flex(grow: 2);
    }

    private string FormatRow(FileEntry entry, bool selected)
    {
        var marker = selected ? "> " : "  ";
        var link = entry.IsReparsePoint ? "  >SYMLINK>" : string.Empty;
        var display = Formatting.DisplayName(entry.Name, _model.Settings.LowerCase);
        var name = entry.IsDirectory ? $"[{display}]" : display;
        return _model.Settings.ViewDetails switch
        {
            ViewDetailMode.NameOnly => $"{marker}{name}{link}",
            ViewDetailMode.PartialDetails => $"{marker}{name}{PartialColumns(entry)}{link}",
            _ => $"{marker}{name}   {(entry.IsDirectory ? "<DIR>" : Formatting.FormatLong(entry.Size))}   {entry.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm}   {AttributesService.ToLetters(entry)}{link}",
        };
    }

    private string PartialColumns(FileEntry entry)
    {
        var p = _model.Settings.PartialDetails;
        var parts = new List<string>();
        if (p.ShowSize && !entry.IsDirectory) parts.Add(Formatting.FormatLong(entry.Size));
        if (p.ShowDate) parts.Add(entry.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd"));
        if (p.ShowTime) parts.Add(entry.LastWriteTimeUtc.ToLocalTime().ToString("HH:mm"));
        if (p.ShowAttributes) parts.Add(AttributesService.ToLetters(entry));
        return parts.Count == 0 ? string.Empty : "   " + string.Join("   ", parts);
    }

    // ─── Dialogs ─────────────────────────────────────────────────────────────

    private Element BuildDialog(Action<Action> act, Action bump)
    {
        var kind = _model.Dialog;
        var parts = new List<Element> { TextBlock(_model.DialogTitle) };
        if (_model.DialogBody.Length > 0) parts.Add(TextBlock(_model.DialogBody));
        if (DialogHasInputs(kind))
            parts.Add(TextBox(_model.DialogInput1, v => { _model.DialogInput1 = v; }, placeholderText: "Value"));
        if (DialogHasSecondInput(kind))
            parts.Add(TextBox(_model.DialogInput2, v => { _model.DialogInput2 = v; }, placeholderText: "Second value"));
        var buttons = new List<Element>();
        if (kind == DialogKind.SelectFiles)
            buttons.Add(Button("Deselect", () => act(() => ExecuteDeselect())));
        if (DialogHasInputs(kind) || kind == DialogKind.Properties || kind == DialogKind.Permissions || kind == DialogKind.Owner || kind == DialogKind.Confirm)
            buttons.Add(Button(kind == DialogKind.SelectFiles ? "Select" : "OK", () => act(() => ExecuteDialog(kind))));
        buttons.Add(Button("Close", () => act(() => _model.Dialog = DialogKind.None)));
        parts.Add(HStack(8, buttons.ToArray()));
        return Border(VStack(8, parts.ToArray())).Margin(horizontal: 8, vertical: 6);
    }

    private static bool DialogHasInputs(DialogKind kind) => kind is
        DialogKind.Move or DialogKind.Copy or DialogKind.Rename or DialogKind.CreateDirectory or
        DialogKind.Search or DialogKind.Goto or DialogKind.Run or DialogKind.Associate or
        DialogKind.SelectFiles or DialogKind.Label or DialogKind.Format or DialogKind.CopyDisk or
        DialogKind.ConnectNetwork or DialogKind.DisconnectNetwork or DialogKind.SelectDrive or
        DialogKind.Font or DialogKind.CustomizeToolbar or DialogKind.Preferences or
        DialogKind.ByFileType or DialogKind.Split or DialogKind.Link or
        DialogKind.Permissions or DialogKind.Owner;

    private static bool DialogHasSecondInput(DialogKind kind) => kind is
        DialogKind.Associate or DialogKind.ConnectNetwork or DialogKind.Font or DialogKind.Preferences or
        DialogKind.Format or DialogKind.Properties or DialogKind.Permissions;

    private void RunDelete()
    {
        var paths = _model.Selection.ToList();
        if (paths.Count == 0) return;
        var entries = new List<FileEntry>();
        foreach (var path in paths)
        {
            var entry = _model.Entries.FirstOrDefault(e =>
                string.Equals(e.FullPath, path, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                try
                {
                    entry = FileEntry.FromFileSystemInfo(Directory.Exists(path)
                        ? (FileSystemInfo)new DirectoryInfo(path)
                        : new FileInfo(path));
                }
                catch { /* an entry that cannot be probed gates as a plain file below */ }
            }
            entries.Add(entry ?? new FileEntry(path, Path.GetFileName(path), false, 0,
                DateTime.MinValue, DateTime.MinValue, FileAttributes.Normal, false, null));
        }
        var reason = SelectionService.DeleteConfirmationFor(entries, _model.Settings.Confirmations);
        if (reason == DeleteConfirmReason.None)
        {
            _model.Report(_model.Ops.Delete(paths), "Delete");
            return;
        }
        var body = reason switch
        {
            DeleteConfirmReason.ProtectedFile =>
                "A selected object is system, hidden, or read-only. Delete anyway?",
            DeleteConfirmReason.SubtreeDelete =>
                "A selected directory and everything in it will be deleted. Continue?",
            _ => paths.Count == 1
                ? $"Delete {Path.GetFileName(paths[0])}?"
                : $"Delete {paths.Count} objects?",
        };
        _model.OpenConfirm("Delete", body, () => _model.Report(_model.Ops.Delete(paths), "Delete"));
    }

    private void ExecuteDeselect()
    {
        var input1 = _model.DialogInput1.Trim();
        var remaining = SelectionService.DeselectByPattern(_model.Entries,
            input1.Length > 0 ? input1 : "*.*", _model.Window.Selection);
        _model.Window.Selection.Clear();
        _model.Window.Selection.AddRange(remaining);
        _model.Dialog = DialogKind.None;
        _model.Refresh();
    }

    private void ExecuteDialog(DialogKind kind)
    {
        var s = _model.Settings;
        var input1 = _model.DialogInput1.Trim();
        var input2 = _model.DialogInput2.Trim();
        var sel = _model.SelectedPath;
        switch (kind)
        {
            case DialogKind.Confirm:
            {
                var pending = _model.PendingConfirm;
                _model.PendingConfirm = null;
                pending?.Invoke();
                break;
            }
            case DialogKind.Move:
                if (sel is not null) _model.Report(_model.Ops.Move([sel], input1, ConflictChoice()), "Move");
                break;
            case DialogKind.Copy:
                if (sel is not null) _model.Report(_model.Ops.Copy([sel], input1, ConflictChoice()), "Copy");
                break;
            case DialogKind.Rename:
                if (sel is not null) _model.Ops.Rename(sel, input1);
                break;
            case DialogKind.CreateDirectory:
                _model.Ops.CreateDirectory(_model.CurrentPath, input1);
                break;
            case DialogKind.Search:
            {
                var results = _model.Search.Search(new SearchCriteria(_model.CurrentPath, input1.Length > 0 ? input1 : "*.*"));
                _model.Entries = results;
                _model.SearchMode = true;
                _model.StatusText = $"{results.Count} files found under {_model.CurrentPath}";
                break;
            }
            case DialogKind.Goto:
            {
                if (Directory.Exists(input1)) { _model.NavigateTo(Path.GetFullPath(input1)); break; }
                _model.EnsureGotoIndex();
                var hits = _model.GotoIndex!.Query(input1);
                if (hits.Count > 0) _model.NavigateTo(hits[0]);
                else _model.StatusText = $"No directory matches '{input1}'";
                break;
            }
            case DialogKind.Run:
                _model.Shell.Run(input1, _model.CurrentPath);
                break;
            case DialogKind.Associate:
                _model.Associations.Associate(input1, input2);
                _model.StatusText = $"Associated {input1} with {input2}";
                break;
            case DialogKind.Link:
                if (sel is not null)
                {
                    if (_model.LinkMode == 2) LinkService.CreateJunction(input1, sel);
                    else if (_model.LinkMode == 1) LinkService.CreateHardLink(input1, sel);
                    else LinkService.CreateSymbolicLink(input1, sel, Directory.Exists(sel));
                }
                break;
            case DialogKind.Properties:
            {
                bool? r = null, h = null;
                foreach (var token in input2.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.Equals("R", StringComparison.OrdinalIgnoreCase)) r = true;
                    else if (token.Equals("-R", StringComparison.OrdinalIgnoreCase)) r = false;
                    else if (token.Equals("H", StringComparison.OrdinalIgnoreCase)) h = true;
                    else if (token.Equals("-H", StringComparison.OrdinalIgnoreCase)) h = false;
                }
                // With several objects selected this is the original's group
                // attributes path: the flags apply to every selected object.
                foreach (var path in _model.Window.Selection.ToList())
                    AttributesService.Set(path, readOnly: r, hidden: h);
                break;
            }
            case DialogKind.SelectFiles:
            {
                var picked = SelectionService.SelectByPattern(_model.Entries, input1.Length > 0 ? input1 : "*.*");
                _model.Window.Selection.Clear();
                _model.Window.Selection.AddRange(picked.Select(p => p.FullPath));
                break;
            }
            case DialogKind.Label:
                _model.Disk.SetLabel(Path.GetPathRoot(_model.CurrentPath) ?? _model.CurrentPath, input1);
                break;
            case DialogKind.Format:
            {
                // Format is destructive and long running. The dialog is the confirmation: its body
                // warns that the drive is erased. On OK the format runs through
                // DiskService.FormatAsync, and the outcome lands in the status bar when it exits.
                var root = Path.GetPathRoot(_model.CurrentPath) ?? _model.CurrentPath;
                var fileSystem = input1.Length > 0 ? input1 : "NTFS";
                var volumeLabel = input2.Length > 0 ? input2 : null;
                _model.StatusText = $"Formatting {root} as {fileSystem}...";
                _ = RunFormatAsync(root, fileSystem, volumeLabel);
                break;
            }
            case DialogKind.CopyDisk:
                _model.Report(_model.Disk.CopyDisk(Path.GetPathRoot(_model.CurrentPath) ?? _model.CurrentPath, input1), "Copy Disk");
                break;
            case DialogKind.ConnectNetwork:
                _model.Network.Connect(input1, input2);
                break;
            case DialogKind.DisconnectNetwork:
                _model.Network.Disconnect(input1);
                break;
            case DialogKind.SelectDrive:
                if (Directory.Exists(input1)) _model.NavigateTo(input1);
                break;
            case DialogKind.Font:
                s.FontName = input1.Length > 0 ? input1 : s.FontName;
                if (double.TryParse(input2, out var fontSize)) s.FontSize = fontSize;
                break;
            case DialogKind.CustomizeToolbar:
                s.ToolbarButtons = input1.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x.ToLowerInvariant()).ToList();
                break;
            case DialogKind.Preferences:
                s.EditorPath = input1;
                s.GotoCachePunctuation = input2.Length > 0 ? input2 : s.GotoCachePunctuation;
                _model.GotoIndex = null;
                break;
            case DialogKind.ByFileType:
                s.IncludeFilter = input1.Length > 0 ? input1 : "*.*";
                break;
            case DialogKind.Split:
                if (double.TryParse(input1, out var split)) s.SplitPosition = Math.Clamp(split, 0.1, 0.9);
                break;
            case DialogKind.Permissions:
                if (sel is not null && input1.Length > 0)
                {
                    var rights = Enum.TryParse<System.Security.AccessControl.FileSystemRights>(input2, ignoreCase: true, out var parsed)
                        ? parsed : System.Security.AccessControl.FileSystemRights.Read;
                    _model.Security.Grant(sel, input1, rights);
                }
                break;
            case DialogKind.Owner:
                if (sel is not null && input1.Length > 0) _model.Security.SetOwner(sel, input1);
                break;
        }
        _model.Dialog = DialogKind.None;
        if (kind is not (DialogKind.Search or DialogKind.Goto)) _model.Refresh();
        else if (kind == DialogKind.Goto) _model.Refresh();
    }

    private async Task RunFormatAsync(string root, string fileSystem, string? volumeLabel)
    {
        int exit;
        try
        {
            exit = await _model.Disk.FormatAsync(root, fileSystem, volumeLabel, quick: true);
        }
        catch (Exception ex)
        {
            _model.Refresh();
            _model.StatusText = $"Format of {root} failed: {ex.Message}";
            _bump?.Invoke();
            return;
        }
        _model.Refresh();
        _model.StatusText = exit == 0
            ? $"Format of {root} as {fileSystem} complete."
            : $"Format of {root} failed: format.com exited with code {exit}.";
        _bump?.Invoke();
    }
}

/// <summary>Help text, kept short. The full feature map lives in docs/FEATURES.md.</summary>
public static class HelpText
{
    public const string Contents = """
        File Manager help

        File: open, edit, move, copy, link, clipboard, delete, rename, properties,
        compress, run, print, associate, create directory, search, select files,
        start a shell, goto a directory, exit.

        Disk: copy disk, label, format, network drives, select drive.
        Tree: expand and collapse branches.
        View: panes, split, detail levels, sorting, file type filter.
        Options: confirmations, font, toolbar, preferences, settings save.
        Security: permissions, auditing, owner.
        Window: child windows, cascade, tile, arrange icons, refresh.

        Shortcuts match the original where the platform allows them.
        Console mode: winfile help lists every verb.
        """;
}
