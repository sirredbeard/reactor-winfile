using Xunit;
using WinFile.Core;

namespace WinFile.FunctionalTests;

public class FileOperationsTests
{
    [Fact]
    public void CopyFilePreservesContents()
    {
        using var t = new TestDir();
        var source = t.File("a.txt", "hello winfile");
        var destDir = t.Dir("dest");
        var result = new FileOperations().Copy([source], destDir);
        Assert.False(result.HasFailures);
        Assert.Equal("hello winfile", File.ReadAllText(Path.Combine(destDir, "a.txt")));
        Assert.True(File.Exists(source), "copy keeps the source");
    }

    [Fact]
    public void CopyDirectoryRecurses()
    {
        using var t = new TestDir();
        t.File("tree/sub/deep.txt", "deep");
        t.File("tree/top.txt", "top");
        var destDir = t.Dir("out");
        var result = new FileOperations().Copy([Path.Combine(t.Root, "tree")], destDir);
        Assert.False(result.HasFailures);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal("deep", File.ReadAllText(Path.Combine(destDir, "tree", "sub", "deep.txt")));
    }

    [Fact]
    public void CopyIntoSameDirectoryUsesCopySuffix()
    {
        using var t = new TestDir();
        var source = t.File("report.txt", "v1");
        var result = new FileOperations().Copy([source], t.Root);
        Assert.False(result.HasFailures);
        var copy = Path.Combine(t.Root, "report - Copy.txt");
        Assert.True(File.Exists(copy));
        Assert.Equal("v1", File.ReadAllText(copy));
        Assert.Equal("v1", File.ReadAllText(source));
    }

    [Fact]
    public void CopyReportsProgress()
    {
        using var t = new TestDir();
        var source = t.File("big.bin", new string('x', 10000));
        var destDir = t.Dir("dest");
        CopyProgress? last = null;
        var progress = new Progress<CopyProgress>(p => last = p);
        new FileOperations().Copy([source], destDir, progress: progress);
        // Progress<T> posts asynchronously; poll briefly.
        for (var i = 0; i < 50 && last is null; i++) Thread.Sleep(20);
        Assert.NotNull(last);
        Assert.Equal(1, last!.FilesTotal);
        Assert.Equal(10000, last.BytesTotal);
    }

    [Fact]
    public void MoveFileRelocatesAndRemovesSource()
    {
        using var t = new TestDir();
        var source = t.File("m.txt", "move me");
        var destDir = t.Dir("dest");
        var result = new FileOperations().Move([source], destDir);
        Assert.False(result.HasFailures);
        Assert.False(File.Exists(source));
        Assert.Equal("move me", File.ReadAllText(Path.Combine(destDir, "m.txt")));
    }

    [Fact]
    public void CopyOntoExistingFailsByDefaultAndOverwritesWhenAsked()
    {
        using var t = new TestDir();
        var source = t.File("src/f.txt", "new");
        var destDir = t.Dir("dst");
        File.WriteAllText(Path.Combine(destDir, "f.txt"), "old");
        var failed = new FileOperations().Copy([source], destDir, ConflictAction.Fail);
        Assert.True(failed.HasFailures);
        Assert.Equal("old", File.ReadAllText(Path.Combine(destDir, "f.txt")));
        var ok = new FileOperations().Copy([source], destDir, ConflictAction.Overwrite);
        Assert.False(ok.HasFailures);
        Assert.Equal("new", File.ReadAllText(Path.Combine(destDir, "f.txt")));
    }

    [Fact]
    public void CopySkipsExistingWhenAsked()
    {
        using var t = new TestDir();
        var source = t.File("src/f.txt", "new");
        var destDir = t.Dir("dst");
        File.WriteAllText(Path.Combine(destDir, "f.txt"), "old");
        var result = new FileOperations().Copy([source], destDir, ConflictAction.Skip);
        Assert.False(result.HasFailures);
        Assert.Equal("old", File.ReadAllText(Path.Combine(destDir, "f.txt")));
    }

