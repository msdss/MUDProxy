using System.Text.RegularExpressions;

namespace MudProxyViewer;

/// <summary>
/// Handles remote commands received from other players via telepath, say, gangpath, etc.
/// Checks permissions via PlayerDatabaseManager before executing commands.
/// </summary>
public class RemoteCommandManager
{
    private readonly PlayerDatabaseManager _playerDatabase;
    private readonly Func<int> _getCurrentHp;
    private readonly Func<int> _getMaxHp;
    private readonly Func<int> _getCurrentMana;
    private readonly Func<int> _getMaxMana;
    private readonly Func<string> _getManaType;  // Returns "MA" or "KAI"
    private readonly Func<bool> _getCombatEnabled;
    private readonly Action<bool> _setCombatEnabled;
    private readonly Func<bool> _getHealEnabled;
    private readonly Action<bool> _setHealEnabled;
    private readonly Func<bool> _getCureEnabled;
    private readonly Action<bool> _setCureEnabled;
    private readonly Func<bool> _getBuffEnabled;
    private readonly Action<bool> _setBuffEnabled;
    private Func<string, bool>? _isPartyMember;
    public void SetPartyMemberCheck(Func<string, bool> isPartyMember) => _isPartyMember = isPartyMember;
    
    // Experience tracking delegates
    private readonly Func<int> _getLevel;
    private readonly Func<long> _getExpNeeded;
    private readonly Func<long> _getExpPerHour;
    private readonly Func<string> _getTimeToLevel;
    private readonly Func<long> _getSessionExpGained;
    private readonly Action _resetExpTracker;
    
    // Events
    public event Action<string>? OnLogMessage;
    public event Action<string>? OnSendCommand;
    public event Action? OnHangupRequested;
    public event Action? OnRelogRequested;
    public event Action? OnAutomationStateChanged;  // Fires when any automation toggle changes
    
    // Communication method for responses
    private enum CommunicationMethod
    {
        Telepath,
        Gangpath,
        Say
    }
    
    // Track the current communication method for responses
    private CommunicationMethod _currentMethod = CommunicationMethod.Telepath;
    
