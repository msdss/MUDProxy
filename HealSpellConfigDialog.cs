namespace MudProxyViewer;

public class HealSpellConfigDialog : Form
{
    private HealSpellConfiguration _spell;
    private bool _isNew;
    
    private TextBox _nameTextBox = null!;
    private TextBox _commandTextBox = null!;
    private NumericUpDown _manaCostNumeric = null!;
    private ComboBox _targetTypeComboBox = null!;
    private TextBox _selfCastMessageTextBox = null!;
    private TextBox _partyCastMessageTextBox = null!;
    private TextBox _partyHealMessageTextBox = null!;
    
    public HealSpellConfiguration Spell => _spell;
    
    public HealSpellConfigDialog(HealSpellConfiguration? existingSpell = null)
    {
        _isNew = existingSpell == null;
        _spell = existingSpell?.Clone() ?? new HealSpellConfiguration();
        
        InitializeComponent();
        LoadSpellData();
    }
    
    private void InitializeComponent()
    {
        this.Text = _isNew ? "Add Heal Spell" : "Edit Heal Spell";
        this.Size = new Size(520, 420);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);
        
        var y = 15;
        var controlLeft = 160;
        var controlWidth = 320;
        var rowHeight = 32;
        
        // Display Name
        AddLabel("Display Name:", 15, y);
        _nameTextBox = AddTextBox(controlLeft, y, controlWidth);
        y += rowHeight;
        
        // Command
        AddLabel("Command:", 15, y);
        _commandTextBox = AddTextBox(controlLeft, y, 80);
        AddHelpLabel("(e.g., mihe)", controlLeft + 90, y);
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
        y += rowHeight;
        
        // Target Type
        AddLabel("Target Type:", 15, y);
        _targetTypeComboBox = new ComboBox
        {
            Location = new Point(controlLeft, y),
            Width = 180,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        _targetTypeComboBox.Items.Add("Self Only");
        _targetTypeComboBox.Items.Add("Single Target (self or party)");
        _targetTypeComboBox.Items.Add("Party Heal (heals everyone)");
        _targetTypeComboBox.SelectedIndex = 1;
        _targetTypeComboBox.SelectedIndexChanged += TargetTypeChanged;
        this.Controls.Add(_targetTypeComboBox);
        y += rowHeight + 10;
        
        // Separator
        AddSeparator("─── Detection Messages (partial match) ───", 15, y);
        y += 25;
        
        // Self Cast Message
        AddLabel("Self Cast Message:", 15, y);
        _selfCastMessageTextBox = AddTextBox(controlLeft, y, controlWidth);
        y += rowHeight;
        
        AddHelpLabel("e.g., You cast minor healing on {target}", controlLeft, y - 8);
        y += 15;
        
        // Party Cast Message (for single target on others)
        AddLabel("Party Cast Message:", 15, y);
        _partyCastMessageTextBox = AddTextBox(controlLeft, y, controlWidth);
        y += rowHeight;
        
        AddHelpLabel("e.g., You cast minor healing on {target}", controlLeft, y - 8);
        y += 15;
        
        // Party Heal Message (for party heal type)
        AddLabel("Party Heal Message:", 15, y);
        _partyHealMessageTextBox = AddTextBox(controlLeft, y, controlWidth);
        y += rowHeight;
        
        AddHelpLabel("e.g., You cast healing rain on your party", controlLeft, y - 8);
        y += 25;
        
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
        var targetType = (HealTargetType)_targetTypeComboBox.SelectedIndex;
        
        _selfCastMessageTextBox.Enabled = targetType != HealTargetType.PartyHeal;
        _partyCastMessageTextBox.Enabled = targetType == HealTargetType.SingleTarget;
        _partyHealMessageTextBox.Enabled = targetType == HealTargetType.PartyHeal;
        
        _selfCastMessageTextBox.BackColor = _selfCastMessageTextBox.Enabled 
            ? Color.FromArgb(60, 60, 60) : Color.FromArgb(40, 40, 40);
        _partyCastMessageTextBox.BackColor = _partyCastMessageTextBox.Enabled 
            ? Color.FromArgb(60, 60, 60) : Color.FromArgb(40, 40, 40);
        _partyHealMessageTextBox.BackColor = _partyHealMessageTextBox.Enabled 
            ? Color.FromArgb(60, 60, 60) : Color.FromArgb(40, 40, 40);
    }
    
    private void LoadSpellData()
    {
        _nameTextBox.Text = _spell.DisplayName;
        _commandTextBox.Text = _spell.Command;
        _manaCostNumeric.Value = Math.Max(0, _spell.ManaCost);
        _targetTypeComboBox.SelectedIndex = (int)_spell.TargetType;
        _selfCastMessageTextBox.Text = _spell.SelfCastMessage;
        _partyCastMessageTextBox.Text = _spell.PartyCastMessage;
        _partyHealMessageTextBox.Text = _spell.PartyHealMessage;
        
        TargetTypeChanged(null, EventArgs.Empty);
    }
    
    private void SaveButton_Click(object? sender, EventArgs e)
    {
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
        
        _spell.DisplayName = _nameTextBox.Text.Trim();
        _spell.Command = _commandTextBox.Text.Trim();
        _spell.ManaCost = (int)_manaCostNumeric.Value;
        _spell.TargetType = (HealTargetType)_targetTypeComboBox.SelectedIndex;
        _spell.SelfCastMessage = _selfCastMessageTextBox.Text.Trim();
        _spell.PartyCastMessage = _partyCastMessageTextBox.Text.Trim();
        _spell.PartyHealMessage = _partyHealMessageTextBox.Text.Trim();
    }
}
