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
    private Label _countLabel = null!;
    private Label _loadingLabel = null!;
    private System.Data.DataTable? _dataTable;
    private System.Data.DataView? _dataView;
    
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
        
        // Search label
        var searchLabel = new Label
        {
            Text = "Search:",
            Location = new Point(15, 15),
            AutoSize = true,
            ForeColor = Color.White
        };
        this.Controls.Add(searchLabel);
        
        // Search box
        _searchBox = new TextBox
        {
            Location = new Point(70, 12),
            Width = 300,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        _searchBox.TextChanged += SearchBox_TextChanged;
        this.Controls.Add(_searchBox);
        
        // Count label
        _countLabel = new Label
        {
            Location = new Point(385, 15),
            AutoSize = true,
            ForeColor = Color.LightGray
        };
        this.Controls.Add(_countLabel);
        
        // Loading label
        _loadingLabel = new Label
        {
            Text = "Loading data...",
            Location = new Point(15, 50),
            Size = new Size(this.ClientSize.Width - 30, this.ClientSize.Height - 110),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 14),
            BackColor = Color.FromArgb(30, 30, 30)
        };
        this.Controls.Add(_loadingLabel);
        
        // Data grid - positioned below search, above buttons
        _dataGrid = new DataGridView
        {
            Location = new Point(15, 50),
            Size = new Size(this.ClientSize.Width - 30, this.ClientSize.Height - 110),
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
        
        // Style the headers
        _dataGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(70, 70, 70),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleLeft
        };
        
        // Style the cells
        _dataGrid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(35, 35, 35),
            ForeColor = Color.White,
            SelectionBackColor = Color.FromArgb(70, 130, 180),
            SelectionForeColor = Color.White
        };
        
        // Alternating row style
        _dataGrid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.White,
            SelectionBackColor = Color.FromArgb(70, 130, 180),
            SelectionForeColor = Color.White
        };
        
        // Enable sorting by clicking column headers
        _dataGrid.ColumnHeaderMouseClick += DataGrid_ColumnHeaderMouseClick;
        
        this.Controls.Add(_dataGrid);
        
        // Close button
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
        
        // Refresh button
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
        
        // Load data when shown
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
            // Try to get from cache first (faster)
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
            
            // Apply default sort by "Number" column if it exists
            if (_dataTable.Columns.Contains("Number"))
            {
                _dataView.Sort = "[Number] ASC";
            }
            
            // Clear existing columns
            _dataGrid.Columns.Clear();
            _dataGrid.DataSource = null;
            
            // Bind to grid
            _dataGrid.DataSource = _dataView;
            
            // Configure columns after binding
            foreach (DataGridViewColumn col in _dataGrid.Columns)
            {
                col.SortMode = DataGridViewColumnSortMode.Programmatic;
                col.MinimumWidth = 50;
                
                // Show sort glyph on Number column if sorted
                if (col.DataPropertyName == "Number")
                {
                    col.HeaderCell.SortGlyphDirection = SortOrder.Ascending;
                }
                
                // Auto-size then fix width
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            }
            
            // Force a refresh to apply auto-sizing
            _dataGrid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
            
            // Now fix the widths and set limits
            foreach (DataGridViewColumn col in _dataGrid.Columns)
            {
                int width = col.Width;
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                col.Width = Math.Min(Math.Max(width, 60), 300);
            }
            
            _loadingLabel.Visible = false;
            _dataGrid.Visible = true;
            _searchBox.Enabled = true;
            UpdateCountLabel();
            
            // Debug: Log column info
            System.Diagnostics.Debug.WriteLine($"Loaded {_dataTable.Columns.Count} columns, {_dataTable.Rows.Count} rows");
            foreach (System.Data.DataColumn col in _dataTable.Columns)
            {
                System.Diagnostics.Debug.WriteLine($"  Column: {col.ColumnName}");
            }
        }
        catch (Exception ex)
        {
            _loadingLabel.Text = $"Error loading data:\n{ex.Message}";
            _countLabel.Text = "Error";
        }
    }
    
    private System.Data.DataTable? ConvertCacheToDataTable(List<Dictionary<string, object?>> cachedData)
    {
        if (cachedData.Count == 0)
            return null;
        
        var dt = new System.Data.DataTable(_tableName);
        
        // Get columns from first row and detect types
        var firstRow = cachedData[0];
        var columns = firstRow.Keys.ToList();
        var columnTypes = new Dictionary<string, Type>();
        
        foreach (var col in columns)
        {
            // Detect type from first non-null value
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
        
        // Add rows
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
        
        // Get columns from first row and detect types
        var firstRow = root[0];
        var columns = new List<string>();
        var columnTypes = new Dictionary<string, Type>();
        
        foreach (var prop in firstRow.EnumerateObject())
        {
            columns.Add(prop.Name);
            
            // Detect type from JSON
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
        
        // Add rows
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
        
        // Fall back to string
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
}
