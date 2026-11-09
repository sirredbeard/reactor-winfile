# Architecture

Two projects ship. One library does the work. One executable presents it.

## WinFile.Core

Target: `net11.0`. No UI. AOT compatible. Every feature of File Manager lives here as plain C# over the file system, the registry, and a small set of Windows APIs.

- `DirectoryScanner` reads directories, drives, and subtrees. `DirectoryWatcher` signals refreshes.
- `FileOperations` copies, moves, deletes, renames, and creates. Copy reports progress and handles conflicts three ways: fail, overwrite, skip.
- `SearchService` searches recursively with a pattern and an optional modified-after date.
- `GotoIndex` tokenizes paths into words and answers word queries.
- `LinkService` creates symbolic links through .NET, hard links through CreateHardLinkW, and junctions by writing the reparse point directly.
- `AttributesService` and `CompressionService` cover properties and NTFS compression.
- `AssociationService` reads and writes HKEY_CLASSES_ROOT through an `IAssociationStore`. Tests substitute an in-memory store.
- `SecurityService` reads and writes ACLs, audit rules, and owners.
- `DiskService` and `NetworkDriveService` cover labels, format, disk copy, and drive mapping.
- `ShellService` opens, edits, runs, and prints through the shell, with an elevated path through runas.
- `ClipboardModel` holds copy and cut semantics. `NavigationHistory` holds per-window back and forward stacks. `MdiManager` and `WindowLayout` hold child window state and tiling geometry.
- `SettingsStore` reads and writes Winfile.ini. `IniFile` is a small order-preserving parser.
- `WinFileCli` is console mode. The GUI binary dispatches to it when the first argument is a verb.

Windows-only operations check the OS first and throw `PlatformNotSupportedException` off Windows. The rest of Core runs anywhere .NET 11 runs, which is what lets the test suites run on any CI agent.

## ReactorWinFile

Target: `net11.0-windows10.0.22621.0`. Output: `winfile.exe`. One project, one binary per architecture.

`Program.cs` picks the mode. A verb goes to console mode. `--self-test` runs a scripted Core pass and writes a result file for CI. Anything else starts the GUI. A directory argument opens the GUI at that directory.

`WinFileApp` is a Microsoft.UI.Reactor component. It renders a title bar, the full menu bar, a toolbar, a drive bar, the tree pane, the directory pane, and a status bar. Dialogs render as an inline panel. State sits in `FrameModel`, which owns the Core services, the current child window, the selection, and the settings. Actions mutate the model, then bump a version counter to re-render. That is the whole pattern. No XAML, no view models, no code-behind.

Child windows from the original MDI frame render as tabs. The model is `MdiManager`, so cascade and tile still compute real rectangles even though the presentation is tabbed.

## Publishing

Releases are Native AOT, self-contained, one folder per runtime identifier: `win-x64` and `win-arm64`. CI builds each architecture on a matching Windows runner, runs the shipped binary through `--self-test`, and uploads the folder as an artifact. The one binary in each folder is `winfile.exe`. The files next to it are the Windows App SDK runtime that WinUI needs. They are not separate programs.

Reactor is experimental software from Microsoft. It has no support policy and its API can change between previews. This repo pins `Microsoft.UI.Reactor` 0.1.0-preview.17 and `Microsoft.WindowsAppSDK` 2.2.0 in `Directory.Packages.props`, so a preview change cannot break the build by drifting.
