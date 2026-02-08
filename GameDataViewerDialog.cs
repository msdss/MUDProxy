using System.Text.Json;

namespace MudProxyViewer;

/// <summary>
/// Dialog for viewing imported game data from JSON files.
/// Loads data asynchronously to prevent UI freezing.
/// </summary>
public class GameDataViewerDialog : Form
{
    private readonly string _tableName;
    private readonly string _filePath;
    
    private DataGridView _dataGrid = null!;
    private TextBox _searchBox = null!;
    private Label _searchLabel = null!;
    private Label _countLabel = null!;
    private Label _loadingLabel = null!;
    private System.Data.DataTable? _dataTable;
    private System.Data.DataView? _dataView;
    
    // Column aliases for Races display (maps DataPropertyName -> Header text)
    private static readonly Dictionary<string, string> RacesColumnAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "mSTR", "Min STR" }, { "xSTR", "Max STR" },
        { "mINT", "Min INT" }, { "xINT", "Max INT" },
        { "mWIL", "Min WIL" }, { "xWIL", "Max WIL" },
        { "mAGL", "Min AGL" }, { "xAGL", "Max AGL" },
        { "mHEA", "Min HEA" }, { "xHEA", "Max HEA" },
        { "mCHM", "Min CHM" }, { "xCHM", "Max CHM" },
        { "ExpTable", "Exp %" }
    };
    
    // Columns to hide for Races (HPPerLVL + all Abil/AbilVal columns handled separately)
    private static readonly HashSet<string> RacesHiddenColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "HPPerLVL"
    };
    
    // Columns to show for Classes (only these)
    private static readonly HashSet<string> ClassesVisibleColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Number", "Name", "ExpTable"
    };
    
    // Tables that don't need a search bar
    private static readonly HashSet<string> NoSearchBarTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "Races", "Classes"
    };
    
    public GameDataViewerDialog(string tableName, string filePath)
    {
        _tableName = tableName;
        _filePath = filePath;
        
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        this.Text = $"Game Data - {_tableName}";
        this.Size = new Size(1000, 600);
        this.MinimumSize = new Size(600, 400);
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);
        
        bool hideSearch = NoSearchBarTables.Contains(_tableName);
        int gridTop = hideSearch ? 15 : 50;
        
        // Search label
        _searchLabel = new Label
        {
            Text = "Search:",
            Location = new Point(15, 15),
            AutoSize = true,
            ForeColor = Color.White,
            Visible = !hideSearch
        };
        this.Controls.Add(_searchLabel);
        
        // Search box
        _searchBox = new TextBox
        {
            Location = new Point(70, 12),
            Width = 300,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Visible = !hideSearch
        };
        _searchBox.TextChanged += SearchBox_TextChanged;
        this.Controls.Add(_searchBox);
        
        // Count label
        _countLabel = new Label
        {
            Location = new Point(hideSearch ? 15 : 385, 15),
            AutoSize = true,
            ForeColor = Color.LightGray,
            Visible = !hideSearch
        };
        this.Controls.Add(_countLabel);
        
        // Loading label
        _loadingLabel = new Label
        {
            Text = "Loading data...",
            Location = new Point(15, gridTop),
            Size = new Size(this.ClientSize.Width - 30, this.ClientSize.Height - gridTop - 60),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 14),
            BackColor = Color.FromArgb(30, 30, 30)
        };
        this.Controls.Add(_loadingLabel);
        
        // Data grid
        _dataGrid = new DataGridView
        {
            Location = new Point(15, gridTop),
            Size = new Size(this.ClientSize.Width - 30, this.ClientSize.Height - gridTop - 60),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackgroundColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            GridColor = Color.FromArgb(60, 60, 60),
            BorderStyle = BorderStyle.FixedSingle,
            RowHeadersVisible = false,
            ColumnHeadersVisible = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            ColumnHeadersHeight = 30,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            RowTemplate = { Height = 24 },
            EnableHeadersVisualStyles = false,
            Visible = false
        };
        
        _dataGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(70, 70, 70),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleLeft
        };
        
        _dataGrid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(35, 35, 35),
            ForeColor = Color.White,
            SelectionBackColor = Color.FromArgb(70, 130, 180),
            SelectionForeColor = Color.White
        };
        
        _dataGrid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.White,
            SelectionBackColor = Color.FromArgb(70, 130, 180),
            SelectionForeColor = Color.White
        };
        
        _dataGrid.ColumnHeaderMouseClick += DataGrid_ColumnHeaderMouseClick;
        _dataGrid.CellDoubleClick += DataGrid_CellDoubleClick;
        
        this.Controls.Add(_dataGrid);
        
        var closeButton = new Button
        {
            Text = "Close",
            Width = 80,
            Height = 30,
            Location = new Point(this.ClientSize.Width - 95, this.ClientSize.Height - 45),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        this.Controls.Add(closeButton);
        
        var refreshButton = new Button
        {
            Text = "Refresh",
            Width = 80,
            Height = 30,
            Location = new Point(this.ClientSize.Width - 185, this.ClientSize.Height - 45),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        refreshButton.Click += async (s, e) => await LoadDataAsync();
        this.Controls.Add(refreshButton);
        
        this.AcceptButton = closeButton;
        this.Shown += async (s, e) => await LoadDataAsync();
    }
    
    private async Task LoadDataAsync()
    {
        _loadingLabel.Visible = true;
        _dataGrid.Visible = false;
        _countLabel.Text = "Loading...";
        _searchBox.Enabled = false;
        
        try
        {
            System.Data.DataTable? dataTable = null;
            var cachedData = GameDataCache.Instance.GetTable(_tableName);
            
            if (cachedData != null)
            {
                dataTable = await Task.Run(() => ConvertCacheToDataTable(cachedData));
            }
            else
            {
                dataTable = await Task.Run(() => LoadJsonToDataTable());
            }
            
            if (dataTable == null || dataTable.Rows.Count == 0)
            {
                _countLabel.Text = "No data found";
                _loadingLabel.Text = "No data found in this table.";
                return;
            }
            
            _dataTable = dataTable;
            _dataView = new System.Data.DataView(_dataTable);
            
            if (_dataTable.Columns.Contains("Number"))
            {
                _dataView.Sort = "[Number] ASC";
            }
            
            _dataGrid.Columns.Clear();
            _dataGrid.DataSource = null;
            _dataGrid.DataSource = _dataView;
            
            ConfigureTableColumns();
            
            foreach (DataGridViewColumn col in _dataGrid.Columns)
            {
                if (!col.Visible) continue;
                
                col.SortMode = DataGridViewColumnSortMode.Programmatic;
                col.MinimumWidth = 50;
                
                if (col.DataPropertyName == "Number")
                {
                    col.HeaderCell.SortGlyphDirection = SortOrder.Ascending;
                }
                
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            }
            
            _dataGrid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
            
            bool useNarrowWidths = _tableName == "Races" || _tableName == "Classes";
            DataGridViewColumn? lastVisibleColumn = null;
            
            foreach (DataGridViewColumn col in _dataGrid.Columns)
            {
                if (!col.Visible) continue;
                
                int width = col.Width;
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                
                if (useNarrowWidths)
                {
                    if (col.DataPropertyName == "Name")
                        col.Width = Math.Max(width - 5, 60);
                    else
                        col.Width = Math.Max(Math.Min(width, 80), 50);
                }
                else
                {
                    col.Width = Math.Min(Math.Max(width, 60), 300);
                }
                
                lastVisibleColumn = col;
            }
            
            if (_tableName == "Classes" && lastVisibleColumn != null)
            {
                lastVisibleColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            }
            
            _loadingLabel.Visible = false;
            _dataGrid.Visible = true;
            _searchBox.Enabled = true;
            UpdateCountLabel();
        }
        catch (Exception ex)
        {
            _loadingLabel.Text = $"Error loading data:\n{ex.Message}";
            _countLabel.Text = "Error";
        }
    }
    
    private void ConfigureTableColumns()
    {
        switch (_tableName)
        {
            case "Races":
                ConfigureRacesColumns();
                break;
            case "Classes":
                ConfigureClassesColumns();
                break;
        }
    }
    
    private void ConfigureRacesColumns()
    {
        foreach (DataGridViewColumn col in _dataGrid.Columns)
        {
            var name = col.DataPropertyName;
            
            if (RacesHiddenColumns.Contains(name))
            {
                col.Visible = false;
                continue;
            }
            
            if (AbilityNames.IsAbilityColumn(name))
            {
                col.Visible = false;
                continue;
            }
            
            if (RacesColumnAliases.TryGetValue(name, out var alias))
            {
                col.HeaderText = alias;
            }
        }
    }
    
    private void ConfigureClassesColumns()
    {
        foreach (DataGridViewColumn col in _dataGrid.Columns)
        {
            var name = col.DataPropertyName;
            
            if (!ClassesVisibleColumns.Contains(name))
            {
                col.Visible = false;
                continue;
            }
            
            if (name == "ExpTable")
            {
                col.HeaderText = "Exp %";
            }
        }
    }
    
    private System.Data.DataTable? ConvertCacheToDataTable(List<Dictionary<string, object?>> cachedData)
    {
        if (cachedData.Count == 0)
            return null;
        
        var dt = new System.Data.DataTable(_tableName);
        
        var firstRow = cachedData[0];
        var columns = firstRow.Keys.ToList();
        var columnTypes = new Dictionary<string, Type>();
        
        foreach (var col in columns)
        {
            Type colType = typeof(string);
            var val = firstRow[col];
            if (val != null)
            {
                if (val is long || val is int || val is short || val is byte)
                    colType = typeof(long);
                else if (val is decimal || val is double || val is float)
                    colType = typeof(decimal);
                else if (val is bool)
                    colType = typeof(bool);
            }
            columnTypes[col] = colType;
            var dataCol = dt.Columns.Add(col, colType);
            dataCol.Caption = col;
        }
        
        foreach (var row in cachedData)
        {
            var values = new object[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                var colName = columns[i];
                if (row.TryGetValue(colName, out var val) && val != null)
                {
                    var targetType = columnTypes[colName];
                    try
                    {
                        if (targetType == typeof(long))
                            values[i] = Convert.ToInt64(val);
                        else if (targetType == typeof(decimal))
                            values[i] = Convert.ToDecimal(val);
                        else if (targetType == typeof(bool))
                            values[i] = Convert.ToBoolean(val);
                        else
                            values[i] = val.ToString() ?? "";
                    }
                    catch
                    {
                        values[i] = val.ToString() ?? "";
                    }
                }
                else
                {
                    values[i] = DBNull.Value;
                }
            }
            dt.Rows.Add(values);
        }
        
        return dt;
    }
    
    private System.Data.DataTable? LoadJsonToDataTable()
    {
        var json = File.ReadAllText(_filePath);
        
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            return null;
        
        var dt = new System.Data.DataTable(_tableName);
        
        var firstRow = root[0];
        var columns = new List<string>();
        var columnTypes = new Dictionary<string, Type>();
        
        foreach (var prop in firstRow.EnumerateObject())
        {
            columns.Add(prop.Name);
            
            Type colType = prop.Value.ValueKind switch
            {
                JsonValueKind.Number => typeof(decimal),
                JsonValueKind.True or JsonValueKind.False => typeof(bool),
                _ => typeof(string)
            };
            
            columnTypes[prop.Name] = colType;
            var dataCol = dt.Columns.Add(prop.Name, colType);
            dataCol.Caption = prop.Name;
        }
        
        foreach (var row in root.EnumerateArray())
        {
            var values = new object[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                var colName = columns[i];
                if (row.TryGetProperty(colName, out var prop))
                {
                    values[i] = GetTypedValue(prop, columnTypes[colName]);
                }
                else
                {
                    values[i] = DBNull.Value;
                }
            }
            dt.Rows.Add(values);
        }
        
        return dt;
    }
    
    private static object GetTypedValue(JsonElement element, Type targetType)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return DBNull.Value;
        
        try
        {
            if (targetType == typeof(decimal))
            {
                if (element.ValueKind == JsonValueKind.Number)
                    return element.GetDecimal();
                if (decimal.TryParse(element.ToString(), out var d))
                    return d;
            }
            else if (targetType == typeof(bool))
            {
                if (element.ValueKind == JsonValueKind.True)
                    return true;
                if (element.ValueKind == JsonValueKind.False)
                    return false;
            }
        }
        catch { }
        
        return GetValueAsString(element);
    }
    
    private static string GetValueAsString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetDecimal().ToString(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            JsonValueKind.Null => "",
            JsonValueKind.Array => $"[{element.GetArrayLength()} items]",
            JsonValueKind.Object => "{...}",
            _ => element.ToString()
        };
    }
    
    private void UpdateCountLabel()
    {
        if (_dataView != null && _dataTable != null)
        {
            _countLabel.Text = $"{_dataView.Count} of {_dataTable.Rows.Count} records";
        }
    }
    
    private void SearchBox_TextChanged(object? sender, EventArgs e)
    {
        if (_dataView == null || _dataTable == null)
            return;
        
        var searchText = _searchBox.Text.Trim();
        
        if (string.IsNullOrEmpty(searchText))
        {
            _dataView.RowFilter = "";
        }
        else
        {
            var filters = new List<string>();
            foreach (System.Data.DataColumn col in _dataTable.Columns)
            {
                var escaped = searchText.Replace("'", "''");
                filters.Add($"CONVERT([{col.ColumnName}], 'System.String') LIKE '%{escaped}%'");
            }
            
            try
            {
                _dataView.RowFilter = string.Join(" OR ", filters);
            }
            catch
            {
                _dataView.RowFilter = "";
            }
        }
        
        UpdateCountLabel();
    }
    
    private void DataGrid_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (_dataView == null)
            return;
        
        var column = _dataGrid.Columns[e.ColumnIndex];
        var columnName = column.DataPropertyName;
        
        if (_dataView.Sort == $"[{columnName}] ASC")
        {
            _dataView.Sort = $"[{columnName}] DESC";
            column.HeaderCell.SortGlyphDirection = SortOrder.Descending;
        }
        else
        {
            _dataView.Sort = $"[{columnName}] ASC";
            column.HeaderCell.SortGlyphDirection = SortOrder.Ascending;
        }
        
        foreach (DataGridViewColumn col in _dataGrid.Columns)
        {
            if (col.Index != e.ColumnIndex)
            {
                col.HeaderCell.SortGlyphDirection = SortOrder.None;
            }
        }
    }
    
    private void DataGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _dataGrid.SelectedRows.Count == 0)
            return;
        
        var selectedRow = _dataGrid.SelectedRows[0];
        var dataRowView = selectedRow.DataBoundItem as System.Data.DataRowView;
        if (dataRowView == null) return;
        
        var dataRow = dataRowView.Row;
        var rowData = new Dictionary<string, object?>();
        
        foreach (System.Data.DataColumn col in dataRow.Table.Columns)
        {
            var value = dataRow[col];
            rowData[col.ColumnName] = value == DBNull.Value ? null : value;
        }
        
        Form detailDialog = _tableName switch
        {
            "Races" => new RaceDetailDialog(rowData),
            "Classes" => new ClassDetailDialog(rowData),
            "Items" => new ItemDetailDialog(rowData),
            _ => new GenericDetailDialog(rowData, $"{_tableName} Details - {GetRowDisplayName(rowData)}")
        };
        
        using (detailDialog)
        {
            detailDialog.ShowDialog(this);
        }
    }
    
    private static string GetRowDisplayName(Dictionary<string, object?> rowData)
    {
        if (rowData.TryGetValue("Name", out var name) && name != null)
            return name.ToString() ?? "Unknown";
        if (rowData.TryGetValue("Number", out var number) && number != null)
            return $"#{number}";
        return "Unknown";
    }
}
