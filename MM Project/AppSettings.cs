using System.Text.Json;

namespace MudProxyViewer;

/// <summary>
/// Application-level settings persisted to settings.json.
/// These are global settings that are not tied to any specific character profile.
/// </summary>
public class AppSettings
{
    private readonly string _settingsFilePath;
    
    public bool AutoLoadLastCharacter { get; set; } = false;
    public string LastCharacterPath { get; set; } = string.Empty;
    public bool DisplaySystemLog { get; set; } = true;
    
    public AppSettings(string appDataPath)
    {
        _settingsFilePath = Path.Combine(appDataPath, "settings.json");
    }
    
    public void Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<ProxySettings>(json);
                if (settings != null)
                {
                    AutoLoadLastCharacter = settings.AutoLoadLastCharacter;
                    LastCharacterPath = settings.LastCharacterPath;
                    DisplaySystemLog = settings.DisplaySystemLog;
                }
            }
        }
        catch
        {
            // Silently use defaults if settings file is corrupt or missing
        }
    }
    
    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var settings = new ProxySettings
            {
                AutoLoadLastCharacter = AutoLoadLastCharacter,
                LastCharacterPath = LastCharacterPath,
                DisplaySystemLog = DisplaySystemLog
            };
            
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Settings save failure is non-critical
        }
    }
}
