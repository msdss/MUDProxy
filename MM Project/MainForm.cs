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
    private TelnetConnection _telnetConnection = null!;
    private bool _isConnected = false;
    
    // Logon automation state
    private bool _isInLoginPhase = true;  // True until we see HP bar
    private HashSet<string> _triggeredLogonSequences = new();  // Track which sequences have fired
    
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
    private readonly GameManager _gameManager = new();
    private BuffManager _buffManager = null!;  // Shortcut to _gameManager.BuffManager for buff-specific calls
    
// Message Router
    private MessageRouter _messageRouter = null!;
    
    // Log Renderer
    private readonly LogRenderer _logRenderer = new();

    // UI Components - Main
    private TerminalControl _terminalControl = null!;  // MUD server output - custom VT100 terminal
    private RichTextBox _systemLogTextBox = null!;  // System/proxy logs
    private SplitContainer _terminalSplitContainer = null!;  // Vertical split: MUD output / logs
    private Label _statusLabel = null!;
    private Label _expStatusLabel = null!;  // Experience status on right side
    private Button _connectButton = null!;
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

    public MainForm()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        
        // Initialize telnet connection
        _telnetConnection = new TelnetConnection();
        _telnetConnection.OnDataReceived += TelnetConnection_OnDataReceived;
        _telnetConnection.OnStatusChanged += TelnetConnection_OnStatusChanged;
        _telnetConnection.OnLogMessage += TelnetConnection_OnLogMessage;
        
        // Initialize shortcut to BuffManager for buff-specific calls
        _buffManager = _gameManager.BuffManager;
        
        // Initialize message router
        _messageRouter = new MessageRouter(_gameManager);
        _messageRouter.OnCombatStateChanged += MessageRouter_OnCombatStateChanged;
        _messageRouter.OnPlayerStatsUpdated += MessageRouter_OnPlayerStatsUpdated;
        _messageRouter.OnCombatTickDetected += MessageRouter_OnCombatTickDetected;
        _messageRouter.OnPlayerDeath += MessageRouter_OnPlayerDeath;
        _messageRouter.OnLoginComplete += MessageRouter_OnLoginComplete;
        _messageRouter.OnPauseStateChanged += MessageRouter_OnPauseStateChanged;
        
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
    
    private void TelnetConnection_OnDataReceived(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => TelnetConnection_OnDataReceived(text));
            return;
        }
        
        // Process for game logic (stripped version)
        string strippedText = StripAnsi(text);
        ProcessServerMessage(strippedText);
        
        // Check for logon automation
        if (_isInLoginPhase)
        {
            CheckLogonAutomation(strippedText);
        }
        
        // Display on terminal
        DisplayMudTextDirect(text);
    }
    
    private void TelnetConnection_OnStatusChanged(bool connected, string statusMessage)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => TelnetConnection_OnStatusChanged(connected, statusMessage));
            return;
        }
        
        _isConnected = connected;
        
        if (connected)
        {
            _isInLoginPhase = true;  // Reset login phase - wait for login sequence to complete
            _messageRouter.ResetLoginPhase();  // Reset the message router's login tracking too
            _triggeredLogonSequences.Clear();  // Reset so logon automation triggers again on reconnect
            UpdateConnectionUI(true);
            UpdateStatus("Connected", Color.White);
            // Connection established - managers will be notified via message processing
        }
        else
        {
            UpdateConnectionUI(false);
            UpdateStatus("Disconnected", Color.White);
            _gameManager.OnDisconnected();
            
            // Reset terminal state
            _userInputBuffer.Clear();
            _serverOutputBuffer.Clear();
            _terminalControl.ClearInput();
        }
    }
    
    private void TelnetConnection_OnLogMessage(string message)
    {
        LogMessage(message, MessageType.System);
    }

    private void MessageRouter_OnCombatStateChanged(bool inCombat)
    {
        SetCombatState(inCombat);
    }
    
    private void MessageRouter_OnPlayerStatsUpdated(int currentHp, int currentMana, string manaType)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => MessageRouter_OnPlayerStatsUpdated(currentHp, currentMana, manaType));
            return;
        }
        
        // Update current values only - max values come from BuffManager (set by stat command)
        _currentHp = currentHp;
        _currentMana = currentMana;
        _manaType = manaType;
        
        // Get max values from BuffManager (set by stat command parsing)
        if (_gameManager.PlayerStateManager.MaxHp > 0)
        {
            _maxHp = _gameManager.PlayerStateManager.MaxHp;
        }
        if (_gameManager.PlayerStateManager.MaxMana > 0)
        {
            _maxMana = _gameManager.PlayerStateManager.MaxMana;
        }
        
        // Update the self status panel
        var info = _gameManager.PlayerStateManager.PlayerInfo;
        _selfStatusPanel.UpdatePlayerExact(
            string.IsNullOrEmpty(info.Name) ? "(Unknown)" : info.Name,
            info.Class,
            _currentHp,
            _maxHp,
            _currentMana,
            _maxMana
        );
    }
    
    private void MessageRouter_OnCombatTickDetected()
    {
        RecordTick();
    }
    
    private void MessageRouter_OnPlayerDeath()
    {
        LogMessage("☠️ YOU DIED!", MessageType.System);
    }
    
    private void MessageRouter_OnLoginComplete()
    {
        _isInLoginPhase = false;
        LogMessage("✅ Login complete - entered game", MessageType.System);
    }
    
    private void MessageRouter_OnPauseStateChanged(bool isPaused)
    {
        if (InvokeRequired)
            BeginInvoke(UpdateToggleButtonStates);
        else
            UpdateToggleButtonStates();
    }
    private void MainForm_Shown(object? sender, EventArgs e)
    {
        // Check for auto-load last character
        if (_gameManager.AppSettings.AutoLoadLastCharacter && !string.IsNullOrEmpty(_gameManager.AppSettings.LastCharacterPath))
        {
            if (File.Exists(_gameManager.AppSettings.LastCharacterPath))
            {
                LogMessage($"Auto-loading last character: {Path.GetFileName(_gameManager.AppSettings.LastCharacterPath)}", MessageType.System);
                var (success, message) = _gameManager.LoadCharacterProfile(_gameManager.AppSettings.LastCharacterPath);
                if (success)
                {
                    RefreshPlayerInfo();
                    RefreshBuffDisplay();
                    UpdateToggleButtonStates();
                    LogMessage($"DEBUG: CombatEnabled={_gameManager.CombatManager.CombatEnabled}, CombatAutoEnabled={_gameManager.CombatAutoEnabled}", MessageType.System);
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
                LogMessage($"Last character file not found: {_gameManager.AppSettings.LastCharacterPath}", MessageType.System);
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
        _gameManager.OnBuffsChanged += () => BeginInvoke(RefreshBuffDisplay);
        _gameManager.OnPartyChanged += () => BeginInvoke(RefreshPartyDisplay);
        _gameManager.OnPlayerInfoChanged += () => BeginInvoke(RefreshPlayerInfo);
        _gameManager.OnBbsSettingsChanged += () => BeginInvoke(RefreshBbsSettingsDisplay);
        _gameManager.OnLogMessage += (msg) => LogMessage(msg, MessageType.System);
        _gameManager.OnRoomTrackerLogMessage += (msg) => LogMessage(msg, MessageType.RoomTracker);
        _gameManager.OnSendCommand += SendCommandToServer;
        _gameManager.OnHangupRequested += () => BeginInvoke(HandleRemoteHangup);
        _gameManager.OnRelogRequested += () => BeginInvoke(HandleRemoteRelog);
        _gameManager.OnAutomationStateChanged += () => BeginInvoke(RefreshAutomationButtons);
        _gameManager.OnTrainingScreenChanged += (inTraining) => BeginInvoke(() => 
        {
            _terminalControl.PassThroughMode = inTraining;
            if (inTraining)
            {
                _terminalControl.ClearInput();  // Clear any buffered input
            }
        });
        
        // Initialize CombatManager with dependencies
        _gameManager.CombatManager.Initialize(
            _gameManager.PlayerDatabase,
            _gameManager.MonsterDatabase,
            () => _inCombat,
            () => _maxMana > 0 ? (_currentMana * 100 / _maxMana) : 100
        );
        _gameManager.CombatManager.OnSendCommand += SendCommandToServer;
    }
        
    private void SendCommandToServer(string command)
    {
    if (!_isConnected) return;
    
    Task.Run(async () => 
    {
        await _telnetConnection.SendCommandAsync(command);
    });
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
        
        _gameManager.CheckParCommand();
        _gameManager.CheckHealthRequests();
    }
    
    /// <summary>
    /// Handle command entered in the terminal control
    /// </summary>
    private void TerminalControl_CommandEntered(string command)
    {
        // Notify room tracker of outgoing command (must be BEFORE send, so tracker
        // knows the direction before the server's room response arrives)
        _gameManager.RoomTracker.OnPlayerCommand(command);
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
        if (!_isConnected)
            return;
        
        Task.Run(async () => 
        {
            await _telnetConnection.SendDataAsync(data);
        });
    }
    
    /// <summary>
    /// Handle terminal size change
    /// </summary>
    private void TerminalControl_SizeChanged(int cols, int rows)
    {
        // Send NAWS (window size) to server if connected
        if (_isConnected)
        {
            _ = SendNawsAsync(cols, rows);
        }
    }
    
    /// <summary>
    /// Send NAWS (window size) to server asynchronously
    /// </summary>
    private async Task SendNawsAsync(int cols, int rows)
    {
        if (!_isConnected) return;
        
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
        
        await _telnetConnection.SendDataAsync(naws);
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
        _gameManager.CheckAutoRecast();
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
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("New Character", null, NewCharacter_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Load Character...", null, LoadCharacter_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Save Character", null, SaveCharacter_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Save Character As...", null, SaveCharacterAs_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        var autoLoadMenuItem = new ToolStripMenuItem("Auto-Load Last Character", null, ToggleAutoLoad_Click) 
        { 
            ForeColor = Color.White, 
            BackColor = Color.FromArgb(45, 45, 45),
            CheckOnClick = true,
            Checked = _gameManager.AppSettings.AutoLoadLastCharacter
        };
        fileMenu.DropDownItems.Add(autoLoadMenuItem);
        
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Display Backscroll", null, DisplayBackscroll_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        
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
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Save System Log...", null, SaveLog_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Clear System Log", null, ClearLog_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
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
        gameDataMenu.DropDownItems.Add(new ToolStripMenuItem("Pathfinding Test...", null, OpenPathfindingTest_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });
        gameDataMenu.DropDownItems.Add(new ToolStripSeparator());
        gameDataMenu.DropDownItems.Add(new ToolStripMenuItem("Player DB...", null, OpenPlayerDB_Click) { ForeColor = Color.White, BackColor = Color.FromArgb(45, 45, 45) });

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
            BackColor = _gameManager.CombatManager.CombatEnabled ? Color.FromArgb(70, 130, 180) : Color.FromArgb(60, 60, 60),
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
            BackColor = _gameManager.HealingManager.HealingEnabled ? Color.FromArgb(70, 130, 180) : Color.FromArgb(60, 60, 60),
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
            BackColor = _gameManager.CureManager.CuringEnabled ? Color.FromArgb(70, 130, 180) : Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _cureToggleButton.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        _cureToggleButton.Click += CureToggleButton_Click;


        settingsPanel.Controls.AddRange(new Control[] {
            _connectButton,
            _serverAddressLabel,
            _pauseButton, _combatToggleButton, _healToggleButton, _buffToggleButton, _cureToggleButton
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
            Text = "System Log",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            Location = new Point(5, 2),
            AutoSize = true
        };
        _autoScrollLogsCheckBox = new CheckBox
        {
            Text = "Auto-Scroll",
            Checked = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 7),
            AutoSize = true,
            Location = new Point(90, 1)
        };
        _showTimestampsCheckBox = new CheckBox
        {
            Text = "Timestamps",
            Checked = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 7),
            AutoSize = true,
            Location = new Point(180, 1)
        };
        var clearLogLink = new Label
        {
            Text = "Clear",
            Font = new Font("Segoe UI", 7, FontStyle.Underline),
            ForeColor = Color.FromArgb(120, 120, 120),
            AutoSize = true,
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        clearLogLink.Click += ClearLog_Click;
        clearLogLink.MouseEnter += (s, e) => clearLogLink.ForeColor = Color.FromArgb(255, 100, 100);
        clearLogLink.MouseLeave += (s, e) => clearLogLink.ForeColor = Color.FromArgb(120, 120, 120);
        // Position right-aligned
        logHeaderPanel.Resize += (s, e) =>
        {
            clearLogLink.Location = new Point(logHeaderPanel.Width - clearLogLink.Width - 6, 3);
        };
        clearLogLink.Location = new Point(logHeaderPanel.Width - clearLogLink.Width - 6, 3);
        logHeaderPanel.Controls.Add(logHeaderLabel);
        logHeaderPanel.Controls.Add(_autoScrollLogsCheckBox);
        logHeaderPanel.Controls.Add(_showTimestampsCheckBox);
        logHeaderPanel.Controls.Add(clearLogLink);
        
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

    private void RepositionPartySection()
    {
        int y = _selfStatusPanelBaseY + _selfStatusPanel.Height + 6;
        
        _partySeparator.Location = new Point(6, y);
        y += 7;  // separator height + spacing
        
        _partyLabel.Location = new Point(6, y);
        y += 16;
        
        _partyContainer.Location = new Point(6, y);
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
    
    private void RecordTick()
    {
        _lastTickTime = DateTime.Now;
        _nextTickTime = _lastTickTime.Value.AddMilliseconds(TICK_INTERVAL_MS);

        if (InvokeRequired)
            BeginInvoke(() => _lastTickTimeLabel.Text = $"Last Tick: {_lastTickTime:HH:mm:ss.f}");
        else
            _lastTickTimeLabel.Text = $"Last Tick: {_lastTickTime:HH:mm:ss.f}";
        
        // Unblock casting since a new tick just happened
        _gameManager.OnCombatTick();
        
        // Check if we should send an attack spell on this tick (mana may have regenerated)
        _gameManager.CombatManager.OnCombatTick();
        
        // Check for auto-recast after each tick (this is the mid-round window)
        // Small delay to let the tick fully process before recasting
        Task.Delay(100).ContinueWith(_ => _gameManager.CheckAutoRecast());
    }

    private void ProcessServerMessage(string text)
    {
        // Route message to MessageRouter for processing
        _messageRouter.ProcessMessage(text);
    }

    private void SetCombatState(bool inCombat)
    {
        _inCombat = inCombat;
        
        // Notify BuffManager of combat state change
        _gameManager.PlayerStateManager.SetCombatState(inCombat);

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

    private void Disconnect()
    {
        _telnetConnection.Disconnect();
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
    /// Check incoming text for logon automation triggers
    /// </summary>
    private void CheckLogonAutomation(string text)
    {
        var sequences = _gameManager.BbsSettings.LogonSequences;
        
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
        await _telnetConnection.SendDataAsync(data);
    }

    /// <summary>
    /// Send a command to the server (adds carriage return/line feed)
    /// </summary>
    public async Task SendCommandAsync(string command)
    {
        if (!_isConnected)
        {
            LogMessage("Cannot send command - not connected", MessageType.System);
            return;
        }
        
        await _telnetConnection.SendCommandAsync(command);
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

        _logRenderer.LogMessageWithAnsi(message, type, _systemLogTextBox, 
            _autoScrollLogsCheckBox, _showTimestampsCheckBox.Checked);
    }

private void LogMessage(string message, MessageType type)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => LogMessage(message, type));
            return;
        }

        _logRenderer.LogMessage(message, type, _systemLogTextBox, 
            _autoScrollLogsCheckBox, _showTimestampsCheckBox.Checked);
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

    private void ApplySystemLogVisibility()
    {
        _terminalSplitContainer.Panel2Collapsed = !_displaySystemLog;
    }
    
    private void LoadUiSettings()
    {
        // Load from BuffManager's settings (stored in settings.json)
        _displaySystemLog = _gameManager.AppSettings.DisplaySystemLog;
    }
    
    private void SaveUiSettings()
    {
        _gameManager.AppSettings.DisplaySystemLog = _displaySystemLog;
        _gameManager.AppSettings.Save();
    }

/// <summary>
    /// Apply window position and size from the loaded character profile.
    /// Validates that the window is visible on at least one screen.
    /// </summary>
    private void ApplyWindowSettings()
    {
        var ws = _gameManager.WindowSettings;
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
        if (string.IsNullOrEmpty(_gameManager.CurrentProfilePath))
            return;
        
        var isMaximized = this.WindowState == FormWindowState.Maximized;
        var bounds = isMaximized ? this.RestoreBounds : this.Bounds;
        
        _gameManager.WindowSettings = new WindowSettings
        {
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height,
            IsMaximized = isMaximized
        };
        
        // Auto-save the profile so window settings persist
        if (!string.IsNullOrEmpty(_gameManager.CurrentProfilePath))
        {
            _gameManager.SaveCharacterProfile(_gameManager.CurrentProfilePath);
        }
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