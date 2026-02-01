using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace MudProxyViewer;

public partial class MainForm : Form
{
    // Configuration - loaded from character profile
    private string _serverAddress = string.Empty;
    private int _serverPort = 23;

    // Network components - direct telnet connection
    private TcpClient? _serverConnection;
    private NetworkStream? _serverStream;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isConnected = false;
    
    // Logon automation state
    private bool _isInLoginPhase = true;  // True until we see HP bar
    private HashSet<string> _triggeredLogonSequences = new();  // Track which sequences have fired

    // Buff Manager
    private readonly BuffManager _buffManager = new();

    // UI Components - Main
    private RichTextBox _logTextBox = null!;  // Keep for compatibility, will point to _mudOutputTextBox
    private RichTextBox _mudOutputTextBox = null!;  // MUD server output
    private RichTextBox _systemLogTextBox = null!;  // System/proxy logs
    private SplitContainer _terminalSplitContainer = null!;  // Vertical split: MUD output / logs
    private Label _statusLabel = null!;
    private Button _connectButton = null!;
    private CheckBox _autoScrollCheckBox = null!;
    private CheckBox _autoScrollLogsCheckBox = null!;
    private CheckBox _showTimestampsCheckBox = null!;
    private Label _serverAddressLabel = null!;
    private Label _serverPortLabel = null!;
    private TextBox _commandInputTextBox = null!;  // Command input for sending to server

    // UI Components - Combat Panel
    private Panel _combatPanel = null!;
    private Label _combatStateLabel = null!;
    private Label _tickTimerLabel = null!;
    private ProgressBar _tickProgressBar = null!;
    private Label _lastTickTimeLabel = null!;
    private PlayerStatusPanel _selfStatusPanel = null!;
    private readonly List<PlayerStatusPanel> _partyStatusPanels = new();
    private Panel _partyContainer = null!;
    private Panel _partySeparator = null!;
    private Label _partyLabel = null!;
    private int _selfStatusPanelBaseY = 0;  // Y position where self status panel starts
    
    // Quick toggle buttons
    private Button _pauseButton = null!;
    private Button _combatToggleButton = null!;
    private Button _healToggleButton = null!;
    private Button _buffToggleButton = null!;
    private Button _cureToggleButton = null!;
    private Button _resetTickButton = null!;
    private Button _manualTickButton = null!;
    private Label _clearBuffsLabel = null!;

    // Combat State Tracking
    private bool _inCombat = false;
    private DateTime? _lastTickTime = null;
    private DateTime? _nextTickTime = null;
    private System.Windows.Forms.Timer _tickTimer = null!;
    private System.Windows.Forms.Timer _buffUpdateTimer = null!;
    private System.Windows.Forms.Timer _outOfCombatRecastTimer = null!;
    private System.Windows.Forms.Timer _parCheckTimer = null!;
    private const int TICK_INTERVAL_MS = 5000;

    // Player State Tracking
    private int _currentHp = 0;
    private int _maxHp = 0;
    private int _currentMana = 0;
    private int _maxMana = 0;
    private string _manaType = "MA";

