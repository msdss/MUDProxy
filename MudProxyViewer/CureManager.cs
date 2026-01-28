using System.Text.Json;
using System.Text.RegularExpressions;

namespace MudProxyViewer;

public class CureManager
{
    private CureConfiguration _config = new();
    private readonly List<ActiveAilment> _activeAilments = new();
    private readonly string _configFilePath;
    
    // References to shared state
    private readonly Func<PlayerInfo> _getPlayerInfo;
    private readonly Func<IEnumerable<PartyMember>> _getPartyMembers;
    private readonly Func<int> _getCurrentMana;
    private readonly Func<int> _getMaxMana;
    private readonly Func<int> _getManaReservePercent;
    private readonly Func<string, bool> _isTargetSelf;
    
    // Events
    public event Action<string>? OnLogMessage;
    
    // Telepath request regex: "Boost telepaths: @held"
    private static readonly Regex TelepathRequestRegex = new(
        @"(\w+)\s+telepaths:\s*(@\w+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    public CureManager(
        Func<PlayerInfo> getPlayerInfo,
        Func<IEnumerable<PartyMember>> getPartyMembers,
        Func<int> getCurrentMana,
        Func<int> getMaxMana,
        Func<int> getManaReservePercent,
        Func<string, bool> isTargetSelf)
    {
        _getPlayerInfo = getPlayerInfo;
        _getPartyMembers = getPartyMembers;
        _getCurrentMana = getCurrentMana;
        _getMaxMana = getMaxMana;
        _getManaReservePercent = getManaReservePercent;
        _isTargetSelf = isTargetSelf;
        
        _configFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MudProxyViewer",
            "cures.json");
        