    [Fact]
    public void DeleteRemovesFilesAndDirectories()
    {
        using var t = new TestDir();
        var file = t.File("gone.txt");
        var dir = t.Dir("gone-dir");
        t.File("gone-dir/inner.txt");
        var result = new FileOperations().Delete([file, dir]);
        Assert.False(result.HasFailures);
        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void DeleteMissingPathReportsFailureNotException()
    {
        using var t = new TestDir();
        var result = new FileOperations().Delete([Path.Combine(t.Root, "nope.txt")]);
        Assert.True(result.HasFailures);
    }

    [Fact]
    public void RenameChangesTheNameInPlace()
    {
        using var t = new TestDir();
        var source = t.File("old.txt", "x");
        var dest = new FileOperations().Rename(source, "new.txt");
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(dest));
    }

    [Fact]
    public void RenameToExistingNameThrows()
    {
        using var t = new TestDir();
        var a = t.File("a.txt");
        t.File("b.txt");
        Assert.Throws<IOException>(() => new FileOperations().Rename(a, "b.txt"));
    }

    [Fact]
    public void CreateDirectoryAndFileWork()
    {
        using var t = new TestDir();
        var ops = new FileOperations();
        var dir = ops.CreateDirectory(t.Root, "fresh");
        Assert.True(Directory.Exists(dir));
        var file = ops.CreateFile(dir, "empty.txt");
        Assert.True(File.Exists(file));
        Assert.Equal(0, new FileInfo(file).Length);
    }
}

public class DirectoryScannerTests
{
    [Fact]
    public void ListDirectoryAppliesSortAndFilter()
    {
        using var t = new TestDir();
        t.File("b.txt");
        t.File("a.md");
        t.File("c.txt");
        t.Dir("subdir");
        var scanner = new DirectoryScanner();
        var all = scanner.ListDirectory(t.Root, new WinFileSettings { Sort = SortMode.Name });
        Assert.Equal("subdir", all[0].Name); // directories first
        var txtOnly = scanner.ListDirectory(t.Root, new WinFileSettings { IncludeFilter = "*.txt" });
        Assert.DoesNotContain(txtOnly, e => e.Name == "a.md");
        Assert.Contains(txtOnly, e => e.Name == "b.txt");
    }

    [Fact]
    public void ListDirectoryMissingThrows()
    {
        var scanner = new DirectoryScanner();
        Assert.Throws<DirectoryNotFoundException>(() =>
            scanner.ListDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), new WinFileSettings()));
    }

    [Fact]
    public void GetDrivesReturnsAtLeastTheSystemDrive()
    {
        var drives = new DirectoryScanner().GetDrives();
        Assert.NotEmpty(drives);
    }

    [Fact]
    public void SubdirectoryQueriesMatchTheTree()
    {
        using var t = new TestDir();
        t.Dir("one");
        t.Dir("two/nested");
        var scanner = new DirectoryScanner();
        var subs = scanner.GetSubdirectories(t.Root);
        Assert.Equal(2, subs.Count);
        Assert.True(scanner.HasSubdirectories(t.Root));
        Assert.False(scanner.HasSubdirectories(Path.Combine(t.Root, "one")));
    }

    [Fact]
    public void DirectorySizeSumsRecursively()
    {
        using var t = new TestDir();
        t.File("s/a.bin", "12345");
        t.File("s/deep/b.bin", "123");
        Assert.Equal(8, new DirectoryScanner().GetDirectorySize(Path.Combine(t.Root, "s")));
    }
}

public class SearchServiceTests
{
    [Fact]
    public void RecursiveSearchFindsNestedMatchesNewestFirst()
    {
        using var t = new TestDir();
        var older = t.File("root-match.log", "x");
        var newer = t.File("sub/nested-match.log", "x");
        t.File("sub/skip.txt", "x");
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var results = new SearchService().Search(new SearchCriteria(t.Root, "*.log"));
        Assert.Equal(2, results.Count);
        Assert.Equal(newer, results[0].FullPath);
    }

