using System.Text.RegularExpressions;

namespace MudProxyViewer;

/// <summary>
/// Manages party membership, party member tracking, health requests, and party-related automation.
/// Extracted from BuffManager for better separation of concerns.
/// </summary>
public class PartyManager
{
    private readonly List<PartyMember> _partyMembers = new();
    
    // Dependencies (injected via delegates following established pattern)
    private readonly Func<bool> _shouldPauseCommands;
    private readonly Func<bool> _isAutomationEnabled;
    private readonly Func<string, bool> _isTargetSelf;
    private readonly Func<PlayerDatabaseManager> _getPlayerDb;
    private readonly Action<string> _sendCommand;
    private readonly Action<string> _logMessage;
    
    // Party state
    private bool _isInParty = false;
    private bool _isPartyLeader = false;
    private bool _requestHealthAfterPartyUpdate = false;
    
    // Par command settings (persisted via character profile)
    private bool _parAutoEnabled = false;
    private int _parFrequencySeconds = 15;
    private bool _parAfterCombatTick = false;
    private DateTime _lastParSent = DateTime.MinValue;
    
    // Health request settings (persisted via character profile)
    private bool _healthRequestEnabled = false;
    private int _healthRequestIntervalSeconds = 60;
    private DateTime _lastHealthRequestCheck = DateTime.MinValue;

    // Auto-invite cooldown — prevents spamming invite/@join on rapid room scans
    private readonly Dictionary<string, DateTime> _recentlyInvited = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan InviteCooldown = TimeSpan.FromSeconds(30);

    // Auto-invite wait state — pauses walking while waiting for invited players to join
    private readonly HashSet<string> _pendingInviteJoins = new(StringComparer.OrdinalIgnoreCase);
    private System.Timers.Timer? _inviteWaitTimer;

    // Auto-invite wait setting (persisted via character profile)
    private int _autoInviteWaitSeconds = 15;

    // Party wait system state
    private string _partyLeaderName = string.Empty;
    private readonly HashSet<string> _waitingMembers = new(StringComparer.OrdinalIgnoreCase);
    private System.Timers.Timer? _waitTimeoutTimer;
    private bool _followerWaitSent = false;

    // Party wait system settings (persisted via character profile)
    private bool _ignorePartyWait = false;
    private int _partyWaitHealthThreshold = 0;
    private int _partyWaitTimeoutMinutes = 2;

    // Events
    public event Action? OnPartyChanged;
    public event Action<IEnumerable<string>>? OnPartyMembersRemoved;  // Names of removed members
    public event Action<IReadOnlyList<PartyMember>>? OnPartyUpdated;  // Full party list after update
    public event Action? OnBecameFollower;  // Fires when we join another player's party as follower
    
    // Regex patterns for party detection
    private static readonly Regex PartyMemberRegex = new(
        @"^\s{2}(\S.*?)\s+\((\w+)\)\s+(?:\[M:\s*(\d+)%\])?\s*\[H:\s*(\d+)%\]\s*([RPM]?)\s*-\s*(\w+)",
        RegexOptions.Compiled | RegexOptions.Multiline);
    