    // Pattern Detection - ANSI regex catches color codes, cursor control, and other escape sequences
    private static readonly Regex AnsiRegex = new(@"\x1B(?:\[[0-9;]*[A-Za-z]|\[[0-9;]*[mKJHfsu]|[\(\)][AB012]|[>=<]|[78DME])", RegexOptions.Compiled);
    private static readonly Regex HpManaRegex = new(@"\[HP=(\d+)(?:/(\d+))?/(MA|KAI)=(\d+)(?:/(\d+))?\]", RegexOptions.Compiled);
    private static readonly Regex DamageRegex = new(@"for \d+ damage!", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CombatEngagedRegex = new(@"\*Combat Engaged\*", RegexOptions.Compiled);
    private static readonly Regex CombatOffRegex = new(@"\*Combat Off\*", RegexOptions.Compiled);
    private static readonly Regex PlayerDeathRegex = new(@"due to a miracle, you have been saved", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Tick Detection
    private DateTime _lastDamageMessageTime = DateTime.MinValue;
    private int _damageMessageCount = 0;
    private const int DAMAGE_CLUSTER_THRESHOLD = 2;
    private const int DAMAGE_CLUSTER_WINDOW_MS = 500;

    public MainForm()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        
        InitializeComponent();
        InitializeTimers();
        InitializeBuffManagerEvents();
        
        // Auto-start proxy if enabled
        this.Shown += MainForm_Shown;
    }
    
    private void MainForm_Shown(object? sender, EventArgs e)
    {
        // Check for auto-load last character
        if (_buffManager.AutoLoadLastCharacter && !string.IsNullOrEmpty(_buffManager.LastCharacterPath))
        {
            if (File.Exists(_buffManager.LastCharacterPath))
            {
                LogMessage($"Auto-loading last character: {Path.GetFileName(_buffManager.LastCharacterPath)}", MessageType.System);
                var (success, message) = _buffManager.LoadCharacterProfile(_buffManager.LastCharacterPath);
                if (success)
                {
                    RefreshPlayerInfo();
                    RefreshBuffDisplay();
                    LogMessage("Character loaded. Click Connect to connect to the server.", MessageType.System);
                }
                else
                {
                    LogMessage($"Failed to auto-load character: {message}", MessageType.System);
                }
            }
            else
            {
                LogMessage($"Last character file not found: {_buffManager.LastCharacterPath}", MessageType.System);
            }
        }
        else
        {
            LogMessage("Welcome! Load a character profile (File → Load Character) to configure connection settings.", MessageType.System);
        }
    }

    private void InitializeTimers()
    {
        _tickTimer = new System.Windows.Forms.Timer();
        _tickTimer.Interval = 50;
        _tickTimer.Tick += TickTimer_Tick;
        _tickTimer.Start();
        
        _buffUpdateTimer = new System.Windows.Forms.Timer();
        _buffUpdateTimer.Interval = 250; // Update buff display 4x per second
        _buffUpdateTimer.Tick += BuffUpdateTimer_Tick;
        _buffUpdateTimer.Start();
        
        _outOfCombatRecastTimer = new System.Windows.Forms.Timer();
        _outOfCombatRecastTimer.Interval = 1000; // Check every second when out of combat
        _outOfCombatRecastTimer.Tick += OutOfCombatRecastTimer_Tick;
        _outOfCombatRecastTimer.Start();
        
        _parCheckTimer = new System.Windows.Forms.Timer();
        _parCheckTimer.Interval = 1000; // Check every second
        _parCheckTimer.Tick += ParCheckTimer_Tick;
        _parCheckTimer.Start();
    }

    private void InitializeBuffManagerEvents()
    {
        _buffManager.OnBuffsChanged += () => BeginInvoke(RefreshBuffDisplay);
        _buffManager.OnPartyChanged += () => BeginInvoke(RefreshPartyDisplay);
        _buffManager.OnPlayerInfoChanged += () => BeginInvoke(RefreshPlayerInfo);
        _buffManager.OnBbsSettingsChanged += () => BeginInvoke(RefreshBbsSettingsDisplay);
        _buffManager.OnLogMessage += (msg) => LogMessage(msg, MessageType.System);
        _buffManager.OnSendCommand += SendCommandToServer;
        
        // Initialize CombatManager with dependencies
        _buffManager.CombatManager.Initialize(
            _buffManager.PlayerDatabase,
            _buffManager.MonsterDatabase,
            () => _inCombat,
            () => _maxMana > 0 ? (_currentMana * 100 / _maxMana) : 100
        );
        _buffManager.CombatManager.OnSendCommand += SendCommandToServer;
    }
    
    /// <summary>
    /// Update the UI when BBS settings are loaded from a profile
    /// </summary>
    private void RefreshBbsSettingsDisplay()
    {
        var settings = _buffManager.BbsSettings;
        _serverAddress = settings.Address;
        _serverPort = settings.Port;
        
        if (!string.IsNullOrEmpty(settings.Address))
        {
            _serverAddressLabel.Text = $"{settings.Address}:{settings.Port}";
            _serverAddressLabel.ForeColor = Color.White;
        }
        else
        {
            _serverAddressLabel.Text = "No server configured";
            _serverAddressLabel.ForeColor = Color.Gray;
        }
    }
    
    private void SendCommandToServer(string command)
    {
        if (_serverStream == null || !_isConnected)
        {
            LogMessage("Cannot send command - not connected to server", MessageType.System);
            return;
        }
        
        try
        {
            var data = Encoding.GetEncoding(437).GetBytes(command + "\r\n");
            _serverStream.Write(data, 0, data.Length);
            // Don't log here - server will echo the command back
        }
        catch (Exception ex)
        {
            LogMessage($"Error sending command: {ex.Message}", MessageType.System);
        }
    }

    private void TickTimer_Tick(object? sender, EventArgs e)
    {
        UpdateTickDisplay();
    }

    private void BuffUpdateTimer_Tick(object? sender, EventArgs e)
    {
        _buffManager.RemoveExpiredBuffs();
        RefreshBuffTimers();
    }
    
    private void ParCheckTimer_Tick(object? sender, EventArgs e)
    {
        // Only check when connected AND in-game (not during login)
        if (!_isConnected || _isInLoginPhase) return;
        if (_serverConnection == null || !_serverConnection.Connected) return;
        
        _buffManager.CheckParCommand();
        _buffManager.CheckHealthRequests();
    }
    
    private void CommandInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;  // Prevent the "ding" sound
            
            var command = _commandInputTextBox.Text;
            if (!string.IsNullOrEmpty(command))
            {
                SendCommandToServer(command);
                _commandInputTextBox.Clear();
            }
            else
            {
                // Send empty line (just Enter)
                SendCommandToServer("");
            }
        }
    }
    
    private void OutOfCombatRecastTimer_Tick(object? sender, EventArgs e)
    {
        // Only auto-recast when connected AND in-game (not during login)
        if (!_isConnected || _isInLoginPhase) return;
        
        // Only auto-recast when NOT in combat
        // During combat, recasts happen after ticks via RecordTick()
        if (_inCombat) return;
        
        // If we have a tick timer running, check if it's safe to cast
        // (not within 1.5 seconds of the next tick)
        if (_nextTickTime.HasValue)
        {
            var now = DateTime.Now;
            var timeUntilTick = (_nextTickTime.Value - now).TotalMilliseconds;
            
            // Normalize - if tick is in the past, calculate next one
            while (timeUntilTick < 0)
            {
                timeUntilTick += TICK_INTERVAL_MS;
            }
            
            // If tick is coming soon (within 1.5 seconds), don't cast
            // We don't want to start casting right before a tick
            if (timeUntilTick < 1500)
            {
                return;
            }
        }
        // If tick time is unknown, the buff manager's internal cooldown will prevent spam
        
        // Safe to check for recasts
        _buffManager.CheckAutoRecast();
    }

    private void UpdateTickDisplay()
    {
        if (InvokeRequired)
        {
            BeginInvoke(UpdateTickDisplay);
            return;
        }

        if (_nextTickTime.HasValue && _lastTickTime.HasValue)
        {
            var now = DateTime.Now;
            var timeUntilTick = (_nextTickTime.Value - now).TotalMilliseconds;

            if (timeUntilTick < 0)
            {
                // Tick time has passed - advance to next tick
                while (_nextTickTime.Value < now)
                {
                    _nextTickTime = _nextTickTime.Value.AddMilliseconds(TICK_INTERVAL_MS);
                }
                
                // When not in combat, we rely on the timer to detect ticks
                // (since there are no damage messages to trigger RecordTick)
                // OnCombatTick has internal duplicate prevention via _lastParSent
                if (!_inCombat)
                {
                    _buffManager.OnCombatTick();
                }
                
                timeUntilTick = (_nextTickTime.Value - now).TotalMilliseconds;
            }

            var seconds = timeUntilTick / 1000.0;
            _tickTimerLabel.Text = $"Next Tick: {seconds:F1}s";

            var progress = (int)(100 - (timeUntilTick / TICK_INTERVAL_MS * 100));
            progress = Math.Clamp(progress, 0, 100);
            _tickProgressBar.Value = progress;

            if (seconds < 1.0)
                _tickTimerLabel.ForeColor = Color.Red;
            else if (seconds < 2.0)
                _tickTimerLabel.ForeColor = Color.Orange;
            else
                _tickTimerLabel.ForeColor = Color.LimeGreen;
        }
        else
        {
            _tickTimerLabel.Text = "Next Tick: --.-s";
            _tickTimerLabel.ForeColor = Color.Gray;
            _tickProgressBar.Value = 0;
        }
    }

    private void InitializeComponent()
    {
        this.Text = "MUD Proxy Viewer v1.3.0 - Combat Assistant";
        this.Size = new Size(1250, 800);
        this.MinimumSize = new Size(1100, 700);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.FromArgb(30, 30, 30);

        // Create menu strip with dark theme
        var menuStrip = new MenuStrip();
        menuStrip.BackColor = Color.FromArgb(45, 45, 45);
        menuStrip.ForeColor = Color.White;
        menuStrip.Renderer = new DarkMenuRenderer();

        var fileMenu = new ToolStripMenuItem("File") { ForeColor = Color.White };
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Load Character...", null, LoadCharacter_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Save Character", null, SaveCharacter_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Save Character As...", null, SaveCharacterAs_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        var autoLoadMenuItem = new ToolStripMenuItem("Auto-Load Last Character", null, ToggleAutoLoad_Click) 
        { 
            ForeColor = Color.White, 
            BackColor = Color.FromArgb(45, 45, 45),
            CheckOnClick = true,
            Checked = _buffManager.AutoLoadLastCharacter
        };
        fileMenu.DropDownItems.Add(autoLoadMenuItem);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Save Log...", null, SaveLog_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Clear Log", null, ClearLog_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Exit", null, (s, e) => this.Close()) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });

        var optionsMenu = new ToolStripMenuItem("Options") { ForeColor = Color.White };
        optionsMenu.DropDownItems.Add(new ToolStripMenuItem("Settings...", null, OpenSettings_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        optionsMenu.DropDownItems.Add(new ToolStripSeparator());
        
        // Export submenu
        var exportMenu = new ToolStripMenuItem("Export") { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) };
        exportMenu.DropDownItems.Add(new ToolStripMenuItem("Export Buffs...", null, ExportBuffs_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        exportMenu.DropDownItems.Add(new ToolStripMenuItem("Export Heals...", null, ExportHeals_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        exportMenu.DropDownItems.Add(new ToolStripMenuItem("Export Cures...", null, ExportCures_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        optionsMenu.DropDownItems.Add(exportMenu);
        
        // Import submenu
        var importMenu = new ToolStripMenuItem("Import") { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) };
        importMenu.DropDownItems.Add(new ToolStripMenuItem("Import Buffs...", null, ImportBuffs_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        importMenu.DropDownItems.Add(new ToolStripMenuItem("Import Heals...", null, ImportHeals_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        importMenu.DropDownItems.Add(new ToolStripMenuItem("Import Cures...", null, ImportCures_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        optionsMenu.DropDownItems.Add(importMenu);

        // Game Data menu
        var gameDataMenu = new ToolStripMenuItem("Game Data") { ForeColor = Color.White };
        gameDataMenu.DropDownItems.Add(new ToolStripMenuItem("Player DB...", null, OpenPlayerDB_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        gameDataMenu.DropDownItems.Add(new ToolStripMenuItem("Monster DB...", null, OpenMonsterDB_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });

        var helpMenu = new ToolStripMenuItem("Help") { ForeColor = Color.White };
        helpMenu.DropDownItems.Add(new ToolStripMenuItem("About", null, About_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });

        menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, optionsMenu, gameDataMenu, helpMenu });
        this.MainMenuStrip = menuStrip;
        this.Controls.Add(menuStrip);

        // Connection settings panel
        var settingsPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = Color.FromArgb(45, 45, 45),
            Padding = new Padding(10, 5, 10, 5)
        };

        _connectButton = new Button
        {
            Text = "Connect",
            Width = 100,
            Height = 28,
            Location = new Point(10, 5),
            BackColor = Color.FromArgb(0, 120, 0),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _connectButton.Click += ConnectButton_Click;

        _serverAddressLabel = new Label 
        { 
            Text = "No profile loaded", 
            ForeColor = Color.Gray, 
            AutoSize = true, 
            Location = new Point(120, 10) 
        };
        
        _serverPortLabel = new Label 
        { 
            Text = "", 
            ForeColor = Color.Gray, 
            AutoSize = true, 
            Location = new Point(120, 10) 
        };
        _serverPortLabel.Visible = false;  // Hidden until profile loaded

        // Pause all commands button
        _pauseButton = new Button
        {
            Text = "||",
            Width = 35,
            Height = 28,
            Location = new Point(350, 5),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        _pauseButton.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        _pauseButton.Click += PauseButton_Click;

        // Combat toggle button
        _combatToggleButton = new Button
        {
            Text = "Combat",
            Width = 55,
            Height = 28,
            Location = new Point(390, 5),
            BackColor = _buffManager.CombatManager.CombatEnabled ? Color.FromArgb(70, 130, 180) : Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _combatToggleButton.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        _combatToggleButton.Click += CombatToggleButton_Click;

        // Quick toggle buttons for Heal/Buff/Cure
        _healToggleButton = new Button
        {
            Text = "Heal",
            Width = 55,
            Height = 28,
            Location = new Point(450, 5),
            BackColor = _buffManager.HealingManager.HealingEnabled ? Color.FromArgb(70, 130, 180) : Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _healToggleButton.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        _healToggleButton.Click += HealToggleButton_Click;
        
        _buffToggleButton = new Button
        {
            Text = "Buff",
            Width = 55,
            Height = 28,
            Location = new Point(510, 5),
            BackColor = _buffManager.AutoRecastEnabled ? Color.FromArgb(70, 130, 180) : Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _buffToggleButton.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        _buffToggleButton.Click += BuffToggleButton_Click;
        
        _cureToggleButton = new Button
        {
            Text = "Cure",
            Width = 55,
            Height = 28,
            Location = new Point(570, 5),
            BackColor = _buffManager.CureManager.CuringEnabled ? Color.FromArgb(70, 130, 180) : Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _cureToggleButton.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        _cureToggleButton.Click += CureToggleButton_Click;

        _autoScrollCheckBox = new CheckBox { Text = "Auto-scroll MUD", Checked = true, ForeColor = Color.White, AutoSize = true, Location = new Point(10, 35) };
        _autoScrollLogsCheckBox = new CheckBox { Text = "Auto-scroll Logs", Checked = true, ForeColor = Color.White, AutoSize = true, Location = new Point(130, 35) };
        _showTimestampsCheckBox = new CheckBox { Text = "Timestamps", Checked = true, ForeColor = Color.White, AutoSize = true, Location = new Point(260, 35) };

        settingsPanel.Controls.AddRange(new Control[] {
            _connectButton,
            _serverAddressLabel,
            _pauseButton, _combatToggleButton, _healToggleButton, _buffToggleButton, _cureToggleButton,
            _autoScrollCheckBox, _autoScrollLogsCheckBox, _showTimestampsCheckBox
        });

        // Combat info panel (right side - resizable via splitter)
        _combatPanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = 300,
            BackColor = Color.FromArgb(25, 25, 25),
            AutoScroll = true,
            Padding = new Padding(6)
        };

        int y = 6;
        int panelWidth = 288;  // Will be updated on resize

        // Combat Status Section with buttons
        var combatHeaderLabel = new Label
        {
            Text = "COMBAT",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(150, 150, 150),
            Location = new Point(6, y),
            AutoSize = true
        };
        _combatPanel.Controls.Add(combatHeaderLabel);

        _combatStateLabel = new Label
        {
            Text = "DISENGAGED",
            Font = new Font("Consolas", 11, FontStyle.Bold),
            ForeColor = Color.Gray,
            Location = new Point(70, y - 1),
            AutoSize = true
        };
        _combatPanel.Controls.Add(_combatStateLabel);
        
        // Reset and Tick buttons (right-aligned)
        _manualTickButton = new Button
        {
            Text = "Tick",
            Size = new Size(40, 20),
            Location = new Point(panelWidth - 40, y - 2),
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 7)
        };
        _manualTickButton.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
        _manualTickButton.Click += ManualTick_Click;
        _combatPanel.Controls.Add(_manualTickButton);
        
        _resetTickButton = new Button
        {
            Text = "Reset",
            Size = new Size(45, 20),
            Location = new Point(panelWidth - 88, y - 2),
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 7)
        };
        _resetTickButton.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
        _resetTickButton.Click += ResetTickTimer_Click;
        _combatPanel.Controls.Add(_resetTickButton);
        
        y += 22;

        // Tick Timer (compact)
        _tickTimerLabel = new Label
        {
            Text = "Next Tick: --.-s",
            Font = new Font("Consolas", 10, FontStyle.Bold),
            ForeColor = Color.Gray,
            Location = new Point(6, y),
            AutoSize = true
        };
        _combatPanel.Controls.Add(_tickTimerLabel);
        y += 20;

        _tickProgressBar = new ProgressBar
        {
            Location = new Point(6, y),
            Size = new Size(panelWidth, 10),
            Style = ProgressBarStyle.Continuous
        };
        _combatPanel.Controls.Add(_tickProgressBar);
        y += 14;

        _lastTickTimeLabel = new Label
        {
            Text = "Last: Never",
            Font = new Font("Segoe UI", 7),
            ForeColor = Color.FromArgb(100, 100, 100),
            Location = new Point(6, y),
            AutoSize = true
        };
        _combatPanel.Controls.Add(_lastTickTimeLabel);
        y += 18;

        // Separator
        var separator1 = new Panel
        {
            Location = new Point(6, y),
            Size = new Size(panelWidth, 1),
            BackColor = Color.FromArgb(50, 50, 50)
        };
        _combatPanel.Controls.Add(separator1);
        y += 6;

        // Self Status Section with Clear Buffs link
        _selfStatusPanelBaseY = y;  // Store base Y position
        
        // Clear Buffs clickable label (right-aligned, on same line as player name will be)
        _clearBuffsLabel = new Label
        {
            Text = "Clear Buffs",
            Font = new Font("Segoe UI", 8),
            ForeColor = Color.FromArgb(120, 120, 120),
            BackColor = Color.FromArgb(35, 45, 55),  // Match self status panel background
            Location = new Point(panelWidth - 70, y + 4),
            AutoSize = true,
            Cursor = Cursors.Hand
        };
        _clearBuffsLabel.Click += ClearActiveBuffs_Click;
        _clearBuffsLabel.MouseEnter += (s, e) => _clearBuffsLabel.ForeColor = Color.FromArgb(180, 180, 180);
        _clearBuffsLabel.MouseLeave += (s, e) => _clearBuffsLabel.ForeColor = Color.FromArgb(120, 120, 120);
        _combatPanel.Controls.Add(_clearBuffsLabel);
        
        _selfStatusPanel = new PlayerStatusPanel(isSelf: true)
        {
            Location = new Point(6, y),
            Width = panelWidth
        };
        _selfStatusPanel.UpdatePlayer("(type 'stat' to detect)", "", 100, 100);
        _combatPanel.Controls.Add(_selfStatusPanel);
        
        // Bring clear buffs label to front so it's on top of self status panel
        _clearBuffsLabel.BringToFront();
        
        y += _selfStatusPanel.Height + 6;

        // Separator (store reference for dynamic positioning)
        _partySeparator = new Panel
        {
            Location = new Point(6, y),
            Size = new Size(panelWidth, 1),
            BackColor = Color.FromArgb(50, 50, 50)
        };
        _combatPanel.Controls.Add(_partySeparator);
        y += 6;

        // Party Section (store reference for dynamic positioning)
        _partyLabel = new Label
        {
            Text = "PARTY",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 150, 200),
            Location = new Point(6, y),
            AutoSize = true
        };
        _combatPanel.Controls.Add(_partyLabel);
        y += 16;

        // Party container for party member panels
        _partyContainer = new Panel
        {
            Location = new Point(6, y),
            Width = panelWidth,
            Height = 300, // Will be adjusted dynamically
            BackColor = Color.Transparent,
            AutoScroll = false
        };
        _combatPanel.Controls.Add(_partyContainer);

        // Create placeholder for "no party" message
        var noPartyLabel = new Label
        {
            Text = "(type 'par' to detect party)",
            Font = new Font("Segoe UI", 8),
            ForeColor = Color.FromArgb(80, 80, 80),
            Location = new Point(0, 0),
            AutoSize = true,
            Tag = "noparty"
        };
        _partyContainer.Controls.Add(noPartyLabel);

        // Status bar
        var statusPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 25,
            BackColor = Color.FromArgb(45, 45, 45)
        };

        _statusLabel = new Label
        {
            Text = "Status: Stopped",
            ForeColor = Color.Yellow,
            AutoSize = true,
            Location = new Point(10, 5)
        };
        statusPanel.Controls.Add(_statusLabel);

        // Split container for MUD output and system logs
        _terminalSplitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            BackColor = Color.FromArgb(30, 30, 30),
            SplitterDistance = 400,  // Initial size of top panel
            SplitterWidth = 6,
            Panel1MinSize = 100,
            Panel2MinSize = 60
        };
        
        // MUD output text box (top panel)
        _mudOutputTextBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(12, 12, 12),
            ForeColor = Color.FromArgb(0, 255, 0),
            Font = new Font("Consolas", 10),
            ReadOnly = true,
            BorderStyle = BorderStyle.None
        };
        
        // Command input text box (bottom of MUD output panel)
        _commandInputTextBox = new TextBox
        {
            Dock = DockStyle.Bottom,
            Height = 25,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(0, 255, 0),
            Font = new Font("Consolas", 10),
            BorderStyle = BorderStyle.FixedSingle
        };
        _commandInputTextBox.KeyDown += CommandInput_KeyDown;
        
        // Add to panel (order matters for docking - bottom first)
        _terminalSplitContainer.Panel1.Controls.Add(_mudOutputTextBox);
        _terminalSplitContainer.Panel1.Controls.Add(_commandInputTextBox);
        
        // System log header
        var logHeaderPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 20,
            BackColor = Color.FromArgb(50, 50, 50)
        };
        var logHeaderLabel = new Label
        {
            Text = "📋 System Log",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            Location = new Point(5, 2),
            AutoSize = true
        };
        logHeaderPanel.Controls.Add(logHeaderLabel);
        
        // System log text box (bottom panel)
        _systemLogTextBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.FromArgb(255, 204, 0),
            Font = new Font("Consolas", 9),
            ReadOnly = true,
            BorderStyle = BorderStyle.None
        };
        _terminalSplitContainer.Panel2.Controls.Add(_systemLogTextBox);
        _terminalSplitContainer.Panel2.Controls.Add(logHeaderPanel);
        
        // Keep _logTextBox pointing to MUD output for backward compatibility
        _logTextBox = _mudOutputTextBox;

        // Add a splitter for resizing the combat panel
        var combatSplitter = new Splitter
        {
            Dock = DockStyle.Right,
            Width = 6,
            BackColor = Color.FromArgb(50, 50, 50),
            MinExtra = 300,  // Minimum terminal width
            MinSize = 250    // Minimum combat panel width
        };

        // Add controls in correct order (right to left for docking)
        this.Controls.Add(_terminalSplitContainer);  // Fill - added first
        this.Controls.Add(combatSplitter);           // Splitter between terminal and combat
        this.Controls.Add(_combatPanel);             // Right dock
        this.Controls.Add(statusPanel);
        this.Controls.Add(settingsPanel);
        this.Controls.Add(menuStrip);

        this.FormClosing += MainForm_FormClosing;
        
        // Handle resize to update combat panel layout
        _combatPanel.Resize += (s, e) => UpdateCombatPanelLayout();
        this.Load += (s, e) => UpdateCombatPanelLayout();
    }
    
    private void UpdateCombatPanelLayout()
    {
        if (_combatPanel == null || _selfStatusPanel == null) return;
        
        int panelWidth = _combatPanel.ClientSize.Width - 12;  // Account for padding
        if (panelWidth < 200) panelWidth = 200;
        
        // Update self status panel width
        _selfStatusPanel.Width = panelWidth;
        
        // Update party container and panels width
        if (_partyContainer != null)
        {
            _partyContainer.Width = panelWidth;
            foreach (var panel in _partyStatusPanels)
            {
                panel.Width = panelWidth;
            }
        }
        
        // Update separator widths
        if (_partySeparator != null)
        {
            _partySeparator.Width = panelWidth;
        }
        
        // Update tick progress bar
        if (_tickProgressBar != null)
        {
            _tickProgressBar.Width = panelWidth;
        }
        
        // Update button positions (right-aligned)
        if (_resetTickButton != null && _manualTickButton != null)
        {
            _manualTickButton.Location = new Point(panelWidth - 34, _manualTickButton.Location.Y);
            _resetTickButton.Location = new Point(panelWidth - 82, _resetTickButton.Location.Y);
        }
        
        // Update Clear Buffs label position (right-aligned, inside the border)
        if (_clearBuffsLabel != null)
        {
            _clearBuffsLabel.Location = new Point(panelWidth - 64, _clearBuffsLabel.Location.Y);
        }
        
        // Refresh displays to update buff bar layouts
        _selfStatusPanel.Invalidate();
        foreach (var panel in _partyStatusPanels)
        {
            panel.Invalidate();
        }
    }

    private void RefreshPlayerInfo()
    {
        var info = _buffManager.PlayerInfo;
        if (!string.IsNullOrEmpty(info.Name))
        {
            _selfStatusPanel.UpdatePlayerExact(
                info.Name, 
                info.Class,
                info.CurrentHp,
                info.MaxHp,
                info.CurrentMana,
                info.MaxMana
            );
        }
    }

    private void RefreshBuffDisplay()
    {
        RefreshSelfBuffs();
        RefreshPartyDisplay();
    }

    private void RefreshSelfBuffs()
    {
        var selfBuffs = _buffManager.GetSelfBuffs()
            .Select(b => new BuffDisplayInfo(b))
            .ToList();
        
        _selfStatusPanel.UpdateBuffs(selfBuffs);
        
        // Reposition party section based on self panel's new height
        RepositionPartySection();
    }
    
    private void RepositionPartySection()
    {
        int y = _selfStatusPanelBaseY + _selfStatusPanel.Height + 6;
        
        _partySeparator.Location = new Point(6, y);
        y += 7;  // separator height + spacing
        
        _partyLabel.Location = new Point(6, y);
        y += 16;
        
        _partyContainer.Location = new Point(6, y);
    }

    private void RefreshPartyDisplay()
    {
        var partyMembers = _buffManager.PartyMembers
            .Where(m => !_buffManager.IsTargetSelf(m.Name))
            .ToList();
        
        var partyBuffs = _buffManager.GetPartyBuffs().ToList();
        
        // Remove "no party" label if we have members
        var noPartyLabel = _partyContainer.Controls.Cast<Control>()
            .FirstOrDefault(c => c.Tag?.ToString() == "noparty");
        
        if (partyMembers.Count == 0)
        {
            // Show "no party" message
            if (noPartyLabel == null)
            {
                noPartyLabel = new Label
                {
                    Text = "(type 'par' to detect party)",
                    Font = new Font("Segoe UI", 8),
                    ForeColor = Color.FromArgb(80, 80, 80),
                    Location = new Point(0, 0),
                    AutoSize = true,
                    Tag = "noparty"
                };
                _partyContainer.Controls.Add(noPartyLabel);
            }
            noPartyLabel.Visible = true;
            
            // Hide all party panels
            foreach (var panel in _partyStatusPanels)
            {
                panel.Visible = false;
            }
            
            _partyContainer.Height = 20;
            return;
        }
        
        // Hide "no party" label
        if (noPartyLabel != null)
        {
            noPartyLabel.Visible = false;
        }
        
        // Ensure we have enough panels (max 5 party members excluding self)
        while (_partyStatusPanels.Count < partyMembers.Count)
        {
            var panel = new PlayerStatusPanel(isSelf: false)
            {
                Width = _partyContainer.Width,
                Visible = false
            };
            _partyStatusPanels.Add(panel);
            _partyContainer.Controls.Add(panel);
        }
        
        // Update panels with party member data
        int y = 0;
        for (int i = 0; i < partyMembers.Count; i++)
        {
            var member = partyMembers[i];
            var panel = _partyStatusPanels[i];
            
            // Get buffs for this party member
            var memberBuffs = partyBuffs
                .Where(b => b.TargetName.Equals(member.Name, StringComparison.OrdinalIgnoreCase))
                .Select(b => new BuffDisplayInfo(b))
                .ToList();
            
            // Use actual HP/Mana values if available from telepath
            if (member.HasActualHpData)
            {
                panel.UpdatePlayerExact(
                    member.Name,
                    member.Class,
                    member.CurrentHp,
                    member.MaxHp,
                    member.CurrentMana,
                    member.MaxMana,
                    member.IsPoisoned,
                    member.IsResting,
                    member.ResourceType
                );
            }
            else
            {
                panel.UpdatePlayer(
                    member.Name,
                    member.Class,
                    member.EffectiveHealthPercent,
                    member.EffectiveManaPercent,
                    member.IsPoisoned,
                    member.IsResting,
                    member.ResourceType
                );
            }
            panel.UpdateBuffs(memberBuffs);
            
            panel.Location = new Point(0, y);
            panel.Visible = true;
            
            y += panel.Height + 4;
        }
        
        // Hide unused panels
        for (int i = partyMembers.Count; i < _partyStatusPanels.Count; i++)
        {
            _partyStatusPanels[i].Visible = false;
        }
        
        _partyContainer.Height = y;
    }

    private void RefreshBuffTimers()
    {
        if (InvokeRequired)
        {
            BeginInvoke(RefreshBuffTimers);
            return;
        }

        // Update self buffs
        RefreshSelfBuffs();
        
        // Update party display (to refresh buff timers)
        RefreshPartyDisplay();
    }

    private void ToggleAutoRecast_Click(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
        {
            _buffManager.AutoRecastEnabled = item.Checked;
            LogMessage($"Auto-recast {(item.Checked ? "ENABLED" : "DISABLED")}", MessageType.System);
        }
    }
    
    private void SetManaReserve_Click(object? sender, EventArgs e)
    {
        using var dialog = new Form
        {
            Text = "Mana Reserve",
            Size = new Size(350, 150),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(45, 45, 45)
        };
        
        var label = new Label
        {
            Text = "Don't auto-cast if mana below:",
            Location = new Point(15, 20),
            AutoSize = true,
            ForeColor = Color.White
        };
        dialog.Controls.Add(label);
        
        var numeric = new NumericUpDown
        {
            Location = new Point(200, 18),
            Width = 60,
            Minimum = 0,
            Maximum = 100,
            Value = _buffManager.ManaReservePercent,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        dialog.Controls.Add(numeric);
        
        var percentLabel = new Label
        {
            Text = "%",
            Location = new Point(265, 20),
            AutoSize = true,
            ForeColor = Color.White
        };
        dialog.Controls.Add(percentLabel);
        
        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(150, 70),
            Size = new Size(75, 28),
            BackColor = Color.FromArgb(0, 120, 0),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        dialog.Controls.Add(okButton);
        dialog.AcceptButton = okButton;
        
        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(235, 70),
            Size = new Size(75, 28),
            BackColor = Color.FromArgb(80, 80, 80),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        dialog.Controls.Add(cancelButton);
        dialog.CancelButton = cancelButton;
        
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _buffManager.ManaReservePercent = (int)numeric.Value;
            LogMessage($"Mana reserve set to {_buffManager.ManaReservePercent}%", MessageType.System);
        }
    }

    private void ManageBuffs_Click(object? sender, EventArgs e)
    {
        using var dialog = new BuffListDialog(_buffManager);
        dialog.ShowDialog(this);
    }
    
    private void OpenSettings_Click(object? sender, EventArgs e)
    {
        var currentCharacter = _buffManager.PlayerInfo.Name;
        using var dialog = new SettingsDialog(_buffManager, _buffManager.CombatManager, currentCharacter);
        dialog.ShowDialog(this);
        // Refresh toggle button states after settings close
        UpdateToggleButtonStates();
    }
    
    private void CombatToggleButton_Click(object? sender, EventArgs e)
    {
        _buffManager.CombatManager.CombatEnabled = !_buffManager.CombatManager.CombatEnabled;
        UpdateToggleButtonStates();
        LogMessage($"Auto-combat {(_buffManager.CombatManager.CombatEnabled ? "ENABLED" : "DISABLED")}", MessageType.System);
    }
    
    private void HealToggleButton_Click(object? sender, EventArgs e)
    {
        _buffManager.HealingManager.HealingEnabled = !_buffManager.HealingManager.HealingEnabled;
        UpdateToggleButtonStates();
        LogMessage($"Auto-healing {(_buffManager.HealingManager.HealingEnabled ? "ENABLED" : "DISABLED")}", MessageType.System);
    }
    
    private void BuffToggleButton_Click(object? sender, EventArgs e)
    {
        _buffManager.AutoRecastEnabled = !_buffManager.AutoRecastEnabled;
        UpdateToggleButtonStates();
        LogMessage($"Auto-buffing {(_buffManager.AutoRecastEnabled ? "ENABLED" : "DISABLED")}", MessageType.System);
    }
    
    private void CureToggleButton_Click(object? sender, EventArgs e)
    {
        _buffManager.CureManager.CuringEnabled = !_buffManager.CureManager.CuringEnabled;
        UpdateToggleButtonStates();
        LogMessage($"Auto-curing {(_buffManager.CureManager.CuringEnabled ? "ENABLED" : "DISABLED")}", MessageType.System);
    }
    
    private void PauseButton_Click(object? sender, EventArgs e)
    {
        _buffManager.CommandsPaused = !_buffManager.CommandsPaused;
        UpdateToggleButtonStates();
        LogMessage($"All commands {(_buffManager.CommandsPaused ? "PAUSED" : "RESUMED")}", MessageType.System);
    }
    
    private void UpdateToggleButtonStates()
    {
        var enabledColor = Color.FromArgb(70, 130, 180);  // Blue when enabled
        var disabledColor = Color.FromArgb(60, 60, 60);   // Gray when disabled
        var pausedColor = Color.FromArgb(180, 100, 50);   // Orange when paused
        
        // Pause button shows orange when paused or when in training screen
        if (_buffManager.ShouldPauseCommands)
        {
            _pauseButton.BackColor = pausedColor;
            _pauseButton.Text = ">";  // Play icon to indicate "click to resume"
        }
        else
        {
            _pauseButton.BackColor = disabledColor;
            _pauseButton.Text = "||";  // Pause icon to indicate "click to pause"
        }
        
        _combatToggleButton.BackColor = _buffManager.CombatManager.CombatEnabled ? enabledColor : disabledColor;
        _healToggleButton.BackColor = _buffManager.HealingManager.HealingEnabled ? enabledColor : disabledColor;
        _buffToggleButton.BackColor = _buffManager.AutoRecastEnabled ? enabledColor : disabledColor;
        _cureToggleButton.BackColor = _buffManager.CureManager.CuringEnabled ? enabledColor : disabledColor;
    }

    private void ClearActiveBuffs_Click(object? sender, EventArgs e)
    {
        if (MessageBox.Show("Clear all active buff timers?", "Confirm", 
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            _buffManager.ClearAllActiveBuffs();
            LogMessage("All active buffs cleared.", MessageType.System);
        }
    }
    
    private void ConfigureHealing_Click(object? sender, EventArgs e)
    {
        using var dialog = new HealingConfigDialog(_buffManager.HealingManager);
        dialog.ShowDialog(this);
    }
    
    private void ToggleHealing_Click(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
        {
            _buffManager.HealingManager.HealingEnabled = item.Checked;
            LogMessage($"Auto-healing {(item.Checked ? "ENABLED" : "DISABLED")}", MessageType.System);
        }
    }
    
    private void ConfigureCures_Click(object? sender, EventArgs e)
    {
        using var dialog = new CureConfigDialog(_buffManager.CureManager);
        dialog.ShowDialog(this);
    }
    
    private void ToggleCuring_Click(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
        {
            _buffManager.CureManager.CuringEnabled = item.Checked;
            LogMessage($"Auto-curing {(item.Checked ? "ENABLED" : "DISABLED")}", MessageType.System);
        }
    }
    
    private void ClearAilments_Click(object? sender, EventArgs e)
    {
        _buffManager.CureManager.ClearAllAilments();
        LogMessage("All active ailments cleared.", MessageType.System);
    }
    
    #region Export/Import
    
    private void ExportBuffs_Click(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export Buffs",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json",
            FileName = "buffs_export.json"
        };
        
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            try
            {
                var json = _buffManager.ExportBuffs();
                File.WriteAllText(dialog.FileName, json);
                LogMessage($"Exported buffs to {dialog.FileName}", MessageType.System);
                MessageBox.Show($"Successfully exported {_buffManager.BuffConfigurations.Count} buff(s).", 
                    "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting buffs: {ex.Message}", "Export Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    
    private void ExportHeals_Click(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export Heals",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json",
            FileName = "heals_export.json"
        };
        
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            try
            {
                var json = _buffManager.HealingManager.ExportHeals();
                File.WriteAllText(dialog.FileName, json);
                LogMessage($"Exported heals to {dialog.FileName}", MessageType.System);
                MessageBox.Show("Successfully exported heal spells and rules.", 
                    "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting heals: {ex.Message}", "Export Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    
    private void ExportCures_Click(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export Cures",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json",
            FileName = "cures_export.json"
        };
        
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            try
            {
                var json = _buffManager.CureManager.ExportCures();
                File.WriteAllText(dialog.FileName, json);
                LogMessage($"Exported cures to {dialog.FileName}", MessageType.System);
                MessageBox.Show("Successfully exported ailments and cure spells.", 
                    "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting cures: {ex.Message}", "Export Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    
    private void ImportBuffs_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import Buffs",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };
        
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            try
            {
                var json = File.ReadAllText(dialog.FileName);
                
                var replaceResult = MessageBox.Show(
                    "Replace existing buffs with the same name?\n\n" +
                    "Yes = Replace duplicates\n" +
                    "No = Skip duplicates",
                    "Import Options", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                
                if (replaceResult == DialogResult.Cancel) return;
                
                var (imported, skipped, message) = _buffManager.ImportBuffs(json, replaceResult == DialogResult.Yes);
                LogMessage(message, MessageType.System);
                MessageBox.Show(message, "Import Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error importing buffs: {ex.Message}", "Import Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    
    private void ImportHeals_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import Heals",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };
        
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            try
            {
                var json = File.ReadAllText(dialog.FileName);
                
                var replaceResult = MessageBox.Show(
                    "Replace existing heal spells with the same name?\n\n" +
                    "Yes = Replace duplicates\n" +
                    "No = Skip duplicates\n\n" +
                    "Note: Heal rules will be added (not replaced).",
                    "Import Options", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                
                if (replaceResult == DialogResult.Cancel) return;
                
                var (imported, skipped, message) = _buffManager.HealingManager.ImportHeals(json, replaceResult == DialogResult.Yes);
                LogMessage(message, MessageType.System);
                MessageBox.Show(message, "Import Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error importing heals: {ex.Message}", "Import Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    
    private void ImportCures_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import Cures",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };
        
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            try
            {
                var json = File.ReadAllText(dialog.FileName);
                
                var replaceResult = MessageBox.Show(
                    "Replace existing ailments and cure spells with the same name?\n\n" +
                    "Yes = Replace duplicates\n" +
                    "No = Skip duplicates",
                    "Import Options", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                
                if (replaceResult == DialogResult.Cancel) return;
                
                var (imported, skipped, message) = _buffManager.CureManager.ImportCures(json, replaceResult == DialogResult.Yes);
                LogMessage(message, MessageType.System);
                MessageBox.Show(message, "Import Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error importing cures: {ex.Message}", "Import Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    
    #endregion
    
    private void ToggleParAuto_Click(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
        {
            _buffManager.ParAutoEnabled = item.Checked;
            LogMessage($"Auto 'par' command {(item.Checked ? "ENABLED" : "DISABLED")}", MessageType.System);
        }
    }
    
    private void OpenPlayerDB_Click(object? sender, EventArgs e)
    {
        using var dialog = new PlayerDatabaseDialog(_buffManager.PlayerDatabase);
        dialog.ShowDialog(this);
    }
    
    private void OpenMonsterDB_Click(object? sender, EventArgs e)
    {
        using var dialog = new MonsterDatabaseDialog(_buffManager.MonsterDatabase);
        dialog.ShowDialog(this);
    }
    
    private void SetParFrequency_Click(object? sender, EventArgs e)
    {
        using var dialog = new Form
        {
            Text = "Par Command Frequency",
            Size = new Size(350, 150),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(45, 45, 45)
        };
        
        var label = new Label
        {
            Text = "Send 'par' command every:",
            Location = new Point(15, 20),
            AutoSize = true,
            ForeColor = Color.White
        };
        dialog.Controls.Add(label);
        
        var numeric = new NumericUpDown
        {
            Location = new Point(180, 18),
            Width = 60,
            Minimum = 5,
            Maximum = 300,
            Value = _buffManager.ParFrequencySeconds,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        dialog.Controls.Add(numeric);
        
        var secondsLabel = new Label
        {
            Text = "seconds",
            Location = new Point(245, 20),
            AutoSize = true,
            ForeColor = Color.White
        };
        dialog.Controls.Add(secondsLabel);
        
        var okButton = new Button
        {
            Text = "OK",
            Location = new Point(165, 70),
            Size = new Size(75, 30),
            DialogResult = DialogResult.OK,
            BackColor = Color.FromArgb(0, 120, 0),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        dialog.Controls.Add(okButton);
        dialog.AcceptButton = okButton;
        
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _buffManager.ParFrequencySeconds = (int)numeric.Value;
            LogMessage($"Par frequency set to {_buffManager.ParFrequencySeconds} seconds", MessageType.System);
        }
    }
    
    private void ToggleParAfterTick_Click(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
        {
            _buffManager.ParAfterCombatTick = item.Checked;
            LogMessage($"Send 'par' after combat tick: {(item.Checked ? "YES" : "NO")}", MessageType.System);
        }
    }
    
    private void SendParNow_Click(object? sender, EventArgs e)
    {
        SendCommandToServer("par");
        LogMessage("Manually sent 'par' command", MessageType.System);
    }
    
    private void ToggleHealthRequest_Click(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
        {
            _buffManager.HealthRequestEnabled = item.Checked;
            LogMessage($"Auto-request health data: {(item.Checked ? "ENABLED" : "DISABLED")}", MessageType.System);
        }
    }
    
    private void SetHealthRequestInterval_Click(object? sender, EventArgs e)
    {
        using var dialog = new Form
        {
            Text = "Health Request Interval",
            Size = new Size(350, 150),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(45, 45, 45)
        };
        
        var label = new Label
        {
            Text = "Request health data every:",
            Location = new Point(15, 20),
            AutoSize = true,
            ForeColor = Color.White
        };
        dialog.Controls.Add(label);
        
        var numeric = new NumericUpDown
        {
            Location = new Point(180, 18),
            Width = 60,
            Minimum = 30,
            Maximum = 300,
            Value = _buffManager.HealthRequestIntervalSeconds,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        dialog.Controls.Add(numeric);
        
        var secondsLabel = new Label
        {
            Text = "seconds",
            Location = new Point(245, 20),
            AutoSize = true,
            ForeColor = Color.White
        };
        dialog.Controls.Add(secondsLabel);
        
        var okButton = new Button
        {
            Text = "OK",
            Location = new Point(165, 70),
            Size = new Size(75, 30),
            DialogResult = DialogResult.OK,
            BackColor = Color.FromArgb(0, 120, 0),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        dialog.Controls.Add(okButton);
        dialog.AcceptButton = okButton;
        
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _buffManager.HealthRequestIntervalSeconds = (int)numeric.Value;
            LogMessage($"Health request interval set to {_buffManager.HealthRequestIntervalSeconds} seconds", MessageType.System);
        }
    }

    private void ResetTickTimer_Click(object? sender, EventArgs e)
    {
        _lastTickTime = null;
        _nextTickTime = null;
        _lastTickTimeLabel.Text = "Last Tick: Reset";
        LogMessage("Tick timer reset.", MessageType.System);
    }

    private void ManualTick_Click(object? sender, EventArgs e)
    {
        RecordTick();
        LogMessage("Manual tick marked.", MessageType.System);
    }

    private void RecordTick()
    {
        _lastTickTime = DateTime.Now;
        _nextTickTime = _lastTickTime.Value.AddMilliseconds(TICK_INTERVAL_MS);

        if (InvokeRequired)
            BeginInvoke(() => _lastTickTimeLabel.Text = $"Last Tick: {_lastTickTime:HH:mm:ss.f}");
        else
            _lastTickTimeLabel.Text = $"Last Tick: {_lastTickTime:HH:mm:ss.f}";
        
        // Unblock casting since a new tick just happened
        _buffManager.OnCombatTick();
        
        // Check if we should send an attack spell on this tick (mana may have regenerated)
        _buffManager.CombatManager.OnCombatTick();
        
        // Check for auto-recast after each tick (this is the mid-round window)
        // Small delay to let the tick fully process before recasting
        Task.Delay(100).ContinueWith(_ => _buffManager.CheckAutoRecast());
    }

    private void ProcessServerMessage(string text)
    {
        // Pass to buff manager for processing
        var wasPaused = _buffManager.ShouldPauseCommands;
        _buffManager.ProcessMessage(text);
        
        // Pass to combat manager for room parsing
        _buffManager.CombatManager.ProcessMessage(text);
        
        // Update pause button if state changed (e.g., training screen detected/exited)
        if (wasPaused != _buffManager.ShouldPauseCommands)
        {
            if (InvokeRequired)
                BeginInvoke(UpdateToggleButtonStates);
            else
                UpdateToggleButtonStates();
        }

        // Combat state changes
        if (CombatEngagedRegex.IsMatch(text))
        {
            SetCombatState(true);
            _buffManager.CombatManager.OnCombatEngaged();
        }
        else if (CombatOffRegex.IsMatch(text))
        {
            SetCombatState(false);
            _buffManager.CombatManager.OnCombatEnded();
        }

        // HP/Mana updates
        var hpMatch = HpManaRegex.Match(text);
        if (hpMatch.Success)
        {
            UpdatePlayerStats(hpMatch);
            
            // HP bar means we're in-game - login phase complete
            if (_isInLoginPhase)
            {
                _isInLoginPhase = false;
                LogMessage("✅ Login complete - entered game", MessageType.System);
            }
        }

        // Damage detection for tick timing
        if (DamageRegex.IsMatch(text))
            DetectCombatTick();

        // Death detection
        if (PlayerDeathRegex.IsMatch(text))
        {
            SetCombatState(false);
            LogMessage("☠️ YOU DIED!", MessageType.System);
        }
    }

    private void DetectCombatTick()
    {
        var now = DateTime.Now;
        var timeSinceLastDamage = (now - _lastDamageMessageTime).TotalMilliseconds;

        if (timeSinceLastDamage < DAMAGE_CLUSTER_WINDOW_MS)
        {
            _damageMessageCount++;
        }
        else
        {
            _damageMessageCount = 1;

            // Always detect tick when we see damage - combat state doesn't matter
            // The global tick runs regardless of whether WE are in combat
            if (_nextTickTime.HasValue)
            {
                var drift = Math.Abs((now - _nextTickTime.Value).TotalMilliseconds);
                // If we're close to expected tick time, or way off, record it
                if (drift < 1500 || drift > 3500)
                    RecordTick();
            }
            else
            {
                // First tick detection - establish timing
                RecordTick();
            }
        }

        _lastDamageMessageTime = now;
    }

    private void SetCombatState(bool inCombat)
    {
        _inCombat = inCombat;
        
        // Notify BuffManager of combat state change
        _buffManager.SetCombatState(inCombat);

        if (InvokeRequired)
            BeginInvoke(UpdateCombatStateUI);
        else
            UpdateCombatStateUI();
    }

    private void UpdateCombatStateUI()
    {
        if (_inCombat)
        {
            _combatStateLabel.Text = "⚔️ ENGAGED";
            _combatStateLabel.ForeColor = Color.Red;
        }
        else
        {
            _combatStateLabel.Text = "DISENGAGED";
            _combatStateLabel.ForeColor = Color.Gray;
        }
    }

    private void UpdatePlayerStats(Match match)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdatePlayerStats(match));
            return;
        }

        if (int.TryParse(match.Groups[1].Value, out int hp))
        {
            _currentHp = hp;
            if (match.Groups[2].Success && int.TryParse(match.Groups[2].Value, out int maxHp))
                _maxHp = maxHp;
            else if (_currentHp > _maxHp)
                _maxHp = _currentHp;
        }

        _manaType = match.Groups[3].Value;
        if (int.TryParse(match.Groups[4].Value, out int mana))
        {
            _currentMana = mana;
            if (match.Groups[5].Success && int.TryParse(match.Groups[5].Value, out int maxMana))
                _maxMana = maxMana;
            else if (_currentMana > _maxMana)
                _maxMana = _currentMana;
        }

        // Update the self status panel
        var info = _buffManager.PlayerInfo;
        _selfStatusPanel.UpdatePlayerExact(
            string.IsNullOrEmpty(info.Name) ? "(Unknown)" : info.Name,
            info.Class,
            _currentHp,
            _maxHp,
            _currentMana,
            _maxMana
        );
    }

    private async void ConnectButton_Click(object? sender, EventArgs e)
    {
        if (!_isConnected)
            await Connect();
        else
            Disconnect();
    }

    private async Task Connect()
    {
        // Check if we have BBS settings
        if (string.IsNullOrEmpty(_serverAddress))
        {
            MessageBox.Show("Please load a character profile with BBS settings first.\n\nUse File → Load Character to load a profile.", 
                "No Profile Loaded", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            _serverConnection = new TcpClient();
            
            UpdateStatus("Connecting...", Color.Yellow);
            LogMessage($"Connecting to {_serverAddress}:{_serverPort}...", MessageType.System);
            
            await _serverConnection.ConnectAsync(_serverAddress, _serverPort, _cancellationTokenSource.Token);
            _serverStream = _serverConnection.GetStream();
            
            _isConnected = true;
            _isInLoginPhase = true;
            _triggeredLogonSequences.Clear();
            
            UpdateConnectionUI(true);
            LogMessage($"Connected to {_serverAddress}:{_serverPort}", MessageType.System);
            UpdateStatus("Connected", Color.LimeGreen);
            
            // Reset combat state on new connection
            _buffManager.CombatManager.ResetState();

            // Start reading from server
            await ReadServerDataAsync(_cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            LogMessage($"Connection failed: {ex.Message}", MessageType.System);
            MessageBox.Show($"Failed to connect to {_serverAddress}:{_serverPort}.\n\n{ex.Message}",
                "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Disconnect();
        }
    }

    private void Disconnect()
    {
        _isConnected = false;
        _cancellationTokenSource?.Cancel();

        try { _serverStream?.Close(); } catch { }
        try { _serverConnection?.Close(); } catch { }

        _serverStream = null;
        _serverConnection = null;

        UpdateConnectionUI(false);
        LogMessage("Disconnected.", MessageType.System);
        UpdateStatus("Disconnected", Color.Gray);
    }

    private void UpdateConnectionUI(bool connected)
    {
        _connectButton.Text = connected ? "Disconnect" : "Connect";
        _connectButton.BackColor = connected ? Color.FromArgb(180, 0, 0) : Color.FromArgb(0, 120, 0);
    }

    private async Task ReadServerDataAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _serverStream != null)
            {
                int bytesRead = await _serverStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead == 0) break;

                // Process telnet IAC commands and get clean data
                var (cleanData, iacResponses) = ProcessTelnetData(buffer, bytesRead);
                
                // Send any IAC responses back to server
                foreach (var response in iacResponses)
                {
                    await SendRawDataAsync(response);
                }

                if (cleanData.Length > 0)
                {
                    string text = Encoding.GetEncoding(437).GetString(cleanData);
                    
                    // Process for game logic (uses stripped version internally)
                    string strippedText = StripAnsi(text);
                    ProcessServerMessage(strippedText);
                    
                    // Check for logon automation triggers
                    if (_isInLoginPhase)
                    {
                        CheckLogonAutomation(strippedText);
                    }

                    // Display with ANSI colors
                    if (!string.IsNullOrEmpty(text.Trim()))
                    {
                        LogMessageWithAnsi(text, MessageType.Server);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                LogMessage($"Read error: {ex.Message}", MessageType.System);
        }
    }

    /// <summary>
    /// Process telnet IAC (Interpret As Command) sequences
    /// Returns clean data with IAC commands stripped, plus any responses to send
    /// </summary>
    private (byte[] cleanData, List<byte[]> responses) ProcessTelnetData(byte[] buffer, int length)
    {
        const byte IAC = 255;  // Interpret As Command
        const byte WILL = 251;
        const byte WONT = 252;
        const byte DO = 253;
        const byte DONT = 254;
        const byte SB = 250;   // Subnegotiation Begin
        const byte SE = 240;   // Subnegotiation End

        var cleanData = new List<byte>();
        var responses = new List<byte[]>();
        int i = 0;

        while (i < length)
        {
            if (buffer[i] == IAC && i + 1 < length)
            {
                byte command = buffer[i + 1];

                if (command == IAC)
                {
                    // Escaped IAC (255 255) = literal 255
                    cleanData.Add(IAC);
                    i += 2;
                }
                else if ((command == WILL || command == WONT || command == DO || command == DONT) && i + 2 < length)
                {
                    byte option = buffer[i + 2];
                    
                    // Respond to negotiations - generally refuse everything for simplicity
                    if (command == WILL)
                    {
                        // Server offers to do something - we say DONT
                        responses.Add(new byte[] { IAC, DONT, option });
                    }
                    else if (command == DO)
                    {
                        // Server asks us to do something - we say WONT
                        responses.Add(new byte[] { IAC, WONT, option });
                    }
                    // WONT and DONT are acknowledgments, no response needed
                    
                    i += 3;
                }
                else if (command == SB)
                {
                    // Subnegotiation - skip until SE
                    i += 2;
                    while (i < length)
                    {
                        if (buffer[i] == IAC && i + 1 < length && buffer[i + 1] == SE)
                        {
                            i += 2;
                            break;
                        }
                        i++;
                    }
                }
                else
                {
                    // Unknown command, skip IAC and command byte
                    i += 2;
                }
            }
            else
            {
                cleanData.Add(buffer[i]);
                i++;
            }
        }

        return (cleanData.ToArray(), responses);
    }

    /// <summary>
    /// Check incoming text for logon automation triggers
    /// </summary>
    private void CheckLogonAutomation(string text)
    {
        var sequences = _buffManager.BbsSettings.LogonSequences;
        
        foreach (var seq in sequences)
        {
            // Skip if already triggered this session
            if (_triggeredLogonSequences.Contains(seq.TriggerMessage))
                continue;
                
            // Check for exact match (case-insensitive, anywhere in text)
            if (text.Contains(seq.TriggerMessage, StringComparison.OrdinalIgnoreCase))
            {
                _triggeredLogonSequences.Add(seq.TriggerMessage);
                LogMessage($"🔑 Logon trigger matched: \"{seq.TriggerMessage}\"", MessageType.System);
                
                // Send the response with a small delay
                Task.Run(async () =>
                {
                    await Task.Delay(100);  // Small delay for more natural feel
                    await SendCommandAsync(seq.Response);
                });
            }
        }
    }

    /// <summary>
    /// Send raw bytes to server
    /// </summary>
    private async Task SendRawDataAsync(byte[] data)
    {
        if (_serverStream == null || !_isConnected) return;
        
        try
        {
            await _serverStream.WriteAsync(data, 0, data.Length);
        }
        catch (Exception ex)
        {
            LogMessage($"Send error: {ex.Message}", MessageType.System);
        }
    }

    /// <summary>
    /// Send a command to the server (adds carriage return/line feed)
    /// </summary>
    public async Task SendCommandAsync(string command)
    {
        if (_serverStream == null || !_isConnected)
        {
            LogMessage("Cannot send command - not connected", MessageType.System);
            return;
        }
        
        try
        {
            var data = Encoding.GetEncoding(437).GetBytes(command + "\r\n");
            await _serverStream.WriteAsync(data, 0, data.Length);
            // Don't log here - server will echo the command back
        }
        catch (Exception ex)
        {
            LogMessage($"Send error: {ex.Message}", MessageType.System);
        }
    }

    private string StripAnsi(string text) => AnsiRegex.Replace(text, string.Empty);

    /// <summary>
    /// Log a message with ANSI color code interpretation
    /// </summary>
    private void LogMessageWithAnsi(string message, MessageType type)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => LogMessageWithAnsi(message, type));
            return;
        }

        RichTextBox targetTextBox = type == MessageType.System ? _systemLogTextBox : _mudOutputTextBox;
        CheckBox autoScrollCheckBox = type == MessageType.System ? _autoScrollLogsCheckBox : _autoScrollCheckBox;

        // Trim log if needed
        _logMessageCount++;
        if (_logMessageCount % 100 == 0)
        {
            TrimLogIfNeeded(targetTextBox);
        }

        // Only add timestamps to system log, not MUD output
        if (type == MessageType.System && _showTimestampsCheckBox.Checked)
        {
            targetTextBox.SelectionStart = targetTextBox.TextLength;
            targetTextBox.SelectionColor = Color.Gray;
            targetTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] ");
        }

        // Parse and render ANSI codes
        int pos = 0;
        Color currentColor = Color.FromArgb(192, 192, 192);  // Default gray
        bool isBold = false;

        while (pos < message.Length)
        {
            // Look for ANSI escape sequence
            int escPos = message.IndexOf('\x1B', pos);
            
            if (escPos == -1)
            {
                // No more escape sequences, output rest of string
                string remaining = message.Substring(pos);
                if (!string.IsNullOrEmpty(remaining))
                {
                    targetTextBox.SelectionStart = targetTextBox.TextLength;
                    targetTextBox.SelectionColor = isBold ? BrightenColor(currentColor) : currentColor;
                    targetTextBox.AppendText(remaining);
                }
                break;
            }

            // Output text before escape sequence
            if (escPos > pos)
            {
                string textBefore = message.Substring(pos, escPos - pos);
                targetTextBox.SelectionStart = targetTextBox.TextLength;
                targetTextBox.SelectionColor = isBold ? BrightenColor(currentColor) : currentColor;
                targetTextBox.AppendText(textBefore);
            }

            // Parse escape sequence - skip ESC character
            int seqEnd = escPos + 1;
            
            if (seqEnd < message.Length)
            {
                char nextChar = message[seqEnd];
                
                if (nextChar == '[')
                {
                    // CSI (Control Sequence Introducer) - ESC[
                    seqEnd++;
                    int codeStart = seqEnd;
                    
                    // Read parameters (digits and semicolons)
                    while (seqEnd < message.Length && (char.IsDigit(message[seqEnd]) || message[seqEnd] == ';' || message[seqEnd] == '?'))
                        seqEnd++;
                    
                    // Read the final character that determines the command
                    if (seqEnd < message.Length)
                    {
                        char cmdChar = message[seqEnd];
                        seqEnd++; // Skip command character
                        
                        if (cmdChar == 'm')
                        {
                            // SGR (Select Graphic Rendition) - colors
                            string codes = message.Substring(codeStart, seqEnd - codeStart - 1);
                            foreach (var code in codes.Split(';'))
                            {
                                if (int.TryParse(code, out int codeNum))
                                {
                                    switch (codeNum)
                                    {
                                        case 0: currentColor = Color.FromArgb(192, 192, 192); isBold = false; break;
                                        case 1: isBold = true; break;
                                        case 30: currentColor = Color.FromArgb(0, 0, 0); break;
                                        case 31: currentColor = Color.FromArgb(170, 0, 0); break;
                                        case 32: currentColor = Color.FromArgb(0, 170, 0); break;
                                        case 33: currentColor = Color.FromArgb(170, 85, 0); break;
                                        case 34: currentColor = Color.FromArgb(0, 0, 170); break;
                                        case 35: currentColor = Color.FromArgb(170, 0, 170); break;
                                        case 36: currentColor = Color.FromArgb(0, 170, 170); break;
                                        case 37: currentColor = Color.FromArgb(192, 192, 192); break;
                                    }
                                }
                            }
                        }
                        // All other CSI sequences (K, J, H, A, B, C, D, etc.) are silently ignored
                        // They're cursor/display control commands not applicable to RichTextBox
                    }
                }
                else if (nextChar == '(' || nextChar == ')')
                {
                    // Character set selection - ESC( or ESC) followed by one character
                    seqEnd++;
                    if (seqEnd < message.Length) seqEnd++; // Skip the charset identifier
                }
                else if (nextChar >= '0' && nextChar <= '9')
                {
                    // Some other escape with digit
                    seqEnd++;
                }
                else if (nextChar == '=' || nextChar == '>' || nextChar == '<')
                {
                    // Keypad mode
                    seqEnd++;
                }
                else if (nextChar == 'M' || nextChar == 'D' || nextChar == 'E' || nextChar == '7' || nextChar == '8')
                {
                    // Single character escapes (reverse index, index, next line, save/restore cursor)
                    seqEnd++;
                }
                else
                {
                    // Unknown escape - skip just the ESC
                    // seqEnd is already at escPos + 1
                }
            }
            
            pos = seqEnd;
        }

        // Add newline
        targetTextBox.AppendText(Environment.NewLine);

        // Auto-scroll
        if (autoScrollCheckBox.Checked)
        {
            targetTextBox.SelectionStart = targetTextBox.TextLength;
            targetTextBox.ScrollToCaret();
        }
    }

    /// <summary>
    /// Brighten a color for bold/bright ANSI codes
    /// </summary>
    private Color BrightenColor(Color color)
    {
        // Standard bright versions
        if (color == Color.FromArgb(0, 0, 0)) return Color.FromArgb(85, 85, 85);           // Bright black (gray)
        if (color == Color.FromArgb(170, 0, 0)) return Color.FromArgb(255, 85, 85);        // Bright red
        if (color == Color.FromArgb(0, 170, 0)) return Color.FromArgb(85, 255, 85);        // Bright green
        if (color == Color.FromArgb(170, 85, 0)) return Color.FromArgb(255, 255, 85);      // Bright yellow
        if (color == Color.FromArgb(0, 0, 170)) return Color.FromArgb(85, 85, 255);        // Bright blue
        if (color == Color.FromArgb(170, 0, 170)) return Color.FromArgb(255, 85, 255);     // Bright magenta
        if (color == Color.FromArgb(0, 170, 170)) return Color.FromArgb(85, 255, 255);     // Bright cyan
        if (color == Color.FromArgb(192, 192, 192)) return Color.FromArgb(255, 255, 255);  // Bright white
        return color;
    }

    private void LogMessage(string message, MessageType type)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => LogMessage(message, type));
            return;
        }

        // Determine which textbox to use
        RichTextBox targetTextBox;
        CheckBox autoScrollCheckBox;
        
        if (type == MessageType.System)
        {
            // System messages go to the system log
            targetTextBox = _systemLogTextBox;
            autoScrollCheckBox = _autoScrollLogsCheckBox;
        }
        else
        {
            // Server and Client messages go to MUD output
            targetTextBox = _mudOutputTextBox;
            autoScrollCheckBox = _autoScrollCheckBox;
        }

        Color color = type switch
        {
            MessageType.Server => Color.FromArgb(0, 255, 0),
            MessageType.Client => Color.FromArgb(0, 191, 255),
            MessageType.System => Color.FromArgb(255, 204, 0),
            _ => Color.White
        };

        string prefix = type == MessageType.Client ? ">>> " : "";
        string timestamp = _showTimestampsCheckBox.Checked ? $"[{DateTime.Now:HH:mm:ss}] " : "";

        // Trim log if it's getting too long (check every 100 messages for performance)
        _logMessageCount++;
        if (_logMessageCount % 100 == 0)
        {
            TrimLogIfNeeded(targetTextBox);
        }

        targetTextBox.SelectionStart = targetTextBox.TextLength;
        targetTextBox.SelectionLength = 0;

        if (!string.IsNullOrEmpty(timestamp))
        {
            targetTextBox.SelectionColor = Color.Gray;
            targetTextBox.AppendText(timestamp);
        }

        targetTextBox.SelectionColor = color;
        targetTextBox.AppendText(prefix + message + Environment.NewLine);

        // Auto-scroll: if checkbox is checked, scroll to bottom
        if (autoScrollCheckBox.Checked)
        {
            targetTextBox.SelectionStart = targetTextBox.TextLength;
            targetTextBox.ScrollToCaret();
        }
    }

    private int _logMessageCount = 0;

    private void TrimLogIfNeeded(RichTextBox textBox)
    {
        // Use TextLength as a proxy for size - much faster than counting lines
        // Approximate: if text is over 500KB, trim it down
        const int MAX_TEXT_LENGTH = 500000;  // ~500KB
        const int TRIM_TO_LENGTH = 300000;   // ~300KB
        
        if (textBox.TextLength > MAX_TEXT_LENGTH)
        {
            try
            {
                textBox.SuspendLayout();
                
                // Calculate how much to remove
                int removeLength = textBox.TextLength - TRIM_TO_LENGTH;
                
                // Find a newline near the remove point to avoid cutting mid-line
                int actualRemovePoint = textBox.Text.IndexOf('\n', removeLength);
                if (actualRemovePoint == -1)
                    actualRemovePoint = removeLength;
                else
                    actualRemovePoint++;
                
                textBox.Select(0, actualRemovePoint);
                
                // Temporarily disable ReadOnly to prevent system beep
                textBox.ReadOnly = false;
                textBox.SelectedText = "";
                textBox.ReadOnly = true;
            }
            finally
            {
                textBox.ResumeLayout();
            }
        }
    }

    private void UpdateStatus(string message, Color color)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateStatus(message, color));
            return;
        }
        _statusLabel.Text = $"Status: {message}";
        _statusLabel.ForeColor = color;
    }

    private void LoadCharacter_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Character Profile (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = "json",
            InitialDirectory = _buffManager.CharacterProfilesPath,
            Title = "Load Character Profile"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var (success, message) = _buffManager.LoadCharacterProfile(dialog.FileName);
            if (success)
            {
                MessageBox.Show(message, "Character Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateTitle();
            }
            else
            {
                MessageBox.Show(message, "Load Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    
    private void ToggleAutoLoad_Click(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
        {
            _buffManager.AutoLoadLastCharacter = item.Checked;
            LogMessage($"Auto-load last character: {(item.Checked ? "ENABLED" : "DISABLED")}", MessageType.System);
        }
    }

    private void SaveCharacter_Click(object? sender, EventArgs e)
    {
        // If we have a current profile path, save to it; otherwise prompt for location
        if (!string.IsNullOrEmpty(_buffManager.CurrentProfilePath))
        {
            var (success, message) = _buffManager.SaveCharacterProfile(_buffManager.CurrentProfilePath);
            if (!success)
            {
                MessageBox.Show(message, "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            UpdateTitle();
        }
        else
        {
            // No current profile, use Save As
            SaveCharacterAs_Click(sender, e);
        }
    }

    private void SaveCharacterAs_Click(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Character Profile (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = "json",
            InitialDirectory = _buffManager.CharacterProfilesPath,
            FileName = _buffManager.GetDefaultProfileFilename(),
            Title = "Save Character Profile"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var (success, message) = _buffManager.SaveCharacterProfile(dialog.FileName);
            if (success)
            {
                MessageBox.Show(message, "Character Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateTitle();
            }
            else
            {
                MessageBox.Show(message, "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void UpdateTitle()
    {
        var title = "MUD Proxy Viewer";
        if (!string.IsNullOrEmpty(_buffManager.PlayerInfo.Name))
        {
            title += $" - {_buffManager.PlayerInfo.Name}";
        }
        if (!string.IsNullOrEmpty(_buffManager.CurrentProfilePath))
        {
            title += $" [{Path.GetFileName(_buffManager.CurrentProfilePath)}]";
        }
        this.Text = title;
    }

    private void SaveLog_Click(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            DefaultExt = "txt",
            FileName = $"mudlog_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            // Save both MUD output and system log
            var combinedLog = "=== MUD OUTPUT ===" + Environment.NewLine +
                              _mudOutputTextBox.Text + Environment.NewLine +
                              Environment.NewLine +
                              "=== SYSTEM LOG ===" + Environment.NewLine +
                              _systemLogTextBox.Text;
            File.WriteAllText(dialog.FileName, combinedLog);
            LogMessage($"Log saved to: {dialog.FileName}", MessageType.System);
        }
    }

    private void ClearLog_Click(object? sender, EventArgs e)
    {
        _mudOutputTextBox.Clear();
        _systemLogTextBox.Clear();
    }

    private void About_Click(object? sender, EventArgs e)
    {
        MessageBox.Show(
            "MUD Proxy Viewer v0.8 - Combat Assistant\n\n" +
            "A proxy tool for MajorMUD with:\n" +
            "• Combat tick detection and countdown\n" +
            "• HP/Mana monitoring\n" +
            "• Buff tracking with auto-recast\n" +
            "• Healing system with priority rules\n" +
            "• Ailment curing (poison, paralysis, etc.)\n" +
            "• Party buff management\n\n" +
            "Tips:\n" +
            "• Type 'stat' in game to detect your character\n" +
            "• Type 'par' to update party list\n" +
            "• Use Buffs menu to configure your buffs\n" +
            "• Use Healing/Cures menus for auto-healing\n\n",
            "About MUD Proxy Viewer",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information
        );
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _tickTimer?.Stop();
        _tickTimer?.Dispose();
        _buffUpdateTimer?.Stop();
        _buffUpdateTimer?.Dispose();
        _outOfCombatRecastTimer?.Stop();
        _outOfCombatRecastTimer?.Dispose();
        _parCheckTimer?.Stop();
        _parCheckTimer?.Dispose();
        Disconnect();
    }
}

public enum MessageType
{
    Server,
    Client,
    System
}

// Custom renderer for dark theme menus
public class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColorTable()) { }
    
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected)
        {
            using var brush = new SolidBrush(Color.FromArgb(70, 70, 70));
            e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
        }
        else
        {
            using var brush = new SolidBrush(Color.FromArgb(45, 45, 45));
            e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
        }
    }
    
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // Draw dark background for check area
        var rect = new Rectangle(e.ImageRectangle.X - 2, e.ImageRectangle.Y - 2, 
                                  e.ImageRectangle.Width + 4, e.ImageRectangle.Height + 4);
        using var bgBrush = new SolidBrush(Color.FromArgb(60, 60, 60));
        e.Graphics.FillRectangle(bgBrush, rect);
        
        // Draw border
        using var borderPen = new Pen(Color.FromArgb(100, 100, 100));
        e.Graphics.DrawRectangle(borderPen, rect);
        
        // Draw checkmark in white/light gray
        using var checkPen = new Pen(Color.FromArgb(200, 200, 200), 2);
        var checkRect = e.ImageRectangle;
        int x = checkRect.X + 3;
        int y = checkRect.Y + checkRect.Height / 2;
        e.Graphics.DrawLine(checkPen, x, y, x + 3, y + 3);
        e.Graphics.DrawLine(checkPen, x + 3, y + 3, x + 9, y - 3);
    }
    
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Color.FromArgb(45, 45, 45));
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }
    
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Color.FromArgb(70, 70, 70));
        int y = e.Item.ContentRectangle.Height / 2;
        e.Graphics.DrawLine(pen, 0, y, e.Item.Width, y);
    }
}

public class DarkColorTable : ProfessionalColorTable
{
    public override Color MenuBorder => Color.FromArgb(70, 70, 70);
    public override Color MenuItemBorder => Color.FromArgb(70, 70, 70);
    public override Color MenuItemSelected => Color.FromArgb(70, 70, 70);
    public override Color MenuStripGradientBegin => Color.FromArgb(45, 45, 45);
    public override Color MenuStripGradientEnd => Color.FromArgb(45, 45, 45);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(70, 70, 70);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(70, 70, 70);
    public override Color MenuItemPressedGradientBegin => Color.FromArgb(60, 60, 60);
    public override Color MenuItemPressedGradientEnd => Color.FromArgb(60, 60, 60);
    public override Color ToolStripDropDownBackground => Color.FromArgb(45, 45, 45);
    public override Color ImageMarginGradientBegin => Color.FromArgb(45, 45, 45);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(45, 45, 45);
    public override Color ImageMarginGradientEnd => Color.FromArgb(45, 45, 45);
}
