using System.Collections.Concurrent;
using System.Text.Json;

namespace MudProxyViewer;

/// <summary>
/// Caches game data in memory for fast access.
/// Pre-loads data on startup in background thread.
/// </summary>
public class GameDataCache
{
    private static GameDataCache? _instance;
    private static readonly object _lock = new();
    
    public static GameDataCache Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new GameDataCache();
                }
            }
            return _instance;
        }
    }
    
    private readonly string _gameDataPath;
    private readonly ConcurrentDictionary<string, List<Dictionary<string, object?>>> _cache = new();
    private readonly ConcurrentDictionary<string, bool> _loadingStatus = new();
    
    public event Action<string>? OnTableLoaded;
    public event Action? OnAllTablesLoaded;
    public event Action<string, Exception>? OnLoadError;
    
    public bool IsLoading { get; private set; }
    public bool IsLoaded { get; private set; }
    
    private GameDataCache()
    {
        _gameDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MudProxyViewer",
            "Game Data");
    }
    
    /// <summary>
    /// Start pre-loading all game data in background
    /// </summary>
    public void StartPreload()
    {
        if (IsLoading || IsLoaded)
            return;
        
        if (!Directory.Exists(_gameDataPath))
            return;
        
        IsLoading = true;
        
        Task.Run(async () =>
        {
            try
            {
                var files = Directory.GetFiles(_gameDataPath, "*.json");
                
                foreach (var file in files)
                {
                    var tableName = Path.GetFileNameWithoutExtension(file);
                    await LoadTableAsync(tableName);
                }
                
                IsLoaded = true;
                OnAllTablesLoaded?.Invoke();
            }
            catch (Exception ex)
            {
                OnLoadError?.Invoke("Preload", ex);
            }
            finally
            {
                IsLoading = false;
            }
        });
    }
    
    /// <summary>
    /// Load a specific table (async)
    /// </summary>
    public async Task<List<Dictionary<string, object?>>?> LoadTableAsync(string tableName)
    {
        // Return cached if available
        if (_cache.TryGetValue(tableName, out var cached))
            return cached;
        
        // Check if already loading
        if (_loadingStatus.TryGetValue(tableName, out var loading) && loading)
        {
            // Wait for load to complete
            while (_loadingStatus.TryGetValue(tableName, out var stillLoading) && stillLoading)
            {
                await Task.Delay(50);
            }
            _cache.TryGetValue(tableName, out cached);
            return cached;
        }
        
        _loadingStatus[tableName] = true;
        
        try
        {
            var filePath = Path.Combine(_gameDataPath, $"{tableName}.json");
            if (!File.Exists(filePath))
                return null;
            
            var json = await File.ReadAllTextAsync(filePath);
            var data = ParseJson(json);
            
            if (data != null)
            {
                _cache[tableName] = data;
                OnTableLoaded?.Invoke(tableName);
            }
            
            return data;
        }
        catch (Exception ex)
        {
            OnLoadError?.Invoke(tableName, ex);
            return null;
        }
        finally
        {
            _loadingStatus[tableName] = false;
        }
    }
    
    /// <summary>
    /// Get cached table data (returns null if not loaded)
    /// </summary>
    public List<Dictionary<string, object?>>? GetTable(string tableName)
    {
        _cache.TryGetValue(tableName, out var data);
        return data;
    }
    
    /// <summary>
    /// Check if a table is cached
    /// </summary>
    public bool IsTableCached(string tableName)
    {
        return _cache.ContainsKey(tableName);
    }
    
    /// <summary>
    /// Clear all cached data (call after re-import)
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
        IsLoaded = false;
    }
    
    /// <summary>
    /// Get list of cached table names
    /// </summary>
    public IEnumerable<string> GetCachedTables()
    {
        return _cache.Keys.ToList();
    }
    
    private static List<Dictionary<string, object?>>? ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        
        if (root.ValueKind != JsonValueKind.Array)
            return null;
        
        var result = new List<Dictionary<string, object?>>();
        
        foreach (var row in root.EnumerateArray())
        {
            var dict = new Dictionary<string, object?>();
            foreach (var prop in row.EnumerateObject())
            {
                dict[prop.Name] = GetValue(prop.Value);
            }
            result.Add(dict);
        }
        
        return result;
    }
    
    private static object? GetValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }
    
    #region Quick Lookup Methods
    
    /// <summary>
    /// Find a monster by name (case-insensitive)
    /// </summary>
    public Dictionary<string, object?>? FindMonster(string name)
    {
        var monsters = GetTable("Monsters");
        return monsters?.FirstOrDefault(m =>
            m.TryGetValue("Name", out var n) && 
            n?.ToString()?.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
    }
    
    /// <summary>
    /// Find an item by name (case-insensitive)
    /// </summary>
    public Dictionary<string, object?>? FindItem(string name)
    {
        var items = GetTable("Items");
        return items?.FirstOrDefault(i =>
            i.TryGetValue("Name", out var n) && 
            n?.ToString()?.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
    }
    
    /// <summary>
    /// Find a spell by abbreviation
    /// </summary>
    public Dictionary<string, object?>? FindSpell(string abbrev)
    {
        var spells = GetTable("Spells");
        return spells?.FirstOrDefault(s =>
            s.TryGetValue("Abbrev", out var a) && 
            a?.ToString()?.Equals(abbrev, StringComparison.OrdinalIgnoreCase) == true);
    }
    
    /// <summary>
    /// Find a room by number
    /// </summary>
    public Dictionary<string, object?>? FindRoom(int roomNumber)
    {
        var rooms = GetTable("Rooms");
        return rooms?.FirstOrDefault(r =>
            r.TryGetValue("Number", out var n) && 
            Convert.ToInt64(n) == roomNumber);
    }
    
    #endregion
}
