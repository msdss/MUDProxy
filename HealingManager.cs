using System.Text.Json;
using System.Text.RegularExpressions;

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
    private readonly Func<int> _getManaReservePercent;
    private readonly Func<string, bool> _isTargetSelf;
    
    // Events
    public event Action<string>? OnLogMessage;
    
    // Telepath regex: "Boost telepaths: {HP=134/144,KAI=7/15}" or "{HP=134/144,MA=7/15}"
    private static readonly Regex TelepathRegex = new(
        @"(\w+) telepaths: \{HP=(\d+)/(\d+),(MA|KAI)=(\d+)/(\d+)\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    public HealingManager(
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
            "healing.json");
        
        LoadConfiguration();
    }
    
    public HealingConfiguration Configuration => _config;
    public bool HealingEnabled
    {
        get => _config.HealingEnabled;
        set
        {
            _config.HealingEnabled = value;
            SaveConfiguration();
        }
    }
    
    public bool HealsPriorityOverBuffs
    {
        get => _config.HealsPriorityOverBuffs;
        set
        {
            _config.HealsPriorityOverBuffs = value;
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
    
    #region Message Processing
    
    public void ProcessMessage(string message, List<PartyMember> partyMembers)
    {
        // Check for telepath HP updates
        var telepathMatch = TelepathRegex.Match(message);
        if (telepathMatch.Success)
        {
            var name = telepathMatch.Groups[1].Value;
            var currentHp = int.Parse(telepathMatch.Groups[2].Value);
            var maxHp = int.Parse(telepathMatch.Groups[3].Value);
            var manaType = telepathMatch.Groups[4].Value;
            var currentMana = int.Parse(telepathMatch.Groups[5].Value);
            var maxMana = int.Parse(telepathMatch.Groups[6].Value);
            
            // Find the party member and update their HP
            var member = partyMembers.FirstOrDefault(p => 
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                p.FullName.StartsWith(name, StringComparison.OrdinalIgnoreCase));
            
            if (member != null)
            {
                member.CurrentHp = currentHp;
                member.MaxHp = maxHp;
                member.CurrentMana = currentMana;
                member.MaxMana = maxMana;
                member.LastTelepathUpdate = DateTime.Now;
                
                OnLogMessage?.Invoke($"💚 {name} HP updated: {currentHp}/{maxHp} ({member.EffectiveHealthPercent}%)");
            }
        }
    }
    
    #endregion
    
    #region Healing Logic
    
    /// <summary>
    /// Check if anyone needs critical healing (rules marked as IsCritical).
    /// </summary>
    public (string? Command, string Description)? CheckCriticalHealing()
    {
        return CheckHealingInternal(criticalOnly: true);
    }
    
    /// <summary>
    /// Check if anyone needs regular healing (rules NOT marked as IsCritical).
    /// </summary>
    public (string? Command, string Description)? CheckRegularHealing()
    {
        return CheckHealingInternal(criticalOnly: false);
    }
    
    /// <summary>
    /// Check if anyone needs healing (any rules).
    /// </summary>
    public (string? Command, string Description)? CheckHealing()
    {
        // Try critical first, then regular
        var critical = CheckCriticalHealing();
        if (critical != null) return critical;
        return CheckRegularHealing();
    }
    
    private (string? Command, string Description)? CheckHealingInternal(bool criticalOnly)
    {
        if (!_config.HealingEnabled) return null;
        
        var playerInfo = _getPlayerInfo();
        var partyMembers = _getPartyMembers().ToList();
        var currentMana = _getCurrentMana();
        var maxMana = _getMaxMana();
        
        // Note: Mana reserve does NOT apply to heals - we always try to heal if we have enough mana
        
        // Filter rules based on critical/regular
        var selfRules = _config.SelfHealRules.Where(r => r.IsCritical == criticalOnly).ToList();
        var partyRules = _config.PartyHealRules.Where(r => r.IsCritical == criticalOnly).ToList();
        var partyWideRules = _config.PartyWideHealRules.Where(r => r.IsCritical == criticalOnly).ToList();
        
        // First, check party-wide heals (might be more efficient)
        var partyWideHeal = CheckPartyWideHealsFiltered(playerInfo, partyMembers, currentMana, maxMana, partyWideRules);
        if (partyWideHeal != null) return partyWideHeal;
        
        // Collect all potential heal targets with their best heal option
        var healCandidates = new List<(string Target, string Command, HealSpellConfiguration Spell, int ThresholdPercent, double HpPercent)>();
        
        // Check self healing
        var selfHeal = GetBestHealForTarget(playerInfo.HpPercent, selfRules, currentMana, maxMana);
        if (selfHeal != null)
        {
            healCandidates.Add(("", selfHeal.Value.Spell.Command, selfHeal.Value.Spell, selfHeal.Value.ThresholdPercent, playerInfo.HpPercent));
        }
        
        // Check party member healing
        foreach (var member in partyMembers)
        {
            if (_isTargetSelf(member.Name)) continue; // Skip self (already handled above)
            
            var memberHeal = GetBestHealForTarget(member.EffectiveHealthPercent, partyRules, currentMana, maxMana);
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
    
    private (string Command, string Description)? CheckPartyWideHeals(
        PlayerInfo playerInfo, 
        List<PartyMember> partyMembers,
        int currentMana,
        int maxMana)
    {
        if (_config.PartyWideHealRules.Count == 0) return null;
        
        // Get all HP percentages including self
        var allHpPercents = new List<double> { playerInfo.HpPercent };
        allHpPercents.AddRange(partyMembers
            .Where(p => !_isTargetSelf(p.Name))
            .Select(p => (double)p.EffectiveHealthPercent));
        
        if (allHpPercents.Count <= 1) return null; // No party to heal
        
        var totalMembers = allHpPercents.Count;
        
        // Check each party-wide heal rule (sorted by threshold - lowest first)
        foreach (var rule in _config.PartyWideHealRules.OrderBy(r => r.HpThresholdPercent))
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
    
    /// <summary>
    /// Returns true if anyone needs healing (used to determine heal vs buff priority)
    /// </summary>
    public bool AnyoneNeedsHealing()
    {
        if (!_config.HealingEnabled) return false;
        
        var playerInfo = _getPlayerInfo();
        var partyMembers = _getPartyMembers().ToList();
        
        // Check self
        foreach (var rule in _config.SelfHealRules)
        {
            if (playerInfo.HpPercent < rule.HpThresholdPercent) return true;
        }
        
        // Check party members
        foreach (var member in partyMembers)
        {
            if (_isTargetSelf(member.Name)) continue;
            
            foreach (var rule in _config.PartyHealRules)
            {
                if (member.EffectiveHealthPercent < rule.HpThresholdPercent) return true;
            }
        }
        
        // Check party-wide thresholds
        var allHpPercents = new List<double> { playerInfo.HpPercent };
        allHpPercents.AddRange(partyMembers
            .Where(p => !_isTargetSelf(p.Name))
            .Select(p => (double)p.EffectiveHealthPercent));
        
        if (allHpPercents.Count > 1)
        {
            var totalMembers = allHpPercents.Count;
            
            foreach (var rule in _config.PartyWideHealRules)
            {
                var belowThreshold = allHpPercents.Count(hp => hp < rule.HpThresholdPercent);
                var percentBelow = (belowThreshold * 100) / totalMembers;
                if (percentBelow >= rule.PartyPercentRequired) return true;
            }
        }
        
        return false;
    }
    
    #endregion
}
