namespace MudProxyViewer;

/// <summary>
/// Configuration for Classes table in the GameDataViewerDialog.
/// </summary>
public static class ClassViewerConfig
{
    /// <summary>
    /// Columns to show for Classes (only these will be visible).
    /// </summary>
    public static readonly HashSet<string> VisibleColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Number", "Name", "ExpTable"
    };
    
    /// <summary>
    /// Column aliases (maps DataPropertyName -> Header text)
    /// </summary>
    public static readonly Dictionary<string, string> ColumnAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "ExpTable", "Exp %" }
    };
    
    /// <summary>
    /// Whether to show the search bar for this table.
    /// </summary>
    public static bool ShowSearchBar => false;
    
    /// <summary>
    /// Whether to use narrow column widths.
    /// </summary>
    public static bool UseNarrowWidths => true;
    
    /// <summary>
    /// Whether the last visible column should fill remaining space.
    /// </summary>
    public static bool LastColumnFills => true;
}

/// <summary>
/// Detail dialog for viewing a single Class.
/// </summary>
public class ClassDetailDialog : Form
{
    private readonly Dictionary<string, object?> _data;
    
    private static readonly HashSet<string> DetailFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Number", "Name", "ExpTable", "CombatLVL", "MinHits", "MaxHits",
        "WeaponType", "ArmourType", "MageryType", "MageryLVL"
    };
    
    public ClassDetailDialog(Dictionary<string, object?> data)
    {
        _data = data;
        InitializeComponent();
    }
    
    private string GetValue(string key)
    {
        if (_data.TryGetValue(key, out var val) && val != null)
            return val.ToString() ?? "";
        return "";
    }
    
    private void InitializeComponent()
    {
        var name = _data.GetValueOrDefault("Name")?.ToString() ?? "Unknown";
        
        this.Text = $"Class Details - {name}";
        this.Size = new Size(600, 370);
        this.MinimumSize = new Size(600, 370);
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
        
        var detailsSection = CreateSection("Class Details", 15, 15, 260, 265);
        var detailsContent = GetContentPanel(detailsSection);
        
        if (detailsContent != null)
        {
            var mageryType = GetValue("MageryType");
            var mageryLvl = GetValue("MageryLVL");
            var magicDisplay = $"{mageryType}-{mageryLvl}";
            if (string.IsNullOrEmpty(mageryType) && string.IsNullOrEmpty(mageryLvl))
                magicDisplay = "";
            
            var minHits = GetValue("MinHits");
            var maxHits = GetValue("MaxHits");
            var hpDisplay = $"{minHits} - {maxHits}";
            
            var fields = new (string Label, string Value)[]
            {
                ("Number", GetValue("Number")),
                ("Name", GetValue("Name")),
                ("Experience", GetValue("ExpTable") + "%"),
                ("Combat", GetValue("CombatLVL")),
                ("HPs/Level", hpDisplay),
                ("Weapons", GetValue("WeaponType")),
                ("Armour", GetValue("ArmourType")),
                ("Magic", magicDisplay)
            };
            
            for (int i = 0; i < fields.Length; i++)
            {
                int yPos = 8 + (i * 26);
                
                var labelCtrl = new Label
                {
                    Text = fields[i].Label,
                    Location = new Point(5, yPos + 2),
                    Size = new Size(80, 18),
                    ForeColor = Color.LightGray,
                    Font = new Font("Segoe UI", 9)
                };
                detailsContent.Controls.Add(labelCtrl);
                
                var valueCtrl = new Label
                {
                    Text = fields[i].Value,
                    Location = new Point(90, yPos + 2),
                    AutoSize = true,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9)
                };
                detailsContent.Controls.Add(valueCtrl);
            }
        }
        
        contentPanel.Controls.Add(detailsSection);
        
        var abilitiesSection = CreateSection("Abilities", 285, 15, 280, 265);
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
}
