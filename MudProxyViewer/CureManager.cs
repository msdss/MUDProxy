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
    public event Action? OnAilmentsChanged;
    
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
                }
            }
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error loading cure configuration: {ex.Message}");
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
                    if (_isTargetSelf(targetName))
                    {
                        RemoveActiveAilment(cureSpell.AilmentId, "");
                        OnLogMessage?.Invoke($"✨ Cured self of {GetAilment(cureSpell.AilmentId)?.DisplayName ?? "ailment"}");
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
        var regexPattern = escapedPattern.Replace("<<<TARGET>>>", "(.+?)");
        
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
        OnAilmentsChanged?.Invoke();
    }
    
    private void RemoveActiveAilment(string ailmentId, string targetName)
    {
        var removed = _activeAilments.RemoveAll(a => 
            a.AilmentId == ailmentId && 
            a.TargetName.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        
        if (removed > 0)
            OnAilmentsChanged?.Invoke();
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
        OnAilmentsChanged?.Invoke();
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
            OnAilmentsChanged?.Invoke();
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
            OnAilmentsChanged?.Invoke();
        }
    }
    
    #endregion
    
    #region Cure Logic
    
    /// <summary>
    /// Check if anyone needs curing and return the command to cast, or null if no curing needed.
    /// </summary>
    public (string? Command, string Description)? CheckCuring()
    {
        if (!_config.CuringEnabled) return null;
        if (_activeAilments.Count == 0) return null;
        
        // Get current party members for validation
        var partyMembers = _getPartyMembers().ToList();
        var partyNames = partyMembers.Select(p => p.Name.ToLowerInvariant()).ToHashSet();
        
        // Debug: Log active ailments
        foreach (var aa in _activeAilments)
        {
            var ailmentName = GetAilment(aa.AilmentId)?.DisplayName ?? "Unknown";
            var debugTarget = aa.IsSelf ? "SELF" : aa.TargetName;
            OnLogMessage?.Invoke($"🔍 Active ailment: {ailmentName} on {debugTarget}");
        }
        
        var currentMana = _getCurrentMana();
        var maxMana = _getMaxMana();
        
        // Note: Mana reserve does NOT apply to cures - we always try to cure if we have enough mana
        
        // Get all ailments with their cure spells, sorted by cure priority
        var cureCandidates = new List<(ActiveAilment Ailment, CureSpellConfiguration Spell, string Command)>();
        
        foreach (var activeAilment in _activeAilments)
        {
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
        
        return (bestCure.Command, $"{bestCure.Spell.DisplayName} on {targetDesc} ({ailment?.DisplayName ?? "ailment"})");
    }
    
    #endregion
}
