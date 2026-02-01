namespace MudProxyViewer;

public class CureSpellConfigDialog : Form
{
    private CureSpellConfiguration _spell;
    private bool _isNew;
    private readonly List<AilmentConfiguration> _ailments;
    
    private TextBox _nameTextBox = null!;
    private TextBox _commandTextBox = null!;
    private NumericUpDown _manaCostNumeric = null!;
    private ComboBox _ailmentComboBox = null!;
    private TextBox _selfCastMessageTextBox = null!;
    private TextBox _partyCastMessageTextBox = null!;
    private NumericUpDown _priorityNumeric = null!;
    
    public CureSpellConfiguration Spell => _spell;
    
    public CureSpellConfigDialog(List<AilmentConfiguration> ailments, CureSpellConfiguration? existingSpell = null)
    {
        _ailments = ailments;
        _isNew = existingSpell == null;
        _spell = existingSpell?.Clone() ?? new CureSpellConfiguration();
        
        InitializeComponent();
        LoadSpellData();
    }
    
    private void InitializeComponent()
    {
        this.Text = _isNew ? "Add Cure Spell" : "Edit Cure Spell";
        this.Size = new Size(520, 380);
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
        _commandTextBox = AddTextBox(controlLeft, y, 100);
        AddHelpLabel("(e.g., cure)", controlLeft + 110, y);
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
        
        // Ailment
        AddLabel("Cures Ailment:", 15, y);
        _ailmentComboBox = new ComboBox
        {
            Location = new Point(controlLeft, y),
            Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        foreach (var ailment in _ailments)
        {
            _ailmentComboBox.Items.Add(new AilmentItem(ailment));
        }
        if (_ailmentComboBox.Items.Count > 0)
            _ailmentComboBox.SelectedIndex = 0;
        this.Controls.Add(_ailmentComboBox);
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
            ForeColor = Color.White
        };
        this.Controls.Add(_priorityNumeric);
        AddHelpLabel("(1 = highest)", controlLeft + 70, y);
        y += rowHeight + 10;
        
        // Self Cast Message
        AddLabel("Self Cast Message:", 15, y);
        _selfCastMessageTextBox = AddTextBox(controlLeft, y, controlWidth);
        y += rowHeight;
        AddHelpLabel("e.g., You cast cure poison on {target}", controlLeft, y - 10);
        y += 15;
        
        // Party Cast Message
        AddLabel("Party Cast Message:", 15, y);
        _partyCastMessageTextBox = AddTextBox(controlLeft, y, controlWidth);
        y += rowHeight;
        AddHelpLabel("e.g., You cast cure poison on {target}", controlLeft, y - 10);
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
    
    private void LoadSpellData()
    {
        _nameTextBox.Text = _spell.DisplayName;
        _commandTextBox.Text = _spell.Command;
        _manaCostNumeric.Value = Math.Max(0, _spell.ManaCost);
        _priorityNumeric.Value = Math.Clamp(_spell.Priority, 1, 10);
        _selfCastMessageTextBox.Text = _spell.SelfCastMessage;
        _partyCastMessageTextBox.Text = _spell.PartyCastMessage;
        
        // Find and select the ailment
        for (int i = 0; i < _ailmentComboBox.Items.Count; i++)
        {
            if (_ailmentComboBox.Items[i] is AilmentItem item && item.Ailment.Id == _spell.AilmentId)
            {
                _ailmentComboBox.SelectedIndex = i;
                break;
            }
        }
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
        
        if (_ailmentComboBox.SelectedItem is not AilmentItem selectedAilment)
        {
            MessageBox.Show("Please select an ailment.", "Validation Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            this.DialogResult = DialogResult.None;
            return;
        }
        
        _spell.DisplayName = _nameTextBox.Text.Trim();
        _spell.Command = _commandTextBox.Text.Trim();
        _spell.ManaCost = (int)_manaCostNumeric.Value;
        _spell.AilmentId = selectedAilment.Ailment.Id;
        _spell.Priority = (int)_priorityNumeric.Value;
        _spell.SelfCastMessage = _selfCastMessageTextBox.Text.Trim();
        _spell.PartyCastMessage = _partyCastMessageTextBox.Text.Trim();
    }
    
    private class AilmentItem
    {
        public AilmentConfiguration Ailment { get; }
        public AilmentItem(AilmentConfiguration ailment) { Ailment = ailment; }
        public override string ToString() => Ailment.DisplayName;
    }
}
