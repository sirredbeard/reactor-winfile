using Xunit;
using WinFile.Core;

namespace WinFile.UnitTests;

public class FormattingTests
{
    [Fact]
    public void FormatBytes_ScalesUnits()
    {
        Assert.Equal("512 bytes", Formatting.FormatBytes(512));
        Assert.Equal("1.0 KB", Formatting.FormatBytes(1024));
        Assert.Equal("1.5 MB", Formatting.FormatBytes(1024 * 1024 + 512 * 1024));
    }

    [Fact]
    public void FormatLong_GroupsThousands()
    {
        var formatted = Formatting.FormatLong(1234567);
        Assert.Contains("1", formatted);
        Assert.True(formatted.Length >= 9, formatted);
    }

    [Fact]
    public void StatusLine_IncludesObjectsAndFreeSpace()
    {
        var drive = new DriveEntry(@"C:\", @"C:\", "System", DriveType.Fixed, 2000, 1000, true);
        var line = Formatting.FormatStatusLine(@"C:\work", 3, 0, drive);
        Assert.Contains("3 objects", line);
        Assert.Contains("bytes free of", line);
    }

    [Fact]
    public void StatusLine_SingularObject()
    {
        var line = Formatting.FormatStatusLine(@"C:\work", 1, 0, null);
        Assert.Contains("1 object", line);
        Assert.DoesNotContain("objects", line.Replace("1 object", string.Empty));
    }

    [Theory]
    [InlineData(@"C:\a\b.txt", @"C:\a\b.txt")]
    [InlineData("relative.txt", "relative.txt")]
    public void StripLongPathPrefix_LeavesOrdinaryPathsAlone(string input, string expectedPart)
    {
        Assert.Contains(expectedPart.Split('\\')[^1], Formatting.StripLongPathPrefix(input));
    }
}

public class NaturalSortTests
{
    [Fact]
    public void NumbersSortByValueNotLexically()
    {
        var names = new[] { "file10.txt", "file2.txt", "file1.txt", "File3.txt" };
        var sorted = names.OrderBy(n => n, NaturalStringComparer.Instance).ToArray();
        Assert.Equal(["file1.txt", "file2.txt", "File3.txt", "file10.txt"], sorted);
    }

    [Fact]
    public void PlainNamesSortAlphabeticallyCaseInsensitive()
    {
        var names = new[] { "banana", "Apple", "cherry" };
        var sorted = names.OrderBy(n => n, NaturalStringComparer.Instance).ToArray();
        Assert.Equal(["Apple", "banana", "cherry"], sorted);
    }

    [Fact]
    public void EqualNumbersFallThroughToRemainder()
    {
        Assert.True(NaturalStringComparer.Instance.Compare("a01b", "a1c") != 0);
        Assert.Equal(0, NaturalStringComparer.Instance.Compare("same2", "same2"));
    }
}

public class EntrySorterTests
{
    private static FileEntry Entry(string name, bool dir = false, long size = 0, int day = 1, string ext = "")
    {
        var full = name;
        return new FileEntry(full, name, dir, size, new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc),
            DateTime.UnixEpoch, dir ? FileAttributes.Directory : FileAttributes.Normal, false, null);
    }

    [Fact]
    public void DirectoriesComeFirst()
    {
        var sorted = EntrySorter.Sort([Entry("aaa.txt"), Entry("zzz", dir: true)], SortMode.Name);
        Assert.Equal("zzz", sorted[0].Name);
    }

