using System.Text.RegularExpressions;

namespace MudProxyViewer;

/// <summary>
/// Routes and processes messages from the MUD server
/// Extracts message processing logic from MainForm for better organization
/// </summary>
public class MessageRouter
{
    // Events to notify MainForm of state changes
    public event Action<bool>? OnCombatStateChanged;           // true = in combat, false = idle
    public event Action<int, int, string>? OnPlayerStatsUpdated;  // currentHP, currentMana, manaType
    public event Action? OnCombatTickDetected;                  // Combat tick happened
    public event Action? OnPlayerDeath;                         // Player died
    public event Action? OnLoginComplete;                       // HP bar detected = login complete
    public event Action<bool>? OnPauseStateChanged;             // Commands paused state changed
    
    // References to managers
    private readonly BuffManager _buffManager;
    
    // State tracking
    private bool _isInLoginPhase = true;
    private DateTime _lastDamageMessageTime = DateTime.MinValue;
    private int _damageMessageCount = 0;
    private DateTime? _nextTickTime = null;
    
    // Configuration
    private const int TICK_INTERVAL_MS = 5000;
    private const int DAMAGE_CLUSTER_THRESHOLD = 2;
    private const int DAMAGE_CLUSTER_WINDOW_MS = 500;
    
    // Pattern Detection
    private static readonly Regex HpManaRegex = new(@"\[HP=(\d+)(?:/(\d+))?/(MA|KAI)=(\d+)(?:/(\d+))?\]", RegexOptions.Compiled);
    private static readonly Regex DamageRegex = new(@"for \d+ damage!", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CombatEngagedRegex = new(@"\*Combat Engaged\*", RegexOptions.Compiled);
    private static readonly Regex CombatOffRegex = new(@"\*Combat Off\*", RegexOptions.Compiled);
    private static readonly Regex PlayerDeathRegex = new(@"due to a miracle, you have been saved", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    public MessageRouter(BuffManager buffManager)
    {
        _buffManager = buffManager;
    }
    
    /// <summary>
    /// Set the expected next tick time (called when tick is manually set)
    /// </summary>
    public void SetNextTickTime(DateTime nextTick)
    {
        _nextTickTime = nextTick;
    }
    
    /// <summary>
    /// Reset login phase flag
    /// </summary>
    public void ResetLoginPhase()
    {
        _isInLoginPhase = true;
        _buffManager.PlayerStateManager.IsInLoginPhase = true;
    }
    
    /// <summary>
    /// Process a message from the MUD server
    /// </summary>
    public void ProcessMessage(string text)
    {
        // Pass to buff manager for processing
        var wasPaused = _buffManager.ShouldPauseCommands;
        _buffManager.ProcessMessage(text);
        
        // Pass to combat manager for room parsing
        _buffManager.CombatManager.ProcessMessage(text);
        
        // Update pause button if state changed (e.g., training screen detected/exited)
        if (wasPaused != _buffManager.ShouldPauseCommands)
        {
            OnPauseStateChanged?.Invoke(_buffManager.ShouldPauseCommands);
        }
        
        // Combat state changes
        if (CombatEngagedRegex.IsMatch(text))
        {
            OnCombatStateChanged?.Invoke(true);
            _buffManager.CombatManager.OnCombatEngaged();
        }
        else if (CombatOffRegex.IsMatch(text))
        {
            OnCombatStateChanged?.Invoke(false);
            _buffManager.CombatManager.OnCombatEnded();
        }
        
        // HP/Mana updates
        var hpMatch = HpManaRegex.Match(text);
        if (hpMatch.Success)
        {
            ParsePlayerStats(hpMatch);
            
            // HP bar means we're in-game - login phase complete
            if (_isInLoginPhase)
            {
                _isInLoginPhase = false;
                _buffManager.PlayerStateManager.IsInLoginPhase = false;
                OnLoginComplete?.Invoke();
            }
        }
        
        // Damage detection for tick timing
        if (DamageRegex.IsMatch(text))
        {
            DetectCombatTick();
        }
        
        // Death detection
        if (PlayerDeathRegex.IsMatch(text))
        {
            OnCombatStateChanged?.Invoke(false);
            OnPlayerDeath?.Invoke();
        }
    }
    
    /// <summary>
    /// Parse HP/Mana stats from regex match and notify MainForm
    /// </summary>
    private void ParsePlayerStats(Match match)
    {
        int currentHp = 0;
        int currentMana = 0;
        string manaType = "MA";
        
        if (int.TryParse(match.Groups[1].Value, out int hp))
        {
            currentHp = hp;
        }
        
        manaType = match.Groups[3].Value;
        if (int.TryParse(match.Groups[4].Value, out int mana))
        {
            currentMana = mana;
        }
        
        OnPlayerStatsUpdated?.Invoke(currentHp, currentMana, manaType);
    }
    
    /// <summary>
    /// Detect combat tick based on damage message clustering
    /// </summary>
    private void DetectCombatTick()
    {
        var now = DateTime.Now;
        var timeSinceLastDamage = (now - _lastDamageMessageTime).TotalMilliseconds;
        
        if (timeSinceLastDamage < DAMAGE_CLUSTER_WINDOW_MS)
        {
            _damageMessageCount++;
        }
        else
        {
            _damageMessageCount = 1;
            
            // Always detect tick when we see damage - combat state doesn't matter
            // The global tick runs regardless of whether WE are in combat
            if (_nextTickTime.HasValue)
            {
                var drift = Math.Abs((now - _nextTickTime.Value).TotalMilliseconds);
                // If we're close to expected tick time, or way off, record it
                if (drift < 1500 || drift > 3500)
                {
                    OnCombatTickDetected?.Invoke();
                    _nextTickTime = now.AddMilliseconds(TICK_INTERVAL_MS);
                }
            }
            else
            {
                // First tick detection - establish timing
                OnCombatTickDetected?.Invoke();
                _nextTickTime = now.AddMilliseconds(TICK_INTERVAL_MS);
            }
        }
        
        _lastDamageMessageTime = now;
    }
}
