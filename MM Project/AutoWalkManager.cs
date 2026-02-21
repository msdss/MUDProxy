namespace MudProxyViewer;

/// <summary>
/// Manages automated walking along a path calculated by RoomGraphManager.
/// 
/// Sends one movement command at a time, waits for RoomTracker to confirm
/// arrival before sending the next. Pauses on combat, resumes when clear.
/// Self-heals by recalculating the path if the player ends up in an unexpected room.
/// 
/// Integration:
///   - Created by GameManager, wired to RoomTracker.OnRoomChanged
///   - MainForm notifies OnCombatStateChanged for pause/resume
///   - WalkToDialog provides the UI for starting/stopping walks
/// </summary>
public class AutoWalkManager
{
    // ── Dependencies (injected) ──
    private readonly RoomTracker _roomTracker;
    private readonly RoomGraphManager _roomGraph;
    private readonly Func<int> _getEnemyCount;          // CombatManager.CurrentRoomEnemies.Count
    private readonly Func<bool> _shouldPauseCommands;    // GameManager.ShouldPauseCommands
    private readonly Action _clearRoomEnemies;            // CombatManager.ClearEnemyList
    private readonly Action<string> _sendCommand;
    private readonly Action<string> _logMessage;

    // ── Walk state ──
    private List<PathStep> _steps = new();
    private int _currentStepIndex = 0;
    private string _destinationKey = "";
    private string _destinationName = "";
    private AutoWalkState _state = AutoWalkState.Idle;
    private WalkMode _walkMode = WalkMode.Normal;

    // ── Timeout handling ──
    private System.Timers.Timer? _stepTimer;
    private bool _hasRetriedCurrentStep = false;
    private const int StepTimeoutMs = 10_000;
    private System.Timers.Timer? _combatRetryTimer;

    // ── Recalculation limits ──
    private int _recalcCount = 0;
    private const int MaxRecalculations = 3;

    // ── Pause polling ──
    private System.Timers.Timer? _pausePollTimer;
    private const int PausePollIntervalMs = 1000;

    // ── Events ──
    public event Action<int, int, string>? OnWalkProgress;     // (currentStep, totalSteps, roomName)
    public event Action<AutoWalkState>? OnWalkStateChanged;
    public event Action<string>? OnWalkCompleted;               // destinationName
    public event Action<string>? OnWalkFailed;                  // reason
    public event Action<string>? OnLogMessage;

    public AutoWalkManager(
        RoomTracker roomTracker,
        RoomGraphManager roomGraph,
        Func<int> getEnemyCount,
        Func<bool> shouldPauseCommands,
        Action clearRoomEnemies,
        Action<string> sendCommand,
        Action<string> logMessage)
    {
        _roomTracker = roomTracker;
        _roomGraph = roomGraph;
        _getEnemyCount = getEnemyCount;
        _shouldPauseCommands = shouldPauseCommands;
        _clearRoomEnemies = clearRoomEnemies;
        _sendCommand = sendCommand;
        _logMessage = logMessage;

        _roomTracker.OnRoomChanged += OnRoomChanged;
    }

    #region Properties

    public AutoWalkState State => _state;
    public int CurrentStepIndex => _currentStepIndex;
    public int TotalSteps => _steps.Count;
    public string DestinationName => _destinationName;
    public string DestinationKey => _destinationKey;
    public bool IsActive => _state == AutoWalkState.Walking || _state == AutoWalkState.WaitingForCombat || _state == AutoWalkState.Paused;

    #endregion

    #region Public API

    /// <summary>
    /// Start walking along a path to the destination.
    /// Call with a PathResult from RoomGraphManager.FindPath().
    /// </summary>
    public bool StartWalk(PathResult path, WalkMode mode = WalkMode.Normal)
    {
        if (path == null || !path.Success || path.Steps.Count == 0)
        {
            OnWalkFailed?.Invoke("No valid path to walk.");
            return false;
        }

        // Stop any existing walk
        if (IsActive)
            Stop();

        _steps = new List<PathStep>(path.Steps);
        _currentStepIndex = 0;
        _destinationKey = path.DestinationKey;
        _recalcCount = 0;
        _walkMode = mode;

        // Resolve destination name
        var destRoom = _roomGraph.GetRoom(_destinationKey);
        _destinationName = destRoom?.Name ?? _destinationKey;

        _logMessage($"🚶 Auto-walk started: {_destinationName} ({_steps.Count} steps)");
        OnLogMessage?.Invoke($"🚶 Walking to [{_destinationKey}] {_destinationName} — {_steps.Count} steps");

        SetState(AutoWalkState.Walking);
        SendNextStep();
        return true;
    }

