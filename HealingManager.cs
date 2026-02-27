using System.Text.Json;

namespace MudProxyViewer;

public class HealingManager
{
    private HealingConfiguration _config = new();
    private readonly string _configFilePath;
    
    // References to shared state
    private readonly Func<PlayerInfo> _getPlayerInfo;
    private readonly Func<IEnumerable<PartyMember>> _getPartyMembers;
    private readonly Func<int> _getCurrentMana;
    private readonly Func<int> _getMaxMana;
    private readonly Func<string, bool> _isTargetSelf;
    private readonly Func<bool> _getIsResting;
    private readonly Func<bool> _getInCombat;
    
    // Events
    public event Action<string>? OnLogMessage;
    
    public HealingManager(
        Func<PlayerInfo> getPlayerInfo,
        Func<IEnumerable<PartyMember>> getPartyMembers,
        Func<int> getCurrentMana,
        Func<int> getMaxMana,
        Func<string, bool> isTargetSelf,
        Func<bool> getIsResting,
        Func<bool> getInCombat)
    {
        _getPlayerInfo = getPlayerInfo;
        _getPartyMembers = getPartyMembers;
        _getCurrentMana = getCurrentMana;
        _getMaxMana = getMaxMana;
        _isTargetSelf = isTargetSelf;
        _getIsResting = getIsResting;
        _getInCombat = getInCombat;
        
        _configFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MudProxyViewer",
            "healing.json");
        
