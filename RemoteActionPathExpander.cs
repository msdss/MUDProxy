namespace MudProxyViewer;

/// <summary>
/// Expands remote-action exit steps in a path into a flat sequence of:
///   1. Walk steps to each prerequisite room
///   2. RemoteAction steps (send command, wait delay, no room change)
///   3. Walk steps back to the exit room
///   4. The exit traversal step (retyped as Normal)
///
/// This converts a single MultiActionHidden step that requires remote actions
/// into a linear sequence the walker can process without a nested state machine.
/// </summary>
public class RemoteActionPathExpander
{
    private readonly RoomGraphManager _roomGraph;
    private readonly Func<Func<RoomExit, bool>>? _getExitFilter;

    public RemoteActionPathExpander(RoomGraphManager roomGraph, Func<Func<RoomExit, bool>>? getExitFilter = null)
    {
        _roomGraph = roomGraph;
        _getExitFilter = getExitFilter;
    }

    /// <summary>
    /// Expand all remote-action exit steps in a path into linear prerequisite sequences.
    /// If the path contains no remote-action steps, returns the original path unchanged.
    /// </summary>
    public PathResult Expand(PathResult path)
    {
        if (!path.Success || path.Steps.Count == 0)
            return path;

        // Check if any steps need expansion
        bool hasRemoteActionSteps = false;
        foreach (var step in path.Steps)
        {
            if (step.ExitType == RoomExitType.MultiActionHidden
                && step.MultiActionData?.IsRemoteActionAutomatable == true)
            {
                hasRemoteActionSteps = true;
                break;
            }
        }

        if (!hasRemoteActionSteps)
            return path;

        var expandedSteps = new List<PathStep>();

        foreach (var step in path.Steps)
        {
            if (step.ExitType == RoomExitType.MultiActionHidden
                && step.MultiActionData?.IsRemoteActionAutomatable == true)
            {
                // Expand this remote-action step
                var prerequisiteSteps = ExpandSingle(step.MultiActionData, step.FromKey);
                if (prerequisiteSteps == null)
                {
                    // Expansion failed — return error
                    return new PathResult
                    {
                        StartKey = path.StartKey,
                        DestinationKey = path.DestinationKey,
                        Success = false,
                        ErrorMessage = $"Failed to expand remote-action prerequisites for exit at {step.FromKey} {step.Direction}"
                    };
                }

                expandedSteps.AddRange(prerequisiteSteps);

                // Add the exit traversal step, retyped as Normal (prerequisites are done)
                expandedSteps.Add(new PathStep
                {
                    Command = step.Command,
                    Direction = step.Direction,
                    FromKey = step.FromKey,
                    ToKey = step.ToKey,
                    ToName = step.ToName,
                    ExitType = RoomExitType.Normal,  // Actions are done, just traverse
                    OriginalMultiActionData = step.MultiActionData  // Preserve for retry
                });
            }
            else
            {
                // Non-remote-action step — pass through unchanged
                expandedSteps.Add(step);
            }
        }

        // Build the expanded result
        var result = new PathResult
        {
            StartKey = path.StartKey,
            DestinationKey = path.DestinationKey,
            Success = true,
            Steps = expandedSteps,
            TotalSteps = expandedSteps.Count,
            Requirements = path.Requirements
        };

        // Update requirements to reflect the expansion
        result.Requirements.HasRemoteActionExits = true;

        return result;
    }

