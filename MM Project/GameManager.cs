namespace MudProxyViewer;

/// <summary>
/// Central coordinator for the MUD proxy application.
/// Owns all sub-managers, handles profile management, and coordinates game state.
/// Extracted from BuffManager to separate coordination from buff logic.
/// </summary>
public class GameManager
{
    // Sub-managers
    private readonly PlayerStateManager _playerStateManager;
    private readonly PartyManager _partyManager;
    private readonly HealingManager _healingManager;
    private readonly CureManager _cureManager;
    private readonly PlayerDatabaseManager _playerDatabaseManager;
    private readonly MonsterDatabaseManager _monsterDatabaseManager;
    private readonly CombatManager _combatManager;
    private readonly RemoteCommandManager _remoteCommandManager;
    private readonly RoomGraphManager _roomGraphManager;
    private readonly RoomTracker _roomTracker;
    private readonly AppSettings _appSettings;
    private readonly ProfileManager _profileManager;
    private readonly BuffManager _buffManager;
    private readonly CastCoordinator _castCoordinator;
    
    // Events for UI updates
    public event Action? OnBuffsChanged;
    public event Action? OnPartyChanged;
    public event Action? OnPlayerInfoChanged;
    public event Action<string>? OnLogMessage;
    public event Action<string>? OnRoomTrackerLogMessage;
    public event Action<string>? OnSendCommand;
    public event Action? OnHangupRequested;
    public event Action? OnRelogRequested;
    public event Action? OnAutomationStateChanged;
    public event Action<bool>? OnTrainingScreenChanged;
    public event Action? OnBbsSettingsChanged;
    
    // Coordinator state
    private bool _commandsPaused = false;
    private bool _combatAutoEnabled = true;
    
    // Profile data (loaded from character profile)
    private BbsSettings _bbsSettings = new();
    private WindowSettings? _windowSettings;
    
    #region Properties
    
    // Command pausing
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

    public bool ShouldPauseCommands => _playerStateManager.InTrainingScreen || _playerStateManager.IsExiting || _commandsPaused || _playerStateManager.IsInLoginPhase;

    public bool CombatAutoEnabled
    {
        get => _combatAutoEnabled;
        set 
        { 
            if (_combatAutoEnabled != value)
            {
                _combatAutoEnabled = value;
                _combatManager.CombatEnabled = value;
            }
        }
    }
    
    // App settings (persisted in settings.json, not character profile)
    public AppSettings AppSettings => _appSettings;
    
    // Sub-manager accessors
    public PlayerStateManager PlayerStateManager => _playerStateManager;
    public PartyManager PartyManager => _partyManager;
    public HealingManager HealingManager => _healingManager;
    public CureManager CureManager => _cureManager;
    public PlayerDatabaseManager PlayerDatabase => _playerDatabaseManager;
    public MonsterDatabaseManager MonsterDatabase => _monsterDatabaseManager;
    public CombatManager CombatManager => _combatManager;
    public RemoteCommandManager RemoteCommandManager => _remoteCommandManager;
    public RoomGraphManager RoomGraph => _roomGraphManager;
    public RoomTracker RoomTracker => _roomTracker;
    public ProfileManager ProfileManager => _profileManager;
    public BuffManager BuffManager => _buffManager;
    public CastCoordinator CastCoordinator => _castCoordinator;
    
    /// <summary>
    /// Public helper for MessageRouter to raise log messages.
    /// </summary>
    public void LogMessage(string message) => OnLogMessage?.Invoke(message);
    
    /// <summary>
    /// Public helper for MessageRouter to send commands.
    /// </summary>
    public void SendCommand(string command) => OnSendCommand?.Invoke(command);
    
    // Profile data
    public BbsSettings BbsSettings => _bbsSettings;
    
    public WindowSettings? WindowSettings
    {
        get => _windowSettings;
        set => _windowSettings = value;
    }
    
    #endregion
    
    #region Constructor
    
    public GameManager()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MudProxyViewer");
        
        _appSettings = new AppSettings(appDataPath);
        _appSettings.Load();
        
