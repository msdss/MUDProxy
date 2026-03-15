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

    // Class/Race name ↔ ID lookup tables (built from Classes.json / Races.json)
    private Dictionary<string, int> _classNameToId = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, string> _classIdToName = new();
    private Dictionary<string, int> _raceNameToId = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, string> _raceIdToName = new();
    private Dictionary<int, string> _itemIdToName = new();

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

    // Regex to parse level restriction: "Level: MIN to MAX" where 0 means no limit
    private static readonly Regex LevelModifierRegex = new(
        @"Level:\s*(\d+)\s+to\s+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Regex to parse class/race restriction entries: "ID OK" or "ID NO"
    private static readonly Regex RestrictionEntryRegex = new(
        @"(\d+)\s+(OK|NO)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Regex to extract key item ID from "Key: NNN" patterns
    private static readonly Regex KeyItemIdRegex = new(
        @"Key:\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

    /// <summary>Look up a class ID by name. Returns 0 if not found.</summary>
    public int GetClassId(string className) =>
        !string.IsNullOrEmpty(className) && _classNameToId.TryGetValue(className, out var id) ? id : 0;

    /// <summary>Look up a race ID by name. Returns 0 if not found.</summary>
    public int GetRaceId(string raceName) =>
        !string.IsNullOrEmpty(raceName) && _raceNameToId.TryGetValue(raceName, out var id) ? id : 0;

    /// <summary>Look up a class name by ID. Returns empty string if not found.</summary>
    public string GetClassName(int classId) =>
        _classIdToName.TryGetValue(classId, out var name) ? name : "";

    /// <summary>Look up a race name by ID. Returns empty string if not found.</summary>
    public string GetRaceName(int raceId) =>
        _raceIdToName.TryGetValue(raceId, out var name) ? name : "";

    /// <summary>Look up an item name by ID. Returns empty string if not found.</summary>
    public string GetItemName(int itemId) =>
        _itemIdToName.TryGetValue(itemId, out var name) ? name : "";

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
            BuildClassRaceLookups();
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

    /// <summary>
    /// Build class and race name↔ID lookup tables from Classes.json and Races.json.
    /// Called during LoadFromGameData after the room graph is built.
    /// </summary>
    private void BuildClassRaceLookups()
    {
        _classNameToId.Clear();
        _classIdToName.Clear();
        _raceNameToId.Clear();
        _raceIdToName.Clear();

        var classes = GameDataCache.Instance.GetTable("Classes");
        if (classes != null)
        {
            foreach (var row in classes)
            {
                var id = GetInt(row, "Number");
                var name = GetString(row, "Name");
                if (id > 0 && !string.IsNullOrEmpty(name))
                {
                    _classNameToId[name] = id;
                    _classIdToName[id] = name;
                }
            }
        }

        var races = GameDataCache.Instance.GetTable("Races");
        if (races != null)
        {
            foreach (var row in races)
            {
                var id = GetInt(row, "Number");
                var name = GetString(row, "Name");
                if (id > 0 && !string.IsNullOrEmpty(name))
                {
                    _raceNameToId[name] = id;
                    _raceIdToName[id] = name;
                }
            }
        }

        _itemIdToName.Clear();
        var items = GameDataCache.Instance.GetTable("Items");
        if (items == null)
        {
            // Items may not be cached yet (async preload) — load synchronously
            var itemsPath = Path.Combine(_gameDataPath, "Items.json");
            if (File.Exists(itemsPath))
            {
                var json = File.ReadAllText(itemsPath);
                items = ParseJsonArray(json);
            }
        }
        if (items != null)
        {
            foreach (var row in items)
            {
                var id = GetInt(row, "Number");
                var name = GetString(row, "Name");
                if (id > 0 && !string.IsNullOrEmpty(name))
                    _itemIdToName[id] = name;
            }
        }

        OnLogMessage?.Invoke($"📋 Loaded {_classNameToId.Count} classes, {_raceNameToId.Count} races, {_itemIdToName.Count} items for lookups");
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

    #region Overrides

    /// <summary>
    /// Loads and applies user-defined overrides from RoomOverrides.json in the Game Data folder.
    /// Called after Pass 2b cleanup to correct data export errors or supply missing entries.
    /// Returns the number of overrides successfully applied.
    /// </summary>
    private int ApplyOverrides()
    {
        var filePath = Path.Combine(_gameDataPath, "RoomOverrides.json");
        if (!File.Exists(filePath))
            return 0;

        List<RoomExitOverride>? overrides;
        try
        {
            var json = File.ReadAllText(filePath);
            overrides = System.Text.Json.JsonSerializer.Deserialize<List<RoomExitOverride>>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"⚠️ Failed to load RoomOverrides.json: {ex.Message}");
            return 0;
        }

        if (overrides == null || overrides.Count == 0)
            return 0;

        int applied = 0;
        int skipped = 0;

        foreach (var ov in overrides)
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(ov.RoomKey) || string.IsNullOrWhiteSpace(ov.ExitDirection))
            {
                OnLogMessage?.Invoke($"⚠️ Override skipped: missing roomKey or exitDirection");
                skipped++;
                continue;
            }

            // Find the target room and exit
            if (!_rooms.TryGetValue(ov.RoomKey, out var room))
            {
                OnLogMessage?.Invoke($"⚠️ Override skipped: room {ov.RoomKey} not found");
                skipped++;
                continue;
            }

            var exit = room.Exits.FirstOrDefault(e =>
                e.Direction.Equals(ov.ExitDirection, StringComparison.OrdinalIgnoreCase));

            if (exit == null)
            {
                OnLogMessage?.Invoke($"⚠️ Override skipped: exit {ov.ExitDirection} not found in room {ov.RoomKey}");
                skipped++;
                continue;
            }

            switch (ov.Override?.ToLowerInvariant())
            {
                case "replaceactions":
                    if (!ApplyReplaceActions(ov, exit))
                    {
                        skipped++;
                        continue;
                    }
                    break;

                default:
                    OnLogMessage?.Invoke(
                        $"⚠️ Override skipped: unknown operation \"{ov.Override}\" for {ov.RoomKey} {ov.ExitDirection}");
                    skipped++;
                    continue;
            }

            applied++;
        }

        if (applied > 0 || skipped > 0)
            OnLogMessage?.Invoke(
                $"📋 Room overrides: {applied} applied, {skipped} skipped (from {overrides.Count} entries)");

        return applied;
    }

    /// <summary>
    /// Applies a "replaceActions" override — completely replaces the action data for an exit.
    /// </summary>
    private bool ApplyReplaceActions(RoomExitOverride ov, RoomExit exit)
    {
        if (ov.Actions == null || ov.Actions.Count == 0)
        {
            OnLogMessage?.Invoke(
                $"⚠️ Override skipped: replaceActions for {ov.RoomKey} {ov.ExitDirection} has no actions");
            return false;
        }

        // Ensure the exit has MultiActionData (create if needed for overrides on exits
        // that were previously missing action data entirely)
        if (exit.MultiActionData == null)
        {
            exit.MultiActionData = new MultiActionExitData();
            exit.ExitType = RoomExitType.MultiActionHidden;
        }

        var data = exit.MultiActionData;

        // Replace action list
        data.Actions.Clear();
        foreach (var ovAction in ov.Actions)
        {
            data.Actions.Add(new ExitAction
            {
                StepNumber = ovAction.Step,
                Commands = ovAction.Commands.ToList(),
                ActionRoomKey = ovAction.ActionRoom
            });
        }

        // Update metadata
        if (ov.RequiredActionCount.HasValue)
            data.RequiredActionCount = ov.RequiredActionCount.Value;

        if (ov.RequiresSpecificOrder.HasValue)
            data.RequiresSpecificOrder = ov.RequiresSpecificOrder.Value;

        // Recalculate flags
        data.HasRemoteActions = data.Actions.Any(a => a.ActionRoomKey != null);
        data.HasItemRequirements = data.Actions.Any(a => a.RequiresItem);

        // Sort by step number
        data.Actions.Sort((a, b) => a.StepNumber.CompareTo(b.StepNumber));

        // Recalculate traversability
        bool wasTraversable = exit.Traversable;
        data.IsRemoteActionAutomatable = false;  // Reset before re-evaluation

        if (data.IsAutomatable)
        {
            exit.Traversable = true;
        }
        else if (data.HasRemoteActions
                 && data.Actions.Count > 0
                 && data.Actions.All(a => a.Commands.Count > 0))
        {
            // Check if all prerequisite rooms are reachable
            // (Item requirements are checked at runtime via GetExitFilter — Phase 6 inventory tracking)
            var remoteActions = data.Actions.Where(a => a.ActionRoomKey != null).ToList();
            bool allReachable = remoteActions.All(a =>
                _rooms.ContainsKey(a.ActionRoomKey!) && FindPath(ov.RoomKey, a.ActionRoomKey!).Success);

            if (allReachable)
            {
                data.IsRemoteActionAutomatable = true;
                exit.Traversable = true;
            }
            else
            {
                exit.Traversable = false;
            }
        }
        else
        {
            exit.Traversable = false;
        }

        string status = exit.Traversable ? "traversable" : "deferred";
        OnLogMessage?.Invoke(
            $"  ✏️ [{ov.RoomKey}] {ov.ExitDirection}: replaced {data.Actions.Count} actions ({status})" +
            (ov.Comment != null ? $" — {ov.Comment}" : ""));

        return true;
    }

    #endregion

    #region Diagnostics

    /// <summary>
    /// Enumerates all multi-action exits with remote actions and logs a detailed report.
    /// Checks whether prerequisite rooms are reachable via existing traversable paths.
    /// Call after LoadFromGameData() to analyze remote action scope.
    /// </summary>
    public void LogRemoteActionDiagnostics()
    {
        var remoteExits = new List<(string ExitRoomKey, string ExitRoomName, string Direction,
            string DestKey, MultiActionExitData Data)>();

        foreach (var (key, room) in _rooms)
        {
            foreach (var exit in room.Exits)
            {
                if (exit.ExitType == RoomExitType.MultiActionHidden
                    && exit.MultiActionData is { HasRemoteActions: true })
                {
                    remoteExits.Add((key, room.Name, exit.Direction, exit.DestinationKey, exit.MultiActionData));
                }
            }
        }

        if (remoteExits.Count == 0)
        {
            OnLogMessage?.Invoke("📊 Remote action diagnostics: No exits with remote actions found.");
            return;
        }

        OnLogMessage?.Invoke($"📊 Remote action diagnostics: {remoteExits.Count} exits with remote actions");
        OnLogMessage?.Invoke("────────────────────────────────────────");

        int totalRemoteActions = 0;
        int totalLocalActions = 0;
        int fullyReachable = 0;
        int partiallyReachable = 0;
        int unreachable = 0;
        var allPrereqRooms = new HashSet<string>();

        foreach (var (exitRoomKey, exitRoomName, direction, destKey, data) in remoteExits)
        {
            var remoteActions = data.Actions.Where(a => a.ActionRoomKey != null).ToList();
            var localActions = data.Actions.Where(a => a.ActionRoomKey == null).ToList();
            totalRemoteActions += remoteActions.Count;
            totalLocalActions += localActions.Count;

            var prereqRooms = remoteActions
                .Select(a => a.ActionRoomKey!)
                .Distinct()
                .ToList();

            foreach (var r in prereqRooms)
                allPrereqRooms.Add(r);

            // Check reachability of each prerequisite room from the exit room
            var reachResults = new List<(string roomKey, bool reachable, int distance)>();
            foreach (var prereqKey in prereqRooms)
            {
                var path = FindPath(exitRoomKey, prereqKey);
                reachResults.Add((prereqKey, path.Success, path.Success ? path.Steps.Count : -1));
            }

            bool allReachable = reachResults.All(r => r.reachable);
            bool noneReachable = reachResults.All(r => !r.reachable);

            if (allReachable) fullyReachable++;
            else if (noneReachable) unreachable++;
            else partiallyReachable++;

            // Log this exit
            string orderStr = data.RequiresSpecificOrder ? "specific order" : "any order";
            OnLogMessage?.Invoke(
                $"  [{exitRoomKey}] \"{exitRoomName}\" {direction} → {destKey} " +
                $"| {data.Actions.Count} actions ({remoteActions.Count} remote, {localActions.Count} local) " +
                $"| {orderStr}" +
                (data.HasItemRequirements ? " | HAS ITEM REQUIREMENTS" : ""));

            foreach (var action in data.Actions)
            {
                string locationStr = action.ActionRoomKey != null
                    ? $"REMOTE room {action.ActionRoomKey}"
                    : "same room";
                string cmdsStr = string.Join(" / ", action.Commands);
                string reachStr = "";

                if (action.ActionRoomKey != null)
                {
                    var reach = reachResults.FirstOrDefault(r => r.roomKey == action.ActionRoomKey);
                    reachStr = reach.reachable
                        ? $" ✓ reachable ({reach.distance} steps)"
                        : " ✗ NOT reachable";
                }

                OnLogMessage?.Invoke(
                    $"    Step {action.StepNumber}: [{locationStr}] {cmdsStr}" +
                    (action.RequiresItem ? $" (Item: {action.RequiredItemId})" : "") +
                    reachStr);
            }
        }

        OnLogMessage?.Invoke("────────────────────────────────────────");
        OnLogMessage?.Invoke(
            $"📊 Summary: {remoteExits.Count} exits, " +
            $"{totalRemoteActions} remote actions, {totalLocalActions} local actions, " +
            $"{allPrereqRooms.Count} unique prerequisite rooms");
        OnLogMessage?.Invoke(
            $"   Reachability: {fullyReachable} fully reachable, " +
            $"{partiallyReachable} partially reachable, {unreachable} unreachable");
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
    public PathResult FindPath(string fromKey, string toKey, Func<RoomExit, bool>? exitFilter = null, bool includeAllExits = false)
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
                if (!includeAllExits && !exit.Traversable)
                    continue;

                // Caller-provided filter (e.g., skip stat-gated doors player can't handle)
                if (!includeAllExits && exitFilter != null && !exitFilter(exit))
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
                    // Aggregate path requirements
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
                        {
                            reqs.HasMultiActionExits = true;
                            if (step.MultiActionData?.IsRemoteActionAutomatable == true)
                                reqs.HasRemoteActionExits = true;
                        }
                        if (step.ExitType == RoomExitType.Teleport)
                            reqs.HasTeleportExits = true;
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
                MultiActionData = exitUsed.MultiActionData,
                LevelRestriction = exitUsed.LevelRestriction,
                ClassRestriction = exitUsed.ClassRestriction,
                RaceRestriction = exitUsed.RaceRestriction,
                TeleportConditions = exitUsed.TeleportConditions
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
        int levelRestrictions = 0;
        int classRestrictions = 0;
        int raceRestrictions = 0;

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
                        if (exit.LevelRestriction != null)
                            levelRestrictions++;
                        if (exit.ClassRestriction != null)
                            classRestrictions++;
                        if (exit.RaceRestriction != null)
                            raceRestrictions++;
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
                RequiredItemId = itemId,
                ActionRoomKey = isRemote ? roomKey : null
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

        // ── Pass 2b: Clean up action data and finalize traversability ──
        int multiActionTraversable = 0;
        int remoteActionTraversable = 0;
        int multiActionDeferred = 0;
        int actionsDeduplicated = 0;
        int deadRoomActionsRemoved = 0;
        int exitsRenumbered = 0;

        foreach (var room in _rooms.Values)
        {
            foreach (var exit in room.Exits)
            {
                if (exit.ExitType != RoomExitType.MultiActionHidden || exit.MultiActionData == null)
                    continue;

                var data = exit.MultiActionData;

                // ── Fix 1: Deduplicate identical action entries ──
                // Same StepNumber + same command set + same ActionRoomKey = data export duplicate
                // (e.g., 12/2231 has Action#2 for push onyx in both E and D columns)
                int beforeDedup = data.Actions.Count;
                data.Actions = data.Actions
                    .GroupBy(a => (
                        a.StepNumber,
                        Cmds: string.Join("|", a.Commands.OrderBy(c => c)),
                        Room: a.ActionRoomKey ?? ""))
                    .Select(g => g.First())
                    .ToList();
                actionsDeduplicated += beforeDedup - data.Actions.Count;

                // ── Fix 2: Filter unreachable dead-room remote actions ──
                // If removing unreachable remote actions still leaves enough actions
                // to satisfy RequiredActionCount, drop them (they're from dead rooms —
                // e.g., room 10/261 has an action targeting 10/42 but is inaccessible)
                var unreachableRemotes = data.Actions
                    .Where(a => a.ActionRoomKey != null)
                    .Where(a => !_rooms.ContainsKey(a.ActionRoomKey!)
                                || !FindPath(room.Key, a.ActionRoomKey!).Success)
                    .ToList();

                if (unreachableRemotes.Count > 0
                    && data.Actions.Count - unreachableRemotes.Count >= data.RequiredActionCount)
                {
                    foreach (var dead in unreachableRemotes)
                        data.Actions.Remove(dead);
                    deadRoomActionsRemoved += unreachableRemotes.Count;
                }

                // Recheck HasRemoteActions after filtering
                data.HasRemoteActions = data.Actions.Any(a => a.ActionRoomKey != null);

                // ── Fix 3: Renumber colliding step numbers for "any order" exits ──
                // Unnumbered actions all default to StepNumber 1, but when multiple
                // target the same exit from different rooms they're separate requirements
                // (e.g., 12/2173 and 12/2174 both have unnumbered actions for 12/2169)
                if (!data.RequiresSpecificOrder
                    && data.Actions.GroupBy(a => a.StepNumber).Any(g => g.Count() > 1))
                {
                    for (int i = 0; i < data.Actions.Count; i++)
                        data.Actions[i].StepNumber = i + 1;
                    exitsRenumbered++;
                }

                // Sort actions by step number for correct execution order
                data.Actions.Sort((a, b) => a.StepNumber.CompareTo(b.StepNumber));

                // Make the exit traversable if it can be automated
                if (data.IsAutomatable)
                {
                    exit.Traversable = true;
                    skippedExits--;  // Was counted as non-traversable in Pass 1
                    multiActionTraversable++;
                }
                else if (data.HasRemoteActions
                         && data.Actions.Count > 0
                         && data.Actions.All(a => a.Commands.Count > 0))
                {
                    // Remote-action exit: check if ALL prerequisite rooms are reachable
                    // (Item requirements are checked at runtime via GetExitFilter — Phase 6 inventory tracking)
                    var remoteActions = data.Actions.Where(a => a.ActionRoomKey != null).ToList();
                    bool allReachable = remoteActions.All(a => FindPath(room.Key, a.ActionRoomKey!).Success);

                    if (allReachable)
                    {
                        data.IsRemoteActionAutomatable = true;
                        exit.Traversable = true;
                        skippedExits--;
                        remoteActionTraversable++;
                    }
                    else
                    {
                        multiActionDeferred++;
                    }
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
            $"{remoteActionTraversable} remote-action traversable, " +
            $"{multiActionDeferred} deferred ({actionsRemote} remote, {actionsItemGated} item-gated)");

        if (levelRestrictions > 0 || classRestrictions > 0 || raceRestrictions > 0)
            OnLogMessage?.Invoke(
                $"🚧 Exit restrictions: {levelRestrictions} level, {classRestrictions} class, {raceRestrictions} race");

        if (actionsDeduplicated > 0 || deadRoomActionsRemoved > 0 || exitsRenumbered > 0)
            OnLogMessage?.Invoke(
                $"🧹 Action cleanup: {actionsDeduplicated} duplicates removed, " +
                $"{deadRoomActionsRemoved} dead-room actions removed, " +
                $"{exitsRenumbered} exits renumbered");

        // ── Pass 3: Apply user-defined overrides from RoomOverrides.json ──
        ApplyOverrides();

        // ── Pass 4: Parse TextBlock teleport commands into virtual exits ──
        BuildTeleportExits(roomRows);

        // Log detailed remote action diagnostics if any exist
        if (actionsRemote > 0)
            LogRemoteActionDiagnostics();
    }

    /// <summary>
    /// Pass 4: Parse CMD TextBlock teleport commands and add virtual Teleport exits to rooms.
    /// </summary>
    private void BuildTeleportExits(List<Dictionary<string, object?>> roomRows)
    {
        // Load TextBlocks table
        var textBlocks = GameDataCache.Instance.GetTable("TextBlocks");
        if (textBlocks == null)
        {
            var tbPath = Path.Combine(_gameDataPath, "TextBlocks.json");
            if (File.Exists(tbPath))
                textBlocks = ParseJsonArray(File.ReadAllText(tbPath));
        }
        if (textBlocks == null || textBlocks.Count == 0)
            return;

        // Build lookup by Number
        var tbLookup = new Dictionary<int, (int LinkTo, string Action)>();
        foreach (var row in textBlocks)
        {
            var num = GetInt(row, "Number");
            var linkTo = GetInt(row, "LinkTo");
            var action = GetString(row, "Action");
            if (num > 0)
                tbLookup[num] = (linkTo, action);
        }

        var teleportRegex = new Regex(@"teleport\s+(\d+)\s+(\d+)", RegexOptions.IgnoreCase);
        int teleportTraversable = 0;
        int teleportNonTraversable = 0;
        int roomsWithTeleports = 0;

        foreach (var roomRow in roomRows)
        {
            var cmd = GetInt(roomRow, "CMD");
            if (cmd <= 0 || !tbLookup.ContainsKey(cmd))
                continue;

            var mapNum = GetInt(roomRow, "Map Number");
            var roomNum = GetInt(roomRow, "Room Number");
            var roomKey = $"{mapNum}/{roomNum}";
            if (!_rooms.ContainsKey(roomKey))
                continue;

            // Follow LinkTo chain to collect all action lines
            var allLines = new List<string>();
            var visited = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(cmd);
            while (queue.Count > 0)
            {
                var tbId = queue.Dequeue();
                if (!visited.Add(tbId) || !tbLookup.ContainsKey(tbId))
                    continue;
                var (linkTo, action) = tbLookup[tbId];
                if (!string.IsNullOrEmpty(action))
                {
                    foreach (var line in action.Split('\n'))
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed) && trimmed != "\0")
                            allLines.Add(trimmed);
                    }
                }
                if (linkTo > 0)
                    queue.Enqueue(linkTo);
            }

            // Parse teleport lines, deduplicate by destination
            var teleportsByDest = new Dictionary<string, (string command, TeleportConditions conditions, string rawLine)>();
            foreach (var line in allLines)
            {
                var teleMatch = teleportRegex.Match(line);
                if (!teleMatch.Success)
                    continue;

                var destRoom = int.Parse(teleMatch.Groups[1].Value);
                var destMap = int.Parse(teleMatch.Groups[2].Value);
                var destKey = $"{destMap}/{destRoom}";

                // Skip self-referencing teleports
                if (destKey == roomKey)
                    continue;

                // Already have a shorter command for this destination
                if (teleportsByDest.ContainsKey(destKey))
                    continue;

                // Extract user command (text before first colon)
                var parts = line.Split(':');
                var userCommand = parts[0].Trim();

                // Skip lines where the "command" is a system keyword (no user-typeable command)
                var cmdLower = userCommand.ToLower();
                if (cmdLower.StartsWith("teleport") || cmdLower.StartsWith("message")
                    || cmdLower.StartsWith("text") || cmdLower.StartsWith("cast"))
                    continue;

                // Parse conditions from the parts between command and teleport
                var conditions = ParseTeleportConditions(parts, 1);

                teleportsByDest[destKey] = (userCommand, conditions, line);
            }

            if (teleportsByDest.Count == 0)
                continue;

            roomsWithTeleports++;
            var room = _rooms[roomKey];

            foreach (var (destKey, (userCommand, conditions, rawLine)) in teleportsByDest)
            {
                // Promote level conditions to standard LevelRestriction for exit filter
                ExitLevelRestriction? levelRestriction = null;
                if (conditions.MinLevel > 0 || conditions.MaxLevel > 0)
                    levelRestriction = new ExitLevelRestriction { MinLevel = conditions.MinLevel, MaxLevel = conditions.MaxLevel };

                bool destExists = _rooms.ContainsKey(destKey);
                bool traversable = destExists && conditions.IsGraphTimeFilterable;

                var exit = new RoomExit
                {
                    Direction = "CMD",
                    Command = userCommand,
                    DestinationKey = destKey,
                    ExitType = RoomExitType.Teleport,
                    RawValue = $"TextBlock #{cmd}: {rawLine}",
                    TextBlockNumber = cmd,
                    TeleportConditions = conditions,
                    LevelRestriction = levelRestriction,
                    Traversable = traversable
                };

                room.Exits.Add(exit);

                if (traversable)
                    teleportTraversable++;
                else
                    teleportNonTraversable++;
            }
        }

        if (teleportTraversable > 0 || teleportNonTraversable > 0)
            OnLogMessage?.Invoke(
                $"🌀 Teleport exits: {teleportTraversable} traversable, {teleportNonTraversable} non-traversable " +
                $"(from {roomsWithTeleports} rooms with CMD TextBlocks)");
    }

    /// <summary>
    /// Parse conditions from colon-separated TextBlock action parts.
    /// </summary>
    private static TeleportConditions ParseTeleportConditions(string[] parts, int startIndex)
    {
        var cond = new TeleportConditions();
        for (int i = startIndex; i < parts.Length; i++)
        {
            var part = parts[i].Trim().ToLower();
            if (part.StartsWith("minlevel"))
            {
                var m = Regex.Match(parts[i], @"minlevel\s+(\d+)", RegexOptions.IgnoreCase);
                if (m.Success) cond.MinLevel = int.Parse(m.Groups[1].Value);
            }
            else if (part.StartsWith("maxlevel"))
            {
                var m = Regex.Match(parts[i], @"maxlevel\s+(\d+)", RegexOptions.IgnoreCase);
                if (m.Success) cond.MaxLevel = int.Parse(m.Groups[1].Value);
            }
            else if (part.StartsWith("roomitem"))
            {
                var m = Regex.Match(parts[i], @"roomitem\s+(\d+)", RegexOptions.IgnoreCase);
                if (m.Success) cond.RoomItemId = int.Parse(m.Groups[1].Value);
            }
            else if (part.StartsWith("checkitem"))
            {
                var m = Regex.Match(parts[i], @"checkitem\s+(\d+)", RegexOptions.IgnoreCase);
                if (m.Success) cond.CheckItemId = int.Parse(m.Groups[1].Value);
            }
            else if (part.StartsWith("nomonsters")) cond.RequiresNoMonsters = true;
            else if (part.StartsWith("needmonster")) cond.RequiresMonster = true;
            else if (part.StartsWith("testskill"))
            {
                var m = Regex.Match(parts[i], @"testskill\s+(\w+)", RegexOptions.IgnoreCase);
                if (m.Success) cond.TestSkill = m.Groups[1].Value;
            }
            else if (part.StartsWith("checkability") || part.StartsWith("testability"))
                cond.RequiresAbilityCheck = true;
            else if (part.StartsWith("evilaligned")) cond.RequiresEvil = true;
            else if (part.StartsWith("goodaligned")) cond.RequiresGood = true;
            // Ignore: message, text, cast, teleport, giveability, takeitem, giveitem, summon, adddelay, price, etc.
        }
        return cond;
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
        int keyItemId = 0;
        ExitLevelRestriction? levelRestriction = null;
        ExitClassRestriction? classRestriction = null;
        ExitRaceRestriction? raceRestriction = null;

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
            else if (modLower.StartsWith("key"))
            {
                // Extract key item ID
                var keyMatch = KeyItemIdRegex.Match(rawModifier);
                if (keyMatch.Success && int.TryParse(keyMatch.Groups[1].Value, out int kid))
                    keyItemId = kid;

                if (modLower.Contains("picklocks") || modLower.Contains("strength"))
                {
                    // Key with picklocks/strength alternative (e.g., "Key: 1416 [or 101 picklocks/strength]")
                    // Treat as Door — traversable if player has sufficient stats
                    exitType = RoomExitType.Door;
                }
                else
                {
                    exitType = RoomExitType.Locked;  // Standalone key, no alternative — not traversable
                }
            }
            else if (modLower.StartsWith("door"))
            {
                if (modLower.Contains("key"))
                {
                    exitType = RoomExitType.Locked;  // Key-required door (e.g., "Door [Key: 177]") — not traversable
                    var keyMatch = KeyItemIdRegex.Match(rawModifier);
                    if (keyMatch.Success && int.TryParse(keyMatch.Groups[1].Value, out int kid))
                        keyItemId = kid;
                }
                else
                    exitType = RoomExitType.Door;     // Plain door or stat-gated (picklocks/strength) — bash/pick handles these
            }
            else if (modLower.StartsWith("level"))
            {
                var levelMatch = LevelModifierRegex.Match(rawModifier);
                if (levelMatch.Success)
                {
                    int min = int.Parse(levelMatch.Groups[1].Value);
                    int max = int.Parse(levelMatch.Groups[2].Value);
                    // Only store restriction if it actually constrains (not 0-to-0 = no restriction)
                    if (min > 0 || max > 0)
                        levelRestriction = new ExitLevelRestriction { MinLevel = min, MaxLevel = max };
                }
                // ExitType stays Normal, Traversable stays true — exitFilter handles the dynamic check
            }
            else if (modLower.StartsWith("class"))
            {
                var entries = RestrictionEntryRegex.Matches(rawModifier);
                var restriction = new ExitClassRestriction();
                foreach (Match entry in entries)
                {
                    int id = int.Parse(entry.Groups[1].Value);
                    string okNo = entry.Groups[2].Value.ToUpper();
                    if (id == 0) continue;  // 0 = placeholder, no restriction
                    if (okNo == "OK") restriction.AllowedClassIds.Add(id);
                    else restriction.DeniedClassIds.Add(id);
                }
                if (restriction.AllowedClassIds.Count > 0 || restriction.DeniedClassIds.Count > 0)
                    classRestriction = restriction;
            }
            else if (modLower.StartsWith("race"))
            {
                var entries = RestrictionEntryRegex.Matches(rawModifier);
                var restriction = new ExitRaceRestriction();
                foreach (Match entry in entries)
                {
                    int id = int.Parse(entry.Groups[1].Value);
                    string okNo = entry.Groups[2].Value.ToUpper();
                    if (id == 0) continue;
                    if (okNo == "OK") restriction.AllowedRaceIds.Add(id);
                    else restriction.DeniedRaceIds.Add(id);
                }
                if (restriction.AllowedRaceIds.Count > 0 || restriction.DeniedRaceIds.Count > 0)
                    raceRestriction = restriction;
            }
            // Remaining modifier types — classified but left traversable for now
            else if (modLower.StartsWith("trap") || modLower.StartsWith("spell trap"))
            {
                // Traps: player takes damage but can pass through. Future: trap disarm support.
            }
            else if (modLower.StartsWith("toll"))
            {
                // Tolls: costs gold but passable. Future: inventory/gold management.
            }
            else if (modLower.StartsWith("cast"))
            {
                // Cast requirements: spell cast pre/post entry. Leave traversable for now.
            }
            else if (modLower.StartsWith("item") || modLower.StartsWith("ticket"))
            {
                // Item/ticket requirements: needs specific inventory item — not traversable until inventory tracking.
                exitType = RoomExitType.Locked;
                var itemMatch = System.Text.RegularExpressions.Regex.Match(rawModifier, @"(?:Item|Ticket):\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (itemMatch.Success && int.TryParse(itemMatch.Groups[1].Value, out int itemId))
                    keyItemId = itemId;
            }
            else if (modLower.StartsWith("alignment"))
            {
                // Alignment restrictions: leave traversable for now (small count, rare in practice).
            }
            else if (modLower.StartsWith("ability"))
            {
                // Ability restrictions: leave traversable for now (only ~5 exits).
            }
            else if (modLower.StartsWith("timed"))
            {
                // Timed exits: leave traversable for now (only 1 exit).
            }
            // Parse numeric stat requirement from door/key modifier
            // Matches: "Door [1000 picklocks/strength]" or "Key: 1416 [or 101 picklocks/strength]"
            if (exitType == RoomExitType.Door && modLower.Contains("picklocks"))
            {
                var numMatch = System.Text.RegularExpressions.Regex.Match(rawModifier, @"(?:\[|or\s+)(\d+)\s+picklocks");
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
            KeyItemId = keyItemId,
            MultiActionData = multiActionData,
            LevelRestriction = levelRestriction,
            ClassRestriction = classRestriction,
            RaceRestriction = raceRestriction,
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