    [Fact]
    public void SortBySizeAscending()
    {
        var sorted = EntrySorter.Sort([Entry("big", size: 300), Entry("small", size: 5), Entry("mid", size: 50)], SortMode.Size);
        Assert.Equal(["small", "mid", "big"], sorted.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void SortByDateNewestFirstByDefault()
    {
        var sorted = EntrySorter.Sort([Entry("old", day: 1), Entry("new", day: 9)], SortMode.Date);
        Assert.Equal("new", sorted[0].Name);
    }

    [Fact]
    public void SortByDateForwardOldestFirst()
    {
        var sorted = EntrySorter.Sort([Entry("old", day: 1), Entry("new", day: 9)], SortMode.DateForward);
        Assert.Equal("old", sorted[0].Name);
    }

    [Fact]
    public void SortByTypeGroupsExtensions()
    {
        var sorted = EntrySorter.Sort(
            [Entry("b.md"), Entry("a.txt"), Entry("c.md")], SortMode.Type);
        // .md group before .txt group, natural order inside each group.
        Assert.Equal(["b.md", "c.md", "a.txt"], sorted.Select(e => e.Name).ToArray());
    }
}

public class FileTypeFilterTests
{
    [Theory]
    [InlineData("notes.txt", "*.txt", true)]
    [InlineData("notes.md", "*.txt", false)]
    [InlineData("notes.md", "*.txt;*.md", true)]
    [InlineData("anything.exe", "*.*", true)]
    [InlineData("README", "*.*", true)]
    [InlineData("a1.txt", "a?.txt", true)]
    [InlineData("a12.txt", "a?.txt", false)]
    [InlineData("ARCHIVE.ZIP", "*.zip", true)]
    public void WildcardAndPatternMatching(string name, string filter, bool expected)
    {
        Assert.Equal(expected, FileTypeFilter.MatchesFilter(name, filter));
    }

    [Fact]
    public void ApplyExcludesHiddenUnlessSettingShowsThem()
    {
        var hidden = new FileEntry("h", "h.txt", false, 1, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Hidden, false, null);
        var visible = new FileEntry("v", "v.txt", false, 1, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Normal, false, null);
        var strict = FileTypeFilter.Apply([hidden, visible], new WinFileSettings()).ToList();
        Assert.Single(strict);
        var lax = FileTypeFilter.Apply([hidden, visible], new WinFileSettings { ShowHiddenAndSystem = true }).ToList();
        Assert.Equal(2, lax.Count);
    }

    [Fact]
    public void DirectoriesAlwaysPassTheFilter()
    {
        var dir = new FileEntry("d", "stuff", true, 0, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Directory, false, null);
        var result = FileTypeFilter.Apply([dir], new WinFileSettings { IncludeFilter = "*.txt" }).ToList();
        Assert.Single(result);
    }
}

public class IniFileTests
{
    [Fact]
    public void ParseReadsSectionsAndValues()
    {
        var ini = IniFile.Parse("[Settings]\r\nEditorPath=C:\\tools\\edit.exe\r\nFlag=1\r\n[Other]\r\nX=y\r\n");
        Assert.Equal(@"C:\tools\edit.exe", ini.Get("Settings", "EditorPath"));
        Assert.True(ini.GetBool("Settings", "Flag", false));
        Assert.Equal("y", ini.Get("other", "x"));
    }

    [Fact]
    public void RoundTripPreservesValues()
    {
        var ini = new IniFile();
        ini.Set("Settings", "CachedPath", @"D:\cache");
        ini.SetBool("Settings", "ScrollOnExpand", false);
        var parsed = IniFile.Parse(ini.ToString());
        Assert.Equal(@"D:\cache", parsed.Get("Settings", "CachedPath"));
        Assert.False(parsed.GetBool("Settings", "ScrollOnExpand", true));
    }

    [Fact]
    public void SetOverwritesExistingKey()
    {
        var ini = new IniFile();
        ini.Set("S", "K", "one");
        ini.Set("S", "K", "two");
        Assert.Equal("two", ini.Get("S", "K"));
    }

    [Fact]
    public void MissingKeysReturnFallbacks()
    {
        var ini = new IniFile();
        Assert.Equal("fb", ini.Get("S", "K", "fb"));
        Assert.Equal(42, ini.GetInt("S", "K", 42));
        Assert.True(ini.GetBool("S", "K", true));
    }
}

public class SettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Winfile.ini");

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Fact]
    public void MissingFileReturnsDefaults()
    {
        var settings = new SettingsStore().Load(_path);
        Assert.Equal("-_", settings.GotoCachePunctuation);
        Assert.True(settings.ScrollOnExpand);
        Assert.Equal("*.*", settings.IncludeFilter);
        Assert.Equal(SortMode.Name, settings.Sort);
    }

