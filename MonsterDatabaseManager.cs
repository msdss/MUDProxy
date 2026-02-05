using System.Text.Json;

namespace MudProxyViewer;

/// <summary>
/// Manages monster data from CSV and per-character overrides.
/// 
/// CSV Path (monster_settings.json) - GLOBAL: The path to the monster CSV file is shared across all characters
/// Monster Overrides - PER-CHARACTER: Stored in CharacterProfile, loaded via LoadOverridesFromProfile()
/// </summary>
public class MonsterDatabaseManager
{
    private List<MonsterData> _monsters = new();
    private readonly List<MonsterOverride> _overrides = new();
    private readonly string _settingsFilePath;
    private string _csvFilePath = string.Empty;
    
    public event Action? OnDatabaseLoaded;
    public event Action<string>? OnLogMessage;
    public event Action? OnDataChanged;  // Fires when overrides change that should trigger a profile save
    
    public MonsterDatabaseManager()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MudProxyViewer");
            
        _settingsFilePath = Path.Combine(appDataPath, "monster_settings.json");
        
        LoadSettings();
        
        // Auto-load if path is set
        if (!string.IsNullOrEmpty(_csvFilePath) && File.Exists(_csvFilePath))
        {
            LoadFromCsv(_csvFilePath);
        }
    }
    
    public IReadOnlyList<MonsterData> Monsters => _monsters.AsReadOnly();
    public int MonsterCount => _monsters.Count;
    public string CsvFilePath => _csvFilePath;
    public bool IsLoaded => _monsters.Count > 0;
    
    #region Settings (CSV Path - Global)
    
    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<MonsterDbSettings>(json);
                if (settings != null)
                {
                    _csvFilePath = settings.CsvFilePath ?? string.Empty;
                }
            }
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error loading monster DB settings: {ex.Message}");
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
            
            var settings = new MonsterDbSettings { CsvFilePath = _csvFilePath };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error saving monster DB settings: {ex.Message}");
        }
    }
    
    #endregion
    
    #region Overrides (Per-Character Profile)
    
    /// <summary>
    /// Load overrides from a character profile
    /// </summary>
    public void LoadOverridesFromProfile(List<MonsterOverride>? overrides)
    {
        _overrides.Clear();
        if (overrides != null)
        {
            _overrides.AddRange(overrides);
        }
        OnLogMessage?.Invoke($"📂 Loaded {_overrides.Count} monster override(s) from profile");
    }
    
    /// <summary>
    /// Get all monster overrides for saving to character profile
    /// </summary>
    public List<MonsterOverride> GetOverridesForProfile()
    {
        return _overrides.ToList();
    }
    
    /// <summary>
    /// Get all overrides (read-only)
    /// </summary>
    public IReadOnlyList<MonsterOverride> GetAllOverrides()
    {
        return _overrides.AsReadOnly();
    }
    
    /// <summary>
    /// Clear all overrides (when no character is loaded)
    /// </summary>
    public void ClearOverrides()
    {
        _overrides.Clear();
    }
    
    /// <summary>
    /// Legacy method - calls LoadOverridesFromProfile
    /// </summary>
    [Obsolete("Use LoadOverridesFromProfile() instead")]
    public void ReplaceOverrides(List<MonsterOverride> overrides)
    {
        LoadOverridesFromProfile(overrides);
    }
    
    public MonsterOverride GetOverride(int monsterNumber)
    {
        var existing = _overrides.FirstOrDefault(o => o.MonsterNumber == monsterNumber);
        if (existing != null)
            return existing;
        
        // Return default override
        return new MonsterOverride { MonsterNumber = monsterNumber };
    }
    
    public void SaveOverride(MonsterOverride monsterOverride)
    {
        var existing = _overrides.FirstOrDefault(o => o.MonsterNumber == monsterOverride.MonsterNumber);
        if (existing != null)
        {
            var index = _overrides.IndexOf(existing);
            _overrides[index] = monsterOverride;
        }
        else
        {
            _overrides.Add(monsterOverride);
        }
        
        OnDataChanged?.Invoke();  // Trigger profile save
    }
    
    public int GetNextMonsterId()
    {
        int maxId = 0;
        if (_monsters.Count > 0)
            maxId = _monsters.Max(m => m.Number);
        
        // Also check overrides for custom monsters
        var customMaxId = _overrides
            .Where(o => o.MonsterNumber > 0)
            .Select(o => o.MonsterNumber)
            .DefaultIfEmpty(0)
            .Max();
        
        return Math.Max(maxId, customMaxId) + 1;
    }
    
    public MonsterData AddCustomMonster(string name, MonsterOverride monsterOverride)
    {
        var newId = GetNextMonsterId();
        
        var monster = new MonsterData
        {
            Number = newId,
            Name = name
        };
        
        _monsters.Add(monster);
        
        // Update override with the new ID
        monsterOverride.MonsterNumber = newId;
        monsterOverride.CustomName = name;
        SaveOverride(monsterOverride);
        
        OnDatabaseLoaded?.Invoke();
        return monster;
    }
    
    #endregion
    
    #region CSV Loading
    
    public bool LoadFromCsv(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                OnLogMessage?.Invoke($"Monster CSV not found: {filePath}");
                return false;
            }
            
            var lines = File.ReadAllLines(filePath);
            if (lines.Length < 2)
            {
                OnLogMessage?.Invoke("Monster CSV is empty or has no data rows");
                return false;
            }
            
            // Parse header to get column indices
            var headers = ParseCsvLine(lines[0]);
            var columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
            {
                columnMap[headers[i]] = i;
            }
            
            // Verify required columns exist
            var requiredColumns = new[] { "Number", "Name", "HP", "EXP" };
            foreach (var col in requiredColumns)
            {
                if (!columnMap.ContainsKey(col))
                {
                    OnLogMessage?.Invoke($"Monster CSV missing required column: {col}");
                    return false;
                }
            }
            
            _monsters.Clear();
            
            // Parse data rows
            for (int i = 1; i < lines.Length; i++)
            {
                try
                {
                    var fields = ParseCsvLine(lines[i]);
                    if (fields.Length < 10) continue;
                    
                    var monster = new MonsterData
                    {
                        Number = GetInt(fields, columnMap, "Number"),
                        Name = GetString(fields, columnMap, "Name"),
                        ArmourClass = GetInt(fields, columnMap, "ArmourClass"),
                        DamageResist = GetInt(fields, columnMap, "DamageResist"),
                        MagicRes = GetInt(fields, columnMap, "MagicRes"),
                        BSDefense = GetInt(fields, columnMap, "BSDefense"),
                        EXP = GetInt(fields, columnMap, "EXP"),
                        HP = GetInt(fields, columnMap, "HP"),
                        AvgDmg = GetDouble(fields, columnMap, "AvgDmg"),
                        HPRegen = GetInt(fields, columnMap, "HPRegen"),
                        Type = GetInt(fields, columnMap, "Type"),
                        Undead = GetInt(fields, columnMap, "Undead") == 1,
                        Align = GetInt(fields, columnMap, "Align"),
                        InGame = GetInt(fields, columnMap, "In Game") == 1,
                        Energy = GetInt(fields, columnMap, "Energy"),
                        CharmLVL = GetInt(fields, columnMap, "CharmLVL"),
                        Weapon = GetInt(fields, columnMap, "Weapon"),
                        FollowPercent = GetInt(fields, columnMap, "Follow%")
                    };
                    
                    // Parse attacks (up to 5)
                    for (int a = 0; a < 5; a++)
                    {
                        var attackName = GetString(fields, columnMap, $"AttName-{a}");
                        if (!string.IsNullOrEmpty(attackName) && attackName != "None")
                        {
                            monster.Attacks.Add(new MonsterAttack
                            {
                                Name = attackName,
                                Min = GetInt(fields, columnMap, $"AttMin-{a}"),
                                Max = GetInt(fields, columnMap, $"AttMax-{a}"),
                                Accuracy = GetInt(fields, columnMap, $"AttAcc-{a}")
                            });
                        }
                    }
                    
                    // Parse drops (up to 10)
                    for (int d = 0; d < 10; d++)
                    {
                        var itemId = GetInt(fields, columnMap, $"DropItem-{d}");
                        var dropPercent = GetInt(fields, columnMap, $"DropItem%-{d}");
                        if (itemId > 0)
                        {
                            monster.Drops.Add(new MonsterDrop
                            {
                                ItemId = itemId,
                                DropPercent = dropPercent
                            });
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(monster.Name))
                    {
                        _monsters.Add(monster);
                        
                        // Auto-set relationship based on alignment (if no override exists yet)
                        // Align: 1=Evil, 2=Chaotic Evil, 5=Neutral Evil, 6=Lawful Evil -> Enemy
                        // Align: 0=Good, 4=Lawful Good -> Friend
                        var existingOverride = _overrides.FirstOrDefault(o => o.MonsterNumber == monster.Number);
                        if (existingOverride == null)
                        {
                            if (monster.Align == 1 || monster.Align == 2 || monster.Align == 5 || monster.Align == 6)
                            {
                                _overrides.Add(new MonsterOverride
                                {
                                    MonsterNumber = monster.Number,
                                    Relationship = MonsterRelationship.Enemy
                                });
                            }
                            else if (monster.Align == 0 || monster.Align == 4)
                            {
                                _overrides.Add(new MonsterOverride
                                {
                                    MonsterNumber = monster.Number,
                                    Relationship = MonsterRelationship.Friend
                                });
                            }
                        }
                    }
                }
                catch
                {
                    // Skip malformed rows
                }
            }
            
            _csvFilePath = filePath;
            SaveSettings();  // Save CSV path (global setting)
            // Note: Overrides are NOT saved here - they're saved with character profile
            
            OnLogMessage?.Invoke($"📊 Loaded {_monsters.Count} monsters from database");
            OnDatabaseLoaded?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error loading monster CSV: {ex.Message}");
            return false;
        }
    }
    
    private string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();
        
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString());
        
        return result.ToArray();
    }
    
    private string GetString(string[] fields, Dictionary<string, int> columnMap, string column)
    {
        if (columnMap.TryGetValue(column, out int index) && index < fields.Length)
        {
            return fields[index].Trim();
        }
        return string.Empty;
    }
    
    private int GetInt(string[] fields, Dictionary<string, int> columnMap, string column)
    {
        var str = GetString(fields, columnMap, column);
        return int.TryParse(str, out int val) ? val : 0;
    }
    
    private double GetDouble(string[] fields, Dictionary<string, int> columnMap, string column)
    {
        var str = GetString(fields, columnMap, column);
        return double.TryParse(str, out double val) ? val : 0;
    }
    
    #endregion
    
    #region Search
    
    public IEnumerable<MonsterData> SearchMonsters(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return _monsters;
        
        return _monsters.Where(m =>
            m.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
            m.Number.ToString() == searchTerm);
    }
    
    public MonsterData? GetMonsterByName(string name)
    {
        return _monsters.FirstOrDefault(m =>
            m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
    
    public MonsterData? GetMonsterByNumber(int number)
    {
        return _monsters.FirstOrDefault(m => m.Number == number);
    }
    
    /// <summary>
    /// Find a monster whose name is contained within the given text.
    /// Used for matching monsters with flavor text prefixes like "fat cave worm" -> "cave worm"
    /// </summary>
    public MonsterData? FindMonsterByPartialName(string text)
    {
        // First try exact match
        var exact = GetMonsterByName(text);
        if (exact != null)
            return exact;
        
        // Then look for monsters whose name is contained in the text
        // Sort by name length descending to prefer longer/more specific matches
        return _monsters
            .Where(m => text.Contains(m.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.Name.Length)
            .FirstOrDefault();
    }
    
    #endregion
}

public class MonsterDbSettings
{
    public string? CsvFilePath { get; set; }
}