    /// <summary>
    /// Stop the current walk immediately.
    /// </summary>
    public void Stop()
    {
        if (_state == AutoWalkState.Idle)
            return;

        StopTimers();
        var wasActive = IsActive;
        SetState(AutoWalkState.Idle);

        if (wasActive)
        {
            _logMessage($"🛑 Auto-walk stopped at step {_currentStepIndex + 1}/{_steps.Count}");
            OnLogMessage?.Invoke("🛑 Auto-walk cancelled");
        }

        _steps.Clear();
        _currentStepIndex = 0;
    }

    /// <summary>
    /// Notify the walker that combat state has changed.
    /// Called by MainForm when MessageRouter fires OnCombatStateChanged.
    /// </summary>
    public void OnCombatStateChanged(bool inCombat)
    {
        _combatResumeRetries = 0;

        if (inCombat && (_state == AutoWalkState.Walking || _state == AutoWalkState.WaitingForCombat))
        {
            // Combat engaged (or re-engaged) — pause walking and cancel any pending retry
            StopTimers();
            SetState(AutoWalkState.WaitingForCombat);
            if (_state != AutoWalkState.WaitingForCombat)
                OnLogMessage?.Invoke("⚔️ Auto-walk paused — combat engaged");
        }
        else if (_state == AutoWalkState.WaitingForCombat && !inCombat)
        {
            // Combat ended — check if safe to resume
            TryResumeAfterCombat();
        }
    }

    /// <summary>
    /// Notify the walker that the player has died.
    /// Aborts the walk immediately.
    /// </summary>
    public void OnPlayerDeath()
    {
        if (!IsActive)
            return;

        StopTimers();
        SetState(AutoWalkState.Failed);
        OnLogMessage?.Invoke("💀 Auto-walk aborted — player died");
        OnWalkFailed?.Invoke("Player died during walk.");
    }

    /// <summary>
    /// Notify the walker that the connection was lost.
    /// Aborts the walk immediately.
    /// </summary>
    public void OnDisconnected()
    {
        if (!IsActive)
            return;

        StopTimers();
        SetState(AutoWalkState.Idle);
        _steps.Clear();
        _currentStepIndex = 0;
    }

    #endregion

    #region Step Execution

    /// <summary>
    /// Send the current step's command and start the timeout timer.
    /// </summary>
    private void SendNextStep()
    {
        // Check if commands are paused (training screen, manual pause, etc.)
        if (_shouldPauseCommands())
        {
            _logMessage("⏸️ Auto-walk paused — commands paused");
            SetState(AutoWalkState.Paused);
            StartPausePollTimer();
            return;
        }

        if (_currentStepIndex >= _steps.Count)
        {
            // We've run out of steps — should have been caught by arrival check
            WalkCompleted();
            return;
        }

        var step = _steps[_currentStepIndex];
        _hasRetriedCurrentStep = false;

        // Verify the player is in the room this step expects to start from.
        // If not (combat drift, detection failure, etc.), recalculate the path
        // instead of sending a command that won't make sense from this room.
        var currentRoom = _roomTracker.CurrentRoom;
        if (currentRoom != null && !string.IsNullOrEmpty(step.FromKey) &&
            currentRoom.Key != step.FromKey)
        {
            _logMessage($"⚠️ Expected to be in [{step.FromKey}] but in [{currentRoom.Key}] — recalculating");
            RecalculatePath(currentRoom);
            return;
        }

        // Check for enemies in the room before sending move command.
        // CombatManager has already processed "Also here:" (it arrives before
        // "Obvious exits:" in the server output), so enemy data is current.
        var enemies = _getEnemyCount();
        if (enemies > 0)
        {
            _logMessage($"⚔️ {enemies} enemies in room — pausing walk for combat");
            StopTimers();
            SetState(AutoWalkState.WaitingForCombat);
            return;
        }

        OnWalkProgress?.Invoke(_currentStepIndex + 1, _steps.Count, step.ToName);

        _logMessage($"🔬 WALK DEBUG: Step {_currentStepIndex + 1}/{_steps.Count} cmd='{step.Command}' toKey='{step.ToKey}' fromKey='{step.FromKey}' trackerRoom='{_roomTracker.CurrentRoomKey}'");
        _roomTracker.OnPlayerCommand(step.Command);
        _sendCommand(step.Command);

        // Clear enemy list after sending the move command.
        // This prevents stale enemies from the current room causing a
        // false pause when we arrive at the next room (which may have no
        // "Also here:" line to trigger a refresh). The next room's
        // "Also here:" will repopulate enemies before OnRoomChanged fires.
        _clearRoomEnemies();

        StartStepTimer();
    }

