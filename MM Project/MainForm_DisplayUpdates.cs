namespace MudProxyViewer;

// UI display update methods
public partial class MainForm
{
    // This file contains methods that update the UI display
    // Extracted from MainForm.cs for better organization
    
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

    private void RefreshBuffDisplay()
    {
        RefreshSelfBuffs();
        RefreshPartyDisplay();
    }

    private void RefreshPartyDisplay()
    {
        var partyMembers = _buffManager.PartyManager.PartyMembers
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

    private void UpdateToggleButtonStates()
    {
        var enabledColor = Color.FromArgb(70, 130, 180);  // Blue when enabled
        var disabledColor = Color.FromArgb(60, 60, 60);   // Gray when disabled
        var pausedColor = Color.FromArgb(180, 100, 50);   // Orange when paused
        
        // Auto-all button: blue when all automation is on, grey with play icon when off
        bool anyAutomationOn = _buffManager.CombatManager.CombatEnabled ||
                               _buffManager.HealingManager.HealingEnabled ||
                               _buffManager.AutoRecastEnabled ||
                               _buffManager.CureManager.CuringEnabled;
        if (anyAutomationOn)
        {
            _pauseButton.BackColor = enabledColor;
            _pauseButton.Text = "||";  // Pause icon to indicate "click to turn all off"
        }
        else
        {
            _pauseButton.BackColor = disabledColor;
            _pauseButton.Text = ">";  // Play icon to indicate "click to turn all on"
        }
        
        _combatToggleButton.BackColor = _buffManager.CombatManager.CombatEnabled ? enabledColor : disabledColor;
        _healToggleButton.BackColor = _buffManager.HealingManager.HealingEnabled ? enabledColor : disabledColor;
        _buffToggleButton.BackColor = _buffManager.AutoRecastEnabled ? enabledColor : disabledColor;
        _cureToggleButton.BackColor = _buffManager.CureManager.CuringEnabled ? enabledColor : disabledColor;
    }

    private void RefreshPlayerInfo()
    {
        var info = _buffManager.PlayerStateManager.PlayerInfo;
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
        var info = _buffManager.PlayerStateManager.PlayerInfo;
        var tracker = _buffManager.PlayerStateManager.ExperienceTracker;
        
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

    private void RefreshSelfBuffs()
    {
        var selfBuffs = _buffManager.GetSelfBuffs()
            .Select(b => new BuffDisplayInfo(b))
            .ToList();
        
        _selfStatusPanel.UpdateBuffs(selfBuffs);
        
        // Reposition party section based on self panel's new height
        RepositionPartySection();
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

    /// <summary>
    /// Refresh automation button states (called when remote commands change automation)
    /// </summary>
    private void RefreshAutomationButtons()
    {
        UpdateToggleButtonStates();
    }

    private void UpdateConnectionUI(bool connected)
    {
        _connectButton.Text = connected ? "Disconnect" : "Connect";
        _connectButton.BackColor = connected ? Color.FromArgb(180, 0, 0) : Color.FromArgb(0, 120, 0);
    }

    private void UpdateTitle()
    {
        var title = "MUD Proxy Viewer";
        if (!string.IsNullOrEmpty(_buffManager.PlayerStateManager.PlayerInfo.Name))
        {
            title += $" - {_buffManager.PlayerStateManager.PlayerInfo.Name}";
        }
        if (!string.IsNullOrEmpty(_buffManager.CurrentProfilePath))
        {
            title += $" [{Path.GetFileName(_buffManager.CurrentProfilePath)}]";
        }
        this.Text = title;
    }
}