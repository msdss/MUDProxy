using System.Text.Json;
using System.Text.RegularExpressions;

namespace MudProxyViewer;

public class BuffManager
{
    private readonly List<BuffConfiguration> _buffConfigurations = new();
    private readonly List<ActiveBuff> _activeBuffs = new();
    private readonly List<PartyMember> _partyMembers = new();
    private PlayerInfo _playerInfo = new();
    
    private readonly string _configFilePath;
    private readonly string _settingsFilePath;
    private readonly string _characterProfilesPath;
    private string _currentProfilePath = string.Empty;
    private readonly HealingManager _healingManager;
    private readonly CureManager _cureManager;
    private readonly PlayerDatabaseManager _playerDatabaseManager;
    private readonly MonsterDatabaseManager _monsterDatabaseManager;
    private readonly CombatManager _combatManager;
    private readonly RemoteCommandManager _remoteCommandManager;
    private readonly RoomGraphManager _roomGraphManager;
    
    // Events for UI updates
    public event Action? OnBuffsChanged;
    public event Action? OnPartyChanged;
    public event Action? OnPlayerInfoChanged;
    public event Action<string>? OnLogMessage;
    public event Action<string>? OnSendCommand; // Event to send commands to the MUD
    public event Action? OnHangupRequested;  // Remote command requested hangup
    public event Action? OnRelogRequested;   // Remote command requested relog
    public event Action? OnAutomationStateChanged;  // Remote command changed automation state
    public event Action<bool>? OnTrainingScreenChanged;  // true = entered, false = exited
    
    // Casting state
    private bool _castBlockedUntilNextTick = false;
    private int _currentMana = 0;
    private int _maxMana = 0;
    private int _currentHp = 0;
    private int _maxHp = 0;
    private string _manaType = "MA";  // "MA" or "KAI" - detected from HP bar
    private int _manaReservePercent = 20; // Don't cast if mana below this %
    private bool _isResting = false;  // Player is in resting state
    private bool _inCombat = false;   // Player is in combat
    private bool _isInLoginPhase = true;  // Start true, cleared when HP bar appears
    private bool _inTrainingScreen = false;  // Player is in character training screen
    private bool _commandsPaused = false;  // Manual pause for all commands
    private bool _hasEnteredGame = false;  // True once we've seen the HP bar this session
    private bool _isInParty = false;  // True when following someone or being followed
    private bool _requestHealthAfterPartyUpdate = false;  // Request health from all members after next par
    
    // Buff state settings (persisted)
    private bool _buffWhileResting = true;
    private bool _buffWhileInCombat = true;
    
    // App settings (persisted)
    private bool _autoStartProxy = false;
    private bool _combatAutoEnabled = false;
    private bool _autoLoadLastCharacter = false;
    private string _lastCharacterPath = string.Empty;
    private bool _displaySystemLog = true;  // UI setting: show/hide system log panel
    
    // BBS/Telnet settings (loaded from character profile)
    private BbsSettings _bbsSettings = new();
    
    // Window settings (loaded from character profile)
    private WindowSettings? _windowSettings;
    
    // Experience tracking
    private readonly ExperienceTracker _experienceTracker = new();
    
    // Par frequency settings (persisted)
    private bool _parAutoEnabled = false;
    private int _parFrequencySeconds = 15;
    private bool _parAfterCombatTick = false;
    private DateTime _lastParSent = DateTime.MinValue;
    
    // Health request settings (persisted)
    private bool _healthRequestEnabled = false;
    private int _healthRequestIntervalSeconds = 60;
    private DateTime _lastHealthRequestCheck = DateTime.MinValue;
    
