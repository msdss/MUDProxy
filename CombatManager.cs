using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MudProxyViewer;

public class CombatManager
{
    private CombatSettingsDatabase _database = new();
    private readonly string _settingsFilePath;
    private string _currentCharacter = string.Empty;
    
    // Dependencies injected via Initialize
    private PlayerDatabaseManager? _playerDatabase;
    private MonsterDatabaseManager? _monsterDatabase;
    private Func<bool>? _isInCombat;
    private Func<int>? _getCurrentManaPercent;
    
    // Combat state
    private bool _combatEnabled = false;
    private List<string> _currentRoomEnemies = new();
    private bool _attackPending = false;
    private DateTime _attackPendingSince = DateTime.MinValue;
    private const int ATTACK_PENDING_TIMEOUT_MS = 5000; // Clear pending flag after 5 seconds
    private string _lastProcessedAlsoHere = string.Empty; // Prevent duplicate processing
    
    // Attack spell tracking
    private string _currentTarget = string.Empty;
    private int _attackSpellCastCount = 0;
    private bool _usedMeleeThisRound = false; // Track if we fell back to melee due to low mana
    
    // Room parsing - buffer for multi-chunk "Also here:" lines
    // MUD output can split long lines across multiple TCP chunks
    private StringBuilder _alsoHereBuffer = new();
    private bool _capturingAlsoHere = false;
    
    // Regex to match complete "Also here:" with content ending in period
    // Uses Singleline mode so . matches newlines (for wrapped lines)
    private static readonly Regex AlsoHereRegex = new(@"Also here:\s*(.+?)\.", RegexOptions.Compiled | RegexOptions.Singleline);
    
    public event Action<string>? OnLogMessage;
    public event Action<string>? OnSendCommand;
    public event Action? OnCombatEnabledChanged; // Event for BuffManager to save settings
    
    public CombatManager()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MudProxyViewer");
            
        _settingsFilePath = Path.Combine(appDataPath, "combat_settings.json");
        