    /// <summary>
    /// Expand a single remote-action exit's prerequisites into walk + action steps.
    /// Returns the prerequisite steps only (no exit traversal step).
    /// Used by both Expand() for initial path building and by AutoWalkManager for retry.
    /// </summary>
    /// <param name="data">The MultiActionExitData containing the action prerequisites.</param>
    /// <param name="exitRoomKey">The room key where the exit is located (walker returns here after prerequisites).</param>
    /// <returns>List of prerequisite PathSteps, or null if expansion fails (unreachable rooms).</returns>
    public List<PathStep>? ExpandSingle(MultiActionExitData data, string exitRoomKey)
    {
        var steps = new List<PathStep>();
        var exitFilter = _getExitFilter?.Invoke();

        // Separate local actions (same room) from remote actions (different rooms)
        var localActions = data.Actions.Where(a => a.ActionRoomKey == null).ToList();
        var remoteActions = data.Actions.Where(a => a.ActionRoomKey != null).ToList();

        // Group remote actions by ActionRoomKey (visit each prerequisite room once)
        var roomGroups = remoteActions
            .GroupBy(a => a.ActionRoomKey!)
            .Select(g => new
            {
                RoomKey = g.Key,
                Actions = g.OrderBy(a => a.StepNumber).ToList(),
                MinStep = g.Min(a => a.StepNumber)
            })
            .ToList();

        // Order room visits
        List<string> visitOrder;
        if (data.RequiresSpecificOrder)
        {
            // Specific order: sort groups by their minimum step number
            visitOrder = roomGroups
                .OrderBy(g => g.MinStep)
                .Select(g => g.RoomKey)
                .ToList();
        }
        else
        {
            // Any order: nearest-neighbor greedy to minimize total travel
            visitOrder = NearestNeighborOrder(exitRoomKey, roomGroups.Select(g => g.RoomKey).ToList());
        }

        // Build a lookup for quick access to actions by room key
        var actionsByRoom = roomGroups.ToDictionary(g => g.RoomKey, g => g.Actions);

        // Visit each prerequisite room
        string currentPosition = exitRoomKey;
        foreach (var prereqRoomKey in visitOrder)
        {
            // Walk from current position to prerequisite room
            if (currentPosition != prereqRoomKey)
            {
                var walkPath = _roomGraph.FindPath(currentPosition, prereqRoomKey, exitFilter);
                if (!walkPath.Success)
                    return null;  // Can't reach prerequisite room

                steps.AddRange(walkPath.Steps);
                currentPosition = prereqRoomKey;
            }

            // Insert RemoteAction steps for all actions in this room
            foreach (var action in actionsByRoom[prereqRoomKey])
            {
                steps.Add(new PathStep
                {
                    Command = action.Commands[0],  // Use first alternative command
                    Direction = "",
                    FromKey = prereqRoomKey,
                    ToKey = prereqRoomKey,  // No room change
                    ToName = "",
                    ExitType = RoomExitType.RemoteAction
                });
            }
        }

        // Walk back to exit room from last prerequisite room
        if (currentPosition != exitRoomKey)
        {
            var returnPath = _roomGraph.FindPath(currentPosition, exitRoomKey, exitFilter);
            if (!returnPath.Success)
                return null;  // Can't return to exit room

            steps.AddRange(returnPath.Steps);
        }

        // Insert local actions last (right before exit traversal) to minimize reset risk
        foreach (var action in localActions.OrderBy(a => a.StepNumber))
        {
            steps.Add(new PathStep
            {
                Command = action.Commands[0],
                Direction = "",
                FromKey = exitRoomKey,
                ToKey = exitRoomKey,
                ToName = "",
                ExitType = RoomExitType.RemoteAction
            });
        }

        return steps;
    }

    /// <summary>
    /// Nearest-neighbor greedy ordering to minimize total travel distance between prerequisite rooms.
    /// Starts from the exit room and repeatedly picks the closest unvisited room.
    /// O(n²) with max n≈7 — trivial cost.
    /// </summary>
    private List<string> NearestNeighborOrder(string startRoomKey, List<string> roomKeys)
    {
        if (roomKeys.Count <= 1)
            return roomKeys.ToList();

        var remaining = new HashSet<string>(roomKeys);
        var order = new List<string>();
        var current = startRoomKey;

        while (remaining.Count > 0)
        {
            string? nearest = null;
            int shortestDistance = int.MaxValue;

            foreach (var roomKey in remaining)
            {
                var path = _roomGraph.FindPath(current, roomKey);
                if (path.Success && path.TotalSteps < shortestDistance)
                {
                    shortestDistance = path.TotalSteps;
                    nearest = roomKey;
                }
            }

            if (nearest == null)
            {
                // Fallback: add remaining rooms in original order if unreachable
                foreach (var roomKey in roomKeys)
                {
                    if (remaining.Contains(roomKey))
                        order.Add(roomKey);
                }
                break;
            }

            order.Add(nearest);
            remaining.Remove(nearest);
            current = nearest;
        }

        return order;
    }
}
