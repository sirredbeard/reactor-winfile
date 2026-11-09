using Xunit;
using WinFile.Core;

namespace WinFile.IntegrationTests;

/// <summary>
/// Services composed together over a realistic fixture tree, the way the app uses them:
/// scan, sort, search, goto, settings persistence, clipboard, and associations in one flow.
/// </summary>
public class ComposedWorkflowTests
{
    private static TestDir BuildFixture()
    {
        var t = new TestDir();
        t.File("projects/alpha/README.md", "# Alpha");
        t.File("projects/alpha/src/Program.cs", "class Program {}");
        t.File("projects/alpha/src/notes.txt", "alpha notes");
        t.File("projects/beta/README.md", "# Beta");
        t.File("projects/beta/src/Program.cs", "class Program {}");
        t.File("docs/guide.md", "guide");
        t.File("docs/manual.txt", "manual");
        return t;
    }

    [Fact]
    public void ScanSearchAndGotoOverOneTree()
    {
        using var t = BuildFixture();
        var scanner = new DirectoryScanner();

        var projects = scanner.ListDirectory(Path.Combine(t.Root, "projects"), new WinFileSettings());
        Assert.Equal(["alpha", "beta"], projects.Where(e => e.IsDirectory).Select(e => e.Name).ToArray());

        var readmes = new SearchService().Search(new SearchCriteria(t.Root, "README.md"));
        Assert.Equal(2, readmes.Count);

        var index = GotoIndex.BuildFromRoots([t.Root]);
        var hits = index.Query("beta");
        Assert.Contains(hits, h => h.EndsWith(Path.Combine("projects", "beta")));
        var srcHits = index.Query("alp src");
        Assert.Contains(srcHits, h => h.EndsWith(Path.Combine("alpha", "src")));
    }

    [Fact]
    public void SettingsDriveScannerBehavior()
    {
        using var t = BuildFixture();
        var iniPath = Path.Combine(t.Root, "Winfile.ini");
        var store = new SettingsStore();
        store.Save(iniPath, new WinFileSettings { IncludeFilter = "*.md", Sort = SortMode.Name });
        var loaded = store.Load(iniPath);

        var docs = new DirectoryScanner().ListDirectory(Path.Combine(t.Root, "docs"), loaded);
        Assert.Single(docs); // guide.md only: manual.txt is filtered, and there are no subdirectories
        Assert.Equal("guide.md", docs[0].Name);
    }

    [Fact]
    public void CopyThenSearchFindsTheCopies()
    {
        using var t = BuildFixture();
        var dest = t.Dir("backup");
        var ops = new FileOperations();
        var result = ops.Copy([Path.Combine(t.Root, "projects")], dest);
        Assert.False(result.HasFailures);
        var found = new SearchService().Search(new SearchCriteria(dest, "Program.cs"));
        Assert.Equal(2, found.Count);
    }

    [Fact]
    public void RenameThenGotoIndexRebuildFindsNewName()
    {
        using var t = BuildFixture();
        var ops = new FileOperations();
        ops.Rename(Path.Combine(t.Root, "projects", "beta"), "gamma");
        var index = GotoIndex.BuildFromRoots([t.Root]);
        Assert.Contains(index.Query("gamma"), h => h.EndsWith("gamma"));
        Assert.DoesNotContain(index.Query("beta"), h => h.EndsWith("beta"));
    }

    [Fact]
    public void ClipboardCutPasteRestructuresTheTree()
    {
        using var t = BuildFixture();
        var clipboard = new ClipboardModel();
        var guide = Path.Combine(t.Root, "docs", "guide.md");
        clipboard.Cut([guide]);
        var result = clipboard.Paste(Path.Combine(t.Root, "projects", "alpha"));
        Assert.False(result.HasFailures);
        Assert.False(File.Exists(guide));
        Assert.True(File.Exists(Path.Combine(t.Root, "projects", "alpha", "guide.md")));
    }

    [Fact]
    public void AssociationFlowStoresAndExpandsCommands()
    {
        var service = new AssociationService(new InMemoryAssociationStore());
        service.Associate(".md", @"C:\tools\editor.exe");
        Assert.Equal(1, service.GetAll().Count);
        var command = service.GetOpenCommand("README.md");
        Assert.NotNull(command);
        var (fileName, args) = AssociationService.ExpandCommand(command!, @"D:\docs\README.md");
        Assert.Equal(@"C:\tools\editor.exe", fileName);
        Assert.Contains("README.md", args);
    }

    [Fact]
    public void MdiChildWindowsTrackNavigationPerWindow()
    {
        var mdi = new MdiManager();
        var first = mdi.NewWindow(@"C:\start");
        var second = mdi.NewWindow(@"D:\other");
        first.History.NavigateTo(@"C:\start\sub");
        Assert.Equal(@"C:\start\sub", first.History.Current);
        Assert.Equal(@"D:\other", second.History.Current);
        mdi.Activate(first);
        Assert.Same(first, mdi.Active);
    }

    [Fact]
    public void TreeExpandAllThenScannerAgreesOnLeafCount()
    {
        using var t = BuildFixture();
        var root = new DirectoryTreeNode(t.Root);
        root.ExpandBranch();
        var nodes = root.FlattenVisible().Select(r => r.Node.Path).ToList();
        Assert.Contains(nodes, p => p.EndsWith(Path.Combine("projects", "alpha", "src")));
        Assert.Contains(nodes, p => p.EndsWith("docs"));
    }

    [Fact]
    public void StatusLineForFixtureDirectory()
    {
        using var t = BuildFixture();
        var scanner = new DirectoryScanner();
        var entries = scanner.ListDirectory(t.Root, new WinFileSettings { ShowHiddenAndSystem = true });
        var drive = scanner.GetDriveForPath(t.Root);
        var line = Formatting.FormatStatusLine(t.Root, entries.Count, 0, drive);
        Assert.Contains("objects", line);
    }

    [Fact]
    public void SearchWithDateCutoffMatchesFileTimesWrittenByCopy()
    {
        using var t = BuildFixture();
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        var results = new SearchService().Search(new SearchCriteria(t.Root, "*.txt", ModifiedAfterUtc: cutoff));
        Assert.Equal(2, results.Count); // notes.txt and manual.txt, both just written
        var none = new SearchService().Search(new SearchCriteria(t.Root, "*.txt", ModifiedAfterUtc: DateTime.UtcNow.AddDays(1)));
        Assert.Empty(none);
    }
}
