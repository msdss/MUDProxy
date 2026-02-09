namespace MudProxyViewer;

/// <summary>
/// Configuration for Items table in the GameDataViewerDialog.
/// </summary>
public static class ItemViewerConfig
{
    /// <summary>
    /// Columns to show for Items (only these will be visible).
    /// </summary>
    public static readonly HashSet<string> VisibleColumns = new(StringComparer.OrdinalIgnoreCase)
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
    
    /// <summary>
    /// Fixed width for Number column.
    /// </summary>
    public static int NumberColumnWidth => 80;
}

/// <summary>
/// Detail dialog for viewing a single Item.
/// Left: Item info, Options (placeholder checkboxes), Details
/// Right: Other Info (remaining fields + resolved abilities)
/// </summary>
public class ItemDetailDialog : Form
{
    private readonly Dictionary<string, object?> _data;
    
    // Fields shown in the structured left-side sections (excluded from Other Info)
    private static readonly HashSet<string> DetailFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Number", "Name", "Encum", "Price", "Currency", "ItemType", "Obtained"
    };
    
    public ItemDetailDialog(Dictionary<string, object?> data)
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
        
        this.Text = $"Item Details - {name}";
        this.Size = new Size(640, 545);
        this.MinimumSize = new Size(640, 545);
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
        
        // ════════════════════════════════════════
        // LEFT SIDE — stacked sections
        // ════════════════════════════════════════
        int leftWidth = 295;
        int leftX = 15;
        
        // ── Item section ──
        var itemSection = CreateSection("Item", leftX, 15, leftWidth, 80);
        var itemContent = GetContentPanel(itemSection);
        if (itemContent != null)
        {
            AddLabelPair(itemContent, "Number", GetValue("Number"), 0);
            AddLabelPair(itemContent, "Name", GetValue("Name"), 1);
        }
        contentPanel.Controls.Add(itemSection);
        
        // ── Options section ──
        var optionsSection = CreateSection("Options", leftX, 103, leftWidth, 195);
        var optionsContent = GetContentPanel(optionsSection);
        if (optionsContent != null)
        {
            // Left column checkboxes
            string[] leftChecks = { "Auto-collect", "Auto-discard", "Auto-equip", "Auto-find", "Auto-open", "Auto-buy", "Auto-sell" };
            for (int i = 0; i < leftChecks.Length; i++)
            {
                var cb = new CheckBox
                {
                    Text = leftChecks[i],
                    Location = new Point(5, 5 + (i * 22)),
                    AutoSize = true,
                    ForeColor = Color.White,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 8.5f),
                    Checked = false
                };
                optionsContent.Controls.Add(cb);
            }
            
            // Right column checkboxes
            string[] rightChecks = { "Cannot be taken", "Can use to backstab", "Must have minimum", "Loyal item" };
            for (int i = 0; i < rightChecks.Length; i++)
            {
                var cb = new CheckBox
                {
                    Text = rightChecks[i],
                    Location = new Point(145, 5 + (i * 22)),
                    AutoSize = true,
                    ForeColor = Color.White,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 8.5f),
                    Checked = false
                };
                optionsContent.Controls.Add(cb);
            }
        }
        contentPanel.Controls.Add(optionsSection);
        
        // ── Details section ──
        var detailsSection = CreateSection("Details", leftX, 306, leftWidth, 130);
        var detailsContent = GetContentPanel(detailsSection);
        if (detailsContent != null)
        {
            // Row 0: Min. to keep [NumericTextBox]    Weight [Encum]
            AddLabel(detailsContent, "Min. to keep", 5, 5);
            var minToKeepBox = new TextBox
            {
                Location = new Point(90, 3),
                Width = 35,
                Text = "0",
                MaxLength = 2,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9)
            };
            minToKeepBox.KeyPress += NumericTextBox_KeyPress;
            detailsContent.Controls.Add(minToKeepBox);
            AddLabelPair(detailsContent, "Weight", GetValue("Encum"), 0, 50, 150);
            
            // Row 1: Max to get [NumericTextBox]    Price [Price Currency]
            AddLabel(detailsContent, "Max to get", 5, 5 + 24);
            var maxToGetBox = new TextBox
            {
                Location = new Point(90, 3 + 24),
                Width = 35,
                Text = "0",
                MaxLength = 2,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9)
            };
            maxToGetBox.KeyPress += NumericTextBox_KeyPress;
            detailsContent.Controls.Add(maxToGetBox);
            var priceVal = GetValue("Price");
            var currVal = GetValue("Currency");
            var priceDisplay = string.IsNullOrEmpty(currVal) ? priceVal : $"{priceVal} {currVal}";
            AddLabelPair(detailsContent, "Price", priceDisplay, 1, 50, 150);
            
            // Row 2: Item type [ItemType]
            AddLabelPair(detailsContent, "Item type", GetValue("ItemType"), 2, 85);
            
            // Row 3: If needed, do [____]
            AddLabelPair(detailsContent, "If needed, do", "", 3, 85);
        }
        contentPanel.Controls.Add(detailsSection);
        
        // ════════════════════════════════════════
        // RIGHT SIDE — Other Info
        // ════════════════════════════════════════
        var otherInfoSection = CreateSection("Other Info", 320, 15, 290, 460);
        var otherInfoContent = GetContentPanel(otherInfoSection);
        if (otherInfoContent != null)
        {
            var abilities = AbilityNames.ResolveAbilities(_data);
            
            // Collect non-detail, non-ability fields
            var miscFields = new List<(string Name, string Value)>();
            foreach (var kvp in _data)
            {
                if (kvp.Value == null) continue;
                if (DetailFields.Contains(kvp.Key)) continue;
                if (AbilityNames.IsAbilityColumn(kvp.Key)) continue;
                // Skip "Obtained From" - we'll handle it specially
                if (kvp.Key.Equals("Obtained From", StringComparison.OrdinalIgnoreCase)) continue;
                miscFields.Add((kvp.Key, kvp.Value.ToString() ?? ""));
            }
            
            int row = 0;
            
            foreach (var (fieldName, fieldValue) in miscFields)
            {
                var fLabel = new Label
                {
                    Text = $"{fieldName}:",
                    Location = new Point(5, 5 + (row * 22)),
                    Size = new Size(100, 18),
                    ForeColor = Color.LightGray,
                    Font = new Font("Segoe UI", 9)
                };
                otherInfoContent.Controls.Add(fLabel);
                
                var fValue = new Label
                {
                    Text = fieldValue,
                    Location = new Point(105, 5 + (row * 22)),
                    AutoSize = true,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9)
                };
                otherInfoContent.Controls.Add(fValue);
                row++;
            }
            
            // Handle "Obtained From" field specially - resolve shop/monster/textblock names
            if (_data.TryGetValue("Obtained From", out var obtainedVal) && obtainedVal != null)
            {
                var obtainedStr = obtainedVal.ToString() ?? "";
                var sources = ResolveObtainedFromEntries(obtainedStr);
                
                if (sources.Count > 0)
                {
                    // First source with label
                    var obtainedLabel = new Label
                    {
                        Text = "Obtained From:",
                        Location = new Point(5, 5 + (row * 22)),
                        Size = new Size(100, 18),
                        ForeColor = Color.LightGray,
                        Font = new Font("Segoe UI", 9)
                    };
                    otherInfoContent.Controls.Add(obtainedLabel);
                    
                    var firstSourceLabel = new Label
                    {
                        Text = sources[0],
                        Location = new Point(105, 5 + (row * 22)),
                        AutoSize = true,
                        ForeColor = Color.White,
                        Font = new Font("Segoe UI", 9)
                    };
                    otherInfoContent.Controls.Add(firstSourceLabel);
                    row++;
                    
                    // Additional sources (indented, no label)
                    for (int i = 1; i < sources.Count; i++)
                    {
                        var additionalSourceLabel = new Label
                        {
                            Text = sources[i],
                            Location = new Point(105, 5 + (row * 22)),
                            AutoSize = true,
                            ForeColor = Color.White,
                            Font = new Font("Segoe UI", 9)
                        };
                        otherInfoContent.Controls.Add(additionalSourceLabel);
                        row++;
                    }
                }
            }
            
            // Resolved abilities
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
                otherInfoContent.Controls.Add(nameLabel);
                
                var valueLabel = new Label
                {
                    Text = abilValue,
                    Location = new Point(170, 5 + (row * 22)),
                    AutoSize = true,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9)
                };
                otherInfoContent.Controls.Add(valueLabel);
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
                otherInfoContent.Controls.Add(noneLabel);
            }
        }
        contentPanel.Controls.Add(otherInfoSection);
        
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
    
    /// <summary>
    /// Resolve all "Obtained From" entries to their actual names.
    /// Handles Shop #N, Monster #N, and Textblock #N formats.
    /// Returns a list of resolved names with percentages if present.
    /// </summary>
    private static List<string> ResolveObtainedFromEntries(string obtainedValue)
    {
        var result = new List<string>();
        
        if (string.IsNullOrEmpty(obtainedValue))
            return result;
        
        // Split by comma
        var parts = obtainedValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            
            // Extract percentage if present (e.g., "(5%)")
            string percentage = "";
            var parenIndex = trimmed.IndexOf('(');
            if (parenIndex > 0)
            {
                percentage = trimmed.Substring(parenIndex); // Keep "(5%)"
                trimmed = trimmed.Substring(0, parenIndex).Trim();
            }
            
            string resolvedName = "";
            
            if (trimmed.StartsWith("Shop #", StringComparison.OrdinalIgnoreCase))
            {
                var numStr = trimmed.Substring(6).Trim();
                if (int.TryParse(numStr, out var num) && num != 0)
                {
                    resolvedName = ResolveName("Shops", num);
                    if (string.IsNullOrEmpty(resolvedName))
                        resolvedName = $"Shop #{num}";
                    else
                        resolvedName = $"{resolvedName} (#{num})";
                }
            }
            else if (trimmed.StartsWith("Monster #", StringComparison.OrdinalIgnoreCase))
            {
                var numStr = trimmed.Substring(9).Trim();
                if (int.TryParse(numStr, out var num) && num != 0)
                {
                    resolvedName = ResolveName("Monsters", num);
                    if (string.IsNullOrEmpty(resolvedName))
                        resolvedName = $"Monster #{num}";
                    else
                        resolvedName = $"{resolvedName} (#{num})";
                }
            }
            else if (trimmed.StartsWith("Textblock #", StringComparison.OrdinalIgnoreCase))
            {
                var numStr = trimmed.Substring(11).Trim();
                if (int.TryParse(numStr, out var num) && num != 0)
                {
                    resolvedName = ResolveName("TextBlocks", num);
                    if (string.IsNullOrEmpty(resolvedName))
                        resolvedName = $"Textblock #{num}";
                    else
                        resolvedName = $"{resolvedName} (#{num})";
                }
            }
            else
            {
                // Unknown format, just use as-is
                resolvedName = trimmed;
            }
            
            if (!string.IsNullOrEmpty(resolvedName))
            {
                result.Add(resolvedName + percentage);
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Resolve a number to a name from the specified table.
    /// </summary>
    private static string ResolveName(string tableName, int number)
    {
        var table = GameDataCache.Instance.GetTable(tableName);
        if (table != null)
        {
            var entry = table.FirstOrDefault(e =>
                e.TryGetValue("Number", out var n) &&
                n != null && Convert.ToInt64(n) == number);
            
            if (entry != null && entry.TryGetValue("Name", out var name) && name != null)
            {
                return name.ToString() ?? "";
            }
        }
        
        return "";
    }
    
    /// <summary>
    /// Add a label + value pair at a given row, with optional x offset for side-by-side layout.
    /// </summary>
    private static void AddLabelPair(Panel content, string label, string value, int row, int labelWidth = 80, int xOffset = 0)
    {
        int yPos = 5 + (row * 24);
        
        var labelCtrl = new Label
        {
            Text = label,
            Location = new Point(xOffset + 5, yPos),
            Size = new Size(labelWidth, 18),
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 9)
        };
        content.Controls.Add(labelCtrl);
        
        var valueCtrl = new Label
        {
            Text = value,
            Location = new Point(xOffset + labelWidth + 8, yPos),
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9)
        };
        content.Controls.Add(valueCtrl);
    }
    
    /// <summary>
    /// Add a simple label at the specified position.
    /// </summary>
    private static void AddLabel(Panel content, string text, int x, int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 9)
        };
        content.Controls.Add(label);
    }
    
    /// <summary>
    /// Restricts textbox input to numeric values only (0-9).
    /// </summary>
    private void NumericTextBox_KeyPress(object? sender, KeyPressEventArgs e)
    {
        // Allow control characters (backspace, etc.) and digits only
        if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
        {
            e.Handled = true;
        }
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