        LoadConfiguration();
        _config.HealingEnabled = true; // Always default to ON on startup
    }
    
    public HealingConfiguration Configuration => _config;
    public bool HealingEnabled
    {
        get => _config.HealingEnabled;
        set => _config.HealingEnabled = value;
    }
    
    public bool HasSelfHealRules => _config.SelfHealRules.Count > 0;
    public bool HasPartyHealRules => _config.PartyHealRules.Count > 0;
    
    #region Configuration Management
    
    public void LoadConfiguration()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                var config = JsonSerializer.Deserialize<HealingConfiguration>(json);
                if (config != null)
                {
                    _config = config;
                }
            }
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"Error loading healing configuration: {ex.Message}");
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
            OnLogMessage?.Invoke($"Error saving healing configuration: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Replace the entire configuration (used when loading character profiles)
    /// </summary>
    public void ReplaceConfiguration(HealingConfiguration config)
    {
        _config = config;
        SaveConfiguration();
    }
    
    public string ExportHeals()
    {
        var export = new HealExport
        {
            ExportVersion = "1.0",
            ExportedAt = DateTime.Now,
            HealSpells = _config.HealSpells.Select(s => s.Clone()).ToList(),
            SelfHealRules = _config.SelfHealRules.Select(r => r.Clone()).ToList(),
            PartyHealRules = _config.PartyHealRules.Select(r => r.Clone()).ToList(),
            PartyWideHealRules = _config.PartyWideHealRules.Select(r => r.Clone()).ToList()
        };
        
        // Create a mapping of old spell IDs to new ones
        var idMapping = new Dictionary<string, string>();
        foreach (var spell in export.HealSpells)
        {
            var oldId = spell.Id;
            spell.Id = Guid.NewGuid().ToString();
            idMapping[oldId] = spell.Id;
        }
        
        // Update rule references to use new spell IDs
        foreach (var rule in export.SelfHealRules.Concat(export.PartyHealRules).Concat(export.PartyWideHealRules))
        {
            rule.Id = Guid.NewGuid().ToString();
            if (idMapping.TryGetValue(rule.HealSpellId, out var newSpellId))
                rule.HealSpellId = newSpellId;
        }
        
        return JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
    }
    
    public (int imported, int skipped, string message) ImportHeals(string json, bool replaceExisting)
    {
        try
        {
            var export = JsonSerializer.Deserialize<HealExport>(json);
            if (export == null)
                return (0, 0, "Invalid heal export file format.");
            
            int importedSpells = 0;
            int skippedSpells = 0;
            var spellIdMapping = new Dictionary<string, string>();
            
            // Import spells first
            foreach (var spell in export.HealSpells ?? new List<HealSpellConfiguration>())
            {
                var existing = _config.HealSpells.FirstOrDefault(s => 
                    s.DisplayName.Equals(spell.DisplayName, StringComparison.OrdinalIgnoreCase));
                
                var oldId = spell.Id;
                
                if (existing != null)
                {
                    if (replaceExisting)
                    {
                        spellIdMapping[oldId] = existing.Id;
                        spell.Id = existing.Id;
                        var index = _config.HealSpells.IndexOf(existing);
                        _config.HealSpells[index] = spell;
                        importedSpells++;
                    }
                    else
                    {
                        spellIdMapping[oldId] = existing.Id; // Map to existing for rules
                        skippedSpells++;
                    }
                }
                else
                {
                    spell.Id = Guid.NewGuid().ToString();
                    spellIdMapping[oldId] = spell.Id;
                    _config.HealSpells.Add(spell);
                    importedSpells++;
                }
            }
            
            // Import rules with updated spell references
            int importedRules = 0;
            
            foreach (var rule in export.SelfHealRules ?? new List<HealRule>())
            {
                if (spellIdMapping.TryGetValue(rule.HealSpellId, out var newSpellId))
                {
                    rule.Id = Guid.NewGuid().ToString();
                    rule.HealSpellId = newSpellId;
                    _config.SelfHealRules.Add(rule);
                    importedRules++;
                }
            }
            
            foreach (var rule in export.PartyHealRules ?? new List<HealRule>())
            {
                if (spellIdMapping.TryGetValue(rule.HealSpellId, out var newSpellId))
                {
                    rule.Id = Guid.NewGuid().ToString();
                    rule.HealSpellId = newSpellId;
                    _config.PartyHealRules.Add(rule);
                    importedRules++;
                }
            }
            
            foreach (var rule in export.PartyWideHealRules ?? new List<HealRule>())
            {
                if (spellIdMapping.TryGetValue(rule.HealSpellId, out var newSpellId))
                {
                    rule.Id = Guid.NewGuid().ToString();
                    rule.HealSpellId = newSpellId;
                    _config.PartyWideHealRules.Add(rule);
                    importedRules++;
                }
            }
            
            if (importedSpells > 0 || importedRules > 0)
            {
                SaveConfiguration();
            }
            
            return (importedSpells + importedRules, skippedSpells, 
                $"Imported {importedSpells} spell(s) and {importedRules} rule(s), skipped {skippedSpells} duplicate spell(s).");
        }
        catch (Exception ex)
        {
            return (0, 0, $"Error importing heals: {ex.Message}");
        }
    }
    
    // Heal Spell CRUD
    public void AddHealSpell(HealSpellConfiguration spell)
    {
        _config.HealSpells.Add(spell);
        SaveConfiguration();
    }
    
    public void UpdateHealSpell(HealSpellConfiguration spell)
    {
        var index = _config.HealSpells.FindIndex(s => s.Id == spell.Id);
        if (index >= 0)
        {
            _config.HealSpells[index] = spell;
            SaveConfiguration();
        }
    }
    
    public void RemoveHealSpell(string id)
    {
        _config.HealSpells.RemoveAll(s => s.Id == id);
        // Also remove any rules referencing this spell
        _config.SelfHealRules.RemoveAll(r => r.HealSpellId == id);
        _config.PartyHealRules.RemoveAll(r => r.HealSpellId == id);
        _config.PartyWideHealRules.RemoveAll(r => r.HealSpellId == id);
        SaveConfiguration();
    }
    
    public HealSpellConfiguration? GetHealSpell(string id)
    {
        return _config.HealSpells.FirstOrDefault(s => s.Id == id);
    }
    
    // Self Heal Rules CRUD
    public void AddSelfHealRule(HealRule rule)
    {
        _config.SelfHealRules.Add(rule);
        SaveConfiguration();
    }
    
    public void UpdateSelfHealRule(HealRule rule)
    {
        var index = _config.SelfHealRules.FindIndex(r => r.Id == rule.Id);
        if (index >= 0)
        {
            _config.SelfHealRules[index] = rule;
            SaveConfiguration();
        }
    }
    
    public void RemoveSelfHealRule(string id)
    {
        _config.SelfHealRules.RemoveAll(r => r.Id == id);
        SaveConfiguration();
    }
    
    // Party Heal Rules CRUD
    public void AddPartyHealRule(HealRule rule)
    {
        _config.PartyHealRules.Add(rule);
        SaveConfiguration();
    }
    
    public void UpdatePartyHealRule(HealRule rule)
    {
        var index = _config.PartyHealRules.FindIndex(r => r.Id == rule.Id);
        if (index >= 0)
        {
            _config.PartyHealRules[index] = rule;
            SaveConfiguration();
        }
    }
    
    public void RemovePartyHealRule(string id)
    {
        _config.PartyHealRules.RemoveAll(r => r.Id == id);
        SaveConfiguration();
    }
    
    // Party-Wide Heal Rules CRUD
    public void AddPartyWideHealRule(HealRule rule)
    {
        rule.IsPartyHealRule = true;
        _config.PartyWideHealRules.Add(rule);
        SaveConfiguration();
    }
    
    public void UpdatePartyWideHealRule(HealRule rule)
    {
        var index = _config.PartyWideHealRules.FindIndex(r => r.Id == rule.Id);
        if (index >= 0)
        {
            _config.PartyWideHealRules[index] = rule;
            SaveConfiguration();
        }
    }
    
    public void RemovePartyWideHealRule(string id)
    {
        _config.PartyWideHealRules.RemoveAll(r => r.Id == id);
        SaveConfiguration();
    }
    
    #endregion
    
    #region Healing Logic
    
    /// <summary>
    /// Check if anyone needs healing based on current player state.
    /// Self-healing uses state-based rules (Combat vs Resting).
    /// Party healing uses simple threshold rules.
    /// </summary>
    public (string? Command, string Description)? CheckHealing()
    {
        if (!_config.HealingEnabled) return null;
        
        var playerInfo = _getPlayerInfo();
        var partyMembers = _getPartyMembers().ToList();
        var currentMana = _getCurrentMana();
        var maxMana = _getMaxMana();
        var isResting = _getIsResting();
        var inCombat = _getInCombat();
        
        // Determine which self-heal rules to use based on state
        List<HealRule> applicableSelfRules;
        if (isResting)
        {
            // Resting: only use Resting rules
            applicableSelfRules = _config.SelfHealRules.Where(r => r.RuleType == HealRuleType.Resting).ToList();
        }
        else
        {
            // Combat or Idle: use Combat rules
            applicableSelfRules = _config.SelfHealRules.Where(r => r.RuleType == HealRuleType.Combat).ToList();
        }
        
        // Party rules are not state-based
        var partyRules = _config.PartyHealRules;
        var partyWideRules = _config.PartyWideHealRules;
        
        // First, check party-wide heals (might be more efficient)
        var partyWideHeal = CheckPartyWideHealsFiltered(playerInfo, partyMembers, currentMana, maxMana, partyWideRules.ToList());
        if (partyWideHeal != null) return partyWideHeal;
        
        // Collect all potential heal targets with their best heal option
        var healCandidates = new List<(string Target, string Command, HealSpellConfiguration Spell, int ThresholdPercent, double HpPercent)>();
        
        // Check self healing (using state-filtered rules)
        var selfHeal = GetBestHealForTarget(playerInfo.HpPercent, applicableSelfRules, currentMana, maxMana);
        if (selfHeal != null)
        {
            healCandidates.Add(("", selfHeal.Value.Spell.Command, selfHeal.Value.Spell, selfHeal.Value.ThresholdPercent, playerInfo.HpPercent));
        }
        
        // Check party member healing (not state-based)
        foreach (var member in partyMembers)
        {
            if (_isTargetSelf(member.Name)) continue; // Skip self (already handled above)
            
            var memberHeal = GetBestHealForTarget(member.EffectiveHealthPercent, partyRules.ToList(), currentMana, maxMana);
            if (memberHeal != null)
            {
                var command = $"{memberHeal.Value.Spell.Command} {member.Name}";
                healCandidates.Add((member.Name, command, memberHeal.Value.Spell, memberHeal.Value.ThresholdPercent, member.EffectiveHealthPercent));
            }
        }
        
        if (healCandidates.Count == 0) return null;
        
        // Sort by lowest HP first (most urgent), then by lowest threshold (most specific rule)
        var bestHeal = healCandidates
            .OrderBy(h => h.HpPercent)
            .ThenBy(h => h.ThresholdPercent)
            .First();
        
        var targetDesc = string.IsNullOrEmpty(bestHeal.Target) ? "self" : bestHeal.Target;
        return (bestHeal.Command, $"{bestHeal.Spell.DisplayName} on {targetDesc} (HP: {bestHeal.HpPercent:F0}%)");
    }
    
    private (string Command, string Description)? CheckPartyWideHealsFiltered(
        PlayerInfo playerInfo, 
        List<PartyMember> partyMembers,
        int currentMana,
        int maxMana,
        List<HealRule> rules)
    {
        if (rules.Count == 0) return null;
        
        // Get all HP percentages including self
        var allHpPercents = new List<double> { playerInfo.HpPercent };
        allHpPercents.AddRange(partyMembers
            .Where(p => !_isTargetSelf(p.Name))
            .Select(p => (double)p.EffectiveHealthPercent));
        
        if (allHpPercents.Count <= 1) return null; // No party to heal
        
        var totalMembers = allHpPercents.Count;
        
        // Check each party-wide heal rule (sorted by threshold - lowest first)
        foreach (var rule in rules.OrderBy(r => r.HpThresholdPercent))
        {
            var spell = GetHealSpell(rule.HealSpellId);
            if (spell == null) continue;
            
            // Check mana cost (no reserve check for heals)
            if (!CanAffordSpell(spell.ManaCost, currentMana, maxMana)) continue;
            
            // Count how many are below threshold
            var belowThreshold = allHpPercents.Count(hp => hp < rule.HpThresholdPercent);
            var percentBelow = (belowThreshold * 100) / totalMembers;
            
            if (percentBelow >= rule.PartyPercentRequired)
            {
                return (spell.Command, $"{spell.DisplayName} (party heal - {belowThreshold}/{totalMembers} below {rule.HpThresholdPercent}%)");
            }
        }
        
        return null;
    }
    
    private (HealSpellConfiguration Spell, int ThresholdPercent)? GetBestHealForTarget(
        double hpPercent,
        List<HealRule> rules,
        int currentMana,
        int maxMana)
    {
        // Find all applicable rules (where HP is below threshold)
        // Sort by LOWEST threshold first (most specific/urgent rule)
        var applicableRules = rules
            .Where(r => hpPercent < r.HpThresholdPercent)
            .OrderBy(r => r.HpThresholdPercent);
        
        foreach (var rule in applicableRules)
        {
            var spell = GetHealSpell(rule.HealSpellId);
            if (spell == null) continue;
            
            // Check mana cost (no reserve check for heals)
            if (!CanAffordSpell(spell.ManaCost, currentMana, maxMana)) continue;
            
            return (spell, rule.HpThresholdPercent);
        }
        
        return null;
    }
    
    private bool CanAffordSpell(int manaCost, int currentMana, int maxMana)
    {
        if (manaCost <= 0) return true;
        return currentMana >= manaCost;
    }
    
    #endregion
}
