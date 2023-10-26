using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Win32;
using VdfParser;

namespace CobaltCoreEditor;

public static class DataManager
{
    private const string SteamInstallSubKey64 = @"SOFTWARE\WOW6432Node\Valve\Steam";
    private const string SteamInstallSubKey32 = @"SOFTWARE\Valve\Steam";
    private const string SteamInstallKeyName = "InstallPath";
    private const string ModFolderName = "ShipMods";

    private const string SampleShipsFileName = "SampleShips.zip";

    public static string CurrentRootLocation { get; private set; } = "";
    public static bool CurrentRootIsValid { get; private set; }
    public static string ModFolderPath { get; private set; }

    public static ShipMetaData[] Ships { get; private set; }
    public static string[] ShipJsons { get; private set; }

    private static FileSystemWatcher? _watcher;
    private static bool _dirty;

    static DataManager()
    {
        CheckDefaultLocations();
    }

    private static void CheckDefaultLocations()
    {
        CurrentRootIsValid = true;
        var lastChosenRootLocation = Settings.LastSelectedGameRoot;
        if (TestFolder(lastChosenRootLocation))
        {
            InternalSetRootFolder(lastChosenRootLocation);
            return;
        }

        if (OperatingSystem.IsWindows() && CheckWindowsRegistry())
        {
            return;
        }

        Console.WriteLine("Failed to find Cobalt Core root game folder");
        CurrentRootIsValid = false;
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckWindowsRegistry()
    {
        var installLocations = new List<string>(2);
        foreach (var subkey in new[] { SteamInstallSubKey32, SteamInstallSubKey64 })
        {
            using var key = Registry.LocalMachine.OpenSubKey(subkey);
            var value = key?.GetValue(SteamInstallKeyName, null);
            if (value is string sValue)
            {
                installLocations.Add(sValue);
            }
        }

        if (installLocations.Count == 0)
        {
            Console.WriteLine("Failed to find steam install paths");
            return false;
        }

        var libraryLocations = new List<string>();
        foreach (var installLocation in installLocations)
        {
            var libraryVdfPath = Path.Combine(installLocation, "steamapps", "libraryfolders.vdf");
            using var libraryVdfFile = File.OpenRead(libraryVdfPath);
            var deserializer = new VdfDeserializer();
            if (deserializer.Deserialize(libraryVdfFile) is not IDictionary<string, dynamic> result)
            {
                Console.WriteLine($"Failed to deserialize vdf file at '{libraryVdfPath}'");
                continue;
            }

            if (result["libraryfolders"] is not IDictionary<string, dynamic> libraryFoldersVdfEntry)
            {
                Console.WriteLine($"No libraryfolders in vdf file at '{libraryVdfPath}'");
                continue;
            }

            foreach (var folderDynamic in libraryFoldersVdfEntry.Values)
            {
                if (folderDynamic is not IDictionary<string, dynamic> folderDict)
                {
                    Console.WriteLine($"LibraryFolders is not a list of dict at '{libraryVdfPath}'");
                    continue;
                }

                if (folderDict["path"] is not string path)
                {
                    Console.WriteLine($"Path is not a string in '{libraryVdfPath}'");
                    continue;
                }

                libraryLocations.Add(path);
            }
        }

        if (libraryLocations.Count == 0)
        {
            Console.WriteLine($"Found {installLocations.Count} vdf files, but none contained any steam library paths");
            return false;
        }

        foreach (var libraryPath in libraryLocations)
        {
            var cobaltCoreFolder = Path.Combine(libraryPath, "steamapps", "common", "Cobalt Core");
            if (TestFolder(cobaltCoreFolder))
            {
                InternalSetRootFolder(cobaltCoreFolder);
                CurrentRootIsValid = true;
                return true;
            }

            var cobaltCoreDemoFolder = Path.Combine(libraryPath, "steamapps", "common", "Cobalt Core Demo");

            if (!TestFolder(cobaltCoreDemoFolder)) continue;
            InternalSetRootFolder(cobaltCoreDemoFolder);
            CurrentRootIsValid = true;
            return true;
        }

        Console.WriteLine("None of the steam library folders contain cobalt core");
        return false;
    }

    private static bool TestFolder(string? path)
    {
        if (path == null) return false;

        if (!Directory.Exists(path))
        {
            Console.WriteLine($"Folder '{path}' does not exist");
            return false;
        }

        var dataPath = Path.Combine(path, "Data");
        if (!Directory.Exists(dataPath))
        {
            Console.WriteLine($"Failed to find Data folder in '{path}'");
            return false;
        }

        var exePath = Path.Combine(path, "CobaltCore.exe");
        if (!File.Exists(exePath))
        {
            Console.WriteLine($"Failed to find CobaltCore.exe in '{path}'");
            return false;
        }

        Console.WriteLine($"Found valid data root directory at '{path}'");
        return true;
    }

    public static bool TrySetRootFolder(string path)
    {
        if (!TestFolder(path)) return false;
        Settings.LastSelectedGameRoot = path;
        Settings.Save();
        InternalSetRootFolder(path);
        return true;
    }

    private static void InternalSetRootFolder(string path)
    {
        CurrentRootLocation = path;
        ModFolderPath = Path.Combine(path, ModFolderName);
        if (!Directory.Exists(ModFolderPath))
        {
            Directory.CreateDirectory(ModFolderPath);
            ExportSampleShips(ModFolderPath);
        }

        SetupWatcher();
        _dirty = true;
    }

    private static void SetupWatcher()
    {
        _watcher?.Dispose();

        _watcher = new FileSystemWatcher();
        _watcher.Path = ModFolderPath;

        _watcher.IncludeSubdirectories = true;

        _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.EnableRaisingEvents = true;

        Ships = Array.Empty<ShipMetaData>();
        ShipJsons = Array.Empty<string>();
    }

    private static void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        _dirty = true;
    }

    public static void MaybeImport()
    {
        if (!_dirty) return;
        _dirty = false;
        (Ships, ShipJsons) = ImportExport.ReadAll(ModFolderPath);
    }

    private static void ExportSampleShips(string folderPath)
    {
        var currentAssembly = Assembly.GetExecutingAssembly();

        using var resourceStream = currentAssembly.GetManifestResourceStream("CobaltCoreEditor." + SampleShipsFileName);
        if (resourceStream == null)
        {
            Console.WriteLine("Failed to find sample ships in assembly.");
            return;
        }

        var path = Path.Join(folderPath, SampleShipsFileName);
        using var fileStream = File.Create(path);
        resourceStream.CopyTo(fileStream);
    }
}