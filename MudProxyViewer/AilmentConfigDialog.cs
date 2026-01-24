namespace MudProxyViewer;

public class AilmentConfigDialog : Form
{
    private AilmentConfiguration _ailment;
    private bool _isNew;
    
    private TextBox _nameTextBox = null!;
    private TextBox _partyIndicatorTextBox = null!;
    private TextBox _telepathRequestTextBox = null!;
    private ListBox _messagesListBox = null!;
    private TextBox _newMessageTextBox = null!;
    
    public AilmentConfiguration Ailment => _ailment;
    
    public AilmentConfigDialog(AilmentConfiguration? existingAilment = null)
    {
        _isNew = existingAilment == null;
        _ailment = existingAilment?.Clone() ?? new AilmentConfiguration();
        
        InitializeComponent();
        LoadAilmentData();
    }
    
    private void InitializeComponent()
    {
        this.Text = _isNew ? "Add Ailment" : "Edit Ailment";
        this.Size = new Size(500, 450);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);
        
        var y = 15;
        var controlLeft = 140;
        var controlWidth = 320;
        var rowHeight = 32;
        
        // Display Name
        AddLabel("Name:", 15, y);
        _nameTextBox = AddTextBox(controlLeft, y, controlWidth);
        y += rowHeight;
        
        // Party Indicator (optional)
        AddLabel("Party Indicator:", 15, y);
        _partyIndicatorTextBox = AddTextBox(controlLeft, y, 40);
        AddHelpLabel("(e.g., P for poison - leave blank if none)", controlLeft + 50, y);
        y += rowHeight;
        
        // Telepath Request (optional)
        AddLabel("Telepath Request:", 15, y);
        _telepathRequestTextBox = AddTextBox(controlLeft, y, 100);
        AddHelpLabel("(e.g., @held for paralysis)", controlLeft + 110, y);
        y += rowHeight + 10;
        
        // Detection Messages section
        AddLabel("Detection Messages:", 15, y);
        AddHelpLabel("(messages that indicate YOU have this ailment)", controlLeft, y);
        y += 25;
        
        _messagesListBox = new ListBox
        {
            Location = new Point(15, y),
            Size = new Size(controlWidth + controlLeft - 30, 120),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Consolas", 9)
        };
        this.Controls.Add(_messagesListBox);
        y += 125;
        
        // Add new message
        _newMessageTextBox = AddTextBox(15, y, controlWidth + controlLeft - 110);
        
        var addButton = new Button
        {
            Text = "Add",
            Location = new Point(controlWidth + controlLeft - 85, y - 2),
            Size = new Size(60, 26),
            BackColor = Color.FromArgb(0, 100, 0),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        addButton.Click += AddMessage_Click;
        this.Controls.Add(addButton);
        
        var removeButton = new Button
        {
            Text = "Remove",
            Location = new Point(controlWidth + controlLeft - 85, y + 28),
            Size = new Size(60, 26),
            BackColor = Color.FromArgb(100, 0, 0),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        removeButton.Click += RemoveMessage_Click;
        this.Controls.Add(removeButton);
        
        y += 70;
        
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
    
    private void LoadAilmentData()
    {
        _nameTextBox.Text = _ailment.DisplayName;
        _partyIndicatorTextBox.Text = _ailment.PartyIndicator ?? "";
        _telepathRequestTextBox.Text = _ailment.TelepathRequest ?? "";
        
        _messagesListBox.Items.Clear();
        foreach (var msg in _ailment.DetectionMessages)
        {
            _messagesListBox.Items.Add(msg);
        }
    }
    
    private void AddMessage_Click(object? sender, EventArgs e)
    {
        var msg = _newMessageTextBox.Text.Trim();
        if (!string.IsNullOrEmpty(msg) && !_messagesListBox.Items.Contains(msg))
        {
            _messagesListBox.Items.Add(msg);
            _newMessageTextBox.Clear();
            _newMessageTextBox.Focus();
        }
    }
    
    private void RemoveMessage_Click(object? sender, EventArgs e)
    {
        if (_messagesListBox.SelectedIndex >= 0)
        {
            _messagesListBox.Items.RemoveAt(_messagesListBox.SelectedIndex);
        }
    }
    
    private void SaveButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nameTextBox.Text))
        {
            MessageBox.Show("Please enter a name.", "Validation Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _nameTextBox.Focus();
            this.DialogResult = DialogResult.None;
            return;
        }
        
        _ailment.DisplayName = _nameTextBox.Text.Trim();
        _ailment.PartyIndicator = string.IsNullOrWhiteSpace(_partyIndicatorTextBox.Text) 
            ? null : _partyIndicatorTextBox.Text.Trim();
        _ailment.TelepathRequest = string.IsNullOrWhiteSpace(_telepathRequestTextBox.Text) 
            ? null : _telepathRequestTextBox.Text.Trim();
        
        _ailment.DetectionMessages.Clear();
        foreach (var item in _messagesListBox.Items)
        {
            _ailment.DetectionMessages.Add(item.ToString() ?? "");
        }
    }
}
