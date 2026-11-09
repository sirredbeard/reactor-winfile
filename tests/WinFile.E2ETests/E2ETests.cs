using Xunit;
using WinFile.Core;

namespace WinFile.E2ETests;

/// <summary>
/// End-to-end runs through the console mode of the single binary.
/// Each test drives WinFileCli the way a user at a prompt (or the GUI's own
/// command dispatch) would, and checks the file system and the output text.
/// </summary>
public class CliEndToEndTests
{
    private static (int Exit, string Out, string Err) RunCli(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = WinFileCli.Run(args, stdout, stderr);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void HelpAndVersionSucceed()
    {
        var help = RunCli("help");
        Assert.Equal(0, help.Exit);
        Assert.Contains("console mode", help.Out);
        var version = RunCli("version");
        Assert.Equal(0, version.Exit);
        Assert.Contains("Reactor WinFile", version.Out);
    }

    [Fact]
    public void UnknownVerbFailsWithUsageHint()
    {
        var result = RunCli("frobnicate");
        Assert.Equal(2, result.Exit);
        Assert.Contains("unknown command", result.Err);
    }

    [Fact]
    public void FullFileManagementScenario()
    {
        using var t = new TestDir();

        Assert.Equal(0, RunCli("mkdir", Path.Combine(t.Root, "work")).Exit);
        Assert.Equal(0, RunCli("mkfile", Path.Combine(t.Root, "work", "draft.txt")).Exit);
        File.WriteAllText(Path.Combine(t.Root, "work", "draft.txt"), "scenario");

        var list = RunCli("ls", Path.Combine(t.Root, "work"));
        Assert.Equal(0, list.Exit);
        Assert.Contains("draft.txt", list.Out);

        Assert.Equal(0, RunCli("mkdir", Path.Combine(t.Root, "archive")).Exit);
        Assert.Equal(0, RunCli("copy", Path.Combine(t.Root, "work", "draft.txt"), Path.Combine(t.Root, "archive")).Exit);
        Assert.True(File.Exists(Path.Combine(t.Root, "archive", "draft.txt")));
        Assert.True(File.Exists(Path.Combine(t.Root, "work", "draft.txt")));

        Assert.Equal(0, RunCli("rename", Path.Combine(t.Root, "work", "draft.txt"), "final.txt").Exit);
        Assert.True(File.Exists(Path.Combine(t.Root, "work", "final.txt")));

        var search = RunCli("search", t.Root, "final.txt");
        Assert.Equal(0, search.Exit);
        Assert.Contains("final.txt", search.Out);

        Assert.Equal(0, RunCli("move", Path.Combine(t.Root, "work", "final.txt"), Path.Combine(t.Root, "archive")).Exit);
        Assert.False(File.Exists(Path.Combine(t.Root, "work", "final.txt")));
        Assert.True(File.Exists(Path.Combine(t.Root, "archive", "final.txt")));

        Assert.Equal(0, RunCli("delete", Path.Combine(t.Root, "archive", "draft.txt")).Exit);
        Assert.False(File.Exists(Path.Combine(t.Root, "archive", "draft.txt")));
    }

    [Fact]
    public void TreeCommandPrintsNestedDirectories()
    {
        using var t = new TestDir();
        t.Dir("outer/inner");
        var result = RunCli("tree", t.Root);
        Assert.Equal(0, result.Exit);
        Assert.Contains("outer", result.Out);
        Assert.Contains("inner", result.Out);
    }

    [Fact]
    public void GotoCommandFindsDirectoryByWords()
    {
        using var t = new TestDir();
        t.Dir("my-projects/source");
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(t.Root);
            var result = RunCli("goto", "proj", "sour");
            Assert.Equal(0, result.Exit);
            Assert.Contains(Path.Combine("my-projects", "source"), result.Out);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    [Fact]
    public void AttribCommandShowsAndSetsFlags()
    {
        using var t = new TestDir();
        var file = t.File("flags.txt");
        var before = RunCli("attrib", file);
        Assert.Equal(0, before.Exit);
        Assert.Contains("flags.txt", before.Out);
        Assert.Equal(0, RunCli("attrib", file, "+R").Exit);
        Assert.True((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0);
        Assert.Equal(0, RunCli("attrib", file, "-R").Exit);
        Assert.True((File.GetAttributes(file) & FileAttributes.ReadOnly) == 0);
    }

    [Fact]
    public void SymlinkScenarioThroughTheCli()
    {
        using var t = new TestDir();
        var target = t.File("real.txt", "target contents");
        var link = Path.Combine(t.Root, "alias.txt");
        Assert.Equal(0, RunCli("link", link, target).Exit);
        Assert.Equal("target contents", File.ReadAllText(link));
    }

    [Fact]
    public void CopyOfMissingFileReportsErrorAndExitCode()
    {
        using var t = new TestDir();
        var result = RunCli("copy", Path.Combine(t.Root, "ghost.txt"), t.Root);
        Assert.NotEqual(0, result.Exit);
        Assert.Contains("error", result.Err);
    }

    [Fact]
    public void DrivesCommandListsDrives()
    {
        var result = RunCli("drives");
        Assert.Equal(0, result.Exit);
        Assert.NotEmpty(result.Out.Trim());
    }

    [Fact]
    public void IsCliCommandSeparatesVerbsFromGuiLaunch()
    {
        Assert.True(WinFileCli.IsCliCommand("copy"));
        Assert.True(WinFileCli.IsCliCommand("LS"));
        Assert.False(WinFileCli.IsCliCommand("C:\\some\\path"));
        Assert.False(WinFileCli.IsCliCommand(null));
    }

    [Fact]
    public void SearchAndList_OverALargeTree()
    {
        using var t = new TestDir();
        for (var d = 0; d < 20; d++)
            for (var f = 0; f < 50; f++)
                t.File($"dir{d}/file{f}.bin");
        var search = RunCli("search", t.Root, "*.bin");
        Assert.Equal(0, search.Exit);
        var list = RunCli("ls", Path.Combine(t.Root, "dir3"));
        Assert.Equal(0, list.Exit);
        Assert.Contains("file7.bin", list.Out);
    }
}
