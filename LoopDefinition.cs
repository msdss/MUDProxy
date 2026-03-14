using System.Text.Json;
using System.Text.Json.Serialization;

namespace MudProxyViewer;

/// <summary>
/// Defines a repeating room-to-room circuit for automated experience grinding.
/// Each step is an explicit room key. The system validates direct exits between
/// consecutive pairs and expands them into movement commands automatically.
/// 
/// The last step connects back to the first step (implicit loop).
/// 
/// Saved as standalone .loop.json files in the Loops directory, shareable between players.
/// </summary>
public class LoopDefinition
{
    /// <summary>File format version for forward compatibility.</summary>
    public string Version { get; set; } = "1.0";

    /// <summary>Display name for the loop (e.g., "Dreary Village Circuit").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Ordered list of room-key steps defining the circuit.
    /// Each consecutive pair must have a direct exit between them.
    /// The last step connects back to the first (implicit loop).
    /// </summary>
    public List<LoopStep> Steps { get; set; } = new();

    /// <summary>Character name that created this loop.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Date the loop was created.</summary>
    public DateTime CreatedDate { get; set; } = DateTime.Now;

    /// <summary>Optional notes about the loop (level range, exp rate, tips).</summary>
    public string Notes { get; set; } = string.Empty;

    // ── File I/O ──

    public const string FileExtension = ".loop.json";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    /// <summary>
    /// Get the default Loops directory (next to the application executable).
    /// Creates the directory if it doesn't exist.
    /// </summary>
    public static string GetLoopsDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MudProxyViewer",
            "Loops");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Save this loop definition to a JSON file.
    /// </summary>
    public static bool SaveToFile(LoopDefinition loop, string filePath)
    {
        try
        {
            var json = JsonSerializer.Serialize(loop, _jsonOptions);
            File.WriteAllText(filePath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Load a loop definition from a JSON file.
    /// Returns null if the file doesn't exist or can't be parsed.
    /// </summary>
    public static LoopDefinition? LoadFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<LoopDefinition>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get all loop files in the Loops directory.
    /// Returns (filePath, loopName) pairs sorted by name.
    /// </summary>
    public static List<(string FilePath, string Name)> GetAvailableLoops()
    {
        var result = new List<(string, string)>();
        var dir = GetLoopsDirectory();

        foreach (var file in Directory.GetFiles(dir, $"*{FileExtension}"))
        {
            try
            {
                var loop = LoadFromFile(file);
                if (loop != null)
                    result.Add((file, loop.Name));
            }
            catch
            {
                // Skip unreadable files
            }
        }

        result.Sort((a, b) => string.Compare(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase));
        return result;
    }
}

/// <summary>
/// A single step in a loop circuit.
/// Currently a room key with cached display name.
/// Structured as a class for future expansion (dwell time, actions, rest stops).
/// </summary>
public class LoopStep
{
    /// <summary>Target room key (e.g., "7/1176").</summary>
    public string RoomKey { get; set; } = string.Empty;

    /// <summary>Cached room name for display. Not used for navigation.</summary>
    public string RoomName { get; set; } = string.Empty;

    // ── Future expansion ──
    // public int DwellTimeMs { get; set; } = 0;
    // public List<string> ActionsOnArrival { get; set; } = new();
    // public bool IsRestStop { get; set; } = false;
    // public string Condition { get; set; } = string.Empty;
}

/// <summary>
/// Result of validating a loop definition against the room graph.
/// </summary>
public class LoopValidationResult
{
    /// <summary>Whether the loop is valid and can be executed.</summary>
    public bool IsValid { get; set; }

    /// <summary>Errors that prevent execution (no exit, missing room, etc.).</summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>Warnings that don't prevent execution (high stat doors, etc.).</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>Aggregated path requirements for the entire loop circuit.</summary>
    public PathRequirements Requirements { get; set; } = new();

    /// <summary>Total number of movement commands in one full lap.</summary>
    public int TotalMoveCommands { get; set; }
}
