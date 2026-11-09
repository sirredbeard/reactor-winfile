# Feature parity

Every row names a feature of the original WinFile (the enhanced master branch of microsoft/winfile, version 10.4) and where it lives here. `Core` means WinFile.Core. `App` means the Reactor frame. `CLI` means console mode in the same binary.

## File menu

| Original | Here |
| --- | --- |
| Open (Enter) | App menu and toolbar. Core: `ShellService.Open`. CLI: `open` |
| Edit (F12) | App menu. Core: `ShellService.Edit`, editor set by `EditorPath` in Winfile.ini |
| Move (F7) | App dialog. Core: `FileOperations.Move`. CLI: `move` |
| Copy (F8) | App dialog. Core: `FileOperations.Copy` with progress and conflict handling. CLI: `copy` |
| Symlink (F11) | App dialog. Core: `LinkService.CreateSymbolicLink`. CLI: `link` |
| Hardlink / junction (Shift+F11) | App dialog picks junction for directories. Core: `LinkService.CreateHardLink`, `CreateJunction`. CLI: `hardlink`, `junction` |
| Copy to Clipboard (Ctrl+C) | App menu. Core: `ClipboardModel.Copy` |
| Cut to Clipboard (Ctrl+X) | App menu. Core: `ClipboardModel.Cut`. Paste after cut moves and clears |
| Paste (Ctrl+V) | App menu. Core: `ClipboardModel.Paste`. Same-directory copies get `- Copy` |
| Delete (Del) | App menu. Core: `FileOperations.Delete`. Recycle Bin path: `RecycleBinService` |
| Rename (F2) | App dialog. Core: `FileOperations.Rename`. CLI: `rename` |
| Properties (Alt+Enter) | App dialog with attribute editing; with several objects selected the flags apply to all of them, the original's group attributes path. Core: `AttributesService` |
| Compress / Uncompress | App menu. Core: `CompressionService` through compact.exe |
| Run | App dialog. Core: `ShellService.Run`. CLI: `run` |
| Print | App menu. Core: `ShellService.Print` with the print verb. CLI: `print` |
| Associate | App dialog. Core: `AssociationService` over HKEY_CLASSES_ROOT. CLI: `associate` |
| Create Directory (Ctrl+Shift+N) | App dialog. Core: `FileOperations.CreateDirectory`. CLI: `mkdir` |
| Search (Ctrl+F) | App dialog. Core: `SearchService`, recursive, optional modified-after date, results sorted by date. CLI: `search` |
| Select Files | App dialog with Select and Deselect, as the original's dialog has both. Core: `SelectionService` |
| Start: Bash (Ctrl+B), Cmd (Ctrl+K), PowerShell (Ctrl+P), Explorer (Ctrl+E) | App submenu. Core: `ShellService.StartShell`. Elevated shell uses the runas verb |
| Goto Directory (Ctrl+G) | App dialog. Core: `GotoIndex` with configurable word punctuation. CLI: `goto` |
| Exit | App menu. Saves settings first when Save Settings on Exit is on |

## Disk menu

| Original | Here |
| --- | --- |
| Copy Disk | App dialog. Core: `DiskService.CopyDisk`, a full recursive copy for modern media |
| Label Disk | App dialog. Core: `DiskService.SetLabel`. CLI: `label` |
| Format Drive | App dialog collects the file system and an optional volume label, then runs the format. Core: `DiskService.FormatAsync` through format.com |
| Connect / Disconnect Network Drive | App dialogs. Core: `NetworkDriveService` over WNetAddConnection2 and WNetCancelConnection2 |
| Select Drive | App dialog and drive bar. Core: `DirectoryScanner.GetDrives`. CLI: `drives` |

UNC paths work directly in navigation, as they did in the enhanced original.

## Tree menu

Expand One Level, Expand Branch, Expand All, and Collapse Branch map to `DirectoryTreeNode` and `DirectoryTree`. Indicate Expandable Branches toggles the plus markers, which come from `DirectoryTreeNode.HasExpandableChildren`. Tree state refreshes on `DirectoryWatcher` change notifications.

## View menu

- Panes: Tree and Directory, Tree Only, Directory Only. Core state: `PaneView`.
- Split: `WinFileSettings.SplitPosition`, 0.1 to 0.9.
- Details: Name, All File Details, Partial Details with size, date, time, and attribute choices.
- Sort: Name, Type, Size, Date, and forward Date. Directories first. Names use natural sort.
- By File Type: semicolon patterns such as `*.txt;*.md`, stored as `IncludeFilter`.
- Lowercase: Off, Uppercase Names, or All Names. This is the original's LowerCase display attribute (winfile.h TA_LOWERCASE and TA_LOWERCASEALL), stored as the `LowerCase` value in Winfile.ini: names stored all-uppercase display lowercase in the first mode, every name in the second. Display only. The original exposes it only through the ini value; the menu here is an addition.

