using System.Text.RegularExpressions;

namespace MudProxyViewer;

/// <summary>
/// Coordinates the priority-based casting system across heals, cures, and buffs.
/// Owns cast timing, cooldowns, and failure detection.
/// Each sub-manager (HealingManager, CureManager, BuffManager) evaluates whether
/// a cast is needed and returns what to cast. CastCoordinator decides when and
/// sends the actual command.
/// 
/// Extracted from BuffManager in Phase 7 of the refactoring plan.
/// GameManager owns this class and delegates CheckAutoRecast/OnCombatTick to it.
/// </summary>
public class CastCoordinator
{
    // Dependencies
    private readonly Func<bool> _shouldPauseCommands;
    private readonly HealingManager _healingManager;
    private readonly CureManager _cureManager;
    private readonly BuffManager _buffManager;
    private readonly Action<string> _sendCommand;
    
    // Events
    public event Action<string>? OnLogMessage;
    
    // Cast timing state
    private bool _castBlockedUntilNextTick = false;
    private DateTime _lastRecastAttempt = DateTime.MinValue;
    private DateTime _lastCastCommandSent = DateTime.MinValue;
    private const int MIN_RECAST_INTERVAL_MS = 500;
    private const int CAST_COOLDOWN_MS = 5500;
    
    // Cast failure regex patterns
    private static readonly Regex CastFailRegex = new(
        @"You attempt to cast (.+?), but fail\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    private static readonly Regex NotEnoughManaRegex = new(
        @"You do not have enough mana to cast that spell\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    private static readonly Regex AlreadyCastRegex = new(
        @"You have already cast a spell this round!",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    #region Constructor
    
    public CastCoordinator(
        Func<bool> shouldPauseCommands,
        HealingManager healingManager,
        CureManager cureManager,
        BuffManager buffManager,
        Action<string> sendCommand,
        Action<string> logMessage)
    {
        _shouldPauseCommands = shouldPauseCommands;
        _healingManager = healingManager;
        _cureManager = cureManager;
        _buffManager = buffManager;
        _sendCommand = sendCommand;
        OnLogMessage += logMessage;
    }
    
    #endregion
    
    #region Cast Priority Loop
    
    /// <summary>
    /// Main entry point for the priority-based casting system.
    /// Called on a timer by MainForm. Iterates through priority order
    /// (typically Heals → Cures → Buffs) and casts the first available.
    /// </summary>
    public void CheckAutoRecast()
    {
        if (_shouldPauseCommands()) return;
        
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
    
    /// <summary>
    /// Called on combat tick. Resets cast blocking state.
    /// </summary>
    public void OnCombatTick()
    {
        _castBlockedUntilNextTick = false;
        _lastCastCommandSent = DateTime.MinValue;
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
            _sendCommand(healResult.Value.Command);
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
            _sendCommand(cureResult.Value.Command);
            
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
        var buffResult = _buffManager.CheckBuffRecast();
        if (buffResult.HasValue)
        {
            _lastRecastAttempt = DateTime.Now;
            _lastCastCommandSent = DateTime.Now;
            OnLogMessage?.Invoke($"🔄 Auto-recasting: {buffResult.Value.Description}");
            _sendCommand(buffResult.Value.Command);
            return true;
        }
        return false;
    }
    
    #endregion
    
    #region Cast Failure Detection
    
    /// <summary>
    /// Process a message for cast failure detection.
    /// Called from BuffManager.ProcessMessage() via delegate injection.
    /// Returns true if a cast failure was detected (caller should skip
    /// further buff-specific processing for this message).
    /// </summary>
    public bool ProcessCastFailures(string message)
    {
        var failMatch = CastFailRegex.Match(message);
        if (failMatch.Success)
        {
            var spellName = failMatch.Groups[1].Value;
            OnLogMessage?.Invoke($"⚠️ Spell failed: {spellName} - blocked until next tick");
            _castBlockedUntilNextTick = true;
            return true;
        }
        
        if (NotEnoughManaRegex.IsMatch(message))
        {
            OnLogMessage?.Invoke("⚠️ Not enough mana - blocked until next tick");
            _castBlockedUntilNextTick = true;
            return true;
        }
        
        if (AlreadyCastRegex.IsMatch(message))
        {
            OnLogMessage?.Invoke("⚠️ Already cast this round - blocked until next tick");
            _castBlockedUntilNextTick = true;
            return true;
        }
        
        return false;
    }
    
    #endregion
}
