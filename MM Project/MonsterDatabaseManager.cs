using System.Text.Json;

namespace MudProxyViewer;

/// <summary>
/// Manages monster data from imported Game Data (Monsters.json) and per-character overrides.
/// 
/// Monster Data - Loaded from Game Data JSON (imported via MDB importer)
/// Monster Overrides - PER-CHARACTER: Stored in CharacterProfile, loaded via LoadOverridesFromProfile()
/// </summary>
public class MonsterDatabaseManager
{
    private List<MonsterData> _monsters = new();
    private readonly List<MonsterOverride> _overrides = new();
    private readonly string _gameDataPath;
    
    public event Action? OnDatabaseLoaded;
    public event Action<string>? OnLogMessage;
    public event Action? OnDataChanged;  // Fires when overrides change that should trigger a profile save
    
    public MonsterDatabaseManager()
    {
        _gameDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MudProxyViewer",
            "Game Data");
        
        // Auto-load from Game Data if available
        LoadFromGameData();
    }
    
    public IReadOnlyList<MonsterData> Monsters => _monsters.AsReadOnly();
    public int MonsterCount => _monsters.Count;
    public bool IsLoaded => _monsters.Count > 0;
    
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
    
    #region Game Data Loading
    
    /// <summary>
    /// Load monsters from imported Game Data JSON (Monsters.json).
    /// Tries GameDataCache first, falls back to reading the file directly.
    /// </summary>
    public bool LoadFromGameData()
    {
        try
        {
            List<Dictionary<string, object?>>? monsterRows = null;
            
            // Try cache first
            monsterRows = GameDataCache.Instance.GetTable("Monsters");
            
            // Fall back to reading file directly
            if (monsterRows == null)
            {
                var filePath = Path.Combine(_gameDataPath, "Monsters.json");
                if (!File.Exists(filePath))
                {
                    // No data available yet - not an error, just not imported
                    return false;
                }
                
                var json = File.ReadAllText(filePath);
                monsterRows = ParseJsonArray(json);
            }
            
            if (monsterRows == null || monsterRows.Count == 0)
                return false;
            
            _monsters.Clear();
            
            foreach (var row in monsterRows)
            {
                try
                {
                    var monster = new MonsterData
                    {
                        Number = GetInt(row, "Number"),
                        Name = GetString(row, "Name"),
                        ArmourClass = GetInt(row, "ArmourClass"),
                        DamageResist = GetInt(row, "DamageResist"),
                        MagicRes = GetInt(row, "MagicRes"),
                        BSDefense = GetInt(row, "BSDefense"),
                        EXP = GetInt(row, "EXP"),
                        HP = GetInt(row, "HP"),
                        AvgDmg = GetDouble(row, "AvgDmg"),
                        HPRegen = GetInt(row, "HPRegen"),
                        Type = GetInt(row, "Type"),
                        Undead = GetInt(row, "Undead") == 1,
                        Align = GetInt(row, "Align"),
                        InGame = GetInt(row, "In Game") == 1,
                        Energy = GetInt(row, "Energy"),
                        CharmLVL = GetInt(row, "CharmLVL"),
                        Weapon = GetInt(row, "Weapon"),
                        FollowPercent = GetInt(row, "Follow%")
                    };
                    
                    // Parse attacks (up to 5)
                    for (int a = 0; a < 5; a++)
                    {
                        var attackName = GetString(row, $"AttName-{a}");
                        if (!string.IsNullOrEmpty(attackName) && attackName != "None")
                        {
                            monster.Attacks.Add(new MonsterAttack
                            {
                                Name = attackName,
                                Min = GetInt(row, $"AttMin-{a}"),
                                Max = GetInt(row, $"AttMax-{a}"),
                                Accuracy = GetInt(row, $"AttAcc-{a}")
                            });
                        }
                    }
                    
                    // Parse drops (up to 10)
                    for (int d = 0; d < 10; d++)
                    {
                        var itemId = GetInt(row, $"DropItem-{d}");
                        var dropPercent = GetInt(row, $"DropItem%-{d}");
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
            
            OnLogMessage?.Invoke($"📊 Loaded {_monsters.Count} monsters from game data");
            OnDatabaseLoaded?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error loading monster game data: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Reload monster data (e.g. after a new MDB import)
    /// </summary>
    public void Reload()
    {
        LoadFromGameData();
    }
    
    private static List<Dictionary<string, object?>>? ParseJsonArray(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        
        if (root.ValueKind != JsonValueKind.Array)
            return null;
        
        var result = new List<Dictionary<string, object?>>();
        
        foreach (var row in root.EnumerateArray())
        {
            var dict = new Dictionary<string, object?>();
            foreach (var prop in row.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDecimal(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => prop.Value.ToString()
                };
            }
            result.Add(dict);
        }
        
        return result;
    }
    
    private static string GetString(Dictionary<string, object?> row, string key)
    {
        if (row.TryGetValue(key, out var val) && val != null)
            return val.ToString()?.Trim() ?? string.Empty;
        return string.Empty;
    }
    
    private static int GetInt(Dictionary<string, object?> row, string key)
    {
        if (row.TryGetValue(key, out var val) && val != null)
        {
            if (val is long l) return (int)l;
            if (val is int i) return i;
            if (val is decimal d) return (int)d;
            if (int.TryParse(val.ToString(), out int parsed)) return parsed;
        }
        return 0;
    }
    
    private static double GetDouble(Dictionary<string, object?> row, string key)
    {
        if (row.TryGetValue(key, out var val) && val != null)
        {
            if (val is double d) return d;
            if (val is decimal dec) return (double)dec;
            if (val is long l) return l;
            if (double.TryParse(val.ToString(), out double parsed)) return parsed;
        }
        return 0;
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