    [Fact]
    public void RoundTripPreservesEveryField()
    {
        var store = new SettingsStore();
        var saved = new WinFileSettings
        {
            EditorPath = @"C:\edit.exe",
            CachedPath = @"C:\root",
            GotoCachePunctuation = "-_.",
            ScrollOnExpand = false,
            IncludeFilter = "*.cs",
            ShowHiddenAndSystem = true,
            FontName = "Consolas",
            FontSize = 12.5,
            LastDirectory = @"D:\work",
            SplitPosition = 0.5,
            Sort = SortMode.Date,
            ViewDetails = ViewDetailMode.PartialDetails,
            Pane = PaneView.TreeOnly,
            Confirmations = new ConfirmationSettings(false, false, false, true, true),
            PartialDetails = new PartialDetailSettings(false, true, true, true),
            ShowToolbar = false,
            MinimizeOnUse = true,
            IndexOnLaunch = true,
            SaveSettingsOnExit = false,
            WindowX = 10, WindowY = 20, WindowWidth = 800, WindowHeight = 600,
            ToolbarButtons = ["search", "open"],
        };
        store.Save(_path, saved);
        var loaded = store.Load(_path);
        Assert.Equal(saved.EditorPath, loaded.EditorPath);
        Assert.Equal(saved.CachedPath, loaded.CachedPath);
        Assert.Equal("-_.", loaded.GotoCachePunctuation);
        Assert.False(loaded.ScrollOnExpand);
        Assert.Equal("*.cs", loaded.IncludeFilter);
        Assert.True(loaded.ShowHiddenAndSystem);
        Assert.Equal("Consolas", loaded.FontName);
        Assert.Equal(12.5, loaded.FontSize);
        Assert.Equal(@"D:\work", loaded.LastDirectory);
        Assert.Equal(0.5, loaded.SplitPosition);
        Assert.Equal(SortMode.Date, loaded.Sort);
        Assert.Equal(ViewDetailMode.PartialDetails, loaded.ViewDetails);
        Assert.Equal(PaneView.TreeOnly, loaded.Pane);
        Assert.Equal(saved.Confirmations, loaded.Confirmations);
        Assert.Equal(saved.PartialDetails, loaded.PartialDetails);
        Assert.False(loaded.ShowToolbar);
        Assert.True(loaded.MinimizeOnUse);
        Assert.True(loaded.IndexOnLaunch);
        Assert.False(loaded.SaveSettingsOnExit);
        Assert.Equal(10, loaded.WindowX);
        Assert.Equal(600, loaded.WindowHeight);
        Assert.Equal(["search", "open"], loaded.ToolbarButtons);
    }
}

public class NavigationHistoryTests
{
    [Fact]
    public void BackAndForwardWalkTheStack()
    {
        var history = new NavigationHistory(@"C:\a");
        history.NavigateTo(@"C:\b");
        history.NavigateTo(@"C:\c");
        Assert.True(history.CanGoBack);
        Assert.Equal(@"C:\b", history.GoBack());
        Assert.Equal(@"C:\a", history.GoBack());
        Assert.False(history.CanGoBack);
        Assert.Equal(@"C:\b", history.GoForward());
        Assert.Equal(@"C:\c", history.Current == @"C:\b" ? history.GoForward() : null);
    }

    [Fact]
    public void NavigatingClearsForward()
    {
        var history = new NavigationHistory(@"C:\a");
        history.NavigateTo(@"C:\b");
        history.GoBack();
        history.NavigateTo(@"C:\z");
        Assert.False(history.CanGoForward);
        Assert.Equal(@"C:\z", history.Current);
    }

    [Fact]
    public void SamePathNavigationIsANoOp()
    {
        var history = new NavigationHistory(@"C:\a");
        history.NavigateTo(@"C:\a");
        Assert.False(history.CanGoBack);
    }
}

public class GotoIndexUnitTests
{
    [Fact]
    public void SplitWordsUsesPunctuationAndSeparators()
    {
        var index = new GotoIndex("-_");
        var words = index.SplitWords(@"C:\my-projects\win_file\src");
        Assert.Contains("my", words);
        Assert.Contains("projects", words);
        Assert.Contains("win", words);
        Assert.Contains("file", words);
        Assert.Contains("src", words);
    }

    [Fact]
    public void CustomPunctuationIsHonored()
    {
        var index = new GotoIndex(".");
        var words = index.SplitWords("alpha.beta");
        Assert.Equal(["alpha", "beta"], words);
    }

    [Fact]
    public void DuplicatePathsAreIndexedOnce()
    {
        var index = new GotoIndex();
        var path = Path.GetTempPath();
        index.Add(path);
        index.Add(path);
        Assert.Equal(1, index.Count);
    }
}