    /// <summary>
    /// Handle room arrival. Check if it matches expectations and advance or recalculate.
    /// </summary>
    private void OnRoomChanged(RoomNode? newRoom)
    {
        // Track position during both Walking and WaitingForCombat states.
        // The player may physically move after combat engages (command was already sent).
        // We must still advance the step index so we don't repeat the step on resume.
        if (_state != AutoWalkState.Walking && _state != AutoWalkState.WaitingForCombat)
            return;

        if (newRoom == null)
            return;

        StopStepTimer();

        // Check if we've arrived at the final destination
        if (newRoom.Key == _destinationKey)
        {
            WalkCompleted();
            return;
        }

        // Check if we arrived at the expected next room
        if (_currentStepIndex < _steps.Count)
        {
            var expectedKey = _steps[_currentStepIndex].ToKey;
            if (newRoom.Key == expectedKey)
            {
                // Expected arrival — advance to next step
                _currentStepIndex++;

                if (_currentStepIndex >= _steps.Count)
                {
                    WalkCompleted();
                    return;
                }

                // Only send next step if actively walking (not paused for combat)
                if (_state == AutoWalkState.Walking)
                    SendNextStep();
                return;
            }
        }

        // Unexpected room — only recalculate if actively walking
        if (_state == AutoWalkState.Walking)
            RecalculatePath(newRoom);
    }

    /// <summary>
    /// Recalculate path from current room to destination.
    /// </summary>
    private void RecalculatePath(RoomNode currentRoom)
    {
        _recalcCount++;

        if (_recalcCount > MaxRecalculations)
        {
            StopTimers();
            SetState(AutoWalkState.Failed);
            OnLogMessage?.Invoke($"❌ Auto-walk failed — exceeded {MaxRecalculations} recalculations");
            OnWalkFailed?.Invoke("Too many path recalculations. Walk aborted.");
            return;
        }

        OnLogMessage?.Invoke($"⚠️ Unexpected room: [{currentRoom.Key}] {currentRoom.Name} — recalculating path (attempt {_recalcCount}/{MaxRecalculations})");

        var newPath = _roomGraph.FindPath(currentRoom.Key, _destinationKey);

        if (!newPath.Success || newPath.Steps.Count == 0)
        {
            // Check if we're already at the destination
            if (currentRoom.Key == _destinationKey)
            {
                WalkCompleted();
                return;
            }

            StopTimers();
            SetState(AutoWalkState.Failed);
            OnLogMessage?.Invoke($"❌ Auto-walk failed — no path from [{currentRoom.Key}] to [{_destinationKey}]");
            OnWalkFailed?.Invoke($"Cannot reach {_destinationName} from current location.");
            return;
        }

        // Replace remaining steps with new path
        _steps = new List<PathStep>(newPath.Steps);
        _currentStepIndex = 0;

        OnLogMessage?.Invoke($"🔄 Path recalculated: {_steps.Count} steps remaining");
        SendNextStep();
    }

    /// <summary>
    /// Walk completed successfully.
    /// </summary>
    private void WalkCompleted()
    {
        StopTimers();
        SetState(AutoWalkState.Completed);
        _logMessage($"✅ Arrived at {_destinationName}");
        OnLogMessage?.Invoke($"✅ Auto-walk complete — arrived at [{_destinationKey}] {_destinationName}");
        OnWalkCompleted?.Invoke(_destinationName);
    }

    #endregion

    #region Combat Pause/Resume

    /// <summary>
    /// After combat ends, check if it's safe to resume walking.
    /// Uses a short retry because the combat manager may not have
    /// cleared its enemy list yet when this fires.
    /// </summary>
    private int _combatResumeRetries = 0;
    private const int MaxCombatResumeRetries = 3;
    private const int CombatResumeRetryMs = 1000;

