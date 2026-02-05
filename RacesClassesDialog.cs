using System.Text.Json;

namespace MudProxyViewer;

/// <summary>
/// Combined dialog for viewing Races and Classes game data
/// </summary>
public class RacesClassesDialog : Form
{
    private readonly string _gameDataPath;
    private TabControl _tabControl = null!;
    private DataGridView _racesGrid = null!;
    private DataGridView _classesGrid = null!;
    private TextBox _racesSearchBox = null!;
    private TextBox _classesSearchBox = null!;
    private Label _racesCountLabel = null!;
    private Label _classesCountLabel = null!;
    
    private List<Dictionary<string, object?>>? _racesData;
    private List<Dictionary<string, object?>>? _classesData;
    private System.Data.DataTable? _racesTable;
    private System.Data.DataTable? _classesTable;
    private System.Data.DataView? _racesView;
    private System.Data.DataView? _classesView;
    
    public RacesClassesDialog()
    {
        _gameDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MudProxyViewer",
            "Game Data");
        
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        this.Text = "Races / Classes";
        this.Size = new Size(500, 550);
        this.MinimumSize = new Size(400, 400);
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);
        
        // Tab control
        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9)
        };
        
        // Style the tab control for dark theme
        _tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
        _tabControl.DrawItem += TabControl_DrawItem;
        
        // Create tabs
        var racesTab = new TabPage("Races") { BackColor = Color.FromArgb(45, 45, 45) };
        var classesTab = new TabPage("Classes") { BackColor = Color.FromArgb(45, 45, 45) };
        
        // Build Races tab content
        BuildRacesTab(racesTab);
        
        // Build Classes tab content
        BuildClassesTab(classesTab);
        
        _tabControl.TabPages.Add(racesTab);
        _tabControl.TabPages.Add(classesTab);
        
        this.Controls.Add(_tabControl);
        
        // Load data when shown
        this.Shown += async (s, e) => await LoadDataAsync();
    }
    
    private void TabControl_DrawItem(object? sender, DrawItemEventArgs e)
    {
        var tabPage = _tabControl.TabPages[e.Index];
        var tabBounds = _tabControl.GetTabRect(e.Index);
        
        // Background
        var bgColor = e.State == DrawItemState.Selected 
            ? Color.FromArgb(45, 45, 45) 
            : Color.FromArgb(35, 35, 35);
        using var bgBrush = new SolidBrush(bgColor);
        e.Graphics.FillRectangle(bgBrush, tabBounds);
        
        // Text
        var textColor = e.State == DrawItemState.Selected ? Color.White : Color.LightGray;
        TextRenderer.DrawText(e.Graphics, tabPage.Text, e.Font, tabBounds, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
    
    private void BuildRacesTab(TabPage tab)
    {
        var container = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        
        // Search panel
        var searchPanel = new Panel { Dock = DockStyle.Top, Height = 35 };
        
        var searchLabel = new Label
        {
            Text = "Search:",
            Location = new Point(0, 8),
            AutoSize = true,
            ForeColor = Color.White
        };
        searchPanel.Controls.Add(searchLabel);
        
        _racesSearchBox = new TextBox
        {
            Location = new Point(55, 5),
            Width = 200,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        _racesSearchBox.TextChanged += RacesSearchBox_TextChanged;
        searchPanel.Controls.Add(_racesSearchBox);
        
        _racesCountLabel = new Label
        {
            Location = new Point(270, 8),
            AutoSize = true,
            ForeColor = Color.LightGray
        };
        searchPanel.Controls.Add(_racesCountLabel);
        
        container.Controls.Add(searchPanel);
        
        // Button panel
        var buttonPanel = new Panel { Dock = DockStyle.Bottom, Height = 45 };
        
        var viewButton = new Button
        {
            Text = "View",
            Width = 80,
            Height = 30,
            Location = new Point(0, 8),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        viewButton.Click += RacesViewButton_Click;
        buttonPanel.Controls.Add(viewButton);
        
        var closeButton = new Button
        {
            Text = "Close",
            Width = 80,
            Height = 30,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        closeButton.Location = new Point(container.ClientSize.Width - 100, 8);
        buttonPanel.Controls.Add(closeButton);
        
        container.Controls.Add(buttonPanel);
        
        // Data grid
        _racesGrid = CreateDataGrid();
        _racesGrid.CellDoubleClick += RacesGrid_CellDoubleClick;
        
        var gridPanel = new Panel { Dock = DockStyle.Fill };
        gridPanel.Controls.Add(_racesGrid);
        container.Controls.Add(gridPanel);
        
        tab.Controls.Add(container);
    }
    
    private void BuildClassesTab(TabPage tab)
    {
        var container = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        
        // Search panel
        var searchPanel = new Panel { Dock = DockStyle.Top, Height = 35 };
        
        var searchLabel = new Label
        {
            Text = "Search:",
            Location = new Point(0, 8),
            AutoSize = true,
            ForeColor = Color.White
        };
        searchPanel.Controls.Add(searchLabel);
        
        _classesSearchBox = new TextBox
        {
            Location = new Point(55, 5),
            Width = 200,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        _classesSearchBox.TextChanged += ClassesSearchBox_TextChanged;
        searchPanel.Controls.Add(_classesSearchBox);
        
        _classesCountLabel = new Label
        {
            Location = new Point(270, 8),
            AutoSize = true,
            ForeColor = Color.LightGray
        };
        searchPanel.Controls.Add(_classesCountLabel);
        
        container.Controls.Add(searchPanel);
        
        // Button panel
        var buttonPanel = new Panel { Dock = DockStyle.Bottom, Height = 45 };
        
        var viewButton = new Button
        {
            Text = "View",
            Width = 80,
            Height = 30,
            Location = new Point(0, 8),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        viewButton.Click += ClassesViewButton_Click;
        buttonPanel.Controls.Add(viewButton);
        
        var closeButton = new Button
        {
            Text = "Close",
            Width = 80,
            Height = 30,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        closeButton.Location = new Point(container.ClientSize.Width - 100, 8);
        buttonPanel.Controls.Add(closeButton);
        
        container.Controls.Add(buttonPanel);
        
        // Data grid
        _classesGrid = CreateDataGrid();
        _classesGrid.CellDoubleClick += ClassesGrid_CellDoubleClick;
        
        var gridPanel = new Panel { Dock = DockStyle.Fill };
        gridPanel.Controls.Add(_classesGrid);
        container.Controls.Add(gridPanel);
        
        tab.Controls.Add(container);
    }
    
    private DataGridView CreateDataGrid()
    {
        return new DataGridView
        {
            Dock = DockStyle.Fill,
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
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeight = 28,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            RowTemplate = { Height = 24 },
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleLeft
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.White,
                SelectionBackColor = Color.FromArgb(70, 130, 180),
                SelectionForeColor = Color.White
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                SelectionBackColor = Color.FromArgb(70, 130, 180),
                SelectionForeColor = Color.White
            }
        };
    }
    
    private async Task LoadDataAsync()
    {
        // Load Races
        var racesPath = Path.Combine(_gameDataPath, "Races.json");
        if (File.Exists(racesPath))
        {
            try
            {
                _racesData = await LoadJsonAsync(racesPath);
                if (_racesData != null && _racesData.Count > 0)
                {
                    _racesTable = CreateSimpleTable(_racesData, "Number", "Name");
                    _racesView = new System.Data.DataView(_racesTable);
                    _racesView.Sort = "[Number] ASC";
                    _racesGrid.DataSource = _racesView;
                    
                    // Set column widths after binding
                    _racesGrid.DataBindingComplete += (s, e) =>
                    {
                        if (_racesGrid.Columns.Count >= 1 && _racesGrid.Columns["Number"] != null)
                        {
                            _racesGrid.Columns["Number"].Width = 80;
                            _racesGrid.Columns["Number"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                        }
                    };
                    
                    UpdateRacesCount();
                }
            }
            catch (Exception ex)
            {
                _racesCountLabel.Text = $"Error: {ex.Message}";
            }
        }
        else
        {
            _racesCountLabel.Text = "No data (import game database first)";
        }
        
        // Load Classes
        var classesPath = Path.Combine(_gameDataPath, "Classes.json");
        if (File.Exists(classesPath))
        {
            try
            {
                _classesData = await LoadJsonAsync(classesPath);
                if (_classesData != null && _classesData.Count > 0)
                {
                    _classesTable = CreateSimpleTable(_classesData, "Number", "Name");
                    _classesView = new System.Data.DataView(_classesTable);
                    _classesView.Sort = "[Number] ASC";
                    _classesGrid.DataSource = _classesView;
                    
                    // Set column widths after binding
                    _classesGrid.DataBindingComplete += (s, e) =>
                    {
                        if (_classesGrid.Columns.Count >= 1 && _classesGrid.Columns["Number"] != null)
                        {
                            _classesGrid.Columns["Number"].Width = 80;
                            _classesGrid.Columns["Number"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                        }
                    };
                    
                    UpdateClassesCount();
                }
            }
            catch (Exception ex)
            {
                _classesCountLabel.Text = $"Error: {ex.Message}";
            }
        }
        else
        {
            _classesCountLabel.Text = "No data (import game database first)";
        }
    }
    
    private async Task<List<Dictionary<string, object?>>?> LoadJsonAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
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
                dict[prop.Name] = GetJsonValue(prop.Value);
            }
            result.Add(dict);
        }
        return result;
    }
    
    private static object? GetJsonValue(JsonElement element)
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
    
    private System.Data.DataTable CreateSimpleTable(List<Dictionary<string, object?>> data, params string[] columns)
    {
        var dt = new System.Data.DataTable();
        
        foreach (var col in columns)
        {
            // Detect type from first row
            Type colType = typeof(string);
            if (data.Count > 0 && data[0].TryGetValue(col, out var val) && val != null)
            {
                if (val is long or int or short or byte)
                    colType = typeof(long);
                else if (val is decimal or double or float)
                    colType = typeof(decimal);
            }
            dt.Columns.Add(col, colType);
        }
        
        foreach (var row in data)
        {
            var values = new object[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                if (row.TryGetValue(columns[i], out var val) && val != null)
                {
                    values[i] = val;
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
    
    private void UpdateRacesCount()
    {
        if (_racesView != null && _racesTable != null)
        {
            _racesCountLabel.Text = $"{_racesView.Count} of {_racesTable.Rows.Count} records";
        }
    }
    
    private void UpdateClassesCount()
    {
        if (_classesView != null && _classesTable != null)
        {
            _classesCountLabel.Text = $"{_classesView.Count} of {_classesTable.Rows.Count} records";
        }
    }
    
    private void RacesSearchBox_TextChanged(object? sender, EventArgs e)
    {
        if (_racesView == null || _racesTable == null) return;
        
        var search = _racesSearchBox.Text.Trim();
        if (string.IsNullOrEmpty(search))
        {
            _racesView.RowFilter = "";
        }
        else
        {
            var escaped = search.Replace("'", "''");
            _racesView.RowFilter = $"CONVERT([Number], 'System.String') LIKE '%{escaped}%' OR [Name] LIKE '%{escaped}%'";
        }
        UpdateRacesCount();
    }
    
    private void ClassesSearchBox_TextChanged(object? sender, EventArgs e)
    {
        if (_classesView == null || _classesTable == null) return;
        
        var search = _classesSearchBox.Text.Trim();
        if (string.IsNullOrEmpty(search))
        {
            _classesView.RowFilter = "";
        }
        else
        {
            var escaped = search.Replace("'", "''");
            _classesView.RowFilter = $"CONVERT([Number], 'System.String') LIKE '%{escaped}%' OR [Name] LIKE '%{escaped}%'";
        }
        UpdateClassesCount();
    }
    
    private void RacesViewButton_Click(object? sender, EventArgs e)
    {
        OpenSelectedRace();
    }
    
    private void RacesGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0)
        {
            OpenSelectedRace();
        }
    }
    
    private void OpenSelectedRace()
    {
        if (_racesGrid.SelectedRows.Count == 0 || _racesData == null) return;
        
        var selectedRow = _racesGrid.SelectedRows[0];
        var number = selectedRow.Cells["Number"].Value;
        
        // Find the full data for this race
        var raceData = _racesData.FirstOrDefault(r =>
            r.TryGetValue("Number", out var n) && n?.ToString() == number?.ToString());
        
        if (raceData != null)
        {
            using var detailDialog = new RaceDetailDialog(raceData);
            detailDialog.ShowDialog(this);
        }
    }
    
    private void ClassesViewButton_Click(object? sender, EventArgs e)
    {
        OpenSelectedClass();
    }
    
    private void ClassesGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0)
        {
            OpenSelectedClass();
        }
    }
    
    private void OpenSelectedClass()
    {
        if (_classesGrid.SelectedRows.Count == 0 || _classesData == null) return;
        
        var selectedRow = _classesGrid.SelectedRows[0];
        var number = selectedRow.Cells["Number"].Value;
        
        // Find the full data for this class
        var classData = _classesData.FirstOrDefault(c =>
            c.TryGetValue("Number", out var n) && n?.ToString() == number?.ToString());
        
        if (classData != null)
        {
            using var detailDialog = new ClassDetailDialog(classData);
            detailDialog.ShowDialog(this);
        }
    }
}

/// <summary>
/// Detail dialog for viewing a single Race
/// </summary>
public class RaceDetailDialog : Form
{
    private readonly Dictionary<string, object?> _data;
    
    // Known attribute fields
    private static readonly string[] AttributeFields = { "STR", "INT", "WIL", "AGL", "HEA", "CHM" };
    private static readonly string[] AttributeLabels = { "Strength", "Intellect", "Willpower", "Agility", "Health", "Charm" };
    
    // Known detail fields (to exclude from Abilities)
    private static readonly HashSet<string> DetailFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Number", "Name", "HPPerLVL", "ExpTable",
        "mSTR", "xSTR", "mINT", "xINT", "mWIL", "xWIL",
        "mAGL", "xAGL", "mHEA", "xHEA", "mCHM", "xCHM"
    };
    
    public RaceDetailDialog(Dictionary<string, object?> data)
    {
        _data = data;
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        var name = _data.GetValueOrDefault("Name")?.ToString() ?? "Unknown";
        
        this.Text = $"Race Details - {name}";
        this.Size = new Size(600, 450);
        this.MinimumSize = new Size(500, 400);
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        
        // Main content panel
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(15)
        };
        
        // Left column (Race Details + Attributes)
        var leftColumn = new Panel
        {
            Location = new Point(15, 15),
            Size = new Size(260, 340)
        };
        
        // Race Details section
        var detailsSection = CreateSection("Race Details", 0, 0, 260, 130);
        AddDetailField(detailsSection, "Number", GetValue("Number"), 0);
        AddDetailField(detailsSection, "Name", GetValue("Name"), 1);
        AddDetailField(detailsSection, "Bonus HP", GetValue("HPPerLVL"), 2);
        AddDetailField(detailsSection, "Experience", GetValue("ExpTable") + "%", 3);
        leftColumn.Controls.Add(detailsSection);
        
        // Attributes section
        var attrSection = CreateSection("Attributes (CPs)", 0, 140, 260, 200);
        AddAttributeHeader(attrSection);
        for (int i = 0; i < AttributeFields.Length; i++)
        {
            var field = AttributeFields[i];
            var minVal = GetValue($"m{field}");
            var maxVal = GetValue($"x{field}");
            AddAttributeRow(attrSection, AttributeLabels[i], minVal, maxVal, i + 1);
        }
        leftColumn.Controls.Add(attrSection);
        
        contentPanel.Controls.Add(leftColumn);
        
        // Right column (Abilities)
        var abilitiesSection = CreateSection("Abilities", 285, 15, 280, 340);
        var abilitiesContent = GetContentPanel(abilitiesSection);
        
        // Add all remaining fields
        int row = 0;
        foreach (var kvp in _data)
        {
            if (!DetailFields.Contains(kvp.Key) && kvp.Value != null && abilitiesContent != null)
            {
                var label = new Label
                {
                    Text = $"{kvp.Key}: {kvp.Value}",
                    Location = new Point(5, 5 + (row * 22)),
                    AutoSize = true,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9)
                };
                abilitiesContent.Controls.Add(label);
                row++;
            }
        }
        
        contentPanel.Controls.Add(abilitiesSection);
        
        this.Controls.Add(contentPanel);
        
        // Button panel
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = Color.FromArgb(40, 40, 40)
        };
        
        var closeButton = new Button
        {
            Text = "Close",
            Width = 80,
            Height = 30,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        closeButton.Location = new Point(buttonPanel.Width - 95, 10);
        buttonPanel.Controls.Add(closeButton);
        
        this.Controls.Add(buttonPanel);
        this.AcceptButton = closeButton;
    }
    
    private string GetValue(string key)
    {
        if (_data.TryGetValue(key, out var val) && val != null)
            return val.ToString() ?? "";
        return "";
    }
    
    private Panel CreateSection(string title, int x, int y, int width, int height)
    {
        var section = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(width, height),
            BackColor = Color.FromArgb(35, 35, 35),
            BorderStyle = BorderStyle.FixedSingle
        };
        
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 25,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)
        };
        section.Controls.Add(titleLabel);
        
        var contentPanel = new Panel
        {
            Name = "ContentPanel",  // Name it so we can find it
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            AutoScroll = true
        };
        section.Controls.Add(contentPanel);
        
        // Ensure content is below title
        contentPanel.BringToFront();
        
        return section;
    }
    
    /// <summary>
    /// Find the content panel inside a section panel
    /// </summary>
    private Panel? GetContentPanel(Panel section)
    {
        foreach (Control ctrl in section.Controls)
        {
            if (ctrl is Panel panel && panel.Name == "ContentPanel")
                return panel;
        }
        // Fallback: find any panel that isn't docked to top
        foreach (Control ctrl in section.Controls)
        {
            if (ctrl is Panel panel && panel.Dock == DockStyle.Fill)
                return panel;
        }
        return null;
    }
    
    private void AddDetailField(Panel section, string label, string value, int row)
    {
        var contentPanel = GetContentPanel(section);
        if (contentPanel == null) return;
        
        int yPos = 8 + (row * 28);
        
        var labelCtrl = new Label
        {
            Text = label,
            Location = new Point(5, yPos + 3),
            Size = new Size(80, 20),
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 9)
        };
        contentPanel.Controls.Add(labelCtrl);
        
        var valueCtrl = new TextBox
        {
            Text = value,
            Location = new Point(90, yPos),
            Size = new Size(150, 23),
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true
        };
        contentPanel.Controls.Add(valueCtrl);
    }
    
    private void AddAttributeHeader(Panel section)
    {
        var contentPanel = GetContentPanel(section);
        if (contentPanel == null) return;
        
        var minLabel = new Label
        {
            Text = "Min",
            Location = new Point(95, 8),
            Size = new Size(50, 20),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };
        contentPanel.Controls.Add(minLabel);
        
        var maxLabel = new Label
        {
            Text = "Max",
            Location = new Point(155, 8),
            Size = new Size(50, 20),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };
        contentPanel.Controls.Add(maxLabel);
    }
    
    private void AddAttributeRow(Panel section, string label, string minVal, string maxVal, int row)
    {
        var contentPanel = GetContentPanel(section);
        if (contentPanel == null) return;
        
        int yPos = 8 + (row * 26);
        
        var labelCtrl = new Label
        {
            Text = label,
            Location = new Point(5, yPos + 3),
            Size = new Size(80, 20),
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 9)
        };
        contentPanel.Controls.Add(labelCtrl);
        
        var minCtrl = new TextBox
        {
            Text = minVal,
            Location = new Point(95, yPos),
            Size = new Size(50, 23),
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true,
            TextAlign = HorizontalAlignment.Center
        };
        contentPanel.Controls.Add(minCtrl);
        
        var maxCtrl = new TextBox
        {
            Text = maxVal,
            Location = new Point(155, yPos),
            Size = new Size(50, 23),
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true,
            TextAlign = HorizontalAlignment.Center
        };
        contentPanel.Controls.Add(maxCtrl);
    }
}

