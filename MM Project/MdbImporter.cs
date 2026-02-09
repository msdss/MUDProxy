using System.Data;
using System.Data.OleDb;
using System.Text.Json;

namespace MudProxyViewer;

/// <summary>
/// Imports MajorMUD game data from Access .mdb database files.
/// Requires Microsoft Access Database Engine (ACE) to be installed.
/// </summary>
public class MdbImporter
{
    private readonly string _gameDataPath;
    
    // Events for progress reporting
    public event Action<string>? OnStatusChanged;
    public event Action<int, int>? OnProgressChanged;  // current, total
    public event Action<string>? OnError;
    
    // Tables to import - these are the standard MajorMUD table names
    private static readonly string[] TablesToImport = new[]
    {
        "Classes",
        "Races", 
        "Items",
        "Lairs",
        "Monsters",
        "Rooms",
        "Shops",
        "Spells",
        "TextBlocks",
        "Textblocks",
        "TBInfo",      // Text Blocks alternate name
        "tbinfo"       // Lowercase variant
    };
    
    // Events for row-level progress
    public event Action<string, int, int>? OnRowProgress;  // tableName, currentRow, totalRows
    
    public MdbImporter()
    {
        _gameDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MudProxyViewer",
            "Game Data");
    }
    
    public string GameDataPath => _gameDataPath;
    
    /// <summary>
    /// Check if ACE (Access Database Engine) is available
    /// </summary>
    public static bool IsAceInstalled()
    {
        try
        {
            // Check if the provider is registered by enumerating OLE DB providers
            var providers = OleDbEnumerator.GetRootEnumerator();
            while (providers.Read())
            {
                var providerName = providers.GetValue(0)?.ToString() ?? "";
                if (providerName.Contains("Microsoft.ACE.OLEDB"))
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Get a user-friendly error message about ACE not being installed
    /// </summary>
    public static string GetAceNotInstalledMessage()
    {
        return @"Microsoft Access Database Engine (ACE) is not installed.

ACE (Access Connectivity Engine) is required to read .mdb database files. This is a free Microsoft component that provides database connectivity.

To install ACE:
1. Download from: https://www.microsoft.com/en-us/download/details.aspx?id=54920
2. Choose the version that matches your system (32-bit or 64-bit)
3. Run the installer
4. Restart this application

Note: If you have 32-bit Office installed, you need 32-bit ACE.
If you have 64-bit Office installed, you need 64-bit ACE.";
    }
    
    /// <summary>
    /// Import all tables from an MDB file
    /// </summary>
    public async Task<(bool success, string message)> ImportAsync(string mdbFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(mdbFilePath))
        {
            return (false, $"Database file not found: {mdbFilePath}");
        }
        
        if (!IsAceInstalled())
        {
            return (false, GetAceNotInstalledMessage());
        }
        
        // Ensure output directory exists
        if (!Directory.Exists(_gameDataPath))
        {
            Directory.CreateDirectory(_gameDataPath);
        }
        
        var connectionString = $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={mdbFilePath};Mode=Read;";
        
        try
        {
            using var connection = new OleDbConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            
            OnStatusChanged?.Invoke("Connected to database...");
            
            // Get list of tables in the database
            var restrictions = new object?[] { null, null, null, "TABLE" };
            var schema = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, restrictions!);
            var availableTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (schema != null)
            {
                foreach (DataRow row in schema.Rows)
                {
                    var tableName = row["TABLE_NAME"]?.ToString();
                    if (!string.IsNullOrEmpty(tableName) && !tableName.StartsWith("MSys"))
                    {
                        availableTables.Add(tableName);
                    }
                }
            }
            
            OnStatusChanged?.Invoke($"Found {availableTables.Count} tables in database");
            
            // Determine which tables to actually import (avoid duplicates like TextBlocks/Textblocks)
            var tablesToProcess = new List<string>();
            var processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var tableName in TablesToImport)
            {
                if (availableTables.Contains(tableName) && !processedNames.Contains(tableName))
                {
                    // Find the actual table name with correct casing
                    var actualName = availableTables.FirstOrDefault(t => 
                        t.Equals(tableName, StringComparison.OrdinalIgnoreCase)) ?? tableName;
                    tablesToProcess.Add(actualName);
                    processedNames.Add(actualName);
                }
            }
            
            // Import each table
            int tablesProcessed = 0;
            int totalTables = tablesToProcess.Count;
            var importedTables = new List<string>();
            var skippedTables = new List<string>();
            
            OnProgressChanged?.Invoke(0, totalTables);
            
            foreach (var tableName in tablesToProcess)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                OnStatusChanged?.Invoke($"Importing {tableName}...");
                
                try
                {
                    var (success, rowCount) = await ImportTableAsync(connection, tableName, cancellationToken);
                    if (success)
                    {
                        importedTables.Add($"{tableName} ({rowCount} rows)");
                    }
                    else
                    {
                        skippedTables.Add($"{tableName} (error)");
                    }
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"Error importing {tableName}: {ex.Message}");
                    skippedTables.Add($"{tableName} (error: {ex.Message})");
                }
                
                tablesProcessed++;
                OnProgressChanged?.Invoke(tablesProcessed, totalTables);
            }
            
            // Build result message
            var message = $"Import completed!\n\nImported {importedTables.Count} tables:\n";
            message += string.Join("\n", importedTables.Select(t => $"  ✓ {t}"));
            
            if (skippedTables.Count > 0)
            {
                message += $"\n\nSkipped {skippedTables.Count} tables:\n";
                message += string.Join("\n", skippedTables.Select(t => $"  ⚠ {t}"));
            }
            
            message += $"\n\nData saved to:\n{_gameDataPath}";
            
            return (true, message);
        }
        catch (OleDbException ex)
        {
            var errorMessage = $"Database error: {ex.Message}";
            if (ex.Message.Contains("not a valid path") || ex.Message.Contains("Could not find file"))
            {
                errorMessage = $"Could not open database file: {mdbFilePath}\n\nMake sure the file exists and is a valid Access database.";
            }
            else if (ex.Message.Contains("not registered"))
            {
                errorMessage = GetAceNotInstalledMessage();
            }
            return (false, errorMessage);
        }
        catch (OperationCanceledException)
        {
            return (false, "Import was cancelled.");
        }
        catch (Exception ex)
        {
            return (false, $"Unexpected error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Import a single table to JSON
    /// </summary>
    private async Task<(bool success, int rowCount)> ImportTableAsync(OleDbConnection connection, string tableName, CancellationToken cancellationToken)
    {
        // First, get total row count for progress
        var countQuery = $"SELECT COUNT(*) FROM [{tableName}]";
        using var countCommand = new OleDbCommand(countQuery, connection);
        var totalRows = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
        
        OnStatusChanged?.Invoke($"  Reading {totalRows} rows...");
        
        var query = $"SELECT * FROM [{tableName}]";
        
        using var command = new OleDbCommand(query, connection);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        
        var rows = new List<Dictionary<string, object?>>();
        var columns = new List<string>();
        
        // Get column names from the schema
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }
        
        // Read all rows with progress reporting
        int rowCount = 0;
        int lastReportedPercent = 0;
        
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                // Convert DBNull to null for cleaner JSON
                row[columns[i]] = value == DBNull.Value ? null : value;
            }
            rows.Add(row);
            rowCount++;
            
            // Report progress every 5%
            if (totalRows > 0)
            {
                int percent = (rowCount * 100) / totalRows;
                if (percent >= lastReportedPercent + 5)
                {
                    lastReportedPercent = percent;
                    OnRowProgress?.Invoke(tableName, rowCount, totalRows);
                }
            }
        }
        
        OnStatusChanged?.Invoke($"  Writing {rows.Count} rows to JSON...");
        
        // Serialize to JSON
        var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        
        // Normalize table name for file (e.g., "Textblocks" -> "TextBlocks")
        var normalizedName = NormalizeTableName(tableName);
        var outputPath = Path.Combine(_gameDataPath, $"{normalizedName}.json");
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);
        
        OnStatusChanged?.Invoke($"  Saved {rows.Count} rows to {normalizedName}.json");
        
        return (true, rows.Count);
    }
    
    /// <summary>
    /// Normalize table names to consistent casing
    /// </summary>
    private static string NormalizeTableName(string tableName)
    {
        return tableName.ToLower() switch
        {
            "textblocks" => "TextBlocks",
            "classes" => "Classes",
            "races" => "Races",
            "items" => "Items",
            "lairs" => "Lairs",
            "monsters" => "Monsters",
            "rooms" => "Rooms",
            "shops" => "Shops",
            "spells" => "Spells",
            "tbinfo" => "TextBlocks",  // TBInfo -> TextBlocks
            _ => tableName
        };
    }
    
    /// <summary>
    /// Check if game data has been imported
    /// </summary>
    public bool IsGameDataImported()
    {
        if (!Directory.Exists(_gameDataPath))
            return false;
        
        // Check if at least some key tables exist
        var requiredTables = new[] { "Classes.json", "Items.json", "Monsters.json", "Rooms.json", "Spells.json" };
        return requiredTables.All(t => File.Exists(Path.Combine(_gameDataPath, t)));
    }
    
    /// <summary>
    /// Get list of imported tables
    /// </summary>
    public IEnumerable<string> GetImportedTables()
    {
        if (!Directory.Exists(_gameDataPath))
            yield break;
        
        foreach (var file in Directory.GetFiles(_gameDataPath, "*.json"))
        {
            yield return Path.GetFileNameWithoutExtension(file);
        }
    }
}
