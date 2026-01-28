namespace MudProxyViewer;

public class BuffConfigDialog : Form
{
    private BuffConfiguration _buff;
    private bool _isNew;
    
    private TextBox _nameTextBox = null!;
    private TextBox _commandTextBox = null!;
    private NumericUpDown _durationNumeric = null!;
    private NumericUpDown _manaCostNumeric = null!;
    private ComboBox _categoryComboBox = null!;
    private ComboBox _targetTypeComboBox = null!;
    private TextBox _selfCastMessageTextBox = null!;
    private TextBox _partyCastMessageTextBox = null!;
    private TextBox _expireMessageTextBox = null!;
    
    // Auto-recast controls
    private CheckBox _autoRecastCheckBox = null!;
    private NumericUpDown _recastBufferNumeric = null!;
    private NumericUpDown _priorityNumeric = null!;
    
    public BuffConfiguration Buff => _buff;
    
    public BuffConfigDialog(BuffConfiguration? existingBuff = null)
    {
        _isNew = existingBuff == null;
        _buff = existingBuff?.Clone() ?? new BuffConfiguration();
        
        InitializeComponent();
        LoadBuffData();
    }
    
    private void InitializeComponent()
    {
        this.Text = _isNew ? "Add New Buff" : "Edit Buff";
        this.Size = new Size(500, 700);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);
        
        var y = 15;
        var controlLeft = 145;
        var controlWidth = 320;
        var rowHeight = 32;
        
        // Display Name
        AddLabel("Display Name:", 15, y);
        _nameTextBox = AddTextBox(controlLeft, y, controlWidth);
        y += rowHeight;
        
        // Command
        AddLabel("Command:", 15, y);
        _commandTextBox = AddTextBox(controlLeft, y, 80);
        AddHelpLabel("(4-letter abbreviation)", controlLeft + 90, y);
        y += rowHeight;
        
        // Duration
        AddLabel("Duration (seconds):", 15, y);
        _durationNumeric = new NumericUpDown
        {
            Location = new Point(controlLeft, y),
            Width = 80,
            Minimum = 1,
            Maximum = 9999,
            Value = 60,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        this.Controls.Add(_durationNumeric);
        y += rowHeight;
        
        // Mana Cost
        AddLabel("Mana Cost:", 15, y);
        _manaCostNumeric = new NumericUpDown
        {
            Location = new Point(controlLeft, y),
            Width = 80,
            Minimum = 0,
            Maximum = 9999,
            Value = 0,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        this.Controls.Add(_manaCostNumeric);
        AddHelpLabel("(0 = no cost / unknown)", controlLeft + 90, y);
        y += rowHeight;
        
        // Category
        AddLabel("Category:", 15, y);
        _categoryComboBox = new ComboBox
        {
            Location = new Point(controlLeft, y),
            Width = 150,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        _categoryComboBox.Items.AddRange(Enum.GetNames<BuffCategory>());
        _categoryComboBox.SelectedIndex = 0;
        this.Controls.Add(_categoryComboBox);
        y += rowHeight;
        
        // Target Type
        AddLabel("Target Type:", 15, y);
        _targetTypeComboBox = new ComboBox
        {
            Location = new Point(controlLeft, y),
            Width = 150,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        _targetTypeComboBox.Items.Add("Self Only");
        _targetTypeComboBox.Items.Add("Melee Party");
        _targetTypeComboBox.Items.Add("Caster Party");
        _targetTypeComboBox.Items.Add("All Party");
        _targetTypeComboBox.SelectedIndex = 0;
        _targetTypeComboBox.SelectedIndexChanged += TargetTypeChanged;
        this.Controls.Add(_targetTypeComboBox);
        y += rowHeight + 10;
        
        // Separator - Detection Messages
        AddSeparator("─── Detection Messages (partial match) ───", 15, y);
        y += 25;
        
        // Self Cast Message
        AddLabel("Self Cast Message:", 15, y);
        _selfCastMessageTextBox = AddTextBox(controlLeft, y, controlWidth);
        y += rowHeight;
        
        // Party Cast Message
        AddLabel("Party Cast Message:", 15, y);
        _partyCastMessageTextBox = AddTextBox(controlLeft, y, controlWidth);
        y += rowHeight;
        
        // Help text for both messages
        var helpLabel = new Label
        {
            Text = "Use {target} as placeholder for player name (e.g., \"You cast bless on {target}\")",
            Location = new Point(controlLeft, y),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8)
        };
        this.Controls.Add(helpLabel);
        y += 22;
        
        // Expire Message
        AddLabel("Expire Message:", 15, y);
        _expireMessageTextBox = AddTextBox(controlLeft, y, controlWidth);
        y += rowHeight + 10;
        
        // Separator - Auto-Recast
        AddSeparator("─── Auto-Recast Settings ───", 15, y);
        y += 25;
        
        // Auto-Recast Checkbox
        _autoRecastCheckBox = new CheckBox
        {
            Text = "Enable Auto-Recast",
            Location = new Point(15, y),
            AutoSize = true,
            ForeColor = Color.LimeGreen,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        _autoRecastCheckBox.CheckedChanged += AutoRecastChanged;
        this.Controls.Add(_autoRecastCheckBox);
        y += 30;
        
        // Recast Buffer
        AddLabel("Recast when:", 15, y);
        _recastBufferNumeric = new NumericUpDown
        {
            Location = new Point(controlLeft, y),
            Width = 60,
            Minimum = 0,
            Maximum = 300,
            Value = 10,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Enabled = false
        };
        this.Controls.Add(_recastBufferNumeric);
        AddHelpLabel("seconds remaining (0 = wait for expire)", controlLeft + 70, y);
        y += rowHeight;
        
        // Priority
        AddLabel("Priority:", 15, y);
        _priorityNumeric = new NumericUpDown
        {
            Location = new Point(controlLeft, y),
            Width = 60,
            Minimum = 1,
            Maximum = 10,
            Value = 5,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Enabled = false
        };
        this.Controls.Add(_priorityNumeric);
        AddHelpLabel("1 = highest, 10 = lowest", controlLeft + 70, y);
        y += rowHeight + 20;
        
        // Buttons
        var saveButton = new Button
        {
            Text = "Save",
            Location = new Point(this.ClientSize.Width - 180, y),
            Size = new Size(75, 30),
            BackColor = Color.FromArgb(0, 120, 0),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        saveButton.Click += SaveButton_Click;
        this.Controls.Add(saveButton);
        
        var cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(this.ClientSize.Width - 95, y),
            Size = new Size(75, 30),
            BackColor = Color.FromArgb(80, 80, 80),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.Cancel
        };
        this.Controls.Add(cancelButton);
        
        this.AcceptButton = saveButton;
        this.CancelButton = cancelButton;
    }
    
    private void AddLabel(string text, int x, int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(x, y + 3),
            AutoSize = true,
            ForeColor = Color.White
        };
        this.Controls.Add(label);
    }
    
    private void AddHelpLabel(string text, int x, int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(x, y + 3),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8)
        };
        this.Controls.Add(label);
    }
    
