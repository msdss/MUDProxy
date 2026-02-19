namespace MudProxyViewer;

// Menu and button event handlers
public partial class MainForm
{
    // This file contains all menu click handlers and button events
    // Extracted from MainForm.cs for better organization

    private void NewCharacter_Click(object? sender, EventArgs e)
    {
        // Warn if there are unsaved changes
        if (_gameManager.HasUnsavedChanges)
        {
            var result = MessageBox.Show(
                "You have unsaved changes. Do you want to save before creating a new character?",
                "Unsaved Changes",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Cancel)
                return;

            if (result == DialogResult.Yes)
                SaveCharacter_Click(sender, e);
        }

        _gameManager.NewCharacterProfile();
        UpdateTitle();
        LogMessage("📄 New character profile created. Configure settings and use Save As to save.", MessageType.System);
    }

    private void LoadCharacter_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Character Profile (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = "json",
            InitialDirectory = _gameManager.CharacterProfilesPath,
            Title = "Load Character Profile"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var (success, message) = _gameManager.LoadCharacterProfile(dialog.FileName);
            if (success)
            {
                ApplyWindowSettings();
                UpdateToggleButtonStates();
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
            _gameManager.ProfileManager.AutoLoadLastCharacter = item.Checked;
            LogMessage($"Auto-load last character: {(item.Checked ? "ENABLED" : "DISABLED")}", MessageType.System);
        }
    }
    