public class WindowLayoutTests
{
    private static readonly WindowRect Area = new(0, 0, 900, 600);

    [Fact]
    public void CascadeOffsetsEachWindow()
    {
        var rects = WindowLayout.Cascade(3, Area);
        Assert.Equal(3, rects.Count);
        Assert.True(rects[1].X > rects[0].X);
        Assert.True(rects[2].Y > rects[1].Y);
    }

    [Fact]
    public void TileHorizontalSplitsHeightEvenly()
    {
        var rects = WindowLayout.TileHorizontal(2, Area);
        Assert.Equal(300, rects[0].Height);
        Assert.Equal(300, rects[1].Y);
        Assert.Equal(900, rects[1].Width);
    }

    [Fact]
    public void TileVerticalSplitsWidthEvenly()
    {
        var rects = WindowLayout.TileVertical(4, Area);
        Assert.Equal(225, rects[1].X);
        Assert.Equal(675, rects[3].X);
        Assert.Equal(225, rects[0].Width);
    }

    [Fact]
    public void ArrangeIconsRowsAlongTheBottom()
    {
        var rects = WindowLayout.ArrangeIcons(3, Area, iconWidth: 100, iconHeight: 50);
        Assert.Equal(550, rects[0].Y);
        Assert.Equal(100, rects[1].X);
    }

    [Fact]
    public void ZeroWindowsProduceNoRects()
    {
        Assert.Empty(WindowLayout.Cascade(0, Area));
        Assert.Empty(WindowLayout.TileHorizontal(0, Area));
        Assert.Empty(WindowLayout.ArrangeIcons(0, Area));
    }
}

public class AssociationUnitTests
{
    [Fact]
    public void ExpandCommandReplacesPlaceholder()
    {
        var (fileName, args) = AssociationService.ExpandCommand("\"C:\\Program Files\\Edit\\edit.exe\" \"%1\"", @"D:\a b.txt");
        Assert.Equal(@"C:\Program Files\Edit\edit.exe", fileName);
        Assert.Contains("a b.txt", args);
    }

    [Fact]
    public void InMemoryStoreRoundTrips()
    {
        var service = new AssociationService(new InMemoryAssociationStore());
        service.Associate(".xyz", @"C:\tools\xyz.exe");
        var command = service.GetOpenCommand("file.xyz");
        Assert.NotNull(command);
        Assert.Contains("xyz.exe", command);
        service.Remove(".xyz");
        Assert.Null(service.GetOpenCommand("file.xyz"));
    }

    [Fact]
    public void ExtensionLookupIsCaseInsensitiveAndDotOptional()
    {
        var service = new AssociationService(new InMemoryAssociationStore());
        service.Associate("MD", @"C:\tools\md.exe");
        Assert.NotNull(service.GetOpenCommand(".md"));
        Assert.NotNull(service.GetOpenCommand("notes.Md"));
    }
}

public class DuplicateNameTests
{
    [Fact]
    public void DuplicateNameAppendsCopySuffix()
    {
        var dir = Path.GetTempPath();
        var name = FileOperations.DuplicateName(Path.Combine(dir, "report.txt"));
        Assert.EndsWith("report - Copy.txt", name);
    }

    [Fact]
    public void SamePathIgnoresTrailingSeparators()
    {
        var dir = Path.GetTempPath();
        Assert.True(FileOperations.SamePath(dir, dir + Path.DirectorySeparatorChar));
    }
}

public class MdiManagerTests
{
    [Fact]
    public void NewWindowBecomesActiveAndCloseFallsBack()
    {
        var mdi = new MdiManager();
        var first = mdi.NewWindow(@"C:\a");
        var second = mdi.NewWindow(@"C:\b");
        Assert.Same(second, mdi.Active);
        mdi.Close(second);
        Assert.Same(first, mdi.Active);
        Assert.Single(mdi.Windows);
    }

    [Fact]
    public void TileAppliesBoundsToEveryWindow()
    {
        var mdi = new MdiManager();
        mdi.NewWindow(@"C:\a");
        mdi.NewWindow(@"C:\b");
        mdi.ApplyTileVertical(new WindowRect(0, 0, 800, 600));
        Assert.All(mdi.Windows, w => Assert.Equal(400, w.Bounds.Width));
    }
}
