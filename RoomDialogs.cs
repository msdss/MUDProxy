namespace MudProxyViewer;

/// <summary>
/// Configuration for Rooms table in the GameDataViewerDialog.
/// </summary>
public static class RoomViewerConfig
{
    /// <summary>
    /// Columns to show for Rooms (null = show all columns).
    /// </summary>
    public static readonly HashSet<string>? VisibleColumns = null;
    
    /// <summary>
    /// Whether to show the search bar for this table.
    /// </summary>
    public static bool ShowSearchBar => true;
}

/// <summary>
/// Detail dialog for viewing a single Room.
/// TODO: Implement custom layout for room details (exits, items, monsters, etc.)
/// </summary>
public class RoomDetailDialog : Form
{
    private readonly Dictionary<string, object?> _data;
    
    public RoomDetailDialog(Dictionary<string, object?> data)
    {
        _data = data;
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        var name = _data.GetValueOrDefault("Name")?.ToString() ?? "Unknown";
        
        this.Text = $"Room Details - {name}";
        this.Size = new Size(600, 500);
        this.MinimumSize = new Size(500, 400);
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        
        // TODO: Implement custom room detail layout
        // Should show: exits, items that spawn here, monsters that spawn here, etc.
        
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(15)
        };
        
        int row = 0;
        
        foreach (var kvp in _data)
        {
            if (kvp.Value == null) continue;
            if (AbilityNames.IsAbilityColumn(kvp.Key)) continue;
            
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
}
