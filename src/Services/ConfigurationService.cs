using System;
using System.IO;
using Aethermeter.Models;
using Newtonsoft.Json;

namespace Aethermeter.Services;

/// <summary>Loads and saves the configuration as JSON in the plugin's config folder.</summary>
public sealed class ConfigurationService : IDisposable
{
    private readonly string configFilePath;
    private Configuration? configuration;

    /// <summary>True when no configuration file existed on disk, i.e. this is a first install.</summary>
    public bool IsNewConfiguration { get; private set; }

    public Configuration Configuration => configuration ??= Load();

    public string Directory { get; }

    public ConfigurationService()
    {
        Directory = Plugin.PluginInterface.ConfigDirectory.FullName;
        configFilePath = Path.Combine(Directory, "config.json");
        System.IO.Directory.CreateDirectory(Directory);
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(configFilePath, JsonConvert.SerializeObject(Configuration, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to save configuration to {ConfigPath}", configFilePath);
        }
    }

    private Configuration Load()
    {
        try
        {
            if (File.Exists(configFilePath))
            {
                // Replace, don't merge: otherwise the default window would be added to the saved ones.
                var settings = new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace };
                var config = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(configFilePath), settings);
                if (config != null)
                {
                    config.Windows ??= new();
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to load configuration from {ConfigPath}", configFilePath);
        }

        IsNewConfiguration = true;
        return new Configuration();
    }

    public void Dispose() => Save();
}
