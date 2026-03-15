namespace MudProxyViewer;

/// <summary>
/// Orchestrates repeating loop execution.
/// 
/// Validates loop definitions against the room graph, expands room-key pairs
/// into PathSteps (with exit commands, door types, etc.), feeds the expanded
/// path to AutoWalkManager, and restarts automatically on completion.
/// 
/// Sits on top of AutoWalkManager — it doesn't know about loops. LoopManager
/// is the higher-level coordinator that feeds it paths and reacts to events.
/// 
/// Integration:
///   - Created by GameManager, wired to AutoWalkManager events
///   - LoopDialog provides the UI for creating/starting/stopping loops
///   - RoomGraphManager provides room data for validation and expansion
///   - ExperienceTracker provides exp stats for loop performance tracking
/// </summary>
public class LoopManager
{
    // ── Dependencies ──
    private readonly AutoWalkManager _autoWalkManager;
    private readonly RoomGraphManager _roomGraph;
    private readonly RoomTracker _roomTracker;
    private readonly ExperienceTracker _experienceTracker;
    private readonly Action<string> _logMessage;
    private Func<bool>? _isFollower;                     // PartyManager.IsFollower

    // ── Debug logging ──
    private DebugLogWriter? _debugLog;

    // ── Loop state ──
    private LoopDefinition? _activeLoop;
    private LoopState _state = LoopState.Idle;
    private int _currentLap = 0;
    private int _consecutiveFailures = 0;
    private const int MaxConsecutiveFailures = 3;

    // ── Stats tracking ──
    private DateTime _startTime;
    private long _expAtStart;

    // ── Events ──
    /// <summary>Loop state changed (Idle, Running, Paused, Failed).</summary>
    public event Action<LoopState>? OnLoopStateChanged;

    /// <summary>A lap completed. Parameter is the lap number just finished.</summary>
    public event Action<int>? OnLapCompleted;

    /// <summary>Loop failed and stopped. Parameter is the reason.</summary>
    public event Action<string>? OnLoopFailed;

    /// <summary>Loop stats updated (called on lap complete, periodic, etc.).</summary>
    public event Action? OnLoopStatsUpdated;

    /// <summary>Log message for the system log.</summary>
    public event Action<string>? OnLogMessage;

    public LoopManager(
        AutoWalkManager autoWalkManager,
        RoomGraphManager roomGraph,
        RoomTracker roomTracker,
        ExperienceTracker experienceTracker,
        Action<string> logMessage)
    {
        _autoWalkManager = autoWalkManager;
        _roomGraph = roomGraph;
        _roomTracker = roomTracker;
        _experienceTracker = experienceTracker;
        _logMessage = logMessage;

        // Subscribe to walk events
        _autoWalkManager.OnWalkCompleted += OnWalkCompleted;
        _autoWalkManager.OnWalkFailed += OnWalkFailed;
    }

    #region Properties

    public LoopState State => _state;
    public LoopDefinition? ActiveLoop => _activeLoop;
    public int CurrentLap => _currentLap;
    public bool IsActive => _state == LoopState.Running || _state == LoopState.Paused;

    public TimeSpan Runtime => IsActive || _state == LoopState.Failed
        ? DateTime.Now - _startTime
        : TimeSpan.Zero;

    public long ExpGainedDuringLoop => IsActive || _state == LoopState.Failed
        ? _experienceTracker.SessionExpGained - _expAtStart
        : 0;