    // Regex patterns
    private static readonly Regex StatNameRegex = new(@"Name:\s*(.+?)\s{2,}Lives", RegexOptions.Compiled);
    private static readonly Regex StatRaceRegex = new(@"Race:\s*(\w+)", RegexOptions.Compiled);
    private static readonly Regex StatClassRegex = new(@"Class:\s*(\w+)", RegexOptions.Compiled);
    private static readonly Regex StatLevelRegex = new(@"Level:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex StatHitsRegex = new(@"Hits:\s*(\d+)/(\d+)", RegexOptions.Compiled);
    private static readonly Regex StatManaRegex = new(@"Mana:\s*\*?\s*(\d+)/(\d+)", RegexOptions.Compiled);
    
    // Experience parsing
    // Format: Exp: 13837284996 Level: 72 Exp needed for next level: 898825529 (14736110525) [93%]
    private static readonly Regex ExpCommandRegex = new(
        @"Exp:\s*(\d+)\s+Level:\s*(\d+)\s+Exp needed for next level:\s*(\d+)\s+\((\d+)\)\s+\[(\d+)%\]",
        RegexOptions.Compiled);
    
    // Format: You gain 1000 experience.
    // May be prefixed with HP bar like [HP=325/MA=47]:You gain 1000 experience.
    private static readonly Regex ExpGainRegex = new(
        @"You gain (\d[\d,]*) experience\.",
        RegexOptions.Compiled);
    
    private static readonly Regex PartyMemberRegex = new(
        @"^\s{2}(\S.*?)\s+\((\w+)\)\s+(?:\[M:\s*(\d+)%\])?\s*\[H:\s*(\d+)%\]\s*([RPM]?)\s*-\s*(\w+)",
        RegexOptions.Compiled | RegexOptions.Multiline);
    
    private static readonly Regex CastFailRegex = new(
        @"You attempt to cast (.+?), but fail\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    private static readonly Regex NotEnoughManaRegex = new(
        @"You do not have enough mana to cast that spell\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    private static readonly Regex AlreadyCastRegex = new(
        @"You have already cast a spell this round!",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    // Party membership detection
    // "You are now following {player}." - you joined someone's party
    // "{player} started to follow you." - someone joined your party
    // "{player} is no longer following you." - someone left your party  
    // "{player} has been removed from your followers." - you kicked someone from your party
    // "You are no longer following {player}." - you left someone's party (now solo)
    // "Your party has been disbanded." - party ended (everyone left)
    private static readonly Regex StartedFollowingRegex = new(
        @"You are now following (\w+)\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SomeoneFollowingYouRegex = new(
        @"(\w+) started to follow you\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SomeoneLeftPartyRegex = new(
        @"(\w+) is no longer following you\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SomeoneRemovedFromPartyRegex = new(
        @"(\w+) has been removed from your followers\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex YouLeftPartyRegex = new(
        @"You are no longer following (\w+)\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PartyDisbandedRegex = new(
        @"Your party has been disbanded\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    // HP bar formats:
    // Non-mana user: [HP=208]:
    // Mana user:     [HP=208/MA=30]:
    // Kai user:      [HP=208/KAI=30]:
    // With resting:  [HP=208/MA=30]: (Resting)
    private static readonly Regex HpManaPromptRegex = new(
        @"\[HP=(\d+)(?:/(MA|KAI)=(\d+))?\]:?\s*(\(Resting\))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    // Telepath HP update variations:
    // "Xyz telepaths: {HP=745/745}" (non-mana user)
    // "Azii telepaths: {HP=151/160,MA=82/110}" (mana user)
    // "Boost telepaths: {HP=149/149,KAI=3/16}" (kai user)
    // "Boost telepaths: {HP=149/149,KAI=3/16, Resting}" (resting)
    // "Boost telepaths: {HP=174/189,KAI=20/21, Resting, Losing HPs}" (resting + losing hp)
    // "Boost telepaths: {HP=189/189,KAI=21/21, Resting, Poisoned}" (resting + poisoned)
    // "Boost telepaths: {HP=189/189,KAI=21/21, Poisoned}" (poisoned, not resting)
    private static readonly Regex TelepathHpRegex = new(
        @"(\w+)\s+telepaths:\s*\{HP=(\d+)/(\d+)(?:,(MA|KAI)=(\d+)/(\d+))?(?:,\s*(?:Resting|Poisoned|Losing HPs))*\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    public int ManaReservePercent
    {
        get => _manaReservePercent;
        set { _manaReservePercent = Math.Clamp(value, 0, 100); SaveSettings(); }
    }
    
    public bool ParAutoEnabled
    {
        get => _parAutoEnabled;
        set { _parAutoEnabled = value; SaveSettings(); }
    }
    
    public int ParFrequencySeconds
    {
        get => _parFrequencySeconds;
        set { _parFrequencySeconds = Math.Clamp(value, 5, 300); SaveSettings(); }
    }
    
    public bool ParAfterCombatTick
    {
        get => _parAfterCombatTick;
        set { _parAfterCombatTick = value; SaveSettings(); }
    }
    
    public bool HealthRequestEnabled
    {
        get => _healthRequestEnabled;
        set { _healthRequestEnabled = value; SaveSettings(); }
    }
    
    public int HealthRequestIntervalSeconds
    {
        get => _healthRequestIntervalSeconds;
        set { _healthRequestIntervalSeconds = Math.Clamp(value, 30, 300); SaveSettings(); }
    }
    
    public bool BuffWhileResting
    {
        get => _buffWhileResting;
        set { _buffWhileResting = value; SaveSettings(); }
    }
    
    public bool BuffWhileInCombat
    {
        get => _buffWhileInCombat;
        set { _buffWhileInCombat = value; SaveSettings(); }
    }
    
    public bool AutoStartProxy
    {
        get => _autoStartProxy;
        set { _autoStartProxy = value; SaveSettings(); }
    }
    
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
    
    public bool CombatAutoEnabled
    {
        get => _combatAutoEnabled;
        set 
        { 
            if (_combatAutoEnabled != value)
            {
                _combatAutoEnabled = value;
                _combatManager.CombatEnabled = value;
                SaveSettings(); 
            }
        }
    }
    
    public bool IsResting => _isResting;
    public bool InCombat => _inCombat;
    public bool InTrainingScreen => _inTrainingScreen;
    public bool IsInParty => _isInParty;
    
    /// <summary>
    /// Experience tracker for calculating exp/hour and time to level
    /// </summary>
    public ExperienceTracker ExperienceTracker => _experienceTracker;
    
    /// <summary>
    /// Manual pause for all automatic commands
    /// </summary>
    public bool CommandsPaused
    {
        get => _commandsPaused;
        set
        {
            if (_commandsPaused != value)
            {
                _commandsPaused = value;
                OnLogMessage?.Invoke(value ? "⏸️ All commands PAUSED" : "▶️ Commands RESUMED");
            }
        }
    }

    public bool IsInLoginPhase
    {
        get => _isInLoginPhase;
        set => _isInLoginPhase = value;
    }

    /// <summary>
    /// Returns true if commands should not be sent (training screen, login, or manually paused)
    /// </summary>
    public bool ShouldPauseCommands => _inTrainingScreen || _commandsPaused || _isInLoginPhase;
    
    /// <summary>
    /// Set the combat state (called from MainForm when combat state changes)
    /// </summary>
    public void SetCombatState(bool inCombat)
    {
        if (_inCombat != inCombat)
        {
            _inCombat = inCombat;
            OnLogMessage?.Invoke(inCombat ? "⚔️ Entered combat" : "🏠 Left combat");
        }
    }

    public HealingManager HealingManager => _healingManager;
    public CureManager CureManager => _cureManager;
    public PlayerDatabaseManager PlayerDatabase => _playerDatabaseManager;
    public MonsterDatabaseManager MonsterDatabase => _monsterDatabaseManager;
    public CombatManager CombatManager => _combatManager;
    public RemoteCommandManager RemoteCommandManager => _remoteCommandManager;
    public RoomGraphManager RoomGraph => _roomGraphManager;
    public int CurrentHp => _currentHp;
    public int MaxHp => _maxHp;
    public int CurrentMana => _currentMana;
    public int MaxMana => _maxMana;
    public int CurrentHpPercent => _maxHp > 0 ? (_currentHp * 100 / _maxHp) : 100;
    
    // BBS Settings (loaded from character profile)
    public BbsSettings BbsSettings => _bbsSettings;
    
    // Window Settings (loaded from character profile)
    public WindowSettings? WindowSettings
    {
        get => _windowSettings;
        set => _windowSettings = value;
    }
    public event Action? OnBbsSettingsChanged;
    
    public BuffManager()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MudProxyViewer");
            
        _configFilePath = Path.Combine(appDataPath, "buffs.json");
        _settingsFilePath = Path.Combine(appDataPath, "settings.json");
        _characterProfilesPath = Path.Combine(appDataPath, "Characters");
        
        // Ensure Characters directory exists
        if (!Directory.Exists(_characterProfilesPath))
        {
            Directory.CreateDirectory(_characterProfilesPath);
        }
        
        _healingManager = new HealingManager(
            () => _playerInfo,
            () => _partyMembers,
            () => _currentMana,
            () => _maxMana,
            IsTargetSelf,
            () => _isResting,
            () => _inCombat
        );
        _healingManager.OnLogMessage += msg => OnLogMessage?.Invoke(msg);
        
        _cureManager = new CureManager(
            () => _playerInfo,
            () => _partyMembers,
            () => _currentMana,
            () => _maxMana,
            () => _manaReservePercent,
            IsTargetSelf
        );
        _cureManager.OnLogMessage += msg => OnLogMessage?.Invoke(msg);
        
        _playerDatabaseManager = new PlayerDatabaseManager();
        _playerDatabaseManager.OnLogMessage += msg => OnLogMessage?.Invoke(msg);
        _playerDatabaseManager.OnDataChanged += AutoSaveCharacterProfile;
        
        _monsterDatabaseManager = new MonsterDatabaseManager();
        _monsterDatabaseManager.OnLogMessage += msg => OnLogMessage?.Invoke(msg);
        _monsterDatabaseManager.OnDataChanged += AutoSaveCharacterProfile;
        
        _combatManager = new CombatManager();
        _combatManager.OnLogMessage += msg => OnLogMessage?.Invoke(msg);
        _combatManager.OnCombatEnabledChanged += () => 
        { 
            _combatAutoEnabled = _combatManager.CombatEnabled; 
            SaveSettings(); 
        };
        _combatManager.OnPlayersDetected += CheckAutoInvitePlayers;
        
        _remoteCommandManager = new RemoteCommandManager(
            _playerDatabaseManager,
            () => _currentHp,
            () => _maxHp,
            () => _currentMana,
            () => _maxMana,
            () => _manaType,  // Use actual mana type from HP bar (MA or KAI)
            () => _combatManager.CombatEnabled,
            enabled => _combatManager.CombatEnabled = enabled,
            () => _healingManager.HealingEnabled,
            enabled => _healingManager.HealingEnabled = enabled,
            () => _cureManager.CuringEnabled,
            enabled => _cureManager.CuringEnabled = enabled,
            () => _autoRecastEnabled,
            enabled => AutoRecastEnabled = enabled,
            () => _playerInfo.Level,
            () => _playerInfo.ExperienceNeededForNextLevel,
            () => _experienceTracker.GetExpPerHour(),
            () => ExperienceTracker.FormatTimeSpan(
                _experienceTracker.EstimateTimeToExp(_playerInfo.ExperienceNeededForNextLevel),
                _playerInfo.ExperienceNeededForNextLevel <= 0),  // "Now!" if already leveled
            () => _experienceTracker.SessionExpGained,
            () => _experienceTracker.Reset()
        );
        _remoteCommandManager.OnLogMessage += msg => OnLogMessage?.Invoke(msg);
        _remoteCommandManager.OnSendCommand += cmd => OnSendCommand?.Invoke(cmd);
        _remoteCommandManager.OnHangupRequested += () => OnHangupRequested?.Invoke();
        _remoteCommandManager.OnRelogRequested += () => OnRelogRequested?.Invoke();
        _remoteCommandManager.OnAutomationStateChanged += () => OnAutomationStateChanged?.Invoke();
        
        _roomGraphManager = new RoomGraphManager();
        _roomGraphManager.OnLogMessage += msg => OnLogMessage?.Invoke(msg);
        _roomGraphManager.LoadFromGameData();
        
        LoadConfigurations();
        LoadSettings();
    }
    
    public IReadOnlyList<BuffConfiguration> BuffConfigurations => _buffConfigurations.AsReadOnly();
    public IReadOnlyList<ActiveBuff> ActiveBuffs => _activeBuffs.AsReadOnly();
    public IReadOnlyList<PartyMember> PartyMembers => _partyMembers.AsReadOnly();
    public PlayerInfo PlayerInfo => _playerInfo;
    
    #region Configuration Management
    
    public void AddBuffConfiguration(BuffConfiguration config)
    {
        _buffConfigurations.Add(config);
        SaveConfigurations();
        OnBuffsChanged?.Invoke();
    }
    
    public void UpdateBuffConfiguration(BuffConfiguration config)
    {
        var index = _buffConfigurations.FindIndex(b => b.Id == config.Id);
        if (index >= 0)
        {
            _buffConfigurations[index] = config;
            SaveConfigurations();
            OnBuffsChanged?.Invoke();
        }
    }
    
    public void RemoveBuffConfiguration(string id)
    {
        _buffConfigurations.RemoveAll(b => b.Id == id);
        SaveConfigurations();
        OnBuffsChanged?.Invoke();
    }
    
    private void LoadConfigurations()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                var configs = JsonSerializer.Deserialize<List<BuffConfiguration>>(json);
                if (configs != null)
                {
                    _buffConfigurations.Clear();
                    _buffConfigurations.AddRange(configs);
                }
            }
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error loading buff configurations: {ex.Message}");
        }
    }
    
    private void SaveConfigurations()
    {
        try
        {
            var directory = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var json = JsonSerializer.Serialize(_buffConfigurations, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error saving buff configurations: {ex.Message}");
        }
    }
    
    public string ExportBuffs()
    {
        var export = new BuffExport
        {
            ExportVersion = "1.0",
            ExportedAt = DateTime.Now,
            Buffs = _buffConfigurations.Select(b => b.Clone()).ToList()
        };
        
        // Generate new IDs on export to avoid conflicts when importing
        foreach (var buff in export.Buffs)
        {
            buff.Id = Guid.NewGuid().ToString();
        }
        
        return JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
    }
    
    public (int imported, int skipped, string message) ImportBuffs(string json, bool replaceExisting)
    {
        try
        {
            var export = JsonSerializer.Deserialize<BuffExport>(json);
            if (export == null || export.Buffs == null)
                return (0, 0, "Invalid buff export file format.");
            
            int imported = 0;
            int skipped = 0;
            
            foreach (var buff in export.Buffs)
            {
                // Check for duplicate by name
                var existing = _buffConfigurations.FirstOrDefault(b => 
                    b.DisplayName.Equals(buff.DisplayName, StringComparison.OrdinalIgnoreCase));
                
                if (existing != null)
                {
                    if (replaceExisting)
                    {
                        buff.Id = existing.Id; // Keep same ID for replacement
                        var index = _buffConfigurations.IndexOf(existing);
                        _buffConfigurations[index] = buff;
                        imported++;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                else
                {
                    buff.Id = Guid.NewGuid().ToString(); // New ID
                    _buffConfigurations.Add(buff);
                    imported++;
                }
            }
            
            if (imported > 0)
            {
                SaveConfigurations();
                OnBuffsChanged?.Invoke();
            }
            
            return (imported, skipped, $"Imported {imported} buff(s), skipped {skipped} duplicate(s).");
        }
        catch (Exception ex)
        {
            return (0, 0, $"Error importing buffs: {ex.Message}");
        }
    }
    
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
                    _parAutoEnabled = settings.ParAutoEnabled;
                    _parFrequencySeconds = settings.ParFrequencySeconds;
                    _parAfterCombatTick = settings.ParAfterCombatTick;
                    _healthRequestEnabled = settings.HealthRequestEnabled;
                    _healthRequestIntervalSeconds = settings.HealthRequestIntervalSeconds;
                    _manaReservePercent = settings.ManaReservePercent;
                    _buffWhileResting = settings.BuffWhileResting;
                    _buffWhileInCombat = settings.BuffWhileInCombat;
                    _autoStartProxy = settings.AutoStartProxy;
                    _combatAutoEnabled = settings.CombatAutoEnabled;
                    _autoLoadLastCharacter = settings.AutoLoadLastCharacter;
                    _lastCharacterPath = settings.LastCharacterPath;
                    _displaySystemLog = settings.DisplaySystemLog;
                    _combatManager.SetCombatEnabledFromSettings(_combatAutoEnabled);
                }
            }
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error loading settings: {ex.Message}");
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
                ParAutoEnabled = _parAutoEnabled,
                ParFrequencySeconds = _parFrequencySeconds,
                ParAfterCombatTick = _parAfterCombatTick,
                HealthRequestEnabled = _healthRequestEnabled,
                HealthRequestIntervalSeconds = _healthRequestIntervalSeconds,
                ManaReservePercent = _manaReservePercent,
                BuffWhileResting = _buffWhileResting,
                BuffWhileInCombat = _buffWhileInCombat,
                AutoStartProxy = _autoStartProxy,
                CombatAutoEnabled = _combatAutoEnabled,
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
            OnLogMessage?.Invoke($"Error saving settings: {ex.Message}");
        }
    }
    