## Options menu

- Confirmation: the original's six toggles: file delete, directory delete, file replace, mouse action, format, and system, hidden, or read-only files. Stored as the original's six Confirm* keys in Winfile.ini. The toggles gate the operations: delete asks first under File Delete, a directory in the selection asks under Directory Delete, and a system, hidden, or read-only object asks under its own toggle, in that priority order (`SelectionService.DeleteConfirmationFor`). File Replace already drives the copy and move conflict choice. The Format dialog is itself the format confirmation. Mouse Action has no surface here: this build moves files with the clipboard, not OLE drag and drop.
- Font: name and size in the dialog; name, size, and weight persisted as `Face`, `Size`, and `FaceWeight`. Size is whole points, as the original stores it.
- Customize Toolbar: comma separated button ids, persisted.
- Additional Preferences: editor path, Goto punctuation, scroll on expand, hidden and system file visibility.
- Toolbar, Drivebar, Status Bar: visibility toggles.
- Minimize on Use, Create Goto Index on Launch, Save Settings on Exit: behavior toggles.
- Save Settings Now: writes Winfile.ini immediately through `SettingsStore`.

## Security menu

Permissions lists access rules and grants new ones. Auditing lists SACL rules when the caller holds the privilege and reports the error when it does not. Owner shows the current owner and sets a new one. Core: `SecurityService` over the .NET access control APIs.

## Window menu

New Window, Cascade, Tile Horizontally, Tile Vertically, Arrange Icons, and Refresh. Child windows are `MdiManager` state and render as tabs in the frame. Layout geometry is pure code in `WindowLayout` and unit tested. Each child window keeps its own navigation history, so back and forward work per window. That covers the forward and back navigation the enhanced original added.

## Help menu

Contents, Search for Help on, How to Use Help, and About File Manager. Contents also lives in the App as `HelpText`. Console mode prints its own help.

## Winfile.ini compatibility

Settings live in the original's layout, so one Winfile.ini serves both programs: a single `[Settings]` section with the original's key names from winfile.h. `ToolBar`, `DriveBar`, `StatusBar`, `MinOnRun`, `IndexOnLaunch`, `IndexHiddenSystem`, `Save Settings`, `ScrollOnExpand`, `EditorPath`, `CachedPath`, `GotoCachePunctuation`, the six `Confirm*` keys, `ChangeNotifyTime`, `Face`, `Size`, `FaceWeight`, `LowerCase`, `NewWinOnNetConnect`, and `Window` all read and write under those names, booleans as integers. `Window` round-trips in the original's exact shape: left, top, width, height, two empty fields, and the show command. Loading reads the original's keys first and falls back to this app's first-release layout, so an ini from the first release keeps working. Saving edits the file on disk at the line level: keys update in place, sections and keys this app does not own, `[AddOns]` above all, survive untouched, and comments survive with them. The original likewise ignores the extra keys this app keeps for its own state (sort, view, pane, filter, split, toolbar buttons).

## Behavior details worth naming

- Long paths: the manifest declares longPathAware and `Formatting.ToLongPath` applies the `\\?\` form past 260 characters. The original handled paths up to 1024 characters the same way.
- Ctrl+C in a pane copies the selection to the clipboard model. It does not change drives. That was an explicit fix in the enhanced original.
- Ctrl+Enter opens as administrator through the runas verb.
- OLE drag and drop in the original maps to clipboard copy, cut, and paste here, plus the link modifiers: Ctrl+Shift for a symlink, Ctrl+Shift+Alt for a hard link or junction.
- The status bar shows the current directory, the object count, selected bytes, and free space, formatted by `Formatting.FormatStatusLine`.
- The `- Copy` suffix rule, search sorted by date, resizable dialog behavior, and relative path handling on the command line all match the 10.x change history.
- Directory listing and search read the file system's find data directly (`FileEntry.FromEntry` over `FileSystemEnumerable`), so a large directory costs no per-file stat call. The status bar probes only the drive the current path lives on, not every drive on the machine.
- Directory watching honors `ChangeNotifyTime` (default 3000 ms): a burst of changes refreshes the pane once, after the burst settles, instead of once per file.
- The goto index dedupes through a hash set. Building it over a full drive is linear in the directory count, not quadratic.
