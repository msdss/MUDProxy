namespace MudProxyViewer;

/// <summary>
/// Configuration for Races table in the GameDataViewerDialog.
/// </summary>
public static class RaceViewerConfig
{
    /// <summary>
    /// Column aliases for Races display (maps DataPropertyName -> Header text)
    /// </summary>
    public static readonly Dictionary<string, string> ColumnAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "mSTR", "Min STR" }, { "xSTR", "Max STR" },
        { "mINT", "Min INT" }, { "xINT", "Max INT" },
        { "mWIL", "Min WIL" }, { "xWIL", "Max WIL" },
        { "mAGL", "Min AGL" }, { "xAGL", "Max AGL" },
        { "mHEA", "Min HEA" }, { "xHEA", "Max HEA" },
        { "mCHM", "Min CHM" }, { "xCHM", "Max CHM" },
        { "ExpTable", "Exp %" }
    };
    
    /// <summary>
    /// Columns to hide for Races (HPPerLVL + all Abil/AbilVal columns handled separately)
    /// </summary>
    public static readonly HashSet<string> HiddenColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "HPPerLVL"
    };
    
    /// <summary>
    /// Whether to show the search bar for this table.
    /// </summary>
    public static bool ShowSearchBar => false;
    
    /// <summary>
    /// Whether to use narrow column widths.
    /// </summary>
    public static bool UseNarrowWidths => true;
}

/// <summary>
/// Detail dialog for viewing a single Race.
/// </summary>
public class RaceDetailDialog : Form
{
    private readonly Dictionary<string, object?> _data;
    
    private static readonly string[] AttributeFields = { "STR", "INT", "WIL", "AGL", "HEA", "CHM" };
    private static readonly string[] AttributeLabels = { "Strength", "Intellect", "Willpower", "Agility", "Health", "Charm" };
    
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
        this.Size = new Size(600, 510);
        this.MinimumSize = new Size(500, 510);
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(15)
        };
        
        var leftColumn = new Panel
        {
            Location = new Point(15, 15),
            Size = new Size(260, 400)
        };
        
        var detailsSection = CreateSection("Race Details", 0, 0, 260, 155);
        AddDetailField(detailsSection, "Number", GetValue("Number"), 0);
        AddDetailField(detailsSection, "Name", GetValue("Name"), 1);
        AddDetailField(detailsSection, "Bonus HP", GetValue("HPPerLVL"), 2);
        AddDetailField(detailsSection, "Experience", GetValue("ExpTable") + "%", 3);
        leftColumn.Controls.Add(detailsSection);
        
        var attrSection = CreateSection("Attributes (CPs)", 0, 165, 260, 220);
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
        
        var abilitiesSection = CreateSection("Abilities", 285, 15, 280, 400);
        var abilitiesContent = GetContentPanel(abilitiesSection);
        
        if (abilitiesContent != null)
        {
            var abilities = AbilityNames.ResolveAbilities(_data);
            
            var miscFields = new List<(string Name, string Value)>();
            foreach (var kvp in _data)
            {
                if (kvp.Value == null) continue;
                if (DetailFields.Contains(kvp.Key)) continue;
                if (AbilityNames.IsAbilityColumn(kvp.Key)) continue;
                miscFields.Add((kvp.Key, kvp.Value.ToString() ?? ""));
            }
            
            int row = 0;
            
            foreach (var (fieldName, fieldValue) in miscFields)
            {
                var label = new Label
                {
                    Text = $"{fieldName}: {fieldValue}",
                    Location = new Point(5, 5 + (row * 22)),
                    AutoSize = true,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9)
                };
                abilitiesContent.Controls.Add(label);
                row++;
            }
            
            foreach (var (abilName, abilValue) in abilities)
            {
                var nameLabel = new Label
                {
                    Text = $"{abilName}:",
                    Location = new Point(5, 5 + (row * 22)),
                    Size = new Size(160, 18),
                    ForeColor = Color.LightGray,
                    Font = new Font("Segoe UI", 9)
                };
                abilitiesContent.Controls.Add(nameLabel);
                
                var valueLabel = new Label
                {
                    Text = abilValue,
                    Location = new Point(170, 5 + (row * 22)),
                    AutoSize = true,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9)
                };
                abilitiesContent.Controls.Add(valueLabel);
                row++;
            }
            
            if (abilities.Count == 0 && miscFields.Count == 0)
            {
                var noneLabel = new Label
                {
                    Text = "(None)",
                    Location = new Point(5, 5),
                    AutoSize = true,
                    ForeColor = Color.Gray,
                    Font = new Font("Segoe UI", 9)
                };
                abilitiesContent.Controls.Add(noneLabel);
            }
        }
        
        contentPanel.Controls.Add(abilitiesSection);
        
        this.Controls.Add(contentPanel);
        
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
            Name = "ContentPanel",
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            AutoScroll = true
        };
        section.Controls.Add(contentPanel);
        contentPanel.BringToFront();
        
        return section;
    }
    
    private Panel? GetContentPanel(Panel section)
    {
        foreach (Control ctrl in section.Controls)
        {
            if (ctrl is Panel panel && panel.Name == "ContentPanel")
                return panel;
        }
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
