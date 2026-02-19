using System.Text.Json;
using System.Text.RegularExpressions;

namespace MudProxyViewer;

/// <summary>
/// Manages buff configurations, active buff tracking, and buff recast eligibility.
/// Pure buff management — no longer coordinates cross-system casting.
/// 
/// Cast coordination (heal/cure/buff priority, timing, cooldowns, failure detection)
/// is handled by CastCoordinator, which calls CheckBuffRecast() when it's buff's turn.
/// 
/// GameManager owns BuffManager and wires dependencies.
/// </summary>
public class BuffManager
{
    private readonly List<BuffConfiguration> _buffConfigurations = new();
    private readonly List<ActiveBuff> _activeBuffs = new();
    
    private readonly string _configFilePath;
    
    // Dependencies (injected by GameManager)
    private readonly PlayerStateManager _playerStateManager;
    private readonly PartyManager _partyManager;
    
    // Cast failure delegate (injected by GameManager, points to CastCoordinator.ProcessCastFailures)
    private Func<string, bool>? _processCastFailures;
    
    // Events
    public event Action? OnBuffsChanged;
    public event Action<string>? OnLogMessage;
    
    // Buff state settings (persisted in character profile)
    private int _manaReservePercent = 20;
    private bool _buffWhileResting = true;
    private bool _buffWhileInCombat = true;
    
    #region Properties
    
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
    
    public IReadOnlyList<BuffConfiguration> BuffConfigurations => _buffConfigurations.AsReadOnly();
    public IReadOnlyList<ActiveBuff> ActiveBuffs => _activeBuffs.AsReadOnly();
    
    #endregion
    
    #region Constructor
    
    public BuffManager(
        string appDataPath,
        PlayerStateManager playerStateManager,
        PartyManager partyManager,
        Action<string> logMessage)
    {
        _configFilePath = Path.Combine(appDataPath, "buffs.json");
        _playerStateManager = playerStateManager;
        _partyManager = partyManager;
        
        // Wire log message through event so GameManager can forward to UI
        OnLogMessage += logMessage;
        
        LoadConfigurations();
    }
    
    /// <summary>
    /// Wire the cast failure handler from CastCoordinator.
    /// Called by GameManager after creating both BuffManager and CastCoordinator.
    /// When ProcessMessage() encounters a server message, it delegates cast failure
    /// detection to CastCoordinator before checking buff-specific patterns.
    /// </summary>
    public void SetCastFailureHandler(Func<string, bool> handler)
    {
        _processCastFailures = handler;
    }
    
