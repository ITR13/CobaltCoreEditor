namespace CobaltCoreEditor;

public static class ProfileManager
{
    public static string CurrentRootLocation { get; private set; } = "";
    public static bool CurrentRootIsValid { get; private set; }
    public static readonly bool[] ValidProfiles = { false, false, false };

    private static FileSystemWatcher? _watcher;

    static ProfileManager()
    {
        CheckDefaultLocations();
        if (CurrentRootIsValid) SpawnFileSystemWatcher();
    }

    private static void CheckDefaultLocations()
    {
        CurrentRootIsValid = true;
        var lastChosenRootLocation = Settings.LastSelectedRoot;
        if (TestFolder(lastChosenRootLocation))
        {
            CurrentRootLocation = lastChosenRootLocation;
            return;
        }

        var appData = Environment.GetEnvironmentVariable("AppData");
        if (appData == null)
        {
            Console.WriteLine($"Failed to find AppData environment variable");
            return;
        }

        var appFolder = Path.Combine(appData, "CobaltCore");
        if (TestFolder(appFolder))
        {
            CurrentRootLocation = appFolder;
            return;
        }

        var demoFolder = Path.Combine(appData, "CobaltCoreDemo");
        if (TestFolder(demoFolder))
        {
            CurrentRootLocation = demoFolder;
            return;
        }

        Console.WriteLine("Failed to find Cobalt Core root profile folder");
        CurrentRootIsValid = false;
    }

    private static bool TestFolder(string? path)
    {
        if (path == null) return false;
        
        if (!Directory.Exists(path))
        {
            Console.WriteLine($"Folder '{path}' does not exist");
            return false;
        }

        var found = false;
        for (var i = 0; i < 3; i++)
        {
            var subPath = Path.Combine(path, $"Slot{i}", "Save.json");
            if (!File.Exists(subPath)) continue;
            found = true;
            break;
        }

        if (!found)
        {
            Console.WriteLine($"Failed to find Slot[0-2]/Save.json in '{path}'");
            return false;
        }

        Console.WriteLine($"Found valid profile root directory at '{path}'");
        return true;
    }

    public static bool TrySetRootFolder(string text)
    {
        if (!TestFolder(text)) return false;
        CurrentRootLocation = text;
        Settings.LastSelectedRoot = text;
        Settings.Save();

        SpawnFileSystemWatcher();

        return true;
    }

    private static void UpdateValidSaves()
    {
        for (var i = 0; i < 3; i++)
        {
            var subPath = Path.Combine(CurrentRootLocation, $"Slot{i}", "Save.json");
            if (!File.Exists(subPath)) continue;
            ValidProfiles[i] = true;
        }
    }

    private static void SpawnFileSystemWatcher()
    {
        _watcher?.Dispose();

        _watcher = new FileSystemWatcher();
        _watcher.Path = CurrentRootLocation;

        _watcher.IncludeSubdirectories = true;

        _watcher.NotifyFilter = NotifyFilters.DirectoryName;
        _watcher.Created += OnDirectoryChanged;
        _watcher.Deleted += OnDirectoryChanged;
        _watcher.EnableRaisingEvents = true;

        UpdateValidSaves();
    }

    private static void OnDirectoryChanged(object sender, FileSystemEventArgs e)
    {
        UpdateValidSaves();
    }

    public static string GetSlotPath(int slot)
    {
        return Path.Combine(CurrentRootLocation, $"Slot{slot}", "Save.json");
    }
}