    #endregion
    
    #region Message Processing
    
    public void ProcessMessage(string message)
    {
        // Check for remote commands first (telepath, say, gangpath)
        // This processes commands like @health, @invite, @do, etc.
        _remoteCommandManager.ProcessMessage(message);
        
        // Check for party invitations from other players
        // Format: "PlayerName has invited you to follow him." or "PlayerName has invited you to follow her."
        CheckPartyInvitation(message);
        
        // Check for party membership changes - track _isInParty state
        CheckPartyMembershipChanges(message);
        
        // Check for training/character creation screen
        if (message.Contains("Point Cost Chart"))
        {
            if (!_inTrainingScreen)
            {
                _inTrainingScreen = true;
                OnLogMessage?.Invoke("📋 Training screen detected - pass-through mode enabled");
                OnTrainingScreenChanged?.Invoke(true);
            }
        }
        
        // Check for stat command output
        if (message.Contains("Name:") && message.Contains("Race:") && message.Contains("Class:"))
        {
            ParseStatOutput(message);
        }
        
        // Check for exp command output
        // Format: Exp: 13837284996 Level: 72 Exp needed for next level: 898825529 (14736110525) [93%]
        var expMatch = ExpCommandRegex.Match(message);
        if (expMatch.Success)
        {
            ParseExpCommandOutput(expMatch);
        }
        
        // Check for experience gain
        // Format: You gain 1000 experience.
        var expGainMatch = ExpGainRegex.Match(message);
        if (expGainMatch.Success)
        {
            if (long.TryParse(expGainMatch.Groups[1].Value, out long expGained))
            {
                _experienceTracker.AddExpGain(expGained);
                _playerInfo.TotalExperience += expGained;
                _playerInfo.ExperienceNeededForNextLevel -= expGained;
                // Debug system log message to track exp gains and leveling progress.
                OnLogMessage?.Invoke($"📈 EXP gained: {expGained} | Session total: {_experienceTracker.SessionExpGained} | Rate: {_experienceTracker.GetExpPerHour()}/hr");
                
                // Check for level up (exp needed went negative or zero)
                if (_playerInfo.ExperienceNeededForNextLevel <= 0)
                {
                    OnLogMessage?.Invoke($"🎉 Level up imminent! Sending exp command to refresh...");
                    OnSendCommand?.Invoke("exp");
                }
            }
        }
        
        // Check for party command output
        if (message.Contains("following people are in your travel party") || 
            message.Contains("You are not in a party"))
        {
            try
            {
                ParsePartyOutput(message);
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"⚠️ Error parsing party output: {ex.Message}");
            }
        }
        
        // Check for telepath HP updates from party members
        var telepathMatch = TelepathHpRegex.Match(message);
        if (telepathMatch.Success)
        {
            OnLogMessage?.Invoke($"📡 Telepath match found: {telepathMatch.Value}");
            UpdatePartyMemberHpFromTelepath(telepathMatch);
        }
        else if (message.Contains("telepaths:") && message.Contains("{HP="))
        {
            // Debug: telepath message found but regex didn't match
            OnLogMessage?.Invoke($"⚠️ Telepath message not parsed: {message}");
        }
        
        // Process cure-related messages (ailment detection, telepath requests, cure success)
        _cureManager.ProcessMessage(message, _partyMembers);
        
        // Process player database (parse "who" command output)
        _playerDatabaseManager.ProcessMessage(message);
        
        // Track HP and mana from prompt [HP=X]: or [HP=X/MA=Y]: or [HP=X/KAI=Y]:
        // Note: The HP bar only shows CURRENT values, not max. Max comes from 'stat' command.
        var promptMatch = HpManaPromptRegex.Match(message);
        if (promptMatch.Success)
        {
            // If we see the HP prompt, we're back in the game
            if (_inTrainingScreen)
            {
                _inTrainingScreen = false;
                OnLogMessage?.Invoke("🎮 Returned to game - pass-through mode disabled");
                OnTrainingScreenChanged?.Invoke(false);
            }
            
            // First time seeing HP bar this session - send startup commands
            if (!_hasEnteredGame)
            {
                _hasEnteredGame = true;
                OnLogMessage?.Invoke("🎮 Entered game - sending startup commands");
                
                // Small delay between commands to avoid flooding
                Task.Run(async () =>
                {
                    await Task.Delay(500);
                    OnSendCommand?.Invoke("who");
                    await Task.Delay(500);
                    OnSendCommand?.Invoke("stat");
                    await Task.Delay(500);
                    OnSendCommand?.Invoke("exp");
                });
            }
            
            // Group 1: Current HP
            if (int.TryParse(promptMatch.Groups[1].Value, out int hp))
            {
                _currentHp = hp;
                _playerInfo.CurrentHp = hp;
                
                // If current HP exceeds known max, update max (until stat command provides real value)
                if (_currentHp > _maxHp)
                {
                    _maxHp = _currentHp;
                    _playerInfo.MaxHp = _currentHp;
                }
            }
            
            // Group 2: Mana type (MA or KAI), Group 3: Current Mana
            if (promptMatch.Groups[2].Success)
            {
                _manaType = promptMatch.Groups[2].Value.ToUpperInvariant();
                
                if (int.TryParse(promptMatch.Groups[3].Value, out int mana))
                {
                    _currentMana = mana;
                    _playerInfo.CurrentMana = mana;
                    
                    // If current mana exceeds known max, update max (until stat command provides real value)
                    if (_currentMana > _maxMana)
                    {
                        _maxMana = _currentMana;
                        _playerInfo.MaxMana = _currentMana;
                    }
                }
            }
            
            // Group 4: (Resting) indicator
            bool wasResting = _isResting;
            _isResting = promptMatch.Groups[4].Success;
            
            if (_isResting != wasResting)
            {
                OnLogMessage?.Invoke(_isResting ? "💤 Player is now resting" : "🏃 Player is no longer resting");
            }
        }
        