        LoadConfiguration();
    }
    
    public CureConfiguration Configuration => _config;
    public IReadOnlyList<ActiveAilment> ActiveAilments => _activeAilments.AsReadOnly();
    
    public bool CuringEnabled
    {
        get => _config.CuringEnabled;
        set
        {
            _config.CuringEnabled = value;
            SaveConfiguration();
        }
    }
    
    public List<CastPriorityType> PriorityOrder
    {
        get => _config.PriorityOrder;
        set
        {
            _config.PriorityOrder = value;
            SaveConfiguration();
        }
    }
    
    #region Configuration Management
    
    public void LoadConfiguration()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                var config = JsonSerializer.Deserialize<CureConfiguration>(json);
                if (config != null)
                {
                    _config = config;
                    
                    // Migrate old priority values to new enum
                    // Old: CriticalHeals=0, Cures=1, RegularHeals=2, Buffs=3
                    // New: Heals=0, Cures=1, Buffs=2
                    MigratePriorityOrder();
                }
            }
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error loading cure configuration: {ex.Message}");
        }
    }
    
    private void MigratePriorityOrder()
    {
        var validValues = new HashSet<CastPriorityType> 
        { 
            CastPriorityType.Heals, 
            CastPriorityType.Cures, 
            CastPriorityType.Buffs 
        };
        
        // Check if migration is needed (any invalid values or duplicates)
        var currentOrder = _config.PriorityOrder;
        bool needsMigration = currentOrder.Any(p => !validValues.Contains(p)) ||
                              currentOrder.Count != 3 ||
                              currentOrder.Distinct().Count() != currentOrder.Count;
        
        if (needsMigration)
        {
            // Reset to default order
            _config.PriorityOrder = new List<CastPriorityType>
            {
                CastPriorityType.Heals,
                CastPriorityType.Cures,
                CastPriorityType.Buffs
            };
            SaveConfiguration();
            OnLogMessage?.Invoke("📋 Cast priority migrated to new format (Heals, Cures, Buffs)");
        }
    }
    
    public void SaveConfiguration()
    {
        try
        {
            var directory = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error saving cure configuration: {ex.Message}");
        }
    }
    
    public string ExportCures()
    {
        var export = new CureExport
        {
            ExportVersion = "1.0",
            ExportedAt = DateTime.Now,
            Ailments = _config.Ailments.Select(a => a.Clone()).ToList(),
            CureSpells = _config.CureSpells.Select(s => s.Clone()).ToList()
        };
        
        // Create a mapping of old ailment IDs to new ones
        var idMapping = new Dictionary<string, string>();
        foreach (var ailment in export.Ailments)
        {
            var oldId = ailment.Id;
            ailment.Id = Guid.NewGuid().ToString();
            idMapping[oldId] = ailment.Id;
        }
        
        // Update cure spell references to use new ailment IDs
        foreach (var spell in export.CureSpells)
        {
            spell.Id = Guid.NewGuid().ToString();
            if (idMapping.TryGetValue(spell.AilmentId, out var newAilmentId))
                spell.AilmentId = newAilmentId;
        }
        
        return JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
    }
    
    public (int imported, int skipped, string message) ImportCures(string json, bool replaceExisting)
    {
        try
        {
            var export = JsonSerializer.Deserialize<CureExport>(json);
            if (export == null)
                return (0, 0, "Invalid cure export file format.");
            
            int importedAilments = 0;
            int skippedAilments = 0;
            var ailmentIdMapping = new Dictionary<string, string>();
            
            // Import ailments first
            foreach (var ailment in export.Ailments ?? new List<AilmentConfiguration>())
            {
                var existing = _config.Ailments.FirstOrDefault(a => 
                    a.DisplayName.Equals(ailment.DisplayName, StringComparison.OrdinalIgnoreCase));
                
                var oldId = ailment.Id;
                
                if (existing != null)
                {
                    if (replaceExisting)
                    {
                        ailmentIdMapping[oldId] = existing.Id;
                        ailment.Id = existing.Id;
                        var index = _config.Ailments.IndexOf(existing);
                        _config.Ailments[index] = ailment;
                        importedAilments++;
                    }
                    else
                    {
                        ailmentIdMapping[oldId] = existing.Id; // Map to existing for spells
                        skippedAilments++;
                    }
                }
                else
                {
                    ailment.Id = Guid.NewGuid().ToString();
                    ailmentIdMapping[oldId] = ailment.Id;
                    _config.Ailments.Add(ailment);
                    importedAilments++;
                }
            }
            
            // Import cure spells with updated ailment references
            int importedSpells = 0;
            int skippedSpells = 0;
            
            foreach (var spell in export.CureSpells ?? new List<CureSpellConfiguration>())
            {
                if (!ailmentIdMapping.TryGetValue(spell.AilmentId, out var newAilmentId))
                    continue; // Skip spells for ailments we couldn't map
                
                var existing = _config.CureSpells.FirstOrDefault(s => 
                    s.DisplayName.Equals(spell.DisplayName, StringComparison.OrdinalIgnoreCase));
                
                if (existing != null)
                {
                    if (replaceExisting)
                    {
                        spell.Id = existing.Id;
                        spell.AilmentId = newAilmentId;
                        var index = _config.CureSpells.IndexOf(existing);
                        _config.CureSpells[index] = spell;
                        importedSpells++;
                    }
                    else
                    {
                        skippedSpells++;
                    }
                }
                else
                {
                    spell.Id = Guid.NewGuid().ToString();
                    spell.AilmentId = newAilmentId;
                    _config.CureSpells.Add(spell);
                    importedSpells++;
                }
            }
            
            if (importedAilments > 0 || importedSpells > 0)
            {
                SaveConfiguration();
            }
            
            return (importedAilments + importedSpells, skippedAilments + skippedSpells, 
                $"Imported {importedAilments} ailment(s) and {importedSpells} cure spell(s), skipped {skippedAilments + skippedSpells} duplicate(s).");
        }
        catch (Exception ex)
        {
            return (0, 0, $"Error importing cures: {ex.Message}");
        }
    }
    
    // Ailment CRUD
    public void AddAilment(AilmentConfiguration ailment)
    {
        _config.Ailments.Add(ailment);
        SaveConfiguration();
    }
    
    public void UpdateAilment(AilmentConfiguration ailment)
    {
        var index = _config.Ailments.FindIndex(a => a.Id == ailment.Id);
        if (index >= 0)
        {
            _config.Ailments[index] = ailment;
            SaveConfiguration();
        }
    }
    
    public void RemoveAilment(string id)
    {
        _config.Ailments.RemoveAll(a => a.Id == id);
        // Also remove cure spells referencing this ailment
        _config.CureSpells.RemoveAll(c => c.AilmentId == id);
        SaveConfiguration();
    }
    
    public AilmentConfiguration? GetAilment(string id)
    {
        return _config.Ailments.FirstOrDefault(a => a.Id == id);
    }
    
    public AilmentConfiguration? GetAilmentByTelepathRequest(string request)
    {
        return _config.Ailments.FirstOrDefault(a => 
            !string.IsNullOrEmpty(a.TelepathRequest) && 
            a.TelepathRequest.Equals(request, StringComparison.OrdinalIgnoreCase));
    }
    
    public AilmentConfiguration? GetAilmentByPartyIndicator(string indicator)
    {
        return _config.Ailments.FirstOrDefault(a => 
            !string.IsNullOrEmpty(a.PartyIndicator) && 
            a.PartyIndicator.Equals(indicator, StringComparison.OrdinalIgnoreCase));
    }
    
    // Cure Spell CRUD
    public void AddCureSpell(CureSpellConfiguration spell)
    {
        _config.CureSpells.Add(spell);
        SaveConfiguration();
    }
    
    public void UpdateCureSpell(CureSpellConfiguration spell)
    {
        var index = _config.CureSpells.FindIndex(c => c.Id == spell.Id);
        if (index >= 0)
        {
            _config.CureSpells[index] = spell;
            SaveConfiguration();
        }
    }
    
    public void RemoveCureSpell(string id)
    {
        _config.CureSpells.RemoveAll(c => c.Id == id);
        SaveConfiguration();
    }
    
    public CureSpellConfiguration? GetCureSpell(string id)
    {
        return _config.CureSpells.FirstOrDefault(c => c.Id == id);
    }
    
    public CureSpellConfiguration? GetCureSpellForAilment(string ailmentId)
    {
        return _config.CureSpells
            .Where(c => c.AilmentId == ailmentId)
            .OrderBy(c => c.Priority)
            .FirstOrDefault();
    }
    
    #endregion
    
    #region Message Processing
    
    public void ProcessMessage(string message, List<PartyMember> partyMembers)
    {
        // Check for telepath ailment requests
        var telepathMatch = TelepathRequestRegex.Match(message);
        if (telepathMatch.Success)
        {
            var name = telepathMatch.Groups[1].Value;
            var request = telepathMatch.Groups[2].Value;
            
            var ailment = GetAilmentByTelepathRequest(request);
            if (ailment != null)
            {
                // Find target - could be a party member or self
                string targetName = "";
                if (!_isTargetSelf(name))
                {
                    var member = partyMembers.FirstOrDefault(p => 
                        p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (member != null)
                    {
                        targetName = member.Name;
                    }
                }
                
                AddActiveAilment(ailment.Id, targetName);
                OnLogMessage?.Invoke($"🤢 {(string.IsNullOrEmpty(targetName) ? "You" : name)} has {ailment.DisplayName} (telepath request)");
            }
        }
        
        // First, check for successful cure casts - these should NOT trigger ailment detection
        bool wasCureSuccess = false;
        foreach (var cureSpell in _config.CureSpells)
        {
            // Check self cast
            if (!string.IsNullOrEmpty(cureSpell.SelfCastMessage))
            {
                var targetName = TryExtractTarget(message, cureSpell.SelfCastMessage);
                if (targetName != null)
                {
                    wasCureSuccess = true;
                    OnLogMessage?.Invoke($"🔍 Cure detected (self pattern): target='{targetName}', isTargetSelf={_isTargetSelf(targetName)}");
                    if (_isTargetSelf(targetName))
                    {
                        RemoveActiveAilment(cureSpell.AilmentId, "");
                        OnLogMessage?.Invoke($"✨ Cured self of {GetAilment(cureSpell.AilmentId)?.DisplayName ?? "ailment"}");
                    }
                    else
                    {
                        RemoveActiveAilment(cureSpell.AilmentId, targetName);
                        OnLogMessage?.Invoke($"✨ Cured {targetName} of {GetAilment(cureSpell.AilmentId)?.DisplayName ?? "ailment"}");
                    }
                }
            }
            
            // Check party cast
            if (!string.IsNullOrEmpty(cureSpell.PartyCastMessage))
            {
                var targetName = TryExtractTarget(message, cureSpell.PartyCastMessage);
                if (targetName != null)
                {
                    wasCureSuccess = true;
                    OnLogMessage?.Invoke($"🔍 Cure detected (party pattern): target='{targetName}', isTargetSelf={_isTargetSelf(targetName)}");
                    if (_isTargetSelf(targetName))
                    {
                        RemoveActiveAilment(cureSpell.AilmentId, "");
                        OnLogMessage?.Invoke($"✨ Cured self of {GetAilment(cureSpell.AilmentId)?.DisplayName ?? "ailment"}");
                    }
                    else
                    {
                        RemoveActiveAilment(cureSpell.AilmentId, targetName);
                        OnLogMessage?.Invoke($"✨ Cured {targetName} of {GetAilment(cureSpell.AilmentId)?.DisplayName ?? "ailment"}");
                    }
                }
            }
        }
        
        // Only check for self ailment detection messages if this wasn't a cure success message
        if (!wasCureSuccess)
        {
            foreach (var ailment in _config.Ailments)
            {
                foreach (var detectionMsg in ailment.DetectionMessages)
                {
                    if (!string.IsNullOrEmpty(detectionMsg) && 
                        message.Contains(detectionMsg, StringComparison.OrdinalIgnoreCase))
                    {
                        AddActiveAilment(ailment.Id, "");
                        OnLogMessage?.Invoke($"🤢 You have {ailment.DisplayName}!");
                        break;
                    }
                }
            }
        }
    }
    
    private string? TryExtractTarget(string message, string pattern)
    {
        if (!pattern.Contains("{target}"))
        {
            // No placeholder, just check if message contains the pattern
            if (message.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return ""; // Match but no target extracted
            return null;
        }
        
        var patternWithPlaceholder = pattern.Replace("{target}", "<<<TARGET>>>");
        var escapedPattern = Regex.Escape(patternWithPlaceholder);
        
        // Use greedy (.+) if {target} is at the end of pattern, non-greedy (.+?) otherwise
        // This ensures we capture full names like "Azii" instead of just "A"
        string regexPattern;
        if (pattern.TrimEnd().EndsWith("{target}"))
        {
            // Target is at end - use greedy match up to common ending punctuation or end of string
            regexPattern = escapedPattern.Replace("<<<TARGET>>>", @"(.+?)(?:[!\.\,\?\;\:]|$)");
        }
        else
        {
            // Target is in middle - use non-greedy
            regexPattern = escapedPattern.Replace("<<<TARGET>>>", "(.+?)");
        }
        
        try
        {
            var match = Regex.Match(message, regexPattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                // Trim whitespace and common punctuation from the target
                var target = match.Groups[1].Value.Trim().TrimEnd('!', '.', ',', '?', ';', ':');
                return target;
            }
        }
        catch { }
        
        return null;
    }
    
    public void UpdatePartyPoisonStatus(List<PartyMember> partyMembers)
    {
        // Find poison ailment by party indicator
        var poisonAilment = GetAilmentByPartyIndicator("P");
        if (poisonAilment == null) return;
        
        foreach (var member in partyMembers)
        {
            if (_isTargetSelf(member.Name)) continue;
            
            if (member.IsPoisoned)
            {
                // Add poison ailment if not already tracked
                if (!HasActiveAilment(poisonAilment.Id, member.Name))
                {
                    AddActiveAilment(poisonAilment.Id, member.Name);
                    OnLogMessage?.Invoke($"🤢 {member.Name} is poisoned!");
                }
            }
            else
            {
                // Remove poison ailment if tracked but no longer poisoned
                RemoveActiveAilment(poisonAilment.Id, member.Name);
            }
        }
    }
    
    #endregion
    
    #region Ailment Tracking
    
    private void AddActiveAilment(string ailmentId, string targetName)
    {
        // Check if already exists
        if (HasActiveAilment(ailmentId, targetName)) return;
        
        _activeAilments.Add(new ActiveAilment
        {
            AilmentId = ailmentId,
            TargetName = targetName,
            DetectedAt = DateTime.Now
        });
    }
    
    private void RemoveActiveAilment(string ailmentId, string targetName)
    {
        var beforeCount = _activeAilments.Count;
        _activeAilments.RemoveAll(a => 
            a.AilmentId == ailmentId && 
            a.TargetName.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        var afterCount = _activeAilments.Count;
        
        if (beforeCount != afterCount)
        {
            OnLogMessage?.Invoke($"🔍 Removed ailment: ailmentId={ailmentId}, target='{targetName}', removed={beforeCount - afterCount}");
        }
        else
        {
            OnLogMessage?.Invoke($"🔍 No ailment found to remove: ailmentId={ailmentId}, target='{targetName}'");
            // Debug: show what ailments we have
            foreach (var a in _activeAilments)
            {
                OnLogMessage?.Invoke($"   Active ailment: id={a.AilmentId}, target='{a.TargetName}'");
            }
        }
    }
    
    public bool HasActiveAilment(string ailmentId, string targetName)
    {
        return _activeAilments.Any(a => 
            a.AilmentId == ailmentId && 
            a.TargetName.Equals(targetName, StringComparison.OrdinalIgnoreCase));
    }
    
    public bool AnyoneNeedsCuring()
    {
        return _activeAilments.Count > 0;
    }
    
    public void ClearAllAilments()
    {
        _activeAilments.Clear();
    }
    
    /// <summary>
    /// Clear ailments for a specific target (used when they leave the party)
    /// </summary>
    public void ClearAilmentsForTarget(string targetName)
    {
        var removed = _activeAilments.RemoveAll(a => 
            !a.IsSelf && a.TargetName.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        
        if (removed > 0)
        {
            OnLogMessage?.Invoke($"🧹 Cleared {removed} ailment(s) for {targetName} (left party)");
        }
    }
    
    /// <summary>
    /// Clear ailments for targets not in the provided party list
    /// </summary>
    public void ClearAilmentsForMissingMembers(IEnumerable<PartyMember> currentPartyMembers)
    {
        var currentNames = currentPartyMembers.Select(p => p.Name.ToLowerInvariant()).ToHashSet();
        
        var removed = _activeAilments.RemoveAll(a => 
            !a.IsSelf && !currentNames.Contains(a.TargetName.ToLowerInvariant()));
        
        if (removed > 0)
        {
            OnLogMessage?.Invoke($"🧹 Cleared {removed} ailment(s) for members no longer in party");
        }
    }
    
    #endregion
    
    #region Cure Logic
    
    /// <summary>
    /// Check if anyone needs curing and return the command to cast, or null if no curing needed.
    /// </summary>
    public (string? Command, string Description, ActiveAilment? Ailment)? CheckCuring()
    {
        if (!_config.CuringEnabled) return null;
        if (_activeAilments.Count == 0) return null;
        
        // Get current party members for validation
        var partyMembers = _getPartyMembers().ToList();
        var partyNames = partyMembers.Select(p => p.Name.ToLowerInvariant()).ToHashSet();
        
        // Clean up expired pending cures (re-allow curing after 10 seconds if cure didn't work)
        foreach (var expiredAilment in _activeAilments.Where(a => a.IsCurePendingExpired))
        {
            expiredAilment.CureInitiatedAt = null;  // Reset so we can try again
            OnLogMessage?.Invoke($"🔄 Cure timeout expired for {expiredAilment.TargetName}, will retry");
        }
        
        // Debug: Log active ailments
        foreach (var aa in _activeAilments)
        {
            var ailmentName = GetAilment(aa.AilmentId)?.DisplayName ?? "Unknown";
            var debugTarget = aa.IsSelf ? "SELF" : aa.TargetName;
            var pendingNote = aa.CurePending ? " (cure pending)" : "";
            OnLogMessage?.Invoke($"🔍 Active ailment: {ailmentName} on {debugTarget}{pendingNote}");
        }
        
        var currentMana = _getCurrentMana();
        var maxMana = _getMaxMana();
        
        // Note: Mana reserve does NOT apply to cures - we always try to cure if we have enough mana
        
        // Get all ailments with their cure spells, sorted by cure priority
        var cureCandidates = new List<(ActiveAilment Ailment, CureSpellConfiguration Spell, string Command)>();
        
        foreach (var activeAilment in _activeAilments)
        {
            // Skip ailments with pending cures (already initiated a cure for this)
            if (activeAilment.CurePending)
            {
                continue;
            }
            
            // Skip party members who are no longer in the party
            if (!activeAilment.IsSelf && !partyNames.Contains(activeAilment.TargetName.ToLowerInvariant()))
            {
                OnLogMessage?.Invoke($"⚠️ Skipping {activeAilment.TargetName} - not in party");
                continue;
            }
            
            var cureSpell = GetCureSpellForAilment(activeAilment.AilmentId);
            if (cureSpell == null)
            {
                var ailmentName = GetAilment(activeAilment.AilmentId)?.DisplayName ?? "Unknown";
                OnLogMessage?.Invoke($"⚠️ No cure spell configured for {ailmentName}");
                continue;
            }
            
            // Check mana cost
            if (cureSpell.ManaCost > 0 && currentMana < cureSpell.ManaCost)
            {
                OnLogMessage?.Invoke($"⚠️ Not enough mana for {cureSpell.DisplayName} (need {cureSpell.ManaCost}, have {currentMana})");
                continue;
            }
            
            // Note: No mana reserve check for cures - cures are always priority
            
            // Build command
            string command = cureSpell.Command;
            if (!activeAilment.IsSelf)
            {
                command = $"{command} {activeAilment.TargetName}";
            }
            
            cureCandidates.Add((activeAilment, cureSpell, command));
        }
        
        if (cureCandidates.Count == 0) return null;
        
        // Sort by priority (lower = higher priority), then by detection time (oldest first)
        var bestCure = cureCandidates
            .OrderBy(c => c.Spell.Priority)
            .ThenBy(c => c.Ailment.DetectedAt)
            .First();
        
        var ailment = GetAilment(bestCure.Ailment.AilmentId);
        var targetDesc = bestCure.Ailment.IsSelf ? "self" : bestCure.Ailment.TargetName;
        
        return (bestCure.Command, $"{bestCure.Spell.DisplayName} on {targetDesc} ({ailment?.DisplayName ?? "ailment"})", bestCure.Ailment);
    }
    
    /// <summary>
    /// Mark an ailment as having a cure initiated (to prevent duplicate cures)
    /// </summary>
    public void MarkCureInitiated(ActiveAilment ailment)
    {
        ailment.CureInitiatedAt = DateTime.Now;
    }
    
    #endregion
}