        _profileManager = new ProfileManager(appDataPath, msg => OnLogMessage?.Invoke(msg));
        
        // Create PlayerStateManager first (others need player info)
        _playerStateManager = new PlayerStateManager(
            msg => OnLogMessage?.Invoke(msg),
            cmd => OnSendCommand?.Invoke(cmd)
        );
        _playerStateManager.OnPlayerInfoChanged += () => OnPlayerInfoChanged?.Invoke();
        _playerStateManager.OnTrainingScreenChanged += entered => OnTrainingScreenChanged?.Invoke(entered);
        _playerStateManager.OnGameExited += () => _partyManager?.Clear();
        
        // Create PlayerDatabaseManager (PartyManager needs it)
        _playerDatabaseManager = new PlayerDatabaseManager();
        _playerDatabaseManager.OnLogMessage += msg => OnLogMessage?.Invoke(msg);
        
        // Create PartyManager
        _partyManager = new PartyManager(
            () => ShouldPauseCommands,
            _playerStateManager.IsTargetSelf,
            () => _playerDatabaseManager,
            cmd => OnSendCommand?.Invoke(cmd),
            msg => OnLogMessage?.Invoke(msg)
        );
        _partyManager.OnPartyChanged += () => OnPartyChanged?.Invoke();
        
        _healingManager = new HealingManager(
            () => _playerStateManager.PlayerInfo,
            () => _partyManager.PartyMembers,
            () => _playerStateManager.CurrentMana,
            () => _playerStateManager.MaxMana,
            _playerStateManager.IsTargetSelf,
            () => _playerStateManager.IsResting,
            () => _playerStateManager.InCombat
        );
        _healingManager.OnLogMessage += msg => OnLogMessage?.Invoke(msg);
        
        _cureManager = new CureManager(
            () => _playerStateManager.PlayerInfo,
            () => _partyManager.PartyMembers,
            () => _playerStateManager.CurrentMana,
            () => _playerStateManager.MaxMana,
            () => _buffManager?.ManaReservePercent ?? 20,
            _playerStateManager.IsTargetSelf
        );
        _cureManager.OnLogMessage += msg => OnLogMessage?.Invoke(msg);
        
        _monsterDatabaseManager = new MonsterDatabaseManager();
        _monsterDatabaseManager.OnLogMessage += msg => OnLogMessage?.Invoke(msg);
        
        _combatManager = new CombatManager();
        _combatManager.OnLogMessage += msg => OnLogMessage?.Invoke(msg);
        _combatManager.OnCombatEnabledChanged += () => 
        { 
            _combatAutoEnabled = _combatManager.CombatEnabled;
        };
        _combatManager.OnPlayersDetected += _partyManager.CheckAutoInvitePlayers;
        