    private void AddSeparator(string text, int x, int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.Gray
        };
        this.Controls.Add(label);
    }
    
    private TextBox AddTextBox(int x, int y, int width)
    {
        var textBox = new TextBox
        {
            Location = new Point(x, y),
            Width = width,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        this.Controls.Add(textBox);
        return textBox;
    }
    
    private void TargetTypeChanged(object? sender, EventArgs e)
    {
        var isSelfOnly = _targetTypeComboBox.SelectedIndex == 0;
        _partyCastMessageTextBox.Enabled = !isSelfOnly;
        _partyCastMessageTextBox.BackColor = isSelfOnly 
            ? Color.FromArgb(40, 40, 40) 
            : Color.FromArgb(60, 60, 60);
    }
    
    private void AutoRecastChanged(object? sender, EventArgs e)
    {
        var enabled = _autoRecastCheckBox.Checked;
        _recastBufferNumeric.Enabled = enabled;
        _priorityNumeric.Enabled = enabled;
    }
    
    private void LoadBuffData()
    {
        _nameTextBox.Text = _buff.DisplayName;
        _commandTextBox.Text = _buff.Command;
        _durationNumeric.Value = Math.Max(1, _buff.DurationSeconds);
        _manaCostNumeric.Value = Math.Max(0, _buff.ManaCost);
        _categoryComboBox.SelectedIndex = (int)_buff.Category;
        _targetTypeComboBox.SelectedIndex = (int)_buff.TargetType;
        _selfCastMessageTextBox.Text = _buff.SelfCastMessage;
        _partyCastMessageTextBox.Text = _buff.PartyCastMessage;
        _expireMessageTextBox.Text = _buff.ExpireMessage;
        
        _autoRecastCheckBox.Checked = _buff.AutoRecast;
        _recastBufferNumeric.Value = Math.Clamp(_buff.RecastBufferSeconds, 0, 300);
        _priorityNumeric.Value = Math.Clamp(_buff.Priority, 1, 10);
        
        TargetTypeChanged(null, EventArgs.Empty);
        AutoRecastChanged(null, EventArgs.Empty);
    }
    
    private void SaveButton_Click(object? sender, EventArgs e)
    {
        // Validation
        if (string.IsNullOrWhiteSpace(_nameTextBox.Text))
        {
            MessageBox.Show("Please enter a display name.", "Validation Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _nameTextBox.Focus();
            this.DialogResult = DialogResult.None;
            return;
        }
        
        if (string.IsNullOrWhiteSpace(_commandTextBox.Text))
        {
            MessageBox.Show("Please enter a command.", "Validation Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _commandTextBox.Focus();
            this.DialogResult = DialogResult.None;
            return;
        }
        
        if (string.IsNullOrWhiteSpace(_selfCastMessageTextBox.Text))
        {
            MessageBox.Show("Please enter a self cast message for detection.", "Validation Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _selfCastMessageTextBox.Focus();
            this.DialogResult = DialogResult.None;
            return;
        }
        
        // Save to buff object
        _buff.DisplayName = _nameTextBox.Text.Trim();
        _buff.Command = _commandTextBox.Text.Trim();
        _buff.DurationSeconds = (int)_durationNumeric.Value;
        _buff.ManaCost = (int)_manaCostNumeric.Value;
        _buff.Category = (BuffCategory)_categoryComboBox.SelectedIndex;
        _buff.TargetType = (BuffTargetType)_targetTypeComboBox.SelectedIndex;
        _buff.SelfCastMessage = _selfCastMessageTextBox.Text.Trim();
        _buff.PartyCastMessage = _partyCastMessageTextBox.Text.Trim();
        _buff.ExpireMessage = _expireMessageTextBox.Text.Trim();
        
        _buff.AutoRecast = _autoRecastCheckBox.Checked;
        _buff.RecastBufferSeconds = (int)_recastBufferNumeric.Value;
        _buff.Priority = (int)_priorityNumeric.Value;
    }
}
