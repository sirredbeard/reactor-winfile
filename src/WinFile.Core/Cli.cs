namespace WinFile.Core;

/// <summary>
/// Console mode for the single binary. Running winfile.exe with a verb performs the operation
/// and exits, which is also how the end-to-end tests drive the shipped program logic.
/// The GUI starts when no verb is given.
/// </summary>
public static class WinFileCli
{
    private static readonly string[] Verbs =
    [
        "ls", "dir", "tree", "copy", "cp", "move", "mv", "delete", "del", "rm", "rename", "ren",
        "mkdir", "mkfile", "search", "find", "goto", "drives", "label", "attrib", "link",
        "hardlink", "junction", "open", "run", "print", "associate", "version", "help",
    ];

    public static bool IsCliCommand(string? arg) =>
        arg is not null && Verbs.Contains(arg.ToLowerInvariant());

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0) return PrintHelp(output);
        var verb = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        try
        {
            return verb switch
            {
                "version" => Version(output),
                "help" => PrintHelp(output),
                "ls" or "dir" => List(rest, output),
                "tree" => Tree(rest, output),
                "drives" => Drives(output),
                "copy" or "cp" => CopyMove(rest, move: false, error),
                "move" or "mv" => CopyMove(rest, move: true, error),
                "delete" or "del" or "rm" => Delete(rest, error),
                "rename" or "ren" => Rename(rest, error),
                "mkdir" => Mkdir(rest),
                "mkfile" => Mkfile(rest),
                "search" or "find" => Search(rest, output),
                "goto" => Goto(rest, output),
                "label" => Label(rest, output),
                "attrib" => Attrib(rest, output, error),
                "link" => Link(rest, isHard: false, isJunction: false),
                "hardlink" => Link(rest, isHard: true, isJunction: false),
                "junction" => Link(rest, isHard: false, isJunction: true),
                "open" => OpenPath(rest),
                "run" => RunCommand(rest),
                "print" => Print(rest),
                "associate" => Associate(rest, output),
                _ => Unknown(verb, error),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or PlatformNotSupportedException or ArgumentException)
        {
            error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Version(TextWriter output)
    {
        output.WriteLine("Reactor WinFile 1.0.0");
        output.WriteLine("A .NET 11 reimplementation of Microsoft Windows File Manager (microsoft/winfile).");
        return 0;
    }

    private static int PrintHelp(TextWriter output)
    {
        output.WriteLine("""
            Reactor WinFile, console mode. Start with no arguments for the GUI.

            Usage: winfile <verb> [arguments]

              ls [path]                     List a directory
              tree [path]                   Show the directory tree
              drives                        List drives
              copy <source> <destdir>       Copy a file or directory
              move <source> <destdir>       Move a file or directory
              delete <path>                 Delete a file or directory
              rename <path> <newname>       Rename in place
              mkdir <path>                  Create a directory
              mkfile <path>                 Create an empty file
              search <root> <pattern>       Search recursively, newest first
              goto <words...>               Find a directory by words from its path
              label [drive] [newlabel]      Read or set a volume label
              attrib <path>                 Show attributes, for example attrib <path> +R -H
              link <link> <target>          Create a symbolic link
              hardlink <link> <target>      Create a hard link (Windows)
              junction <link> <target>      Create a junction (Windows)
              open <path>                   Open with the associated program
              run <command...>              Run a command in the current directory
              print <path>                  Print through the associated program
              associate <ext> <program>     Associate a file type with a program
              version                       Show the version
            """);
        return 0;
    }

    private static int Unknown(string verb, TextWriter error)
    {
        error.WriteLine($"error: unknown command '{verb}'. Try 'winfile help'.");
        return 2;
    }

    private static int List(string[] args, TextWriter output)
    {
        var path = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        var settings = new WinFileSettings { ShowHiddenAndSystem = true };
        var scanner = new DirectoryScanner();
        foreach (var entry in scanner.ListDirectory(path, settings))
        {
            var kind = entry.IsDirectory ? "<DIR>" : entry.Size.ToString();
            var link = entry.IsReparsePoint ? " -> " + (entry.LinkTarget ?? string.Empty) : string.Empty;
            output.WriteLine($"{kind,12}  {entry.Name}{link}");
        }
        return 0;
    }

    private static int Tree(string[] args, TextWriter output)
    {
        var path = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        var root = new DirectoryTreeNode(Path.GetFullPath(path));
        root.ExpandBranch();
        foreach (var (node, depth) in root.FlattenVisible())
            output.WriteLine(new string(' ', depth * 2) + node.Name);
        return 0;
    }

    private static int Drives(TextWriter output)
    {
        foreach (var drive in new DirectoryScanner().GetDrives())
        {
            output.WriteLine(drive.IsReady
                ? $"{drive.RootPath}  {drive.Type}  {drive.Label}  {Formatting.FormatLong(drive.FreeSpace)} bytes free of {Formatting.FormatLong(drive.TotalSize)}"
                : $"{drive.RootPath}  {drive.Type}  (not ready)");
        }
        return 0;
    }

    private static int CopyMove(string[] args, bool move, TextWriter error)
    {
        if (args.Length < 2) { error.WriteLine("error: expected <source> <destdir>"); return 2; }
        var ops = new FileOperations();
        var result = move ? ops.Move([args[0]], args[1]) : ops.Copy([args[0]], args[1]);
        foreach (var failure in result.Failures) error.WriteLine($"error: {failure.SourcePath}: {failure.Message}");
        return result.HasFailures ? 1 : 0;
    }

    private static int Delete(string[] args, TextWriter error)
    {
        if (args.Length < 1) { error.WriteLine("error: expected <path>"); return 2; }
        var result = new FileOperations().Delete([args[0]]);
        foreach (var failure in result.Failures) error.WriteLine($"error: {failure.SourcePath}: {failure.Message}");
        return result.HasFailures ? 1 : 0;
    }

    private static int Rename(string[] args, TextWriter error)
    {
        if (args.Length < 2) { error.WriteLine("error: expected <path> <newname>"); return 2; }
        new FileOperations().Rename(args[0], args[1]);
        return 0;
    }

    private static int Mkdir(string[] args)
    {
        Directory.CreateDirectory(args[0]);
        return 0;
    }

    private static int Mkfile(string[] args)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
        new FileOperations().CreateFile(dir, Path.GetFileName(args[0]));
        return 0;
    }

    private static int Search(string[] args, TextWriter output)
    {
        if (args.Length < 2) { output.WriteLine("usage: winfile search <root> <pattern>"); return 2; }
        var results = new SearchService().Search(new SearchCriteria(args[0], args[1]));
        foreach (var entry in results) output.WriteLine(entry.FullPath);
        return 0;
    }

    private static int Goto(string[] args, TextWriter output)
    {
        if (args.Length < 1) { output.WriteLine("usage: winfile goto <words...>"); return 2; }
        var index = GotoIndex.BuildFromRoots([Directory.GetCurrentDirectory()]);
        foreach (var match in index.Query(string.Join(' ', args))) output.WriteLine(match);
        return 0;
    }

    private static int Label(string[] args, TextWriter output)
    {
        var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        var disk = new DiskService();
        if (args.Length > 1)
        {
            disk.SetLabel(root, args[1]);
            output.WriteLine($"Label set to {args[1]}");
        }
        else
        {
            output.WriteLine(disk.GetLabel(root));
        }
        return 0;
    }

    private static int Attrib(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length < 1) { error.WriteLine("error: expected <path> [changes]"); return 2; }
        var path = args[0];
        if (args.Length == 1)
        {
            var entry = FileEntry.FromFileSystemInfo(
                Directory.Exists(path) ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path));
            output.WriteLine($"{AttributesService.ToLetters(entry)}  {path}");
            return 0;
        }
        bool? r = null, h = null, a = null, s = null;
        foreach (var change in args.Skip(1))
        {
            if (change.Length != 2) continue;
            var on = change[0] == '+';
            switch (char.ToUpperInvariant(change[1]))
            {
                case 'R': r = on; break;
                case 'H': h = on; break;
                case 'A': a = on; break;
                case 'S': s = on; break;
            }
        }
        AttributesService.Set(path, r, h, a, s);
        return 0;
    }

    private static int Link(string[] args, bool isHard, bool isJunction)
    {
        if (args.Length < 2) return 2;
        if (isJunction) LinkService.CreateJunction(args[0], args[1]);
        else if (isHard) LinkService.CreateHardLink(args[0], args[1]);
        else LinkService.CreateSymbolicLink(args[0], args[1], Directory.Exists(args[1]));
        return 0;
    }

    private static int OpenPath(string[] args)
    {
        new ShellService().Open(args[0]);
        return 0;
    }

    private static int RunCommand(string[] args)
    {
        new ShellService().Run(string.Join(' ', args), Directory.GetCurrentDirectory());
        return 0;
    }

    private static int Print(string[] args)
    {
        new ShellService().Print(args[0]);
        return 0;
    }

    private static int Associate(string[] args, TextWriter output)
    {
        if (args.Length < 2) { output.WriteLine("usage: winfile associate <ext> <program>"); return 2; }
        new AssociationService().Associate(args[0], args[1]);
        output.WriteLine($"Associated {args[0]} with {args[1]}");
        return 0;
    }
}