        // Check for cast failures - block until next tick
        var failMatch = CastFailRegex.Match(message);
        if (failMatch.Success)
        {
            var spellName = failMatch.Groups[1].Value;
            OnLogMessage?.Invoke($"⚠️ Spell failed: {spellName} - blocked until next tick");
            _castBlockedUntilNextTick = true;
            return;
        }
        
        // Check for "not enough mana" - block until next tick
        if (NotEnoughManaRegex.IsMatch(message))
        {
            OnLogMessage?.Invoke($"⚠️ Not enough mana - blocked until next tick");
            _castBlockedUntilNextTick = true;
            return;
        }
        
        // Check for "already cast this round" - block until next tick
        if (AlreadyCastRegex.IsMatch(message))
        {
            OnLogMessage?.Invoke($"⚠️ Already cast this round - blocked until next tick");
            _castBlockedUntilNextTick = true;
            return;
        }
        
        // Check for buff cast success
        foreach (var config in _buffConfigurations)
        {
            // Check self cast message (supports {target} placeholder)
            if (!string.IsNullOrEmpty(config.SelfCastMessage))
            {
                var targetName = TryExtractTargetFromPattern(message, config.SelfCastMessage);
                if (targetName != null)
                {
                    if (IsTargetSelf(targetName))
                    {
                        ActivateBuff(config, string.Empty);
                        continue;
                    }
                }
                // Also try literal match for backwards compatibility
                else if (message.Contains(config.SelfCastMessage, StringComparison.OrdinalIgnoreCase))
                {
                    ActivateBuff(config, string.Empty);
                    continue;
                }
            }
            
            // Check party cast message (supports {target} placeholder)
            if (!string.IsNullOrEmpty(config.PartyCastMessage) && 
                config.TargetType != BuffTargetType.SelfOnly)
            {
                var targetName = TryExtractTargetFromPattern(message, config.PartyCastMessage);
                if (targetName != null)
                {
                    // Check if it's self (player name starts with target, or target starts with player name)
                    // This handles cases like "Azii" matching "Azii RageQuit"
                    if (IsTargetSelf(targetName))
                    {
                        ActivateBuff(config, string.Empty);
                    }
                    else
                    {
                        ActivateBuff(config, targetName);
                    }
                }
            }
            
            // Check expiration
            if (!string.IsNullOrEmpty(config.ExpireMessage) &&
                message.Contains(config.ExpireMessage, StringComparison.OrdinalIgnoreCase))
            {
                ExpireBuff(config);
            }
        }
    }
    
    private string? TryExtractTargetFromPattern(string message, string pattern)
    {
        // Pattern contains {target} placeholder
        // Convert it to a regex to extract the name
        
        if (!pattern.Contains("{target}"))
            return null;
        
        // IMPORTANT: Replace the placeholder BEFORE escaping, 
        // then escape everything else
        var patternWithPlaceholder = pattern.Replace("{target}", "<<<TARGET>>>");
        var escapedPattern = Regex.Escape(patternWithPlaceholder);
        
        // Use appropriate regex based on where {target} appears
        string regexPattern;
        if (pattern.TrimEnd().EndsWith("{target}"))
        {
            // Target is at end - match up to common ending punctuation or end of string
            regexPattern = escapedPattern.Replace("<<<TARGET>>>", @"(.+?)(?:[!\.\,\?\;\:]|$)");
        }
        else
        {
            // Target is in middle - use non-greedy
            regexPattern = escapedPattern.Replace("<<<TARGET>>>", "(.+?)");
        }
        
        try
        {
            var match = Regex.Match(message, regexPattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                // Trim whitespace and common punctuation from the target
                var extracted = match.Groups[1].Value.Trim().TrimEnd('!', '.', ',', '?', ';', ':');
                return extracted;
            }
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"🔍 Regex error: {ex.Message}");
        }
        
        return null;
    }
    
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
        
        // Player name is the first part of target (unlikely but just in case)
        if (targetName.StartsWith(_playerInfo.Name + " ", StringComparison.OrdinalIgnoreCase))
            return true;
        
        // First word of player name matches target
        var playerFirstName = _playerInfo.Name.Split(' ')[0];
        if (targetName.Equals(playerFirstName, StringComparison.OrdinalIgnoreCase))
            return true;
        
        return false;
    }
    
    private void UpdatePartyMemberHpFromTelepath(Match match)
    {
        var name = match.Groups[1].Value;
        var currentHp = int.Parse(match.Groups[2].Value);
        var maxHp = int.Parse(match.Groups[3].Value);
        
        // Mana/Kai is optional (non-mana users like Warriors don't have it)
        int currentMana = 0;
        int maxMana = 0;
        string resourceType = "";
        
        if (match.Groups[4].Success)
        {
            resourceType = match.Groups[4].Value.ToUpperInvariant();  // "MA" or "KAI"
            currentMana = int.Parse(match.Groups[5].Value);
            maxMana = int.Parse(match.Groups[6].Value);
        }
        
        // Find the party member and update their HP
        var member = _partyMembers.FirstOrDefault(p => 
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        
        if (member != null)
        {
            member.CurrentHp = currentHp;
            member.MaxHp = maxHp;
            member.CurrentMana = currentMana;
            member.MaxMana = maxMana;
            member.LastTelepathUpdate = DateTime.Now;
            
            // Set resource type
            if (resourceType == "KAI")
                member.ResourceType = "Kai";
            else if (resourceType == "MA")
                member.ResourceType = "Mana";
            // If no resource type, leave as default or previous value
            
            var hpPercent = maxHp > 0 ? (currentHp * 100 / maxHp) : 100;
            OnLogMessage?.Invoke($"📡 {name} HP: {currentHp}/{maxHp} ({hpPercent}%)");
            OnPartyChanged?.Invoke();
        }
    }
    
    private void ParseStatOutput(string message)
    {
        var nameMatch = StatNameRegex.Match(message);
        var raceMatch = StatRaceRegex.Match(message);
        var classMatch = StatClassRegex.Match(message);
        var levelMatch = StatLevelRegex.Match(message);
        var hitsMatch = StatHitsRegex.Match(message);
        var manaMatch = StatManaRegex.Match(message);
        
        if (nameMatch.Success)
            _playerInfo.Name = nameMatch.Groups[1].Value.Trim();
        if (raceMatch.Success)
            _playerInfo.Race = raceMatch.Groups[1].Value;
        if (classMatch.Success)
            _playerInfo.Class = classMatch.Groups[1].Value;
        if (levelMatch.Success)
            _playerInfo.Level = int.Parse(levelMatch.Groups[1].Value);
        if (hitsMatch.Success)
        {
            _playerInfo.CurrentHp = int.Parse(hitsMatch.Groups[1].Value);
            _playerInfo.MaxHp = int.Parse(hitsMatch.Groups[2].Value);
            _currentHp = _playerInfo.CurrentHp;
            _maxHp = _playerInfo.MaxHp;
        }
        if (manaMatch.Success)
        {
            _playerInfo.CurrentMana = int.Parse(manaMatch.Groups[1].Value);
            _playerInfo.MaxMana = int.Parse(manaMatch.Groups[2].Value);
            _currentMana = _playerInfo.CurrentMana;
            _maxMana = _playerInfo.MaxMana;
        }
        
        OnLogMessage?.Invoke($"📊 Player detected: {_playerInfo.Name} ({_playerInfo.Class} {_playerInfo.Level})");
        _combatManager.CurrentCharacter = _playerInfo.Name;
        OnPlayerInfoChanged?.Invoke();
    }
    
    /// <summary>
    /// Parse the exp command output
    /// Format: Exp: 13837284996 Level: 72 Exp needed for next level: 898825529 (14736110525) [93%]
    /// </summary>
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
        
        OnLogMessage?.Invoke($"📊 EXP: Level {_playerInfo.Level} | " +
            $"Need {ExperienceTracker.FormatNumber(_playerInfo.ExperienceNeededForNextLevel)} | " +
            $"Rate: {ExperienceTracker.FormatNumber(expPerHour)}/hr | " +
            $"ETA: {ExperienceTracker.FormatTimeSpan(timeToLevel)}");
        
        OnPlayerInfoChanged?.Invoke();
    }
    
    private void ParsePartyOutput(string message)
    {
        // Remember previous members and their telepath data
        // Use GroupBy to handle any existing duplicates safely
        var previousMembers = _partyMembers
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key, 
                g => g.First(), 
                StringComparer.OrdinalIgnoreCase);
        
        _partyMembers.Clear();
        
        // Track names we've already added to prevent duplicates
        var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        var matches = PartyMemberRegex.Matches(message);
        foreach (Match match in matches)
        {
            var fullName = match.Groups[1].Value.Trim();
            var firstName = fullName.Split(' ')[0]; // Get first name only for matching
            
            // Skip duplicates
            if (addedNames.Contains(firstName))
            {
                OnLogMessage?.Invoke($"⚠️ Skipping duplicate party member: {firstName}");
                continue;
            }
            addedNames.Add(firstName);
            
            // Group 5 captures the single status indicator: R (resting), P (poisoned), M (meditating), or empty
            var indicator = match.Groups[5].Value;
            var isResting = indicator == "R";
            var isPoisoned = indicator == "P";
            var isMeditating = indicator == "M";
            
            var member = new PartyMember
            {
                Name = firstName,
                FullName = fullName,
                Class = match.Groups[2].Value,
                ManaPercent = match.Groups[3].Success && int.TryParse(match.Groups[3].Value, out int m) ? m : 0,
                HealthPercent = int.TryParse(match.Groups[4].Value, out int h) ? h : 0,
                IsResting = isResting,
                IsPoisoned = isPoisoned,
                IsMeditating = isMeditating,
                Rank = match.Groups[6].Value  // Was Groups[7], now Groups[6] after regex change
            };
            
            // Preserve telepath data if we had it before (MaxHp, MaxMana, ResourceType)
            // But UPDATE CurrentHp/CurrentMana based on fresh percentage from par command
            if (previousMembers.TryGetValue(firstName, out var prevMember))
            {
                member.MaxHp = prevMember.MaxHp;
                member.MaxMana = prevMember.MaxMana;
                member.ResourceType = prevMember.ResourceType;
                member.LastTelepathUpdate = prevMember.LastTelepathUpdate;
                
                // Calculate current values from the fresh percentage if we have max values
                if (prevMember.MaxHp > 0)
                {
                    member.CurrentHp = (member.HealthPercent * prevMember.MaxHp) / 100;
                }
                if (prevMember.MaxMana > 0)
                {
                    member.CurrentMana = (member.ManaPercent * prevMember.MaxMana) / 100;
                }
            }
            
            _partyMembers.Add(member);
            
            var restingIndicator = isResting ? " 💤" : "";
            var poisonIndicator = isPoisoned ? " ☠️" : "";
            var meditatingIndicator = isMeditating ? " 🧘" : "";
            var manaDisplay = member.ManaPercent > 0 ? $" M:{member.ManaPercent}%" : "";
            OnLogMessage?.Invoke($"  👤 {member.Name} ({member.Class}) H:{member.HealthPercent}%{manaDisplay}{restingIndicator}{poisonIndicator}{meditatingIndicator} - {member.Rank}");
        }
        
        OnLogMessage?.Invoke($"👥 Party updated: {_partyMembers.Count} members detected");
        
        // Find members who left the party
        var currentMembers = _partyMembers.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removedMembers = previousMembers.Keys.Where(name => !currentMembers.Contains(name) && !IsTargetSelf(name)).ToList();
        
        // Find NEW members who joined the party (not in previous list)
        var newMembers = _partyMembers
            .Where(m => !IsTargetSelf(m.Name) && !previousMembers.ContainsKey(m.Name))
            .ToList();
        
        // Clear buffs and ailments for removed members
        foreach (var removedName in removedMembers)
        {
            ClearBuffsForTarget(removedName);
            _cureManager.ClearAilmentsForTarget(removedName);
        }
        
        OnPartyChanged?.Invoke();
        
        // Update poison status in CureManager
        _cureManager.UpdatePartyPoisonStatus(_partyMembers);
        
        // If we just joined a party, request health from all members
        if (_requestHealthAfterPartyUpdate)
        {
            _requestHealthAfterPartyUpdate = false;
            RequestPartyMemberHealth();
        }
    }
    
    // Regex for party invitation: "PlayerName has invited you to follow him." or "her."
    private static readonly Regex PartyInviteRegex = new(
        @"(\w+)\s+has invited you to follow (?:him|her)\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    /// <summary>
    /// Check if message contains a party invitation and auto-join if player has that option enabled
    /// </summary>
    private void CheckPartyInvitation(string message)
    {
        var match = PartyInviteRegex.Match(message);
        if (!match.Success)
            return;
        
        var inviterName = match.Groups[1].Value;
        OnLogMessage?.Invoke($"👥 Party invitation received from {inviterName}");
        
        // Check if this player is in our database with JoinPartyIfInvited enabled
        var player = _playerDatabaseManager.GetPlayer(inviterName);
        if (player != null && player.JoinPartyIfInvited)
        {
            OnLogMessage?.Invoke($"👥 Auto-joining {inviterName}'s party");
            OnSendCommand?.Invoke($"join {inviterName}");
        }
    }
    
    /// <summary>
    /// Check for party join/leave messages and update _isInParty state accordingly.
    /// Also sends 'par' command when party membership changes to refresh party list.
    /// </summary>
    private void CheckPartyMembershipChanges(string message)
    {
        // Check if we started following someone (we joined a party)
        if (StartedFollowingRegex.IsMatch(message))
        {
            var match = StartedFollowingRegex.Match(message);
            var leaderName = match.Groups[1].Value;
            _isInParty = true;
            _requestHealthAfterPartyUpdate = true;  // Request health from all members after par populates list
            OnLogMessage?.Invoke($"👥 Joined party - now following {leaderName}");
            OnSendCommand?.Invoke("par");
            return;
        }
        
        // Check if someone started following us (they joined our party)
        if (SomeoneFollowingYouRegex.IsMatch(message))
        {
            var match = SomeoneFollowingYouRegex.Match(message);
            var followerName = match.Groups[1].Value;
            var wasInParty = _isInParty;
            _isInParty = true;
            
            if (!wasInParty)
            {
                OnLogMessage?.Invoke($"👥 Party formed - {followerName} is now following you");
            }
            else
            {
                OnLogMessage?.Invoke($"👥 {followerName} joined the party");
            }
            OnSendCommand?.Invoke("par");
            
            // Request health from the new party member immediately
            RequestHealthFromPlayer(followerName);
            return;
        }
        
        // Check if someone left our party (still in party, just fewer members)
        if (SomeoneLeftPartyRegex.IsMatch(message))
        {
            var match = SomeoneLeftPartyRegex.Match(message);
            var followerName = match.Groups[1].Value;
            OnLogMessage?.Invoke($"👥 {followerName} left the party");
            // Still in a party (unless disbanded message follows)
            OnSendCommand?.Invoke("par");
            return;
        }
        
        // Check if we kicked someone from our party (still in party, just fewer members)
        if (SomeoneRemovedFromPartyRegex.IsMatch(message))
        {
            var match = SomeoneRemovedFromPartyRegex.Match(message);
            var followerName = match.Groups[1].Value;
            OnLogMessage?.Invoke($"👥 {followerName} was removed from the party");
            // Still in a party (unless disbanded message follows)
            OnSendCommand?.Invoke("par");
            return;
        }
        
        // Check if we left someone else's party (now solo)
        if (YouLeftPartyRegex.IsMatch(message))
        {
            var match = YouLeftPartyRegex.Match(message);
            var leaderName = match.Groups[1].Value;
            _isInParty = false;
            _partyMembers.Clear();
            OnPartyChanged?.Invoke();
            OnLogMessage?.Invoke($"👤 Left {leaderName}'s party - now solo");
            return;
        }
        
        // Check if party was disbanded (no longer in a party)
        if (PartyDisbandedRegex.IsMatch(message))
        {
            _isInParty = false;
            _partyMembers.Clear();
            OnPartyChanged?.Invoke();
            OnLogMessage?.Invoke("👤 Party disbanded - now solo");
            return;
        }
        
        // Legacy detection for "You have invited X to follow you" 
        if (message.Contains("You have invited") && message.Contains("to follow you"))
        {
            OnSendCommand?.Invoke("par");
        }
    }
    
    /// <summary>
    /// Check "Also here:" content for players that should be auto-invited
    /// Called from CombatManager when it parses room contents
    /// </summary>
    public void CheckAutoInvitePlayers(IEnumerable<string> playersInRoom)
    {
        foreach (var playerName in playersInRoom)
        {
            // Extract first name (players may be shown as "Name (Class Level)")
            var firstName = playerName.Split(' ')[0].Trim();
            if (firstName.Contains("("))
                firstName = firstName.Split('(')[0].Trim();
            
            var player = _playerDatabaseManager.GetPlayer(firstName);
            if (player != null && player.InviteToPartyIfSeen)
            {
                // Check if already in party
                var inParty = _partyMembers.Any(m => 
                    m.Name.StartsWith(firstName, StringComparison.OrdinalIgnoreCase));
                
                if (!inParty)
                {
                    OnLogMessage?.Invoke($"👥 Auto-inviting {firstName} to party (seen in room)");
                    OnSendCommand?.Invoke($"invite {firstName}");
                    OnSendCommand?.Invoke($"/{firstName} @join");
                }
            }
        }
    }
    
    #endregion
    
    #region Buff Activation/Expiration
    
    private void ActivateBuff(BuffConfiguration config, string targetName)
    {
        // Remove any existing buff of the same type on the same target
        _activeBuffs.RemoveAll(b => 
            b.Configuration.Id == config.Id && 
            b.TargetName.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        
        var activeBuff = new ActiveBuff
        {
            Configuration = config,
            TargetName = targetName,
            CastTime = DateTime.Now,
            ExpireTime = DateTime.Now.AddSeconds(config.DurationSeconds)
        };
        
        _activeBuffs.Add(activeBuff);
        
        var targetDisplay = string.IsNullOrEmpty(targetName) ? "SELF" : targetName;
        OnLogMessage?.Invoke($"✨ Buff activated: {config.DisplayName} on {targetDisplay} ({config.DurationSeconds}s)");
        OnBuffsChanged?.Invoke();
    }
    
    private void ExpireBuff(BuffConfiguration config)
    {
        // For expiration messages, we typically only know it expired on self
        // unless the message contains target name
        var removed = _activeBuffs.RemoveAll(b => 
            b.Configuration.Id == config.Id && b.IsSelfBuff);
        
        if (removed > 0)
        {
            OnLogMessage?.Invoke($"⏰ Buff expired: {config.DisplayName}");
            OnBuffsChanged?.Invoke();
        }
    }
    
    public void RemoveExpiredBuffs()
    {
        var expiredCount = _activeBuffs.RemoveAll(b => b.IsExpired);
        if (expiredCount > 0)
        {
            OnBuffsChanged?.Invoke();
        }
    }
    
    public void ClearAllActiveBuffs()
    {
        _activeBuffs.Clear();
        OnBuffsChanged?.Invoke();
    }
    
    /// <summary>
    /// Called when disconnected from server - resets session state
    /// </summary>
    public void OnDisconnected()
    {
        _hasEnteredGame = false;
        _inTrainingScreen = false;
        _inCombat = false;
        _isResting = false;
        _isInParty = false;
        _requestHealthAfterPartyUpdate = false;
        _partyMembers.Clear();
        _experienceTracker.Reset();
        OnPartyChanged?.Invoke();
        OnLogMessage?.Invoke("📡 Disconnected - session state reset");
    }
    
    /// <summary>
    /// Clear buffs for a specific target (used when they leave the party)
    /// </summary>
    public void ClearBuffsForTarget(string targetName)
    {
        var removed = _activeBuffs.RemoveAll(b => 
            !b.IsSelfBuff && b.TargetName.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        
        if (removed > 0)
        {
            OnLogMessage?.Invoke($"🧹 Cleared {removed} buff(s) for {targetName} (left party)");
            OnBuffsChanged?.Invoke();
        }
    }
    
    #endregion
    
    #region Queries
    
    public IEnumerable<ActiveBuff> GetSelfBuffs()
    {
        return _activeBuffs.Where(b => b.IsSelfBuff).OrderBy(b => b.TimeRemaining);
    }
    
    public IEnumerable<ActiveBuff> GetPartyBuffs()
    {
        return _activeBuffs.Where(b => !b.IsSelfBuff).OrderBy(b => b.TargetName).ThenBy(b => b.TimeRemaining);
    }
    
    public IEnumerable<ActiveBuff> GetBuffsForTarget(string targetName)
    {
        return _activeBuffs.Where(b => 
            b.TargetName.Equals(targetName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(b => b.TimeRemaining);
    }
    
    public IEnumerable<PartyMember> GetMeleePartyMembers()
    {
        return _partyMembers.Where(p => p.IsMelee && !IsTargetSelf(p.Name));
    }
    
    public IEnumerable<PartyMember> GetCasterPartyMembers()
    {
        return _partyMembers.Where(p => p.IsCaster && !IsTargetSelf(p.Name));
    }
    
    public IEnumerable<PartyMember> GetAllPartyMembers()
    {
        return _partyMembers.Where(p => !IsTargetSelf(p.Name));
    }
    
    #endregion
    
    #region Auto-Recast System
    
    private bool _autoRecastEnabled = true;
    private DateTime _lastRecastAttempt = DateTime.MinValue;
    private DateTime _lastCastCommandSent = DateTime.MinValue;
    private const int MIN_RECAST_INTERVAL_MS = 500; // Don't spam recasts
    private const int CAST_COOLDOWN_MS = 5500; // Slightly longer than tick interval for safety
    
    public bool AutoRecastEnabled 
    { 
        get => _autoRecastEnabled; 
        set => _autoRecastEnabled = value; 
    }
    
    /// <summary>
    /// Called after a combat tick to check if healing, cures, or buffs are needed.
    /// This is the optimal time to cast (mid-round window).
    /// Uses configurable priority order.
    /// </summary>
    public void CheckAutoRecast()
    {
        // If commands are paused (training screen or manual), don't do anything
        if (ShouldPauseCommands)
        {
            return;
        }
        
        // Note: AutoRecastEnabled only controls BUFFS, not heals/cures
        // We still need to process heals and cures even if buff auto-recast is disabled
        
        // If we're blocked from casting due to failure/already cast, don't try
        if (_castBlockedUntilNextTick)
        {
            OnLogMessage?.Invoke("⏸️ Cast blocked until next tick");
            return;
        }
        
        // If we recently sent a cast command, wait for the cooldown
        // This prevents sending multiple commands before server responds
        var timeSinceLastCast = (DateTime.Now - _lastCastCommandSent).TotalMilliseconds;
        if (timeSinceLastCast < CAST_COOLDOWN_MS)
        {
            return;
        }
        
        // Don't spam recasts
        var timeSinceLastRecast = (DateTime.Now - _lastRecastAttempt).TotalMilliseconds;
        if (timeSinceLastRecast < MIN_RECAST_INTERVAL_MS)
            return;
        
        // NOTE: Mana reserve check is NOT done here - it only applies to buffs
        // Heals and cures should always attempt to cast if we have enough mana for the spell
        
        // Process by priority order
        foreach (var priorityType in _cureManager.PriorityOrder)
        {
            var result = TryCastByPriority(priorityType);
            if (result)
            {
                return; // Successfully cast something
            }
        }
    }
    
    /// <summary>
    /// Check if it's time to send a 'par' command and send it if so.
    /// Call this periodically (e.g., every second).
    /// Only sends if we're actually in a party.
    /// </summary>
    public void CheckParCommand()
    {
        if (!_parAutoEnabled) return;
        if (!_isInParty) return;  // Don't send par if not in a party
        if (ShouldPauseCommands) return;
        
        var timeSinceLastPar = (DateTime.Now - _lastParSent).TotalSeconds;
        if (timeSinceLastPar >= _parFrequencySeconds)
        {
            SendParCommand();
        }
    }
    
    /// <summary>
    /// Called when a combat tick is detected. Can trigger par command if configured.
    /// </summary>
    public void OnCombatTick()
    {
        // Unblock casting for next round
        _castBlockedUntilNextTick = false;
        _lastCastCommandSent = DateTime.MinValue; // Reset cooldown on tick
        
        // Don't send commands if paused
        if (ShouldPauseCommands) return;
        
        // Send par after combat tick if configured (only if in a party)
        // Check that we haven't sent par in the last 2 seconds to prevent duplicates
        if (_parAfterCombatTick && _parAutoEnabled && _isInParty)
        {
            var timeSinceLastPar = (DateTime.Now - _lastParSent).TotalSeconds;
            if (timeSinceLastPar >= 2.0)
            {
                SendParCommand();
            }
        }
    }
    
    private void SendParCommand()
    {
        _lastParSent = DateTime.Now;
        OnSendCommand?.Invoke("par");
        OnLogMessage?.Invoke("📋 Auto-sending 'par' command");
    }
    
    /// <summary>
    /// Check if any party members are missing health data and request it.
    /// Call this periodically (e.g., every second).
    /// </summary>
    public void CheckHealthRequests()
    {
        if (!_healthRequestEnabled) return;
        if (ShouldPauseCommands) return;
        
        var timeSinceLastCheck = (DateTime.Now - _lastHealthRequestCheck).TotalSeconds;
        if (timeSinceLastCheck < _healthRequestIntervalSeconds) return;
        
        _lastHealthRequestCheck = DateTime.Now;
        
        // Find party members without actual HP data
        foreach (var member in _partyMembers)
        {
            if (IsTargetSelf(member.Name)) continue;
            
            // If we don't have actual HP data (MaxHp is 0), request it
            if (member.MaxHp == 0)
            {
                var command = $"/{member.Name} @health";
                OnSendCommand?.Invoke(command);
                OnLogMessage?.Invoke($"📡 Requesting health from {member.Name}");
                
                // Only request from one member per interval to avoid spam
                return;
            }
        }
    }
    
    /// <summary>
    /// Request health data from a specific player immediately.
    /// Used when joining a party or when a new member joins.
    /// </summary>
    private void RequestHealthFromPlayer(string playerName)
    {
        if (ShouldPauseCommands) return;
        if (IsTargetSelf(playerName)) return;
        
        var command = $"/{playerName} @health";
        OnSendCommand?.Invoke(command);
        OnLogMessage?.Invoke($"📡 Requesting health from {playerName}");
    }
    
    /// <summary>
    /// Request health data from all current party members.
    /// Used when joining an existing party.
    /// </summary>
    private void RequestHealthFromAllPartyMembers()
    {
        if (ShouldPauseCommands) return;
        
        foreach (var member in _partyMembers)
        {
            if (IsTargetSelf(member.Name)) continue;
            
            var command = $"/{member.Name} @health";
            OnSendCommand?.Invoke(command);
        }
        
        if (_partyMembers.Count(m => !IsTargetSelf(m.Name)) > 0)
        {
            OnLogMessage?.Invoke($"📡 Requesting health from all party members");
        }
    }
    
    private void RequestPartyMemberHealth()
    {
        foreach (var member in _partyMembers)
        {
            if (!IsTargetSelf(member.Name))
            {
                RequestHealthFromPlayer(member.Name);
            }
        }
    }

    private bool TryCastByPriority(CastPriorityType priorityType)
    {
        switch (priorityType)
        {
            case CastPriorityType.Heals:
                return TryCastHeal();
            
            case CastPriorityType.Cures:
                return TryCastCure();
            
            case CastPriorityType.Buffs:
                return TryCastBuff();
            
            default:
                return false;
        }
    }
    
    private bool TryCastHeal()
    {
        if (!_healingManager.HealingEnabled) return false;
        
        var healResult = _healingManager.CheckHealing();
        if (healResult.HasValue && healResult.Value.Command != null)
        {
            _lastRecastAttempt = DateTime.Now;
            _lastCastCommandSent = DateTime.Now;
            OnLogMessage?.Invoke($"💚 Auto-healing: {healResult.Value.Description}");
            OnSendCommand?.Invoke(healResult.Value.Command);
            return true;
        }
        
        return false;
    }
    
    private bool TryCastCure()
    {
        if (!_cureManager.CuringEnabled) return false;
        
        var cureResult = _cureManager.CheckCuring();
        if (cureResult.HasValue && cureResult.Value.Command != null)
        {
            _lastRecastAttempt = DateTime.Now;
            _lastCastCommandSent = DateTime.Now;
            OnLogMessage?.Invoke($"💊 Auto-curing: {cureResult.Value.Description}");
            OnSendCommand?.Invoke(cureResult.Value.Command);
            
            // Mark the ailment as having a cure initiated to prevent duplicate casts
            if (cureResult.Value.Ailment != null)
            {
                _cureManager.MarkCureInitiated(cureResult.Value.Ailment);
            }
            
            return true;
        }
        return false;
    }
    
    private bool TryCastBuff()
    {
        // Check if buff auto-recast is enabled
        if (!_autoRecastEnabled) return false;
        
        // Check if we should buff in current state
        if (_isResting && !_buffWhileResting)
        {
            // Don't log every time to avoid spam - only when there are buffs to cast
            return false;
        }
        
        if (_inCombat && !_buffWhileInCombat)
        {
            // Don't log every time to avoid spam - only when there are buffs to cast
            return false;
        }
        
        // Get buffs that need recasting, sorted by priority
        var buffsToRecast = GetBuffsNeedingRecast().ToList();;
        
        if (buffsToRecast.Count == 0) return false;
        
        // Check mana reserve before attempting any buff
        if (_maxMana > 0)
        {
            var currentManaPercent = (_currentMana * 100) / _maxMana;
            if (currentManaPercent < _manaReservePercent)
            {
                OnLogMessage?.Invoke($"⏸️ Buff skipped: mana {currentManaPercent}% < {_manaReservePercent}% reserve");
                return false;
            }
        }
        
        // Find a buff we can afford to cast
        BuffConfiguration? configToCast = null;
        string targetToCast = string.Empty;
        
        foreach (var (config, target) in buffsToRecast)
        {
            // Check if we have enough mana for this spell
            if (config.ManaCost > 0 && _currentMana < config.ManaCost)
            {
                OnLogMessage?.Invoke($"⏸️ Buff {config.DisplayName} skipped: need {config.ManaCost} mana, have {_currentMana}");
                continue; // Skip, can't afford
            }
            
            // Also check that casting won't put us below reserve
            if (_maxMana > 0 && config.ManaCost > 0)
            {
                var manaAfterCast = _currentMana - config.ManaCost;
                var manaPercentAfter = (manaAfterCast * 100) / _maxMana;
                if (manaPercentAfter < _manaReservePercent)
                {
                    OnLogMessage?.Invoke($"⏸️ Buff {config.DisplayName} skipped: would drop mana to {manaPercentAfter}% (reserve: {_manaReservePercent}%)");
                    continue; // Skip, would put us below reserve
                }
            }
            
            configToCast = config;
            targetToCast = target;
            break;
        }
        
        if (configToCast == null)
        {
            return false; // Nothing we can afford to cast
        }
        
        _lastRecastAttempt = DateTime.Now;
        _lastCastCommandSent = DateTime.Now; // Block further casts until cooldown
        
        // Determine the command to send
        string command = configToCast.Command;
        
        // If it's a party buff and has a target, add the target name
        if (!string.IsNullOrEmpty(targetToCast))
        {
            command = $"{command} {targetToCast}";
        }
        
        OnLogMessage?.Invoke($"🔄 Auto-recasting: {configToCast.DisplayName}" + 
            (string.IsNullOrEmpty(targetToCast) ? "" : $" on {targetToCast}") +
            $" (cost: {configToCast.ManaCost}, have: {_currentMana})");
        OnSendCommand?.Invoke(command);
        return true;
    }
    
    /// <summary>
    /// Called when a successful cast is detected - resets cooldown to allow next cast after tick
    /// </summary>
    public void OnSuccessfulCast()
    {
        // Keep the cooldown timer running - we still need to wait for next tick
    }
    
    /// <summary>
    /// Gets all buffs that need to be recast, sorted by priority.
    /// </summary>
    private IEnumerable<(BuffConfiguration Config, string Target)> GetBuffsNeedingRecast()
    {
        var results = new List<(BuffConfiguration Config, string Target, int Priority)>();
        
        foreach (var config in _buffConfigurations.Where(c => c.AutoRecast))
        {
            // Check if we should maintain this buff on self
            bool shouldBuffSelf = config.TargetType == BuffTargetType.SelfOnly || 
                config.TargetType == BuffTargetType.AllParty ||
                (config.TargetType == BuffTargetType.MeleeParty && _playerInfo.IsMelee) ||
                (config.TargetType == BuffTargetType.CasterParty && _playerInfo.IsCaster);
            
            // Also check if we already have this buff active on self (user manually cast it)
            var existingSelfBuff = _activeBuffs.FirstOrDefault(b => 
                b.Configuration.Id == config.Id && b.IsSelfBuff);
            
            if (shouldBuffSelf || existingSelfBuff != null)
            {
                if (NeedsRecast(config, string.Empty))
                {
                    results.Add((config, string.Empty, config.Priority));
                }
            }
            
            // Check party buffs
            if (config.TargetType != BuffTargetType.SelfOnly)
            {
                var partyTargets = GetPartyTargetsForBuff(config);
                foreach (var target in partyTargets)
                {
                    if (NeedsRecast(config, target.Name))
                    {
                        results.Add((config, target.Name, config.Priority));
                    }
                }
                
                // Also check any party members we already have this buff active on
                // (even if they don't match the target type - user manually cast it)
                var existingPartyBuffs = _activeBuffs.Where(b => 
                    b.Configuration.Id == config.Id && !b.IsSelfBuff);
                
                foreach (var existingBuff in existingPartyBuffs)
                {
                    // Skip if already in results
                    if (results.Any(r => r.Config.Id == config.Id && 
                        r.Target.Equals(existingBuff.TargetName, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    
                    if (NeedsRecast(config, existingBuff.TargetName))
                    {
                        results.Add((config, existingBuff.TargetName, config.Priority));
                    }
                }
            }
        }
        
        // Sort by priority (lower number = higher priority)
        return results
            .OrderBy(r => r.Priority)
            .ThenBy(r => string.IsNullOrEmpty(r.Target) ? 0 : 1) // Self first
            .Select(r => (r.Config, r.Target));
    }
    
    /// <summary>
    /// Check if a specific buff on a specific target needs recasting.
    /// </summary>
    private bool NeedsRecast(BuffConfiguration config, string target)
    {
        var activeBuff = _activeBuffs.FirstOrDefault(b => 
            b.Configuration.Id == config.Id && 
            b.TargetName.Equals(target, StringComparison.OrdinalIgnoreCase));
        
        if (activeBuff == null)
        {
            // Buff is not active at all - needs recast
            return true;
        }
        
        // Check if within recast buffer window
        if (config.RecastBufferSeconds > 0)
        {
            return activeBuff.TimeRemaining.TotalSeconds <= config.RecastBufferSeconds;
        }
        
        // If buffer is 0, only recast when fully expired
        return activeBuff.IsExpired;
    }
    
    /// <summary>
    /// Get party members that should receive this buff based on target type.
    /// </summary>
    private IEnumerable<PartyMember> GetPartyTargetsForBuff(BuffConfiguration config)
    {
        return config.TargetType switch
        {
            BuffTargetType.MeleeParty => GetMeleePartyMembers(),
            BuffTargetType.CasterParty => GetCasterPartyMembers(),
            BuffTargetType.AllParty => GetAllPartyMembers(),
            _ => Enumerable.Empty<PartyMember>()
        };
    }
    
    #endregion
    
    #region Character Profile Management
    
    public string CharacterProfilesPath => _characterProfilesPath;
    public string CurrentProfilePath => _currentProfilePath;
    public bool HasUnsavedChanges { get; private set; } = false;
    
    /// <summary>
    /// Get the default filename for a character profile
    /// </summary>
    public string GetDefaultProfileFilename()
    {
        if (!string.IsNullOrEmpty(_playerInfo.Name))
        {
            // Sanitize the name for use as a filename
            var safeName = string.Join("_", _playerInfo.Name.Split(Path.GetInvalidFileNameChars()));
            return $"{safeName}.json";
        }
        return "character.json";
    }
    
    /// <summary>
    /// Update BBS/Telnet settings
    /// </summary>
    public void UpdateBbsSettings(BbsSettings settings)
    {
        _bbsSettings = settings.Clone();
        HasUnsavedChanges = true;
        OnBbsSettingsChanged?.Invoke();
        OnLogMessage?.Invoke("📡 BBS settings updated");
    }
    
    /// <summary>
    /// Auto-save character profile when data changes (called by manager events)
    /// </summary>
    private void AutoSaveCharacterProfile()
    {
        if (!string.IsNullOrEmpty(_currentProfilePath))
        {
            var (success, message) = SaveCharacterProfile(_currentProfilePath);
            if (!success)
            {
                OnLogMessage?.Invoke($"⚠️ Auto-save failed: {message}");
            }
        }
    }
    
    /// <summary>
    /// Save the current character profile to a file
    /// </summary>
    public (bool success, string message) SaveCharacterProfile(string filePath)
    {
        try
        {
            var profile = new CharacterProfile
            {
                ProfileVersion = "1.0",
                SavedAt = DateTime.Now,
                
                // Character Info
                CharacterName = _playerInfo.Name,
                CharacterClass = _playerInfo.Class,
                CharacterLevel = _playerInfo.Level,
                
                // BBS/Telnet Settings
                BbsSettings = _bbsSettings.Clone(),
                
                // Combat Settings
                CombatSettings = _combatManager.GetCurrentSettings(),
                
                // Buff Configurations
                Buffs = _buffConfigurations.Select(b => b.Clone()).ToList(),
                
                // Heal Configurations
                HealSpells = _healingManager.Configuration.HealSpells.Select(h => h.Clone()).ToList(),
                SelfHealRules = _healingManager.Configuration.SelfHealRules.Select(r => r.Clone()).ToList(),
                PartyHealRules = _healingManager.Configuration.PartyHealRules.Select(r => r.Clone()).ToList(),
                PartyWideHealRules = _healingManager.Configuration.PartyWideHealRules.Select(r => r.Clone()).ToList(),
                
                // Cure Configurations
                Ailments = _cureManager.Configuration.Ailments.Select(a => a.Clone()).ToList(),
                CureSpells = _cureManager.Configuration.CureSpells.Select(c => c.Clone()).ToList(),
                
                // Monster Overrides
                MonsterOverrides = _monsterDatabaseManager.GetOverridesForProfile(),
                
                // Player Database
                Players = _playerDatabaseManager.GetPlayersForProfile(),
                
                // Window Settings
                WindowSettings = _windowSettings
            };
            
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
            
            _currentProfilePath = filePath;
            HasUnsavedChanges = false;
            
            OnLogMessage?.Invoke($"💾 Character profile saved: {Path.GetFileName(filePath)}");
            return (true, $"Character profile saved successfully.");
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error saving character profile: {ex.Message}");
            return (false, $"Error saving character profile: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Load a character profile from a file
    /// </summary>
    public (bool success, string message) LoadCharacterProfile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return (false, "Character profile file not found.");
            }
            
            var json = File.ReadAllText(filePath);
            var profile = JsonSerializer.Deserialize<CharacterProfile>(json);
            
            if (profile == null)
            {
                return (false, "Invalid character profile format.");
            }
            
            // Load Combat Settings
            _combatManager.LoadFromProfile(profile.CombatSettings);
            if (!string.IsNullOrEmpty(profile.CharacterName))
            {
                _combatManager.CurrentCharacter = profile.CharacterName;
            }
            
            // Load BBS/Telnet Settings
            if (profile.BbsSettings != null)
            {
                _bbsSettings = profile.BbsSettings.Clone();
                OnBbsSettingsChanged?.Invoke();
            }
            
            // Load Buff Configurations
            if (profile.Buffs != null && profile.Buffs.Count > 0)
            {
                _buffConfigurations.Clear();
                _buffConfigurations.AddRange(profile.Buffs);
                SaveConfigurations();
                OnBuffsChanged?.Invoke();
            }
            
            // Load Heal Configurations - use the configuration object directly
            if (profile.HealSpells != null && profile.HealSpells.Count > 0)
            {
                var healConfig = new HealingConfiguration
                {
                    HealingEnabled = _healingManager.HealingEnabled,
                    HealSpells = profile.HealSpells,
                    SelfHealRules = profile.SelfHealRules ?? new List<HealRule>(),
                    PartyHealRules = profile.PartyHealRules ?? new List<HealRule>(),
                    PartyWideHealRules = profile.PartyWideHealRules ?? new List<HealRule>()
                };
                _healingManager.ReplaceConfiguration(healConfig);
            }
            
            // Load Cure Configurations - use the configuration object directly
            if (profile.Ailments != null && profile.Ailments.Count > 0)
            {
                var cureConfig = new CureConfiguration
                {
                    CuringEnabled = _cureManager.CuringEnabled,
                    Ailments = profile.Ailments,
                    CureSpells = profile.CureSpells ?? new List<CureSpellConfiguration>(),
                    PriorityOrder = _cureManager.PriorityOrder
                };
                _cureManager.ReplaceConfiguration(cureConfig);
            }
            
            // Load Monster Overrides
            _monsterDatabaseManager.LoadOverridesFromProfile(profile.MonsterOverrides);
            
            // Load Player Database
            _playerDatabaseManager.LoadFromProfile(profile.Players);
            
            // Update player info if provided
            if (!string.IsNullOrEmpty(profile.CharacterName))
            {
                _playerInfo.Name = profile.CharacterName;
                _playerInfo.Class = profile.CharacterClass;
                _playerInfo.Level = profile.CharacterLevel;
                OnPlayerInfoChanged?.Invoke();
            }
            
            // Load Window Settings
            _windowSettings = profile.WindowSettings;
            
            _currentProfilePath = filePath;
            HasUnsavedChanges = false;
            
            // Remember this as the last loaded character
            _lastCharacterPath = filePath;
            SaveSettings();
            
            OnLogMessage?.Invoke($"📂 Character profile loaded: {Path.GetFileName(filePath)}");
            return (true, $"Character profile '{profile.CharacterName}' loaded successfully.");
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error loading character profile: {ex.Message}");
            return (false, $"Error loading character profile: {ex.Message}");
        }
    }
    
    #endregion
}
