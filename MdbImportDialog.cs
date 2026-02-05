namespace MudProxyViewer;

/// <summary>
/// Dialog for importing MDB database files with progress indication
/// </summary>
public class MdbImportDialog : Form
{
    private readonly MdbImporter _importer;
    private readonly string _mdbFilePath;
    
    private Label _statusLabel = null!;
    private ProgressBar _progressBar = null!;
    private Label _progressLabel = null!;
    private Button _cancelButton = null!;
    private Button _retryButton = null!;
    private Button _okButton = null!;
    private TextBox _detailsTextBox = null!;
    
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _importComplete = false;
    private bool _importSuccess = false;
    
    public MdbImportDialog(string mdbFilePath)
    {
        _mdbFilePath = mdbFilePath;
        _importer = new MdbImporter();
        
        InitializeComponent();
        
        // Wire up events
        _importer.OnStatusChanged += status => 
        {
            if (InvokeRequired)
                BeginInvoke(() => UpdateStatus(status));
            else
                UpdateStatus(status);
        };
        
        _importer.OnProgressChanged += (current, total) =>
        {
            if (InvokeRequired)
                BeginInvoke(() => UpdateProgress(current, total));
            else
                UpdateProgress(current, total);
        };
        
        _importer.OnRowProgress += (tableName, currentRow, totalRows) =>
        {
            if (InvokeRequired)
                BeginInvoke(() => UpdateRowProgress(tableName, currentRow, totalRows));
            else
                UpdateRowProgress(tableName, currentRow, totalRows);
        };
        
        _importer.OnError += error =>
        {
            if (InvokeRequired)
                BeginInvoke(() => AppendDetails($"ERROR: {error}"));
            else
                AppendDetails($"ERROR: {error}");
        };
    }
    
    private void InitializeComponent()
    {
        this.Text = "Import Game Database";
        this.Size = new Size(500, 350);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterParent;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.BackColor = Color.FromArgb(45, 45, 45);
        this.ControlBox = false;  // Prevent closing via X button during import
        
        int y = 15;
        
        // Status label
        _statusLabel = new Label
        {
            Text = "Preparing to import...",
            Location = new Point(15, y),
            Size = new Size(455, 25),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        this.Controls.Add(_statusLabel);
        y += 35;
        
        // Progress bar
        _progressBar = new ProgressBar
        {
            Location = new Point(15, y),
            Size = new Size(455, 25),
            Style = ProgressBarStyle.Continuous
        };
        this.Controls.Add(_progressBar);
        y += 35;
        
        // Progress label
        _progressLabel = new Label
        {
            Text = "",
            Location = new Point(15, y),
            Size = new Size(455, 20),
            ForeColor = Color.LightGray,
            TextAlign = ContentAlignment.MiddleCenter
        };
        this.Controls.Add(_progressLabel);
        y += 30;
        
        // Details text box
        _detailsTextBox = new TextBox
        {
            Location = new Point(15, y),
            Size = new Size(455, 150),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.LightGray,
            Font = new Font("Consolas", 9)
        };
        this.Controls.Add(_detailsTextBox);
        y += 160;
        
        // Buttons
        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(295, y),
            Size = new Size(80, 30),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _cancelButton.Click += CancelButton_Click;
        this.Controls.Add(_cancelButton);
        
        _retryButton = new Button
        {
            Text = "Retry",
            Location = new Point(295, y),
            Size = new Size(80, 30),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Visible = false
        };
        _retryButton.Click += RetryButton_Click;
        this.Controls.Add(_retryButton);
        
        _okButton = new Button
        {
            Text = "OK",
            Location = new Point(385, y),
            Size = new Size(80, 30),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Visible = false
        };
        _okButton.Click += OkButton_Click;
        this.Controls.Add(_okButton);
        
        // Start import when dialog loads
        this.Load += async (s, e) => await StartImportAsync();
    }
    
    private async Task StartImportAsync()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _importComplete = false;
        _importSuccess = false;
        
        _cancelButton.Visible = true;
        _retryButton.Visible = false;
        _okButton.Visible = false;
        _progressBar.Value = 0;
        _detailsTextBox.Clear();
        
        AppendDetails($"Importing: {_mdbFilePath}");
        AppendDetails($"Output: {_importer.GameDataPath}");
        AppendDetails("");
        
        try
        {
            var (success, message) = await _importer.ImportAsync(_mdbFilePath, _cancellationTokenSource.Token);
            
            _importComplete = true;
            _importSuccess = success;
            
            if (success)
            {
                _statusLabel.Text = "Import Successful!";
                _statusLabel.ForeColor = Color.LightGreen;
                _progressBar.Value = _progressBar.Maximum > 0 ? _progressBar.Maximum : 1;
                AppendDetails("");
                AppendDetails("=== IMPORT COMPLETE ===");
                AppendDetails(message);
                
                _cancelButton.Visible = false;
                _okButton.Visible = true;
                this.ControlBox = true;  // Allow closing
                this.DialogResult = DialogResult.OK;
            }
            else
            {
                ShowError(message);
            }
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Import Cancelled";
            _statusLabel.ForeColor = Color.Orange;
            AppendDetails("");
            AppendDetails("Import was cancelled by user.");
            
            _cancelButton.Visible = false;
            _okButton.Visible = true;
            this.ControlBox = true;
            this.DialogResult = DialogResult.Cancel;
        }
        catch (Exception ex)
        {
            ShowError($"Unexpected error: {ex.Message}");
        }
    }
    
