namespace MudProxyViewer;

/// <summary>
/// Evaluates HP and mana thresholds to decide health management actions:
/// rest, meditate, flee (run), or emergency disconnect (hang).
///
/// Does NOT execute actions directly — fires events that GameManager
/// wires to the appropriate managers (TelnetConnection, AutoWalkManager, etc.).
///
/// Owned by GameManager. Consults PlayerStateManager for current stats
/// and CombatManager for enemy presence via injected delegates.
/// </summary>
public class HealthManager
{
    // ── Dependencies (injected as delegates) ──
    private readonly Func<int> _getHpPercent;
    private readonly Func<int> _getManaPercent;
    private readonly Func<int> _getMaxHp;
    private readonly Func<int> _getMaxMana;
    private readonly Func<int> _getCurrentHp;
    private readonly Func<int> _getCurrentMana;
    private readonly Func<bool> _isResting;
    private readonly Func<bool> _isMeditating;
    private readonly Func<bool> _inCombat;
    private readonly Func<int> _getEnemyCount;
    private readonly Func<bool> _isAnyAutomationEnabled;
    private readonly Func<HealthSettings> _getSettings;
    private readonly Action<string> _logMessage;

    // ── State tracking ──
    private HealthAction _currentAction = HealthAction.None;
    private bool _hangupTriggered = false;
    private bool _isRunning = false;
    private int _roomsFled = 0;
    private bool _enabled = true;
    private DateTime _lastRestMeditateSent = DateTime.MinValue;
    private const int REST_MEDITATE_COOLDOWN_MS = 2000;
    private DateTime _lastCombatEndTime = DateTime.MinValue;
    private const int POST_COMBAT_DELAY_MS = 250;
    private bool _postCombatPauseWalking = false;
    private System.Timers.Timer? _postCombatEvalTimer;
    private bool _restingForMana = false;

