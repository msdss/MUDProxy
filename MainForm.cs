using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
    
    // Reconnection state
    private int _connectionAttemptCount = 0;
    private bool _userRequestedDisconnect = false;  // True if user clicked Disconnect
    
    // Terminal input/output state - user types directly after HP prompt like MegaMUD
    private readonly StringBuilder _userInputBuffer = new();      // What user is currently typing
    private readonly StringBuilder _serverOutputBuffer = new();   // Buffered server data while user types
    private bool IsUserTyping => _userInputBuffer.Length > 0;     // True if user has typed something
    
    // Virtual terminal buffer - proper VT100/ANSI emulation
    private ScreenBuffer _screenBuffer = null!;
    private AnsiVtParser _ansiParser = null!;
    private bool _screenDirty = false;
    private System.Windows.Forms.Timer _renderTimer = null!;

    // Buff Manager
    private readonly BuffManager _buffManager = new();

    // UI Components - Main
    private TerminalControl _terminalControl = null!;  // MUD server output - custom VT100 terminal
    private RichTextBox _systemLogTextBox = null!;  // System/proxy logs
    private SplitContainer _terminalSplitContainer = null!;  // Vertical split: MUD output / logs
    private Label _statusLabel = null!;
    private Label _expStatusLabel = null!;  // Experience status on right side
    private Button _connectButton = null!;
    private CheckBox _autoScrollCheckBox = null!;
    private CheckBox _autoScrollLogsCheckBox = null!;
    private CheckBox _showTimestampsCheckBox = null!;
    private Label _serverAddressLabel = null!;
    private Label _serverPortLabel = null!;
    // Command input is handled directly in _terminalControl (like MegaMUD)
    
    // UI Settings (persisted separately from character settings)
    private bool _displaySystemLog = true;  // Show/hide system log panel (synced with BuffManager)

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
        
        // Load UI settings before creating components
        LoadUiSettings();
        
        // Initialize virtual terminal BEFORE creating UI components
        // (TerminalControl needs the screen buffer in InitializeComponent)
        _screenBuffer = new ScreenBuffer(80, 24);
        _ansiParser = new AnsiVtParser(_screenBuffer);
        
        InitializeComponent();
        InitializeTimers();
        InitializeBuffManagerEvents();
        
        // Apply UI settings after components are created
        ApplySystemLogVisibility();
        
        // Start pre-loading game data cache in background
        GameDataCache.Instance.StartPreload();
        
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
                    ApplyWindowSettings();
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
        // Note: _screenBuffer and _ansiParser are now created in constructor
        // before InitializeComponent (TerminalControl needs the buffer)
        
        // Render timer - updates terminal from screen buffer
        _renderTimer = new System.Windows.Forms.Timer();
        _renderTimer.Interval = 100; // 10 FPS - balance between responsiveness and flicker
        _renderTimer.Tick += RenderTimer_Tick;
        _renderTimer.Start();
        
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
    
    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        if (_screenDirty && _isConnected)
        {
            _screenDirty = false;
            RenderScreenBuffer();
        }
    }

    private void InitializeBuffManagerEvents()
    {
        _buffManager.OnBuffsChanged += () => BeginInvoke(RefreshBuffDisplay);
        _buffManager.OnPartyChanged += () => BeginInvoke(RefreshPartyDisplay);
        _buffManager.OnPlayerInfoChanged += () => BeginInvoke(RefreshPlayerInfo);
        _buffManager.OnBbsSettingsChanged += () => BeginInvoke(RefreshBbsSettingsDisplay);
        _buffManager.OnLogMessage += (msg) => LogMessage(msg, MessageType.System);
        _buffManager.OnSendCommand += SendCommandToServer;
        _buffManager.OnHangupRequested += () => BeginInvoke(HandleRemoteHangup);
        _buffManager.OnRelogRequested += () => BeginInvoke(HandleRemoteRelog);
        _buffManager.OnAutomationStateChanged += () => BeginInvoke(RefreshAutomationButtons);
        _buffManager.OnTrainingScreenChanged += (inTraining) => BeginInvoke(() => 
        {
            _terminalControl.PassThroughMode = inTraining;
            if (inTraining)
            {
                _terminalControl.ClearInput();  // Clear any buffered input
            }
        });
        
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
        
        // Update exp status bar periodically (rate changes over time)
        if (_isConnected && !_isInLoginPhase)
        {
            UpdateExpStatusBar();
        }
    }
    
    private void ParCheckTimer_Tick(object? sender, EventArgs e)
    {
        // Only check when connected AND in-game (not during login)
        if (!_isConnected || _isInLoginPhase) return;
        if (_serverConnection == null || !_serverConnection.Connected) return;
        
        _buffManager.CheckParCommand();
        _buffManager.CheckHealthRequests();
    }
    
    /// <summary>
    /// Handle command entered in the terminal control
    /// </summary>
    private void TerminalControl_CommandEntered(string command)
    {
        // Send command to server
        SendCommandToServer(command);
        
        // Flush any buffered server output
        FlushServerBuffer();
    }
    
    /// <summary>
    /// Handle raw key data from terminal (pass-through mode for training screen)
    /// </summary>
    private void TerminalControl_RawKeyData(byte[] data)
    {
        if (!_isConnected || _serverStream == null)
            return;
        
        try
        {
            _serverStream.Write(data, 0, data.Length);
            _serverStream.Flush();
        }
        catch (Exception ex)
        {
            LogMessage($"Error sending key data: {ex.Message}", MessageType.System);
        }
    }
    
    /// <summary>
    /// Handle terminal size change
    /// </summary>
    private void TerminalControl_SizeChanged(int cols, int rows)
    {
        // Send NAWS (window size) to server if connected
        if (_isConnected && _serverStream != null)
        {
            _ = SendNawsAsync(cols, rows);
        }
    }
    
    /// <summary>
    /// Send NAWS (window size) to server asynchronously
    /// </summary>
    private async Task SendNawsAsync(int cols, int rows)
    {
        const byte IAC = 255;
        const byte SB = 250;
        const byte SE = 240;
        const byte NAWS = 31;
        
        byte[] naws = new byte[9];
        naws[0] = IAC;
        naws[1] = SB;
        naws[2] = NAWS;
        naws[3] = (byte)((cols >> 8) & 0xFF);
        naws[4] = (byte)(cols & 0xFF);
        naws[5] = (byte)((rows >> 8) & 0xFF);
        naws[6] = (byte)(rows & 0xFF);
        naws[7] = IAC;
        naws[8] = SE;
        
        try
        {
            await SendRawDataAsync(naws);
            LogMessage($"📡 Sent window size: {cols}x{rows}", MessageType.System);
        }
        catch
        {
            // Ignore send errors
        }
    }
    
    /// <summary>
    /// Flush buffered server output to display
    /// </summary>
    private void FlushServerBuffer()
    {
        if (_serverOutputBuffer.Length > 0)
        {
            string buffered = _serverOutputBuffer.ToString();
            _serverOutputBuffer.Clear();
            DisplayMudTextDirect(buffered);
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
        
        var displaySystemLogMenuItem = new ToolStripMenuItem("Display System Log", null, ToggleDisplaySystemLog_Click) 
        { 
            ForeColor = Color.White, 
            BackColor = Color.FromArgb(45, 45, 45),
            CheckOnClick = true,
            Checked = _displaySystemLog
        };
        fileMenu.DropDownItems.Add(displaySystemLogMenuItem);
        
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Import Game Database...", null, ImportGameDatabase_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        
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
        gameDataMenu.DropDownItems.Add(new ToolStripMenuItem("Races...", null, OpenGameDataRaces_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        gameDataMenu.DropDownItems.Add(new ToolStripMenuItem("Classes...", null, OpenGameDataClasses_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        gameDataMenu.DropDownItems.Add(new ToolStripMenuItem("Items...", null, OpenGameDataItems_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        gameDataMenu.DropDownItems.Add(new ToolStripMenuItem("Spells...", null, OpenGameDataSpells_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        gameDataMenu.DropDownItems.Add(new ToolStripMenuItem("Monsters...", null, OpenGameDataMonsters_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        gameDataMenu.DropDownItems.Add(new ToolStripMenuItem("Rooms...", null, OpenGameDataRooms_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        gameDataMenu.DropDownItems.Add(new ToolStripMenuItem("Shops...", null, OpenGameDataShops_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        gameDataMenu.DropDownItems.Add(new ToolStripMenuItem("Lairs...", null, OpenGameDataLairs_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        gameDataMenu.DropDownItems.Add(new ToolStripMenuItem("Text Blocks...", null, OpenGameDataTextBlocks_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        gameDataMenu.DropDownItems.Add(new ToolStripSeparator());
        gameDataMenu.DropDownItems.Add(new ToolStripMenuItem("Player DB...", null, OpenPlayerDB_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        gameDataMenu.DropDownItems.Add(new ToolStripMenuItem("Monster DB (Deprecated)...", null, OpenMonsterDB_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });

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
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(10, 5)
        };
        statusPanel.Controls.Add(_statusLabel);
        
        _expStatusLabel = new Label
        {
            Text = "",
            ForeColor = Color.White,
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleRight
        };
        statusPanel.Controls.Add(_expStatusLabel);
        
        // Position exp label on resize
        statusPanel.Resize += (s, e) =>
        {
            _expStatusLabel.Location = new Point(statusPanel.Width - _expStatusLabel.Width - 10, 5);
        };

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
        
        // MUD output terminal (top panel) - custom VT100 terminal control
        _terminalControl = new TerminalControl
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,  // Pure black like MegaMUD
            Font = new Font("Consolas", 10)
        };
        
        // Connect terminal to screen buffer
        _terminalControl.SetScreenBuffer(_screenBuffer);
        
        // Handle commands entered in terminal
        _terminalControl.OnCommandEntered += TerminalControl_CommandEntered;
        _terminalControl.OnTerminalSizeChanged += TerminalControl_SizeChanged;
        _terminalControl.OnRawKeyData += TerminalControl_RawKeyData;
        
        // Add to panel
        _terminalSplitContainer.Panel1.Controls.Add(_terminalControl);
        
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
            BackColor = Color.Black,  // Pure black like MegaMUD
            ForeColor = Color.FromArgb(255, 204, 0),
            Font = new Font("Consolas", 9),
            ReadOnly = true,
            BorderStyle = BorderStyle.None
        };
        _terminalSplitContainer.Panel2.Controls.Add(_systemLogTextBox);
        _terminalSplitContainer.Panel2.Controls.Add(logHeaderPanel);

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
        
        // Update exp status bar
        UpdateExpStatusBar();
    }
    
    private void UpdateExpStatusBar()
    {
        var info = _buffManager.PlayerInfo;
        var tracker = _buffManager.ExperienceTracker;
        
        if (info.Level <= 0)
        {
            _expStatusLabel.Text = "";
            return;
        }
        
        var sessionExp = tracker.SessionExpGained;
        var expNeeded = info.ExperienceNeededForNextLevel;
        var expPerHour = tracker.GetExpPerHour();
        var alreadyLeveled = expNeeded <= 0;
        var timeToLevel = ExperienceTracker.FormatTimeSpan(
            tracker.EstimateTimeToExp(expNeeded), 
            alreadyLeveled);
        
        _expStatusLabel.Text = $"Level: {info.Level} / " +
            $"Made: {ExperienceTracker.FormatNumberAbbreviated(sessionExp)} / " +
            $"Needed: {ExperienceTracker.FormatNumberAbbreviated(expNeeded)} / " +
            $"Rate: {ExperienceTracker.FormatNumberAbbreviated(expPerHour)}/hr / " +
            $"Will level in: {timeToLevel}";
        
        // Reposition after text change
        _expStatusLabel.Location = new Point(
            _expStatusLabel.Parent!.Width - _expStatusLabel.Width - 10, 5);
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
    
    /// <summary>
    /// Refresh automation button states (called when remote commands change automation)
    /// </summary>
    private void RefreshAutomationButtons()
    {
        UpdateToggleButtonStates();
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
    
    private void ImportGameDatabase_Click(object? sender, EventArgs e)
    {
        // Check if ACE is installed first
        if (!MdbImporter.IsAceInstalled())
        {
            using var aceDialog = new AceNotInstalledDialog();
            aceDialog.ShowDialog(this);
            return;
        }
        
        // Open file picker
        using var openDialog = new OpenFileDialog
        {
            Title = "Select your game database",
            Filter = "Access Database (*.mdb)|*.mdb",
            CheckFileExists = true
        };
        
        if (openDialog.ShowDialog(this) != DialogResult.OK)
            return;
        
        // Show import dialog with progress
        using var importDialog = new MdbImportDialog(openDialog.FileName);
        var result = importDialog.ShowDialog(this);
        
        if (result == DialogResult.OK)
        {
            LogMessage("Game database imported successfully!", MessageType.System);
            
            // Clear and reload the cache with new data
            GameDataCache.Instance.ClearCache();
            GameDataCache.Instance.StartPreload();
        }
    }
    
    private void OpenGameDataRaces_Click(object? sender, EventArgs e)
    {
        OpenGameDataViewer("Races");
    }
    
    private void OpenGameDataClasses_Click(object? sender, EventArgs e)
    {
        OpenGameDataViewer("Classes");
    }
    
    private void OpenGameDataItems_Click(object? sender, EventArgs e)
    {
        OpenGameDataViewer("Items");
    }
    
    private void OpenGameDataSpells_Click(object? sender, EventArgs e)
    {
        OpenGameDataViewer("Spells");
    }
    
    private void OpenGameDataMonsters_Click(object? sender, EventArgs e)
    {
        using var dialog = new MonsterDatabaseDialog(_buffManager.MonsterDatabase);
        dialog.ShowDialog(this);
    }
    
    private void OpenGameDataRooms_Click(object? sender, EventArgs e)
    {
        OpenGameDataViewer("Rooms");
    }
    
    private void OpenGameDataShops_Click(object? sender, EventArgs e)
    {
        OpenGameDataViewer("Shops");
    }
    
    private void OpenGameDataLairs_Click(object? sender, EventArgs e)
    {
        OpenGameDataViewer("Lairs");
    }
    
    private void OpenGameDataTextBlocks_Click(object? sender, EventArgs e)
    {
        OpenGameDataViewer("TextBlocks");
    }
    
    private void OpenGameDataViewer(string tableName)
    {
        var importer = new MdbImporter();
        var filePath = Path.Combine(importer.GameDataPath, $"{tableName}.json");
        
        if (!File.Exists(filePath))
        {
            MessageBox.Show(
                $"Game data for '{tableName}' has not been imported yet.\n\n" +
                "Use File → Import Game Database... to import data from your MajorMUD .mdb file.",
                "Game Data Not Found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        
        using var dialog = new GameDataViewerDialog(tableName, filePath);
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
                _buffManager.IsInLoginPhase = false;
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
        {
            _userRequestedDisconnect = false;
            _connectionAttemptCount = 0;
            await ConnectWithRetry(isInitialConnection: true);
        }
        else
        {
            _userRequestedDisconnect = true;
            Disconnect();
        }
    }

    private async Task ConnectWithRetry(bool isInitialConnection)
    {
        // Check if we have BBS settings
        if (string.IsNullOrEmpty(_serverAddress))
        {
            if (isInitialConnection)
            {
                LogMessage("Please load a character profile with BBS settings first.", MessageType.System);
            }
            return;
        }

        var bbsSettings = _buffManager.BbsSettings;
        var maxAttempts = bbsSettings.MaxConnectionAttempts;  // 0 = unlimited
        var retryPauseSeconds = bbsSettings.ConnectionRetryPauseSeconds;
        var shouldRetry = isInitialConnection 
            ? bbsSettings.ReconnectOnConnectionFail 
            : bbsSettings.ReconnectOnConnectionLost;

        while (!_userRequestedDisconnect)
        {
            _connectionAttemptCount++;
            
            // Check if we've exceeded max attempts (0 = unlimited)
            if (maxAttempts > 0 && _connectionAttemptCount > maxAttempts)
            {
                LogMessage($"Max connection attempts ({maxAttempts}) reached. Giving up.", MessageType.System);
                UpdateStatus("Connection failed", Color.Red);
                break;
            }

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                _serverConnection = new TcpClient();
                
                var attemptText = maxAttempts > 0 
                    ? $" (attempt {_connectionAttemptCount}/{maxAttempts})"
                    : _connectionAttemptCount > 1 ? $" (attempt {_connectionAttemptCount})" : "";
                    
                UpdateStatus("Connecting...", Color.Yellow);
                LogMessage($"Connecting to {_serverAddress}:{_serverPort}...{attemptText}", MessageType.System);
                
                await _serverConnection.ConnectAsync(_serverAddress, _serverPort, _cancellationTokenSource.Token);
                _serverStream = _serverConnection.GetStream();
                
                _isConnected = true;
                _isInLoginPhase = true;
                _connectionAttemptCount = 0;  // Reset on successful connection
                _triggeredLogonSequences.Clear();
                
                UpdateConnectionUI(true);
                LogMessage($"Connected to {_serverAddress}:{_serverPort}", MessageType.System);
                UpdateStatus("Connected", Color.White);
                
                // Reset combat state on new connection
                _buffManager.CombatManager.ResetState();

                // Start reading from server - this will return when connection is lost
                await ReadServerDataAsync(_cancellationTokenSource.Token);
                
                // If we get here, connection was lost (not user-initiated disconnect)
                if (!_userRequestedDisconnect && bbsSettings.ReconnectOnConnectionLost)
                {
                    LogMessage("Connection lost. Will attempt to reconnect...", MessageType.System);
                    CleanupConnection();
                    _connectionAttemptCount = 0;  // Reset for reconnection attempts
                    shouldRetry = true;
                    // Continue the while loop to reconnect
                }
                else
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                // User cancelled - don't retry
                break;
            }
            catch (Exception ex)
            {
                LogMessage($"Connection failed: {ex.Message}", MessageType.System);
                CleanupConnection();
                
                if (!shouldRetry || _userRequestedDisconnect)
                {
                    UpdateStatus("Connection failed", Color.Red);
                    break;
                }
                
                // Wait before retry
                UpdateStatus($"Retrying in {retryPauseSeconds}s...", Color.Orange);
                LogMessage($"Waiting {retryPauseSeconds} seconds before retry...", MessageType.System);
                
                try
                {
                    await Task.Delay(retryPauseSeconds * 1000, _cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        
        // Final cleanup
        if (!_isConnected)
        {
            Disconnect();
        }
    }

    /// <summary>
    /// Clean up connection resources without triggering full disconnect logic
    /// </summary>
    private void CleanupConnection()
    {
        _isConnected = false;
        
        try { _serverStream?.Close(); } catch { }
        try { _serverConnection?.Close(); } catch { }
        
        _serverStream = null;
        _serverConnection = null;
    }

    private void Disconnect()
    {
        _isConnected = false;
        _cancellationTokenSource?.Cancel();

        try { _serverStream?.Close(); } catch { }
        try { _serverConnection?.Close(); } catch { }

        _serverStream = null;
        _serverConnection = null;
        
        // Reset terminal state
        _userInputBuffer.Clear();
        _serverOutputBuffer.Clear();
        _terminalControl.ClearInput();
        
        // Reset session state in BuffManager
        _buffManager.OnDisconnected();

        UpdateConnectionUI(false);
        LogMessage("Disconnected.", MessageType.System);
        UpdateStatus("Disconnected", Color.Gray);
    }
    
    /// <summary>
    /// Handle remote hangup request
    /// </summary>
    private void HandleRemoteHangup()
    {
        LogMessage("📡 Remote hangup command received - disconnecting...", MessageType.System);
        Disconnect();
    }
    
    /// <summary>
    /// Handle remote relog request
    /// </summary>
    private void HandleRemoteRelog()
    {
        LogMessage("📡 Remote relog command received - reconnecting...", MessageType.System);
        Disconnect();
        
        // Small delay before reconnecting
        Task.Delay(1000).ContinueWith(_ =>
        {
            if (!IsDisposed)
            {
                BeginInvoke(() =>
                {
                    if (!_isConnected)
                    {
                        ConnectButton_Click(this, EventArgs.Empty);
                    }
                });
            }
        });
    }

    private void UpdateConnectionUI(bool connected)
    {
        _connectButton.Text = connected ? "Disconnect" : "Connect";
        _connectButton.BackColor = connected ? Color.FromArgb(180, 0, 0) : Color.FromArgb(0, 120, 0);
    }

    private async Task ReadServerDataAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        
        // Reset terminal state for new connection
        _userInputBuffer.Clear();
        _serverOutputBuffer.Clear();
        _screenBuffer.ClearAll();
        _screenBuffer.MoveCursorAbs(0, 0);
        _screenBuffer.Fg = ConsoleColor.Gray;
        _screenBuffer.Bg = ConsoleColor.Black;
        _terminalControl.ClearInput();

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

                    // Display the text with proper line handling
                    DisplayMudText(text);
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
    /// Display MUD text - buffers if user is typing, displays directly otherwise.
    /// This is the key to the MegaMUD-style behavior.
    /// </summary>
    private void DisplayMudText(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => DisplayMudText(text));
            return;
        }
        
        if (IsUserTyping)
        {
            // User is typing - buffer the server output
            _serverOutputBuffer.Append(text);
        }
        else
        {
            // User not typing - display immediately
            DisplayMudTextDirect(text);
        }
    }
    
    /// <summary>
    /// Display text by feeding it through the ANSI parser to the screen buffer.
    /// This provides proper VT100 terminal emulation.
    /// </summary>
    private void DisplayMudTextDirect(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => DisplayMudTextDirect(text));
            return;
        }
        
        // Convert string to bytes for the parser (using CP437 for BBS compatibility)
        byte[] bytes;
        try
        {
            bytes = Encoding.GetEncoding(437).GetBytes(text);
        }
        catch
        {
            bytes = Encoding.Latin1.GetBytes(text);
        }
        
        // Feed to the ANSI parser
        _ansiParser.Feed(bytes);
        
        // Mark screen as dirty - will be rendered on next timer tick
        _screenDirty = true;
        
        // Give focus to the terminal for typing
        if (_isConnected)
        {
            _terminalControl.Focus();
        }
    }
    
    /// <summary>
    /// Render the virtual screen buffer to the terminal control.
    /// The TerminalControl paints directly from the ScreenBuffer.
    /// </summary>
    private void RenderScreenBuffer()
    {
        if (InvokeRequired)
        {
            BeginInvoke(RenderScreenBuffer);
            return;
        }
        
        // Tell the terminal control to repaint
        _terminalControl.InvalidateTerminal();
    }
    
    /// <summary>
    /// Convert ConsoleColor to System.Drawing.Color
    /// </summary>
    private static Color ConsoleColorToColor(ConsoleColor cc)
    {
        return cc switch
        {
            ConsoleColor.Black => Color.FromArgb(0, 0, 0),
            ConsoleColor.DarkBlue => Color.FromArgb(0, 0, 128),
            ConsoleColor.DarkGreen => Color.FromArgb(0, 128, 0),
            ConsoleColor.DarkCyan => Color.FromArgb(0, 128, 128),
            ConsoleColor.DarkRed => Color.FromArgb(128, 0, 0),
            ConsoleColor.DarkMagenta => Color.FromArgb(128, 0, 128),
            ConsoleColor.DarkYellow => Color.FromArgb(128, 128, 0),
            ConsoleColor.Gray => Color.FromArgb(192, 192, 192),
            ConsoleColor.DarkGray => Color.FromArgb(128, 128, 128),
            ConsoleColor.Blue => Color.FromArgb(0, 0, 255),
            ConsoleColor.Green => Color.FromArgb(0, 255, 0),
            ConsoleColor.Cyan => Color.FromArgb(0, 255, 255),
            ConsoleColor.Red => Color.FromArgb(255, 0, 0),
            ConsoleColor.Magenta => Color.FromArgb(255, 0, 255),
            ConsoleColor.Yellow => Color.FromArgb(255, 255, 0),
            ConsoleColor.White => Color.FromArgb(255, 255, 255),
            _ => Color.FromArgb(192, 192, 192)
        };
    }

    /// <summary>
    /// Process telnet IAC (Interpret As Command) sequences
    /// Returns clean data with IAC commands stripped, plus any responses to send
    /// Properly negotiates terminal capabilities (ANSI, NAWS, etc.)
    /// </summary>
    private (byte[] cleanData, List<byte[]> responses) ProcessTelnetData(byte[] buffer, int length)
    {
        const byte IAC = 255;   // Interpret As Command
        const byte WILL = 251;
        const byte WONT = 252;
        const byte DO = 253;
        const byte DONT = 254;
        const byte SB = 250;    // Subnegotiation Begin
        const byte SE = 240;    // Subnegotiation End
        
        // Telnet options
        const byte ECHO = 1;    // Echo
        const byte SGA = 3;     // Suppress Go Ahead
        const byte TTYPE = 24;  // Terminal Type
        const byte NAWS = 31;   // Negotiate About Window Size
        
        const byte TELQUAL_IS = 0;
        const byte TELQUAL_SEND = 1;

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
                else if (command == SB)
                {
                    // Subnegotiation - find IAC SE
                    int j = i + 2;
                    while (j < length - 1)
                    {
                        if (buffer[j] == IAC && buffer[j + 1] == SE) break;
                        j++;
                    }
                    if (j >= length - 1)
                    {
                        i = length; // incomplete, wait for more data
                        break;
                    }
                    
                    if (i + 2 < length)
                    {
                        byte opt = buffer[i + 2];
                        
                        // Handle terminal type request
                        if (opt == TTYPE && j > i + 3 && buffer[i + 3] == TELQUAL_SEND)
                        {
                            // Server asks for terminal type - send "ANSI"
                            byte[] termType = Encoding.ASCII.GetBytes("ANSI");
                            byte[] response = new byte[6 + termType.Length];
                            response[0] = IAC;
                            response[1] = SB;
                            response[2] = TTYPE;
                            response[3] = TELQUAL_IS;
                            Buffer.BlockCopy(termType, 0, response, 4, termType.Length);
                            response[4 + termType.Length] = IAC;
                            response[5 + termType.Length] = SE;
                            responses.Add(response);
                            LogMessage("📡 Sent terminal type: ANSI", MessageType.System);
                        }
                    }
                    
                    i = j + 2;
                }
                else if ((command == WILL || command == WONT || command == DO || command == DONT) && i + 2 < length)
                {
                    byte option = buffer[i + 2];
                    
                    if (command == DO)
                    {
                        // Server asks us to enable an option
                        if (option == NAWS || option == TTYPE || option == SGA)
                        {
                            // Accept these options
                            responses.Add(new byte[] { IAC, WILL, option });
                            
                            // If NAWS, send our window size
                            if (option == NAWS)
                            {
                                SendWindowSize(responses);
                            }
                        }
                        else
                        {
                            // Refuse unknown options
                            responses.Add(new byte[] { IAC, WONT, option });
                        }
                    }
                    else if (command == DONT)
                    {
                        // Server tells us to disable - acknowledge
                        responses.Add(new byte[] { IAC, WONT, option });
                    }
                    else if (command == WILL)
                    {
                        // Server offers to enable an option
                        if (option == ECHO || option == SGA)
                        {
                            // Accept echo and suppress-go-ahead
                            responses.Add(new byte[] { IAC, DO, option });
                        }
                        else
                        {
                            // Refuse unknown options
                            responses.Add(new byte[] { IAC, DONT, option });
                        }
                    }
                    else if (command == WONT)
                    {
                        // Server won't do something - acknowledge
                        responses.Add(new byte[] { IAC, DONT, option });
                    }
                    
                    i += 3;
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
    /// Build NAWS (window size) subnegotiation and add to responses
    /// </summary>
    private void SendWindowSize(List<byte[]> responses)
    {
        const byte IAC = 255;
        const byte SB = 250;
        const byte SE = 240;
        const byte NAWS = 31;
        
        // Use a reasonable terminal size (80x24 is standard)
        int cols = 80;
        int rows = 24;
        
        byte[] naws = new byte[9];
        naws[0] = IAC;
        naws[1] = SB;
        naws[2] = NAWS;
        naws[3] = (byte)((cols >> 8) & 0xFF);
        naws[4] = (byte)(cols & 0xFF);
        naws[5] = (byte)((rows >> 8) & 0xFF);
        naws[6] = (byte)(rows & 0xFF);
        naws[7] = IAC;
        naws[8] = SE;
        
        responses.Add(naws);
        LogMessage($"📡 Sent window size: {cols}x{rows}", MessageType.System);
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

        // All log messages go to the system log
        RichTextBox targetTextBox = _systemLogTextBox;
        CheckBox autoScrollCheckBox = _autoScrollLogsCheckBox;

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

        // All log messages go to the system log
        RichTextBox targetTextBox = _systemLogTextBox;
        CheckBox autoScrollCheckBox = _autoScrollLogsCheckBox;

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
                ApplyWindowSettings();
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
    
    private void ToggleDisplaySystemLog_Click(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
        {
            _displaySystemLog = item.Checked;
            ApplySystemLogVisibility();
            SaveUiSettings();
            LogMessage($"Display system log: {(_displaySystemLog ? "ENABLED" : "DISABLED")}", MessageType.System);
        }
    }
    
    private void ApplySystemLogVisibility()
    {
        _terminalSplitContainer.Panel2Collapsed = !_displaySystemLog;
    }
    
    private void LoadUiSettings()
    {
        // Load from BuffManager's settings (stored in settings.json)
        _displaySystemLog = _buffManager.DisplaySystemLog;
    }
    
    private void SaveUiSettings()
    {
        // Save via BuffManager (stored in settings.json)
        _buffManager.DisplaySystemLog = _displaySystemLog;
    }

/// <summary>
    /// Apply window position and size from the loaded character profile.
    /// Validates that the window is visible on at least one screen.
    /// </summary>
    private void ApplyWindowSettings()
    {
        var ws = _buffManager.WindowSettings;
        if (ws == null)
            return;
        
        // Validate that the saved position is at least partially visible on a connected screen
        var savedBounds = new Rectangle(ws.X, ws.Y, ws.Width, ws.Height);
        bool isOnScreen = false;
        foreach (var screen in Screen.AllScreens)
        {
            if (screen.WorkingArea.IntersectsWith(savedBounds))
            {
                isOnScreen = true;
                break;
            }
        }
        
        if (isOnScreen && ws.Width > 100 && ws.Height > 100)
        {
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(ws.X, ws.Y);
            this.Size = new Size(ws.Width, ws.Height);
            
            if (ws.IsMaximized)
            {
                this.WindowState = FormWindowState.Maximized;
            }
        }
    }
    
    /// Capture current window position/size and save to the character profile.
    /// Saves RestoreBounds when maximized so the normal position is preserved.
    private void CaptureWindowSettings()
    {
        // Only save if a character profile is loaded
        if (string.IsNullOrEmpty(_buffManager.CurrentProfilePath))
            return;
        
        var isMaximized = this.WindowState == FormWindowState.Maximized;
        var bounds = isMaximized ? this.RestoreBounds : this.Bounds;
        
        _buffManager.WindowSettings = new WindowSettings
        {
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height,
            IsMaximized = isMaximized
        };
        
        // Auto-save the profile so window settings persist
        if (!string.IsNullOrEmpty(_buffManager.CurrentProfilePath))
        {
            _buffManager.SaveCharacterProfile(_buffManager.CurrentProfilePath);
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
            // Save both terminal content and system log
            var terminalContent = _screenBuffer.GetContentAsText();
            var combinedLog = "=== MUD OUTPUT ===" + Environment.NewLine +
                              terminalContent + Environment.NewLine +
                              Environment.NewLine +
                              "=== SYSTEM LOG ===" + Environment.NewLine +
                              _systemLogTextBox.Text;
            File.WriteAllText(dialog.FileName, combinedLog);
            LogMessage($"Log saved to: {dialog.FileName}", MessageType.System);
        }
    }

    private void ClearLog_Click(object? sender, EventArgs e)
    {
        _screenBuffer.ClearAll();
        _terminalControl.InvalidateTerminal();
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
        // Save window position/size to character profile
        CaptureWindowSettings();
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

/* ===========================
 * Virtual Terminal Buffer
 * Proper VT100/ANSI emulation
 * =========================== */

/// <summary>
/// A single cell in the terminal buffer
/// </summary>
public readonly struct TerminalCell
{
    public readonly char Ch;
    public readonly ConsoleColor Fg;
    public readonly ConsoleColor Bg;

    public TerminalCell(char ch, ConsoleColor fg, ConsoleColor bg)
    {
        Ch = ch;
        Fg = fg;
        Bg = bg;
    }
}

/// <summary>
/// Virtual terminal screen buffer - a 2D grid of characters with colors.
/// Supports cursor positioning, scrolling, and erase operations.
/// </summary>
public sealed class ScreenBuffer
{
    public int Cols { get; private set; }
    public int Rows { get; private set; }

    // Cursor position
    public int CursorX { get; set; }
    public int CursorY { get; set; }

    // Scroll region (inclusive, 0-based)
    public int ScrollTop { get; private set; }
    public int ScrollBottom { get; private set; }

    // Current text attributes
    public ConsoleColor Fg { get; set; } = ConsoleColor.Gray;
    public ConsoleColor Bg { get; set; } = ConsoleColor.Black;
    public bool DecLineDrawing { get; set; } = false;

    private TerminalCell[,] _cells;

    public ScreenBuffer(int cols, int rows)
    {
        Cols = Math.Clamp(cols, 20, 400);
        Rows = Math.Clamp(rows, 10, 200);
        _cells = new TerminalCell[Rows, Cols];
        ScrollTop = 0;
        ScrollBottom = Rows - 1;
        ClearAll();
    }

    public void Resize(int cols, int rows)
    {
        cols = Math.Clamp(cols, 20, 400);
        rows = Math.Clamp(rows, 10, 200);

        var newCells = new TerminalCell[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                newCells[r, c] = new TerminalCell(' ', ConsoleColor.Gray, ConsoleColor.Black);

        int copyRows = Math.Min(rows, Rows);
        int copyCols = Math.Min(cols, Cols);
        for (int r = 0; r < copyRows; r++)
            for (int c = 0; c < copyCols; c++)
                newCells[r, c] = _cells[r, c];

        _cells = newCells;
        Cols = cols;
        Rows = rows;

        CursorX = Math.Clamp(CursorX, 0, Cols - 1);
        CursorY = Math.Clamp(CursorY, 0, Rows - 1);

        ScrollTop = 0;
        ScrollBottom = Rows - 1;
    }

    public TerminalCell GetCell(int row, int col)
    {
        if ((uint)row >= (uint)Rows || (uint)col >= (uint)Cols)
            return new TerminalCell(' ', ConsoleColor.Gray, ConsoleColor.Black);
        return _cells[row, col];
    }

    public void SetCell(int row, int col, char ch, ConsoleColor fg, ConsoleColor bg)
    {
        if ((uint)row >= (uint)Rows || (uint)col >= (uint)Cols) return;
        _cells[row, col] = new TerminalCell(ch, fg, bg);
    }

    public void ClearAll()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                _cells[r, c] = new TerminalCell(' ', ConsoleColor.Gray, ConsoleColor.Black);
    }

    public void ClearLine(int row, int fromCol, int toCol)
    {
        if ((uint)row >= (uint)Rows) return;
        fromCol = Math.Clamp(fromCol, 0, Cols - 1);
        toCol = Math.Clamp(toCol, 0, Cols - 1);
        if (toCol < fromCol) (fromCol, toCol) = (toCol, fromCol);

        for (int c = fromCol; c <= toCol; c++)
            _cells[row, c] = new TerminalCell(' ', Fg, Bg);
    }

    public void MoveCursorAbs(int row, int col)
    {
        CursorY = Math.Clamp(row, 0, Rows - 1);
        CursorX = Math.Clamp(col, 0, Cols - 1);
    }

    public void MoveCursorRel(int dy, int dx)
    {
        MoveCursorAbs(CursorY + dy, CursorX + dx);
    }

    public void SetScrollRegion(int top, int bottom)
    {
        top = Math.Clamp(top, 0, Rows - 1);
        bottom = Math.Clamp(bottom, 0, Rows - 1);
        if (bottom < top) (top, bottom) = (bottom, top);

        ScrollTop = top;
        ScrollBottom = bottom;
        MoveCursorAbs(0, 0);
    }

    public void PutChar(char ch)
    {
        if (ch == '\n')
        {
            NewLine();
            return;
        }
        if (ch == '\r')
        {
            CursorX = 0;
            return;
        }
        if (ch == '\b')
        {
            CursorX = Math.Max(0, CursorX - 1);
            return;
        }
        if (ch == '\t')
        {
            CursorX = Math.Min(Cols - 1, ((CursorX / 8) + 1) * 8);
            return;
        }
        if (ch == '\a') // Bell - ignore
            return;

        char drawCh = DecLineDrawing ? DecGraphicsMap(ch) : ch;
        SetCell(CursorY, CursorX, drawCh, Fg, Bg);
        CursorX++;
        if (CursorX >= Cols)
        {
            CursorX = 0;
            NewLine();
        }
    }

    private void NewLine()
    {
        int nextY = CursorY + 1;
        if (nextY > ScrollBottom)
        {
            ScrollUp(1);
            CursorY = ScrollBottom;
        }
        else
        {
            CursorY = nextY;
        }
    }

    public void ScrollUp(int lines)
    {
        lines = Math.Clamp(lines, 1, ScrollBottom - ScrollTop + 1);

        for (int _ = 0; _ < lines; _++)
        {
            for (int r = ScrollTop; r < ScrollBottom; r++)
                for (int c = 0; c < Cols; c++)
                    _cells[r, c] = _cells[r + 1, c];

            for (int c = 0; c < Cols; c++)
                _cells[ScrollBottom, c] = new TerminalCell(' ', ConsoleColor.Gray, ConsoleColor.Black);
        }
    }

    public void ScrollDown(int lines)
    {
        lines = Math.Clamp(lines, 1, ScrollBottom - ScrollTop + 1);

        for (int _ = 0; _ < lines; _++)
        {
            for (int r = ScrollBottom; r > ScrollTop; r--)
                for (int c = 0; c < Cols; c++)
                    _cells[r, c] = _cells[r - 1, c];

            for (int c = 0; c < Cols; c++)
                _cells[ScrollTop, c] = new TerminalCell(' ', ConsoleColor.Gray, ConsoleColor.Black);
        }
    }

    public void InsertLines(int n)
    {
        n = Math.Clamp(n, 1, ScrollBottom - CursorY + 1);
        if (CursorY < ScrollTop || CursorY > ScrollBottom) return;

        for (int _ = 0; _ < n; _++)
        {
            for (int r = ScrollBottom; r > CursorY; r--)
                for (int c = 0; c < Cols; c++)
                    _cells[r, c] = _cells[r - 1, c];
            for (int c = 0; c < Cols; c++)
                _cells[CursorY, c] = new TerminalCell(' ', ConsoleColor.Gray, ConsoleColor.Black);
        }
    }

    public void DeleteLines(int n)
    {
        n = Math.Clamp(n, 1, ScrollBottom - CursorY + 1);
        if (CursorY < ScrollTop || CursorY > ScrollBottom) return;

        for (int _ = 0; _ < n; _++)
        {
            for (int r = CursorY; r < ScrollBottom; r++)
                for (int c = 0; c < Cols; c++)
                    _cells[r, c] = _cells[r + 1, c];
            for (int c = 0; c < Cols; c++)
                _cells[ScrollBottom, c] = new TerminalCell(' ', ConsoleColor.Gray, ConsoleColor.Black);
        }
    }

    public void InsertChars(int n)
    {
        n = Math.Clamp(n, 1, Cols - CursorX);
        int row = CursorY;
        int start = CursorX;

        for (int c = Cols - 1; c >= start + n; c--)
            _cells[row, c] = _cells[row, c - n];
        for (int c = start; c < start + n; c++)
            _cells[row, c] = new TerminalCell(' ', Fg, Bg);
    }

    public void DeleteChars(int n)
    {
        n = Math.Clamp(n, 1, Cols - CursorX);
        int row = CursorY;
        int start = CursorX;

        for (int c = start; c < Cols - n; c++)
            _cells[row, c] = _cells[row, c + n];
        for (int c = Cols - n; c < Cols; c++)
            _cells[row, c] = new TerminalCell(' ', Fg, Bg);
    }

    // Erase in display (ED)
    public void EraseDisplay(int mode)
    {
        if (mode == 2)
        {
            ClearAll();
            return;
        }

        if (mode == 0)
        {
            // cursor to end
            ClearLine(CursorY, CursorX, Cols - 1);
            for (int r = CursorY + 1; r < Rows; r++)
                ClearLine(r, 0, Cols - 1);
            return;
        }

        if (mode == 1)
        {
            // start to cursor
            for (int r = 0; r < CursorY; r++)
                ClearLine(r, 0, Cols - 1);
            ClearLine(CursorY, 0, CursorX);
        }
    }

    // Erase in line (EL)
    public void EraseLine(int mode)
    {
        if (mode == 2) { ClearLine(CursorY, 0, Cols - 1); return; }
        if (mode == 0) { ClearLine(CursorY, CursorX, Cols - 1); return; }
        if (mode == 1) { ClearLine(CursorY, 0, CursorX); return; }
    }

    // DEC Special Graphics character mapping
    private static char DecGraphicsMap(char c) => c switch
    {
        'j' => '┘',
        'k' => '┐',
        'l' => '┌',
        'm' => '└',
        'n' => '┼',
        'q' => '─',
        't' => '├',
        'u' => '┤',
        'v' => '┴',
        'w' => '┬',
        'x' => '│',
        _ => c
    };
    
    /// <summary>
    /// Get the screen content as a string (for saving logs)
    /// </summary>
    public string GetContentAsText()
    {
        var sb = new StringBuilder();
        
        for (int row = 0; row < Rows; row++)
        {
            // Find last non-space character in row
            int lastNonSpace = -1;
            for (int col = Cols - 1; col >= 0; col--)
            {
                if (_cells[row, col].Ch != ' ' && _cells[row, col].Ch != '\0')
                {
                    lastNonSpace = col;
                    break;
                }
            }
            
            // Add characters up to last non-space
            for (int col = 0; col <= lastNonSpace; col++)
            {
                sb.Append(_cells[row, col].Ch);
            }
            
            sb.AppendLine();
        }
        
        return sb.ToString();
    }
}

/// <summary>
/// ANSI/VT100 escape sequence parser.
/// Interprets escape sequences and updates the screen buffer accordingly.
/// </summary>
public sealed class AnsiVtParser
{
    private const byte ESC = 0x1B;
    private readonly ScreenBuffer _s;

    private enum State { Text, Esc, Csi, Charset }
    private State _state = State.Text;

    private readonly List<int> _params = new();
    private int _paramAcc = -1;
    private char _charsetWhich = '\0';

    public AnsiVtParser(ScreenBuffer screen)
    {
        _s = screen;
    }

    public void Feed(byte[] bytes)
    {
        Feed(bytes.AsSpan());
    }

    public void Feed(ReadOnlySpan<byte> bytes)
    {
        // Decode as CP437 (common for BBS)
        string text;
        try
        {
            text = Encoding.GetEncoding(437).GetString(bytes);
        }
        catch
        {
            text = Encoding.Latin1.GetString(bytes);
        }

        foreach (char ch in text)
        {
            byte b = (byte)ch;

            switch (_state)
            {
                case State.Text:
                    if (b == ESC)
                        _state = State.Esc;
                    else
                        _s.PutChar(ch);
                    break;

                case State.Esc:
                    if (ch == '[')
                    {
                        EnterCsi();
                    }
                    else if (ch == '(' || ch == ')')
                    {
                        _charsetWhich = ch;
                        _state = State.Charset;
                    }
                    else if (ch == 'M')
                    {
                        // Reverse index - move cursor up, scroll if at top
                        if (_s.CursorY == _s.ScrollTop)
                            _s.ScrollDown(1);
                        else
                            _s.MoveCursorRel(-1, 0);
                        _state = State.Text;
                    }
                    else if (ch == 'D')
                    {
                        // Index - move cursor down, scroll if at bottom
                        if (_s.CursorY == _s.ScrollBottom)
                            _s.ScrollUp(1);
                        else
                            _s.MoveCursorRel(1, 0);
                        _state = State.Text;
                    }
                    else if (ch == 'E')
                    {
                        // Next line
                        _s.CursorX = 0;
                        if (_s.CursorY == _s.ScrollBottom)
                            _s.ScrollUp(1);
                        else
                            _s.MoveCursorRel(1, 0);
                        _state = State.Text;
                    }
                    else if (ch == 'H')
                    {
                        // Tab set - ignore
                        _state = State.Text;
                    }
                    else if (ch == '7')
                    {
                        // Save cursor - not implemented
                        _state = State.Text;
                    }
                    else if (ch == '8')
                    {
                        // Restore cursor - not implemented
                        _state = State.Text;
                    }
                    else if (ch == 'c')
                    {
                        // Reset terminal
                        _s.ClearAll();
                        _s.MoveCursorAbs(0, 0);
                        _s.Fg = ConsoleColor.Gray;
                        _s.Bg = ConsoleColor.Black;
                        _s.DecLineDrawing = false;
                        _state = State.Text;
                    }
                    else
                    {
                        // Unknown escape
                        _state = State.Text;
                    }
                    break;

                case State.Charset:
                    if (_charsetWhich == '(')
                    {
                        if (ch == '0') _s.DecLineDrawing = true;
                        else if (ch == 'B') _s.DecLineDrawing = false;
                    }
                    _state = State.Text;
                    break;

                case State.Csi:
                    HandleCsiChar(ch);
                    break;
            }
        }
    }

    private void EnterCsi()
    {
        _params.Clear();
        _paramAcc = -1;
        _state = State.Csi;
    }

    private void PushParam()
    {
        if (_paramAcc < 0) _params.Add(0);
        else _params.Add(_paramAcc);
        _paramAcc = -1;
    }

    private int P(int idx, int def)
    {
        if (idx < 0 || idx >= _params.Count) return def;
        return _params[idx];
    }

    private void HandleCsiChar(char ch)
    {
        // Parse digits/semicolons
        if (ch >= '0' && ch <= '9')
        {
            int d = ch - '0';
            _paramAcc = (_paramAcc < 0) ? d : (_paramAcc * 10 + d);
            return;
        }

        if (ch == ';')
        {
            PushParam();
            return;
        }

        // Final byte
        PushParam();

        switch (ch)
        {
            // Cursor positioning
            case 'H':
            case 'f':
                {
                    int row = P(0, 1) - 1;
                    int col = P(1, 1) - 1;
                    _s.MoveCursorAbs(row, col);
                    break;
                }
            case 'A': _s.MoveCursorRel(-P(0, 1), 0); break;  // CUU - up
            case 'B': _s.MoveCursorRel(P(0, 1), 0); break;   // CUD - down
            case 'C': _s.MoveCursorRel(0, P(0, 1)); break;   // CUF - forward
            case 'D': _s.MoveCursorRel(0, -P(0, 1)); break;  // CUB - back
            case 'E': // CNL - cursor next line
                _s.CursorX = 0;
                _s.MoveCursorRel(P(0, 1), 0);
                break;
            case 'F': // CPL - cursor previous line
                _s.CursorX = 0;
                _s.MoveCursorRel(-P(0, 1), 0);
                break;
            case 'G': // CHA - cursor horizontal absolute
                {
                    int col = P(0, 1) - 1;
                    _s.MoveCursorAbs(_s.CursorY, col);
                    break;
                }
            case 'd': // VPA - vertical position absolute
                {
                    int row = P(0, 1) - 1;
                    _s.MoveCursorAbs(row, _s.CursorX);
                    break;
                }

            // Erase
            case 'J': _s.EraseDisplay(P(0, 0)); break;
            case 'K': _s.EraseLine(P(0, 0)); break;

            // Scroll region
            case 'r':
                {
                    if (_params.Count >= 2)
                    {
                        int top = P(0, 1) - 1;
                        int bot = P(1, _s.Rows) - 1;
                        _s.SetScrollRegion(top, bot);
                    }
                    else
                    {
                        _s.SetScrollRegion(0, _s.Rows - 1);
                    }
                    break;
                }

            // Insert/Delete line
            case 'L': _s.InsertLines(P(0, 1)); break;
            case 'M': _s.DeleteLines(P(0, 1)); break;

            // Insert/Delete char
            case '@': _s.InsertChars(P(0, 1)); break;
            case 'P': _s.DeleteChars(P(0, 1)); break;

            // Scroll
            case 'S': _s.ScrollUp(P(0, 1)); break;
            case 'T': _s.ScrollDown(P(0, 1)); break;

            // SGR - colors
            case 'm':
                ApplySgr(_params);
                break;

            // Modes (ignored but parsed)
            case 'h':
            case 'l':
                // Set/reset mode - ignore
                break;

            case 'n':
                // Device status report - ignore
                break;

            case 's':
                // Save cursor position - not implemented
                break;

            case 'u':
                // Restore cursor position - not implemented
                break;

            default:
                // Unknown CSI command - ignore
                break;
        }

        _state = State.Text;
    }

    private void ApplySgr(List<int> ps)
    {
        if (ps.Count == 0) ps.Add(0);

        bool bold = false;

        for (int i = 0; i < ps.Count; i++)
        {
            int p = ps[i];

            if (p == 0)
            {
                _s.Fg = ConsoleColor.Gray;
                _s.Bg = ConsoleColor.Black;
                bold = false;
            }
            else if (p == 1)
            {
                bold = true;
            }
            else if (p == 22)
            {
                bold = false;
            }
            else if (p == 7)
            {
                // Reverse video
                (_s.Fg, _s.Bg) = (_s.Bg, _s.Fg);
            }
            else if (p == 27)
            {
                // Reverse off - swap back
                (_s.Fg, _s.Bg) = (_s.Bg, _s.Fg);
            }
            else if (30 <= p && p <= 37)
            {
                _s.Fg = Ansi16ToConsoleColor(p - 30, bold);
            }
            else if (p == 39)
            {
                _s.Fg = ConsoleColor.Gray;
            }
            else if (40 <= p && p <= 47)
            {
                _s.Bg = Ansi16ToConsoleColor(p - 40, false);
            }
            else if (p == 49)
            {
                _s.Bg = ConsoleColor.Black;
            }
            else if (90 <= p && p <= 97)
            {
                _s.Fg = Ansi16ToConsoleColor(p - 90, true);
            }
            else if (100 <= p && p <= 107)
            {
                _s.Bg = Ansi16ToConsoleColor(p - 100, true);
            }
        }
    }

    private static ConsoleColor Ansi16ToConsoleColor(int ansi, bool bright)
    {
        ansi = Math.Clamp(ansi, 0, 7);

        return (ansi, bright) switch
        {
            (0, false) => ConsoleColor.Black,
            (1, false) => ConsoleColor.DarkRed,
            (2, false) => ConsoleColor.DarkGreen,
            (3, false) => ConsoleColor.DarkYellow,
            (4, false) => ConsoleColor.DarkBlue,
            (5, false) => ConsoleColor.DarkMagenta,
            (6, false) => ConsoleColor.DarkCyan,
            (7, false) => ConsoleColor.Gray,

            (0, true) => ConsoleColor.DarkGray,
            (1, true) => ConsoleColor.Red,
            (2, true) => ConsoleColor.Green,
            (3, true) => ConsoleColor.Yellow,
            (4, true) => ConsoleColor.Blue,
            (5, true) => ConsoleColor.Magenta,
            (6, true) => ConsoleColor.Cyan,
            (7, true) => ConsoleColor.White,

            _ => ConsoleColor.Gray
        };
    }
}

/// <summary>
/// Custom terminal control that paints a ScreenBuffer directly using GDI+.
/// Replaces RichTextBox for proper VT100/ANSI terminal emulation.
/// </summary>
public sealed class TerminalControl : UserControl
{
    private ScreenBuffer? _screenBuffer;
    private readonly StringBuilder _inputBuffer = new();
    private int _charWidth = 8;
    private int _charHeight = 16;
    private bool _cursorVisible = true;
    private readonly System.Windows.Forms.Timer _cursorTimer;
    
    // Events
    public event Action<string>? OnCommandEntered;
    public event Action<int, int>? OnTerminalSizeChanged;
    public event Action<byte[]>? OnRawKeyData;  // For pass-through mode (training screen)
    
    // Properties
    public int TerminalCols { get; private set; } = 80;
    public int TerminalRows { get; private set; } = 24;
    public string InputText => _inputBuffer.ToString();
    
    /// <summary>
    /// When true, keystrokes are sent directly to server instead of buffered locally.
    /// Used for training screen where server handles input fields.
    /// </summary>
    public bool PassThroughMode { get; set; } = false;
    
    public TerminalControl()
    {
        // Enable double buffering to reduce flicker
        SetStyle(ControlStyles.AllPaintingInWmPaint | 
                 ControlStyles.UserPaint | 
                 ControlStyles.DoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        
        BackColor = Color.Black;
        Font = new Font("Consolas", 10f, FontStyle.Regular);
        
        // Cursor blink timer
        _cursorTimer = new System.Windows.Forms.Timer();
        _cursorTimer.Interval = 530;  // Standard cursor blink rate
        _cursorTimer.Tick += (s, e) => 
        {
            _cursorVisible = !_cursorVisible;
            InvalidateCursorArea();
        };
        _cursorTimer.Start();
        
        // Calculate initial character dimensions
        UpdateCharacterDimensions();
    }
    
    /// <summary>
    /// Set the screen buffer to render
    /// </summary>
    public void SetScreenBuffer(ScreenBuffer buffer)
    {
        _screenBuffer = buffer;
        Invalidate();
    }
    
    /// <summary>
    /// Clear the input buffer
    /// </summary>
    public void ClearInput()
    {
        _inputBuffer.Clear();
        InvalidateCursorArea();
    }
    
    /// <summary>
    /// Force a full repaint
    /// </summary>
    public void InvalidateTerminal()
    {
        Invalidate();
    }
    
    /// <summary>
    /// Update character dimensions based on current font
    /// </summary>
    private void UpdateCharacterDimensions()
    {
        using (var g = CreateGraphics())
        {
            // Measure character size using TextRenderer for accuracy
            var size = TextRenderer.MeasureText(g, "M", Font, Size.Empty, 
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            _charWidth = size.Width;
            _charHeight = Font.Height;
        }
        
        // Ensure minimum sizes
        if (_charWidth < 1) _charWidth = 8;
        if (_charHeight < 1) _charHeight = 16;
        
        UpdateTerminalSize();
    }
    
    /// <summary>
    /// Calculate terminal dimensions based on control size
    /// </summary>
    private void UpdateTerminalSize()
    {
        int newCols = Math.Max(20, ClientSize.Width / _charWidth);
        int newRows = Math.Max(5, ClientSize.Height / _charHeight);
        
        if (newCols != TerminalCols || newRows != TerminalRows)
        {
            TerminalCols = newCols;
            TerminalRows = newRows;
            
            // Resize the screen buffer if we have one
            _screenBuffer?.Resize(TerminalCols, TerminalRows);
            
            // Notify listeners
            OnTerminalSizeChanged?.Invoke(TerminalCols, TerminalRows);
        }
    }
    
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateTerminalSize();
    }
    
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateCharacterDimensions();
    }
    
    /// <summary>
    /// Invalidate just the cursor area for efficient blinking
    /// </summary>
    private void InvalidateCursorArea()
    {
        if (_screenBuffer == null) return;
        
        // Calculate cursor position considering input buffer
        int cursorRow = _screenBuffer.CursorY;
        int cursorCol = _screenBuffer.CursorX + _inputBuffer.Length;
        
        // Handle line wrapping
        while (cursorCol >= TerminalCols && cursorRow < TerminalRows - 1)
        {
            cursorCol -= TerminalCols;
            cursorRow++;
        }
        
        var rect = new Rectangle(cursorCol * _charWidth, cursorRow * _charHeight, 
                                 _charWidth, _charHeight);
        Invalidate(rect);
    }
    
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        
        var g = e.Graphics;
        g.Clear(BackColor);
        
        if (_screenBuffer == null) return;
        
        // Use TextRenderer for crisp text (like the system uses)
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        
        // Draw each cell from the buffer
        for (int row = 0; row < Math.Min(_screenBuffer.Rows, TerminalRows); row++)
        {
            for (int col = 0; col < Math.Min(_screenBuffer.Cols, TerminalCols); col++)
            {
                var cell = _screenBuffer.GetCell(row, col);
                int x = col * _charWidth;
                int y = row * _charHeight;
                
                // Draw background if not black (even for empty cells - important for input fields)
                if (cell.Bg != ConsoleColor.Black)
                {
                    using var bgBrush = new SolidBrush(ConsoleColorToColor(cell.Bg));
                    g.FillRectangle(bgBrush, x, y, _charWidth, _charHeight);
                }
                
                // Draw the character (skip spaces/nulls - they're just background)
                if (cell.Ch != ' ' && cell.Ch != '\0')
                {
                    var fgColor = ConsoleColorToColor(cell.Fg);
                    TextRenderer.DrawText(g, cell.Ch.ToString(), Font, 
                        new Point(x, y), fgColor, flags);
                }
            }
        }
        
        // Draw the input buffer after the cursor position
        if (_inputBuffer.Length > 0)
        {
            int inputRow = _screenBuffer.CursorY;
            int inputCol = _screenBuffer.CursorX;
            
            for (int i = 0; i < _inputBuffer.Length; i++)
            {
                int x = inputCol * _charWidth;
                int y = inputRow * _charHeight;
                
                // Draw the character in gray (user input color)
                TextRenderer.DrawText(g, _inputBuffer[i].ToString(), Font,
                    new Point(x, y), Color.FromArgb(192, 192, 192), flags);
                
                inputCol++;
                if (inputCol >= TerminalCols)
                {
                    inputCol = 0;
                    inputRow++;
                    if (inputRow >= TerminalRows) break;
                }
            }
        }
        
        // Draw cursor
        if (_cursorVisible && Focused)
        {
            int cursorRow = _screenBuffer.CursorY;
            int cursorCol = PassThroughMode ? _screenBuffer.CursorX : _screenBuffer.CursorX + _inputBuffer.Length;
            
            // Handle line wrapping (only in normal mode)
            if (!PassThroughMode)
            {
                while (cursorCol >= TerminalCols && cursorRow < TerminalRows - 1)
                {
                    cursorCol -= TerminalCols;
                    cursorRow++;
                }
            }
            
            int cx = cursorCol * _charWidth;
            int cy = cursorRow * _charHeight;
            
            if (PassThroughMode)
            {
                // Training screen cursor: dark grey background with white text
                using var cursorBrush = new SolidBrush(Color.FromArgb(96, 96, 96));  // Dark grey
                g.FillRectangle(cursorBrush, cx, cy, _charWidth, _charHeight);
                
                // Draw the character at cursor position in white (if any)
                var cell = _screenBuffer.GetCell(cursorRow, cursorCol);
                if (cell.Ch != ' ' && cell.Ch != '\0')
                {
                    TextRenderer.DrawText(g, cell.Ch.ToString(), Font,
                        new Point(cx, cy), Color.White, flags);
                }
            }
            else
            {
                // Normal cursor: semi-transparent grey block
                using var cursorBrush = new SolidBrush(Color.FromArgb(180, 192, 192, 192));
                g.FillRectangle(cursorBrush, cx, cy, _charWidth, _charHeight);
            }
        }
    }
    
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Allow Ctrl+C for copy (future: implement copy)
        if (e.Control && e.KeyCode == Keys.C)
        {
            base.OnKeyDown(e);
            return;
        }
        
        // Pass-through mode: send keystrokes directly to server (for training screen)
        if (PassThroughMode)
        {
            byte[]? data = null;
            
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    data = new byte[] { 0x0D };  // CR
                    break;
                case Keys.Back:
                    data = new byte[] { 0x08 };  // BS
                    break;
                case Keys.Escape:
                    data = new byte[] { 0x1B };  // ESC
                    break;
                case Keys.Up:
                    data = new byte[] { 0x1B, 0x5B, 0x41 };  // ESC [ A
                    break;
                case Keys.Down:
                    data = new byte[] { 0x1B, 0x5B, 0x42 };  // ESC [ B
                    break;
                case Keys.Right:
                    data = new byte[] { 0x1B, 0x5B, 0x43 };  // ESC [ C
                    break;
                case Keys.Left:
                    data = new byte[] { 0x1B, 0x5B, 0x44 };  // ESC [ D
                    break;
                case Keys.Space:
                    data = new byte[] { 0x20 };  // Space
                    break;
                case Keys.Tab:
                    data = new byte[] { 0x09 };  // Tab
                    break;
                case Keys.Delete:
                    data = new byte[] { 0x7F };  // DEL
                    break;
            }
            
            if (data != null)
            {
                // Only suppress KeyPress for special keys we handled
                e.Handled = true;
                e.SuppressKeyPress = true;
                OnRawKeyData?.Invoke(data);
            }
            // For regular character keys, let OnKeyPress handle them
            return;
        }
        
        // Normal mode: buffer input locally
        
        // Handle Enter - send command
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            
            string command = _inputBuffer.ToString();
            _inputBuffer.Clear();
            _cursorVisible = true;  // Reset cursor visibility
            
            OnCommandEntered?.Invoke(command);
            Invalidate();
            return;
        }
        
        // Handle Backspace
        if (e.KeyCode == Keys.Back)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            
            if (_inputBuffer.Length > 0)
            {
                _inputBuffer.Remove(_inputBuffer.Length - 1, 1);
                _cursorVisible = true;
                Invalidate();
            }
            return;
        }
        
        // Handle Escape - clear input
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            
            if (_inputBuffer.Length > 0)
            {
                _inputBuffer.Clear();
                _cursorVisible = true;
                Invalidate();
            }
            return;
        }
        
        // Block navigation and special keys
        if (e.Control || e.Alt ||
            e.KeyCode == Keys.Left || e.KeyCode == Keys.Right ||
            e.KeyCode == Keys.Up || e.KeyCode == Keys.Down ||
            e.KeyCode == Keys.Home || e.KeyCode == Keys.End ||
            e.KeyCode == Keys.PageUp || e.KeyCode == Keys.PageDown ||
            e.KeyCode == Keys.Delete || e.KeyCode == Keys.Insert)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        
        base.OnKeyDown(e);
    }
    
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        // Pass-through mode: send characters directly to server
        if (PassThroughMode)
        {
            if (!char.IsControl(e.KeyChar))
            {
                // Send the character directly
                OnRawKeyData?.Invoke(new byte[] { (byte)e.KeyChar });
                e.Handled = true;
            }
            return;
        }
        
        // Normal mode: buffer locally
        
        // Ignore control characters
        if (char.IsControl(e.KeyChar) && e.KeyChar != '\r' && e.KeyChar != '\b')
        {
            e.Handled = true;
            return;
        }
        
        // Add printable characters to input buffer
        if (!char.IsControl(e.KeyChar))
        {
            _inputBuffer.Append(e.KeyChar);
            _cursorVisible = true;
            Invalidate();
            e.Handled = true;
        }
        
        base.OnKeyPress(e);
    }
    
    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _cursorVisible = true;
        _cursorTimer.Start();
        Invalidate();
    }
    
    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _cursorTimer.Stop();
        _cursorVisible = false;
        Invalidate();
    }
    
    protected override bool IsInputKey(Keys keyData)
    {
        // Allow arrow keys and other navigation to be handled by KeyDown
        switch (keyData)
        {
            case Keys.Up:
            case Keys.Down:
            case Keys.Left:
            case Keys.Right:
            case Keys.Tab:
            case Keys.Escape:
                return true;
        }
        return base.IsInputKey(keyData);
    }
    
    /// <summary>
    /// Convert ConsoleColor to System.Drawing.Color
    /// </summary>
    private static Color ConsoleColorToColor(ConsoleColor cc)
    {
        return cc switch
        {
            ConsoleColor.Black => Color.FromArgb(0, 0, 0),
            ConsoleColor.DarkBlue => Color.FromArgb(0, 0, 128),
            ConsoleColor.DarkGreen => Color.FromArgb(0, 128, 0),
            ConsoleColor.DarkCyan => Color.FromArgb(0, 128, 128),
            ConsoleColor.DarkRed => Color.FromArgb(128, 0, 0),
            ConsoleColor.DarkMagenta => Color.FromArgb(128, 0, 128),
            ConsoleColor.DarkYellow => Color.FromArgb(128, 128, 0),
            ConsoleColor.Gray => Color.FromArgb(192, 192, 192),
            ConsoleColor.DarkGray => Color.FromArgb(128, 128, 128),
            ConsoleColor.Blue => Color.FromArgb(0, 0, 255),
            ConsoleColor.Green => Color.FromArgb(0, 255, 0),
            ConsoleColor.Cyan => Color.FromArgb(0, 255, 255),
            ConsoleColor.Red => Color.FromArgb(255, 0, 0),
            ConsoleColor.Magenta => Color.FromArgb(255, 0, 255),
            ConsoleColor.Yellow => Color.FromArgb(255, 255, 0),
            ConsoleColor.White => Color.FromArgb(255, 255, 255),
            _ => Color.FromArgb(192, 192, 192)
        };
    }
    
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cursorTimer?.Stop();
            _cursorTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}