    private void TryResumeAfterCombat()
    {
        if (_state != AutoWalkState.WaitingForCombat)
            return;

        var enemyCount = _getEnemyCount();
        if (enemyCount > 0)
        {
            if (_combatResumeRetries < MaxCombatResumeRetries)
            {
                _combatResumeRetries++;
                _logMessage($"⚔️ Combat ended but {enemyCount} enemies reported — rechecking ({_combatResumeRetries}/{MaxCombatResumeRetries})");
                StopCombatRetryTimer();
                _combatRetryTimer = new System.Timers.Timer(CombatResumeRetryMs);
                _combatRetryTimer.AutoReset = false;
                _combatRetryTimer.Elapsed += (s, e) =>
                {
                    StopCombatRetryTimer();
                    TryResumeAfterCombat();
                };
                _combatRetryTimer.Start();
                return;
            }

            // Still enemies after retries — genuinely waiting for combat to re-engage
            _logMessage($"⚔️ {enemyCount} enemies remain after retries — staying paused");
            _combatResumeRetries = 0;
            return;
        }

        // No enemies, safe to resume
        _combatResumeRetries = 0;
        _logMessage("▶️ Auto-walk resuming — combat cleared");
        SetState(AutoWalkState.Walking);

        // Check if the player is still where we expect them to be.
        var currentRoom = _roomTracker.CurrentRoom;
        if (currentRoom != null && _currentStepIndex < _steps.Count)
        {
            var expectedFromKey = _steps[_currentStepIndex].FromKey;
            if (currentRoom.Key != expectedFromKey)
            {
                _logMessage($"📍 Position changed during combat: expected [{expectedFromKey}], now [{currentRoom.Key}] — recalculating");
                RecalculatePath(currentRoom);
                return;
            }
        }

        SendNextStep();
    }

    #endregion

    #region Timeout Handling

    private void StartStepTimer()
    {
        StopStepTimer();
        _stepTimer = new System.Timers.Timer(StepTimeoutMs);
        _stepTimer.AutoReset = false;
        _stepTimer.Elapsed += (s, e) => OnStepTimeout();
        _stepTimer.Start();
    }

    private void StopStepTimer()
    {
        _stepTimer?.Stop();
        _stepTimer?.Dispose();
        _stepTimer = null;
    }

    private void OnStepTimeout()
    {
        if (_state != AutoWalkState.Walking)
            return;

        if (!_hasRetriedCurrentStep && _currentStepIndex < _steps.Count)
        {
            // Retry once — must re-register the pending move with RoomTracker
            // so room detection has movement context. Without this, Guard 1
            // treats the server response as a redisplay and skips it.
            _hasRetriedCurrentStep = true;
            var step = _steps[_currentStepIndex];
            _logMessage($"⏱️ Step timeout — retrying: {step.Command}");
            _roomTracker.OnPlayerCommand(step.Command);
            _sendCommand(step.Command);
            StartStepTimer();
            return;
        }

        // Second timeout — give up
        StopTimers();
        SetState(AutoWalkState.Failed);
        _logMessage("❌ Auto-walk failed — step timed out after retry");
        OnWalkFailed?.Invoke("Movement timed out. The path may be blocked.");
    }

    #endregion

    #region Pause Polling

    /// <summary>
    /// Poll ShouldPauseCommands until it clears, then resume walking.
    /// </summary>
    private void StartPausePollTimer()
    {
        StopPausePollTimer();
        _pausePollTimer = new System.Timers.Timer(PausePollIntervalMs);
        _pausePollTimer.AutoReset = true;
        _pausePollTimer.Elapsed += (s, e) => OnPausePollTick();
        _pausePollTimer.Start();
    }

    private void StopPausePollTimer()
    {
        _pausePollTimer?.Stop();
        _pausePollTimer?.Dispose();
        _pausePollTimer = null;
    }

    private void StopCombatRetryTimer()
    {
        _combatRetryTimer?.Stop();
        _combatRetryTimer?.Dispose();
        _combatRetryTimer = null;
    }

    private void OnPausePollTick()
    {
        if (_state != AutoWalkState.Paused)
        {
            StopPausePollTimer();
            return;
        }

        if (!_shouldPauseCommands())
        {
            StopPausePollTimer();
            OnLogMessage?.Invoke("▶️ Auto-walk resuming — commands unpaused");
            SetState(AutoWalkState.Walking);
            SendNextStep();
        }
    }

    #endregion

    #region Helpers

    private void SetState(AutoWalkState newState)
    {
        if (_state != newState)
        {
            _state = newState;
            OnWalkStateChanged?.Invoke(newState);
        }
    }

    private void StopTimers()
    {
        StopStepTimer();
        StopPausePollTimer();
        StopCombatRetryTimer();
    }

    #endregion
}

/// <summary>
/// Auto-walk state machine states.
/// </summary>
public enum AutoWalkState
{
    Idle,
    Walking,
    WaitingForCombat,
    Paused,
    Completed,
    Failed
}

/// <summary>
/// Walk behavior mode.
/// Only Normal is implemented currently.
/// </summary>
public enum WalkMode
{
    /// <summary>Pause on combat, resume when enemies are cleared.</summary>
    Normal,

    // Future modes:
    // /// <summary>Bypass combat entirely — keep moving.</summary>
    // Run,
    // /// <summary>Fight while walking — don't stop for combat.</summary>
    // RunAndGun
}
