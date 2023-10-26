using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CobaltCoreEditor;

public static class FileSystemHelper
{
    public static string MakeValidFileName(string name)
    {
        var invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
        var regexSearch = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
        var r = new Regex($"[{Regex.Escape(regexSearch)}]");
        name = r.Replace(name, "");

        foreach (var c in invalidChars)
        {
            name = name.Replace(c.ToString(), "");
        }

        return name;
    }
    public static string Now() => DateTime.Now.ToString("yyyy-dd-M_HH-mm-ss");
    
    public static void Open(string filepath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = filepath,
            UseShellExecute = true,
        };
        try
        {
            Process.Start(psi);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}