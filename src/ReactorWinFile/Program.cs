using System.Runtime.InteropServices;
using ReactorWinFile;
using WinFile.Core;

// Reactor WinFile entry point. One binary, three modes:
//
//   winfile.exe                GUI (Microsoft.UI.Reactor over WinUI 3)
//   winfile.exe <verb> ...     Console mode, handled by WinFile.Core.WinFileCli
//   winfile.exe --self-test <result-file>
//                              Headless end-to-end check used by CI on Windows
//
// A directory path as the only argument opens the GUI at that directory,
// matching the enhanced WinFile command line.

if (args.Length > 0 && args[0] == "--self-test")
{
    return SelfTest.Run(args.Length > 1 ? args[1] : "self-test-result.txt");
}

if (args.Length > 0 && WinFileCli.IsCliCommand(args[0]))
{
    return WinFileCli.Run(args, Console.Out, Console.Error);
}

var startDirectory = args.Length > 0 && Directory.Exists(args[0])
    ? Path.GetFullPath(args[0])
    : null;

WinFileApp.StartDirectory = startDirectory;
Microsoft.UI.Reactor.ReactorApp.Run<WinFileApp>("Windows File Manager", width: 1180, height: 760);
return 0;

/// <summary>Runs a scripted pass over Core against a temp tree so CI can prove the shipped logic works on Windows.</summary>
static class SelfTest
{
    public static int Run(string resultPath)
    {
        var lines = new List<string>();
        var root = Path.Combine(Path.GetTempPath(), "reactor-winfile-selftest", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var ops = new FileOperations();
            var file = ops.CreateFile(root, "hello.txt");
            File.WriteAllText(file, "self test");
            var dest = ops.CreateDirectory(root, "dest");
            var copy = ops.Copy([file], dest);
            lines.Add($"copy failures: {copy.Failures.Count}");
            var found = new SearchService().Search(new SearchCriteria(root, "hello.txt"));
            lines.Add($"search results: {found.Count}");
            var renamed = ops.Rename(file, "renamed.txt");
            lines.Add($"renamed exists: {File.Exists(renamed)}");
            var pass = copy.Failures.Count == 0 && found.Count == 2 && File.Exists(renamed);
            lines.Add(pass ? "SELF-TEST PASS" : "SELF-TEST FAIL");
            File.WriteAllLines(resultPath, lines);
            return pass ? 0 : 1;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(resultPath, "SELF-TEST FAIL: " + ex.Message); } catch { }
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
