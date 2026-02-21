using System.Text.RegularExpressions;

namespace MudProxyViewer;

/// <summary>
/// Tracks the player's current room by parsing MUD server output.
/// 
/// Detects room displays by watching for the "Obvious exits:" line, then walks backwards
/// through a line buffer to extract the room name. Matches against the RoomGraphManager
/// to determine the exact room (Map/Room key).
/// 
/// Room display format (always in this order):
///   1. Room Name               (always 1 line, never indented)
///   2. Verbose description      (optional, indented lines — continuation lines may be flush-left)
///   3. You notice ... here.     (optional, can wrap multiple lines — items/currency)
///   4. Also here: ...           (optional, can wrap)
///   5. Obvious exits: ...       (always present, can wrap — flush left continuation)
///   6. The room is dimly lit    (optional, only this exact message, appears after exits)
/// 
/// BUFFERING STRATEGY: Only lines that could be part of a room block are buffered.
/// All other lines (combat messages, player actions, game system messages, commands, etc.)
/// are rejected. This prevents noise from being mistaken for room names.
/// </summary>
public class RoomTracker
{
    private readonly RoomGraphManager _roomGraph;

    // ── Line buffer for room detection ──
    // Only room-block lines are allowed in: room names, verbose descriptions,
    // "You notice...", "Also here:", and their continuations.
    private readonly List<string> _lineBuffer = new();
    private const int MaxBufferLines = 50;

    // ── State machine for tracking what we're currently capturing ──
    private enum BufferState
    {
        Idle,                  // Waiting for a potential room name
        InVerboseDescription,  // We've seen an indented line — buffer everything until a room-block marker
        InRoomBlock,           // We've seen a "You notice" or "Also here:" — room block is active
    }
    private BufferState _bufferState = BufferState.Idle;

    // ── Obvious exits continuation ──
    private bool _capturingExitsLine = false;
    private string _exitsLineBuffer = "";

    // ── "You notice" / "Also here" continuation tracking ──
    // When we see "You notice..." that doesn't end with "here.", we know the next line(s) continue it.
    private bool _inYouNoticeContinuation = false;
    private bool _inAlsoHereContinuation = false;

    // ── Movement tracking ──
    private string? _pendingMoveCommand = null;
    private string? _pendingMoveFromKey = null;
    private string? _pendingLookDirection = null;

    // ── Current state ──
    private RoomNode? _currentRoom;
    private string _currentRoomName = "";
    private DateTime _lastDetectionTime = DateTime.MinValue;
    private List<VisibleExit> _currentVisibleExits = new();
    private string? _lastSentCommand = null;

    // ── Room history for disambiguation ──
    private readonly List<RoomTransition> _roomHistory = new();
    private const int MaxRoomHistory = 10;

    // ── Known direction words ──
    private static readonly string[] DirectionWords =
    {
        "northeast", "northwest", "southeast", "southwest",
        "north", "south", "east", "west",
        "up", "down"
    };

    // ── Map full direction word to short key ──
    private static readonly Dictionary<string, string> DirectionWordToKey = new(StringComparer.OrdinalIgnoreCase)
    {
        { "north", "N" }, { "south", "S" }, { "east", "E" }, { "west", "W" },
        { "northeast", "NE" }, { "northwest", "NW" }, { "southeast", "SE" }, { "southwest", "SW" },
        { "up", "U" }, { "down", "D" }
    };

    // ── Map movement commands to direction keys ──
    private static readonly Dictionary<string, string> MoveCommandToDirectionKey = new(StringComparer.OrdinalIgnoreCase)
    {
        { "n", "N" }, { "s", "S" }, { "e", "E" }, { "w", "W" },
        { "ne", "NE" }, { "nw", "NW" }, { "se", "SE" }, { "sw", "SW" },
        { "u", "U" }, { "d", "D" },
        { "north", "N" }, { "south", "S" }, { "east", "E" }, { "west", "W" },
        { "northeast", "NE" }, { "northwest", "NW" }, { "southeast", "SE" }, { "southwest", "SW" },
        { "up", "U" }, { "down", "D" }
    };

    // ── Modifiers that mean an exit is BLOCKED ──
    // Add new entries here as we discover more blocked-exit prefixes.
    private static readonly string[] BlockedModifiers =
    {
        "closed"
    };

    // ── Movement failure messages ──
    // Add new entries here as we discover more failure messages.
    private static readonly string[] MovementFailureMessages =
    {
        "There is no exit in that direction!",
        "The door is closed!",
        "The gate is closed!"
    };

