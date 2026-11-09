# Reactor WinFile

Windows File Manager, rebuilt on .NET 11.

This is a clean-room reimplementation of Microsoft's [Windows File Manager](https://github.com/microsoft/winfile). The original is C. This one is C# end to end. The feature set matches the original menu for menu. The runtime is modern: .NET 11, Native AOT, one binary per architecture.

## What you get

- One `winfile.exe` per architecture: `win-x64` and `win-arm64`.
- A GUI built on [Microsoft.UI.Reactor](https://github.com/microsoft/microsoft-ui-reactor) over WinUI 3. No XAML files. State lives in one model, views are functions of that state.
- A console mode in the same binary. Run `winfile.exe copy a.txt D:\dest` and it copies and exits. Run it with no arguments and the GUI opens.
- Settings in the same place and format as the original: `%AppData%\Roaming\Microsoft\WinFile\Winfile.ini`.
- Tests at four levels: unit, functional, integration, and end-to-end. See [docs/TESTING.md](docs/TESTING.md).

## Features

Every command from the original menus is implemented. Highlights:

- File: open, edit (F12), move (F7), copy (F8), symlink (F11), hard link and junction (Shift+F11), clipboard copy/cut/paste, delete, rename (F2), properties, NTFS compress and uncompress, run, print, associate, create directory, search, select files.
- Disk: copy disk, label, format, connect and disconnect network drives, select drive.
- Tree: expand one level, expand branch, expand all, collapse branch, expandable-branch markers.
- View: tree and directory panes, split, name only, all details, partial details, sort by name, type, size, and date in both directions, filter by file type.
- Options: the five confirmation toggles, font, toolbar customization, preferences, settings saved on exit or on demand.
- Security: permissions, auditing, owner.
- Window: child windows, cascade, tile, arrange icons, refresh.
- Search sorts by date and can filter on a modified-after date. Goto (Ctrl+G) finds directories from a few words of the path. Same-directory copies get the `- Copy` suffix. Sort order is natural: `file2` comes before `file10`.

The full parity map is in [docs/FEATURES.md](docs/FEATURES.md).

## Run it

Grab the artifact for your architecture from the latest [build](../../actions/workflows/build.yml), or build it yourself:

```
dotnet publish src/ReactorWinFile/ReactorWinFile.csproj -c Release -r win-x64 -p:Platform=x64 -p:PublishAot=true --self-contained true -o out/win-x64
out\win-x64\winfile.exe
```

Console mode:

```
winfile.exe ls C:\work
winfile.exe search C:\work "*.md"
winfile.exe goto proj sour
winfile.exe help
```

This is a Windows-only project: Reactor is WinUI, so the app runs on Windows 10 and 11, on x64 and Arm64.

## Build and test

You need the .NET 11 SDK. The repo pins it in `global.json` with roll-forward on.

```
dotnet restore ReactorWinFile.slnx
dotnet build src/ReactorWinFile/ReactorWinFile.csproj -c Release -p:Platform=x64
dotnet test tests/WinFile.UnitTests -c Release
dotnet test tests/WinFile.FunctionalTests -c Release
dotnet test tests/WinFile.IntegrationTests -c Release
dotnet test tests/WinFile.E2ETests -c Release
```

Details: [docs/BUILDING.md](docs/BUILDING.md). Architecture: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Relationship to the original

Microsoft released WinFile under the MIT License in 2018 and archived the repository in March 2025. This project is an independent reimplementation written from the original's documented and observable behavior. It shares no code with the original. The original stays the reference for what File Manager should do. Credit for the design belongs to its authors.

## License

MIT. See [LICENSE](LICENSE).
