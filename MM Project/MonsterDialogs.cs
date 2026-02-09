namespace MudProxyViewer;

/// <summary>
/// Configuration for Monsters table in the GameDataViewerDialog.
/// Note: Monsters currently use the separate MonsterDatabaseDialog for the full
/// list view with override support. This config is for the generic GameDataViewerDialog
/// if it's ever used for monsters.
/// </summary>
public static class MonsterViewerConfig
{
    /// <summary>
    /// Columns to show for Monsters in the generic viewer.
    /// </summary>
    public static readonly HashSet<string>? VisibleColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Number", "Name"
    };
    
    /// <summary>
    /// Whether to show the search bar for this table.
    /// </summary>
    public static bool ShowSearchBar => true;
    
    /// <summary>
    /// Whether the Name column should fill remaining space.
    /// </summary>
    public static bool NameColumnFills => true;
}

// Note: MonsterDetailDialog and MonsterEditDialog are currently defined in 
// MonsterDatabaseDialog.cs. They should eventually be moved here for consistency.
// 
// The MonsterDatabaseDialog.cs file contains:
// - MonsterDatabaseDialog: The main list view with search, sorting, and override support
// - MonsterEditDialog: Dialog for editing monster overrides (relationship, spells, priority, etc.)
//
// TODO: Consider refactoring to move the dialogs here while keeping the custom
// list view in MonsterDatabaseDialog.cs, or fully integrating with the 
// GameDataViewerDialog pattern.