    // ── Regex patterns ──
    private static readonly Regex ObviousExitsRegex = new(@"^Obvious exits:\s*(.+)", RegexOptions.Compiled);
    private static readonly Regex YouNoticeRegex = new(@"^You notice\s", RegexOptions.Compiled);
    private static readonly Regex AlsoHereRegex = new(@"^Also here:\s", RegexOptions.Compiled);
    private static readonly Regex HpPromptRegex = new(@"\[HP=\d+", RegexOptions.Compiled);
    private static readonly Regex HpPromptStripRegex = new(@"\[HP=\d+[^\]]*\]:?", RegexOptions.Compiled);

    // ── Events ──
    public event Action<RoomNode?>? OnRoomChanged;
    public event Action<string>? OnLogMessage;
    public event Action<string, List<VisibleExit>>? OnRoomDisplayDetected;

    public RoomTracker(RoomGraphManager roomGraph)
    {
        _roomGraph = roomGraph;
    }

    #region Properties

    /// <summary>Current room the player is in, or null if unknown.</summary>
    public RoomNode? CurrentRoom => _currentRoom;

    /// <summary>Current room key (e.g., "1/297") or empty if unknown.</summary>
    public string CurrentRoomKey => _currentRoom?.Key ?? "";

    /// <summary>Current room name as displayed by the MUD.</summary>
    public string CurrentRoomName => _currentRoomName;

    /// <summary>Currently visible exits as shown in "Obvious exits:" line.</summary>
    public IReadOnlyList<VisibleExit> CurrentVisibleExits => _currentVisibleExits.AsReadOnly();

    /// <summary>
    /// Manually set the current room (e.g., restored from a saved profile).
    /// The next room display will either confirm this or trigger normal matching.
    /// </summary>
    public void SetCurrentRoom(RoomNode room)
    {
        _currentRoom = room;
        _currentRoomName = room.Name;
        _lastDetectionTime = DateTime.MinValue;
    }

    /// <summary>
    /// Get the current room transition history (for saving to profile).
    /// </summary>
    public List<RoomTransition> GetRoomHistory() => new List<RoomTransition>(_roomHistory);

    /// <summary>
    /// Restore room transition history (e.g., from a saved profile).
    /// </summary>
    public void SetRoomHistory(List<RoomTransition> history)
    {
        _roomHistory.Clear();
        if (history != null)
        {
            foreach (var entry in history.TakeLast(MaxRoomHistory))
                _roomHistory.Add(entry);
        }
    }

    #endregion

    #region Command Tracking

    /// <summary>
    /// Call this when the player sends a command to the MUD.
    /// If the command is a movement direction, we record it for room-change tracking.
    /// </summary>
    public void OnPlayerCommand(string command)
    {
        var trimmed = command.Trim().ToLower();

        if (MoveCommandToDirectionKey.ContainsKey(trimmed))
        {
            _pendingMoveCommand = trimmed;
            _pendingMoveFromKey = _currentRoom?.Key;
            _pendingLookDirection = null;
            return;
        }

        // Check if the command is a "look <direction>" variant.
        // When the player looks into an adjacent room, the server displays that room's
        // full room block — but the player hasn't actually moved. We flag this so the
        // room tracker ignores the resulting room display.
        var lookDirection = ParseLookCommand(trimmed);
        if (lookDirection != null)
        {
            _pendingLookDirection = lookDirection;
            return;
        }
    }

    /// <summary>
    /// Call this for EVERY command sent to the MUD (not just movement).
    /// Tracks the command so its server echo can be filtered from the room buffer.
    /// </summary>
    public void OnCommandSent(string command)
    {
        _lastSentCommand = command.Trim();
    }

    #endregion

    #region Message Processing