    private void ShowError(string message)
    {
        _statusLabel.Text = "Import Failed";
        _statusLabel.ForeColor = Color.OrangeRed;
        
        AppendDetails("");
        AppendDetails("=== ERROR ===");
        AppendDetails(message);
        
        _cancelButton.Visible = false;
        _retryButton.Visible = true;
        _okButton.Text = "Close";
        _okButton.Visible = true;
        this.ControlBox = true;
    }
    
    private void UpdateStatus(string status)
    {
        _statusLabel.Text = status;
        AppendDetails(status);
    }
    
    private void UpdateProgress(int current, int total)
    {
        _progressBar.Maximum = total;
        _progressBar.Value = current;
        _progressLabel.Text = $"Table {current} of {total}";
    }
    
    private void UpdateRowProgress(string tableName, int currentRow, int totalRows)
    {
        int percent = totalRows > 0 ? (currentRow * 100) / totalRows : 0;
        _progressLabel.Text = $"Importing {tableName}: {currentRow:N0} / {totalRows:N0} rows ({percent}%)";
    }
    
    private void AppendDetails(string text)
    {
        _detailsTextBox.AppendText(text + Environment.NewLine);
    }
    
    private void CancelButton_Click(object? sender, EventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        _cancelButton.Enabled = false;
        _cancelButton.Text = "Cancelling...";
    }
    
    private async void RetryButton_Click(object? sender, EventArgs e)
    {
        await StartImportAsync();
    }
    
    private void OkButton_Click(object? sender, EventArgs e)
    {
        this.DialogResult = _importSuccess ? DialogResult.OK : DialogResult.Cancel;
        this.Close();
    }
    
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Don't allow closing during import
        if (!_importComplete && _cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
        {
            e.Cancel = true;
            return;
        }
        
        _cancellationTokenSource?.Dispose();
        base.OnFormClosing(e);
    }
}

/// <summary>
/// Dialog shown when ACE is not installed
/// </summary>
public class AceNotInstalledDialog : Form
{
    public AceNotInstalledDialog()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        this.Text = "Microsoft Access Database Engine Required";
        this.Size = new Size(550, 320);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterParent;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.BackColor = Color.FromArgb(45, 45, 45);
        
        // Warning icon area
        var iconLabel = new Label
        {
            Text = "⚠️",
            Location = new Point(15, 15),
            Size = new Size(50, 50),
            Font = new Font("Segoe UI", 28),
            ForeColor = Color.Orange
        };
        this.Controls.Add(iconLabel);
        
        // Title
        var titleLabel = new Label
        {
            Text = "ACE (Access Database Engine) Not Found",
            Location = new Point(70, 15),
            Size = new Size(450, 25),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 12, FontStyle.Bold)
        };
        this.Controls.Add(titleLabel);
        
        // Description
        var descriptionText = @"ACE (Access Connectivity Engine) is required to read .mdb database files. 
This is a free Microsoft component that provides database connectivity.

To import MajorMUD game data, you need to install ACE first.";
        
        var descLabel = new Label
        {
            Text = descriptionText,
            Location = new Point(70, 45),
            Size = new Size(450, 80),
            ForeColor = Color.LightGray
        };
        this.Controls.Add(descLabel);
        
        // Instructions
        var instructionsLabel = new Label
        {
            Text = "Installation Instructions:",
            Location = new Point(15, 130),
            Size = new Size(500, 20),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        this.Controls.Add(instructionsLabel);
        
        var stepsText = @"1. Click the download link below
2. Choose the version matching your system (32-bit or 64-bit)
3. Run the installer
4. Restart this application
5. Try importing again";
        
        var stepsLabel = new Label
        {
            Text = stepsText,
            Location = new Point(25, 155),
            Size = new Size(490, 85),
            ForeColor = Color.LightGray
        };
        this.Controls.Add(stepsLabel);
        
        // Download link
        var linkLabel = new LinkLabel
        {
            Text = "Download Microsoft Access Database Engine",
            Location = new Point(15, 245),
            Size = new Size(350, 20),
            LinkColor = Color.CornflowerBlue,
            ActiveLinkColor = Color.LightBlue,
            VisitedLinkColor = Color.CornflowerBlue
        };
        linkLabel.LinkClicked += (s, e) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://www.microsoft.com/en-us/download/details.aspx?id=54920",
                    UseShellExecute = true
                });
            }
            catch { }
        };
        this.Controls.Add(linkLabel);
        
        // OK button
        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(435, 240),
            Size = new Size(80, 30),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        this.Controls.Add(okButton);
        this.AcceptButton = okButton;
    }
}
