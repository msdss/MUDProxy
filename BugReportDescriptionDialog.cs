namespace MudProxyViewer;

public class BugReportDescriptionDialog : Form
{
    private TextBox _descriptionTextBox = null!;

    public string Description { get; private set; } = string.Empty;

    public BugReportDescriptionDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        this.Text = "Bug Report Description";
        this.Size = new Size(450, 220);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.Manual;
        this.BackColor = Color.FromArgb(45, 45, 45);
        this.ShowInTaskbar = false;

        var promptLabel = new Label
        {
            Text = "Describe the issue (optional):",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9),
            Location = new Point(15, 12),
            AutoSize = true
        };

        _descriptionTextBox = new TextBox
        {
            Location = new Point(15, 35),
            Size = new Size(400, 90),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Consolas", 9),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true
        };

        var okButton = new Button
        {
            Text = "OK",
            Width = 80,
            Height = 28,
            Location = new Point(335, 135),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        okButton.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);

        this.AcceptButton = okButton;
        this.Controls.Add(promptLabel);
        this.Controls.Add(_descriptionTextBox);
        this.Controls.Add(okButton);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (Owner != null)
        {
            this.Location = new Point(
                Owner.Left + (Owner.Width - this.Width) / 2,
                Owner.Top + (Owner.Height - this.Height) / 2);
        }
        _descriptionTextBox.Focus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        Description = _descriptionTextBox.Text.Trim();
    }
}