    [Fact]
    public void TopLevelOnlySearchSkipsSubdirectories()
    {
        using var t = new TestDir();
        t.File("top.log");
        t.File("sub/deep.log");
        var results = new SearchService().Search(new SearchCriteria(t.Root, "*.log", IncludeSubdirectories: false));
        Assert.Single(results);
    }

    [Fact]
    public void ModifiedAfterFiltersOldFiles()
    {
        using var t = new TestDir();
        var old = t.File("old.log");
        var fresh = t.File("fresh.log");
        File.SetLastWriteTimeUtc(old, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow);
        var results = new SearchService().Search(new SearchCriteria(
            t.Root, "*.log", ModifiedAfterUtc: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Single(results);
        Assert.Equal(fresh, results[0].FullPath);
    }
}

public class TreeModelTests
{
    [Fact]
    public void ExpandBranchLoadsTheWholeSubtree()
    {
        using var t = new TestDir();
        t.Dir("a/b/c");
        var root = new DirectoryTreeNode(t.Root);
        root.ExpandBranch();
        var flat = root.FlattenVisible().ToList();
        Assert.Contains(flat, row => row.Node.Name == "c");
        Assert.True(root.IsExpanded);
    }

    [Fact]
    public void CollapseHidesChildren()
    {
        using var t = new TestDir();
        t.Dir("a");
        var root = new DirectoryTreeNode(t.Root);
        root.ExpandOneLevel();
        Assert.Equal(2, root.FlattenVisible().Count());
        root.CollapseBranch();
        Assert.Single(root.FlattenVisible());
    }

    [Fact]
    public void FindLocatesNestedNodes()
    {
        using var t = new TestDir();
        var target = t.Dir("x/y");
        var root = new DirectoryTreeNode(t.Root);
        var found = root.Find(target);
        Assert.NotNull(found);
        Assert.Equal("y", found!.Name);
    }
}

public class LinkAndAttributeTests
{
    [Fact]
    public void SymbolicLinkToFileReadsTarget()
    {
        using var t = new TestDir();
        var target = t.File("target.txt", "linked");
        var link = Path.Combine(t.Root, "link.txt");
        LinkService.CreateSymbolicLink(link, target, targetIsDirectory: false);
        Assert.Equal("linked", File.ReadAllText(link));
        var entry = FileEntry.FromFileSystemInfo(new FileInfo(link));
        Assert.True(entry.IsReparsePoint);
        Assert.Equal(target, entry.LinkTarget);
    }

    [Fact]
    public void SymbolicLinkToDirectoryWorks()
    {
        using var t = new TestDir();
        var targetDir = t.Dir("real");
        t.File("real/inside.txt", "in");
        var link = Path.Combine(t.Root, "dirlink");
        LinkService.CreateSymbolicLink(link, targetDir, targetIsDirectory: true);
        Assert.Equal("in", File.ReadAllText(Path.Combine(link, "inside.txt")));
    }

    [Fact]
    public void AttributesRoundTrip()
    {
        using var t = new TestDir();
        var file = t.File("attrs.txt");
        AttributesService.Set(file, readOnly: true);
        Assert.True((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0);
        AttributesService.Set(file, readOnly: false);
        Assert.True((File.GetAttributes(file) & FileAttributes.ReadOnly) == 0);
    }

    [Fact]
    public void AttributeLettersReflectFlags()
    {
        var entry = new FileEntry("p", "p.txt", false, 1, DateTime.UtcNow, DateTime.UtcNow,
            FileAttributes.ReadOnly | FileAttributes.Archive, true, "target");
        Assert.Equal("R-A--L", AttributesService.ToLetters(entry));
    }
}

public class ClipboardModelTests
{
    [Fact]
    public void PasteAfterCopyDuplicatesIntoAnotherDirectory()
    {
        using var t = new TestDir();
        var source = t.File("c.txt", "copy me");
        var dest = t.Dir("dest");
        var clipboard = new ClipboardModel();
        clipboard.Copy([source]);
        var result = clipboard.Paste(dest);
        Assert.False(result.HasFailures);
        Assert.Equal("copy me", File.ReadAllText(Path.Combine(dest, "c.txt")));
        Assert.True(clipboard.HasContent, "copy stays on the clipboard");
    }

    [Fact]
    public void PasteAfterCutMovesAndClears()
    {
        using var t = new TestDir();
        var source = t.File("c.txt", "cut me");
        var dest = t.Dir("dest");
        var clipboard = new ClipboardModel();
        clipboard.Cut([source]);
        var result = clipboard.Paste(dest);
        Assert.False(result.HasFailures);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(dest, "c.txt")));
        Assert.False(clipboard.HasContent, "cut clears after a successful paste");
    }

    [Fact]
    public void PasteCopyIntoSameDirectoryCreatesCopySuffix()
    {
        using var t = new TestDir();
        var source = t.File("c.txt", "same dir");
        var clipboard = new ClipboardModel();
        clipboard.Copy([source]);
        var result = clipboard.Paste(t.Root);
        Assert.False(result.HasFailures);
        Assert.True(File.Exists(Path.Combine(t.Root, "c - Copy.txt")));
    }

    [Fact]
    public void PasteWithEmptyClipboardIsANoOp()
    {
        using var t = new TestDir();
        var result = new ClipboardModel().Paste(t.Root);
        Assert.Equal(0, result.Succeeded);
        Assert.False(result.HasFailures);
    }
}

public class SelectionServiceTests
{
    [Fact]
    public void SelectByPatternPicksMatchingFiles()
    {
        using var t = new TestDir();
        t.File("a.txt");
        t.File("b.md");
        t.File("c.txt");
        var entries = new DirectoryScanner().ListDirectory(t.Root, new WinFileSettings { ShowHiddenAndSystem = true });
        var selected = SelectionService.SelectByPattern(entries, "*.txt");
        Assert.Equal(2, selected.Count);
    }
}

public class WatcherTests
{
    [Fact]
    public void WatcherSignalsOnCreate()
    {
        using var t = new TestDir();
        using var watcher = new DirectoryWatcher(t.Root);
        using var signal = new ManualResetEventSlim();
        watcher.Changed += (_, _) => signal.Set();
        t.File("new.txt");
        Assert.True(signal.Wait(TimeSpan.FromSeconds(10)), "watcher should report the new file");
    }
}

public class WatcherDebounceTests
{
    [Fact]
    public void BurstOfChanges_RaisesChangedOnce()
    {
        using var t = new TestDir();
        using var watcher = new DirectoryWatcher(t.Root, TimeSpan.FromMilliseconds(400));
        var count = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref count);
        for (var i = 0; i < 8; i++) t.File($"f{i}.txt");
        // The burst settles, then exactly one notification should land.
        Thread.Sleep(TimeSpan.FromMilliseconds(1800));
        Assert.Equal(1, count);
    }
}

