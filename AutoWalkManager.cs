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
    // ── Door handling sub-states ──
    private enum DoorSubState { None, WaitingForBash, WaitingForPick, WaitingForOpen, WaitingForMove }
    private enum SearchSubState { None, WaitingForSearch, WaitingForMove }
    private enum MultiActionSubState { None, WaitingForActionDelay, WaitingForMove }

    // ── Dependencies (injected) ──
    private readonly RoomTracker _roomTracker;
    private readonly RoomGraphManager _roomGraph;
    private readonly Func<int> _getEnemyCount;          // CombatManager.CurrentRoomEnemies.Count
    private readonly Func<bool> _shouldPauseCommands;    // GameManager.ShouldPauseCommands
    private readonly Action _clearRoomEnemies;            // CombatManager.ClearEnemyList
    private readonly Action _clearAlsoHereDedup;          // CombatManager.ClearAlsoHereDedup
    private readonly Func<bool> _usePicklock;             // NavigationSettings.UsePicklockInsteadOfBash
    private readonly Func<int> _getMaxDoorAttempts;       // NavigationSettings.MaxDoorAttempts
    private readonly Func<int> _getMaxSearchAttempts;    // NavigationSettings.MaxSearchAttempts
    private readonly Func<int> _getMultiActionDelayMs;  // NavigationSettings.MultiActionDelayMs
    private readonly Func<int> _getMaxRemoteActionRetries;  // NavigationSettings.MaxRemoteActionRetries
    private readonly Func<Func<RoomExit, bool>> _getExitFilter;  // BFS door stat filter
    private readonly Func<PlayerInfo> _getPlayerInfo;             // Player stats for door bypass decisions
    private readonly Action<string> _sendCommand;
    private readonly Action<string> _logMessage;

    // ── Walk state ──
    private List<PathStep> _steps = new();
    private int _currentStepIndex = 0;
    private string _destinationKey = "";
    private string _destinationName = "";
    private AutoWalkState _state = AutoWalkState.Idle;
    private WalkMode _walkMode = WalkMode.Normal;

    // ── Duplicate send prevention ──
    private int _lastSentStepIndex = -1;
    private DateTime _lastStepSendTime = DateTime.MinValue;

    // ── Timeout handling ──
    private System.Timers.Timer? _stepTimer;
    private bool _hasRetriedCurrentStep = false;
    private const int StepTimeoutMs = 10_000;
    private System.Timers.Timer? _combatRetryTimer;
    private System.Timers.Timer? _combatVerificationTimer;
    private const int CombatVerificationDelayMs = 5000;
    private const int CombatVerificationRecheckMs = 2000;
    private System.Timers.Timer? _combatHeartbeatTimer;
    private const int CombatHeartbeatTimeoutMs = 10_000;
    
    // ── Timeout re-sync ──
    // When a step times out twice, we re-sync by sending an empty command to
    // trigger a room display, then compare the detected room against the step's
    // FromKey/ToKey to determine our actual position before failing/retrying.
    private bool _resyncInProgress = false;
    private System.Timers.Timer? _resyncTimer;
    private const int ResyncTimeoutMs = 10_000;

    // ── Debug logging ──
    private DebugLogWriter? _debugLog;

    /// <summary>
    /// Set the debug log writer. Caller manages lifecycle (create/close).
    /// When non-null, walk events are logged to the file.
    /// </summary>
    public void SetDebugLog(DebugLogWriter? log) => _debugLog = log;

    // ── Recalculation limits ──
    private int _recalcCount = 0;
    private const int MaxRecalculations = 3;

    // ── Door handling state ──
    private bool _doorStepActive = false;
    private DoorSubState _doorSubState = DoorSubState.None;
    private int _doorRetryCount = 0;
    private int _doorClosedRetryCount = 0;
    private string _doorDirection = "";
    private const int MaxDoorClosedRetries = 2;
    private System.Timers.Timer? _doorBypassDelayTimer;

    // ── Search handling state ──
    private bool _searchStepActive = false;
    private SearchSubState _searchSubState = SearchSubState.None;
    private int _searchRetryCount = 0;
    private string _searchDirection = "";

    // ── Multi-action hidden exit handling state ──
    private bool _multiActionStepActive = false;
    private MultiActionSubState _multiActionSubState = MultiActionSubState.None;
    private int _multiActionCurrentIndex = 0;
    private List<ExitAction>? _multiActionSteps = null;
    private System.Timers.Timer? _multiActionDelayTimer;

    // ── Remote action step handling state ──
    private bool _remoteActionStepActive = false;
    private System.Timers.Timer? _remoteActionDelayTimer;
    private int _remoteActionRetryCount = 0;

    private static readonly Dictionary<string, string> DirectionFullNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["n"] = "north", ["s"] = "south", ["e"] = "east", ["w"] = "west",
        ["ne"] = "northeast", ["nw"] = "northwest", ["se"] = "southeast", ["sw"] = "southwest",
        ["u"] = "up", ["d"] = "down"
    };

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
        Action clearAlsoHereDedup,
        Func<bool> usePicklock,
        Func<int> getMaxDoorAttempts,
        Func<int> getMaxSearchAttempts,
        Func<int> getMultiActionDelayMs,
        Func<int> getMaxRemoteActionRetries,
        Func<Func<RoomExit, bool>> getExitFilter,
        Func<PlayerInfo> getPlayerInfo,
        Action<string> sendCommand,
        Action<string> logMessage)
    {
        _roomTracker = roomTracker;
        _roomGraph = roomGraph;
        _getEnemyCount = getEnemyCount;
        _shouldPauseCommands = shouldPauseCommands;
        _clearRoomEnemies = clearRoomEnemies;
        _clearAlsoHereDedup = clearAlsoHereDedup;
        _usePicklock = usePicklock;
        _getMaxDoorAttempts = getMaxDoorAttempts;
        _getMaxSearchAttempts = getMaxSearchAttempts;
        _getMultiActionDelayMs = getMultiActionDelayMs;
        _getMaxRemoteActionRetries = getMaxRemoteActionRetries;
        _getExitFilter = getExitFilter;
        _getPlayerInfo = getPlayerInfo;
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
    public bool DoorStepActive => _doorStepActive;
    public bool SearchStepActive => _searchStepActive;
    public bool MultiActionStepActive => _multiActionStepActive;
    public bool RemoteActionStepActive => _remoteActionStepActive;

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
        _remoteActionRetryCount = 0;
        _walkMode = mode;
        _lastSentStepIndex = -1;
        _lastStepSendTime = DateTime.MinValue;

        // Resolve destination name
        var destRoom = _roomGraph.GetRoom(_destinationKey);
        _destinationName = destRoom?.Name ?? _destinationKey;

        _logMessage($"🚶 Auto-walk started: {_destinationName} ({_steps.Count} steps)");
        _debugLog?.Write("WALK_START", $"dest=[{_destinationKey}] {_destinationName} steps={_steps.Count}");
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
        ResetDoorState();
        ResetSearchState();
        ResetMultiActionState();
        ResetRemoteActionState();
        var wasActive = IsActive;
        SetState(AutoWalkState.Idle);

        if (wasActive)
        {
            _logMessage($"🛑 Auto-walk stopped at step {_currentStepIndex + 1}/{_steps.Count}");
            _debugLog?.Write("WALK_STOP", $"step={_currentStepIndex + 1}/{_steps.Count}");
            OnLogMessage?.Invoke("🛑 Auto-walk cancelled");
        }

        _steps.Clear();
        _currentStepIndex = 0;
        _lastSentStepIndex = -1;
        _lastStepSendTime = DateTime.MinValue;
    }

    /// <summary>
    /// Notify the walker that combat state has changed.
    /// Called by MainForm when MessageRouter fires OnCombatStateChanged.
    /// </summary>
    public void OnCombatStateChanged(bool inCombat)
    {
        if (inCombat && (_state == AutoWalkState.Walking || _state == AutoWalkState.WaitingForCombat))
        {
            // Combat engaged (or re-engaged) — pause walking and cancel any pending retry
            _combatResumeRetries = 0;
            StopTimers();
            OnLogMessage?.Invoke("⚔️ Auto-walk paused — combat engaged");
            SetState(AutoWalkState.WaitingForCombat);
            StartCombatHeartbeatTimer();
        }
        else if (_state == AutoWalkState.WaitingForCombat && !inCombat)
        {
            // Combat ended — check if safe to resume
            TryResumeAfterCombat();
        }
    }

    /// <summary>
    /// Called when a combat damage tick is detected (damage message arrived).
    /// Resets the heartbeat timer — combat is still active, don't interfere.
    /// </summary>
    public void OnCombatTick()
    {
        if (_state != AutoWalkState.WaitingForCombat)
            return;

        // Restart the heartbeat timer — damage is flowing, combat is active
        StartCombatHeartbeatTimer();
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
        ResetDoorState();
        ResetSearchState();
        ResetMultiActionState();
        SetState(AutoWalkState.Failed);
        OnLogMessage?.Invoke("💀 Auto-walk aborted — player died");
        OnWalkFailed?.Invoke("Player died during walk.");
    }

    /// <summary>
    /// Notify the walker that the connection was lost.
    /// Stops timers and sets Disconnected state, but preserves the step list
    /// and current index so we can resume exactly where we left off.
    /// </summary>
    public void OnDisconnected()
    {
        if (!IsActive)
            return;

        StopTimers();
        ResetDoorState();
        ResetSearchState();
        ResetMultiActionState();

        // Preserve _steps, _currentStepIndex, _destinationKey, _destinationName.
        // On reconnect we'll find our position in the existing step list and resume.
        _logMessage($"📡 Auto-walk suspended — connection lost (step {_currentStepIndex + 1}/{_steps.Count}, destination: {_destinationName})");
        _debugLog?.Write("WALK_DISCONNECTED", $"step={_currentStepIndex + 1}/{_steps.Count} dest=[{_destinationKey}] {_destinationName}");

        SetState(AutoWalkState.Disconnected);
    }

    /// <summary>
    /// Notify the walker that the connection has been restored.
    /// Checks whether the in-flight step completed during disconnect by
    /// comparing the RoomTracker's current room against the step's FromKey
    /// and ToKey. If the tracker can't disambiguate (duplicate room names),
    /// conservatively assumes the step did NOT complete and sets the tracker
    /// to our last confirmed position. Any off-by-one drift is handled by
    /// TryResolveNearbyStep when the first mismatch is detected.
    /// </summary>
    public void OnReconnected()
    {
        if (_state != AutoWalkState.Disconnected)
            return;

        if (_steps.Count == 0)
        {
            _logMessage("📡 Auto-walk cannot resume — no steps preserved");
            SetState(AutoWalkState.Idle);
            return;
        }

        if (_currentStepIndex >= _steps.Count)
        {
            _logMessage($"📡 Walk completed during disconnect — already past final step");
            WalkCompleted();
            return;
        }

        var currentRoom = _roomTracker.CurrentRoom;
        var step = _steps[_currentStepIndex];

        // Already at the destination?
        if (currentRoom != null && currentRoom.Key == _destinationKey)
        {
            _logMessage($"📡 Already at destination {_destinationName} — walk complete");
            WalkCompleted();
            return;
        }

        // Check if the tracker correctly identified our room
        if (currentRoom != null && currentRoom.Key == step.ToKey)
        {
            // In-flight step completed during disconnect
            _debugLog?.Write("RECONNECT_STEP_COMPLETED", $"step {_currentStepIndex + 1}/{_steps.Count} completed during disconnect, now in [{currentRoom.Key}]");
            _currentStepIndex++;

            if (_currentStepIndex >= _steps.Count)
            {
                _logMessage($"📡 Final step completed during disconnect — arrived at {_destinationName}");
                WalkCompleted();
                return;
            }
        }
        else if (currentRoom != null && currentRoom.Key == step.FromKey)
        {
            // Still at the same room — step didn't complete
            _debugLog?.Write("RECONNECT_SAME_POSITION", $"still at step {_currentStepIndex + 1}/{_steps.Count} fromKey=[{currentRoom.Key}]");
        }
        else
        {
            // Tracker couldn't reliably identify our room (duplicate names, null, etc.)
            // Conservative assumption: step didn't complete, we're still at FromKey.
            // Set tracker to our assumed position so the pending move state is correct
            // when SendNextStep fires.
            var assumedRoom = _roomGraph.GetRoom(step.FromKey);
            if (assumedRoom != null)
            {
                _roomTracker.SetCurrentRoom(assumedRoom);
                _debugLog?.Write("RECONNECT_ASSUMED_POSITION", $"tracker room [{currentRoom?.Key}] not in step, assumed [{step.FromKey}]");
            }
            else
            {
                _logMessage("📡 Auto-walk cannot resume — assumed room not found in graph");
                SetState(AutoWalkState.Idle);
                return;
            }
        }

        _logMessage($"📡 Reconnected — resuming walk to {_destinationName} (step {_currentStepIndex + 1}/{_steps.Count})");
        _debugLog?.Write("WALK_RECONNECTED", $"resuming step={_currentStepIndex + 1}/{_steps.Count} dest=[{_destinationKey}] {_destinationName}");
        OnLogMessage?.Invoke($"📡 Resuming walk to {_destinationName} — step {_currentStepIndex + 1}/{_steps.Count}");

        _recalcCount = 0;
        SetState(AutoWalkState.Walking);
        SendNextStep();
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

        // Guard: prevent duplicate sends for the same step (rapid combat cycling
        // can cause TryResumeAfterCombat → SendNextStep to fire twice within ~100ms)
        if (_currentStepIndex == _lastSentStepIndex &&
            (DateTime.Now - _lastStepSendTime).TotalMilliseconds < 500)
        {
            _debugLog?.Write("STEP_SKIP", $"step={_currentStepIndex + 1}/{_steps.Count} duplicate send suppressed");
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
            _logMessage($"⚠️ Expected to be in [{step.FromKey}] but in [{currentRoom.Key}] — checking nearby steps");
            _debugLog?.Write("UNEXPECTED_ROOM", $"expected=[{step.FromKey}] actual=[{currentRoom.Key}] {currentRoom.Name}");

            var prevIndex = _currentStepIndex;
            if (TryResolveNearbyStep(currentRoom) && _currentStepIndex != prevIndex)
            {
                SendNextStep();
                return;
            }

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
            _debugLog?.Write("COMBAT_PAUSE", $"enemies={enemies} step={_currentStepIndex + 1}/{_steps.Count}");
            StopTimers();
            SetState(AutoWalkState.WaitingForCombat);
            StartCombatVerificationTimer();
            return;
        }

        OnWalkProgress?.Invoke(_currentStepIndex + 1, _steps.Count, step.ToName);

        // Remote action steps: send command in-place, wait delay, advance (no room change expected)
        if (step.ExitType == RoomExitType.RemoteAction)
        {
            _remoteActionStepActive = true;
            _logMessage($"🔧 Remote action: sending '{step.Command}' in [{step.FromKey}]");
            _debugLog?.Write("REMOTE_ACTION", $"step={_currentStepIndex + 1}/{_steps.Count} cmd='{step.Command}' room=[{step.FromKey}]");
            _roomTracker.OnPlayerCommand(step.Command);
            _sendCommand(step.Command);
            StartRemoteActionDelayTimer();
            return;
        }

        // Door exits require bash/pick before movement command
        if (step.ExitType == RoomExitType.Door)
        {
            ResetDoorState();
            BeginDoorSequence(step);
            return;
        }

        // Searchable hidden exits require "sea {direction}" before movement command
        if (step.ExitType == RoomExitType.SearchableHidden)
        {
            ResetSearchState();
            BeginSearchSequence(step);
            return;
        }

        // Multi-action hidden exits require action commands before movement
        if (step.ExitType == RoomExitType.MultiActionHidden)
        {
            ResetMultiActionState();
            BeginMultiActionSequence(step);
            return;
        }

        if (step.ExitType == RoomExitType.Teleport)
            _logMessage($"🌀 Teleport: sending '{step.Command}' from [{step.FromKey}] to [{step.ToKey}]");
        _logMessage($"🔬 WALK DEBUG: Step {_currentStepIndex + 1}/{_steps.Count} cmd='{step.Command}' toKey='{step.ToKey}' fromKey='{step.FromKey}' trackerRoom='{_roomTracker.CurrentRoomKey}'");
        _debugLog?.Write("STEP", $"{_currentStepIndex + 1}/{_steps.Count} cmd='{step.Command}' from=[{step.FromKey}] to=[{step.ToKey}] exit={step.ExitType} tracker=[{_roomTracker.CurrentRoomKey}]");
        _roomTracker.OnPlayerCommand(step.Command);
        _sendCommand(step.Command);
        _lastSentStepIndex = _currentStepIndex;
        _lastStepSendTime = DateTime.Now;

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
        // ── Re-sync intercept ──
        // If a timeout re-sync is in progress, route the first room detection
        // to the re-sync handler instead of normal walk logic.
        if (_resyncInProgress && newRoom != null)
        {
            OnResyncRoomDetected(newRoom);
            return;
        }

        // Track position during both Walking and WaitingForCombat states.
        // The player may physically move after combat engages (command was already sent).
        // We must still advance the step index so we don't repeat the step on resume.
        if (_state != AutoWalkState.Walking && _state != AutoWalkState.WaitingForCombat)
            return;

        if (newRoom == null)
            return;

        // During door handling (before movement is sent), bash/pick/open can
        // trigger room redisplays. Ignore these — we haven't moved yet.
        if (_doorStepActive && _doorSubState != DoorSubState.WaitingForMove)
            return;

        // During multi-action handling (before movement is sent), action commands
        // may trigger room redisplays. Ignore these — we haven't moved yet.
        if (_multiActionStepActive && _multiActionSubState != MultiActionSubState.WaitingForMove)
            return;

        // During remote action handling, action commands (pull lever, push button, etc.)
        // may trigger room text redisplays. Ignore these — we haven't moved yet.
        if (_remoteActionStepActive)
            return;

        // Room arrival after door step — clean up door state
        if (_doorStepActive)
            ResetDoorState();

        // Room arrival after search step — clean up search state
        if (_searchStepActive)
            ResetSearchState();

        // Room arrival after multi-action step — clean up multi-action state
        if (_multiActionStepActive)
            ResetMultiActionState();

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
                _debugLog?.Write("ARRIVAL", $"room=[{newRoom.Key}] {newRoom.Name} step={_currentStepIndex}/{_steps.Count}");

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

        _debugLog?.Write("UNEXPECTED_ARRIVAL", $"room=[{newRoom.Key}] {newRoom.Name} expected=[{(_currentStepIndex < _steps.Count ? _steps[_currentStepIndex].ToKey : "?")}]");

        // Unexpected room — try to find it in the step list before recalculating
        if (_state == AutoWalkState.Walking)
        {
            if (TryResolveNearbyStep(newRoom))
            {
                if (_currentStepIndex >= _steps.Count)
                {
                    WalkCompleted();
                    return;
                }
                SendNextStep();
                return;
            }
            RecalculatePath(newRoom);
        }
    }

    /// <summary>
    /// When an unexpected room is detected, try to find it in the existing step
    /// list before falling back to a full path recalculation. This handles
    /// off-by-one drift (e.g., after disconnect/reconnect where we don't know if
    /// the in-flight step completed) and minor position discrepancies.
    /// 
    /// Checks in order:
    ///   Pass 1 — Exact room key match in nearby steps (±3 from current index)
    ///   Pass 2 — Exact room key match anywhere in the full step list
    ///   Pass 3 — Name-based proximity search: if the tracker matched the wrong
    ///            room with a duplicate name (e.g., "Sovereign Street" → 5/139
    ///            instead of 1/228), find rooms with the same name that DO appear
    ///            in the step list and pick the closest one by step distance.
    /// 
    /// Returns true if position was resolved, false if recalculation is needed.
    /// </summary>
    private bool TryResolveNearbyStep(RoomNode actualRoom)
    {
        var actualKey = actualRoom.Key;

        // ── Pass 1: Exact key in nearby steps (most common — off by 1-2) ──
        int searchRadius = 3;
        int minIdx = Math.Max(0, _currentStepIndex - searchRadius);
        int maxIdx = Math.Min(_steps.Count - 1, _currentStepIndex + searchRadius);

        // Check ToKeys first (we completed a step and are at the destination)
        for (int i = minIdx; i <= maxIdx; i++)
        {
            if (_steps[i].ToKey == actualKey)
            {
                var oldIndex = _currentStepIndex;
                _currentStepIndex = i + 1;
                _logMessage($"🔧 Position resolved: room [{actualKey}] found at step {i + 1} ToKey (adjusted from step {oldIndex + 1} to {_currentStepIndex + 1})");
                _debugLog?.Write("NEARBY_STEP_RESOLVED", $"pass=1 key=[{actualKey}] at step {i + 1} toKey, adjusted {oldIndex + 1}→{_currentStepIndex + 1}");
                return true;
            }
        }

        // Check FromKeys (we're at the start of a step, ready to execute it)
        for (int i = minIdx; i <= maxIdx; i++)
        {
            if (_steps[i].FromKey == actualKey)
            {
                var oldIndex = _currentStepIndex;
                _currentStepIndex = i;
                _logMessage($"🔧 Position resolved: room [{actualKey}] found at step {i + 1} FromKey (adjusted from step {oldIndex + 1} to {_currentStepIndex + 1})");
                _debugLog?.Write("NEARBY_STEP_RESOLVED", $"pass=1 key=[{actualKey}] at step {i + 1} fromKey, adjusted {oldIndex + 1}→{_currentStepIndex + 1}");
                return true;
            }
        }

        // ── Pass 2: Exact key anywhere in the full step list (larger drift) ──
        for (int i = 0; i < _steps.Count; i++)
        {
            if (i >= minIdx && i <= maxIdx) continue; // already checked in Pass 1

            if (_steps[i].ToKey == actualKey)
            {
                var oldIndex = _currentStepIndex;
                _currentStepIndex = i + 1;
                _logMessage($"🔧 Position resolved: room [{actualKey}] found at step {i + 1} ToKey (adjusted from step {oldIndex + 1} to {_currentStepIndex + 1})");
                _debugLog?.Write("STEP_LIST_RESOLVED", $"pass=2 key=[{actualKey}] at step {i + 1} toKey, adjusted {oldIndex + 1}→{_currentStepIndex + 1}");
                return true;
            }
            if (_steps[i].FromKey == actualKey)
            {
                var oldIndex = _currentStepIndex;
                _currentStepIndex = i;
                _logMessage($"🔧 Position resolved: room [{actualKey}] found at step {i + 1} FromKey (adjusted from step {oldIndex + 1} to {_currentStepIndex + 1})");
                _debugLog?.Write("STEP_LIST_RESOLVED", $"pass=2 key=[{actualKey}] at step {i + 1} fromKey, adjusted {oldIndex + 1}→{_currentStepIndex + 1}");
                return true;
            }
        }

        // ── Pass 3: Name-based proximity search ──
        // The tracker may have matched the wrong room with a duplicate name
        // (e.g., "Sovereign Street" exists in multiple map areas).
        // Find all rooms with the same name and check if any appear in the step list.
        // Pick the one closest to our current step index.
        var sameNameRooms = _roomGraph.GetRoomsByName(actualRoom.Name);
        if (sameNameRooms != null && sameNameRooms.Count > 1)
        {
            int bestStepIndex = -1;
            int bestDistance = int.MaxValue;
            bool bestIsToKey = false;

            foreach (var candidate in sameNameRooms)
            {
                if (candidate.Key == actualKey) continue; // already checked in Pass 1/2

                for (int i = 0; i < _steps.Count; i++)
                {
                    int distance = Math.Abs(i - _currentStepIndex);

                    if (_steps[i].ToKey == candidate.Key && distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestStepIndex = i;
                        bestIsToKey = true;
                    }
                    if (_steps[i].FromKey == candidate.Key && distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestStepIndex = i;
                        bestIsToKey = false;
                    }
                }
            }

            if (bestStepIndex >= 0)
            {
                var oldIndex = _currentStepIndex;
                _currentStepIndex = bestIsToKey ? bestStepIndex + 1 : bestStepIndex;
                _logMessage($"🔧 Position resolved by name proximity: \"{actualRoom.Name}\" — adjusted from step {oldIndex + 1} to {_currentStepIndex + 1} (distance: {bestDistance})");
                _debugLog?.Write("NAME_PROXIMITY_RESOLVED", $"pass=3 actual=[{actualKey}] name='{actualRoom.Name}' bestStep={bestStepIndex + 1} distance={bestDistance} adjusted {oldIndex + 1}→{_currentStepIndex + 1}");
                return true;
            }
        }

        _debugLog?.Write("NEARBY_STEP_FAILED", $"room=[{actualKey}] '{actualRoom.Name}' not found in step list");
        return false;
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

        var newPath = _roomGraph.FindPath(currentRoom.Key, _destinationKey, _getExitFilter());

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

        // Expand any remote-action exits in the new path
        var expander = new RemoteActionPathExpander(_roomGraph, _getExitFilter);
        var expanded = expander.Expand(newPath);
        if (expanded.Success)
            newPath = expanded;

        // Replace remaining steps with new path
        _steps = new List<PathStep>(newPath.Steps);
        _currentStepIndex = 0;
        ResetDoorState();
        ResetSearchState();
        ResetMultiActionState();
        ResetRemoteActionState();

        OnLogMessage?.Invoke($"🔄 Path recalculated: {_steps.Count} steps remaining");
        _debugLog?.Write("RECALC", $"attempt={_recalcCount}/{MaxRecalculations} from=[{currentRoom.Key}] newSteps={_steps.Count}");
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
        _debugLog?.Write("WALK_COMPLETE", $"dest=[{_destinationKey}] {_destinationName}");
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

        // Always defer the first check after *Combat Off*.
        // CombatManager.OnCombatEnded() fires AFTER OnCombatStateChanged(false)
        // in the MessageRouter event chain, so on this first call the enemy list
        // is stale — OnCombatEngaged() already removed the current target, making
        // the list appear empty even when the enemy survived (e.g., self-heal
        // triggers *Combat Off* without killing the enemy). The deferral gives
        // OnCombatEnded()'s room refresh time to repopulate the enemy list.
        if (_combatResumeRetries == 0)
        {
            _combatResumeRetries = 1;
            _debugLog?.Write("COMBAT_RESUME_DEFER", "deferring resume check — waiting for room refresh");
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
        StopCombatHeartbeatTimer();
        _logMessage("▶️ Auto-walk resuming — combat cleared");
        _debugLog?.Write("COMBAT_RESUME", $"step={_currentStepIndex + 1}/{_steps.Count}");
        ResetDoorState();  // Combat may have interrupted a door sequence — start fresh
        ResetSearchState();  // Combat may have interrupted a search sequence — start fresh
        ResetMultiActionState();  // Combat may have interrupted a multi-action sequence — start fresh
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
            // Retry once — for door steps, restart the entire bash/pick sequence
            _hasRetriedCurrentStep = true;
            var step = _steps[_currentStepIndex];

            if (_doorStepActive)
            {
                _logMessage($"⏱️ Door step timeout — retrying door sequence");
                _doorRetryCount = 0;
                _doorClosedRetryCount = 0;
                BeginDoorSequence(step);
                return;
            }

            if (_multiActionStepActive)
            {
                _logMessage($"⏱️ Multi-action step timeout — retrying action sequence");
                _debugLog?.Write("MULTIACTION_TIMEOUT_RETRY", $"step={_currentStepIndex + 1}/{_steps.Count}");
                ResetMultiActionState();
                BeginMultiActionSequence(step);
                return;
            }

            // Normal step retry — re-register the pending move with RoomTracker
            // so room detection has movement context. Without this, Guard 1
            // treats the server response as a redisplay and skips it.
            _logMessage($"⏱️ Step timeout — retrying: {step.Command}");
            _roomTracker.OnPlayerCommand(step.Command);
            _sendCommand(step.Command);
            StartStepTimer();
            return;
        }

        // Second timeout — attempt re-sync before failing.
        // The room tracker may be desynced (character moved but detection failed).
        // Clear pending moves and send an empty command to trigger a fresh room
        // display, then compare against FromKey/ToKey to determine actual position.
        ResetDoorState();
        ResetSearchState();
        ResetMultiActionState();
        StopTimers();

        if (_currentStepIndex < _steps.Count)
        {
            var step = _steps[_currentStepIndex];
            _logMessage($"⏱️ Step timed out after retry — attempting room re-sync");
            _debugLog?.Write("RESYNC_START", $"step={_currentStepIndex + 1}/{_steps.Count} cmd='{step.Command}' fromKey='{step.FromKey}' toKey='{step.ToKey}'");

            _resyncInProgress = true;
            _roomTracker.ClearPendingMoves();
            _sendCommand("");  // empty command triggers room redisplay

            // Start a safety timer in case the room display never arrives
            _resyncTimer?.Stop();
            _resyncTimer?.Dispose();
            _resyncTimer = new System.Timers.Timer(ResyncTimeoutMs);
            _resyncTimer.AutoReset = false;
            _resyncTimer.Elapsed += (s, e) => OnResyncTimeout();
            _resyncTimer.Start();
            return;
        }

        // No step to re-sync against — fail immediately
        SetState(AutoWalkState.Failed);
        _debugLog?.Write("TIMEOUT_FAIL", $"step={_currentStepIndex + 1}/{_steps.Count} cmd='{(_currentStepIndex < _steps.Count ? _steps[_currentStepIndex].Command : "?")}'");
        _logMessage("❌ Auto-walk failed — step timed out after retry");
        OnWalkFailed?.Invoke("Movement timed out. The path may be blocked.");
    }

    #endregion

    #region Timeout Re-sync

    /// <summary>
    /// Called when the re-sync empty command fails to produce a room detection
    /// within the timeout window. Falls back to the normal failure path.
    /// </summary>
    private void OnResyncTimeout()
    {
        if (!_resyncInProgress)
            return;

        _resyncInProgress = false;
        StopResyncTimer();

        _logMessage("❌ Room re-sync timed out — no room detected");
        _debugLog?.Write("RESYNC_TIMEOUT", $"step={_currentStepIndex + 1}/{_steps.Count}");

        // Remote-action exit retry on re-sync timeout: if we can't detect the room
        // but the step has OriginalMultiActionData, attempt a retry from current position
        if (_currentStepIndex < _steps.Count)
        {
            var step = _steps[_currentStepIndex];
            var maxRetries = _getMaxRemoteActionRetries();
            if (step.OriginalMultiActionData != null && _remoteActionRetryCount < maxRetries)
            {
                _remoteActionRetryCount++;
                _logMessage($"🔄 Remote action exit — re-sync failed, retrying prerequisites (attempt {_remoteActionRetryCount}/{maxRetries})");
                _debugLog?.Write("REMOTE_ACTION_RETRY_RESYNC_TIMEOUT", $"attempt={_remoteActionRetryCount}/{maxRetries}");

                var expander = new RemoteActionPathExpander(_roomGraph, _getExitFilter);
                var prerequisiteSteps = expander.ExpandSingle(step.OriginalMultiActionData, step.FromKey);

                if (prerequisiteSteps != null && prerequisiteSteps.Count > 0)
                {
                    _steps.InsertRange(_currentStepIndex, prerequisiteSteps);
                    _hasRetriedCurrentStep = false;

                    SetState(AutoWalkState.Walking);
                    SendNextStep();
                    return;
                }
            }
        }

        SetState(AutoWalkState.Failed);
        _logMessage("❌ Auto-walk failed — step timed out after retry");
        OnWalkFailed?.Invoke("Movement timed out. The path may be blocked.");
    }

    /// <summary>
    /// Called from OnRoomChanged when a re-sync is in progress.
    /// Compares the detected room against the step's FromKey and ToKey
    /// to determine actual position — mirrors the disconnect recovery logic.
    /// </summary>
    private void OnResyncRoomDetected(RoomNode detectedRoom)
    {
        _resyncInProgress = false;
        StopResyncTimer();

        var step = _steps[_currentStepIndex];

        if (detectedRoom.Key == step.ToKey)
        {
            // The in-flight step actually completed — detection had failed earlier.
            // Advance past it and continue walking.
            _logMessage($"🔄 Re-sync: step completed (now in [{detectedRoom.Key}] {detectedRoom.Name}) — advancing");
            _debugLog?.Write("RESYNC_STEP_COMPLETED", $"room=[{detectedRoom.Key}] {detectedRoom.Name} step={_currentStepIndex + 1}/{_steps.Count}");
            _currentStepIndex++;
            _hasRetriedCurrentStep = false;

            if (_currentStepIndex >= _steps.Count)
            {
                WalkCompleted();
                return;
            }

            SetState(AutoWalkState.Walking);
            SendNextStep();
        }
        else if (detectedRoom.Key == step.FromKey)
        {
            // Still at the same room — step didn't complete.
            _logMessage($"🔄 Re-sync: still at [{detectedRoom.Key}] {detectedRoom.Name} — step did not complete");
            _debugLog?.Write("RESYNC_SAME_POSITION", $"room=[{detectedRoom.Key}] {detectedRoom.Name} step={_currentStepIndex + 1}/{_steps.Count}");

            // Remote-action exit retry: if this step has OriginalMultiActionData, the exit
            // likely didn't open because levers/buttons reset. Re-expand prerequisites and retry.
            var maxRetries = _getMaxRemoteActionRetries();
            if (step.OriginalMultiActionData != null && _remoteActionRetryCount < maxRetries)
            {
                _remoteActionRetryCount++;
                _logMessage($"🔄 Remote action exit timed out — re-executing prerequisites (attempt {_remoteActionRetryCount}/{maxRetries})");
                _debugLog?.Write("REMOTE_ACTION_RETRY", $"attempt={_remoteActionRetryCount}/{maxRetries} step={_currentStepIndex + 1}/{_steps.Count}");

                var expander = new RemoteActionPathExpander(_roomGraph, _getExitFilter);
                var prerequisiteSteps = expander.ExpandSingle(step.OriginalMultiActionData, step.FromKey);

                if (prerequisiteSteps != null && prerequisiteSteps.Count > 0)
                {
                    // Insert prerequisite steps before the current exit step
                    _steps.InsertRange(_currentStepIndex, prerequisiteSteps);
                    _hasRetriedCurrentStep = false;

                    _logMessage($"🔧 Inserted {prerequisiteSteps.Count} prerequisite steps for retry");
                    _debugLog?.Write("REMOTE_ACTION_RETRY_EXPANDED", $"inserted={prerequisiteSteps.Count} totalSteps={_steps.Count}");

                    SetState(AutoWalkState.Walking);
                    SendNextStep();
                    return;
                }
                else
                {
                    _logMessage("❌ Failed to expand remote action prerequisites for retry");
                    _debugLog?.Write("REMOTE_ACTION_RETRY_FAIL", "expansion returned null");
                }
            }
            else if (step.OriginalMultiActionData != null && _remoteActionRetryCount >= maxRetries)
            {
                _logMessage($"❌ Remote action exit failed after {maxRetries} retry attempt(s)");
                _debugLog?.Write("REMOTE_ACTION_RETRIES_EXHAUSTED", $"attempts={_remoteActionRetryCount}/{maxRetries}");
            }

            SetState(AutoWalkState.Failed);
            OnWalkFailed?.Invoke("Movement timed out. The path may be blocked.");
        }
        else
        {
            // Detected a room that is neither FromKey nor ToKey.
            // Conservative assumption: set tracker to FromKey (same as disconnect logic).
            _logMessage($"🔄 Re-sync: unexpected room [{detectedRoom.Key}] {detectedRoom.Name} — assuming still at [{step.FromKey}]");
            _debugLog?.Write("RESYNC_ASSUMED_POSITION", $"detected=[{detectedRoom.Key}] assumed=[{step.FromKey}]");

            var assumedRoom = _roomGraph.GetRoom(step.FromKey);
            if (assumedRoom != null)
                _roomTracker.SetCurrentRoom(assumedRoom);

            SetState(AutoWalkState.Failed);
            OnWalkFailed?.Invoke("Movement timed out. The path may be blocked.");
        }
    }

    private void StopResyncTimer()
    {
        _resyncTimer?.Stop();
        _resyncTimer?.Dispose();
        _resyncTimer = null;
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

    private void StartCombatVerificationTimer()
    {
        StopCombatVerificationTimer();
        _combatVerificationTimer = new System.Timers.Timer(CombatVerificationDelayMs);
        _combatVerificationTimer.AutoReset = false;
        _combatVerificationTimer.Elapsed += (s, e) => OnCombatVerificationTimeout();
        _combatVerificationTimer.Start();
    }

    private void StopCombatVerificationTimer()
    {
        _combatVerificationTimer?.Stop();
        _combatVerificationTimer?.Dispose();
        _combatVerificationTimer = null;
    }

    private void StartCombatHeartbeatTimer()
    {
        StopCombatHeartbeatTimer();
        _combatHeartbeatTimer = new System.Timers.Timer(CombatHeartbeatTimeoutMs);
        _combatHeartbeatTimer.AutoReset = false;
        _combatHeartbeatTimer.Elapsed += (s, e) => OnCombatHeartbeatTimeout();
        _combatHeartbeatTimer.Start();
    }

    private void StopCombatHeartbeatTimer()
    {
        _combatHeartbeatTimer?.Stop();
        _combatHeartbeatTimer?.Dispose();
        _combatHeartbeatTimer = null;
    }

    /// <summary>
    /// No damage detected for 10 seconds while in WaitingForCombat.
    /// Combat may have ended with a missed *Combat Off*. Clear the dedup cache
    /// and send a room refresh to find out what's actually here.
    /// </summary>
    private void OnCombatHeartbeatTimeout()
    {
        if (_state != AutoWalkState.WaitingForCombat)
            return;

        _logMessage("💓 Combat heartbeat timeout — no damage for 10s, checking room...");
        _debugLog?.Write("HEARTBEAT_TIMEOUT", $"step={_currentStepIndex + 1}/{_steps.Count} — refreshing room");

        // Clear dedup so CombatManager reprocesses the next "Also here:" line
        _clearAlsoHereDedup();

        // Send room refresh — non-aggressive, just asking "what's here?"
        _sendCommand("");

        // Wait for server response, then check enemy count
        _combatHeartbeatTimer = new System.Timers.Timer(2000);
        _combatHeartbeatTimer.AutoReset = false;
        _combatHeartbeatTimer.Elapsed += (s, e) => OnCombatHeartbeatRecheck();
        _combatHeartbeatTimer.Start();
    }

    /// <summary>
    /// Re-check after room refresh. If no enemies, *Combat Off* was missed — resume.
    /// If enemies are present, combat is still active (dodge round, etc.) — restart heartbeat.
    /// </summary>
    private void OnCombatHeartbeatRecheck()
    {
        StopCombatHeartbeatTimer();

        if (_state != AutoWalkState.WaitingForCombat)
            return;

        var enemyCount = _getEnemyCount();
        if (enemyCount > 0)
        {
            // Enemies still present — combat ongoing, restart heartbeat
            _logMessage($"💓 Heartbeat recheck: {enemyCount} enemies present — restarting heartbeat");
            _debugLog?.Write("HEARTBEAT_RECHECK_ENEMIES", $"enemies={enemyCount} — combat still active");
            StartCombatHeartbeatTimer();
        }
        else
        {
            // Room is clear — *Combat Off* was missed, resume walking
            _logMessage("💓 Heartbeat recheck: room clear — resuming walk (missed *Combat Off*)");
            _debugLog?.Write("HEARTBEAT_RECHECK_CLEAR", "no enemies — resuming walk");
            SetState(AutoWalkState.Walking);
            SendNextStep();
        }
    }

    /// <summary>
    /// Combat verification timeout fired — we've been in WaitingForCombat for 5 seconds
    /// without *Combat Engaged* ever arriving. This means the enemy data was likely stale.
    /// Clear it and refresh the room to find out what's really here.
    /// </summary>
    private void OnCombatVerificationTimeout()
    {
        if (_state != AutoWalkState.WaitingForCombat)
            return;

        _logMessage("⚠️ Combat verification: no combat engaged after 5s — refreshing room");
        _debugLog?.Write("COMBAT_VERIFY", $"step={_currentStepIndex + 1}/{_steps.Count} — clearing stale enemies and refreshing room");

        // Clear stale enemy data (also clears _attackPending, _lastProcessedAlsoHere, etc.)
        _clearRoomEnemies();

        // Send blank command to force room refresh from the server.
        // If enemies ARE here, "Also here:" will fire, CombatManager will re-detect
        // them, and TryInitiateCombat() will send a fresh attack command.
        _sendCommand("");

        // Wait for the server response to arrive, then re-check the situation
        _combatVerificationTimer = new System.Timers.Timer(CombatVerificationRecheckMs);
        _combatVerificationTimer.AutoReset = false;
        _combatVerificationTimer.Elapsed += (s, e) => OnCombatVerificationRecheck();
        _combatVerificationTimer.Start();
    }

    /// <summary>
    /// Re-check after the room refresh. If the room is empty, the enemy list will
    /// still be clear (no "Also here:" came back). Resume walking.
    /// If enemies are genuinely present, CombatManager has already re-detected them
    /// and sent an attack command — let combat proceed normally.
    /// </summary>
    private void OnCombatVerificationRecheck()
    {
        StopCombatVerificationTimer();

        if (_state != AutoWalkState.WaitingForCombat)
            return;

        var enemyCount = _getEnemyCount();
        if (enemyCount > 0)
        {
            // Enemies genuinely present — CombatManager should have sent an attack
            // from the room refresh. Wait for combat to proceed normally.
            _logMessage($"⚔️ Combat verification: {enemyCount} enemies confirmed after refresh — waiting for combat");
            _debugLog?.Write("COMBAT_VERIFY_ENEMIES", $"enemies={enemyCount} — combat system should engage");
        }
        else
        {
            // Room is empty — the enemy data was stale. Resume walking.
            _logMessage("✅ Combat verification: room is clear — resuming walk");
            _debugLog?.Write("COMBAT_VERIFY_CLEAR", "no enemies after refresh — resuming");
            SetState(AutoWalkState.Walking);
            SendNextStep();
        }
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

    #region Door Handling

    /// <summary>
    /// Process server text for door and search related responses.
    /// Called by MessageRouter on every server message. Returns immediately
    /// when no door or search step is active (zero cost for normal walks).
    /// </summary>
    public void ProcessServerText(string text)
    {
        if (_doorStepActive)
        {
            ProcessDoorText(text);
            return;
        }

        if (_searchStepActive)
        {
            ProcessSearchText(text);
            return;
        }
    }

    private void ProcessDoorText(string text)
    {
        switch (_doorSubState)
        {
            case DoorSubState.WaitingForBash:
                if (text.Contains("bashed the door open", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("bashed the gate open", StringComparison.OrdinalIgnoreCase))
                {
                    _logMessage("🚪 Bash succeeded");
                    SendMovementAfterDoor();
                }
                else if (text.Contains("attempts to bash through fail", StringComparison.OrdinalIgnoreCase))
                {
                    _doorRetryCount++;
                    var maxAttempts = _getMaxDoorAttempts();
                    if (maxAttempts > 0 && _doorRetryCount >= maxAttempts)
                    {
                        FailDoorStep("Could not bash door open after multiple attempts");
                        return;
                    }
                    _logMessage($"🚪 Bash failed — retrying ({_doorRetryCount}/{maxAttempts})");
                    StartStepTimer();
                    _sendCommand($"bash {_doorDirection}");
                }
                else if (text.Contains("already open", StringComparison.OrdinalIgnoreCase))
                {
                    _logMessage("🚪 Door already open");
                    SendMovementAfterDoor();
                }
                break;

            case DoorSubState.WaitingForPick:
                if (text.Contains("successfully unlock the door", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("successfully unlock the gate", StringComparison.OrdinalIgnoreCase))
                {
                    _doorSubState = DoorSubState.WaitingForOpen;
                    _logMessage($"🔑 Lock picked — opening: open {_doorDirection}");
                    _sendCommand($"open {_doorDirection}");
                }
                else if (text.Contains("skill fails you", StringComparison.OrdinalIgnoreCase))
                {
                    _doorRetryCount++;
                    var maxAttempts = _getMaxDoorAttempts();
                    if (maxAttempts > 0 && _doorRetryCount >= maxAttempts)
                    {
                        FailDoorStep("Could not pick lock after multiple attempts");
                        return;
                    }
                    _logMessage($"🔑 Pick failed — retrying ({_doorRetryCount}/{maxAttempts})");
                    StartStepTimer();
                    _sendCommand($"pick {_doorDirection}");
                }
                else if (text.Contains("was not locked", StringComparison.OrdinalIgnoreCase))
                {
                    _doorSubState = DoorSubState.WaitingForOpen;
                    _logMessage($"🔑 Door not locked — opening: open {_doorDirection}");
                    _sendCommand($"open {_doorDirection}");
                }
                else if (text.Contains("already open", StringComparison.OrdinalIgnoreCase))
                {
                    _logMessage("🚪 Door already open");
                    SendMovementAfterDoor();
                }
                break;

            case DoorSubState.WaitingForOpen:
                if (text.Contains("is now open", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("already open", StringComparison.OrdinalIgnoreCase))
                {
                    _logMessage("🚪 Door opened");
                    SendMovementAfterDoor();
                }
                break;

            case DoorSubState.WaitingForMove:
                // Door closed between opening and moving through
                if (text.Contains("The door is closed", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("The gate is closed", StringComparison.OrdinalIgnoreCase))
                {
                    _doorClosedRetryCount++;
                    if (_doorClosedRetryCount >= MaxDoorClosedRetries)
                    {
                        FailDoorStep("Door keeps closing before passage");
                        return;
                    }
                    _logMessage($"🚪 Door closed! Re-opening ({_doorClosedRetryCount}/{MaxDoorClosedRetries})");

                    if (_usePicklock())
                    {
                        _doorSubState = DoorSubState.WaitingForPick;
                        _sendCommand($"pick {_doorDirection}");
                    }
                    else
                    {
                        _doorSubState = DoorSubState.WaitingForBash;
                        _sendCommand($"bash {_doorDirection}");
                    }
                    StartStepTimer();
                }
                break;
        }
    }

    /// <summary>
    /// Start the bash/pick sequence for a door step.
    /// </summary>
    private void BeginDoorSequence(PathStep step)
    {
        _doorStepActive = true;
        _doorDirection = DirectionFullNames.GetValueOrDefault(step.Command, step.Command);

        _logMessage($"🔬 WALK DEBUG: Step {_currentStepIndex + 1}/{_steps.Count} cmd='{step.Command}' toKey='{step.ToKey}' fromKey='{step.FromKey}' exitType=Door direction='{_doorDirection}'");

        // Check if we should use an action bypass (lever/switch) instead of bash/pick.
        // Use bypass only when the player lacks the stats to open the door normally.
        if (step.DoorActionBypass != null && step.DoorStatRequirement > 0)
        {
            var info = _getPlayerInfo();
            bool canBashOrPick = info.Strength >= step.DoorStatRequirement
                              || info.Picklocks >= step.DoorStatRequirement;

            if (!canBashOrPick)
            {
                var bypassCmd = step.DoorActionBypass[0];
                _logMessage($"🔧 Using action bypass: {bypassCmd} (door requires {step.DoorStatRequirement} str/pick)");
                _sendCommand(bypassCmd);
                // Timer-based: wait for multi-action delay then send movement command.
                // Game response to lever/switch varies, so use a delay like multi-action exits.
                _doorSubState = DoorSubState.WaitingForOpen;
                StartDoorBypassDelayTimer();
                return;
            }
        }

        if (_usePicklock())
        {
            _doorSubState = DoorSubState.WaitingForPick;
            _logMessage($"🔑 Picking lock: pick {_doorDirection}");
            _sendCommand($"pick {_doorDirection}");
        }
        else
        {
            _doorSubState = DoorSubState.WaitingForBash;
            _logMessage($"🚪 Bashing door: bash {_doorDirection}");
            _sendCommand($"bash {_doorDirection}");
        }

        StartStepTimer();
    }

    /// <summary>
    /// Door is open — send the movement command to walk through.
    /// </summary>
    private void SendMovementAfterDoor()
    {
        if (_currentStepIndex >= _steps.Count)
            return;

        var step = _steps[_currentStepIndex];
        _doorSubState = DoorSubState.WaitingForMove;
        _roomTracker.ClearLineBuffer();
        _logMessage($"🚪 Moving through door: {step.Command}");
        _roomTracker.OnPlayerCommand(step.Command);
        _sendCommand(step.Command);
        _clearRoomEnemies();
        StartStepTimer();
    }

    /// <summary>
    /// Door step failed — abort the walk.
    /// </summary>
    private void FailDoorStep(string reason)
    {
        ResetDoorState();
        StopTimers();
        SetState(AutoWalkState.Failed);
        _logMessage($"❌ Auto-walk failed — {reason}");
        _debugLog?.Write("DOOR_FAIL", $"reason='{reason}' retries={_doorRetryCount}");
        OnWalkFailed?.Invoke($"{reason}. Try a different route or open the door manually.");
    }

    /// <summary>
    /// Reset all door handling state.
    /// </summary>
    private void ResetDoorState()
    {
        _doorStepActive = false;
        _doorSubState = DoorSubState.None;
        _doorRetryCount = 0;
        _doorClosedRetryCount = 0;
        _doorDirection = "";
        StopDoorBypassDelayTimer();
    }

    /// <summary>
    /// Start a delay timer after sending a door bypass command (lever/switch).
    /// On elapsed, sends the movement command to walk through the opened door.
    /// </summary>
    private void StartDoorBypassDelayTimer()
    {
        StopDoorBypassDelayTimer();
        _doorBypassDelayTimer = new System.Timers.Timer(_getMultiActionDelayMs());
        _doorBypassDelayTimer.AutoReset = false;
        _doorBypassDelayTimer.Elapsed += (s, e) =>
        {
            if (_state != AutoWalkState.Walking || !_doorStepActive)
                return;
            _logMessage("🔧 Bypass delay elapsed — sending movement command");
            SendMovementAfterDoor();
        };
        _doorBypassDelayTimer.Start();
    }

    private void StopDoorBypassDelayTimer()
    {
        _doorBypassDelayTimer?.Stop();
        _doorBypassDelayTimer?.Dispose();
        _doorBypassDelayTimer = null;
    }

    #endregion

    #region Search Handling

    /// <summary>
    /// Process server text for search-related responses.
    /// </summary>
    private void ProcessSearchText(string text)
    {
        switch (_searchSubState)
        {
            case SearchSubState.WaitingForSearch:
                if (text.Contains("You found an exit", StringComparison.OrdinalIgnoreCase))
                {
                    _logMessage($"🔍 Search succeeded — exit found to the {_searchDirection}");
                    _debugLog?.Write("SEARCH_FOUND", $"direction='{_searchDirection}' attempts={_searchRetryCount + 1}");
                    SendMovementAfterSearch();
                }
                else if (text.Contains("You notice nothing different", StringComparison.OrdinalIgnoreCase))
                {
                    _searchRetryCount++;
                    var maxAttempts = _getMaxSearchAttempts();
                    if (maxAttempts > 0 && _searchRetryCount >= maxAttempts)
                    {
                        FailSearchStep("Could not find hidden exit after multiple search attempts");
                        return;
                    }
                    _logMessage($"🔍 Search failed — retrying ({_searchRetryCount}/{maxAttempts})");
                    _debugLog?.Write("SEARCH_RETRY", $"direction='{_searchDirection}' attempt={_searchRetryCount}/{maxAttempts}");
                    StartStepTimer();
                    _sendCommand($"sea {_steps[_currentStepIndex].Command}");
                }
                break;

            case SearchSubState.WaitingForMove:
                // No special handling needed — OnRoomChanged handles arrival
                break;
        }
    }

    /// <summary>
    /// Start the search sequence for a searchable hidden exit.
    /// </summary>
    private void BeginSearchSequence(PathStep step)
    {
        _searchStepActive = true;
        _searchDirection = DirectionFullNames.GetValueOrDefault(step.Command, step.Command);

        _logMessage($"🔍 Searching for hidden exit: sea {step.Command}");
        _debugLog?.Write("SEARCH_START", $"step={_currentStepIndex + 1}/{_steps.Count} cmd='{step.Command}' direction='{_searchDirection}'");

        _searchSubState = SearchSubState.WaitingForSearch;
        _sendCommand($"sea {step.Command}");

        StartStepTimer();
    }

    /// <summary>
    /// Hidden exit found — send the movement command to walk through.
    /// </summary>
    private void SendMovementAfterSearch()
    {
        if (_currentStepIndex >= _steps.Count)
            return;

        var step = _steps[_currentStepIndex];
        _searchSubState = SearchSubState.WaitingForMove;
        _roomTracker.ClearLineBuffer();
        _logMessage($"🔍 Moving through revealed exit: {step.Command}");
        _roomTracker.OnPlayerCommand(step.Command);
        _sendCommand(step.Command);
        _clearRoomEnemies();
        StartStepTimer();
    }

    /// <summary>
    /// Search step failed — abort the walk.
    /// </summary>
    private void FailSearchStep(string reason)
    {
        var retries = _searchRetryCount;
        ResetSearchState();
        StopTimers();
        SetState(AutoWalkState.Failed);
        _logMessage($"❌ Auto-walk failed — {reason}");
        _debugLog?.Write("SEARCH_FAIL", $"reason='{reason}' retries={retries}");
        OnWalkFailed?.Invoke($"{reason}. Try a different route or search manually.");
    }

    /// <summary>
    /// Reset all search handling state.
    /// </summary>
    private void ResetSearchState()
    {
        _searchStepActive = false;
        _searchSubState = SearchSubState.None;
        _searchRetryCount = 0;
        _searchDirection = "";
    }

    #endregion

    #region Multi-Action Hidden Exit Handling

    /// <summary>
    /// Start the multi-action sequence for a hidden exit that requires action commands.
    /// Sends action commands one at a time with configurable delays between them,
    /// then sends the movement command to walk through the revealed exit.
    /// </summary>
    private void BeginMultiActionSequence(PathStep step)
    {
        if (step.MultiActionData == null || !step.MultiActionData.IsAutomatable)
        {
            _logMessage($"❌ Multi-action exit has no automatable action data");
            _debugLog?.Write("MULTIACTION_NO_DATA", $"step={_currentStepIndex + 1}/{_steps.Count} toKey='{step.ToKey}'");
            FailMultiActionStep("Multi-action exit data is missing or not automatable");
            return;
        }

        _multiActionStepActive = true;
        _multiActionCurrentIndex = 0;
        _multiActionSteps = step.MultiActionData.Actions;

        var actionCount = _multiActionSteps.Count;
        var orderType = step.MultiActionData.RequiresSpecificOrder ? "specific order" : "any order";
        _logMessage($"🔮 Multi-action hidden exit: {actionCount} action(s), {orderType}");
        _debugLog?.Write("MULTIACTION_START", $"step={_currentStepIndex + 1}/{_steps.Count} cmd='{step.Command}' actions={actionCount} order='{orderType}'");

        SendNextActionCommand();
    }

    /// <summary>
    /// Send the next action command in the sequence.
    /// Uses the first alternative command from the current action step.
    /// After sending, either starts a delay timer (more actions to send)
    /// or proceeds to send the movement command (all actions sent).
    /// </summary>
    private void SendNextActionCommand()
    {
        if (_multiActionSteps == null || _multiActionCurrentIndex >= _multiActionSteps.Count)
        {
            // All action commands sent — proceed with movement
            SendMovementAfterMultiAction();
            return;
        }

        var action = _multiActionSteps[_multiActionCurrentIndex];
        var command = action.Commands[0]; // Use first alternative command

        _logMessage($"🔮 Action {_multiActionCurrentIndex + 1}/{_multiActionSteps.Count}: {command}");
        _debugLog?.Write("MULTIACTION_CMD", $"index={_multiActionCurrentIndex + 1}/{_multiActionSteps.Count} cmd='{command}'");

        _multiActionSubState = MultiActionSubState.WaitingForActionDelay;
        _sendCommand(command);
        _multiActionCurrentIndex++;

        if (_multiActionCurrentIndex < _multiActionSteps.Count)
        {
            // More actions to send — start delay timer before next command
            StartMultiActionDelayTimer();
        }
        else
        {
            // Last action sent — wait for delay then send movement
            StartMultiActionDelayTimer();
        }
    }

    /// <summary>
    /// All action commands have been sent and the final delay has elapsed.
    /// Send the movement command to walk through the now-revealed exit.
    /// Follows the same pattern as SendMovementAfterDoor/SendMovementAfterSearch.
    /// </summary>
    private void SendMovementAfterMultiAction()
    {
        if (_currentStepIndex >= _steps.Count)
            return;

        var step = _steps[_currentStepIndex];
        _multiActionSubState = MultiActionSubState.WaitingForMove;
        _roomTracker.ClearLineBuffer();
        _logMessage($"🔮 Moving through revealed exit: {step.Command}");
        _debugLog?.Write("MULTIACTION_MOVE", $"cmd='{step.Command}' toKey='{step.ToKey}'");
        _roomTracker.OnPlayerCommand(step.Command);
        _sendCommand(step.Command);
        _clearRoomEnemies();
        StartStepTimer();
    }

    /// <summary>
    /// Start the delay timer between action commands.
    /// Uses the configurable MultiActionDelayMs setting.
    /// </summary>
    private void StartMultiActionDelayTimer()
    {
        StopMultiActionDelayTimer();
        var delayMs = _getMultiActionDelayMs();
        _multiActionDelayTimer = new System.Timers.Timer(delayMs);
        _multiActionDelayTimer.AutoReset = false;
        _multiActionDelayTimer.Elapsed += (s, e) => OnMultiActionDelayElapsed();
        _multiActionDelayTimer.Start();
    }

    /// <summary>
    /// Stop the multi-action delay timer.
    /// </summary>
    private void StopMultiActionDelayTimer()
    {
        _multiActionDelayTimer?.Stop();
        _multiActionDelayTimer?.Dispose();
        _multiActionDelayTimer = null;
    }

    /// <summary>
    /// Delay timer elapsed — send the next action command or the movement command.
    /// </summary>
    private void OnMultiActionDelayElapsed()
    {
        StopMultiActionDelayTimer();

        if (_state != AutoWalkState.Walking || !_multiActionStepActive)
            return;

        if (_multiActionSteps != null && _multiActionCurrentIndex < _multiActionSteps.Count)
        {
            // More action commands to send
            SendNextActionCommand();
        }
        else
        {
            // All action commands sent — send movement
            SendMovementAfterMultiAction();
        }
    }

    /// <summary>
    /// Multi-action step failed — abort the walk.
    /// </summary>
    private void FailMultiActionStep(string reason)
    {
        ResetMultiActionState();
        StopTimers();
        SetState(AutoWalkState.Failed);
        _logMessage($"❌ Auto-walk failed — {reason}");
        _debugLog?.Write("MULTIACTION_FAIL", $"reason='{reason}'");
        OnWalkFailed?.Invoke($"{reason}. The exit may require items or remote actions.");
    }

    /// <summary>
    /// Reset all multi-action handling state.
    /// </summary>
    private void ResetMultiActionState()
    {
        _multiActionStepActive = false;
        _multiActionSubState = MultiActionSubState.None;
        _multiActionCurrentIndex = 0;
        _multiActionSteps = null;
        StopMultiActionDelayTimer();
    }

    #endregion

    #region Remote Action Handling

    /// <summary>
    /// Start the remote action delay timer — waits MultiActionDelayMs before advancing to next step.
    /// </summary>
    private void StartRemoteActionDelayTimer()
    {
        StopRemoteActionDelayTimer();
        var delayMs = _getMultiActionDelayMs();  // Reuse same delay setting
        _remoteActionDelayTimer = new System.Timers.Timer(delayMs);
        _remoteActionDelayTimer.AutoReset = false;
        _remoteActionDelayTimer.Elapsed += (s, e) => OnRemoteActionDelayElapsed();
        _remoteActionDelayTimer.Start();
    }

    /// <summary>
    /// Stop the remote action delay timer.
    /// </summary>
    private void StopRemoteActionDelayTimer()
    {
        _remoteActionDelayTimer?.Stop();
        _remoteActionDelayTimer?.Dispose();
        _remoteActionDelayTimer = null;
    }

    /// <summary>
    /// Remote action delay elapsed — advance to next step.
    /// </summary>
    private void OnRemoteActionDelayElapsed()
    {
        StopRemoteActionDelayTimer();

        if (_state != AutoWalkState.Walking || !_remoteActionStepActive)
            return;

        _remoteActionStepActive = false;
        _currentStepIndex++;
        _debugLog?.Write("REMOTE_ACTION_DONE", $"advancing to step {_currentStepIndex + 1}/{_steps.Count}");
        SendNextStep();
    }

    /// <summary>
    /// Reset remote action handling state.
    /// </summary>
    private void ResetRemoteActionState()
    {
        _remoteActionStepActive = false;
        StopRemoteActionDelayTimer();
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
        StopCombatVerificationTimer();
        StopCombatHeartbeatTimer();
        StopMultiActionDelayTimer();
        StopDoorBypassDelayTimer();
        StopRemoteActionDelayTimer();
        StopResyncTimer();
        _resyncInProgress = false;
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
    Failed,
    Disconnected
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