    #endregion
    
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
        _activeBuffs.RemoveAll(b => b.Configuration.Id == id);
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
        return JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
    }
    
    public (int imported, int skipped, string message) ImportBuffs(string json, bool replaceExisting)
    {
        try
        {
            var export = JsonSerializer.Deserialize<BuffExport>(json);
            if (export?.Buffs == null || export.Buffs.Count == 0)
                return (0, 0, "No buffs found in import file.");
            
            if (replaceExisting)
            {
                _buffConfigurations.Clear();
                _activeBuffs.Clear();
            }
            
            int imported = 0;
            int skipped = 0;
            foreach (var buff in export.Buffs)
            {
                if (!replaceExisting && _buffConfigurations.Any(b => 
                    b.DisplayName.Equals(buff.DisplayName, StringComparison.OrdinalIgnoreCase)))
                {
                    skipped++;
                    continue;
                }
                
                buff.Id = Guid.NewGuid().ToString();
                _buffConfigurations.Add(buff);
                imported++;
            }
            
            SaveConfigurations();
            OnBuffsChanged?.Invoke();
            
            return (imported, skipped, 
                $"Imported {imported} buff(s)" + (skipped > 0 ? $", skipped {skipped} duplicate(s)" : ""));
        }
        catch (Exception ex)
        {
            return (0, 0, $"Import failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Clear all buff configurations and active buffs.
    /// Called by GameManager during NewCharacterProfile.
    /// </summary>
    public void ClearAllConfigurations()
    {
        _buffConfigurations.Clear();
        _activeBuffs.Clear();
        SaveConfigurations();
        OnBuffsChanged?.Invoke();
    }
    
    /// <summary>
    /// Load buff configurations from a profile.
    /// Called by GameManager during LoadCharacterProfile.
    /// </summary>
    public void LoadFromProfile(List<BuffConfiguration>? buffs)
    {
        _buffConfigurations.Clear();
        if (buffs != null)
            _buffConfigurations.AddRange(buffs);
        SaveConfigurations();
        OnBuffsChanged?.Invoke();
    }
    
    /// <summary>
    /// Load buff-specific settings from a profile.
    /// </summary>
    public void LoadSettingsFromProfile(int manaReservePercent, bool buffWhileResting, bool buffWhileInCombat)
    {
        _manaReservePercent = manaReservePercent;
        _buffWhileResting = buffWhileResting;
        _buffWhileInCombat = buffWhileInCombat;
    }
    
    /// <summary>
    /// Reset buff settings to defaults.
    /// Called by GameManager during NewCharacterProfile.
    /// </summary>
    public void ResetSettings()
    {
        _manaReservePercent = 20;
        _buffWhileResting = false;
        _buffWhileInCombat = true;
    }
    
    #endregion
    
    #region Message Processing
    
    /// <summary>
    /// Process a message for buff-specific detection only.
    /// Cast success, buff expiration, and cast failure detection
    /// (delegated to CastCoordinator via injected handler).
    /// All other message dispatching is handled by MessageRouter.
    /// </summary>
    public void ProcessMessage(string message)
    {
        // Delegate cast failure detection to CastCoordinator
        if (_processCastFailures?.Invoke(message) == true)
            return;
        
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
    
    private IEnumerable<PartyMember> GetMeleePartyMembers()
    {
        return _partyManager.PartyMembers.Where(p => p.IsMelee && !IsTargetSelf(p.Name));
    }
    
    private IEnumerable<PartyMember> GetCasterPartyMembers()
    {
        return _partyManager.PartyMembers.Where(p => p.IsCaster && !IsTargetSelf(p.Name));
    }
    
    private IEnumerable<PartyMember> GetAllPartyMembers()
    {
        return _partyManager.PartyMembers.Where(p => !IsTargetSelf(p.Name));
    }
    
    #endregion
    
    #region Buff Recast System
    
    private bool _autoRecastEnabled = true;
    
    public bool AutoRecastEnabled 
    { 
        get => _autoRecastEnabled; 
        set => _autoRecastEnabled = value; 
    }
    
    /// <summary>
    /// Check if any buffs need recasting and return the command to cast.
    /// Called by CastCoordinator when it's buff's turn in the priority loop.
    /// Returns null if no buff needs recasting (disabled, conditions not met, mana too low).
    /// </summary>
    public (string Command, string Description)? CheckBuffRecast()
    {
        if (!_autoRecastEnabled) return null;
        if (_playerStateManager.IsResting && !_buffWhileResting) return null;
        if (_playerStateManager.InCombat && !_buffWhileInCombat) return null;
        
        var buffsToRecast = GetBuffsNeedingRecast().ToList();
        if (buffsToRecast.Count == 0) return null;
        
        if (_playerStateManager.MaxMana > 0)
        {
            var currentManaPercent = (_playerStateManager.CurrentMana * 100) / _playerStateManager.MaxMana;
            if (currentManaPercent < _manaReservePercent)
            {
                OnLogMessage?.Invoke($"⏸️ Buff skipped: mana {currentManaPercent}% < {_manaReservePercent}% reserve");
                return null;
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
        
        if (configToCast == null) return null;
        
        string command = configToCast.Command;
        if (!string.IsNullOrEmpty(targetToCast))
        {
            command = $"{command} {targetToCast}";
        }
        
        var description = $"{configToCast.DisplayName}" + 
            (string.IsNullOrEmpty(targetToCast) ? "" : $" on {targetToCast}") +
            $" (cost: {configToCast.ManaCost}, have: {_playerStateManager.CurrentMana})";
        
        return (command, description);
    }
    
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
}
