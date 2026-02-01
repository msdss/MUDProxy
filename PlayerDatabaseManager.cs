using System.Text.Json;
using System.Text.RegularExpressions;

namespace MudProxyViewer;

public class PlayerDatabaseManager
{
    private PlayerDatabase _database = new();
    private readonly string _databaseFilePath;
    
    // Events
    public event Action? OnDatabaseChanged;
    public event Action<string>? OnLogMessage;
    
    // Regex to parse "who" command output
    // Format: "   Alignment FirstName LastName      -  Title of Gang V"
    // or:     "   Alignment FirstName              -  Title of Gang V"
    // Alignment can be: Fiend, Villain, Criminal, Outlaw, Seedy, (blank), Good, Saint, Lawful
    // The - or x separator comes after the name(s) with variable spacing (two spaces after - or x)
    private static readonly Regex WhoLineRegex = new(
        @"^\s*(FIEND|Fiend|Villain|Criminal|Outlaw|Seedy|Good|Saint|Lawful)?\s+(\w+)\s+(\w+)?\s*[-x]\s{2}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    public PlayerDatabaseManager()
    {
        _databaseFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MudProxyViewer",
            "playerdb.json");
        
        LoadDatabase();
    }
    
    public IReadOnlyList<PlayerData> Players => _database.Players.AsReadOnly();
    public int PlayerCount => _database.Players.Count;
    
    #region Database Operations
    
    private void LoadDatabase()
    {
        try
        {
            if (File.Exists(_databaseFilePath))
            {
                var json = File.ReadAllText(_databaseFilePath);
                var database = JsonSerializer.Deserialize<PlayerDatabase>(json);
                if (database != null)
                {
                    _database = database;
                }
            }
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error loading player database: {ex.Message}");
        }
    }
    
    public void SaveDatabase()
    {
        try
        {
            var directory = Path.GetDirectoryName(_databaseFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var json = JsonSerializer.Serialize(_database, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(_databaseFilePath, json);
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error saving player database: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Replace the entire database (for loading from character profile)
    /// </summary>
    public void ReplaceDatabase(List<PlayerData> players)
    {
        _database.Players.Clear();
        _database.Players.AddRange(players);
        SaveDatabase();
        OnDatabaseChanged?.Invoke();
        OnLogMessage?.Invoke($"📂 Loaded {players.Count} player(s) from profile");
    }
    
    #endregion
    
    #region Player CRUD
    
    public PlayerData? GetPlayer(string firstName)
    {
        return _database.Players.FirstOrDefault(p => 
            p.FirstName.Equals(firstName, StringComparison.OrdinalIgnoreCase));
    }
    
    public PlayerData AddOrUpdatePlayer(string firstName, string? lastName = null)
    {
        var existing = GetPlayer(firstName);
        
        if (existing != null)
        {
            // Update existing player
            if (lastName != null)
                existing.LastName = lastName;
            existing.LastSeen = DateTime.Now;
            
            SaveDatabase();
            OnDatabaseChanged?.Invoke();
            return existing;
        }
        else
        {
            // Add new player
            var player = new PlayerData
            {
                FirstName = firstName,
                LastName = lastName ?? string.Empty,
                LastSeen = DateTime.Now
            };
            
            _database.Players.Add(player);
            SaveDatabase();
            OnDatabaseChanged?.Invoke();
            
            OnLogMessage?.Invoke($"📝 New player added to database: {player.FullName}");
            return player;
        }
    }
    
    public void UpdatePlayer(PlayerData player)
    {
        var existing = GetPlayer(player.FirstName);
        if (existing != null)
        {
            var index = _database.Players.IndexOf(existing);
            _database.Players[index] = player;
            SaveDatabase();
            OnDatabaseChanged?.Invoke();
        }
    }
    
    public void RemovePlayer(string firstName)
    {
        _database.Players.RemoveAll(p => 
            p.FirstName.Equals(firstName, StringComparison.OrdinalIgnoreCase));
        SaveDatabase();
        OnDatabaseChanged?.Invoke();
    }
    
    public void ClearDatabase()
    {
        _database.Players.Clear();
        SaveDatabase();
        OnDatabaseChanged?.Invoke();
    }
    
    #endregion
    
    #region Message Processing
    
    /// <summary>
    /// Process messages looking for "who" command output
    /// </summary>
    public void ProcessMessage(string message)
    {
        // Quick check - does this look like it might contain who output?
        if (!message.Contains(" - ") && !message.Contains(" x "))
            return;
        
        // Split message into lines and process each one
        var lines = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            // Skip header lines
            if (line.Contains("Current Adventurers") || line.Contains("==================="))
                continue;
            
            // Skip lines that don't look like player entries
            if (!line.Contains(" - ") && !line.Contains(" x "))
                continue;
            
            // Try to parse as a player line
            var match = WhoLineRegex.Match(line);
            if (match.Success)
            {
                var firstName = match.Groups[2].Value;
                var lastName = match.Groups[3].Success ? match.Groups[3].Value : "";
                
                // Add or update player (just name, no alignment)
                AddOrUpdatePlayer(firstName, lastName);
            }
        }
    }
    
    #endregion
    
    #region Search and Filter
    
    public IEnumerable<PlayerData> SearchPlayers(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return _database.Players;
        
        return _database.Players.Where(p =>
            p.FirstName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
            p.LastName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
    }
    
    #endregion
}