    /// <summary>
    /// Process a line of text from the MUD server.
    /// Call this for every line received.
    /// 
    /// Only lines that could be part of a room block are buffered.
    /// All other lines are ignored, preventing noise from being mistaken for room names.
    /// </summary>
    public void ProcessLine(string line)
    {
        // Debug: log exits lines reaching RoomTracker to diagnose detection failures.
        // If a room detection fails silently and this log does NOT appear, the problem
        // is upstream (MessageRouter partial buffer held the line). If it DOES appear,
        // the problem is downstream (a guard in ProcessRoomDetection blocked it).
        var _dbgTrim = line.TrimEnd();
        if (_dbgTrim.StartsWith("Obvious exits:", StringComparison.OrdinalIgnoreCase))
            OnLogMessage?.Invoke($"🔬 LINE→EXITS: [{_bufferState}] capture={_capturingExitsLine} \"{_dbgTrim}\"");

        // Skip completely empty lines
        if (string.IsNullOrEmpty(line))
            return;

        // Strip trailing whitespace but preserve leading whitespace (indentation matters)
        var trimmedRight = line.TrimEnd();
        if (trimmedRight.Length == 0)
            return;

        // ── Check for movement failure ──
        foreach (var failMsg in MovementFailureMessages)
        {
            if (trimmedRight.Contains(failMsg, StringComparison.OrdinalIgnoreCase))
            {
                _pendingMoveCommand = null;
                _pendingMoveFromKey = null;
                return;
            }
        }

        // ── Filter entity movement messages ──
        // Lines like "Tester walks into the room from the north." or
        // "A large giant rat creeps into the room from the east!"
        // These can arrive interleaved with the next room's output during fast
        // movement, overwriting the real room name in the buffer. Discard them.
        if (trimmedRight.Contains("into the room", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // ── Filter party/entity action messages ──
        // "Tester moves to attack nasty thug." / "Tester breaks off combat."
        // These party action lines can land in the buffer between room displays
        // and get mistaken for room names.
        if (trimmedRight.Contains("moves to attack", StringComparison.OrdinalIgnoreCase) ||
            trimmedRight.Contains("breaks off combat", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // ── Filter command echoes ──
        // The server echoes back every command the player sends. When moving fast,
        // these echoes can land in the buffer and get extracted as room names.
        // Filter any line that exactly matches the last sent command.
        if (_lastSentCommand != null &&
            trimmedRight.Equals(_lastSentCommand, StringComparison.OrdinalIgnoreCase))
        {
            _lastSentCommand = null;
            return;
        }

        // ── Handle "Obvious exits:" line continuation ──
        if (_capturingExitsLine)
        {
            if (ContainsDirectionWord(trimmedRight) && !HpPromptRegex.IsMatch(trimmedRight) && trimmedRight != "The room is dimly lit")
            {
                _exitsLineBuffer += " " + trimmedRight.Trim();
                return;
            }
            else
            {
                _capturingExitsLine = false;
                ProcessRoomDetection(_exitsLineBuffer);
                _exitsLineBuffer = "";
                return;
            }
        }

        // ── Check for "Obvious exits:" — the trigger ──
        var exitsMatch = ObviousExitsRegex.Match(trimmedRight);
        if (exitsMatch.Success)
        {
            var exitsText = exitsMatch.Groups[1].Value.Trim();

            if (EndsWithDirectionWord(exitsText))
            {
                ProcessRoomDetection(exitsText);
            }
            else
            {
                _capturingExitsLine = true;
                _exitsLineBuffer = exitsText;
            }

            // Reset continuation state
            _inYouNoticeContinuation = false;
            _inAlsoHereContinuation = false;
            return;
        }

        // ── Whitelist: Only buffer lines that are part of a room block ──

        // "Also here: ..." — buffer it, check if it continues
        if (AlsoHereRegex.IsMatch(trimmedRight))
        {
            _bufferState = BufferState.InRoomBlock;
            _inAlsoHereContinuation = !trimmedRight.TrimEnd().EndsWith(".");
            _inYouNoticeContinuation = false;
            _lineBuffer.Add(trimmedRight);
            TrimBuffer();
            return;
        }

        // "You notice ..." — buffer it, check if it continues
        if (YouNoticeRegex.IsMatch(trimmedRight))
        {
            // Filter out "You notice Xyz sneaking in from the north." — these are player/monster
            // movement messages, not room item listings. Room item lines always contain "here."
            // or end without a direction phrase.
            if (Regex.IsMatch(trimmedRight, @"from the (north|south|east|west|northeast|southeast|northwest|southwest|above|below)[.!]", RegexOptions.IgnoreCase))
            {
                // This is a movement message (e.g., "You notice Tester sneaking in
                // from the north."), not a room item listing. Discard it as noise.
                // Do NOT clear the buffer — a valid room name may already be stored
                // there waiting for the exits line to trigger detection.
                return;
            }

            _bufferState = BufferState.InRoomBlock;
            _inYouNoticeContinuation = !trimmedRight.Contains(" here.");
            _inAlsoHereContinuation = false;
            _lineBuffer.Add(trimmedRight);
            TrimBuffer();
            return;
        }

        // Continuation of "You notice" (multi-line item lists)
        if (_inYouNoticeContinuation)
        {
            _lineBuffer.Add(trimmedRight);
            TrimBuffer();
            if (trimmedRight.Contains(" here.") || trimmedRight.TrimEnd().EndsWith("here."))
            {
                _inYouNoticeContinuation = false;
            }
            return;
        }

        // Continuation of "Also here:" (multi-line entity lists)
        if (_inAlsoHereContinuation)
        {
            _lineBuffer.Add(trimmedRight);
            TrimBuffer();
            if (trimmedRight.TrimEnd().EndsWith("."))
            {
                _inAlsoHereContinuation = false;
            }
            return;
        }

        // Indented lines — potential start of verbose description.
        // Only enter InVerboseDescription if the previous line in the buffer is a known room name.
        // This prevents game output with indented lines (par, stat, etc.) from being mistaken
        // for verbose room descriptions.
        if (trimmedRight.Length > 0 && (trimmedRight[0] == ' ' || trimmedRight[0] == '\t'))
        {
            if (_lineBuffer.Count > 0 && _roomGraph.IsLoaded)
            {
                var candidate = _lineBuffer[_lineBuffer.Count - 1].Trim();
                var matches = _roomGraph.GetRoomsByName(candidate);
                if (matches.Count > 0)
                {
                    // The line before this IS a known room name — this is a real verbose description
                    _bufferState = BufferState.InVerboseDescription;
                    _lineBuffer.Add(trimmedRight);
                    TrimBuffer();
                    return;
                }
            }

            // Not a known room name before the indented line — discard as noise
            _lineBuffer.Clear();
            _bufferState = BufferState.Idle;
            return;
        }

        // If we're in verbose description mode, flush-left continuation lines are part of
        // the description — buffer them. This continues until we hit "You notice", "Also here:",
        // or "Obvious exits:" (which are handled above and will exit this state).
        if (_bufferState == BufferState.InVerboseDescription)
        {
            _lineBuffer.Add(trimmedRight);
            TrimBuffer();
            return;
        }

        // "The room is dimly lit" — skip, it appears after Obvious exits
        if (trimmedRight == "The room is dimly lit")
            return;

        // ── Strip HP prompts ──
        // The HP prompt (e.g., "[HP=81/MA=60]:") often arrives as a partial line
        // that gets prepended to the next chunk by MessageRouter's TCP reassembly.
        // This creates lines like "[HP=81/MA=60]:Slum Street" where the room name
        // is glued to the prompt. Instead of discarding the entire line, strip the
        // prompt and continue processing whatever remains (which may be a room name).
        // NOTE: HpPromptStripRegex matches the FULL prompt "[HP=81/MA=63]:" whereas
        // HpPromptRegex only matches the opening "[HP=81" — using the detection
        // regex for stripping would leave "/MA=63]:" behind, still losing the room name.
        if (HpPromptRegex.IsMatch(trimmedRight))
        {
            trimmedRight = HpPromptStripRegex.Replace(trimmedRight, "").Trim();
            if (string.IsNullOrEmpty(trimmedRight))
                return;
            // Fall through to continue processing the remainder
        }

        // ── Everything else: potential room name ──
        // Validate against the room graph before accepting. Every room in the game
        // is in the database, so any line that doesn't match a known room name is
        // guaranteed to be noise (combat messages, gossip, system text, etc.).
        // This is an O(1) dictionary lookup — no performance concern.
        if (_roomGraph.IsLoaded && _roomGraph.GetRoomsByName(trimmedRight.Trim()).Count == 0)
        {
            // Not a known room name — discard as noise.
            // Don't clear the buffer; a valid room name may already be there.
            return;
        }

        // When we're Idle (not inside a room block), clear the buffer so noise doesn't
        // accumulate. The room name is always the last non-marker line before the room
        // block starts, so we only need to keep the most recent candidate.
        if (_bufferState == BufferState.Idle)
        {
            _lineBuffer.Clear();
        }

        _lineBuffer.Add(trimmedRight);
        TrimBuffer();
    }

    private void TrimBuffer()
    {
        while (_lineBuffer.Count > MaxBufferLines)
        {
            _lineBuffer.RemoveAt(0);
        }
    }

    #endregion

    #region Room Detection

    /// <summary>
    /// Called when we have a complete "Obvious exits:" line.
    /// Extracts the room name from the buffer and matches against the graph.
    /// </summary>
    private void ProcessRoomDetection(string exitsText)
    {
        // Step 1: Parse visible exits
        var visibleExits = ParseObviousExits(exitsText);

        // Step 2: Extract room name from buffer
        var roomName = ExtractRoomName();

        // Reset buffer state
        _bufferState = BufferState.Idle;
        _inYouNoticeContinuation = false;
        _inAlsoHereContinuation = false;

        if (string.IsNullOrEmpty(roomName))
        {
            var bufferDump = string.Join(" | ", _lineBuffer.Select(l => l.Length > 40 ? l.Substring(0, 40) + "…" : l));
            OnLogMessage?.Invoke($"🔬 ROOM DETECT: roomName is empty — buffer had {_lineBuffer.Count} lines: [{bufferDump}]");
            _lineBuffer.Clear();
            return;
        }

        // Store display state
        _currentRoomName = roomName;
        _currentVisibleExits = visibleExits;

        // Debug: show what ProcessRoomDetection sees before the guards evaluate it.
        // pending=null means no movement, guards will treat as redisplay.
        // current=room name shows what Guard 1 compares against.
        OnLogMessage?.Invoke($"🔬 ROOM DETECT: name='{roomName}' pending='{_pendingMoveCommand}' current='{_currentRoom?.Name}' elapsed={(DateTime.Now - _lastDetectionTime).TotalMilliseconds:F0}ms");

        OnRoomDisplayDetected?.Invoke(roomName, visibleExits);
        
        // If the player used a "look" command, this room display is from looking
        // into an adjacent room — the player hasn't actually moved. Skip detection.
        if (_pendingLookDirection != null)
        {
            _pendingLookDirection = null;
            _lineBuffer.Clear();
            return;
        }

        // Guard 1: No-move redisplay.
        // If the room display matches our current room and we didn't move, skip it.
        // Handles: pressing Enter, combat ending, room refresh, etc.
        // No timing restriction — redisplays can happen seconds after last detection.
        if (_currentRoom != null &&
            _pendingMoveCommand == null &&
            _currentRoom.Name.Equals(roomName, StringComparison.OrdinalIgnoreCase))
        {
            _lineBuffer.Clear();
            return;
        }

        // Guard 2: Party-member-follow redisplay.
        // When a party member follows into the room, the server sends a redisplay
        // of the current room. By this time the walker has already sent the NEXT
        // move command, so _pendingMoveCommand is set. However, real game-enforced
        // movement takes at least 1 second, so if we see the same room name
        // within 750ms of the last detection, it's a follow redisplay, not
        // arrival at the next room. Skip WITHOUT consuming the pending move.
        if (_currentRoom != null &&
            _pendingMoveCommand != null &&
            _currentRoom.Name.Equals(roomName, StringComparison.OrdinalIgnoreCase) &&
            (DateTime.Now - _lastDetectionTime).TotalMilliseconds < 750)
        {
            _lineBuffer.Clear();
            return;
        }

        // Step 3: Match against graph
        var matchedRoom = MatchRoom(roomName, visibleExits);

        // Record this room observation in history (only when player actually moved).
        // Redisplays (no movement) are not recorded to avoid flushing useful history.
        if (_pendingMoveCommand != null)
        {
            _roomHistory.Add(new RoomTransition
            {
                RoomName = roomName,
                ExitDirections = visibleExits.Select(e => e.DirectionKey).ToList(),
                DirectionMoved = _pendingMoveCommand
            });
            while (_roomHistory.Count > MaxRoomHistory)
                _roomHistory.RemoveAt(0);
        }

        // Clear pending movement BEFORE firing event.
        // OnRoomChanged subscribers (AutoWalkManager) may call OnPlayerCommand()
        // to set the NEXT step's pending move — clearing after the event would wipe it.
        _pendingMoveCommand = null;
        _pendingMoveFromKey = null;

        _lastDetectionTime = DateTime.Now;

        // Step 4: Update current room if changed
        if (matchedRoom?.Key != _currentRoom?.Key)
        {
            _currentRoom = matchedRoom;

            if (matchedRoom != null)
            {
                OnLogMessage?.Invoke($"📍 Room: [{matchedRoom.Key}] {matchedRoom.Name}");
            }
            else
            {
                OnLogMessage?.Invoke($"📍 Room: {roomName} (not matched to graph)");
            }

            OnRoomChanged?.Invoke(matchedRoom);
        }

        _lineBuffer.Clear();
    }

    /// <summary>
    /// Extract the room name from the buffer.
    /// 
    /// The room name is always the line immediately before the first room-block marker
    /// (verbose description, "You notice", or "Also here:").
    /// If there are no markers, the last line in the buffer is the room name
    /// (the room had no description, no items, and no entities).
    /// </summary>
    private string ExtractRoomName()
    {
        if (_lineBuffer.Count == 0)
            return "";

        // Find the first room-block marker line (indented, "You notice", or "Also here:")
        int firstMarkerIndex = -1;
        for (int i = 0; i < _lineBuffer.Count; i++)
        {
            var line = _lineBuffer[i];
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                firstMarkerIndex = i;
                break;
            }
            if (YouNoticeRegex.IsMatch(line))
            {
                firstMarkerIndex = i;
                break;
            }
            if (AlsoHereRegex.IsMatch(line))
            {
                firstMarkerIndex = i;
                break;
            }
        }

        if (firstMarkerIndex > 0)
        {
            // Room name is the line just before the first marker
            return _lineBuffer[firstMarkerIndex - 1].Trim();
        }

        if (firstMarkerIndex == 0)
        {
            // The very first line is a marker — no room name found
            return "";
        }

        // No markers found — the room had no description, no items, no entities.
        // The LAST line in the buffer is closest to "Obvious exits:", so it's the room name.
        return _lineBuffer[_lineBuffer.Count - 1].Trim();
    }

    #endregion

    #region Room Matching

    /// <summary>
    /// Match a detected room name + visible exits to a room in the graph.
    /// Uses multiple strategies for disambiguation.
    /// </summary>
    private RoomNode? MatchRoom(string roomName, List<VisibleExit> visibleExits)
    {
        if (!_roomGraph.IsLoaded)
            return null;

        // Strategy 1: Movement prediction
        if (_pendingMoveCommand != null && _pendingMoveFromKey != null)
        {
            var predicted = PredictRoomFromMovement(_pendingMoveFromKey, _pendingMoveCommand);
            OnLogMessage?.Invoke($"🔬 MATCH DEBUG: Strategy 1 — from='{_pendingMoveFromKey}' cmd='{_pendingMoveCommand}' predicted='{predicted?.Key}' name='{predicted?.Name}' roomName='{roomName}'");
            if (predicted != null && predicted.Name.Equals(roomName, StringComparison.OrdinalIgnoreCase))
            {
                return predicted;
            }
        }
        else
        {
            OnLogMessage?.Invoke($"🔬 MATCH DEBUG: Strategy 1 SKIPPED — pendingCmd='{_pendingMoveCommand}' pendingFrom='{_pendingMoveFromKey}'");
        }

        // Strategy 2: Exact name lookup
        var candidates = _roomGraph.GetRoomsByName(roomName);

        if (candidates.Count == 0)
            return null;

        if (candidates.Count == 1)
            return candidates[0];

        // Strategy 3: Room history disambiguation (before exit matching)
        // In areas with many duplicate room names AND similar exit patterns,
        // history-based backwards chain walking is far more reliable than
        // exit matching alone. Try it first when we have movement data.
        if (_pendingMoveCommand != null && _roomHistory.Count > 0)
        {
            var historyMatch = DisambiguateWithHistory(candidates, _pendingMoveCommand);
            if (historyMatch != null)
            {
                OnLogMessage?.Invoke($"🔬 MATCH DEBUG: Strategy 3 (history) — resolved [{historyMatch.Key}] {historyMatch.Name}");
                return historyMatch;
            }
        }

        // Strategy 4: Disambiguate by exits
        var exitDirections = visibleExits.Select(e => e.DirectionKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var exitMatches = new List<(RoomNode room, int score)>();
        foreach (var candidate in candidates)
        {
            var candidateExitDirs = candidate.Exits
                .Select(e => e.Direction)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int matchCount = exitDirections.Count(d => candidateExitDirs.Contains(d));
            int mismatchCount = exitDirections.Count(d => !candidateExitDirs.Contains(d));

            if (matchCount == exitDirections.Count && mismatchCount == 0)
            {
                exitMatches.Add((candidate, matchCount * 10));
            }
            else if (matchCount > 0)
            {
                exitMatches.Add((candidate, matchCount - mismatchCount));
            }
        }

        if (exitMatches.Count > 0)
        {
            var best = exitMatches.OrderByDescending(m => m.score).First();
            if (best.score > 0)
            {
                var tied = exitMatches.Count(m => m.score == best.score);
                if (tied == 1)
                    return best.room;

                if (_pendingMoveCommand != null && _pendingMoveFromKey != null)
                {
                    var predicted = PredictRoomFromMovement(_pendingMoveFromKey, _pendingMoveCommand);
                    if (predicted != null)
                    {
                        var tiedRooms = exitMatches.Where(m => m.score == best.score).Select(m => m.room);
                        var matchesPrediction = tiedRooms.FirstOrDefault(r => r.Key == predicted.Key);
                        if (matchesPrediction != null)
                            return matchesPrediction;
                    }
                }

                return best.room;
            }
        }

        // Strategy 5: Movement prediction alone
        if (_pendingMoveCommand != null && _pendingMoveFromKey != null)
        {
            var predicted = PredictRoomFromMovement(_pendingMoveFromKey, _pendingMoveCommand);
            if (predicted != null && predicted.Name.Equals(roomName, StringComparison.OrdinalIgnoreCase))
                return predicted;
        }

        OnLogMessage?.Invoke($"⚠️ Ambiguous room: \"{roomName}\" matches {candidates.Count} rooms, could not disambiguate");
        return null;
    }

    /// <summary>
    /// Given the room we were in and the direction we moved, look up the destination in the graph.
    /// </summary>
    private RoomNode? PredictRoomFromMovement(string fromKey, string moveCommand)
    {
        var fromRoom = _roomGraph.GetRoom(fromKey);
        if (fromRoom == null)
            return null;

        var cmdLower = moveCommand.Trim().ToLower();
        if (!MoveCommandToDirectionKey.TryGetValue(cmdLower, out var dirKey))
            return null;

        var exit = fromRoom.Exits.FirstOrDefault(e =>
            e.Direction.Equals(dirKey, StringComparison.OrdinalIgnoreCase));

        if (exit == null)
            return null;

        return _roomGraph.GetRoom(exit.DestinationKey);
    }

    /// <summary>
    /// Attempt to disambiguate candidates by walking backwards through room history.
    /// 
    /// For each candidate, we check: "Could the player have arrived here given the
    /// recorded sequence of moves and room names?" We build chains of room keys
    /// backwards through history, eliminating candidates where the graph connections
    /// don't match the observed transitions.
    /// 
    /// Example: 50 rooms named "Slum Street" with exits N,S. We moved east to get here.
    /// History says the previous room was "Slum Street, Bend". For each candidate,
    /// check if any room named "Slum Street, Bend" has an east exit leading to it.
    /// Most candidates won't have a valid source room and get eliminated.
    /// </summary>
    private RoomNode? DisambiguateWithHistory(List<RoomNode> candidates, string directionMoved)
    {
        if (_roomHistory.Count == 0 || string.IsNullOrEmpty(directionMoved))
            return null;

        // Each chain tracks a sequence of room keys: [currentCandidate, prevRoom, prevPrevRoom, ...]
        // We start with one chain per candidate.
        var chains = candidates.Select(c => new List<string> { c.Key }).ToList();

        // The direction used to arrive at the current (most recent) position in the chain
        string arrivalDirection = directionMoved;

        // Walk backwards through history, newest to oldest
        for (int h = _roomHistory.Count - 1; h >= 0 && chains.Count > 0; h--)
        {
            var histEntry = _roomHistory[h];

            // Convert the movement command to a direction key (e.g., "e" → "E")
            if (!MoveCommandToDirectionKey.TryGetValue(arrivalDirection, out var dirKey))
                break;

            // Get all rooms that could be the previous room (by name)
            var prevRoomCandidates = _roomGraph.GetRoomsByName(histEntry.RoomName);
            if (prevRoomCandidates.Count == 0)
                break;

            // For each surviving chain, check if any previous room candidate
            // has an exit in the right direction leading to the chain's tail
            var newChains = new List<List<string>>();
            foreach (var chain in chains)
            {
                var targetKey = chain[chain.Count - 1]; // room we need to reach

                foreach (var prevRoom in prevRoomCandidates)
                {
                    // Does this previous room have an exit in dirKey that leads to targetKey?
                    var exit = prevRoom.Exits.FirstOrDefault(e =>
                        e.Direction.Equals(dirKey, StringComparison.OrdinalIgnoreCase));

                    if (exit != null && exit.DestinationKey == targetKey)
                    {
                        var newChain = new List<string>(chain) { prevRoom.Key };
                        newChains.Add(newChain);
                    }
                }
            }

            chains = newChains;

            // Check if we've narrowed to a single original candidate
            var uniqueCurrentKeys = chains.Select(c => c[0]).Distinct().ToList();
            if (uniqueCurrentKeys.Count == 1)
            {
                return _roomGraph.GetRoom(uniqueCurrentKeys[0]);
            }

            // If all chains eliminated, history doesn't help
            if (chains.Count == 0)
                break;

            // Safety cap to prevent chain explosion in highly connected areas
            if (chains.Count > 500)
                break;

            // Next iteration checks one step further back
            arrivalDirection = histEntry.DirectionMoved;
        }

        return null;
    }

    #endregion

    #region Exit Parsing

    /// <summary>
    /// Parse the "Obvious exits:" content into structured exit data.
    /// </summary>
    private List<VisibleExit> ParseObviousExits(string exitsText)
    {
        var results = new List<VisibleExit>();

        if (string.IsNullOrWhiteSpace(exitsText))
            return results;

        if (exitsText.Trim().Equals("NONE!", StringComparison.OrdinalIgnoreCase) ||
            exitsText.Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase))
            return results;

        var segments = exitsText.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            var trimmed = segment.Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var exit = ParseSingleExit(trimmed);
            if (exit != null)
                results.Add(exit);
        }

        return results;
    }

    /// <summary>
    /// Parse a single exit segment like "north", "open door east", "secret passage south".
    /// Finds the direction word and treats everything before it as a modifier.
    /// </summary>
    private VisibleExit? ParseSingleExit(string segment)
    {
        var lower = segment.ToLower().Trim();

        foreach (var dirWord in DirectionWords)
        {
            if (lower == dirWord || lower.EndsWith(" " + dirWord))
            {
                var modifier = "";
                if (lower.Length > dirWord.Length)
                {
                    modifier = segment.Substring(0, segment.Length - dirWord.Length).Trim();
                }

                var dirKey = DirectionWordToKey[dirWord];
                var isBlocked = IsBlockedModifier(modifier);

                return new VisibleExit
                {
                    DirectionKey = dirKey,
                    DirectionWord = dirWord,
                    Modifier = modifier,
                    IsBlocked = isBlocked
                };
            }
        }

        OnLogMessage?.Invoke($"⚠️ Could not parse exit segment: \"{segment}\"");
        return null;
    }

    /// <summary>
    /// Check if a modifier string indicates a blocked exit.
    /// Add new blocked modifiers to the BlockedModifiers array at the top of this class.
    /// </summary>
    private static bool IsBlockedModifier(string modifier)
    {
        if (string.IsNullOrWhiteSpace(modifier))
            return false;

        var lower = modifier.ToLower();
        foreach (var blocked in BlockedModifiers)
        {
            if (lower.Contains(blocked))
                return true;
        }

        return false;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Check if a command is a "look direction" variant.
    /// Returns the direction key if it is, or null if it isn't.
    /// Add new look command patterns here as needed.
    /// </summary>
    private static string? ParseLookCommand(string command)
    {
        // Patterns: "l n", "l north", "look n", "look north", etc.
        string? suffix = null;

        if (command.StartsWith("l "))
            suffix = command.Substring(2).Trim();
        else if (command.StartsWith("look "))
            suffix = command.Substring(5).Trim();

        if (suffix == null)
            return null;

        if (MoveCommandToDirectionKey.ContainsKey(suffix))
            return MoveCommandToDirectionKey[suffix];

        return null;
    }

    /// <summary>Check if a line contains any known direction word.</summary>
    private static bool ContainsDirectionWord(string line)
    {
        var lower = line.ToLower();
        foreach (var dir in DirectionWords)
        {
            if (lower.Contains(dir))
                return true;
        }
        return false;
    }

    /// <summary>Check if text ends with a known direction word.</summary>
    private static bool EndsWithDirectionWord(string text)
    {
        var lower = text.TrimEnd().ToLower();
        foreach (var dir in DirectionWords)
        {
            if (lower.EndsWith(dir))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Reset all tracking state (e.g., on disconnect or character change).
    /// </summary>
    public void Reset()
    {
        _currentRoom = null;
        _currentRoomName = "";
        _currentVisibleExits.Clear();
        _pendingMoveCommand = null;
        _pendingMoveFromKey = null;
        _pendingLookDirection = null;
        _lineBuffer.Clear();
        _capturingExitsLine = false;
        _exitsLineBuffer = "";
        _bufferState = BufferState.Idle;
        _inYouNoticeContinuation = false;
        _inAlsoHereContinuation = false;
        _lastDetectionTime = DateTime.MinValue;
        _lastSentCommand = null;
        _roomHistory.Clear();
        OnRoomChanged?.Invoke(null);
    }

    #endregion
}

/// <summary>
/// Represents an exit as displayed in the "Obvious exits:" line.
/// </summary>
public class VisibleExit
{
    /// <summary>Short direction key: "N", "S", "E", "W", "NE", "NW", "SE", "SW", "U", "D"</summary>
    public string DirectionKey { get; set; } = "";

    /// <summary>Full direction word as shown: "north", "south", "up", etc.</summary>
    public string DirectionWord { get; set; } = "";

    /// <summary>Text before the direction word: "open door", "closed gate", "secret passage", etc. Empty for plain exits.</summary>
    public string Modifier { get; set; } = "";

    /// <summary>Whether this exit is currently blocked (e.g., "closed door", "closed gate").</summary>
    public bool IsBlocked { get; set; }

    public override string ToString()
    {
        if (string.IsNullOrEmpty(Modifier))
            return DirectionWord;
        return $"{Modifier} {DirectionWord}";
    }
}