        _remoteCommandManager = new RemoteCommandManager(
            _playerDatabaseManager,
            () => _playerStateManager.CurrentHp,
            () => _playerStateManager.MaxHp,
            () => _playerStateManager.CurrentMana,
            () => _playerStateManager.MaxMana,
            () => _playerStateManager.ManaType,
            () => _combatManager.CombatEnabled,
            enabled => _combatManager.CombatEnabled = enabled,
            () => _healingManager.HealingEnabled,
            enabled => _healingManager.HealingEnabled = enabled,
            () => _cureManager.CuringEnabled,
            enabled => _cureManager.CuringEnabled = enabled,
            () => _buffManager?.AutoRecastEnabled ?? true,
            enabled => { if (_buffManager != null) _buffManager.AutoRecastEnabled = enabled; },
            () => _playerStateManager.PlayerInfo.Level,
            () => _playerStateManager.PlayerInfo.ExperienceNeededForNextLevel,
            () => _playerStateManager.ExperienceTracker.GetExpPerHour(),
            () => ExperienceTracker.FormatTimeSpan(
                _playerStateManager.ExperienceTracker.EstimateTimeToExp(_playerStateManager.PlayerInfo.ExperienceNeededForNextLevel),
                _playerStateManager.PlayerInfo.ExperienceNeededForNextLevel <= 0),
            () => _playerStateManager.ExperienceTracker.SessionExpGained,
            () => _playerStateManager.ExperienceTracker.Reset()
        );
        _remoteCommandManager.OnLogMessage += msg => OnLogMessage?.Invoke(msg);
        _remoteCommandManager.OnSendCommand += cmd => OnSendCommand?.Invoke(cmd);
        _remoteCommandManager.OnHangupRequested += () => OnHangupRequested?.Invoke();
        _remoteCommandManager.OnRelogRequested += () => OnRelogRequested?.Invoke();
        _remoteCommandManager.OnAutomationStateChanged += () => OnAutomationStateChanged?.Invoke();
        _remoteCommandManager.SetPartyMemberCheck(name => _partyManager.PartyMembers.Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        
        _roomGraphManager = new RoomGraphManager();
        _roomGraphManager.OnLogMessage += msg => OnLogMessage?.Invoke(msg);
        _roomGraphManager.LoadFromGameData();
        _roomTracker = new RoomTracker(_roomGraphManager);
        _roomTracker.OnLogMessage += msg => OnRoomTrackerLogMessage?.Invoke(msg);
        
        // Create BuffManager — buff config CRUD, active tracking, recast eligibility
        _buffManager = new BuffManager(
            appDataPath,
            _playerStateManager,
            _partyManager,
            msg => OnLogMessage?.Invoke(msg)
        );
        _buffManager.OnBuffsChanged += () => OnBuffsChanged?.Invoke();
        
        // Create CastCoordinator — priority-based casting across heals, cures, buffs
        _castCoordinator = new CastCoordinator(
            () => ShouldPauseCommands,
            _healingManager,
            _cureManager,
            _buffManager,
            cmd => OnSendCommand?.Invoke(cmd),
            msg => OnLogMessage?.Invoke(msg)
        );
        
        // Wire cast failure detection: BuffManager delegates to CastCoordinator
        _buffManager.SetCastFailureHandler(_castCoordinator.ProcessCastFailures);
        
        // Wire party events that need BuffManager
        _partyManager.OnPartyMembersRemoved += removedNames =>
        {
            foreach (var name in removedNames)
            {
                _buffManager.ClearBuffsForTarget(name);
                _cureManager.ClearAilmentsForTarget(name);
            }
        };
        _partyManager.OnPartyUpdated += partyMembers =>
        {
            _cureManager.UpdatePartyPoisonStatus(partyMembers.ToList());
        };
        
        // Ensure all automation toggles default to ON on fresh launch.
        // Individual config files (cures.json, heals.json) may have persisted
        // an OFF state from a previous session — override that here.
        // Loading a profile also forces all toggles ON.
        _combatAutoEnabled = true;
        _combatManager.SetCombatEnabledFromSettings(true);
        _buffManager.AutoRecastEnabled = true;
        _healingManager.HealingEnabled = true;
        _cureManager.CuringEnabled = true;
    }
    
    #endregion
    
    #region Game State Coordination
    
    /// <summary>
    /// Called when disconnected from server. Resets all session state.
    /// </summary>
    public void OnDisconnected()
    {
        _playerStateManager.OnDisconnected();
        _partyManager.OnDisconnected();
        OnLogMessage?.Invoke("📡 Disconnected - session state reset");
    }
    
    /// <summary>
    /// Called on each combat tick. Resets cast blocking and notifies sub-managers.
    /// </summary>
    public void OnCombatTick()
    {
        _castCoordinator.OnCombatTick();
        _partyManager.OnCombatTick();
    }
    
    /// <summary>
    /// Check if auto-recast should fire (delegates to CastCoordinator).
    /// </summary>
    public void CheckAutoRecast() => _castCoordinator.CheckAutoRecast();
    
    /// <summary>
    /// Check if par command should be sent (delegates to PartyManager).
    /// </summary>
    public void CheckParCommand() => _partyManager.CheckParCommand();
    