public class SelectionDeselectTests
{
    [Fact]
    public void DeselectByPattern_RemovesOnlyMatches()
    {
        using var t = new TestDir();
        var a = t.File("a.txt");
        var b = t.File("b.txt");
        var c = t.File("c.md");
        var scanner = new DirectoryScanner();
        var entries = scanner.ListDirectoryRaw(t.Root);
        var remaining = SelectionService.DeselectByPattern(entries, "*.txt", [a, b, c]);
        Assert.Equal([c], remaining);
    }
}

public class LargeTreeTests
{
    private static TestDir BuildTree(int dirs, int filesPerDir)
    {
        var t = new TestDir();
        for (var d = 0; d < dirs; d++)
            for (var f = 0; f < filesPerDir; f++)
                t.File($"dir{d}/file{f}.txt", "x");
        return t;
    }

    [Fact]
    public void ListDirectory_TwoThousandFiles_NaturalOrder()
    {
        using var t = new TestDir();
        for (var i = 1; i <= 2000; i++) t.File($"f{i}.txt", "x");
        var scanner = new DirectoryScanner();
        var entries = scanner.ListDirectory(t.Root, new WinFileSettings());
        Assert.Equal(2000, entries.Count);
        var names = entries.Select(e => e.Name).ToList();
        Assert.True(names.IndexOf("f2.txt") < names.IndexOf("f10.txt"), "natural sort puts f2 before f10");
        Assert.Equal("f1.txt", names[0]);
    }