    // Party membership detection
    private static readonly Regex StartedFollowingRegex = new(
        @"You are now following (\w+)\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SomeoneFollowingYouRegex = new(
        @"(\w+) started to follow you\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SomeoneLeftPartyRegex = new(
        @"(\w+) is no longer following you\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SomeoneRemovedFromPartyRegex = new(
        @"(\w+) has been removed from your followers\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex YouLeftPartyRegex = new(
        @"You are no longer following (\w+)\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PartyDisbandedRegex = new(
        @"Your party has been disbanded\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    // Party invitation
    private static readonly Regex PartyInviteRegex = new(
        @"(\w+)\s+has invited you to follow (?:him|her)\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    // Telepath HP update
    private static readonly Regex TelepathHpRegex = new(
        @"(\w+)\s+telepaths:\s*\{HP=(\d+)/(\d+)(?:,(MA|KAI)=(\d+)/(\d+))?(?:,\s*(?:Resting|Poisoned|Losing HPs))*\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    public PartyManager(
        Func<bool> shouldPauseCommands,
        Func<bool> isAutomationEnabled,
        Func<string, bool> isTargetSelf,
        Func<PlayerDatabaseManager> getPlayerDb,
        Action<string> sendCommand,
        Action<string> logMessage)
    {
        _shouldPauseCommands = shouldPauseCommands;
        _isAutomationEnabled = isAutomationEnabled;
        _isTargetSelf = isTargetSelf;
        _getPlayerDb = getPlayerDb;
        _sendCommand = sendCommand;
        _logMessage = logMessage;
    }
    
    #region Properties
    
    public IReadOnlyList<PartyMember> PartyMembers => _partyMembers.AsReadOnly();
    public bool IsInParty => _isInParty;
    
    public bool ParAutoEnabled
    {
        get => _parAutoEnabled;
        set => _parAutoEnabled = value;
    }
    
    public int ParFrequencySeconds
    {
        get => _parFrequencySeconds;
        set => _parFrequencySeconds = Math.Clamp(value, 5, 300);
    }
    
    public bool ParAfterCombatTick
    {
        get => _parAfterCombatTick;
        set => _parAfterCombatTick = value;
    }
    
    public bool HealthRequestEnabled
    {
        get => _healthRequestEnabled;
        set => _healthRequestEnabled = value;
    }
    
    public int HealthRequestIntervalSeconds
    {
        get => _healthRequestIntervalSeconds;
        set => _healthRequestIntervalSeconds = Math.Clamp(value, 15, 300);
    }

    public bool IgnorePartyWait
    {
        get => _ignorePartyWait;
        set => _ignorePartyWait = value;
    }

    public int PartyWaitHealthThreshold
    {
        get => _partyWaitHealthThreshold;
        set => _partyWaitHealthThreshold = Math.Clamp(value, 0, 100);
    }

    public int PartyWaitTimeoutMinutes
    {
        get => _partyWaitTimeoutMinutes;
        set => _partyWaitTimeoutMinutes = Math.Clamp(value, 0, 60);
    }

    public int AutoInviteWaitSeconds
    {
        get => _autoInviteWaitSeconds;
        set => _autoInviteWaitSeconds = Math.Clamp(value, 0, 60);  // 0 = disabled
    }

    /// <summary>
    /// Returns true when walking should be paused because we auto-invited
    /// a player and are waiting for them to join.
    /// </summary>
    public bool ShouldPauseForInviteWait => _pendingInviteJoins.Count > 0;

    public bool IsPartyLeader => _isPartyLeader;

    /// <summary>True when in a party but NOT the leader (following someone).</summary>
    public bool IsFollower => _isInParty && !_isPartyLeader;

    /// <summary>Name of the party leader we are following. Empty when solo or leading.</summary>
    public string PartyLeaderName => _partyLeaderName;

    /// <summary>
    /// Returns true when walking should be paused because we're the leader
    /// and party members have requested a wait, OR because a member's health
    /// is below the configured threshold.
    /// </summary>
    public bool ShouldPauseForPartyWait
    {
        get
        {
            if (!_isInParty || !_isPartyLeader) return false;

            // Check explicit @Wait requests
            if (_waitingMembers.Count > 0) return true;

            // Check health threshold
            if (_partyWaitHealthThreshold > 0)
            {
                foreach (var m in _partyMembers)
                {
                    if (m.EffectiveHealthPercent < _partyWaitHealthThreshold)
                        return true;
                }
            }

            return false;
        }
    }

    #endregion
    
    #region Message Processing
    
    /// <summary>
    /// Process incoming messages for party-related content.
    /// Called by BuffManager.ProcessMessage().
    /// </summary>
    public void ProcessMessage(string message)
    {
        // Check for party invitations from other players
        CheckPartyInvitation(message);
        
        // Check for party membership changes
        CheckPartyMembershipChanges(message);
        
        // Check for party command output
        if (message.Contains("You are not in a party"))
        {
            _isInParty = false;
            _isPartyLeader = false;
            if (_partyMembers.Count > 0)
            {
                _partyMembers.Clear();
                _logMessage("👤 Not in a party");
                OnPartyChanged?.Invoke();
            }
            return;
        }
        
        if (message.Contains("following people are in your travel party"))
        {
            try
            {
                _isInParty = true;
                ParsePartyOutput(message);
            }
            catch (Exception ex)
            {
                _logMessage($"⚠️ Error parsing party output: {ex.Message}");
            }
        }
        
        // Check for telepath HP updates from party members
        var telepathMatch = TelepathHpRegex.Match(message);
        if (telepathMatch.Success)
        {
            _logMessage($"📡 Telepath match found: {telepathMatch.Value}");
            UpdatePartyMemberHpFromTelepath(telepathMatch);
        }
        else if (message.Contains("telepaths:") && message.Contains("{HP="))
        {
            // Debug: telepath message found but regex didn't match
            _logMessage($"⚠️ Telepath message not parsed: {message}");
        }
    }
    
    #endregion
    
    #region Party Output Parsing
    
    private void ParsePartyOutput(string message)
    {
        // Remember previous members and their telepath data
        var previousMembers = _partyMembers
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key, 
                g => g.First(), 
                StringComparer.OrdinalIgnoreCase);
        
        _partyMembers.Clear();
        
        // Track names we've already added to prevent duplicates
        var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        var matches = PartyMemberRegex.Matches(message);
        foreach (Match match in matches)
        {
            var fullName = match.Groups[1].Value.Trim();
            var firstName = fullName.Split(' ')[0];
            
            // Skip duplicates
            if (addedNames.Contains(firstName))
            {
                _logMessage($"⚠️ Skipping duplicate party member: {firstName}");
                continue;
            }
            addedNames.Add(firstName);
            
            var indicator = match.Groups[5].Value;
            var isResting = indicator == "R";
            var isPoisoned = indicator == "P";
            var isMeditating = indicator == "M";
            
            var member = new PartyMember
            {
                Name = firstName,
                FullName = fullName,
                Class = match.Groups[2].Value,
                ManaPercent = match.Groups[3].Success && int.TryParse(match.Groups[3].Value, out int m) ? m : 0,
                HealthPercent = int.TryParse(match.Groups[4].Value, out int h) ? h : 0,
                IsResting = isResting,
                IsPoisoned = isPoisoned,
                IsMeditating = isMeditating,
                Rank = match.Groups[6].Value
            };
            
            // Preserve telepath data if we had it before
            if (previousMembers.TryGetValue(firstName, out var prevMember))
            {
                member.MaxHp = prevMember.MaxHp;
                member.MaxMana = prevMember.MaxMana;
                member.ResourceType = prevMember.ResourceType;
                member.LastTelepathUpdate = prevMember.LastTelepathUpdate;
                
                if (prevMember.MaxHp > 0)
                {
                    member.CurrentHp = (member.HealthPercent * prevMember.MaxHp) / 100;
                }
                if (prevMember.MaxMana > 0)
                {
                    member.CurrentMana = (member.ManaPercent * prevMember.MaxMana) / 100;
                }
            }
            
            _partyMembers.Add(member);
            
            var restingIndicator = isResting ? " 💤" : "";
            var poisonIndicator = isPoisoned ? " ☠️" : "";
            var meditatingIndicator = isMeditating ? " 🧘" : "";
            var manaDisplay = member.ManaPercent > 0 ? $" M:{member.ManaPercent}%" : "";
            _logMessage($"  👤 {member.Name} ({member.Class}) H:{member.HealthPercent}%{manaDisplay}{restingIndicator}{poisonIndicator}{meditatingIndicator} - {member.Rank}");
        }

        // Check for [Invited] members and send @join command to them
        // Only the leader should send @join - non-leaders can't invite
        if (_isPartyLeader)
        {
            var invitedRegex = new Regex(@"^\s{2}(\S.*?)\s+\((\w+)\)\s+\[Invited\]", RegexOptions.Compiled | RegexOptions.Multiline);
            var invitedMatches = invitedRegex.Matches(message);
            foreach (Match invitedMatch in invitedMatches)
            {
                var invitedFullName = invitedMatch.Groups[1].Value.Trim();
                var invitedFirstName = invitedFullName.Split(' ')[0];
                if (!_isTargetSelf(invitedFirstName))
                {
                    _logMessage($"👥 Sending @join to invited member: {invitedFirstName}");
                    _sendCommand($"/{invitedFirstName} @join");
                }
            }
        }
        
        _logMessage($"👥 Party updated: {_partyMembers.Count} members detected");
        
        // Find members who left the party
        var currentMembers = _partyMembers.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removedMembers = previousMembers.Keys
            .Where(name => !currentMembers.Contains(name) && !_isTargetSelf(name))
            .ToList();
        
        // Notify about removed members (BuffManager will clear buffs/ailments)
        if (removedMembers.Count > 0)
        {
            OnPartyMembersRemoved?.Invoke(removedMembers);
        }
        
        OnPartyChanged?.Invoke();
        
        // Notify about full party update (for poison status sync)
        OnPartyUpdated?.Invoke(_partyMembers.AsReadOnly());
        
        // If we just joined a party, request health from all members
        if (_requestHealthAfterPartyUpdate)
        {
            _requestHealthAfterPartyUpdate = false;
            RequestPartyMemberHealth();
        }
    }
    
    #endregion
    
    #region Party Membership Changes
    
    /// <summary>
    /// Check if message contains a party invitation and auto-join if player has that option enabled.
    /// Party-aware: declines with explanation if already in a party.
    /// </summary>
    private void CheckPartyInvitation(string message)
    {
        var match = PartyInviteRegex.Match(message);
        if (!match.Success)
            return;

        var inviterName = match.Groups[1].Value;
        _logMessage($"👥 Party invitation received from {inviterName}");

        // If we're already a follower, decline with explanation
        if (_isInParty && !_isPartyLeader)
        {
            _logMessage($"👥 Declining invite from {inviterName} (following {_partyLeaderName})");
            _sendCommand($"/{inviterName} {{Unable to join, following {_partyLeaderName}}}");
            return;
        }

        // If we're already a leader, decline with explanation
        if (_isInParty && _isPartyLeader)
        {
            _logMessage($"👥 Declining invite from {inviterName} (leading a party)");
            _sendCommand($"/{inviterName} {{Unable to join, I'm leading a party}}");
            return;
        }

        // Not in a party — check if auto-join is enabled for this player
        var playerDb = _getPlayerDb();
        var player = playerDb.GetPlayer(inviterName);
        if (player != null && player.JoinPartyIfInvited)
        {
            _logMessage($"👥 Auto-joining {inviterName}'s party");
            _sendCommand($"join {inviterName}");
        }
    }
    
    /// <summary>
    /// Check for party join/leave messages and update _isInParty state accordingly.
    /// Also sends 'par' command when party membership changes to refresh party list.
    /// </summary>
    private void CheckPartyMembershipChanges(string message)
    {
        // Check if we started following someone (we joined a party)
        if (StartedFollowingRegex.IsMatch(message))
        {
            var match = StartedFollowingRegex.Match(message);
            var leaderName = match.Groups[1].Value;
            _isInParty = true;
            _isPartyLeader = false;
            _partyLeaderName = leaderName;
            _requestHealthAfterPartyUpdate = true;
            _logMessage($"👥 Joined party - now following {leaderName}");
            if (_parAutoEnabled) _sendCommand("par");
            OnBecameFollower?.Invoke();
            return;
        }

        // Check if someone started following us (they joined our party)
        if (SomeoneFollowingYouRegex.IsMatch(message))
        {
            var match = SomeoneFollowingYouRegex.Match(message);
            var followerName = match.Groups[1].Value;
            var wasInParty = _isInParty;
            _isInParty = true;
            _isPartyLeader = true;

            if (!wasInParty)
            {
                _logMessage($"👥 Party formed - {followerName} is now following you");
            }
            else
            {
                _logMessage($"👥 {followerName} joined the party");
            }
            if (_parAutoEnabled) _sendCommand("par");

            // Request health from the new party member immediately
            RequestHealthFromPlayer(followerName);

            // Clear invite-wait pause if this player was pending
            if (_pendingInviteJoins.Remove(followerName))
            {
                _logMessage($"▶️ {followerName} joined — invite wait cleared");
                if (_pendingInviteJoins.Count == 0) StopInviteWaitTimeout();
            }
            return;
        }

        // Check disbanded FIRST — when disbanding, the server sends individual
        // "X is no longer following you." lines AND "Your party has been disbanded."
        // in the same text block. Disbanded is comprehensive and supersedes individual leaves.
        if (PartyDisbandedRegex.IsMatch(message))
        {
            _isInParty = false;
            _isPartyLeader = false;
            _partyLeaderName = string.Empty;
            _followerWaitSent = false;
            _waitingMembers.Clear();
            StopWaitTimeout();
            _pendingInviteJoins.Clear();
            StopInviteWaitTimeout();
            _partyMembers.Clear();
            OnPartyChanged?.Invoke();
            _logMessage("👤 Party disbanded - now solo");
            return;
        }

        // Check if someone left our party — use Matches() to handle multiple
        // leaves in the same text block (e.g., multiple followers leaving at once)
        var leftMatches = SomeoneLeftPartyRegex.Matches(message);
        if (leftMatches.Count > 0)
        {
            var removedNames = new List<string>();
            foreach (System.Text.RegularExpressions.Match match in leftMatches)
            {
                var followerName = match.Groups[1].Value;
                _logMessage($"👥 {followerName} left the party");
                _partyMembers.RemoveAll(m => m.Name.Equals(followerName, StringComparison.OrdinalIgnoreCase));
                _waitingMembers.Remove(followerName);
                removedNames.Add(followerName);
            }
            if (_waitingMembers.Count == 0) StopWaitTimeout();
            if (_partyMembers.Count == 0)
            {
                _isInParty = false;
                _isPartyLeader = false;
            }
            OnPartyMembersRemoved?.Invoke(removedNames);
            OnPartyChanged?.Invoke();
            return;
        }

        // Check if we kicked someone from our party — use Matches() for multiple removals
        var removedMatches = SomeoneRemovedFromPartyRegex.Matches(message);
        if (removedMatches.Count > 0)
        {
            var removedNames = new List<string>();
            foreach (System.Text.RegularExpressions.Match match in removedMatches)
            {
                var followerName = match.Groups[1].Value;
                _logMessage($"👥 {followerName} was removed from the party");
                _partyMembers.RemoveAll(m => m.Name.Equals(followerName, StringComparison.OrdinalIgnoreCase));
                _waitingMembers.Remove(followerName);
                removedNames.Add(followerName);
            }
            if (_waitingMembers.Count == 0) StopWaitTimeout();
            if (_partyMembers.Count == 0)
            {
                _isInParty = false;
                _isPartyLeader = false;
            }
            OnPartyMembersRemoved?.Invoke(removedNames);
            OnPartyChanged?.Invoke();
            return;
        }

        // Check if we left someone else's party (now solo)
        if (YouLeftPartyRegex.IsMatch(message))
        {
            var match = YouLeftPartyRegex.Match(message);
            var leaderName = match.Groups[1].Value;
            _isInParty = false;
            _isPartyLeader = false;
            _partyLeaderName = string.Empty;
            _followerWaitSent = false;
            _partyMembers.Clear();
            OnPartyChanged?.Invoke();
            _logMessage($"👤 Left {leaderName}'s party - now solo");
            return;
        }

        // Legacy detection
        if (message.Contains("You have invited") && message.Contains("to follow you"))
        {
            _isPartyLeader = true;
            if (_parAutoEnabled) _sendCommand("par");
        }
    }
    
    #endregion
    
    #region Auto-Invite / Auto-Join

    /// <summary>
    /// Attempt to join a player's party via @join remote command.
    /// Returns null on success (join command sent), or an error message string
    /// to relay back to the requester. Does NOT check JoinPartyIfInvited —
    /// that flag is for MUD invite messages only. Permission checking
    /// (ExecuteCommands) is the caller's responsibility.
    /// </summary>
    public string? TryJoinParty(string senderName)
    {
        if (_isInParty && !_isPartyLeader)
        {
            _logMessage($"👥 Cannot join {senderName}'s party (already following {_partyLeaderName})");
            return $"Unable to join, following {_partyLeaderName}";
        }

        if (_isPartyLeader)
        {
            _logMessage($"👥 Cannot join {senderName}'s party (we are a party leader)");
            return "Unable to join, I'm leading a party";
        }

        _logMessage($"👥 Joining {senderName}'s party (via @join)");
        _sendCommand($"join {senderName}");
        return null;
    }

    /// <summary>
    /// Check "Also here:" content for players that should be auto-invited.
    /// Called from CombatManager when it parses room contents.
    /// </summary>
    public void CheckAutoInvitePlayers(IEnumerable<string> playersInRoom)
    {
        // Followers don't invite — only leaders or solo players can
        if (_isInParty && !_isPartyLeader)
            return;

        var playerDb = _getPlayerDb();
        
        // Clean up expired cooldowns
        var now = DateTime.Now;
        var expired = _recentlyInvited.Where(kvp => now - kvp.Value > InviteCooldown).Select(kvp => kvp.Key).ToList();
        foreach (var key in expired) _recentlyInvited.Remove(key);

        foreach (var playerName in playersInRoom)
        {
            // Extract first name
            var firstName = playerName.Split(' ')[0].Trim();
            if (firstName.Contains("("))
                firstName = firstName.Split('(')[0].Trim();

            var player = playerDb.GetPlayer(firstName);
            if (player != null && player.InviteToPartyIfSeen)
            {
                // Check if already in party
                var inParty = _partyMembers.Any(m =>
                    m.Name.StartsWith(firstName, StringComparison.OrdinalIgnoreCase));

                // Check invite cooldown — prevents spam on rapid room scans
                var onCooldown = _recentlyInvited.ContainsKey(firstName);

                if (!inParty && !onCooldown)
                {
                    _logMessage($"👥 Auto-inviting {firstName} to party (seen in room)");
                    _sendCommand($"invite {firstName}");
                    _sendCommand($"/{firstName} @join");
                    _recentlyInvited[firstName] = now;

                    // Track pending join for invite-wait pause (pauses walking until they join or timeout)
                    if (_autoInviteWaitSeconds > 0)
                    {
                        _pendingInviteJoins.Add(firstName);
                        StartInviteWaitTimeoutIfNeeded();
                    }
                }
            }
        }
    }
    
    #endregion
    
    #region Par Command
    
    /// <summary>
    /// Check if it's time to send a 'par' command and send it if so.
    /// Call this periodically (e.g., every second).
    /// </summary>
    public void CheckParCommand()
    {
        if (!_parAutoEnabled) return;
        if (!_isInParty) return;
        if (_shouldPauseCommands()) return;
        if (!_isAutomationEnabled()) return;

        var timeSinceLastPar = (DateTime.Now - _lastParSent).TotalSeconds;
        if (timeSinceLastPar >= _parFrequencySeconds)
        {
            SendParCommand();
        }
    }
    
    /// <summary>
    /// Called when a combat tick is detected. Can trigger par command if configured.
    /// </summary>
    public void OnCombatTick()
    {
        if (_shouldPauseCommands()) return;
        if (!_isAutomationEnabled()) return;

        // Send par after combat tick if configured (only if in a party)
        if (_parAfterCombatTick && _parAutoEnabled && _isInParty)
        {
            var timeSinceLastPar = (DateTime.Now - _lastParSent).TotalSeconds;
            if (timeSinceLastPar >= 2.0)
            {
                SendParCommand();
            }
        }
    }
    
    private void SendParCommand()
    {
        _lastParSent = DateTime.Now;
        _sendCommand("par");
        _logMessage("📋 Auto-sending 'par' command");
    }
    
    #endregion
    
    #region Health Requests
    
    /// <summary>
    /// Check if any party members are missing health data and request it.
    /// Call this periodically (e.g., every second).
    /// </summary>
    public void CheckHealthRequests()
    {
        if (!_healthRequestEnabled) return;
        if (_shouldPauseCommands()) return;
        
        var timeSinceLastCheck = (DateTime.Now - _lastHealthRequestCheck).TotalSeconds;
        if (timeSinceLastCheck < _healthRequestIntervalSeconds) return;
        
        _lastHealthRequestCheck = DateTime.Now;
        
        // Find party members without actual HP data
        foreach (var member in _partyMembers)
        {
            if (_isTargetSelf(member.Name)) continue;
            
            // If we don't have actual HP data (MaxHp is 0), request it
            if (member.MaxHp == 0)
            {
                var command = $"/{member.Name} @health";
                _sendCommand(command);
                _logMessage($"📡 Requesting health from {member.Name}");
                
                // Only request from one member per interval to avoid spam
                return;
            }
        }
    }
    
    /// <summary>
    /// Request health data from a specific player immediately.
    /// </summary>
    public void RequestHealthFromPlayer(string playerName)
    {
        if (_shouldPauseCommands()) return;
        if (_isTargetSelf(playerName)) return;
        
        var command = $"/{playerName} @health";
        _sendCommand(command);
        _logMessage($"📡 Requesting health from {playerName}");
    }
    
    /// <summary>
    /// Request health data from all current party members.
    /// </summary>
    public void RequestHealthFromAllPartyMembers()
    {
        if (_shouldPauseCommands()) return;
        
        foreach (var member in _partyMembers)
        {
            if (_isTargetSelf(member.Name)) continue;
            
            var command = $"/{member.Name} @health";
            _sendCommand(command);
        }
        
        if (_partyMembers.Count(m => !_isTargetSelf(m.Name)) > 0)
        {
            _logMessage($"📡 Requesting health from all party members");
        }
    }
    
    private void RequestPartyMemberHealth()
    {
        foreach (var member in _partyMembers)
        {
            if (!_isTargetSelf(member.Name))
            {
                RequestHealthFromPlayer(member.Name);
            }
        }
    }
    
    #endregion
    
    #region Telepath Updates
    
    private void UpdatePartyMemberHpFromTelepath(Match match)
    {
        var name = match.Groups[1].Value;
        var currentHp = int.Parse(match.Groups[2].Value);
        var maxHp = int.Parse(match.Groups[3].Value);
        
        int currentMana = 0;
        int maxMana = 0;
        string resourceType = "";
        
        if (match.Groups[4].Success)
        {
            resourceType = match.Groups[4].Value.ToUpperInvariant();
            currentMana = int.Parse(match.Groups[5].Value);
            maxMana = int.Parse(match.Groups[6].Value);
        }
        
        var member = _partyMembers.FirstOrDefault(p => 
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        
        if (member != null)
        {
            member.CurrentHp = currentHp;
            member.MaxHp = maxHp;
            member.CurrentMana = currentMana;
            member.MaxMana = maxMana;
            member.LastTelepathUpdate = DateTime.Now;
            
            if (resourceType == "KAI")
                member.ResourceType = "Kai";
            else if (resourceType == "MA")
                member.ResourceType = "Mana";
            
            var hpPercent = maxHp > 0 ? (currentHp * 100 / maxHp) : 100;
            _logMessage($"📡 {name} HP: {currentHp}/{maxHp} ({hpPercent}%)");
            OnPartyChanged?.Invoke();
        }
    }
    
    #endregion
    
    #region State Management
    
    /// <summary>
    /// Clear all party data. Called when creating a new character profile.
    /// </summary>
    public void Clear()
    {
        _partyMembers.Clear();
        _isInParty = false;
        _isPartyLeader = false;
        _waitingMembers.Clear();
        StopWaitTimeout();
        _partyLeaderName = string.Empty;
        _followerWaitSent = false;
        OnPartyChanged?.Invoke();
    }
    
    /// <summary>
    /// Called when disconnecting from the server.
    /// </summary>
    public void OnDisconnected()
    {
        _isInParty = false;
        _isPartyLeader = false;
        _partyMembers.Clear();
        _lastParSent = DateTime.MinValue;
        _lastHealthRequestCheck = DateTime.MinValue;
        _waitingMembers.Clear();
        StopWaitTimeout();
        _partyLeaderName = string.Empty;
        _followerWaitSent = false;
        OnPartyChanged?.Invoke();
    }
    
    /// <summary>
    /// Load settings from a character profile.
    /// </summary>
    public void LoadFromProfile(bool parAutoEnabled, int parFrequencySeconds, bool parAfterCombatTick,
        bool healthRequestEnabled, int healthRequestIntervalSeconds,
        bool ignorePartyWait, int partyWaitHealthThreshold, int partyWaitTimeoutMinutes,
        int autoInviteWaitSeconds)
    {
        _parAutoEnabled = parAutoEnabled;
        _parFrequencySeconds = parFrequencySeconds;
        _parAfterCombatTick = parAfterCombatTick;
        _healthRequestEnabled = healthRequestEnabled;
        _healthRequestIntervalSeconds = healthRequestIntervalSeconds;
        _ignorePartyWait = ignorePartyWait;
        _partyWaitHealthThreshold = partyWaitHealthThreshold;
        _partyWaitTimeoutMinutes = partyWaitTimeoutMinutes;
        _autoInviteWaitSeconds = autoInviteWaitSeconds;
    }

    /// <summary>
    /// Reset settings to defaults. Called when creating a new character profile.
    /// </summary>
    public void ResetToDefaults()
    {
        _parAutoEnabled = true;
        _parFrequencySeconds = 15;
        _parAfterCombatTick = false;
        _healthRequestEnabled = true;
        _healthRequestIntervalSeconds = 60;
        _ignorePartyWait = false;
        _partyWaitHealthThreshold = 0;
        _partyWaitTimeoutMinutes = 2;
        _autoInviteWaitSeconds = 15;
    }

    #endregion

    #region Party Wait System

    /// <summary>
    /// Called when a party member sends @Wait. If we're the leader, track them
    /// as waiting and pause walking. If ignoring, respond with a message.
    /// </summary>
    public void HandleWaitCommand(string senderName)
    {
        if (!_isInParty || !_isPartyLeader) return;

        // Validate sender is a party member
        if (!_partyMembers.Any(m => m.Name.Equals(senderName, StringComparison.OrdinalIgnoreCase)))
        {
            _logMessage($"👥 Ignoring @Wait from {senderName} (not in party)");
            return;
        }

        if (_ignorePartyWait)
        {
            _logMessage($"👥 Ignoring @Wait from {senderName} (ignore enabled)");
            _sendCommand($"/{senderName} {{Currently ignoring @Wait messages.}}");
            return;
        }

        _waitingMembers.Add(senderName);
        _logMessage($"⏸️ Party wait: {senderName} requested wait ({_waitingMembers.Count} waiting)");

        // Acknowledge receipt
        _sendCommand($"/{senderName} {{Ok}}");

        // Start timeout if configured and not already running
        StartWaitTimeoutIfNeeded();
    }

    /// <summary>
    /// Called when a party member sends @Ok. Remove them from the waiting set.
    /// Walking resumes automatically when ShouldPauseForPartyWait becomes false.
    /// </summary>
    public void HandleOkCommand(string senderName)
    {
        if (!_isInParty || !_isPartyLeader) return;

        if (_waitingMembers.Remove(senderName))
        {
            _logMessage($"▶️ Party wait: {senderName} sent @Ok ({_waitingMembers.Count} still waiting)");

            if (_waitingMembers.Count == 0)
            {
                StopWaitTimeout();
                _logMessage("▶️ Party wait: all members ready, resuming");
            }
        }
    }

    /// <summary>
    /// Called when our HealthManager action changes. When we're a follower and
    /// start resting/meditating, notify the leader. When we recover, send @Ok.
    /// </summary>
    public void OnHealthActionChanged(HealthAction action)
    {
        if (!_isInParty || _isPartyLeader || string.IsNullOrEmpty(_partyLeaderName)) return;

        if ((action == HealthAction.Resting || action == HealthAction.Meditating) && !_followerWaitSent)
        {
            _followerWaitSent = true;
            _sendCommand($"/{_partyLeaderName} @Wait");
            _logMessage($"👥 Sent @Wait to leader {_partyLeaderName} ({action})");
        }
        else if (action == HealthAction.None && _followerWaitSent)
        {
            _followerWaitSent = false;
            _sendCommand($"/{_partyLeaderName} @Ok");
            _logMessage($"👥 Sent @Ok to leader {_partyLeaderName} (recovered)");
        }
    }

    private void StartWaitTimeoutIfNeeded()
    {
        if (_partyWaitTimeoutMinutes <= 0 || _waitTimeoutTimer != null) return;

        _waitTimeoutTimer = new System.Timers.Timer(_partyWaitTimeoutMinutes * 60 * 1000);
        _waitTimeoutTimer.AutoReset = false;
        _waitTimeoutTimer.Elapsed += (s, e) =>
        {
            _logMessage($"⏰ Party wait timeout ({_partyWaitTimeoutMinutes}m) expired — resuming");
            _waitingMembers.Clear();
            StopWaitTimeout();
        };
        _waitTimeoutTimer.Start();
        _logMessage($"⏰ Party wait timeout started: {_partyWaitTimeoutMinutes} minute(s)");
    }

    private void StopWaitTimeout()
    {
        _waitTimeoutTimer?.Stop();
        _waitTimeoutTimer?.Dispose();
        _waitTimeoutTimer = null;
    }

    private void StartInviteWaitTimeoutIfNeeded()
    {
        if (_autoInviteWaitSeconds <= 0 || _inviteWaitTimer != null) return;

        _inviteWaitTimer = new System.Timers.Timer(_autoInviteWaitSeconds * 1000);
        _inviteWaitTimer.AutoReset = false;
        _inviteWaitTimer.Elapsed += (s, e) =>
        {
            if (_pendingInviteJoins.Count > 0)
            {
                _logMessage($"⏰ Auto-invite wait timeout ({_autoInviteWaitSeconds}s) — resuming walk");
                _pendingInviteJoins.Clear();
            }
            StopInviteWaitTimeout();
        };
        _inviteWaitTimer.Start();
        _logMessage($"⏸️ Waiting up to {_autoInviteWaitSeconds}s for invited player(s) to join");
    }

    private void StopInviteWaitTimeout()
    {
        _inviteWaitTimer?.Stop();
        _inviteWaitTimer?.Dispose();
        _inviteWaitTimer = null;
    }

    #endregion
}
