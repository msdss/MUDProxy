using System.Text.Json;

namespace MudProxyViewer;

public class CombatManager
{
    private CombatSettingsDatabase _database = new();
    private readonly string _settingsFilePath;
    private string _currentCharacter = string.Empty;
    
    public event Action<string>? OnLogMessage;
    
    public CombatManager()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MudProxyViewer");
            
        _settingsFilePath = Path.Combine(appDataPath, "combat_settings.json");
        
        LoadSettings();
    }
    
    public string CurrentCharacter
    {
        get => _currentCharacter;
        set
        {
            _currentCharacter = value;
            OnLogMessage?.Invoke($"⚔️ Combat settings loaded for: {value}");
        }
    }
    
    public CombatSettings GetSettings(string characterName)
    {
        var settings = _database.Characters.FirstOrDefault(c => 
            c.CharacterName.Equals(characterName, StringComparison.OrdinalIgnoreCase));
        
        if (settings == null)
        {
            settings = new CombatSettings { CharacterName = characterName };
            _database.Characters.Add(settings);
        }
        
        return settings;
    }
    
    public CombatSettings GetCurrentSettings()
    {
        if (string.IsNullOrEmpty(_currentCharacter))
            return new CombatSettings();
        
        return GetSettings(_currentCharacter);
    }
    
    public void SaveSettings(CombatSettings settings)
    {
        var existing = _database.Characters.FirstOrDefault(c => 
            c.CharacterName.Equals(settings.CharacterName, StringComparison.OrdinalIgnoreCase));
        
        if (existing != null)
        {
            var index = _database.Characters.IndexOf(existing);
            _database.Characters[index] = settings;
        }
        else
        {
            _database.Characters.Add(settings);
        }
        
        SaveToFile();
    }
    
    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var database = JsonSerializer.Deserialize<CombatSettingsDatabase>(json);
                if (database != null)
                {
                    _database = database;
                }
            }
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error loading combat settings: {ex.Message}");
        }
    }
    
    private void SaveToFile()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var json = JsonSerializer.Serialize(_database, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error saving combat settings: {ex.Message}");
        }
    }
    
    public IReadOnlyList<string> GetAllCharacterNames()
    {
        return _database.Characters.Select(c => c.CharacterName).ToList().AsReadOnly();
    }
}