    [Fact]
    public void Search_And_GotoIndex_OverFiveThousandFiles()
    {
        using var t = BuildTree(50, 100);
        var search = new SearchService();
        var results = search.Search(new SearchCriteria(t.Root, "*.txt"));
        Assert.Equal(5000, results.Count);

        var index = GotoIndex.BuildFromRoots([t.Root]);
        Assert.Equal(51, index.Count);
        Assert.Contains(index.Query("dir7"), p => p.EndsWith("dir7"));
    }

    [Fact]
    public void LargeTreeOperations_StayInsideAGenerousBudget()
    {
        // A smoke guard against pathological regressions (quadratic dedupe,
        // per-file stats in a hot loop), not a benchmark: the budget is wide
        // enough for a loaded CI runner and far under what the old shapes cost.
        using var t = BuildTree(50, 100);
        var scanner = new DirectoryScanner();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _ = scanner.ListDirectory(Path.Combine(t.Root, "dir0"), new WinFileSettings());
        _ = new SearchService().Search(new SearchCriteria(t.Root, "*.txt"));
        _ = GotoIndex.BuildFromRoots([t.Root]);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"large-tree pass took {sw.Elapsed}");
    }
}

public class DeleteConfirmationTests
{
    private static FileEntry Entry(bool dir = false, FileAttributes attrs = FileAttributes.Normal) =>
        new(@"C:\work\thing", "thing", dir, 0, DateTime.UnixEpoch, DateTime.UnixEpoch, attrs, false, null);

    [Fact]
    public void PlainFile_Defaults_AsksForDelete()
    {
        Assert.Equal(DeleteConfirmReason.Delete,
            SelectionService.DeleteConfirmationFor([Entry()], ConfirmationSettings.Default));
    }

    [Fact]
    public void PlainFile_DeleteOff_NeedsNothing()
    {
        var c = ConfirmationSettings.Default with { OnDelete = false };
        Assert.Equal(DeleteConfirmReason.None,
            SelectionService.DeleteConfirmationFor([Entry()], c));
    }

    [Fact]
    public void Directory_AsksForSubtreeDelete_ThenFallsBackToDelete()
    {
        Assert.Equal(DeleteConfirmReason.SubtreeDelete,
            SelectionService.DeleteConfirmationFor([Entry(dir: true)], ConfirmationSettings.Default));
        var c = ConfirmationSettings.Default with { OnSubtreeDelete = false };
        Assert.Equal(DeleteConfirmReason.Delete,
            SelectionService.DeleteConfirmationFor([Entry(dir: true)], c));
    }

    [Fact]
    public void ProtectedFile_OutranksEverything()
    {
        var ro = Entry(attrs: FileAttributes.ReadOnly);
        Assert.Equal(DeleteConfirmReason.ProtectedFile,
            SelectionService.DeleteConfirmationFor([ro], ConfirmationSettings.Default));
        Assert.Equal(DeleteConfirmReason.ProtectedFile,
            SelectionService.DeleteConfirmationFor([Entry(dir: true, attrs: FileAttributes.Hidden)], ConfirmationSettings.Default));
        var c = ConfirmationSettings.Default with { OnReadOnlySystemHidden = false };
        Assert.Equal(DeleteConfirmReason.Delete,
            SelectionService.DeleteConfirmationFor([ro], c));
    }

    [Fact]
    public void EmptySelection_NeedsNothing()
    {
        Assert.Equal(DeleteConfirmReason.None,
            SelectionService.DeleteConfirmationFor([], ConfirmationSettings.Default));
    }
}