    // Message parsing patterns
    // Telepath: "Xyz telepaths: @command"
    // Note: No ^ anchor - message may have leading content/whitespace
    private static readonly Regex TelepathRegex = new(
        @"(\w+)\s+telepaths:\s*(.+?)(?:\r?\n|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    // Say: "Xyz says "@command""
    private static readonly Regex SayRegex = new(
        @"(\w+)\s+says\s+""(.+?)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    // Direct Say: "Xyz says (to you) "@command""
    private static readonly Regex DirectSayRegex = new(
        @"(\w+)\s+says\s+\(to you\)\s+""(.+?)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    // Gangpath: "Xyz gangpaths: @command"
    private static readonly Regex GangpathRegex = new(
        @"(\w+)\s+gangpaths:\s*(.+?)(?:\r?\n|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    public RemoteCommandManager(
        PlayerDatabaseManager playerDatabase,
        Func<int> getCurrentHp,
        Func<int> getMaxHp,
        Func<int> getCurrentMana,
        Func<int> getMaxMana,
        Func<string> getManaType,
        Func<bool> getCombatEnabled,
        Action<bool> setCombatEnabled,
        Func<bool> getHealEnabled,
        Action<bool> setHealEnabled,
        Func<bool> getCureEnabled,
        Action<bool> setCureEnabled,
        Func<bool> getBuffEnabled,
        Action<bool> setBuffEnabled,
        Func<int> getLevel,
        Func<long> getExpNeeded,
        Func<long> getExpPerHour,
        Func<string> getTimeToLevel,
        Func<long> getSessionExpGained,
        Action resetExpTracker)
    {
        _playerDatabase = playerDatabase;
        _getCurrentHp = getCurrentHp;
        _getMaxHp = getMaxHp;
        _getCurrentMana = getCurrentMana;
        _getMaxMana = getMaxMana;
        _getManaType = getManaType;
        _getCombatEnabled = getCombatEnabled;
        _setCombatEnabled = setCombatEnabled;
        _getHealEnabled = getHealEnabled;
        _setHealEnabled = setHealEnabled;
        _getCureEnabled = getCureEnabled;
        _setCureEnabled = setCureEnabled;
        _getBuffEnabled = getBuffEnabled;
        _setBuffEnabled = setBuffEnabled;
        _getLevel = getLevel;
        _getExpNeeded = getExpNeeded;
        _getExpPerHour = getExpPerHour;
        _getTimeToLevel = getTimeToLevel;
        _getSessionExpGained = getSessionExpGained;
        _resetExpTracker = resetExpTracker;
    }
    
    /// <summary>
    /// Process a message to check for remote commands
    /// </summary>
    /// <returns>True if a remote command was detected and processed</returns>
    public bool ProcessMessage(string message)
    {
        // Quick check - does this message potentially contain a command?
        if (!message.Contains("@"))
            return false;
        
        // Debug: Log messages that contain @ to help diagnose parsing issues
        if (message.Contains("telepaths:") || message.Contains("says") || message.Contains("gangpaths:"))
        {
            OnLogMessage?.Invoke($"📡 DEBUG: Checking message for remote command: [{message.Replace("\r", "\\r").Replace("\n", "\\n")}]");
        }
        
        // Try to parse the message as a remote command
        var (senderName, commandText, method) = ParseMessage(message);
        
        if (string.IsNullOrEmpty(senderName) || string.IsNullOrEmpty(commandText))
            return false;
        
        // Check if this looks like a command (starts with @)
        if (!commandText.StartsWith("@"))
            return false;
        
        OnLogMessage?.Invoke($"📡 Remote command from {senderName} via {method}: {commandText}");
        
        // Store the method for response routing
        _currentMethod = method;
        
        // Process the command
        ProcessCommand(senderName, commandText);
        return true;
    }
    
    /// <summary>
    /// Parse a message to extract sender name, command text, and communication method
    /// </summary>
    private (string senderName, string commandText, CommunicationMethod method) ParseMessage(string message)
    {
        // Try each pattern in order of specificity
        
        // Direct Say (most specific)
        var match = DirectSayRegex.Match(message);
        if (match.Success)
            return (match.Groups[1].Value, match.Groups[2].Value.Trim(), CommunicationMethod.Say);
        
        // Telepath
        match = TelepathRegex.Match(message);
        if (match.Success)
            return (match.Groups[1].Value, match.Groups[2].Value.Trim(), CommunicationMethod.Telepath);
        
        // Gangpath
        match = GangpathRegex.Match(message);
        if (match.Success)
            return (match.Groups[1].Value, match.Groups[2].Value.Trim(), CommunicationMethod.Gangpath);
        
        // Say (least specific - check last to avoid false matches)
        match = SayRegex.Match(message);
        if (match.Success)
            return (match.Groups[1].Value, match.Groups[2].Value.Trim(), CommunicationMethod.Say);
        
        return (string.Empty, string.Empty, CommunicationMethod.Telepath);
    }
    
    /// <summary>
    /// Process a command from a sender
    /// </summary>
    private void ProcessCommand(string senderName, string commandText)
    {
        // Parse the command and arguments
        var parts = commandText.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLower();
        var args = parts.Length > 1 ? parts[1] : string.Empty;
        
        // Only process known commands - ignore user-defined messages like @diseased
        if (!IsKnownCommand(command))
        {
            OnLogMessage?.Invoke($"📡 Ignoring non-command message: {command}");
            return;
        }
        
        // Get player permissions
        var player = _playerDatabase.GetPlayer(senderName);
        
        switch (command)
        {
            case "@exp":
                HandleQueryExperience(senderName, player);
                break;
                
            case "@where":
                HandleQueryLocation(senderName, player);
                break;
                
            case "@invite":
                HandleRequestInvite(senderName, player);
                break;
                
            case "@do":
                HandleExecuteCommand(senderName, player, args);
                break;
                
            case "@reset":
                HandleResetExpTracker(senderName, player);
                break;
                
            case "@auto-all":
            case "@auto-combat":
            case "@auto-bless":
            case "@auto-cure":
            case "@auto-heal":
                HandleAlterSettings(senderName, player, command, args);
                break;
                
            case "@health":
                HandleQueryHealth(senderName, player);
                break;
                
            case "@enc":
            case "@wealth":
                HandleQueryInventory(senderName, player);
                break;
                
            case "@goto":
                HandleMovePlayer(senderName, player, args);
                break;
                
            case "@hangup":
                HandleHangup(senderName, player);
                break;
                
            case "@relog":
                HandleRelog(senderName, player);
                break;
                
            case "@divert":
                HandleDivertConversations(senderName, player);
                break;
                
            case "@join":
                // @join is sent TO other players, not a command we process
                OnLogMessage?.Invoke($"📡 Ignoring @join (outbound command)");
                break;
        }
    }
    
    /// <summary>
    /// Check if a command is a known remote command
    /// </summary>
    private static bool IsKnownCommand(string command)
    {
        return command switch
        {
            "@exp" => true,
            "@where" => true,
            "@invite" => true,
            "@do" => true,
            "@reset" => true,
            "@auto-all" => true,
            "@auto-combat" => true,
            "@auto-bless" => true,
            "@auto-cure" => true,
            "@auto-heal" => true,
            "@health" => true,
            "@enc" => true,
            "@wealth" => true,
            "@goto" => true,
            "@hangup" => true,
            "@relog" => true,
            "@divert" => true,
            "@join" => true,  // Known but ignored (outbound)
            _ => false
        };
    }
    
    #region Permission Checking
    
    private bool HasPermission(PlayerData? player, Func<RemotePermissions, bool> permissionCheck, string senderName)
    {
        if (player == null)
        {
            OnLogMessage?.Invoke($"📡 Permission denied for {senderName} (not in PlayerDB)");
            SendResponse(senderName, "Permission denied.");
            return false;
        }
        
        if (!permissionCheck(player.AllowedRemotes))
        {
            OnLogMessage?.Invoke($"📡 Permission denied for {senderName} (permission not granted)");
            SendResponse(senderName, "Permission denied.");
            return false;
        }
        
        return true;
    }
    
    #endregion
    
    #region Command Handlers
    
    private void HandleQueryExperience(string senderName, PlayerData? player)
    {
        if (!HasPermission(player, p => p.QueryExperience, senderName))
            return;
        
        var level = _getLevel();
        var sessionExp = _getSessionExpGained();
        var expNeeded = _getExpNeeded();
        var expPerHour = _getExpPerHour();
        var timeToLevel = _getTimeToLevel();
        
        // Format: Level: 72 / Made: 317.2M / Needed: 898.8M / Rate: 5.3M/hr / Will level in: Now!
        var response = $"Level: {level} / " +
                       $"Made: {ExperienceTracker.FormatNumberAbbreviated(sessionExp)} / " +
                       $"Needed: {ExperienceTracker.FormatNumberAbbreviated(expNeeded)} / " +
                       $"Rate: {ExperienceTracker.FormatNumberAbbreviated(expPerHour)}/hr / " +
                       $"Will level in: {timeToLevel}";
        
        OnLogMessage?.Invoke($"📡 Sending exp info to {senderName}: {response}");
        SendResponse(senderName, response);
    }
    
    private void HandleQueryLocation(string senderName, PlayerData? player)
    {
        if (!HasPermission(player, p => p.QueryLocation, senderName))
            return;
        
        SendResponse(senderName, "Not yet implemented");
    }
    
    private void HandleRequestInvite(string senderName, PlayerData? player)
    {
        if (!HasPermission(player, p => p.RequestInvite, senderName))
            return;
        
        OnLogMessage?.Invoke($"📡 Inviting {senderName} to party");
        OnSendCommand?.Invoke($"invite {senderName}");
    }
    
    private void HandleExecuteCommand(string senderName, PlayerData? player, string commandToExecute)
    {
        if (!HasPermission(player, p => p.ExecuteCommands, senderName))
            return;
        
        if (string.IsNullOrWhiteSpace(commandToExecute))
        {
            SendResponse(senderName, "No command specified.");
            return;
        }
        
        OnLogMessage?.Invoke($"📡 Executing command from {senderName}: {commandToExecute}");
        OnSendCommand?.Invoke(commandToExecute);
        SendResponse(senderName, "Ok");
    }
    
    private void HandleResetExpTracker(string senderName, PlayerData? player)
    {
        if (!HasPermission(player, p => p.ExecuteCommands, senderName))
            return;
        
        OnLogMessage?.Invoke($"📡 Resetting experience tracker per request from {senderName}");
        _resetExpTracker();
        SendResponse(senderName, "Ok");
    }
    
    private void HandleAlterSettings(string senderName, PlayerData? player, string command, string args)
    {
        if (!HasPermission(player, p => p.AlterSettings, senderName))
            return;
        
        // Parse optional on/off argument
        bool? requestedState = null;
        if (!string.IsNullOrWhiteSpace(args))
        {
            var argLower = args.ToLower().Trim();
            if (argLower == "on")
                requestedState = true;
            else if (argLower == "off")
                requestedState = false;
        }
        
        switch (command)
        {
            case "@auto-all":
                HandleAutoAll(senderName, requestedState);
                break;
                
            case "@auto-combat":
                HandleAutoToggle(senderName, "auto-combat", _getCombatEnabled, _setCombatEnabled, requestedState);
                break;
                
            case "@auto-bless":
                HandleAutoToggle(senderName, "auto-bless", _getBuffEnabled, _setBuffEnabled, requestedState);
                break;
                
            case "@auto-cure":
                HandleAutoToggle(senderName, "auto-cure", _getCureEnabled, _setCureEnabled, requestedState);
                break;
                
            case "@auto-heal":
                HandleAutoToggle(senderName, "auto-heal", _getHealEnabled, _setHealEnabled, requestedState);
                break;
        }
    }
    
    private void HandleAutoAll(string senderName, bool? requestedState)
    {
        // Determine the new state
        bool newState;
        if (requestedState.HasValue)
        {
            newState = requestedState.Value;
        }
        else
        {
            // Toggle: if ANY are on, turn all off; otherwise turn all on
            bool anyOn = _getCombatEnabled() || _getHealEnabled() || _getCureEnabled() || _getBuffEnabled();
            newState = !anyOn;
        }
        
        // Set all automation states
        _setCombatEnabled(newState);
        _setHealEnabled(newState);
        _setCureEnabled(newState);
        _setBuffEnabled(newState);
        
        var stateStr = newState ? "ON" : "OFF";
        OnLogMessage?.Invoke($"📡 All automation set to {stateStr} by {senderName}");
        SendResponse(senderName, $"auto-all now {stateStr}");
        
        // Notify UI to update
        OnAutomationStateChanged?.Invoke();
    }
    
    private void HandleAutoToggle(string senderName, string settingName, Func<bool> getter, Action<bool> setter, bool? requestedState)
    {
        bool newState = requestedState ?? !getter();
        setter(newState);
        
        var stateStr = newState ? "ON" : "OFF";
        OnLogMessage?.Invoke($"📡 {settingName} set to {stateStr} by {senderName}");
        SendResponse(senderName, $"{settingName} now {stateStr}");
        
        // Notify UI to update
        OnAutomationStateChanged?.Invoke();
    }
    
    private void HandleQueryHealth(string senderName, PlayerData? player)
    {
        // Allow @health if the player has the permission OR if they are a current party member
        bool isPartyMember = _isPartyMember != null && _isPartyMember(senderName);
        if (!isPartyMember && !HasPermission(player, p => p.QueryHealth, senderName))
            return;
        
        var hp = _getCurrentHp();
        var maxHp = _getMaxHp();
        var mana = _getCurrentMana();
        var maxMana = _getMaxMana();
        var manaType = _getManaType();
        
        var response = $"HP={hp}/{maxHp},{manaType}={mana}/{maxMana}";
        OnLogMessage?.Invoke($"📡 Sending health to {senderName}: {response}");
        SendResponse(senderName, response);
    }
    
    private void HandleQueryInventory(string senderName, PlayerData? player)
    {
        if (!HasPermission(player, p => p.QueryInventory, senderName))
            return;
        
        SendResponse(senderName, "Not yet implemented");
    }
    
    private void HandleMovePlayer(string senderName, PlayerData? player, string destination)
    {
        if (!HasPermission(player, p => p.MovePlayer, senderName))
            return;
        
        SendResponse(senderName, "Not yet implemented");
    }
    
    private void HandleHangup(string senderName, PlayerData? player)
    {
        if (!HasPermission(player, p => p.HangupDisconnect, senderName))
            return;
        
        OnLogMessage?.Invoke($"📡 Hangup requested by {senderName}");
        OnHangupRequested?.Invoke();
    }
    
    private void HandleRelog(string senderName, PlayerData? player)
    {
        if (!HasPermission(player, p => p.HangupDisconnect, senderName))
            return;
        
        OnLogMessage?.Invoke($"📡 Relog requested by {senderName}");
        OnRelogRequested?.Invoke();
    }
    
    private void HandleDivertConversations(string senderName, PlayerData? player)
    {
        if (!HasPermission(player, p => p.DivertConversations, senderName))
            return;
        
        SendResponse(senderName, "Not yet implemented");
    }
    
    #endregion
    
    #region Response Sending
    
    /// <summary>
    /// Send a response to a player using the same communication method they used
    /// </summary>
    private void SendResponse(string playerName, string message)
    {
        string command = _currentMethod switch
        {
            CommunicationMethod.Telepath => $"/{playerName} {{{message}}}",
            CommunicationMethod.Gangpath => $"gb {{{message}}}",
            CommunicationMethod.Say => $">{playerName} {{{message}}}",
            _ => $"/{playerName} {{{message}}}"
        };
        
        OnSendCommand?.Invoke(command);
    }
    
    #endregion
}