        LoadSettings();
    }
    
    /// <summary>
    /// Initialize with required dependencies
    /// </summary>
    public void Initialize(
        PlayerDatabaseManager playerDatabase, 
        MonsterDatabaseManager monsterDatabase,
        Func<bool> isInCombat,
        Func<int> getCurrentManaPercent)
    {
        _playerDatabase = playerDatabase;
        _monsterDatabase = monsterDatabase;
        _isInCombat = isInCombat;
        _getCurrentManaPercent = getCurrentManaPercent;
    }
    
    public bool CombatEnabled
    {
        get => _combatEnabled;
        set
        {
            if (_combatEnabled != value)
            {
                _combatEnabled = value;
                OnCombatEnabledChanged?.Invoke(); // Notify BuffManager to save settings
                OnLogMessage?.Invoke(value ? "⚔️ Auto-combat ENABLED" : "🛡️ Auto-combat DISABLED");
                
                if (!value)
                {
                    // Clear state when disabled
                    _currentRoomEnemies.Clear();
                    _attackPending = false;
                    _currentTarget = string.Empty;
                    _attackSpellCastCount = 0;
                    _usedMeleeThisRound = false;
                }
            }
        }
    }
    
    /// <summary>
    /// Set combat enabled without triggering the save event (used during load)
    /// </summary>
    public void SetCombatEnabledFromSettings(bool enabled)
    {
        _combatEnabled = enabled;
    }
    
    public string CurrentCharacter
    {
        get => _currentCharacter;
        set
        {
            var wasEmpty = string.IsNullOrEmpty(_currentCharacter);
            _currentCharacter = value;
            OnLogMessage?.Invoke($"⚔️ Combat settings loaded for: {value}");
            
            // If character was just set (wasn't set before) and we have enemies in the room,
            // try to initiate combat now
            if (wasEmpty && !string.IsNullOrEmpty(value))
            {
                if (_currentRoomEnemies.Count > 0)
                {
                    OnLogMessage?.Invoke($"🔄 Character now known, attacking {_currentRoomEnemies.Count} enemies...");
                    TryInitiateCombat();
                }
                else
                {
                    // Clear the duplicate check so the next room content will be processed
                    _lastProcessedAlsoHere = string.Empty;
                    OnLogMessage?.Invoke($"🔄 Character now known, waiting for room content...");
                }
            }
        }
    }
    
    public IReadOnlyList<string> CurrentRoomEnemies => _currentRoomEnemies.AsReadOnly();
    
    #region Settings Management
    
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
    
    #endregion
    
    #region Message Processing
    
    /// <summary>
    /// Process incoming server messages for room detection and combat triggers.
    /// Handles "Also here:" lines that may span multiple TCP chunks due to line wrapping.
    /// </summary>
    public void ProcessMessage(string message)
    {
        if (!_combatEnabled || _playerDatabase == null || _monsterDatabase == null)
            return;
        
        // Check if we're in the middle of capturing an "Also here:" line
        if (_capturingAlsoHere)
        {
            _alsoHereBuffer.Append(message);
            
            // Check if we have a complete "Also here:" line now (ends with period)
            var bufferedText = _alsoHereBuffer.ToString();
            var alsoHereMatch = AlsoHereRegex.Match(bufferedText);
            
            if (alsoHereMatch.Success)
            {
                // Found complete match - process it
                _capturingAlsoHere = false;
                _alsoHereBuffer.Clear();
                ProcessAlsoHereMatch(alsoHereMatch.Groups[1].Value);
            }
            else if (bufferedText.Contains("Obvious exits:") || bufferedText.Length > 2000)
            {
                // Safety: if we see "Obvious exits:" or buffer too large, stop capturing
                // This prevents infinite buffering if something goes wrong
                OnLogMessage?.Invoke($"⚠️ Also here buffer abandoned (len={bufferedText.Length})");
                _capturingAlsoHere = false;
                _alsoHereBuffer.Clear();
            }
            return;
        }
        
        // Look for start of "Also here:" line
        if (message.Contains("Also here:"))
        {
            // Try to match complete "Also here:" in this chunk
            var alsoHereMatch = AlsoHereRegex.Match(message);
            if (alsoHereMatch.Success)
            {
                // Complete match in single chunk
                ProcessAlsoHereMatch(alsoHereMatch.Groups[1].Value);
            }
            else
            {
                // Partial match - start buffering
                _capturingAlsoHere = true;
                _alsoHereBuffer.Clear();
                _alsoHereBuffer.Append(message);
                OnLogMessage?.Invoke($"🔍 Buffering multi-chunk 'Also here:' line...");
            }
        }
    }
    
    /// <summary>
    /// Process a complete "Also here:" match
    /// </summary>
    private void ProcessAlsoHereMatch(string alsoHereContent)
    {
        // Normalize whitespace (remove newlines from wrapped text)
        alsoHereContent = Regex.Replace(alsoHereContent, @"\s+", " ").Trim();
        
        // Prevent duplicate processing of the same room content
        if (alsoHereContent.Equals(_lastProcessedAlsoHere, StringComparison.OrdinalIgnoreCase))
        {
            OnLogMessage?.Invoke($"🔍 Skipping duplicate 'Also here:' content");
            return;
        }
        
        _lastProcessedAlsoHere = alsoHereContent;
        OnLogMessage?.Invoke($"🔍 Processing: Also here: {alsoHereContent}");
        ParseAlsoHereLine(alsoHereContent);
    }
    
    /// <summary>
    /// Parse the "Also here:" line to identify players and monsters
    /// </summary>
    private void ParseAlsoHereLine(string contents)
    {
        if (_playerDatabase == null || _monsterDatabase == null)
        {
            OnLogMessage?.Invoke("⚠️ ParseAlsoHereLine: Database not initialized");
            return;
        }
        
        _currentRoomEnemies.Clear();
        
        // Split by comma, trim each entry
        var entities = contents.Split(',')
            .Select(e => e.Trim())
            .Where(e => !string.IsNullOrEmpty(e))
            .ToList();
        
        OnLogMessage?.Invoke($"🔍 DEBUG: Parsing {entities.Count} entities: {string.Join(", ", entities)}");
        
        var enemies = new List<(string Name, AttackPriority Priority)>();
        
        foreach (var entity in entities)
        {
            // Check if this is a known player (by first name)
            var firstName = entity.Split(' ')[0];
            var player = _playerDatabase.GetPlayer(firstName);
            
            if (player != null)
            {
                // This is a player, skip
                OnLogMessage?.Invoke($"🔍 DEBUG: '{entity}' is a player (matched '{firstName}'), skipping");
                continue;
            }
            
            // Check if this is a known monster (exact match first)
            var monster = _monsterDatabase.GetMonsterByName(entity);
            if (monster != null)
            {
                var monsterOverride = _monsterDatabase.GetOverride(monster.Number);
                OnLogMessage?.Invoke($"🔍 DEBUG: '{entity}' matched monster #{monster.Number}, relationship={monsterOverride.Relationship}");
                
                // Only attack enemies
                if (monsterOverride.Relationship == MonsterRelationship.Enemy)
                {
                    enemies.Add((entity, monsterOverride.Priority));
                }
            }
            else
            {
                // Unknown entity - check if it might be a monster with flavor text
                // Try to find a monster whose name is contained in this entity
                var matchedMonster = _monsterDatabase.FindMonsterByPartialName(entity);
                if (matchedMonster != null)
                {
                    var monsterOverride = _monsterDatabase.GetOverride(matchedMonster.Number);
                    OnLogMessage?.Invoke($"🔍 DEBUG: '{entity}' partial matched monster '{matchedMonster.Name}' #{matchedMonster.Number}, relationship={monsterOverride.Relationship}");
                    
                    if (monsterOverride.Relationship == MonsterRelationship.Enemy)
                    {
                        // Use the full name we saw (with flavor text) for the attack command
                        enemies.Add((entity, monsterOverride.Priority));
                    }
                }
                else
                {
                    OnLogMessage?.Invoke($"🔍 DEBUG: '{entity}' - no match found (not a player or known monster)");
                }
            }
        }
        
        // Sort enemies by priority (First > High > Normal > Low > Last)
        // For same priority, maintain original order (left to right)
        var sortedEnemies = enemies
            .Select((e, index) => (e.Name, e.Priority, Index: index))
            .OrderBy(e => e.Priority)
            .ThenBy(e => e.Index)
            .Select(e => e.Name)
            .ToList();
        
        _currentRoomEnemies = sortedEnemies;
        
        if (_currentRoomEnemies.Count > 0)
        {
            OnLogMessage?.Invoke($"🎯 Enemies detected: {string.Join(", ", _currentRoomEnemies)}");
            
            // Auto-attack if not already in combat
            TryInitiateCombat();
        }
        else
        {
            OnLogMessage?.Invoke($"🔍 DEBUG: No enemies found in room");
        }
    }
    
    /// <summary>
    /// Called when *Combat Off* is detected - try to attack next enemy
    /// </summary>
    public void OnCombatEnded()
    {
        if (!_combatEnabled)
            return;
        
        // Current target is dead, clear target tracking and pending flag
        _currentTarget = string.Empty;
        _attackSpellCastCount = 0;
        _attackPending = false;
        _usedMeleeThisRound = false;
        _lastProcessedAlsoHere = string.Empty; // Clear so we can process the refreshed room
        
        // Send enter to refresh room contents and detect remaining enemies
        // The response will trigger ProcessMessage -> ParseAlsoHereLine -> TryInitiateCombat
        OnLogMessage?.Invoke("🔄 Combat ended, checking for remaining enemies...");
        OnSendCommand?.Invoke("");
    }
    
    /// <summary>
    /// Attempt to initiate combat with the highest priority enemy
    /// </summary>
    private void TryInitiateCombat()
    {
        if (!_combatEnabled || _isInCombat == null)
            return;
        
        // Don't attack if already in combat
        if (_isInCombat())
        {
            OnLogMessage?.Invoke("⚠️ Already in combat, not re-engaging");
            return;
        }
        
        // Don't attack if we already have an attack pending (waiting for *Combat Engaged*)
        // But clear pending flag if it's been too long (attack may have failed)
        if (_attackPending)
        {
            var pendingDuration = (DateTime.Now - _attackPendingSince).TotalMilliseconds;
            if (pendingDuration > ATTACK_PENDING_TIMEOUT_MS)
            {
                OnLogMessage?.Invoke($"⚠️ Attack pending timeout ({pendingDuration:F0}ms), clearing and retrying");
                _attackPending = false;
            }
            else
            {
                OnLogMessage?.Invoke("⚠️ Attack already pending, waiting for combat to engage");
                return;
            }
        }
        
        if (_currentRoomEnemies.Count == 0)
        {
            OnLogMessage?.Invoke("✓ No enemies remaining");
            return;
        }
        
        var settings = GetCurrentSettings();
        
        // Debug: Log current character and settings
        OnLogMessage?.Invoke($"🔍 DEBUG: CurrentCharacter='{_currentCharacter}', AttackCommand='{settings.AttackCommand}', AttackSpell='{settings.AttackSpell}'");
        
        if (string.IsNullOrEmpty(settings.AttackCommand))
        {
            OnLogMessage?.Invoke("⚠️ No attack command configured");
            return;
        }
        
        // Get the target (first enemy in the sorted list - highest priority)
        var target = _currentRoomEnemies[0];
        
        // Check if this is a new target
        if (!target.Equals(_currentTarget, StringComparison.OrdinalIgnoreCase))
        {
            _currentTarget = target;
            _attackSpellCastCount = 0;
            _usedMeleeThisRound = false;
            OnLogMessage?.Invoke($"🎯 New target: {target}");
        }
        
        // Determine which attack method to use
        var command = GetAttackCommand(settings, target);
        
        // Don't send empty commands
        if (string.IsNullOrEmpty(command))
        {
            OnLogMessage?.Invoke($"⚠️ No valid attack command to send");
            return;
        }
        
        OnLogMessage?.Invoke($"⚔️ Sending command: {command}");
        OnSendCommand?.Invoke(command);
        _attackPending = true;
        _attackPendingSince = DateTime.Now;
    }
    
    /// <summary>
    /// Determine the attack command to use based on settings, mana, and cast count
    /// </summary>
    private string GetAttackCommand(CombatSettings settings, string target)
    {
        // Check if we should use Attack Spell
        if (CanUseAttackSpell(settings))
        {
            _attackSpellCastCount++;
            _usedMeleeThisRound = false;
            OnLogMessage?.Invoke($"🔮 Using Attack Spell: {settings.AttackSpell} ({_attackSpellCastCount}/{settings.AttackMaxCast})");
            
            // Defensive check - ensure spell is not empty
            if (string.IsNullOrEmpty(settings.AttackSpell))
            {
                OnLogMessage?.Invoke($"⚠️ ERROR: AttackSpell is empty but CanUseAttackSpell returned true!");
                return $"{settings.AttackCommand} {target}";
            }
            
            return $"{settings.AttackSpell} {target}";
        }
        
        // Check if we fell back to melee because of low mana (not because max cast reached)
        if (!string.IsNullOrEmpty(settings.AttackSpell) && 
            settings.AttackMaxCast > 0 && 
            _attackSpellCastCount < settings.AttackMaxCast)
        {
            // We have spell casts remaining but couldn't use it (must be low mana)
            _usedMeleeThisRound = true;
            OnLogMessage?.Invoke($"⚔️ Using melee (low mana), spell casts remaining: {settings.AttackMaxCast - _attackSpellCastCount}");
        }
        else
        {
            _usedMeleeThisRound = false;
        }
        
        // Fall back to melee attack command
        OnLogMessage?.Invoke($"⚔️ Using melee attack: {settings.AttackCommand}");
        
        // Defensive check - ensure attack command is not empty
        if (string.IsNullOrEmpty(settings.AttackCommand))
        {
            OnLogMessage?.Invoke($"⚠️ ERROR: AttackCommand is empty!");
            return string.Empty; // Return empty so nothing is sent
        }
        
        return $"{settings.AttackCommand} {target}";
    }
    
    /// <summary>
    /// Check if we can use the Attack Spell
    /// </summary>
    private bool CanUseAttackSpell(CombatSettings settings)
    {
        // No attack spell configured
        if (string.IsNullOrEmpty(settings.AttackSpell))
            return false;
        
        // Max cast is 0 means never use spell
        if (settings.AttackMaxCast <= 0)
            return false;
        
        // Already cast max times on this target
        if (_attackSpellCastCount >= settings.AttackMaxCast)
        {
            OnLogMessage?.Invoke($"⚠️ Attack spell max cast reached ({settings.AttackMaxCast})");
            return false;
        }
        
        // Check mana requirement
        if (_getCurrentManaPercent != null)
        {
            int currentManaPercent = _getCurrentManaPercent();
            if (currentManaPercent < settings.AttackReqManaPercent)
            {
                OnLogMessage?.Invoke($"⚠️ Mana too low for attack spell ({currentManaPercent}% < {settings.AttackReqManaPercent}%)");
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Called when *Combat Engaged* is detected
    /// </summary>
    public void OnCombatEngaged()
    {
        _attackPending = false;
        
        // Remove the first enemy from the list (the one we're now fighting)
        if (_currentRoomEnemies.Count > 0)
        {
            _currentRoomEnemies.RemoveAt(0);
        }
    }
    
    /// <summary>
    /// Called on each combat tick to potentially send another attack spell
    /// if we're in combat and have regained enough mana after previously falling back to melee
    /// </summary>
    public void OnCombatTick()
    {
        if (!_combatEnabled || _isInCombat == null || !_isInCombat())
            return;
        
        // Only do this if we have a current target
        if (string.IsNullOrEmpty(_currentTarget))
            return;
        
        // Only send attack spell if we previously had to use melee due to low mana
        // This prevents sending attack commands every tick when we're already fighting
        if (!_usedMeleeThisRound)
            return;
        
        var settings = GetCurrentSettings();
        
        // Check if we can use attack spell now (mana may have regenerated)
        if (CanUseAttackSpell(settings))
        {
            _attackSpellCastCount++;
            _usedMeleeThisRound = false; // We're using spell now
            var command = $"{settings.AttackSpell} {_currentTarget}";
            OnLogMessage?.Invoke($"🔮 Combat tick - Mana recovered! Using Attack Spell: {settings.AttackSpell} ({_attackSpellCastCount}/{settings.AttackMaxCast})");
            OnSendCommand?.Invoke(command);
        }
    }
    
    /// <summary>
    /// Clear room state (e.g., when player moves to a new room)
    /// </summary>
    public void ClearRoomState()
    {
        _currentRoomEnemies.Clear();
        _attackPending = false;
        _currentTarget = string.Empty;
        _attackSpellCastCount = 0;
        _usedMeleeThisRound = false;
        _lastProcessedAlsoHere = string.Empty;
        _capturingAlsoHere = false;
        _alsoHereBuffer.Clear();
    }
    
    /// <summary>
    /// Reset all combat state (e.g., on reconnection)
    /// </summary>
    public void ResetState()
    {
        _currentRoomEnemies.Clear();
        _attackPending = false;
        _currentTarget = string.Empty;
        _attackSpellCastCount = 0;
        _usedMeleeThisRound = false;
        _lastProcessedAlsoHere = string.Empty;
        _capturingAlsoHere = false;
        _alsoHereBuffer.Clear();
        // Don't clear _currentCharacter - it will be set again when stat output is received
        OnLogMessage?.Invoke("🔄 Combat state reset");
    }
    
    #endregion
}
