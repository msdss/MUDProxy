using System.Text.RegularExpressions;

namespace MudProxyViewer;

/// <summary>
/// Routes and processes all messages from the MUD server.
/// Dispatches to sub-managers and detects game state changes.
/// 
/// Responsibilities:
/// - Room tracker line feeding
/// - Remote command dispatching
/// - Sub-manager message dispatching (party, player state, cure, player DB)
/// - Combat state detection (*Combat Engaged* / *Combat Off*)
/// - HP/Mana parsing from HP bar
/// - Combat tick detection from damage clustering
/// - Death detection
/// - Buff cast/expire detection (delegated to BuffManager)
/// 
/// Note: Exit meditation detection is handled by PlayerStateManager.ProcessMessage().
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
    private readonly GameManager _gameManager;
    
    // State tracking
    private bool _isInLoginPhase = true;
    private DateTime _lastDamageMessageTime = DateTime.MinValue;
    private int _damageMessageCount = 0;
    private DateTime? _nextTickTime = null;
    
    // Configuration
    private const int TICK_INTERVAL_MS = 5000;
    private const int DAMAGE_CLUSTER_THRESHOLD = 2;
    private const int DAMAGE_CLUSTER_WINDOW_MS = 500;
    // Partial line buffer for RoomTracker line-by-line feeding.
    // TCP chunks can split a line mid-content (e.g., "Slum Street" in one chunk,
    // ", Crossroads\r\n..." in the next). The last element from Split('\n') is
    // held back if the chunk didn't end with a newline, then prepended to the
    // next chunk to reassemble the complete line.
    private string _partialLine = string.Empty;

    
    // Pattern Detection
    private static readonly Regex HpManaRegex = new(@"\[HP=(\d+)(?:/(\d+))?/(MA|KAI)=(\d+)(?:/(\d+))?\]", RegexOptions.Compiled);
    private static readonly Regex DamageRegex = new(@"for \d+ damage!", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CombatEngagedRegex = new(@"\*Combat Engaged\*", RegexOptions.Compiled);
    private static readonly Regex CombatOffRegex = new(@"\*Combat Off\*", RegexOptions.Compiled);
    private static readonly Regex PlayerDeathRegex = new(@"due to a miracle, you have been saved", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    public MessageRouter(GameManager gameManager)
    {
        _gameManager = gameManager;
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
        _gameManager.PlayerStateManager.IsInLoginPhase = true;
    }
    
    /// <summary>
    /// Process a message from the MUD server.
    /// Routes to all sub-managers and detects game state changes.
    /// </summary>
    public void ProcessMessage(string text)
    {
        var wasPaused = _gameManager.ShouldPauseCommands;
        
        // --- Combat manager room parsing (MUST run before RoomTracker) ---
        // CombatManager parses "Also here:" to populate the enemy list.
        // RoomTracker parses "Obvious exits:" which fires OnRoomChanged,
        // triggering AutoWalkManager to send the next move command.
        // Both lines arrive in the same text chunk. If CombatManager runs
        // after RoomTracker, the walker moves before enemies are detected,
        // causing attack commands to be sent to the wrong room.
        _gameManager.CombatManager.ProcessMessage(text);
        
        // --- Feed lines to room tracker ---
        // --- Feed complete lines to room tracker ---
        // TCP can split a line across chunks (e.g., "Slum Street" + ", Crossroads\r\n").
        // Hold back the last fragment if the chunk didn't end with a newline.
        var roomText = _partialLine + text;
        _partialLine = string.Empty;

        var lines = roomText.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i == lines.Length - 1 && !roomText.EndsWith('\n'))
            {
                // Last element and chunk didn't end with newline — incomplete line
                _partialLine = lines[i];
                break;
            }
            _gameManager.RoomTracker.ProcessLine(lines[i].TrimEnd('\r'));
        }
        
        // --- Dispatch to sub-managers ---
        _gameManager.RemoteCommandManager.ProcessMessage(text);
        _gameManager.PartyManager.ProcessMessage(text);
        _gameManager.PlayerStateManager.ProcessMessage(text);
        _gameManager.CureManager.ProcessMessage(text, _gameManager.PartyManager.PartyMembers.ToList());
        _gameManager.PlayerDatabase.ProcessMessage(text);
        
        // --- Buff-specific message processing (cast success/failure/expire) ---
        _gameManager.BuffManager.ProcessMessage(text);
        
        // --- Check if pause state changed ---
        if (wasPaused != _gameManager.ShouldPauseCommands)
        {
            OnPauseStateChanged?.Invoke(_gameManager.ShouldPauseCommands);
        }
        
        // --- Combat state changes ---
        if (CombatEngagedRegex.IsMatch(text))
        {
            OnCombatStateChanged?.Invoke(true);
            _gameManager.CombatManager.OnCombatEngaged();
        }
        else if (CombatOffRegex.IsMatch(text))
        {
            OnCombatStateChanged?.Invoke(false);
            _gameManager.CombatManager.OnCombatEnded();
        }
        
        // --- HP/Mana updates ---
        var hpMatch = HpManaRegex.Match(text);
        if (hpMatch.Success)
        {
            ParsePlayerStats(hpMatch);
            
            // HP bar means we're in-game - login phase complete
            if (_isInLoginPhase)
            {
                _isInLoginPhase = false;
                _gameManager.PlayerStateManager.IsInLoginPhase = false;
                OnLoginComplete?.Invoke();
            }
        }
        
        // --- Damage detection for tick timing ---
        if (DamageRegex.IsMatch(text))
        {
            DetectCombatTick();
        }
        
        // --- Death detection ---
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
            
            if (_nextTickTime.HasValue)
            {
                var drift = Math.Abs((now - _nextTickTime.Value).TotalMilliseconds);
                if (drift < 1500 || drift > 3500)
                {
                    OnCombatTickDetected?.Invoke();
                    _nextTickTime = now.AddMilliseconds(TICK_INTERVAL_MS);
                }
            }
            else
            {
                OnCombatTickDetected?.Invoke();
                _nextTickTime = now.AddMilliseconds(TICK_INTERVAL_MS);
            }
        }
        
        _lastDamageMessageTime = now;
    }
}
