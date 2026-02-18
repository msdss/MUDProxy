using System.Text.RegularExpressions;

namespace MudProxyViewer;

/// <summary>
/// Manages player state including HP, Mana, resting/combat status, 
/// experience tracking, and stat/exp parsing.
/// Extracted from BuffManager for better separation of concerns.
/// </summary>
public class PlayerStateManager
{
    // Player info and experience
    private PlayerInfo _playerInfo = new();
    private readonly ExperienceTracker _experienceTracker = new();
    
    // HP/Mana state
    private int _currentHp = 0;
    private int _maxHp = 0;
    private int _currentMana = 0;
    private int _maxMana = 0;
    private string _manaType = "MA";  // "MA" or "KAI"
    
    // Player status flags
    private bool _isResting = false;
    private bool _inCombat = false;
    private bool _isInLoginPhase = true;
    private bool _inTrainingScreen = false;
    private bool _hasEnteredGame = false;
    
    // Dependencies
    private readonly Action<string> _logMessage;
    private readonly Action<string> _sendCommand;
    
    // Events
    public event Action? OnPlayerInfoChanged;
    public event Action<bool>? OnTrainingScreenChanged;  // true = entered, false = exited
    
    // Regex patterns for stat parsing
    private static readonly Regex StatNameRegex = new(@"Name:\s*(.+?)\s{2,}Lives", RegexOptions.Compiled);
    private static readonly Regex StatRaceRegex = new(@"Race:\s*(\w-)", RegexOptions.Compiled);
    private static readonly Regex StatClassRegex = new(@"Class:\s*(\w-)", RegexOptions.Compiled);
    private static readonly Regex StatLevelRegex = new(@"Level:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex StatHitsRegex = new(@"Hits:\s*(\d+)/(\d+)", RegexOptions.Compiled);
    private static readonly Regex StatManaRegex = new(@"Mana:\s*\*?\s*(\d+)/(\d+)", RegexOptions.Compiled);
    
    // Regex patterns for exp parsing
    private static readonly Regex ExpCommandRegex = new(
        @"Exp:\s*(\d+)\s+Level:\s*(\d+)\s+Exp needed for next level:\s*(\d+)\s+\((\d+)\)\s+\[(\d+)%\]",
        RegexOptions.Compiled);
    
    private static readonly Regex ExpGainRegex = new(
        @"You gain (\d[\d,]*) experience\.",
        RegexOptions.Compiled);
    
    // Regex for HP/Mana prompt
    private static readonly Regex HpManaPromptRegex = new(
        @"\[HP=(\d+)(?:/(MA|KAI)=(\d+))?\]:?\s*(\(Resting\))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    public PlayerStateManager(
        Action<string> logMessage,
        Action<string> sendCommand)
    {
        _logMessage = logMessage;
        _sendCommand = sendCommand;
    }
    
    #region Properties
    
    public PlayerInfo PlayerInfo => _playerInfo;
    public ExperienceTracker ExperienceTracker => _experienceTracker;
    
    public int CurrentHp => _currentHp;
    public int MaxHp => _maxHp;
    public int CurrentMana => _currentMana;
    public int MaxMana => _maxMana;
    public string ManaType => _manaType;
    public int CurrentHpPercent => _maxHp > 0 ? (_currentHp * 100 / _maxHp) : 100;
    public int CurrentManaPercent => _maxMana > 0 ? (_currentMana * 100 / _maxMana) : 100;
    
    public bool IsResting => _isResting;
    public bool InCombat => _inCombat;
    public bool InTrainingScreen => _inTrainingScreen;
    public bool HasEnteredGame => _hasEnteredGame;
    
    public bool IsInLoginPhase
    {
        get => _isInLoginPhase;
        set => _isInLoginPhase = value;
    }
    
    #endregion
    
    #region State Setters
    
    /// <summary>
    /// Set the combat state (called from MainForm when combat state changes)
    /// </summary>
    public void SetCombatState(bool inCombat)
    {
        if (_inCombat != inCombat)
        {
            _inCombat = inCombat;
            _logMessage(inCombat ? "⚔️ Entered combat" : "🏠 Left combat");
        }
    }
    
    #endregion
    
    #region Message Processing
    
    /// <summary>
    /// Process incoming messages for player state changes.
    /// Called by BuffManager.ProcessMessage().
    /// </summary>
    public void ProcessMessage(string message)
    {
        // Check for training/character creation screen
        if (message.Contains("Point Cost Chart"))
        {
            if (!_inTrainingScreen)
            {
                _inTrainingScreen = true;
                _logMessage("📋 Training screen detected - pass-through mode enabled");
                OnTrainingScreenChanged?.Invoke(true);
            }
        }
        
        // Check for stat command output
        if (message.Contains("Name:") && message.Contains("Race:") && message.Contains("Class:"))
        {
            ParseStatOutput(message);
        }
        
        // Check for exp command output
        var expMatch = ExpCommandRegex.Match(message);
        if (expMatch.Success)
        {
            ParseExpCommandOutput(expMatch);
        }
        
        // Check for experience gain
        var expGainMatch = ExpGainRegex.Match(message);
        if (expGainMatch.Success)
        {
            if (long.TryParse(expGainMatch.Groups[1].Value, out long expGained))
            {
                _experienceTracker.AddExpGain(expGained);
                _playerInfo.TotalExperience += expGained;
                _playerInfo.ExperienceNeededForNextLevel -= expGained;
                _logMessage($"📈 EXP gained: {expGained} | Session total: {_experienceTracker.SessionExpGained} | Rate: {_experienceTracker.GetExpPerHour()}/hr");
            }
        }
        
        // Track HP and mana from prompt
        var promptMatch = HpManaPromptRegex.Match(message);
        if (promptMatch.Success)
        {
            ProcessHpManaPrompt(promptMatch);
        }
    }
    
    private void ProcessHpManaPrompt(Match promptMatch)
    {
        // If we see the HP prompt, we're back in the game
        if (_inTrainingScreen)
        {
            _inTrainingScreen = false;
            _logMessage("🎮 Returned to game - pass-through mode disabled");
            OnTrainingScreenChanged?.Invoke(false);

            _logMessage("🎮 Refreshing character data after training...");
            Task.Run(async () =>
            {
                await Task.Delay(500);
                _sendCommand("stat");
                await Task.Delay(500);
                _sendCommand("exp");
            });
        }
        
        // First time seeing HP bar this session
        if (!_hasEnteredGame)
        {
            _hasEnteredGame = true;
            _logMessage("🎮 Entered game - sending startup commands");
            
            Task.Run(async () =>
            {
                await Task.Delay(500);
                _sendCommand("who");
                await Task.Delay(500);
                _sendCommand("stat");
                await Task.Delay(500);
                _sendCommand("exp");
            });
        }
        
        // Parse HP
        if (int.TryParse(promptMatch.Groups[1].Value, out int hp))
        {
            _currentHp = hp;
            _playerInfo.CurrentHp = hp;
            
            if (_currentHp > _maxHp)
            {
                _maxHp = _currentHp;
                _playerInfo.MaxHp = _currentHp;
            }
        }
        
        // Parse Mana type and value
        if (promptMatch.Groups[2].Success)
        {
            _manaType = promptMatch.Groups[2].Value.ToUpperInvariant();
            
            if (int.TryParse(promptMatch.Groups[3].Value, out int mana))
            {
                _currentMana = mana;
                _playerInfo.CurrentMana = mana;
                
                if (_currentMana > _maxMana)
                {
                    _maxMana = _currentMana;
                    _playerInfo.MaxMana = _currentMana;
                }
            }
        }
        
        // Check resting state
        bool wasResting = _isResting;
        _isResting = promptMatch.Groups[4].Success;
        
        if (_isResting != wasResting)
        {
            _logMessage(_isResting ? "💤 Player is now resting" : "🏃 Player is no longer resting");
        }
    }
    
    #endregion
    
    #region Stat Parsing
    
    private void ParseStatOutput(string message)
    {
        var nameMatch = StatNameRegex.Match(message);
        if (nameMatch.Success)
        {
            _playerInfo.Name = nameMatch.Groups[1].Value.Trim();
        }
        
        var raceMatch = StatRaceRegex.Match(message);
        if (raceMatch.Success)
        {
            _playerInfo.Race = raceMatch.Groups[1].Value;
        }
        
        var classMatch = StatClassRegex.Match(message);
        if (classMatch.Success)
        {
            _playerInfo.Class = classMatch.Groups[1].Value;
        }
        
        var levelMatch = StatLevelRegex.Match(message);
        if (levelMatch.Success && int.TryParse(levelMatch.Groups[1].Value, out int level))
        {
            _playerInfo.Level = level;
        }
        
        var hitsMatch = StatHitsRegex.Match(message);
        if (hitsMatch.Success)
        {
            if (int.TryParse(hitsMatch.Groups[1].Value, out int currentHp))
            {
                _playerInfo.CurrentHp = currentHp;
                _currentHp = currentHp;
            }
            if (int.TryParse(hitsMatch.Groups[2].Value, out int maxHp))
            {
                _playerInfo.MaxHp = maxHp;
                _maxHp = maxHp;
            }
        }
        
        var manaMatch = StatManaRegex.Match(message);
        if (manaMatch.Success)
        {
            if (int.TryParse(manaMatch.Groups[1].Value, out int currentMana))
            {
                _playerInfo.CurrentMana = currentMana;
                _currentMana = currentMana;
            }
            if (int.TryParse(manaMatch.Groups[2].Value, out int maxMana))
            {
                _playerInfo.MaxMana = maxMana;
                _maxMana = maxMana;
            }
        }
        
        _logMessage($"📊 Stats: {_playerInfo.Name} | {_playerInfo.Race} {_playerInfo.Class} Lv{_playerInfo.Level} | HP:{_currentHp}/{_maxHp} | Mana:{_currentMana}/{_maxMana}");
        OnPlayerInfoChanged?.Invoke();
    }
    
    private void ParseExpCommandOutput(Match match)
    {
        if (long.TryParse(match.Groups[1].Value, out long totalExp))
            _playerInfo.TotalExperience = totalExp;
        
        if (int.TryParse(match.Groups[2].Value, out int level))
            _playerInfo.Level = level;
        
        if (long.TryParse(match.Groups[3].Value, out long expNeeded))
            _playerInfo.ExperienceNeededForNextLevel = expNeeded;
        
        if (long.TryParse(match.Groups[4].Value, out long totalExpForNext))
            _playerInfo.TotalExperienceForNextLevel = totalExpForNext;
        
        if (int.TryParse(match.Groups[5].Value, out int percent))
            _playerInfo.LevelProgressPercent = percent;
        
        var expPerHour = _experienceTracker.GetExpPerHour();
        var timeToLevel = _experienceTracker.EstimateTimeToExp(_playerInfo.ExperienceNeededForNextLevel);
        
        _logMessage($"📊 EXP: Level {_playerInfo.Level} | " +
            $"Need {ExperienceTracker.FormatNumber(_playerInfo.ExperienceNeededForNextLevel)} | " +
            $"Rate: {ExperienceTracker.FormatNumber(expPerHour)}/hr | " +
            $"ETA: {ExperienceTracker.FormatTimeSpan(timeToLevel)}");
        
        OnPlayerInfoChanged?.Invoke();
    }
    
    #endregion
    
    #region Target Checking
    
    /// <summary>
    /// Check if the target name refers to the player themselves.
    /// Handles cases where game uses short name "Azii" but full name is "Azii RageQuit"
    /// </summary>
    public bool IsTargetSelf(string targetName)
    {
        if (string.IsNullOrEmpty(_playerInfo.Name) || string.IsNullOrEmpty(targetName))
            return false;
        
        // Exact match
        if (targetName.Equals(_playerInfo.Name, StringComparison.OrdinalIgnoreCase))
            return true;
        
        // Target is the first part of player name (e.g., "Azii" matches "Azii RageQuit")
        if (_playerInfo.Name.StartsWith(targetName + " ", StringComparison.OrdinalIgnoreCase))
            return true;
        
        // Player name is the first part of target
        if (targetName.StartsWith(_playerInfo.Name + " ", StringComparison.OrdinalIgnoreCase))
            return true;
        
        // First word of player name matches target
        var playerFirstName = _playerInfo.Name.Split(' ')[0];
        if (targetName.Equals(playerFirstName, StringComparison.OrdinalIgnoreCase))
            return true;
        
        return false;
    }
    
    #endregion
    
    #region State Management
    
    /// <summary>
    /// Called when disconnected from server - resets session state
    /// </summary>
    public void OnDisconnected()
    {
        _hasEnteredGame = false;
        _inTrainingScreen = false;
        _inCombat = false;
        _isResting = false;
        _experienceTracker.Reset();
    }
    
    /// <summary>
    /// Reset player info (used when creating new character profile)
    /// </summary>
    public void ResetPlayerInfo()
    {
        _playerInfo = new PlayerInfo();
        OnPlayerInfoChanged?.Invoke();
    }
    
    /// <summary>
    /// Load player info from a character profile
    /// </summary>
    public void LoadFromProfile(string? characterName, string? characterClass, int characterLevel)
    {
        if (!string.IsNullOrEmpty(characterName))
        {
            _playerInfo.Name = characterName;
            _playerInfo.Class = characterClass ?? string.Empty;
            _playerInfo.Level = characterLevel;
            OnPlayerInfoChanged?.Invoke();
        }
    }
    
    /// <summary>
    /// Update player info name (used when loading profile)
    /// </summary>
    public void SetPlayerName(string name)
    {
        _playerInfo.Name = name;
    }
    
    /// <summary>
    /// Update player info class (used when loading profile)
    /// </summary>
    public void SetPlayerClass(string className)
    {
        _playerInfo.Class = className;
    }
    
    /// <summary>
    /// Update player info level (used when loading profile)
    /// </summary>
    public void SetPlayerLevel(int level)
    {
        _playerInfo.Level = level;
    }
    
    #endregion
}
