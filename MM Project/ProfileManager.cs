using System.Text.Json;

namespace MudProxyViewer;

/// <summary>
/// Manages character profile file I/O, app-level settings persistence,
/// and profile path/state tracking.
/// Extracted from BuffManager for better separation of concerns.
/// 
/// Design: ProfileManager handles serialization and file operations only.
/// BuffManager remains the coordinator that assembles CharacterProfile DTOs
/// for saving and distributes loaded data to sub-managers.
/// </summary>
public class ProfileManager
{
    private readonly string _settingsFilePath;
    private readonly string _characterProfilesPath;
    private string _currentProfilePath = string.Empty;
    
    // App-level settings (not per-character)
    private bool _autoLoadLastCharacter = false;
    private string _lastCharacterPath = string.Empty;
    private bool _displaySystemLog = true;
    
    // Dependencies
    private readonly Action<string> _logMessage;
    
    public ProfileManager(string appDataPath, Action<string> logMessage)
    {
        _settingsFilePath = Path.Combine(appDataPath, "settings.json");
        _characterProfilesPath = Path.Combine(appDataPath, "Characters");
        _logMessage = logMessage;
        
        if (!Directory.Exists(_characterProfilesPath))
        {
            Directory.CreateDirectory(_characterProfilesPath);
        }
        
        LoadSettings();
    }
    
    #region Properties
    
    public string CharacterProfilesPath => _characterProfilesPath;
    public string CurrentProfilePath => _currentProfilePath;
    public bool HasUnsavedChanges { get; set; } = false;
    
    public bool AutoLoadLastCharacter
    {
        get => _autoLoadLastCharacter;
        set { _autoLoadLastCharacter = value; SaveSettings(); }
    }
    
    public string LastCharacterPath
    {
        get => _lastCharacterPath;
        set { _lastCharacterPath = value; SaveSettings(); }
    }
    
    public bool DisplaySystemLog
    {
        get => _displaySystemLog;
        set { _displaySystemLog = value; SaveSettings(); }
    }
    
    #endregion
    
    #region Profile File Operations
    
    /// <summary>
    /// Generate a safe default filename based on the player's name.
    /// </summary>
    public string GetDefaultProfileFilename(string playerName)
    {
        if (!string.IsNullOrEmpty(playerName))
        {
            var safeName = string.Join("_", playerName.Split(Path.GetInvalidFileNameChars()));
            return $"{safeName}.json";
        }
        return "character.json";
    }
    
    /// <summary>
    /// Save a CharacterProfile DTO to disk.
    /// The caller (BuffManager) is responsible for assembling the profile from all managers.
    /// </summary>
    public (bool success, string message) SaveProfile(CharacterProfile profile, string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
            
            _currentProfilePath = filePath;
            HasUnsavedChanges = false;
            
            _logMessage($"💾 Character profile saved: {Path.GetFileName(filePath)}");
            return (true, "Character profile saved successfully.");
        }
        catch (Exception ex)
        {
            _logMessage($"Error saving character profile: {ex.Message}");
            return (false, $"Error saving character profile: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Load a CharacterProfile DTO from disk.
    /// The caller (BuffManager) is responsible for distributing the loaded data to sub-managers.
    /// </summary>
    public (bool success, string message, CharacterProfile? profile) LoadProfile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return (false, "Character profile file not found.", null);
            
            var json = File.ReadAllText(filePath);
            var profile = JsonSerializer.Deserialize<CharacterProfile>(json);
            
            if (profile == null)
                return (false, "Invalid character profile format.", null);
            
            _currentProfilePath = filePath;
            HasUnsavedChanges = false;
            _lastCharacterPath = filePath;
            SaveSettings();
            
            _logMessage($"📂 Character profile loaded: {Path.GetFileName(filePath)}");
            return (true, $"Character profile '{profile.CharacterName}' loaded successfully.", profile);
        }
        catch (Exception ex)
        {
            _logMessage($"Error loading character profile: {ex.Message}");
            return (false, $"Error loading character profile: {ex.Message}", null);
        }
    }
    
    /// <summary>
    /// Auto-save the current profile if a path is set.
    /// Returns false if no profile is loaded (nothing to save).
    /// The caller provides the assembled CharacterProfile DTO.
    /// </summary>
    public (bool attempted, bool success, string message) AutoSave(CharacterProfile profile)
    {
        if (string.IsNullOrEmpty(_currentProfilePath))
            return (false, false, "No profile loaded.");
        
        var (success, message) = SaveProfile(profile, _currentProfilePath);
        return (true, success, message);
    }
    
    /// <summary>
    /// Reset profile state for a new character. Does not reset app settings.
    /// </summary>
    public void ResetForNewProfile()
    {
        _currentProfilePath = string.Empty;
        HasUnsavedChanges = false;
    }
    
    #endregion
    
    #region App Settings Persistence
    
    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<ProxySettings>(json);
                if (settings != null)
                {
                    _autoLoadLastCharacter = settings.AutoLoadLastCharacter;
                    _lastCharacterPath = settings.LastCharacterPath;
                    _displaySystemLog = settings.DisplaySystemLog;
                }
            }
        }
        catch (Exception ex)
        {
            _logMessage($"Error loading settings: {ex.Message}");
        }
    }
    
    private void SaveSettings()
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
                AutoLoadLastCharacter = _autoLoadLastCharacter,
                LastCharacterPath = _lastCharacterPath,
                DisplaySystemLog = _displaySystemLog
            };
            
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            _logMessage($"Error saving settings: {ex.Message}");
        }
    }
    
    #endregion
}