    public long ExpPerHourDuringLoop
    {
        get
        {
            var hours = Runtime.TotalHours;
            if (hours < 0.01)
                return 0;
            return (long)(ExpGainedDuringLoop / hours);
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Set the follower-state delegate. When true, Start() refuses to begin.
    /// </summary>
    public void SetFollowerCheck(Func<bool> isFollower)
    {
        _isFollower = isFollower;
    }

    /// <summary>
    /// Validate a loop definition against the room graph.
    /// Checks that all rooms exist, direct exits exist between consecutive pairs,
    /// and the loop closes (last step → first step).
    /// </summary>
    public LoopValidationResult Validate(LoopDefinition loop)
    {
        var result = new LoopValidationResult { IsValid = true };

        if (loop.Steps.Count < 2)
        {
            result.IsValid = false;
            result.Errors.Add("Loop must have at least 2 steps.");
            return result;
        }

        var requirements = new PathRequirements();
        int totalMoveCommands = 0;

        // Validate each consecutive pair + the loop-back (last → first)
        for (int i = 0; i < loop.Steps.Count; i++)
        {
            var fromStep = loop.Steps[i];
            var toStep = loop.Steps[(i + 1) % loop.Steps.Count];
            bool isLoopBack = (i == loop.Steps.Count - 1);

            // Check rooms exist
            var fromRoom = _roomGraph.GetRoom(fromStep.RoomKey);
            if (fromRoom == null)
            {
                result.IsValid = false;
                result.Errors.Add($"Step {i + 1}: Room {fromStep.RoomKey} not found in room data.");
                continue;
            }

            // Update cached room name
            fromStep.RoomName = fromRoom.Name;

            // Find direct exit to next room
            var exit = FindDirectExit(fromRoom, toStep.RoomKey);
            if (exit == null)
            {
                result.IsValid = false;
                var stepDesc = isLoopBack ? $"Loop-back (step {i + 1} → step 1)" : $"Step {i + 1} → {i + 2}";
                result.Errors.Add($"{stepDesc}: No direct exit from {fromStep.RoomKey} ({fromRoom.Name}) to {toStep.RoomKey}.");
                continue;
            }

            if (!exit.Traversable)
            {
                result.IsValid = false;
                var stepDesc = isLoopBack ? $"Loop-back (step {i + 1} → step 1)" : $"Step {i + 1} → {i + 2}";
                result.Errors.Add($"{stepDesc}: Exit from {fromStep.RoomKey} to {toStep.RoomKey} is not traversable ({exit.ExitType}).");
                continue;
            }

            totalMoveCommands++;

            // Check door requirements
            if (exit.ExitType == RoomExitType.Door)
            {
                requirements.HasDoors = true;
                if (exit.DoorStatRequirement > requirements.MaxDoorStatRequirement)
                    requirements.MaxDoorStatRequirement = exit.DoorStatRequirement;

                if (exit.DoorStatRequirement > 0)
                {
                    var stepDesc = isLoopBack ? $"Loop-back (step {i + 1} → step 1)" : $"Step {i + 1} → {i + 2}";
                    result.Warnings.Add($"{stepDesc}: Door requires {exit.DoorStatRequirement} picklocks/strength.");
                }
            }

            // Check for consecutive duplicates
            if (fromStep.RoomKey == toStep.RoomKey)
            {
                var stepDesc = isLoopBack ? $"Loop-back" : $"Step {i + 1} and {i + 2}";
                result.Warnings.Add($"{stepDesc}: Same room listed consecutively ({fromStep.RoomKey}).");
            }
        }

        // Update the last step's cached room name
        var lastRoom = _roomGraph.GetRoom(loop.Steps[^1].RoomKey);
        if (lastRoom != null)
            loop.Steps[^1].RoomName = lastRoom.Name;

        result.Requirements = requirements;
        result.TotalMoveCommands = totalMoveCommands;
        return result;
    }

    /// <summary>
    /// Start executing a loop. Validates first, then begins the first lap.
    /// </summary>
    public bool Start(LoopDefinition loop)
    {
        if (_isFollower?.Invoke() == true)
        {
            _logMessage("🚫 Cannot start loop — currently following a party leader");
            OnLoopFailed?.Invoke("Cannot run loops while following a party leader.");
            return false;
        }

        if (IsActive)
        {
            _logMessage("⚠️ Loop already active — stop it first.");
            return false;
        }

        // Validate
        var validation = Validate(loop);
        if (!validation.IsValid)
        {
            var errorSummary = string.Join("; ", validation.Errors);
            _logMessage($"❌ Loop validation failed: {errorSummary}");
            OnLoopFailed?.Invoke($"Loop validation failed: {validation.Errors.FirstOrDefault() ?? "Unknown error"}");
            return false;
        }

        // Create debug log for this loop session
        _debugLog?.Close();
        _debugLog = DebugLogWriter.Create("loop");
        _autoWalkManager.SetDebugLog(_debugLog);
        _debugLog.Write("LOOP_START", $"loop='{loop.Name}' steps={loop.Steps.Count}");

        _activeLoop = loop;
        _currentLap = 0;
        _consecutiveFailures = 0;
        _startTime = DateTime.Now;
        _expAtStart = _experienceTracker.SessionExpGained;

        _logMessage($"🔄 Loop started: {loop.Name} ({loop.Steps.Count} waypoints)");
        OnLogMessage?.Invoke($"🔄 Loop started: {loop.Name} — {loop.Steps.Count} waypoints");

        SetState(LoopState.Running);
        return StartNextLap();
    }

    /// <summary>
    /// Notify the loop that the connection was lost.
    /// Keeps the loop definition, lap count, and state alive so we can
    /// resume after reconnect. The walker has already been suspended by
    /// its own OnDisconnected() — it still has its step list.
    /// </summary>
    public void OnDisconnected()
    {
        if (!IsActive)
            return;

        _logMessage($"📡 Loop suspended — connection lost (lap {_currentLap}, runtime {Runtime:hh\\:mm\\:ss})");
        _debugLog?.Write("LOOP_DISCONNECTED", $"lap={_currentLap} runtime={Runtime:hh\\:mm\\:ss} loop='{_activeLoop?.Name}'");

        // Do NOT change state, do NOT clear the loop definition.
        // The loop stays in Running (or Paused) state so IsActive remains true.
        // The walker still has its steps. On reconnect, we tell it to resume.
    }

    /// <summary>
    /// Stop the loop immediately.
    /// </summary>
    public void Stop()
    {
        if (!IsActive)
            return;

        // Stop the current walk
        _autoWalkManager.Stop();

        var laps = _currentLap;
        var runtime = Runtime;
        var exp = ExpGainedDuringLoop;

        SetState(LoopState.Idle);
        _logMessage($"🛑 Loop stopped after {laps} laps ({runtime:hh\\:mm\\:ss}, {ExperienceTracker.FormatNumber(exp)} exp)");
        _debugLog?.Write("LOOP_STOP", $"laps={laps} runtime={runtime:hh\\:mm\\:ss} exp={exp}");
        _autoWalkManager.SetDebugLog(null);
        _debugLog?.Close();
        _debugLog = null;
        OnLogMessage?.Invoke($"🛑 Loop stopped: {laps} laps completed");

        _activeLoop = null;
    }

    /// <summary>
    /// Pause the loop. The current walk segment will finish, then the loop won't restart.
    /// </summary>
    public void Pause()
    {
        if (_state != LoopState.Running)
            return;

        SetState(LoopState.Paused);
        _logMessage("⏸️ Loop paused — current walk will finish, then wait");
        OnLogMessage?.Invoke("⏸️ Loop paused");
    }

    /// <summary>
    /// Resume a paused loop. Starts the next lap immediately.
    /// </summary>
    public void Resume()
    {
        if (_state != LoopState.Paused)
            return;

        _logMessage("▶️ Loop resumed");
        OnLogMessage?.Invoke("▶️ Loop resumed");
        SetState(LoopState.Running);

        // If the walker is idle (last segment finished while paused), start next lap
        if (!_autoWalkManager.IsActive)
            StartNextLap();
    }

    /// <summary>
    /// Notify the loop that the connection has been restored and the player
    /// is back in the game. If the walker was mid-walk when we disconnected,
    /// tell it to resume from its existing step list. If the walker had finished
    /// between laps, start a fresh lap.
    /// </summary>
    public void OnReconnected()
    {
        if (!IsActive)
            return;

        if (_activeLoop == null)
            return;

        _consecutiveFailures = 0;

        if (_autoWalkManager.State == AutoWalkState.Disconnected)
        {
            // Walker was mid-walk — it still has the loop's step list.
            // Tell it to find its position and resume. No new path needed.
            _logMessage($"📡 Reconnected — resuming loop '{_activeLoop.Name}' from current position (lap {_currentLap})");
            _debugLog?.Write("LOOP_RECONNECTED", $"lap={_currentLap} loop='{_activeLoop.Name}' action=resume_walk");
            _autoWalkManager.OnReconnected();
        }
        else
        {
            // Walker wasn't mid-walk (finished between laps, or other state).
            // Start a fresh lap from the current room.
            _logMessage($"📡 Reconnected — starting fresh lap for loop '{_activeLoop.Name}' (lap {_currentLap})");
            _debugLog?.Write("LOOP_RECONNECTED", $"lap={_currentLap} loop='{_activeLoop.Name}' action=start_next_lap");
            StartNextLap();
        }
    }

    #endregion

    #region Walk Event Handlers

    /// <summary>
    /// Called when AutoWalkManager completes a walk segment.
    /// Increments lap counter and starts the next lap.
    /// </summary>
    private void OnWalkCompleted(string name)
    {
        _debugLog?.Write($"WALK_COMPLETED: name='{name}' isActive={IsActive} state={_state} walkerState={_autoWalkManager.State}");

        if (!IsActive)
        {
            _debugLog?.Write("WALK_COMPLETED: ignored — loop not active");
            return;
        }

        _currentLap++;
        _consecutiveFailures = 0;

        var expRate = ExpPerHourDuringLoop;
        _logMessage($"🔄 Lap {_currentLap} complete | Runtime: {Runtime:hh\\:mm\\:ss} | EXP/hr: {ExperienceTracker.FormatNumber(expRate)}");
        _debugLog?.Write($"LAP_COMPLETE: lap={_currentLap} runtime={Runtime:hh\\:mm\\:ss} expRate={expRate}");
        OnLapCompleted?.Invoke(_currentLap);
        OnLoopStatsUpdated?.Invoke();

        if (_state == LoopState.Paused)
        {
            _logMessage("⏸️ Loop paused — waiting to resume");
            _debugLog?.Write("WALK_COMPLETED: paused — not starting next lap");
            return;
        }

        // Start next lap
        _debugLog?.Write("WALK_COMPLETED: calling StartNextLap");
        StartNextLap();
    }

    /// <summary>
    /// Called when AutoWalkManager fails a walk segment.
    /// Retries from current position or stops after max failures.
    /// </summary>
    private void OnWalkFailed(string reason)
    {
        _debugLog?.Write($"WALK_FAILED: reason='{reason}' isActive={IsActive} state={_state} walkerState={_autoWalkManager.State} failures={_consecutiveFailures}");

        if (!IsActive)
        {
            _debugLog?.Write("WALK_FAILED: ignored — loop not active");
            return;
        }

        _consecutiveFailures++;
        _logMessage($"⚠️ Loop walk failed: {reason} (failure {_consecutiveFailures}/{MaxConsecutiveFailures})");
        _debugLog?.Write($"WALK_FAILED: failure {_consecutiveFailures}/{MaxConsecutiveFailures}");

        if (_consecutiveFailures >= MaxConsecutiveFailures)
        {
            SetState(LoopState.Failed);
            var msg = $"Loop stopped after {_consecutiveFailures} consecutive failures. Last error: {reason}";
            _logMessage($"❌ {msg}");
            _debugLog?.Write($"LOOP_FAILED: {msg}");
            OnLoopFailed?.Invoke(msg);
            _autoWalkManager.SetDebugLog(null);
            _debugLog?.Close();
            _debugLog = null;
            return;
        }

        // Retry — re-expand from current position and restart
        _logMessage("🔄 Retrying loop from current position...");
        _debugLog?.Write("WALK_FAILED: calling StartNextLap to retry");
        StartNextLap();
    }

    #endregion

    #region Loop Execution

    /// <summary>
    /// Expand the loop into a PathResult and start walking.
    /// On retry, starts from the player's current room to the nearest waypoint.
    /// </summary>
    private bool StartNextLap()
    {
        _debugLog?.Write($"START_NEXT_LAP: activeLoop={((_activeLoop?.Name) ?? "null")} walkerState={_autoWalkManager.State} walkerIsActive={_autoWalkManager.IsActive}");

        if (_activeLoop == null)
        {
            _debugLog?.Write("START_NEXT_LAP: aborted — no active loop");
            return false;
        }

        var path = ExpandLoop(_activeLoop);
        if (path == null || !path.Success || path.Steps.Count == 0)
        {
            _consecutiveFailures++;
            _debugLog?.Write($"START_NEXT_LAP: expand failed — failure {_consecutiveFailures}/{MaxConsecutiveFailures}");
            if (_consecutiveFailures >= MaxConsecutiveFailures)
            {
                SetState(LoopState.Failed);
                _logMessage("❌ Loop failed — could not expand loop path");
                _debugLog?.Write("LOOP_FAILED: could not expand path");
                OnLoopFailed?.Invoke("Could not calculate loop path from current position.");
                return false;
            }
            _logMessage("⚠️ Could not expand loop path — will retry");
            return false;
        }

        // Expand any remote-action exits in the path
        var remoteExpander = new RemoteActionPathExpander(_roomGraph);
        var remoteExpanded = remoteExpander.Expand(path);
        if (remoteExpanded.Success)
            path = remoteExpanded;

        _debugLog?.Write($"START_NEXT_LAP: expanded to {path.Steps.Count} steps, calling StartWalk");
        var result = _autoWalkManager.StartWalk(path);
        _debugLog?.Write($"START_NEXT_LAP: StartWalk returned {result}, walkerState={_autoWalkManager.State}");
        return result;
    }

    /// <summary>
    /// Expand a loop definition into a PathResult by looking up direct exits
    /// between each consecutive room-key pair (including last → first).
    /// </summary>
    private PathResult? ExpandLoop(LoopDefinition loop)
    {
        var steps = new List<PathStep>();
        var requirements = new PathRequirements();

        for (int i = 0; i < loop.Steps.Count; i++)
        {
            var fromKey = loop.Steps[i].RoomKey;
            var toKey = loop.Steps[(i + 1) % loop.Steps.Count].RoomKey;

            var fromRoom = _roomGraph.GetRoom(fromKey);
            if (fromRoom == null)
            {
                _logMessage($"⚠️ Loop expansion failed: room {fromKey} not found");
                return null;
            }

            var exit = FindDirectExit(fromRoom, toKey);
            if (exit == null)
            {
                _logMessage($"⚠️ Loop expansion failed: no exit from {fromKey} to {toKey}");
                return null;
            }

            var toRoom = _roomGraph.GetRoom(toKey);
            steps.Add(new PathStep
            {
                Command = exit.Command,
                Direction = exit.Direction,
                FromKey = fromKey,
                ToKey = toKey,
                ToName = toRoom?.Name ?? "Unknown",
                ExitType = exit.ExitType,
                DoorStatRequirement = exit.DoorStatRequirement
            });

            if (exit.ExitType == RoomExitType.Door)
            {
                requirements.HasDoors = true;
                if (exit.DoorStatRequirement > requirements.MaxDoorStatRequirement)
                    requirements.MaxDoorStatRequirement = exit.DoorStatRequirement;
            }
        }

        // Build the starting room key from the first step
        var startKey = loop.Steps[0].RoomKey;
        var destKey = loop.Steps[0].RoomKey; // Loop destination is back to start

        return new PathResult
        {
            Success = true,
            StartKey = startKey,
            DestinationKey = destKey,
            Steps = steps,
            TotalSteps = steps.Count,
            Requirements = requirements
        };
    }

    /// <summary>
    /// Find a direct exit from a room to a specific destination key.
    /// Returns the first traversable exit found, or null if none exists.
    /// </summary>
    private RoomExit? FindDirectExit(RoomNode fromRoom, string toKey)
    {
        // Prefer traversable exits
        foreach (var exit in fromRoom.Exits)
        {
            if (exit.DestinationKey == toKey && exit.Traversable)
                return exit;
        }

        // Fall back to any exit (for validation error messages)
        return null;
    }

    #endregion

    #region Helpers

    private void SetState(LoopState newState)
    {
        if (_state != newState)
        {
            _state = newState;
            OnLoopStateChanged?.Invoke(newState);
        }
    }

    #endregion
}

/// <summary>
/// Loop execution states.
/// </summary>
public enum LoopState
{
    /// <summary>No loop running.</summary>
    Idle,

    /// <summary>Loop is actively executing laps.</summary>
    Running,

    /// <summary>Loop is paused — current walk finishes but next lap won't start.</summary>
    Paused,

    /// <summary>Loop failed after too many consecutive errors.</summary>
    Failed
}
