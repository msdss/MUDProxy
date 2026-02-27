namespace MudProxyViewer;

public class BuffListDialog : Form
{
    private readonly BuffManager _buffManager;
    private ListBox _buffListBox = null!;
    private Button _addButton = null!;
    private Button _editButton = null!;
    private Button _deleteButton = null!;
    private Label _detailsLabel = null!;

    public BuffListDialog(BuffManager buffManager)
    {
        _buffManager = buffManager;
        InitializeComponent();
        RefreshBuffList();
    }

    private void InitializeComponent()
    {
        this.Text = "Manage Buff Configurations";
        this.Size = new Size(600, 450);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);

        // Buff list
        var listLabel = new Label
        {
            Text = "Configured Buffs:",
            Location = new Point(15, 15),
            AutoSize = true,
            ForeColor = Color.White
        };
        this.Controls.Add(listLabel);

        _buffListBox = new ListBox
        {
            Location = new Point(15, 40),
            Size = new Size(250, 320),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Consolas", 10)
        };
        _buffListBox.SelectedIndexChanged += BuffListBox_SelectedIndexChanged;
        _buffListBox.DoubleClick += EditButton_Click;
        this.Controls.Add(_buffListBox);

        // Details panel
        var detailsPanel = new Panel
        {
            Location = new Point(280, 40),
            Size = new Size(290, 320),
            BackColor = Color.FromArgb(35, 35, 35)
        };

        var detailsHeaderLabel = new Label
        {
            Text = "Details:",
            Location = new Point(10, 10),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        detailsPanel.Controls.Add(detailsHeaderLabel);

        _detailsLabel = new Label
        {
            Location = new Point(10, 35),
            Size = new Size(270, 275),
            ForeColor = Color.White,
            Font = new Font("Consolas", 9),
            Text = "(Select a buff to view details)"
        };
        detailsPanel.Controls.Add(_detailsLabel);

        this.Controls.Add(detailsPanel);

        // Buttons
        _addButton = new Button
        {
            Text = "Add New",
            Location = new Point(15, 370),
            Size = new Size(80, 30),
            BackColor = Color.FromArgb(0, 120, 0),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _addButton.Click += AddButton_Click;
        this.Controls.Add(_addButton);

        _editButton = new Button
        {
            Text = "Edit",
            Location = new Point(100, 370),
            Size = new Size(80, 30),
            BackColor = Color.FromArgb(80, 80, 80),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Enabled = false
        };
        _editButton.Click += EditButton_Click;
        this.Controls.Add(_editButton);

        _deleteButton = new Button
        {
            Text = "Delete",
            Location = new Point(185, 370),
            Size = new Size(80, 30),
            BackColor = Color.FromArgb(150, 0, 0),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Enabled = false
        };
        _deleteButton.Click += DeleteButton_Click;
        this.Controls.Add(_deleteButton);

        var closeButton = new Button
        {
            Text = "Close",
            Location = new Point(490, 370),
            Size = new Size(80, 30),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        this.Controls.Add(closeButton);

        this.AcceptButton = closeButton;
    }

    private void RefreshBuffList()
    {
        _buffListBox.Items.Clear();
        foreach (var buff in _buffManager.BuffConfigurations)
        {
            _buffListBox.Items.Add(new BuffListItem(buff));
        }
        UpdateButtonStates();
    }

    private void BuffListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        UpdateButtonStates();
        UpdateDetails();
    }

    private void UpdateButtonStates()
    {
        var hasSelection = _buffListBox.SelectedItem != null;
        _editButton.Enabled = hasSelection;
        _deleteButton.Enabled = hasSelection;
    }

    private void UpdateDetails()
    {
        if (_buffListBox.SelectedItem is BuffListItem item)
        {
            var b = item.Buff;
            _detailsLabel.Text = 
                $"Name: {b.DisplayName}\n" +
                $"Command: {b.Command}\n" +
                $"Duration: {b.DurationSeconds}s ({b.DurationSeconds / 60}m {b.DurationSeconds % 60}s)\n" +
                $"Category: {b.Category}\n" +
                $"Target: {FormatTargetType(b.TargetType)}\n\n" +
                $"Self Cast:\n  \"{b.SelfCastMessage}\"\n\n" +
                (b.TargetType != BuffTargetType.SelfOnly 
                    ? $"Party Cast:\n  \"{b.PartyCastMessage}\"\n\n" 
                    : "") +
                $"Expire:\n  \"{b.ExpireMessage}\"";
        }
        else
        {
            _detailsLabel.Text = "(Select a buff to view details)";
        }
    }

    private string FormatTargetType(BuffTargetType type) => type switch
    {
        BuffTargetType.SelfOnly => "Self Only",
        BuffTargetType.MeleeParty => "Melee Party Members",
        BuffTargetType.CasterParty => "Caster Party Members",
        BuffTargetType.AllParty => "All Party Members",
        _ => type.ToString()
    };

    private void AddButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new BuffConfigDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _buffManager.AddBuffConfiguration(dialog.Buff);
            RefreshBuffList();
            
            // Select the new item
            for (int i = 0; i < _buffListBox.Items.Count; i++)
            {
                if (_buffListBox.Items[i] is BuffListItem item && item.Buff.Id == dialog.Buff.Id)
                {
                    _buffListBox.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    private void EditButton_Click(object? sender, EventArgs e)
    {
        if (_buffListBox.SelectedItem is BuffListItem item)
        {
            using var dialog = new BuffConfigDialog(item.Buff);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _buffManager.UpdateBuffConfiguration(dialog.Buff);
                RefreshBuffList();
            }
        }
    }

    private void DeleteButton_Click(object? sender, EventArgs e)
    {
        if (_buffListBox.SelectedItem is BuffListItem item)
        {
            var result = MessageBox.Show(
                $"Delete buff configuration '{item.Buff.DisplayName}'?",
                "Confirm Delete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _buffManager.RemoveBuffConfiguration(item.Buff.Id);
                RefreshBuffList();
            }
        }
    }

    private class BuffListItem
    {
        public BuffConfiguration Buff { get; }

        public BuffListItem(BuffConfiguration buff)
        {
            Buff = buff;
        }

        public override string ToString()
        {
            var categoryIcon = Buff.Category switch
            {
                BuffCategory.Combat => "⚔️",
                BuffCategory.Defense => "🛡️",
                BuffCategory.Utility => "✨",
                _ => "•"
            };
            return $"{categoryIcon} {Buff.DisplayName} ({Buff.Command})";
        }
    }
}
