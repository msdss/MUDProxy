using System.Text.Json;
using System.Text.RegularExpressions;

namespace MudProxyViewer;

public class BuffManager
{
    private readonly List<BuffConfiguration> _buffConfigurations = new();
    private readonly List<ActiveBuff> _activeBuffs = new();
    
    private readonly string _configFilePath;
    private readonly string _characterProfilesPath;
    private string _currentProfilePath = string.Empty;
    
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
    
    // Casting state (stays in BuffManager - buff-specific)
    private bool _castBlockedUntilNextTick = false;
    private int _manaReservePercent = 20;
    private bool _commandsPaused = false;
    private bool _isExiting = false;
    
    // Buff state settings (persisted)
    private bool _buffWhileResting = true;
    private bool _buffWhileInCombat = true;
    
    // App settings (persisted)
    private bool _combatAutoEnabled = true;
    
    // BBS/Telnet settings (loaded from character profile)
    private BbsSettings _bbsSettings = new();
    
    // Window settings (loaded from character profile)
    private WindowSettings? _windowSettings;
    
    // Regex patterns for cast failures (buff-specific, stays in BuffManager)
    private static readonly Regex CastFailRegex = new(
        @"You attempt to cast (.+?), but fail\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    private static readonly Regex NotEnoughManaRegex = new(
        @"You do not have enough mana to cast that spell\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    private static readonly Regex AlreadyCastRegex = new(
        @"You have already cast a spell this round!",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    #region Properties
    
    // BuffManager's own settings
    public int ManaReservePercent
    {
        get => _manaReservePercent;
        set => _manaReservePercent = Math.Clamp(value, 0, 100);
    }
    
    public bool BuffWhileResting
    {
        get => _buffWhileResting;
        set => _buffWhileResting = value;
    }
    
    public bool BuffWhileInCombat
    {
        get => _buffWhileInCombat;
        set => _buffWhileInCombat = value;
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
            }
        }
    }
    
    // App settings (persisted in settings.json, not character profile)
    public AppSettings AppSettings => _appSettings;
    
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

    public bool ShouldPauseCommands => _playerStateManager.InTrainingScreen || _isExiting || _commandsPaused || _playerStateManager.IsInLoginPhase;

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
    
    // Profile data
    public BbsSettings BbsSettings => _bbsSettings;
    
    public WindowSettings? WindowSettings
    {
        get => _windowSettings;
        set => _windowSettings = value;
    }
    public event Action? OnBbsSettingsChanged;
    
    #endregion
    
    #region Constructor
    
    public BuffManager()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MudProxyViewer");
            
        _configFilePath = Path.Combine(appDataPath, "buffs.json");
        _characterProfilesPath = Path.Combine(appDataPath, "Characters");
        
        _appSettings = new AppSettings(appDataPath);
        _appSettings.Load();
        
        if (!Directory.Exists(_characterProfilesPath))
        {
            Directory.CreateDirectory(_characterProfilesPath);
        }
        
        // Create PlayerStateManager first (others need player info)
        _playerStateManager = new PlayerStateManager(
            msg => OnLogMessage?.Invoke(msg),
            cmd => OnSendCommand?.Invoke(cmd)
        );
        _playerStateManager.OnPlayerInfoChanged += () => OnPlayerInfoChanged?.Invoke();
        _playerStateManager.OnTrainingScreenChanged += entered => OnTrainingScreenChanged?.Invoke(entered);
        
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
        _partyManager.OnPartyMembersRemoved += removedNames =>
        {
            foreach (var name in removedNames)
            {
                ClearBuffsForTarget(name);
                _cureManager?.ClearAilmentsForTarget(name);
            }
        };
        _partyManager.OnPartyUpdated += partyMembers =>
        {
            _cureManager?.UpdatePartyPoisonStatus(partyMembers.ToList());
        };
        
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
            () => _manaReservePercent,
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
            () => _autoRecastEnabled,
            enabled => AutoRecastEnabled = enabled,
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
        
        LoadConfigurations();
    }
    
    #endregion
    
    public IReadOnlyList<BuffConfiguration> BuffConfigurations => _buffConfigurations.AsReadOnly();
    public IReadOnlyList<ActiveBuff> ActiveBuffs => _activeBuffs.AsReadOnly();
    
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
                var existing = _buffConfigurations.FirstOrDefault(b => 
                    b.DisplayName.Equals(buff.DisplayName, StringComparison.OrdinalIgnoreCase));
                
                if (existing != null)
                {
                    if (replaceExisting)
                    {
                        buff.Id = existing.Id;
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
                    buff.Id = Guid.NewGuid().ToString();
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
    
    #endregion
    
    #region Message Processing
    
    public void ProcessMessage(string message)
    {
        // Detect game exit meditation
        bool justStartedExiting = false;
        if (message.Contains("You will exit after a period of silent meditation."))
        {
            if (!_isExiting)
            {
                _isExiting = true;
                justStartedExiting = true;
                OnLogMessage?.Invoke("🚪 Player exiting game - automation paused");
            }
        }
        
        // Detect exit interrupted
        if (_isExiting && message.Contains("Your meditation has been interrupted"))
        {
            _isExiting = false;
            OnLogMessage?.Invoke("⚔️ Exit interrupted - automation resumed");
            OnSendCommand?.Invoke("");
        }
        
        // Player re-entered the game after exiting — HP bar means we're back
        // Skip if we just started exiting (same chunk may contain trailing HP bar)
        if (_isExiting && !justStartedExiting && message.Contains("[HP="))
        {
            _isExiting = false;
            OnLogMessage?.Invoke("▶️ Player re-entered game - automation resumed");
        }
        
        // Feed lines to room tracker for location detection
        foreach (var line in message.Split('\n'))
        {
            _roomTracker.ProcessLine(line.TrimEnd('\r'));
        }

        // Check for remote commands first
        _remoteCommandManager.ProcessMessage(message);
        
        // Delegate to sub-managers
        _partyManager.ProcessMessage(message);
        _playerStateManager.ProcessMessage(message);
        
        // Process cure-related messages
        _cureManager.ProcessMessage(message, _partyManager.PartyMembers.ToList());
        
        // Process player database
        _playerDatabaseManager.ProcessMessage(message);
        
        // Check for cast failures (buff-specific)
        var failMatch = CastFailRegex.Match(message);
        if (failMatch.Success)
        {
            var spellName = failMatch.Groups[1].Value;
            OnLogMessage?.Invoke($"⚠️ Spell failed: {spellName} - blocked until next tick");
            _castBlockedUntilNextTick = true;
            return;
        }
        
        if (NotEnoughManaRegex.IsMatch(message))
        {
            OnLogMessage?.Invoke($"⚠️ Not enough mana - blocked until next tick");
            _castBlockedUntilNextTick = true;
            return;
        }
        
        if (AlreadyCastRegex.IsMatch(message))
        {
            OnLogMessage?.Invoke($"⚠️ Already cast this round - blocked until next tick");
            _castBlockedUntilNextTick = true;
            return;
        }
        
        // Check for buff cast success
        foreach (var config in _buffConfigurations)
        {
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
                else if (message.Contains(config.SelfCastMessage, StringComparison.OrdinalIgnoreCase))
                {
                    ActivateBuff(config, string.Empty);
                    continue;
                }
            }
            
            if (!string.IsNullOrEmpty(config.PartyCastMessage) && 
                config.TargetType != BuffTargetType.SelfOnly)
            {
                var targetName = TryExtractTargetFromPattern(message, config.PartyCastMessage);
                if (targetName != null)
                {
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
            
            if (!string.IsNullOrEmpty(config.ExpireMessage) &&
                message.Contains(config.ExpireMessage, StringComparison.OrdinalIgnoreCase))
            {
                ExpireBuff(config);
            }
        }
    }
    
    private string? TryExtractTargetFromPattern(string message, string pattern)
    {
        if (!pattern.Contains("{target}"))
            return null;
        
        var patternWithPlaceholder = pattern.Replace("{target}", "<<<TARGET>>>");
        var escapedPattern = Regex.Escape(patternWithPlaceholder);
        var regexPattern = escapedPattern.Replace("<<<TARGET>>>", @"(\w+)");
        
        try
        {
            var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
            var match = regex.Match(message);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
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
    /// Delegates to PlayerStateManager.
    /// </summary>
    public bool IsTargetSelf(string targetName)
    {
        return _playerStateManager.IsTargetSelf(targetName);
    }
    
    #endregion
    
    #region Buff Activation/Expiration
    
    private void ActivateBuff(BuffConfiguration config, string targetName)
    {
        _activeBuffs.RemoveAll(b => 
            b.Configuration.Id == config.Id && 
            b.TargetName.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        
        var buff = new ActiveBuff
        {
            Configuration = config,
            TargetName = targetName,
            CastTime = DateTime.Now,
            ExpireTime = DateTime.Now.AddSeconds(config.DurationSeconds)
        };
        
        _activeBuffs.Add(buff);
        
        var targetDisplay = string.IsNullOrEmpty(targetName) ? "SELF" : targetName;
        OnLogMessage?.Invoke($"✨ Buff activated: {config.DisplayName} on {targetDisplay} ({config.DurationSeconds}s)");
        OnBuffsChanged?.Invoke();
    }
    
    private void ExpireBuff(BuffConfiguration config)
    {
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
    /// Called when disconnected from server
    /// </summary>
    public void OnDisconnected()
    {
        _isExiting = false;
        _playerStateManager.OnDisconnected();
        _partyManager.OnDisconnected();
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
        return _partyManager.PartyMembers.Where(p => p.IsMelee && !IsTargetSelf(p.Name));
    }
    
    public IEnumerable<PartyMember> GetCasterPartyMembers()
    {
        return _partyManager.PartyMembers.Where(p => p.IsCaster && !IsTargetSelf(p.Name));
    }
    
    public IEnumerable<PartyMember> GetAllPartyMembers()
    {
        return _partyManager.PartyMembers.Where(p => !IsTargetSelf(p.Name));
    }
    
    #endregion
    
    #region Auto-Recast System
    
    private bool _autoRecastEnabled = true;
    private DateTime _lastRecastAttempt = DateTime.MinValue;
    private DateTime _lastCastCommandSent = DateTime.MinValue;
    private const int MIN_RECAST_INTERVAL_MS = 500;
    private const int CAST_COOLDOWN_MS = 5500;
    
    public bool AutoRecastEnabled 
    { 
        get => _autoRecastEnabled; 
        set => _autoRecastEnabled = value; 
    }
    
    public void CheckAutoRecast()
    {
        if (ShouldPauseCommands) return;
        
        if (_castBlockedUntilNextTick)
        {
            OnLogMessage?.Invoke("⏸️ Cast blocked until next tick");
            return;
        }
        
        var timeSinceLastCast = (DateTime.Now - _lastCastCommandSent).TotalMilliseconds;
        if (timeSinceLastCast < CAST_COOLDOWN_MS) return;
        
        var timeSinceLastRecast = (DateTime.Now - _lastRecastAttempt).TotalMilliseconds;
        if (timeSinceLastRecast < MIN_RECAST_INTERVAL_MS) return;
        
        foreach (var priorityType in _cureManager.PriorityOrder)
        {
            if (TryCastByPriority(priorityType)) return;
        }
    }
    
    public void CheckParCommand() => _partyManager.CheckParCommand();
    public void CheckHealthRequests() => _partyManager.CheckHealthRequests();
    
    public void OnCombatTick()
    {
        _castBlockedUntilNextTick = false;
        _lastCastCommandSent = DateTime.MinValue;
        _partyManager.OnCombatTick();
    }
    
    private bool TryCastByPriority(CastPriorityType priorityType)
    {
        return priorityType switch
        {
            CastPriorityType.Heals => TryCastHeal(),
            CastPriorityType.Cures => TryCastCure(),
            CastPriorityType.Buffs => TryCastBuff(),
            _ => false
        };
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
        if (!_autoRecastEnabled) return false;
        if (_playerStateManager.IsResting && !_buffWhileResting) return false;
        if (_playerStateManager.InCombat && !_buffWhileInCombat) return false;
        
        var buffsToRecast = GetBuffsNeedingRecast().ToList();
        if (buffsToRecast.Count == 0) return false;
        
        if (_playerStateManager.MaxMana > 0)
        {
            var currentManaPercent = (_playerStateManager.CurrentMana * 100) / _playerStateManager.MaxMana;
            if (currentManaPercent < _manaReservePercent)
            {
                OnLogMessage?.Invoke($"⏸️ Buff skipped: mana {currentManaPercent}% < {_manaReservePercent}% reserve");
                return false;
            }
        }
        
        BuffConfiguration? configToCast = null;
        string targetToCast = string.Empty;
        
        foreach (var (config, target) in buffsToRecast)
        {
            if (config.ManaCost > 0 && _playerStateManager.CurrentMana < config.ManaCost)
            {
                OnLogMessage?.Invoke($"⏸️ Buff {config.DisplayName} skipped: need {config.ManaCost} mana, have {_playerStateManager.CurrentMana}");
                continue;
            }
            
            if (_playerStateManager.MaxMana > 0 && config.ManaCost > 0)
            {
                var manaAfterCast = _playerStateManager.CurrentMana - config.ManaCost;
                var manaPercentAfter = (manaAfterCast * 100) / _playerStateManager.MaxMana;
                if (manaPercentAfter < _manaReservePercent)
                {
                    OnLogMessage?.Invoke($"⏸️ Buff {config.DisplayName} skipped: would drop mana to {manaPercentAfter}% (reserve: {_manaReservePercent}%)");
                    continue;
                }
            }
            
            configToCast = config;
            targetToCast = target;
            break;
        }
        
        if (configToCast == null) return false;
        
        _lastRecastAttempt = DateTime.Now;
        _lastCastCommandSent = DateTime.Now;
        
        string command = configToCast.Command;
        if (!string.IsNullOrEmpty(targetToCast))
        {
            command = $"{command} {targetToCast}";
        }
        
        OnLogMessage?.Invoke($"🔄 Auto-recasting: {configToCast.DisplayName}" + 
            (string.IsNullOrEmpty(targetToCast) ? "" : $" on {targetToCast}") +
            $" (cost: {configToCast.ManaCost}, have: {_playerStateManager.CurrentMana})");
        OnSendCommand?.Invoke(command);
        return true;
    }
    
    public void OnSuccessfulCast() { }
    
    private IEnumerable<(BuffConfiguration Config, string Target)> GetBuffsNeedingRecast()
    {
        var results = new List<(BuffConfiguration Config, string Target, int Priority)>();
        var playerInfo = _playerStateManager.PlayerInfo;
        
        foreach (var config in _buffConfigurations.Where(c => c.AutoRecast))
        {
            bool shouldBuffSelf = config.TargetType == BuffTargetType.SelfOnly || 
                config.TargetType == BuffTargetType.AllParty ||
                (config.TargetType == BuffTargetType.MeleeParty && playerInfo.IsMelee) ||
                (config.TargetType == BuffTargetType.CasterParty && playerInfo.IsCaster);
            
            var existingSelfBuff = _activeBuffs.FirstOrDefault(b => 
                b.Configuration.Id == config.Id && b.IsSelfBuff);
            
            if (shouldBuffSelf || existingSelfBuff != null)
            {
                if (NeedsRecast(config, string.Empty))
                {
                    results.Add((config, string.Empty, config.Priority));
                }
            }
            
            if (config.TargetType != BuffTargetType.SelfOnly)
            {
                foreach (var target in GetPartyTargetsForBuff(config))
                {
                    if (NeedsRecast(config, target.Name))
                    {
                        results.Add((config, target.Name, config.Priority));
                    }
                }
                
                foreach (var existingBuff in _activeBuffs.Where(b => b.Configuration.Id == config.Id && !b.IsSelfBuff))
                {
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
        
        return results
            .OrderBy(r => r.Priority)
            .ThenBy(r => string.IsNullOrEmpty(r.Target) ? 0 : 1)
            .Select(r => (r.Config, r.Target));
    }
    
    private bool NeedsRecast(BuffConfiguration config, string target)
    {
        var activeBuff = _activeBuffs.FirstOrDefault(b => 
            b.Configuration.Id == config.Id && 
            b.TargetName.Equals(target, StringComparison.OrdinalIgnoreCase));
        
        if (activeBuff == null) return true;
        
        if (config.RecastBufferSeconds > 0)
        {
            return activeBuff.TimeRemaining.TotalSeconds <= config.RecastBufferSeconds;
        }
        
        return activeBuff.IsExpired;
    }
    
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
    
    public string GetDefaultProfileFilename()
    {
        var playerInfo = _playerStateManager.PlayerInfo;
        if (!string.IsNullOrEmpty(playerInfo.Name))
        {
            var safeName = string.Join("_", playerInfo.Name.Split(Path.GetInvalidFileNameChars()));
            return $"{safeName}.json";
        }
        return "character.json";
    }
    
    public void UpdateBbsSettings(BbsSettings settings)
    {
        _bbsSettings = settings.Clone();
        HasUnsavedChanges = true;
        OnBbsSettingsChanged?.Invoke();
        OnLogMessage?.Invoke("📡 BBS settings updated");
    }
    
    public (bool success, string message) SaveCharacterProfile(string filePath)
    {
        try
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
                Buffs = _buffConfigurations.Select(b => b.Clone()).ToList(),
                
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
                
                ManaReservePercent = _manaReservePercent,
                BuffWhileResting = _buffWhileResting,
                BuffWhileInCombat = _buffWhileInCombat,
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
            return (true, "Character profile saved successfully.");
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error saving character profile: {ex.Message}");
            return (false, $"Error saving character profile: {ex.Message}");
        }
    }
    
    public void NewCharacterProfile()
    {
        _combatManager.Clear();
        _bbsSettings = new BbsSettings();
        OnBbsSettingsChanged?.Invoke();

        _buffConfigurations.Clear();
        _activeBuffs.Clear();
        SaveConfigurations();
        OnBuffsChanged?.Invoke();

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

        _manaReservePercent = 20;
        _buffWhileResting = false;
        _buffWhileInCombat = true;
        _combatAutoEnabled = true;
        _combatManager.SetCombatEnabledFromSettings(true);
        _windowSettings = null;
        _currentProfilePath = string.Empty;
        HasUnsavedChanges = false;
    }

    public (bool success, string message) LoadCharacterProfile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return (false, "Character profile file not found.");
            
            var json = File.ReadAllText(filePath);
            var profile = JsonSerializer.Deserialize<CharacterProfile>(json);
            
            if (profile == null)
                return (false, "Invalid character profile format.");
            
            _combatManager.LoadFromProfile(profile.CombatSettings);
            if (!string.IsNullOrEmpty(profile.CharacterName))
                _combatManager.CurrentCharacter = profile.CharacterName;
            
            if (profile.BbsSettings != null)
            {
                _bbsSettings = profile.BbsSettings.Clone();
                OnBbsSettingsChanged?.Invoke();
            }
            
            _buffConfigurations.Clear();
            if (profile.Buffs != null)
                _buffConfigurations.AddRange(profile.Buffs);
            SaveConfigurations();
            OnBuffsChanged?.Invoke();
            
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
            
            _manaReservePercent = profile.ManaReservePercent;
            _buffWhileResting = profile.BuffWhileResting;
            _buffWhileInCombat = profile.BuffWhileInCombat;
            
            // All automation toggles default to ON when loading a profile
            _combatAutoEnabled = true;
            _combatManager.SetCombatEnabledFromSettings(true);
            _autoRecastEnabled = true;
            _healingManager.HealingEnabled = true;
            _cureManager.CuringEnabled = true;
            
            _windowSettings = profile.WindowSettings;
            
            _currentProfilePath = filePath;
            HasUnsavedChanges = false;
            _appSettings.LastCharacterPath = filePath;
            _appSettings.Save();
            
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
