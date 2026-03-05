using System.Text.RegularExpressions;

namespace MudProxyViewer;

/// <summary>
/// Builds an in-memory directed graph of all game rooms and provides BFS shortest-path finding.
/// 
/// Room data is loaded from Rooms.json via GameDataCache (imported from the MDB game database).
/// Each room is identified by a composite key "MapNumber/RoomNumber" (e.g., "1/297").
/// 
/// Phase 1 scope: Only normal exits (plain MapNum/RoomNum with no modifiers) are treated as
/// traversable edges. Door, locked, hidden, text, and action-based exits are parsed and stored
/// but excluded from pathfinding until their mechanics are fully understood in later phases.
/// </summary>
public class RoomGraphManager
{
    private readonly Dictionary<string, RoomNode> _rooms = new();
    private readonly Dictionary<string, List<RoomNode>> _roomsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _gameDataPath;

    // Direction columns in the order they appear in the room data
    private static readonly string[] DirectionColumns = { "N", "S", "E", "W", "NE", "NW", "SE", "SW", "U", "D" };

    // Map direction column name to the command the player types
    private static readonly Dictionary<string, string> DirectionToCommand = new(StringComparer.OrdinalIgnoreCase)
    {
        { "N", "n" }, { "S", "s" }, { "E", "e" }, { "W", "w" },
        { "NE", "ne" }, { "NW", "nw" }, { "SE", "se" }, { "SW", "sw" },
        { "U", "u" }, { "D", "d" }
    };

    // Regex to extract MapNum/RoomNum from the start of an exit value
    // Matches: "1/298", "14/1384", etc. — with optional trailing content
    private static readonly Regex ExitDestinationRegex = new(@"^(\d+)/(\d+)", RegexOptions.Compiled);