    // Direction reversal map for flee backtracking
    private static readonly Dictionary<string, string> ReverseDirections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["n"] = "s", ["s"] = "n", ["e"] = "w", ["w"] = "e",
        ["ne"] = "sw", ["sw"] = "ne", ["nw"] = "se", ["se"] = "nw",
        ["u"] = "d", ["d"] = "u",
        ["north"] = "south", ["south"] = "north", ["east"] = "west", ["west"] = "east",
        ["northeast"] = "southwest", ["southwest"] = "northeast",
        ["northwest"] = "southeast", ["southeast"] = "northwest",
        ["up"] = "down", ["down"] = "up"
    };

    // ── Events ──
    public event Action<string>? OnSendCommand;
    public event Action? OnHangupRequested;
    public event Action? OnBlockReconnect;
    public event Action? OnAllowReconnect;
    public event Action<string>? OnLogMessage;
    public event Action<HealthAction>? OnHealthActionChanged;

    public HealthManager(
        Func<int> getHpPercent,
        Func<int> getManaPercent,
        Func<int> getMaxHp,
        Func<int> getMaxMana,
        Func<int> getCurrentHp,
        Func<int> getCurrentMana,
        Func<bool> isResting,
        Func<bool> isMeditating,
        Func<bool> inCombat,
        Func<int> getEnemyCount,
        Func<bool> isAnyAutomationEnabled,
        Func<HealthSettings> getSettings,
        Action<string> logMessage)
    {
        _getHpPercent = getHpPercent;
        _getManaPercent = getManaPercent;
        _getMaxHp = getMaxHp;
        _getMaxMana = getMaxMana;
        _getCurrentHp = getCurrentHp;
        _getCurrentMana = getCurrentMana;
        _isResting = isResting;
        _isMeditating = isMeditating;
        _inCombat = inCombat;
        _getEnemyCount = getEnemyCount;
        _isAnyAutomationEnabled = isAnyAutomationEnabled;
        _getSettings = getSettings;
        _logMessage = logMessage;
    }

    #region Properties

    public HealthAction CurrentAction => _currentAction;
    public bool IsRunning => _isRunning;
    public bool HangupTriggered => _hangupTriggered;

    /// <summary>
    /// Returns true when walking should be paused for resting/meditating.
    /// AutoWalkManager checks this separately from ShouldPauseCommands
    /// so that CastCoordinator can still cast buffs/heals while resting.
    /// </summary>
    public bool ShouldPauseWalking =>
        _currentAction == HealthAction.Resting ||
        _currentAction == HealthAction.Meditating ||
        _postCombatPauseWalking ||
        _isRunning;

    #endregion

    #region Entry Points

    /// <summary>
    /// Called on every HP/mana prompt update. Main evaluation entry point.
    /// </summary>
    public void OnHpPromptReceived()
    {
        if (!_enabled) return;
        Evaluate();
    }

    /// <summary>
    /// Called when the player enters a new room. Used for intelligent run
    /// to check if the new room is safe.
    /// </summary>
    public void OnRoomChanged()
    {
        if (!_isRunning) return;

        var settings = _getSettings();
        var enemyCount = _getEnemyCount();
        var hpPercent = _getHpPercent();

        _roomsFled++;

        if (settings.UseIntelligentRun)
        {
            // Intelligent run: keep going until safe (no enemies) and above threshold
            if (enemyCount == 0)
            {
                StopRunning("reached safe room");
                Evaluate();
            }
            else
            {
                // Still enemies — send another flee move
                OnFleeRequested?.Invoke();
            }
        }
        else
        {
            // Fixed distance: stop after N rooms
            if (_roomsFled >= settings.RunDistance)
            {
                StopRunning($"fled {_roomsFled} rooms");
                Evaluate();
            }
            else
            {
                // Still have rooms to go — send another flee move
                OnFleeRequested?.Invoke();
            }
        }
    }

    /// <summary>
    /// Called when combat ends (*Combat Off* detected). Records the time so
    /// Evaluate() can defer rest/meditate decisions until the room scan has
    /// updated the enemy list (see POST_COMBAT_DELAY_MS).
    ///
    /// Also schedules a deferred Evaluate() call that fires right when the delay
    /// expires. Without this, meditate/rest wouldn't be sent until the next HP
    /// prompt (from auto-par every 5s), wasting seconds of idle time.
    /// </summary>
    public void OnCombatEnded()
    {
        if (!_enabled) return;
        if (!_isAnyAutomationEnabled()) return;
        _lastCombatEndTime = DateTime.Now;

        // Schedule evaluation right after the post-combat delay expires.
        // The +100ms ensures the delay check in Evaluate() will pass.
        _postCombatEvalTimer?.Stop();
        _postCombatEvalTimer?.Dispose();
        _postCombatEvalTimer = new System.Timers.Timer(POST_COMBAT_DELAY_MS + 100);
        _postCombatEvalTimer.AutoReset = false;
        _postCombatEvalTimer.Elapsed += (s, e) =>
        {
            _postCombatEvalTimer?.Dispose();
            _postCombatEvalTimer = null;
            Evaluate();
        };
        _postCombatEvalTimer.Start();
    }

    /// <summary>
    /// Called after a buff or heal spell is cast. If we were resting/meditating,
    /// the cast broke that state — but we do NOT re-send here.
    ///
    /// Re-initiation is handled by OnRestingStateChanged: when the server's HP bar
    /// response arrives without (Resting)/(Meditating), ProcessHpManaPrompt detects
    /// the state change, OnRestingStateChanged clears _currentAction and calls
    /// Evaluate() to determine the correct next action.
    ///
    /// Previously this used Task.Delay(500) to re-send rest/meditate, but that
    /// caused race conditions: the delayed command fired AFTER Evaluate() had
    /// already changed the action (e.g., switching from rest to meditate via
    /// MeditateBeforeResting), resulting in conflicting commands.
    /// </summary>
    public void OnCastCompleted()
    {
        // No action needed — OnRestingStateChanged handles re-initiation.
    }

    /// <summary>
    /// Called when resting/meditating state changes (from PlayerStateManager).
    /// Syncs our action state with actual player state.
    ///
    /// When recovery is interrupted (buff cast, manual spell, etc.), we check the
    /// MAX threshold to decide whether recovery is complete — not the START threshold.
    /// Without this, a player meditating from 18%→50% mana who gets interrupted would
    /// NOT re-send meditate (50% > start threshold) even though mana hasn't reached
    /// the max threshold (e.g., 100%). Walking would resume prematurely.
    /// </summary>
    public void OnRestingStateChanged(bool isResting, bool isMeditating)
    {
        // ALL OFF: clear any active health action so ShouldPauseWalking releases.
        // The player's rest/meditate state will end naturally when they move or
        // enemies appear — we just stop tracking it as an automated action.
        if (!_isAnyAutomationEnabled())
        {
            if (_currentAction != HealthAction.None)
                SetAction(HealthAction.None);
            return;
        }

        if (_currentAction == HealthAction.Resting && !isResting && !isMeditating)
        {
            var settings = _getSettings();
            var hpPercent = _getHpPercent();
            var enemyCount = _getEnemyCount();
            var inCombat = _inCombat();

            if (enemyCount > 0 || inCombat)
            {
                _logMessage("🔄 Resting state lost (enemies present) — clearing");
                SetAction(HealthAction.None);
                return;
            }

            if (hpPercent >= settings.HpRestMaxPercent)
            {
                // HP recovered — let Evaluate() handle mana check / walking resume
                _logMessage($"🔄 Resting interrupted — HP recovered ({hpPercent}%), re-evaluating");
                SetAction(HealthAction.None);
                Evaluate();
            }
            else
            {
                // HP not at max — re-send rest to continue recovery
                _logMessage($"🔄 Resting interrupted — re-sending (HP: {hpPercent}% target: {settings.HpRestMaxPercent}%)");
                OnSendCommand?.Invoke("rest");
                _lastRestMeditateSent = DateTime.Now;
            }
        }
        else if (_currentAction == HealthAction.Meditating && !isMeditating && !isResting)
        {
            var settings = _getSettings();
            var manaPercent = _getManaPercent();
            var enemyCount = _getEnemyCount();
            var inCombat = _inCombat();

            if (enemyCount > 0 || inCombat)
            {
                _logMessage("🔄 Meditating state lost (enemies present) — clearing");
                SetAction(HealthAction.None);
                return;
            }

            if (manaPercent >= settings.ManaRestMaxPercent)
            {
                // Mana recovered — let Evaluate() handle HP check / walking resume
                _logMessage($"🔄 Meditating interrupted — mana recovered ({manaPercent}%), re-evaluating");
                SetAction(HealthAction.None);
                Evaluate();
            }
            else
            {
                // Mana not at max — re-send meditate to continue recovery
                _logMessage($"🔄 Meditating interrupted — re-sending (Mana: {manaPercent}% target: {settings.ManaRestMaxPercent}%)");
                OnSendCommand?.Invoke("meditate");
                _lastRestMeditateSent = DateTime.Now;
            }
        }
    }

    #endregion

    #region Core Evaluation

    /// <summary>
    /// Evaluates all thresholds in priority order and takes the appropriate action.
    /// Priority: Hang > Run > Meditate (if priority) > Rest HP > Meditate Mana > Stop Rest/Meditate
    /// </summary>
    private void Evaluate()
    {
        var settings = _getSettings();
        var hpPercent = _getHpPercent();
        var manaPercent = _getManaPercent();
        var enemyCount = _getEnemyCount();
        var inCombat = _inCombat();
        var isResting = _isResting();
        var isMeditating = _isMeditating();

        // Fast path: if no thresholds are configured and no action in progress, nothing to do.
        // This guarantees default settings (all zeros) are a complete no-op with zero side effects.
        if (settings.HpHangBelowPercent == 0 && settings.HpRunBelowPercent == 0 &&
            settings.HpRestBelowPercent == 0 && settings.ManaRestBelowPercent == 0 &&
            _currentAction == HealthAction.None && !_isRunning)
        {
            return;
        }

        // Priority 1: HANG — emergency disconnect
        if (settings.HpHangBelowPercent > 0 && hpPercent < settings.HpHangBelowPercent)
        {
            bool automationOn = _isAnyAutomationEnabled();
            if (automationOn || settings.AllowHangInAllOff)
            {
                TriggerHangup(hpPercent);
                return;
            }
        }

        // ALL OFF: skip rest/meditate/run when all automation is disabled.
        // Hangup (above) has its own AllowHangInAllOff handling.
        // Clear _postCombatPauseWalking in case we were in the post-combat delay
        // window when ALL OFF was toggled — otherwise the flag stays true and
        // ShouldPauseWalking keeps walking paused indefinitely.
        _postCombatPauseWalking = false;
        if (!_isAnyAutomationEnabled()) return;

        // Priority 2: RUN — flee from enemies
        if (settings.HpRunBelowPercent > 0 && hpPercent < settings.HpRunBelowPercent && enemyCount > 0)
        {
            if (!_isRunning)
            {
                StartRunning(settings);
            }
            return; // Don't evaluate rest while fleeing
        }

        // Enemies present or in combat — can't rest/meditate.
        // Check both enemyCount AND inCombat because the enemy list can be
        // temporarily empty during combat (between room refreshes), but the
        // player is still in combat and cannot rest/meditate.
        if (enemyCount > 0 || inCombat)
        {
            if (_currentAction == HealthAction.Resting || _currentAction == HealthAction.Meditating)
            {
                _logMessage($"⚔️ Enemies/combat detected — clearing {_currentAction} state");
                SetAction(HealthAction.None);
            }
            return;
        }

        // Don't start resting if we're currently running
        if (_isRunning) return;

        // Determine if HP or mana need recovery (computed early for post-combat walking pause)
        bool hpNeedsRest = settings.HpRestBelowPercent > 0 && hpPercent < settings.HpRestBelowPercent;
        bool manaNeedsRest = settings.ManaRestBelowPercent > 0 && manaPercent < settings.ManaRestBelowPercent;

        // Post-combat safety: after *Combat Off*, the server sends the HP prompt
        // BEFORE the room description in the same batch. This means Evaluate() runs
        // with stale enemy data (enemyCount=0, inCombat=false) while enemies may still
        // be in the room. Wait for POST_COMBAT_DELAY_MS to ensure the room scan has
        // processed and the enemy list is accurate before making rest/meditate decisions.
        //
        // While waiting, we must still pause walking if thresholds require rest/meditate.
        // Without this flag, AutoWalkManager sees ShouldPauseWalking=false and resumes
        // walking during the delay, sending the player into rooms with enemies while
        // queuing rest/meditate commands behind the walk commands.
        if ((DateTime.Now - _lastCombatEndTime).TotalMilliseconds < POST_COMBAT_DELAY_MS)
        {
            _postCombatPauseWalking = hpNeedsRest || manaNeedsRest;
            return;
        }

        // Delay has expired — clear the post-combat walking pause flag.
        // Normal health action tracking (_currentAction) takes over from here.
        _postCombatPauseWalking = false;

        // Cooldown check: don't re-send rest/meditate if we sent one recently.
        // Multiple event sources (OnHpPrompt, OnCombatEnded, OnRestingStateChanged)
        // can fire Evaluate() in rapid succession from the same batch of server output.
        // Without throttling, this sends 3-4 redundant rest/meditate commands.
        bool recentlySent = (DateTime.Now - _lastRestMeditateSent).TotalMilliseconds < REST_MEDITATE_COOLDOWN_MS;

        // Priority 3: MEDITATE (if MeditateBeforeResting is set and mana needs it)
        if (manaNeedsRest && settings.UseMeditateAbility && settings.MeditateBeforeResting && hpNeedsRest)
        {
            if (_currentAction != HealthAction.Meditating && !recentlySent)
            {
                SetAction(HealthAction.Meditating);
                OnSendCommand?.Invoke("meditate");
                _lastRestMeditateSent = DateTime.Now;
                _logMessage($"🧘 Meditating (priority) — Mana at {manaPercent}%, HP at {hpPercent}%");
            }
            return;
        }

        // Priority 4: REST for HP
        if (hpNeedsRest)
        {
            if (_currentAction != HealthAction.Resting && !recentlySent)
            {
                _restingForMana = false;
                SetAction(HealthAction.Resting);
                OnSendCommand?.Invoke("rest");
                _lastRestMeditateSent = DateTime.Now;
                _logMessage($"💤 Resting — HP at {hpPercent}%");
            }
            return;
        }

        // Priority 5: MEDITATE/REST for mana (when HP doesn't need resting)
        if (manaNeedsRest)
        {
            if (settings.UseMeditateAbility)
            {
                if (_currentAction != HealthAction.Meditating && !recentlySent)
                {
                    SetAction(HealthAction.Meditating);
                    OnSendCommand?.Invoke("meditate");
                    _lastRestMeditateSent = DateTime.Now;
                    _logMessage($"🧘 Meditating — Mana at {manaPercent}%");
                }
            }
            else
            {
                if (_currentAction != HealthAction.Resting && !recentlySent)
                {
                    _restingForMana = true;
                    SetAction(HealthAction.Resting);
                    OnSendCommand?.Invoke("rest");
                    _lastRestMeditateSent = DateTime.Now;
                    _logMessage($"💤 Resting for mana — Mana at {manaPercent}%");
                }
            }
            return;
        }

        // Priority 6: STOP RESTING — check the resource that triggered rest.
        // _restingForMana tracks whether we started resting for HP (Priority 4) or
        // mana (Priority 5, no meditate ability). This prevents two bugs:
        // - Resting for HP but waiting for mana to also hit max (20+ second idle)
        // - Resting for mana but breaking when HP hits max (ping-pong rest/break)
        if (_currentAction == HealthAction.Resting && isResting)
        {
            bool recovered = _restingForMana
                ? manaPercent >= settings.ManaRestMaxPercent
                : hpPercent >= settings.HpRestMaxPercent;

            if (recovered)
            {
                SetAction(HealthAction.None);
                // Send a movement command to break resting state (empty string sends enter)
                OnSendCommand?.Invoke("");
                _logMessage($"🏃 Done resting — HP at {hpPercent}%, Mana at {manaPercent}%");
            }
            return;
        }

        // Priority 7: STOP MEDITATING — mana recovered to max threshold
        if (_currentAction == HealthAction.Meditating && isMeditating)
        {
            if (manaPercent >= settings.ManaRestMaxPercent)
            {
                // Check if HP still needs resting
                bool hpStillLow = settings.HpRestBelowPercent > 0 && hpPercent < settings.HpRestBelowPercent;
                if (hpStillLow)
                {
                    // Switch from meditating to resting for HP
                    _restingForMana = false;
                    SetAction(HealthAction.Resting);
                    OnSendCommand?.Invoke("rest");
                    _lastRestMeditateSent = DateTime.Now;
                    _logMessage($"💤 Mana full, switching to rest — HP at {hpPercent}%");
                }
                else
                {
                    SetAction(HealthAction.None);
                    OnSendCommand?.Invoke("");
                    _logMessage($"🏃 Done meditating — Mana at {manaPercent}%");
                }
            }
            return;
        }

        // If no threshold is active, clear action.
        // Guard with !recentlySent: when OnRestingStateChanged re-sends a rest/meditate
        // command, Evaluate() fires immediately after (same HP prompt batch). At that point
        // _currentAction is still set but the player isn't resting/meditating yet (server
        // hasn't confirmed). Without this guard, the catch-all clears the action and walking
        // resumes before the server can process the re-sent command.
        if (_currentAction != HealthAction.None && !isResting && !isMeditating && !recentlySent)
        {
            SetAction(HealthAction.None);
        }
    }

    #endregion

    #region Run (Flee) Logic

    /// <summary>
    /// Gets the reverse direction for backtracking. Returns null if unknown.
    /// </summary>
    public static string? GetReverseDirection(string direction)
    {
        return ReverseDirections.TryGetValue(direction, out var reverse) ? reverse : null;
    }

    private void StartRunning(HealthSettings settings)
    {
        _isRunning = true;
        _roomsFled = 0;
        SetAction(HealthAction.Running);

        if (settings.UseIntelligentRun)
            _logMessage($"🏃 Fleeing — HP at {_getHpPercent()}% (intelligent run, until safe)");
        else
            _logMessage($"🏃 Fleeing — HP at {_getHpPercent()}% (running {settings.RunDistance} rooms)");

        // The actual movement command is sent by GameManager which knows the current
        // path steps and can determine the backtrack direction. We fire an event.
        OnFleeRequested?.Invoke();
    }

    private void StopRunning(string reason)
    {
        _isRunning = false;
        SetAction(HealthAction.None);
        _logMessage($"🛑 Stopped fleeing — {reason} (fled {_roomsFled} rooms)");
        OnFleeCompleted?.Invoke();
    }

    /// <summary>
    /// Fired when flee needs to start. GameManager wires this to send the
    /// actual backtrack movement command.
    /// </summary>
    public event Action? OnFleeRequested;

    /// <summary>
    /// Fired when flee completes. GameManager can resume loops/walks.
    /// </summary>
    public event Action? OnFleeCompleted;

    /// <summary>
    /// Called by GameManager to send the next flee movement.
    /// Returns true if still fleeing and another move is needed.
    /// </summary>
    public bool NeedsContinuedFlee()
    {
        if (!_isRunning) return false;
        var settings = _getSettings();

        if (settings.UseIntelligentRun)
        {
            return _getEnemyCount() > 0;
        }
        else
        {
            return _roomsFled < settings.RunDistance;
        }
    }

    #endregion

    #region Hangup Logic

    private void TriggerHangup(int hpPercent)
    {
        if (_hangupTriggered) return;

        _hangupTriggered = true;
        SetAction(HealthAction.HungUp);
        _logMessage($"🚨 EMERGENCY HANGUP — HP at {hpPercent}%! Disconnecting immediately.");
        OnLogMessage?.Invoke($"🚨 EMERGENCY HANGUP — HP at {hpPercent}%! Disconnecting immediately.");
        OnHangupRequested?.Invoke();
        OnBlockReconnect?.Invoke();
    }

    /// <summary>
    /// Called when the user manually reconnects after a hangup.
    /// Clears the hangup state so thresholds can trigger again.
    /// </summary>
    public void ClearHangupState()
    {
        _hangupTriggered = false;
        if (_currentAction == HealthAction.HungUp)
            SetAction(HealthAction.None);
        OnAllowReconnect?.Invoke();
    }

    #endregion

    #region State Management

    private void SetAction(HealthAction action)
    {
        if (_currentAction == action) return;
        _currentAction = action;
        OnHealthActionChanged?.Invoke(action);
    }

    /// <summary>
    /// Reset all state. Called on disconnect.
    /// </summary>
    public void Reset()
    {
        _currentAction = HealthAction.None;
        _isRunning = false;
        _roomsFled = 0;
        _postCombatPauseWalking = false;
        _restingForMana = false;
        _postCombatEvalTimer?.Stop();
        _postCombatEvalTimer?.Dispose();
        _postCombatEvalTimer = null;
        // Don't clear _hangupTriggered here — it persists across disconnects
        // so that reconnect-blocking remains in effect after a health hangup.
        OnHealthActionChanged?.Invoke(HealthAction.None);
    }

    #endregion
}

public enum HealthAction
{
    None,
    Resting,
    Meditating,
    Running,
    HungUp
}
