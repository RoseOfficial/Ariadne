using Dalamud.Configuration;
using System;
using System.IO;
using Newtonsoft.Json;

namespace Ariadne;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Movement settings
    public bool CancelOnUserInput { get; set; } = true;
    public float WaypointTolerance { get; set; } = 0.5f;
    public float StuckTimeoutSeconds { get; set; } = 3.0f;
    public float StuckTolerance { get; set; } = 0.1f;

    // Auto-navigation settings
    public bool AutoStartInDungeon { get; set; } = false;

    public event Action? Modified;

    public void NotifyModified() => Modified?.Invoke();

    public void Load(FileInfo file)
    {
        if (file.Exists)
        {
            try
            {
                var json = File.ReadAllText(file.FullName);
                var loaded = JsonConvert.DeserializeObject<Configuration>(json);
                if (loaded != null)
                {
                    Version = loaded.Version;
                    CancelOnUserInput = loaded.CancelOnUserInput;
                    WaypointTolerance = loaded.WaypointTolerance;
                    StuckTimeoutSeconds = loaded.StuckTimeoutSeconds;
                    StuckTolerance = loaded.StuckTolerance;
                    AutoStartInDungeon = loaded.AutoStartInDungeon;
                }
            }
            catch (Exception ex)
            {
                Services.Log.Error($"Failed to load config: {ex}");
            }
        }
    }

    public void Save(FileInfo file)
    {
        try
        {
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(file.FullName, json);
        }
        catch (Exception ex)
        {
            Services.Log.Error($"Failed to save config: {ex}");
        }
    }
}
