using System.Text;
using System.Text.RegularExpressions;

namespace MudProxyViewer;

/// <summary>
/// Manages combat automation. Combat settings are stored per-character in the CharacterProfile.
/// No global file is used - data is loaded via LoadFromProfile() and retrieved via GetCurrentSettings().
/// </summary>
public class CombatManager
{
    private CombatSettings _settings = new();
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
    private static readonly Regex EntityEnteredRoomRegex = new(@"in from the (north|south|east|west|northeast|southeast|northwest|southwest|above|below)!", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    public event Action<string>? OnLogMessage;
    public event Action<string>? OnSendCommand;
    public event Action? OnCombatEnabledChanged; // Event for BuffManager to save settings
    public event Action<List<string>>? OnPlayersDetected; // Event when players are seen in room
    
    public CombatManager()
    {
        // No file loading - data comes from character profile via LoadFromProfile()
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
            _settings.CharacterName = value;
            OnLogMessage?.Invoke($"⚔️ Combat settings active for: {value}");
            
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
    
    #region Profile Integration
    
    /// <summary>
    /// Load combat settings from a character profile
    /// </summary>
    public void LoadFromProfile(CombatSettings? settings)
    {
        if (settings != null)
        {
            _settings = settings;
            _currentCharacter = settings.CharacterName;
            OnLogMessage?.Invoke($"📂 Loaded combat settings for: {settings.CharacterName}");
        }
        else
        {
            _settings = new CombatSettings();
            OnLogMessage?.Invoke("📂 No combat settings in profile, using defaults");
        }
    }
    
    /// <summary>
    /// Get current settings for saving to character profile
    /// </summary>
    public CombatSettings GetCurrentSettings()
    {
        _settings.CharacterName = _currentCharacter;
        return _settings;
    }
    
    /// <summary>
    /// Save/update settings (called from settings dialog)
    /// </summary>
    public void SaveSettings(CombatSettings settings)
    {
        _settings = settings;
        if (!string.IsNullOrEmpty(settings.CharacterName))
        {
            _currentCharacter = settings.CharacterName;
        }
    }
    
    /// <summary>
    /// Clear settings (when no character is loaded)
    /// </summary>
    public void Clear()
    {
        _settings = new CombatSettings();
        _currentCharacter = string.Empty;
    }
    
    #endregion
    
    #region Message Processing
    
    /// <summary>
    /// Process incoming server messages for room detection and combat triggers.
    /// Handles "Also here:" lines that may span multiple TCP chunks due to line wrapping.
    /// </summary>
    public void ProcessMessage(string message)
    {
        if (!_combatEnabled)
            return;
        
        // Handle multi-chunk "Also here:" lines
        // MUD may send: "Also here: monster1, monster2, monster3,\r\nmonster4, monster5."
        // across multiple TCP chunks
        
        // Check if we're continuing to capture an "Also here:" line
        if (_capturingAlsoHere)
        {
            _alsoHereBuffer.Append(message);
            
            // Check if we have a complete line (ends with period)
            var bufferedContent = _alsoHereBuffer.ToString();
            if (bufferedContent.Contains('.'))
            {
                // We have a complete line
                _capturingAlsoHere = false;
                var match = AlsoHereRegex.Match(bufferedContent);
                if (match.Success)
                {
                    ProcessAlsoHere(match.Groups[1].Value);
                }
                _alsoHereBuffer.Clear();
            }
            return;
        }
        
        // Check for "Also here:" start
        if (message.Contains("Also here:"))
        {
            // Check if this is a complete line (contains the ending period)
            var match = AlsoHereRegex.Match(message);
            if (match.Success)
            {
                ProcessAlsoHere(match.Groups[1].Value);
            }
else
            {
                // Incomplete line - start capturing
                _capturingAlsoHere = true;
                _alsoHereBuffer.Clear();
                _alsoHereBuffer.Append(message);
            }
        }
        
        // Check for entity entering the room - refresh room contents to detect new enemies
        // NOTE: This may false-positive on chat messages containing directional text.
        // This is harmless (just an extra room refresh). If it becomes a problem,
        // add chat message filtering here (e.g., exclude lines matching "tells you:", "gossips:", etc.)
        if (EntityEnteredRoomRegex.IsMatch(message))
        {
            _lastProcessedAlsoHere = string.Empty;
            OnLogMessage?.Invoke("👁️ Something entered the room, refreshing...");
            OnSendCommand?.Invoke("");
        }
    }
    
    /// <summary>
    /// Process the "Also here:" line content
    /// </summary>
    private void ProcessAlsoHere(string alsoHereContent)
    {
        // Avoid processing the same content twice in quick succession
        if (alsoHereContent == _lastProcessedAlsoHere)
        {
            return;
        }
        _lastProcessedAlsoHere = alsoHereContent;
        
        // Clear previous room enemies
        _currentRoomEnemies.Clear();
        
        // Track players detected in room for auto-invite
        var playersInRoom = new List<string>();
        
        // Parse entities in the room
        var entities = alsoHereContent.Split(',')
            .Select(e => e.Trim())
            .Where(e => !string.IsNullOrEmpty(e))
            .ToList();
        
        OnLogMessage?.Invoke($"👁️ Room scan: {entities.Count} entities found");
        
        foreach (var entity in entities)
        {
            // Check if this is a player (has class/level in parentheses or is in player database)
            if (IsPlayer(entity))
            {
                OnLogMessage?.Invoke($"   👤 Player: {entity}");
                playersInRoom.Add(entity);
                continue;
            }
            
            // Check if this is a monster we should attack
            if (ShouldAttack(entity))
            {
                _currentRoomEnemies.Add(entity);
                OnLogMessage?.Invoke($"   🎯 Enemy: {entity}");
            }
            else
            {
                OnLogMessage?.Invoke($"   ⚪ Neutral/Friendly: {entity}");
            }
        }
        
        // Fire event for players detected (for auto-invite feature)
        if (playersInRoom.Count > 0)
        {
            OnPlayersDetected?.Invoke(playersInRoom);
        }
        
        // Sort enemies by priority if monster database is available
        if (_monsterDatabase != null)
        {
            SortEnemiesByPriority();
        }
        
        // Try to initiate combat if we found enemies
        if (_currentRoomEnemies.Count > 0)
        {
            OnLogMessage?.Invoke($"⚔️ {_currentRoomEnemies.Count} enemies to attack");
            TryInitiateCombat();
        }
    }
    
    /// <summary>
    /// Check if an entity is a player
    /// </summary>
    private bool IsPlayer(string entity)
    {
        // Players typically have format: "Name (Class Level)" or just "Name"
        // Example: "Azii (Priest 25)" or "Bob"
        
        // Check for class/level pattern
        if (entity.Contains('(') && entity.Contains(')'))
        {
            // Likely a player with class/level displayed
            return true;
        }
        
        // Check player database
        if (_playerDatabase != null)
        {
            var firstName = entity.Split(' ')[0];
            var player = _playerDatabase.GetPlayer(firstName);
            if (player != null)
            {
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Check if we should attack this entity
    /// </summary>
    private bool ShouldAttack(string entity)
    {
        if (_monsterDatabase == null)
        {
            // Without monster database, attack anything that's not a player
            return true;
        }
        
        // Find the monster in the database
        var monster = _monsterDatabase.FindMonsterByPartialName(entity);
        if (monster == null)
        {
            // Unknown entity - assume hostile
            OnLogMessage?.Invoke($"   ⚠️ Unknown entity (not in DB): {entity}");
            return true;
        }
        
        // Check the override for this monster
        var monsterOverride = _monsterDatabase.GetOverride(monster.Number);
        
        // Don't attack if marked as not hostile
        if (monsterOverride.NotHostile)
        {
            return false;
        }
        
        // Attack enemies, not friends or neutrals
        return monsterOverride.Relationship == MonsterRelationship.Enemy;
    }
    
    /// <summary>
    /// Sort enemies by attack priority from monster database
    /// </summary>
    private void SortEnemiesByPriority()
    {
        if (_monsterDatabase == null)
            return;
        
        _currentRoomEnemies = _currentRoomEnemies
            .OrderBy(e =>
            {
                var monster = _monsterDatabase.FindMonsterByPartialName(e);
                if (monster == null)
                    return (int)AttackPriority.Normal;
                
                var monsterOverride = _monsterDatabase.GetOverride(monster.Number);
                return (int)monsterOverride.Priority;
            })
            .ToList();
    }
    
    #endregion
    
    #region Combat Actions
    
    /// <summary>
    /// Attempt to start combat with the highest priority enemy
    /// </summary>
    private void TryInitiateCombat()
    {
        if (!_combatEnabled)
            return;

        // Don't attack if no character is set (we don't know what settings to use)
        if (string.IsNullOrEmpty(_currentCharacter))
        {
            OnLogMessage?.Invoke("⚠️ No character loaded, cannot initiate combat");
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
    
    #region Legacy Support
    
    /// <summary>
    /// Legacy method - returns settings for current character
    /// </summary>
    [Obsolete("Use GetCurrentSettings() instead")]
    public CombatSettings GetSettings(string characterName)
    {
        if (_settings.CharacterName.Equals(characterName, StringComparison.OrdinalIgnoreCase))
        {
            return _settings;
        }
        return new CombatSettings { CharacterName = characterName };
    }
    
    /// <summary>
    /// Legacy method - no longer returns multiple characters
    /// </summary>
    [Obsolete("Combat settings are now per-character profile")]
    public IReadOnlyList<string> GetAllCharacterNames()
    {
        return new List<string> { _currentCharacter }.AsReadOnly();
    }
    
    #endregion
}