    /// <summary>
    /// Check if health requests should be sent (delegates to PartyManager).
    /// </summary>
    public void CheckHealthRequests() => _partyManager.CheckHealthRequests();
    
    /// <summary>
    /// Check if the target name refers to the player themselves.
    /// </summary>
    public bool IsTargetSelf(string targetName) => _playerStateManager.IsTargetSelf(targetName);
    
    #endregion
    
    #region Character Profile Management
    
    // Delegate to ProfileManager
    public string CharacterProfilesPath => _profileManager.CharacterProfilesPath;
    public string CurrentProfilePath => _profileManager.CurrentProfilePath;
    public bool HasUnsavedChanges => _profileManager.HasUnsavedChanges;
    
    public string GetDefaultProfileFilename()
    {
        return _profileManager.GetDefaultProfileFilename(_playerStateManager.PlayerInfo.Name);
    }
    
    public void UpdateBbsSettings(BbsSettings settings)
    {
        _bbsSettings = settings.Clone();
        _profileManager.HasUnsavedChanges = true;
        OnBbsSettingsChanged?.Invoke();
        OnLogMessage?.Invoke("📡 BBS settings updated");
    }
    
    /// <summary>
    /// Assemble a CharacterProfile DTO from all sub-managers and save to disk.
    /// DTO assembly stays here (GameManager knows all sub-managers).
    /// File I/O is delegated to ProfileManager.
    /// </summary>
    public (bool success, string message) SaveCharacterProfile(string filePath)
    {
        var playerInfo = _playerStateManager.PlayerInfo;
        var profile = new CharacterProfile
        {
            ProfileVersion = "1.0",
            SavedAt = DateTime.Now,
            
            CharacterName = playerInfo.Name,
            CharacterClass = playerInfo.Class,
            CharacterLevel = playerInfo.Level,
            
            BbsSettings = _bbsSettings.Clone(),
            CombatSettings = _combatManager.GetCurrentSettings(),
            Buffs = _buffManager.BuffConfigurations.Select(b => b.Clone()).ToList(),
            
            HealSpells = _healingManager.Configuration.HealSpells.Select(h => h.Clone()).ToList(),
            SelfHealRules = _healingManager.Configuration.SelfHealRules.Select(r => r.Clone()).ToList(),
            PartyHealRules = _healingManager.Configuration.PartyHealRules.Select(r => r.Clone()).ToList(),
            PartyWideHealRules = _healingManager.Configuration.PartyWideHealRules.Select(r => r.Clone()).ToList(),
            
            Ailments = _cureManager.Configuration.Ailments.Select(a => a.Clone()).ToList(),
            CureSpells = _cureManager.Configuration.CureSpells.Select(c => c.Clone()).ToList(),
            
            MonsterOverrides = _monsterDatabaseManager.GetOverridesForProfile(),
            Players = _playerDatabaseManager.GetPlayersForProfile(),
            
            ParAutoEnabled = _partyManager.ParAutoEnabled,
            ParFrequencySeconds = _partyManager.ParFrequencySeconds,
            ParAfterCombatTick = _partyManager.ParAfterCombatTick,
            HealthRequestEnabled = _partyManager.HealthRequestEnabled,
            HealthRequestIntervalSeconds = _partyManager.HealthRequestIntervalSeconds,
            
            ManaReservePercent = _buffManager.ManaReservePercent,
            BuffWhileResting = _buffManager.BuffWhileResting,
            BuffWhileInCombat = _buffManager.BuffWhileInCombat,
            WindowSettings = _windowSettings
        };
        
        return _profileManager.SaveProfile(profile, filePath);
    }
    