    // Regex to detect Action entries (these are NOT exits)
    private static readonly Regex ActionRegex = new(@"^Action", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Regex to extract modifier in parentheses after the MapNum/RoomNum
    private static readonly Regex ModifierRegex = new(@"\(([^)]+)\)", RegexOptions.Compiled);

    // Regex to parse Action# entries: "Action#N [on the DIR exit of this room|room Map/Room]: cmd1, cmd2"
    // Group 1: step number (optional), Group 2: target direction, Group 3: room reference, Group 4: commands
    private static readonly Regex ActionDetailRegex = new(
        @"^Action(?:#(\d+))?\s*\[on the (\w+) exit of (this room|room \d+/\d+)\]:\s*(.*?)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Regex to extract N and order type from "Needs N Actions, any/specific order"
    private static readonly Regex NeedsActionsRegex = new(
        @"Needs\s+(\d+)\s+Actions?,\s*(any order|specific order)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Regex to detect Item requirement embedded in action commands: "(Item: NNN)"
    private static readonly Regex ActionItemRegex = new(
        @"\(Item:\s*(\d+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Regex to extract remote room reference: "room MapNum/RoomNum"
    private static readonly Regex RemoteRoomRegex = new(
        @"room\s+(\d+/\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public event Action<string>? OnLogMessage;

    public RoomGraphManager()
    {
        _gameDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MudProxyViewer",
            "Game Data");
    }

    #region Properties

    /// <summary>Whether room data has been loaded.</summary>
    public bool IsLoaded => _rooms.Count > 0;

    /// <summary>Total number of rooms in the graph.</summary>
    public int RoomCount => _rooms.Count;

    /// <summary>Total number of traversable edges (normal exits) in the graph.</summary>
    public int EdgeCount => _rooms.Values.Sum(r => r.Exits.Count(e => e.Traversable));

    /// <summary>Total number of non-traversable edges (doors, hidden, locked, text, action) in the graph.</summary>
    public int NonTraversableEdgeCount => _rooms.Values.Sum(r => r.Exits.Count(e => !e.Traversable));

    #endregion

    #region Loading

    /// <summary>
    /// Load or reload room data from GameDataCache (Rooms.json).
    /// Returns true if rooms were loaded successfully.
    /// </summary>
    public bool LoadFromGameData()
    {
        try
        {
            List<Dictionary<string, object?>>? roomRows = null;

            // Try cache first
            roomRows = GameDataCache.Instance.GetTable("Rooms");

            // Fall back to reading file directly
            if (roomRows == null)
            {
                var filePath = Path.Combine(_gameDataPath, "Rooms.json");
                if (!File.Exists(filePath))
                    return false;

                var json = File.ReadAllText(filePath);
                roomRows = ParseJsonArray(json);
            }

            if (roomRows == null || roomRows.Count == 0)
                return false;

            BuildGraph(roomRows);
            return true;
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error loading room graph: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reload room data (e.g., after a new MDB import).
    /// </summary>
    public void Reload()
    {
        _rooms.Clear();
        _roomsByName.Clear();
        LoadFromGameData();
    }

    #endregion

    #region Lookup

    /// <summary>
    /// Get a room by its key (e.g., "1/297").
    /// </summary>
    public RoomNode? GetRoom(string key)
    {
        _rooms.TryGetValue(key, out var room);
        return room;
    }

    /// <summary>
    /// Get a room by map and room number.
    /// </summary>
    public RoomNode? GetRoom(int mapNumber, int roomNumber)
    {
        return GetRoom($"{mapNumber}/{roomNumber}");
    }

    /// <summary>
    /// Search rooms by partial name match (case-insensitive).
    /// Returns up to maxResults matches.
    /// </summary>
    public List<RoomNode> SearchByName(string partial, int maxResults = 100)
    {
        if (string.IsNullOrWhiteSpace(partial))
            return new List<RoomNode>();

        var results = new List<RoomNode>();
        var searchTerm = partial.Trim();

        // Exact match first (by normalized name)
        if (_roomsByName.TryGetValue(searchTerm, out var exactMatches))
        {
            results.AddRange(exactMatches);
        }

        // Then partial matches
        if (results.Count < maxResults)
        {
            foreach (var kvp in _roomsByName)
            {
                if (kvp.Key.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var room in kvp.Value)
                    {
                        if (!results.Contains(room))
                        {
                            results.Add(room);
                            if (results.Count >= maxResults)
                                return results;
                        }
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Get all rooms with a specific name (case-insensitive exact match).
    /// Useful for disambiguation.
    /// </summary>
    public List<RoomNode> GetRoomsByName(string name)
    {
        if (_roomsByName.TryGetValue(name, out var rooms))
            return rooms;
        return new List<RoomNode>();
    }

    #endregion

    #region Pathfinding

    /// <summary>
    /// Find the shortest path between two rooms using BFS.
    /// Only traverses normal exits (plain directional exits with no modifiers).
    /// </summary>
    /// <param name="fromKey">Starting room key (e.g., "1/1")</param>
    /// <param name="toKey">Destination room key (e.g., "5/23")</param>
    /// <returns>A PathResult with the list of steps, or a failed result if no path exists.</returns>
    public PathResult FindPath(string fromKey, string toKey, Func<RoomExit, bool>? exitFilter = null)
    {
        var result = new PathResult
        {
            StartKey = fromKey,
            DestinationKey = toKey
        };

        // Validate start and destination exist
        if (!_rooms.ContainsKey(fromKey))
        {
            result.ErrorMessage = $"Start room {fromKey} not found in graph.";
            return result;
        }

        if (!_rooms.ContainsKey(toKey))
        {
            result.ErrorMessage = $"Destination room {toKey} not found in graph.";
            return result;
        }

        // Already there
        if (fromKey == toKey)
        {
            result.Success = true;
            return result;
        }

        // BFS
        var visited = new Dictionary<string, (string parentKey, RoomExit exitUsed)>();
        var queue = new Queue<string>();

        visited[fromKey] = (null!, null!);
        queue.Enqueue(fromKey);

        while (queue.Count > 0)
        {
            var currentKey = queue.Dequeue();
            var currentRoom = _rooms[currentKey];

            foreach (var exit in currentRoom.Exits)
            {
                if (!exit.Traversable)
                    continue;

                // Caller-provided filter (e.g., skip stat-gated doors player can't handle)
                if (exitFilter != null && !exitFilter(exit))
                    continue;

                var destKey = exit.DestinationKey;

                // Skip if destination room doesn't exist in our graph
                if (!_rooms.ContainsKey(destKey))
                    continue;

                if (visited.ContainsKey(destKey))
                    continue;

                visited[destKey] = (currentKey, exit);
                queue.Enqueue(destKey);

                // Found destination — reconstruct path
                if (destKey == toKey)
                {
                    result.Success = true;
                    result.Steps = ReconstructPath(fromKey, toKey, visited);
                    // Aggregate path requirements for UI display
                    var reqs = new PathRequirements();
                    foreach (var step in result.Steps)
                    {
                        if (step.ExitType == RoomExitType.Door)
                        {
                            reqs.HasDoors = true;
                            if (step.DoorStatRequirement > reqs.MaxDoorStatRequirement)
                                reqs.MaxDoorStatRequirement = step.DoorStatRequirement;
                        }
                        if (step.ExitType == RoomExitType.MultiActionHidden)
                            reqs.HasMultiActionExits = true;
                    }
                    result.Requirements = reqs;
                    result.TotalSteps = result.Steps.Count;
                    return result;
                }
            }
        }

        // No path found
        result.ErrorMessage = $"No path found from {fromKey} to {toKey}. The rooms may not be connected via normal exits.";
        return result;
    }

    /// <summary>
    /// Reconstruct the path from BFS visited map.
    /// Walks backwards from destination to start, then reverses.
    /// </summary>
    private List<PathStep> ReconstructPath(
        string fromKey,
        string toKey,
        Dictionary<string, (string parentKey, RoomExit exitUsed)> visited)
    {
        var steps = new List<PathStep>();
        var current = toKey;

        while (current != fromKey)
        {
            var (parentKey, exitUsed) = visited[current];
            var destRoom = _rooms.ContainsKey(current) ? _rooms[current] : null;

            steps.Add(new PathStep
            {
                Command = exitUsed.Command,
                Direction = exitUsed.Direction,
                FromKey = parentKey,
                ToKey = current,
                ToName = destRoom?.Name ?? "Unknown",
                ExitType = exitUsed.ExitType,
                DoorStatRequirement = exitUsed.DoorStatRequirement,
                DoorActionBypass = exitUsed.DoorActionBypass,
                MultiActionData = exitUsed.MultiActionData
            });

            current = parentKey;
        }

        steps.Reverse();
        return steps;
    }

    #endregion

    #region Graph Building

    /// <summary>
    /// Build the room graph from parsed JSON data.
    /// </summary>
    private void BuildGraph(List<Dictionary<string, object?>> roomRows)
    {
        _rooms.Clear();
        _roomsByName.Clear();

        int parsedCount = 0;
        int exitCount = 0;
        int skippedExits = 0;

        // Collect Action# entries during Pass 1 for association in Pass 2
        var actionEntries = new List<(string roomKey, string direction, string actionText)>();

        // ── Pass 1: Parse all rooms and exits ──
        foreach (var row in roomRows)
        {
            try
            {
                var mapNumber = GetInt(row, "Map Number");
                var roomNumber = GetInt(row, "Room Number");

                if (mapNumber <= 0 || roomNumber <= 0)
                    continue;

                var key = $"{mapNumber}/{roomNumber}";
                var name = GetString(row, "Name");

                var node = new RoomNode
                {
                    MapNumber = mapNumber,
                    RoomNumber = roomNumber,
                    Name = name,
                    Key = key,
                    Light = GetInt(row, "Light"),
                    Shop = GetInt(row, "Shop"),
                    Lair = GetString(row, "Lair")
                };

                // Parse all 10 directional exits
                foreach (var direction in DirectionColumns)
                {
                    var exitValue = GetString(row, direction);
                    if (string.IsNullOrWhiteSpace(exitValue) || exitValue.Trim() == "0")
                        continue;

                    var trimmedValue = exitValue.Trim();

                    // Collect Action# entries for Pass 2 (instead of silently skipping)
                    if (ActionRegex.IsMatch(trimmedValue))
                    {
                        actionEntries.Add((key, direction, trimmedValue));
                        continue;
                    }

                    var exit = ParseExit(direction, trimmedValue);

                    if (exit != null)
                    {
                        node.Exits.Add(exit);
                        exitCount++;

                        if (!exit.Traversable)
                            skippedExits++;
                    }
                }

                _rooms[key] = node;
                parsedCount++;

                // Index by name for search
                if (!string.IsNullOrEmpty(name))
                {
                    if (!_roomsByName.ContainsKey(name))
                        _roomsByName[name] = new List<RoomNode>();
                    _roomsByName[name].Add(node);
                }
            }
            catch
            {
                // Skip malformed rows
            }
        }

        // ── Pass 2: Associate Action entries with their target exits ──
        // Links Action#N entries to MultiActionHidden exits (Phase 5e)
        // Links unnumbered Action entries to Door exits as action bypasses (levers/switches)
        int actionsLinked = 0;
        int actionsRemote = 0;
        int actionsItemGated = 0;
        int doorBypasses = 0;

        foreach (var (roomKey, direction, actionText) in actionEntries)
        {
            var match = ActionDetailRegex.Match(actionText);
            if (!match.Success)
                continue;

            int stepNumber = match.Groups[1].Success && int.TryParse(match.Groups[1].Value, out var sn) ? sn : 1;
            string targetDirection = match.Groups[2].Value.ToUpper();
            string roomRef = match.Groups[3].Value;
            string commandsStr = match.Groups[4].Value.Trim();

            // Determine target room key (same room or remote)
            string targetRoomKey;
            bool isRemote;
            if (roomRef.Equals("this room", StringComparison.OrdinalIgnoreCase))
            {
                targetRoomKey = roomKey;
                isRemote = false;
            }
            else
            {
                var remoteMatch = RemoteRoomRegex.Match(roomRef);
                if (!remoteMatch.Success) continue;
                targetRoomKey = remoteMatch.Groups[1].Value;
                isRemote = true;
            }

            // Find the target room and its exit in the specified direction
            // Match MultiActionHidden exits (Phase 5e) OR Door exits (action bypass — e.g., lever opens a stat-gated door)
            if (!_rooms.TryGetValue(targetRoomKey, out var targetRoom))
                continue;

            var targetExit = targetRoom.Exits.FirstOrDefault(e =>
                e.Direction.Equals(targetDirection, StringComparison.OrdinalIgnoreCase)
                && (e.ExitType == RoomExitType.MultiActionHidden || e.ExitType == RoomExitType.Door));

            if (targetExit == null)
                continue;

            // Door exits: store action commands as bypass (lever/switch alternative to bash/pick)
            if (targetExit.ExitType == RoomExitType.Door)
            {
                var bypassCommands = commandsStr.Split(',')
                    .Select(c => c.Trim())
                    .Where(c => !string.IsNullOrEmpty(c))
                    .ToList();

                if (bypassCommands.Count > 0)
                {
                    targetExit.DoorActionBypass = bypassCommands;
                    actionsLinked++;
                    doorBypasses++;
                }
                continue;  // Skip MultiActionData processing below
            }

            // MultiActionHidden exits: add to action sequence (existing Phase 5e logic)
            if (targetExit.MultiActionData == null)
                continue;

            // Check for embedded item requirement: "(Item: NNN)"
            bool hasItemReq = false;
            int itemId = 0;
            var itemMatch = ActionItemRegex.Match(commandsStr);
            if (itemMatch.Success)
            {
                hasItemReq = true;
                int.TryParse(itemMatch.Groups[1].Value, out itemId);
                commandsStr = ActionItemRegex.Replace(commandsStr, "").TrimEnd(',').Trim();
            }

            // Parse alternative commands (comma-separated)
            var commands = commandsStr.Split(',')
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();

            if (commands.Count == 0)
                continue;

            var action = new ExitAction
            {
                StepNumber = stepNumber,
                Commands = commands,
                RequiresItem = hasItemReq,
                RequiredItemId = itemId
            };

            targetExit.MultiActionData.Actions.Add(action);

            if (isRemote)
            {
                targetExit.MultiActionData.HasRemoteActions = true;
                actionsRemote++;
            }
            if (hasItemReq)
            {
                targetExit.MultiActionData.HasItemRequirements = true;
                actionsItemGated++;
            }
            actionsLinked++;
        }

        // ── Pass 2b: Sort actions by step number and finalize traversability ──
        int multiActionTraversable = 0;
        int multiActionDeferred = 0;

        foreach (var room in _rooms.Values)
        {
            foreach (var exit in room.Exits)
            {
                if (exit.ExitType != RoomExitType.MultiActionHidden || exit.MultiActionData == null)
                    continue;

                // Sort actions by step number for correct execution order
                exit.MultiActionData.Actions.Sort((a, b) => a.StepNumber.CompareTo(b.StepNumber));

                // Make the exit traversable if it can be automated
                if (exit.MultiActionData.IsAutomatable)
                {
                    exit.Traversable = true;
                    skippedExits--;  // Was counted as non-traversable in Pass 1
                    multiActionTraversable++;
                }
                else
                {
                    multiActionDeferred++;
                }
            }
        }

        OnLogMessage?.Invoke(
            $"🗺️ Room graph loaded: {parsedCount} rooms, {exitCount} exits " +
            $"({exitCount - skippedExits} traversable, {skippedExits} non-traversable). " +
            $"Actions: {actionsLinked} linked ({doorBypasses} door bypasses), " +
            $"multi-action: {multiActionTraversable} traversable, " +
            $"{multiActionDeferred} deferred ({actionsRemote} remote, {actionsItemGated} item-gated)");
    }

    /// <summary>
    /// Parse a single exit column value into a RoomExit, or null if no exit.
    /// </summary>
    private RoomExit? ParseExit(string direction, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim() == "0")
            return null;

        var trimmed = value.Trim();

        // Action entries are NOT exits — they describe prerequisites for other exits
        if (ActionRegex.IsMatch(trimmed))
            return null;

        // Extract MapNum/RoomNum from the start
        var destMatch = ExitDestinationRegex.Match(trimmed);
        if (!destMatch.Success)
            return null;

        var destMap = int.Parse(destMatch.Groups[1].Value);
        var destRoom = int.Parse(destMatch.Groups[2].Value);
        var destKey = $"{destMap}/{destRoom}";

        // Determine exit type and command from any modifier in parentheses
        var exitType = RoomExitType.Normal;
        var command = DirectionToCommand.GetValueOrDefault(direction, direction.ToLower());
        var rawModifier = "";
        var isPassableHidden = false;
        var doorStatReq = 0;
        MultiActionExitData? multiActionData = null;

        var modMatch = ModifierRegex.Match(trimmed);
        if (modMatch.Success)
        {
            rawModifier = modMatch.Groups[1].Value;
            var modLower = rawModifier.ToLower();

            if (modLower.StartsWith("text:"))
            {
                exitType = RoomExitType.Text;
                // Extract the first command from the comma-separated list
                var commandsPart = rawModifier.Substring(5).Trim(); // Skip "Text:"
                var firstCommand = commandsPart.Split(',')[0].Trim();
                if (!string.IsNullOrEmpty(firstCommand))
                    command = firstCommand;
            }
            else if (modLower.Contains("hidden") || modLower.Contains("needs") && modLower.Contains("action"))
            {
                // Hidden/Passable and Hidden/Passage exits are always traversable —
                // the player types the direction command and arrives normally,
                // the exit simply never appears in "Obvious exits:".
                if (modLower.Contains("passable") || modLower.Contains("passage"))
                {
                    exitType = RoomExitType.Hidden;
                    isPassableHidden = true;
                }
                // Multi-action hidden exits (e.g., "Needs 2 Actions, any order")
                // Phase 5e: classified as MultiActionHidden; traversability set in Pass 2
                else if (modLower.Contains("needs") && modLower.Contains("action"))
                {
                    exitType = RoomExitType.MultiActionHidden;

                    var needsMatch = NeedsActionsRegex.Match(rawModifier);
                    int actionCount = 1;
                    bool specificOrder = false;

                    if (needsMatch.Success)
                    {
                        int.TryParse(needsMatch.Groups[1].Value, out actionCount);
                        specificOrder = needsMatch.Groups[2].Value
                            .Contains("specific", StringComparison.OrdinalIgnoreCase);
                    }

                    // Cap absurd action counts (data anomaly: room 1/1810 has 1278)
                    if (actionCount > 20) actionCount = 1;

                    multiActionData = new MultiActionExitData
                    {
                        RequiredActionCount = actionCount,
                        RequiresSpecificOrder = specificOrder
                    };
                }
                // Hidden/Unknown — data anomaly, treat as simple 1-action hidden exit
                else if (modLower.Contains("unknown"))
                {
                    exitType = RoomExitType.MultiActionHidden;
                    multiActionData = new MultiActionExitData
                    {
                        RequiredActionCount = 1,
                        RequiresSpecificOrder = false
                    };
                }
                // Plain (Hidden) — searchable with "sea {direction}"
                else
                {
                    exitType = RoomExitType.SearchableHidden;
                }
            }
            else if (modLower.StartsWith("door"))
            {
                if (modLower.Contains("key"))
                    exitType = RoomExitType.Locked;  // Key-required — not traversable until inventory system exists
                else
                    exitType = RoomExitType.Door;     // Plain door or stat-gated (picklocks/strength) — bash/pick handles these
            }
            // Parse numeric stat requirement from door modifier (e.g., "Door [1000 picklocks/strength]")
            if (exitType == RoomExitType.Door && modLower.Contains("picklocks"))
            {
                var numMatch = System.Text.RegularExpressions.Regex.Match(rawModifier, @"\[(\d+)\s");
                if (numMatch.Success && int.TryParse(numMatch.Groups[1].Value, out int statReq))
                    doorStatReq = statReq;
            }
        }

        return new RoomExit
        {
            Direction = direction,
            Command = command,
            DestinationKey = destKey,
            ExitType = exitType,
            DoorStatRequirement = doorStatReq,
            MultiActionData = multiActionData,
            RawValue = trimmed,
            // Phase 5b+5c+5e: Normal, Text, Hidden/Passable, Door, and SearchableHidden exits are traversable.
            // MultiActionHidden traversability is determined in Pass 2b after action data is linked.
            Traversable = exitType == RoomExitType.Normal
                       || exitType == RoomExitType.Text
                       || exitType == RoomExitType.Door
                       || exitType == RoomExitType.SearchableHidden
                       || (exitType == RoomExitType.Hidden && isPassableHidden)
        };
    }

    #endregion

    #region JSON Helpers

    private static List<Dictionary<string, object?>>? ParseJsonArray(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != System.Text.Json.JsonValueKind.Array)
            return null;

        var result = new List<Dictionary<string, object?>>();

        foreach (var row in root.EnumerateArray())
        {
            var dict = new Dictionary<string, object?>();
            foreach (var prop in row.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => prop.Value.GetString(),
                    System.Text.Json.JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : (object)prop.Value.GetDecimal(),
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    System.Text.Json.JsonValueKind.Null => null,
                    _ => prop.Value.ToString()
                };
            }
            result.Add(dict);
        }

        return result;
    }

    private static int GetInt(Dictionary<string, object?> row, string key)
    {
        if (row.TryGetValue(key, out var val) && val != null)
        {
            if (val is long l) return (int)l;
            if (val is int i) return i;
            if (val is decimal d) return (int)d;
            if (int.TryParse(val.ToString(), out var parsed)) return parsed;
        }
        return 0;
    }

    private static string GetString(Dictionary<string, object?> row, string key)
    {
        if (row.TryGetValue(key, out var val) && val != null)
            return val.ToString()?.Trim() ?? "";
        return "";
    }

    #endregion
}
