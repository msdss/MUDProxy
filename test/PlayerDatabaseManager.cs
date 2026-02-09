using System.Text.RegularExpressions;

namespace MudProxyViewer;

/// <summary>
/// Manages player data in-memory. Data is persisted via CharacterProfile, not a global file.
/// When a character profile is loaded, call LoadFromProfile().
/// When saving a character profile, call GetPlayersForProfile() to get the current list.
/// </summary>
public class PlayerDatabaseManager
{
    private readonly List<PlayerData> _players = new();
    
    // Events
    public event Action? OnDatabaseChanged;
    public event Action<string>? OnLogMessage;
    public event Action? OnDataChanged;  // Fires when data changes that should trigger a profile save
    
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
        // No file loading - data comes from character profile via LoadFromProfile()
    }
    
    public IReadOnlyList<PlayerData> Players => _players.AsReadOnly();
    public int PlayerCount => _players.Count;
    
    #region Profile Integration
    
    /// <summary>
    /// Load players from a character profile. Call this when loading a character.
    /// </summary>
    public void LoadFromProfile(List<PlayerData> players)
    {
        _players.Clear();
        if (players != null)
        {
            _players.AddRange(players);
        }
        OnDatabaseChanged?.Invoke();
        OnLogMessage?.Invoke($"📂 Loaded {_players.Count} player(s) from profile");
    }
    
    /// <summary>
    /// Get the current player list for saving to a character profile.
    /// </summary>
    public List<PlayerData> GetPlayersForProfile()
    {
        return _players.ToList();
    }
    
    /// <summary>
    /// Clear all players (call when no character is loaded or creating new profile)
    /// </summary>
    public void Clear()
    {
        _players.Clear();
        OnDatabaseChanged?.Invoke();
    }
    
    #endregion
    
    #region Player CRUD
    
    public PlayerData? GetPlayer(string firstName)
    {
        return _players.FirstOrDefault(p => 
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
            
            _players.Add(player);
            OnDatabaseChanged?.Invoke();
            OnDataChanged?.Invoke();  // Trigger profile save
            
            OnLogMessage?.Invoke($"📝 New player added to database: {player.FullName}");
            return player;
        }
    }
    
    public void UpdatePlayer(PlayerData player)
    {
        var existing = GetPlayer(player.FirstName);
        if (existing != null)
        {
            var index = _players.IndexOf(existing);
            _players[index] = player;
            OnDatabaseChanged?.Invoke();
            OnDataChanged?.Invoke();  // Trigger profile save
        }
    }
    
    public void RemovePlayer(string firstName)
    {
        var removed = _players.RemoveAll(p => 
            p.FirstName.Equals(firstName, StringComparison.OrdinalIgnoreCase));
        OnDatabaseChanged?.Invoke();
        if (removed > 0)
        {
            OnDataChanged?.Invoke();  // Trigger profile save
        }
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
            return _players;
        
        return _players.Where(p =>
            p.FirstName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
            p.LastName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
    }
    
    #endregion
    
    #region Legacy Support
    
    /// <summary>
    /// Legacy method - replaced by LoadFromProfile(). Kept for compatibility.
    /// </summary>
    [Obsolete("Use LoadFromProfile() instead")]
    public void ReplaceDatabase(List<PlayerData> players)
    {
        LoadFromProfile(players);
    }
    
    /// <summary>
    /// Legacy method - no longer writes to file. Data is saved via character profile.
    /// </summary>
    [Obsolete("Data is now saved via character profile, not a global file")]
    public void SaveDatabase()
    {
        // No-op - data is saved when character profile is saved
        // Kept for compatibility with any code that calls this
    }
    
    #endregion
}