    public void NewCharacterProfile()
    {
        _combatManager.Clear();
        _bbsSettings = new BbsSettings();
        OnBbsSettingsChanged?.Invoke();

        _buffManager.ClearAllConfigurations();
        _buffManager.ClearAllActiveBuffs();

        _healingManager.ReplaceConfiguration(new HealingConfiguration
        {
            HealingEnabled = false,
            HealSpells = new List<HealSpellConfiguration>(),
            SelfHealRules = new List<HealRule>(),
            PartyHealRules = new List<HealRule>(),
            PartyWideHealRules = new List<HealRule>()
        });

        _cureManager.ReplaceConfiguration(new CureConfiguration
        {
            CuringEnabled = false,
            Ailments = new List<AilmentConfiguration>(),
            CureSpells = new List<CureSpellConfiguration>(),
            PriorityOrder = new List<CastPriorityType> { CastPriorityType.Heals, CastPriorityType.Cures, CastPriorityType.Buffs }
        });

        _monsterDatabaseManager.LoadOverridesFromProfile(new List<MonsterOverride>());
        _monsterDatabaseManager.Reload();
        _playerDatabaseManager.Clear();
        _playerStateManager.ResetPlayerInfo();
        _partyManager.ResetToDefaults();
        _partyManager.Clear();

        _buffManager.ResetSettings();
        _combatAutoEnabled = true;
        _combatManager.SetCombatEnabledFromSettings(true);
        _windowSettings = null;
        _profileManager.ResetForNewProfile();
    }

    /// <summary>
    /// Load a CharacterProfile from disk and distribute to all sub-managers.
    /// File I/O is delegated to ProfileManager.
    /// DTO distribution stays here (GameManager knows all sub-managers).
    /// </summary>
    public (bool success, string message) LoadCharacterProfile(string filePath)
    {
        var (success, message, profile) = _profileManager.LoadProfile(filePath);
        if (!success || profile == null)
            return (success, message);
        
        _combatManager.LoadFromProfile(profile.CombatSettings);
        if (!string.IsNullOrEmpty(profile.CharacterName))
            _combatManager.CurrentCharacter = profile.CharacterName;
        
        if (profile.BbsSettings != null)
        {
            _bbsSettings = profile.BbsSettings.Clone();
            OnBbsSettingsChanged?.Invoke();
        }
        
        _buffManager.LoadFromProfile(profile.Buffs);
        
        _healingManager.ReplaceConfiguration(new HealingConfiguration
        {
            HealingEnabled = _healingManager.HealingEnabled,
            HealSpells = profile.HealSpells ?? new List<HealSpellConfiguration>(),
            SelfHealRules = profile.SelfHealRules ?? new List<HealRule>(),
            PartyHealRules = profile.PartyHealRules ?? new List<HealRule>(),
            PartyWideHealRules = profile.PartyWideHealRules ?? new List<HealRule>()
        });
        
        _cureManager.ReplaceConfiguration(new CureConfiguration
        {
            CuringEnabled = _cureManager.CuringEnabled,
            Ailments = profile.Ailments ?? new List<AilmentConfiguration>(),
            CureSpells = profile.CureSpells ?? new List<CureSpellConfiguration>(),
            PriorityOrder = _cureManager.PriorityOrder
        });
        
        _monsterDatabaseManager.LoadOverridesFromProfile(profile.MonsterOverrides);
        _playerDatabaseManager.LoadFromProfile(profile.Players);
        
        _playerStateManager.LoadFromProfile(profile.CharacterName, profile.CharacterClass, profile.CharacterLevel);
        
        _partyManager.LoadFromProfile(
            profile.ParAutoEnabled, profile.ParFrequencySeconds, profile.ParAfterCombatTick,
            profile.HealthRequestEnabled, profile.HealthRequestIntervalSeconds);
        
        _buffManager.LoadSettingsFromProfile(profile.ManaReservePercent, profile.BuffWhileResting, profile.BuffWhileInCombat);
        
        // All automation toggles default to ON when loading a profile
        _combatAutoEnabled = true;
        _combatManager.SetCombatEnabledFromSettings(true);
        _buffManager.AutoRecastEnabled = true;
        _healingManager.HealingEnabled = true;
        _cureManager.CuringEnabled = true;
        
        _windowSettings = profile.WindowSettings;
        
        // Sync AppSettings (will be consolidated in Phase 8)
        _appSettings.LastCharacterPath = filePath;
        _appSettings.Save();
        
        return (success, message);
    }
    
    #endregion
}