/// <summary>
/// Detail dialog for viewing a single Class (placeholder - can be expanded later)
/// </summary>
public class ClassDetailDialog : Form
{
    private readonly Dictionary<string, object?> _data;
    
    public ClassDetailDialog(Dictionary<string, object?> data)
    {
        _data = data;
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        var name = _data.GetValueOrDefault("Name")?.ToString() ?? "Unknown";
        
        this.Text = $"Class Details - {name}";
        this.Size = new Size(600, 500);
        this.MinimumSize = new Size(500, 400);
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        
        // Content panel with scroll
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(15)
        };
        
        // Display all fields
        int row = 0;
        foreach (var kvp in _data)
        {
            if (kvp.Value != null)
            {
                var label = new Label
                {
                    Text = $"{kvp.Key}:",
                    Location = new Point(15, 15 + (row * 28)),
                    Size = new Size(150, 20),
                    ForeColor = Color.LightGray,
                    Font = new Font("Segoe UI", 9)
                };
                contentPanel.Controls.Add(label);
                
                var valueBox = new TextBox
                {
                    Text = kvp.Value.ToString(),
                    Location = new Point(170, 12 + (row * 28)),
                    Size = new Size(380, 23),
                    BackColor = Color.FromArgb(50, 50, 50),
                    ForeColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle,
                    ReadOnly = true
                };
                contentPanel.Controls.Add(valueBox);
                row++;
            }
        }
        
        this.Controls.Add(contentPanel);
        
        // Button panel
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = Color.FromArgb(40, 40, 40)
        };
        
        var closeButton = new Button
        {
            Text = "Close",
            Width = 80,
            Height = 30,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        closeButton.Location = new Point(buttonPanel.Width - 95, 10);
        buttonPanel.Controls.Add(closeButton);
        
        this.Controls.Add(buttonPanel);
        this.AcceptButton = closeButton;
    }
}
