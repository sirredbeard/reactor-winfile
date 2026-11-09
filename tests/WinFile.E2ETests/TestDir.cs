namespace WinFile.E2ETests;

/// <summary>A throwaway directory tree for one test. Deleted on dispose.</summary>
public sealed class TestDir : IDisposable
{
    public TestDir()
    {
        Root = Path.Combine(Path.GetTempPath(), "reactor-winfile-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string File(string relativePath, string contents = "data")
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, contents);
        return path;
    }

    public string Dir(string relativePath)
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                try { System.IO.File.SetAttributes(f, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(Root, recursive: true);
        }
        catch { }
    }
}
