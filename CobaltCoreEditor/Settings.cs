using System.Configuration;
using Configuration = System.Configuration.Configuration;

namespace CobaltCoreEditor;

public static class Settings {
    private static SettingsSection Instance;
    private static Configuration Config;

    public static string LastSelectedRoot 
    {
        get => Instance.LastSelectedRoot;
        set => Instance.LastSelectedRoot = value;
    }
    public static int LastSelectedProfile 
    {
        get => Instance.LastSelectedProfile;
        set => Instance.LastSelectedProfile = value;
    }
    public static string LastSelectedGameRoot 
    {
        get => Instance.LastSelectedGameRoot;
        set => Instance.LastSelectedGameRoot = value;
    }
    public static string AuthorName 
    {
        get => Instance.AuthorName;
        set => Instance.AuthorName = value;
    }
    static Settings()
    {
        Config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoaming);
        var configFileMap =
            new ExeConfigurationFileMap
            {
                ExeConfigFilename = Config.FilePath
            };
        Config = ConfigurationManager.OpenMappedExeConfiguration(configFileMap, ConfigurationUserLevel.None);

        Console.WriteLine($"Loaded Settings from '{Config.FilePath}'");

        if (Config.Sections["Settings"] == null)
        {
            Instance = new SettingsSection();
            Config.Sections.Add("Settings", Instance);
        }
        else
        {
            Instance = (SettingsSection)Config.Sections["Settings"];
        }
        Instance.SectionInformation.ForceSave = true;
    }

    public static void Save()
    {
        Config.Save(ConfigurationSaveMode.Full);
    }
}

public sealed class SettingsSection : ConfigurationSection
{

    [ConfigurationProperty(nameof(LastSelectedRoot),
        DefaultValue = "",
        IsRequired = false,
        IsKey = true)]
    public string LastSelectedRoot
    {
        get => (string)this[nameof(LastSelectedRoot)];
        set => this[nameof(LastSelectedRoot)] = value;
    }

    [ConfigurationProperty(nameof(LastSelectedProfile),
        DefaultValue = -1,
        IsRequired = false,
        IsKey = true)]
    public int LastSelectedProfile
    {
        get => (int)this[nameof(LastSelectedProfile)];
        set => this[nameof(LastSelectedProfile)] = value;
    }

    [ConfigurationProperty(nameof(LastSelectedGameRoot),
        DefaultValue = "",
        IsRequired = false,
        IsKey = true)]
    public string LastSelectedGameRoot
    {
        get => (string)this[nameof(LastSelectedGameRoot)];
        set => this[nameof(LastSelectedGameRoot)] = value;
    }

    [ConfigurationProperty(nameof(AuthorName),
        DefaultValue = "",
        IsRequired = false,
        IsKey = true)]
    public string AuthorName
    {
        get => (string)this[nameof(AuthorName)];
        set => this[nameof(AuthorName)] = value;
    }
}