    private void DisplayBackscroll_Click(object? sender, EventArgs e)
    {
        var scrollbackLines = _screenBuffer.GetScrollbackSnapshot();
        if (scrollbackLines.Count == 0)
        {
            MessageBox.Show("No backscroll history available yet.\nText will be captured as it scrolls off the terminal.", 
                "Backscroll", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        
        using var dialog = new BackscrollDialog(scrollbackLines);
        dialog.ShowDialog(this);
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

    private void SaveCharacter_Click(object? sender, EventArgs e)
    {
        // If we have a current profile path, save to it; otherwise prompt for location
        if (!string.IsNullOrEmpty(_gameManager.CurrentProfilePath))
        {
            var (success, message) = _gameManager.SaveCharacterProfile(_gameManager.CurrentProfilePath);
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
            InitialDirectory = _gameManager.CharacterProfilesPath,
            FileName = _gameManager.GetDefaultProfileFilename(),
            Title = "Save Character Profile"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var (success, message) = _gameManager.SaveCharacterProfile(dialog.FileName);
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

    private void SaveLog_Click(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            DefaultExt = "txt",
            FileName = $"systemlog_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            File.WriteAllText(dialog.FileName, _systemLogTextBox.Text);
            LogMessage($"System log saved to: {dialog.FileName}", MessageType.System);
        }
    }

    private void ClearLog_Click(object? sender, EventArgs e)
    {
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
        var currentCharacter = _gameManager.PlayerStateManager.PlayerInfo.Name;
        using var dialog = new SettingsDialog(_gameManager, _gameManager.CombatManager, currentCharacter);
        dialog.ShowDialog(this);
        // Refresh toggle button states after settings close
        UpdateToggleButtonStates();
    }
    
    private void CombatToggleButton_Click(object? sender, EventArgs e)
    {
        _gameManager.CombatManager.CombatEnabled = !_gameManager.CombatManager.CombatEnabled;
        UpdateToggleButtonStates();
        LogMessage($"Auto-combat {(_gameManager.CombatManager.CombatEnabled ? "ENABLED" : "DISABLED")}", MessageType.System);
        
        if (_isConnected && !_isInLoginPhase)
        {
            if (_gameManager.CombatManager.CombatEnabled)
            {
                // Reset room scan so "Also here:" is not deduplicated, then refresh room
                _gameManager.CombatManager.ResetRoomScan();
            }
            else if (_inCombat)
            {
                // Send break to disengage from combat
                SendCommandToServer("break");
            }
        }
    }
    
    private void HealToggleButton_Click(object? sender, EventArgs e)
    {
        _gameManager.HealingManager.HealingEnabled = !_gameManager.HealingManager.HealingEnabled;
        UpdateToggleButtonStates();
        LogMessage($"Auto-healing {(_gameManager.HealingManager.HealingEnabled ? "ENABLED" : "DISABLED")}", MessageType.System);
    }
    
    private void BuffToggleButton_Click(object? sender, EventArgs e)
    {
        _buffManager.AutoRecastEnabled = !_buffManager.AutoRecastEnabled;
        UpdateToggleButtonStates();
        LogMessage($"Auto-buffing {(_buffManager.AutoRecastEnabled ? "ENABLED" : "DISABLED")}", MessageType.System);
    }
    
    private void CureToggleButton_Click(object? sender, EventArgs e)
    {
        _gameManager.CureManager.CuringEnabled = !_gameManager.CureManager.CuringEnabled;
        UpdateToggleButtonStates();
        LogMessage($"Auto-curing {(_gameManager.CureManager.CuringEnabled ? "ENABLED" : "DISABLED")}", MessageType.System);
    }
    
    private void PauseButton_Click(object? sender, EventArgs e)
    {
        // Determine new state: if ANY automation is on, turn all off; otherwise turn all on
        bool anyOn = _gameManager.CombatManager.CombatEnabled ||
                     _gameManager.HealingManager.HealingEnabled ||
                     _buffManager.AutoRecastEnabled ||
                     _gameManager.CureManager.CuringEnabled;
        bool newState = !anyOn;

        _gameManager.CombatManager.CombatEnabled = newState;
        _gameManager.HealingManager.HealingEnabled = newState;
        _buffManager.AutoRecastEnabled = newState;
        _gameManager.CureManager.CuringEnabled = newState;

        UpdateToggleButtonStates();
        LogMessage($"All automation {(newState ? "ENABLED" : "DISABLED")}", MessageType.System);
        
        if (_isConnected && !_isInLoginPhase)
        {
            if (newState)
            {
                // Reset room scan so "Also here:" is not deduplicated, then refresh room
                _gameManager.CombatManager.ResetRoomScan();
            }
            else if (_inCombat)
            {
                // Send break to disengage from combat
                SendCommandToServer("break");
            }
        }
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
        using var dialog = new HealingConfigDialog(_gameManager.HealingManager);
        dialog.ShowDialog(this);
    }
    
    private void ToggleHealing_Click(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
        {
            _gameManager.HealingManager.HealingEnabled = item.Checked;
            LogMessage($"Auto-healing {(item.Checked ? "ENABLED" : "DISABLED")}", MessageType.System);
        }
    }
    
    private void ConfigureCures_Click(object? sender, EventArgs e)
    {
        using var dialog = new CureConfigDialog(_gameManager.CureManager);
        dialog.ShowDialog(this);
    }
    
    private void ToggleCuring_Click(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
        {
            _gameManager.CureManager.CuringEnabled = item.Checked;
            LogMessage($"Auto-curing {(item.Checked ? "ENABLED" : "DISABLED")}", MessageType.System);
        }
    }
    
    private void ClearAilments_Click(object? sender, EventArgs e)
    {
        _gameManager.CureManager.ClearAllAilments();
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
                var json = _gameManager.HealingManager.ExportHeals();
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
                var json = _gameManager.CureManager.ExportCures();
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
                
                var (imported, skipped, message) = _gameManager.HealingManager.ImportHeals(json, replaceResult == DialogResult.Yes);
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
                
                var (imported, skipped, message) = _gameManager.CureManager.ImportCures(json, replaceResult == DialogResult.Yes);
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
            _gameManager.PartyManager.ParAutoEnabled = item.Checked;
            LogMessage($"Auto 'par' command {(item.Checked ? "ENABLED" : "DISABLED")}", MessageType.System);
        }
    }
    
    private void OpenPlayerDB_Click(object? sender, EventArgs e)
    {
        using var dialog = new PlayerDatabaseDialog(_gameManager.PlayerDatabase);
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
            _gameManager.RoomGraph.Reload();
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
        using var dialog = new MonsterDatabaseDialog(_gameManager.MonsterDatabase);
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

    private void OpenPathfindingTest_Click(object? sender, EventArgs e)
    {
        using var dialog = new PathfindingTestDialog(_gameManager.RoomGraph);
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
            Value = _gameManager.PartyManager.ParFrequencySeconds,
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
            _gameManager.PartyManager.ParFrequencySeconds = (int)numeric.Value;
            LogMessage($"Par frequency set to {_gameManager.PartyManager.ParFrequencySeconds} seconds", MessageType.System);
        }
    }
    
    private void ToggleParAfterTick_Click(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
        {
            _gameManager.PartyManager.ParAfterCombatTick = item.Checked;
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
            _gameManager.PartyManager.HealthRequestEnabled = item.Checked;
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
            Minimum = 15,
            Maximum = 300,
            Value = _gameManager.PartyManager.HealthRequestIntervalSeconds,
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
            _gameManager.PartyManager.HealthRequestIntervalSeconds = (int)numeric.Value;
            LogMessage($"Health request interval set to {_gameManager.PartyManager.HealthRequestIntervalSeconds} seconds", MessageType.System);
        }
    }

    private void ResetTickTimer_Click(object? sender, EventArgs e)
    {
        _lastTickTime = null;
        _nextTickTime = null;
        _messageRouter.SetNextTickTime(DateTime.Now.AddMilliseconds(TICK_INTERVAL_MS));
        _lastTickTimeLabel.Text = "Last Tick: Reset";
        LogMessage("Tick timer reset.", MessageType.System);
    }

    private void ManualTick_Click(object? sender, EventArgs e)
    {
        RecordTick();
        LogMessage("Manual tick marked.", MessageType.System);
    }

    private async void ConnectButton_Click(object? sender, EventArgs e)
    {
        if (_isConnected)
        {
            // Disconnect
            _telnetConnection.Disconnect();
            return;
        }

        // Get connection settings from BuffManager
        var bbsSettings = _gameManager.BbsSettings;
        
        if (string.IsNullOrEmpty(bbsSettings.Address))
        {
            MessageBox.Show("No server configured. Load a character profile first.", "No Server", 
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _serverAddress = bbsSettings.Address;
        _serverPort = bbsSettings.Port;
        
        // Update UI
        _serverAddressLabel.Text = $"{_serverAddress}:{_serverPort}";
        
        // Reset login phase tracking
        _isInLoginPhase = true;
        _triggeredLogonSequences.Clear();
        _messageRouter.ResetLoginPhase();
        
        // Clear terminal
        _screenBuffer.ClearAll();
        _terminalControl.InvalidateTerminal();
        
        // Connect using TelnetConnection
        await _telnetConnection.ConnectAsync(_serverAddress, _serverPort, bbsSettings);
    }
}