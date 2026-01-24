namespace MudProxyViewer;

public enum BuffCategory
{
    Combat,
    Defense,
    Utility
}

public enum BuffTargetType
{
    SelfOnly,
    MeleeParty,
    CasterParty,
    AllParty
}

public class BuffConfiguration
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DisplayName { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public int DurationSeconds { get; set; } = 60;
    public int ManaCost { get; set; } = 0;
    public BuffCategory Category { get; set; } = BuffCategory.Combat;
    public BuffTargetType TargetType { get; set; } = BuffTargetType.SelfOnly;
    public string SelfCastMessage { get; set; } = string.Empty;
    public string PartyCastMessage { get; set; } = string.Empty; // Use {target} as placeholder
    public string ExpireMessage { get; set; } = string.Empty;
    
    // Auto-recast settings
    public bool AutoRecast { get; set; } = false;
    public int RecastBufferSeconds { get; set; } = 10; // Recast when this many seconds remaining
    public int Priority { get; set; } = 5; // 1 = highest priority, 10 = lowest
    
    public BuffConfiguration Clone()
    {
        return new BuffConfiguration
        {
            Id = this.Id,
            DisplayName = this.DisplayName,
            Command = this.Command,
            DurationSeconds = this.DurationSeconds,
            ManaCost = this.ManaCost,
            Category = this.Category,
            TargetType = this.TargetType,
            SelfCastMessage = this.SelfCastMessage,
            PartyCastMessage = this.PartyCastMessage,
            ExpireMessage = this.ExpireMessage,
            AutoRecast = this.AutoRecast,
            RecastBufferSeconds = this.RecastBufferSeconds,
            Priority = this.Priority
        };
    }
}

public class ActiveBuff
{
    public BuffConfiguration Configuration { get; set; } = null!;
    public string TargetName { get; set; } = string.Empty; // Empty = self
    public DateTime CastTime { get; set; }
    public DateTime ExpireTime { get; set; }
    
    public TimeSpan TimeRemaining => ExpireTime - DateTime.Now;
    public bool IsExpired => DateTime.Now >= ExpireTime;
    public double PercentRemaining => Math.Max(0, TimeRemaining.TotalSeconds / Configuration.DurationSeconds * 100);
    
    public bool IsSelfBuff => string.IsNullOrEmpty(TargetName);
    
    public string GetDisplayTimeRemaining()
    {
        var remaining = TimeRemaining;
        if (remaining.TotalSeconds <= 0)
            return "EXPIRED";
        if (remaining.TotalMinutes >= 1)
            return $"{(int)remaining.TotalMinutes}:{remaining.Seconds:D2}";
        return $"0:{remaining.Seconds:D2}";
    }
}

public class PartyMember
{
    public string Name { get; set; } = string.Empty; // First name only (used for commands)
    public string FullName { get; set; } = string.Empty; // Full display name
    public string Class { get; set; } = string.Empty;
    public int ManaPercent { get; set; }
    public int HealthPercent { get; set; }
    public string Rank { get; set; } = string.Empty; // Frontrank or Backrank
    public bool IsResting { get; set; } = false;
    public bool IsPoisoned { get; set; } = false;
    
    // Actual HP/Mana values from telepath messages
    public int CurrentHp { get; set; } = 0;
    public int MaxHp { get; set; } = 0;
    public int CurrentMana { get; set; } = 0;
    public int MaxMana { get; set; } = 0;
    public string ResourceType { get; set; } = "Mana";  // "Mana" or "Kai"
    public DateTime LastTelepathUpdate { get; set; } = DateTime.MinValue;
    
    // Use actual HP if known, otherwise fall back to percentage
    public int EffectiveHealthPercent => MaxHp > 0 ? (CurrentHp * 100 / MaxHp) : HealthPercent;
    public int EffectiveManaPercent => MaxMana > 0 ? (CurrentMana * 100 / MaxMana) : ManaPercent;
    public bool HasActualHpData => MaxHp > 0;
    public bool HasActualManaData => MaxMana > 0;
    
    public bool IsMelee => MeleeClasses.Contains(Class.ToLower());
    public bool IsCaster => CasterClasses.Contains(Class.ToLower());
    
    public static readonly HashSet<string> MeleeClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "bard", "cleric", "gypsy", "missionary", "mystic", "ninja",
        "paladin", "ranger", "thief", "warlock", "warrior", "witchunter"
    };
    
    public static readonly HashSet<string> CasterClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "mage", "priest", "druid"
    };
}

public class PlayerInfo
{
    public string Name { get; set; } = string.Empty;
    public string Race { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public int Level { get; set; }
    public int CurrentHp { get; set; }
    public int MaxHp { get; set; }
    public int CurrentMana { get; set; }
    public int MaxMana { get; set; }
    
    public bool IsMelee => PartyMember.MeleeClasses.Contains(Class);
    public bool IsCaster => PartyMember.CasterClasses.Contains(Class);
    
    public double HpPercent => MaxHp > 0 ? (CurrentHp * 100.0 / MaxHp) : 100;
}

public enum HealTargetType
{
    SelfOnly,
    SingleTarget,  // Can target self or party member
    PartyHeal      // Heals entire party
}

public class HealSpellConfiguration
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DisplayName { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public int ManaCost { get; set; } = 0;
    public HealTargetType TargetType { get; set; } = HealTargetType.SingleTarget;
    public string SelfCastMessage { get; set; } = string.Empty;  // e.g., "You cast minor healing on {target}, healing {amount} damage!"
    public string PartyCastMessage { get; set; } = string.Empty; // For SingleTarget on others
    public string PartyHealMessage { get; set; } = string.Empty; // For PartyHeal type, e.g., "You cast healing rain on your party"
    
    public HealSpellConfiguration Clone()
    {
        return new HealSpellConfiguration
        {
            Id = this.Id,
            DisplayName = this.DisplayName,
            Command = this.Command,
            ManaCost = this.ManaCost,
            TargetType = this.TargetType,
            SelfCastMessage = this.SelfCastMessage,
            PartyCastMessage = this.PartyCastMessage,
            PartyHealMessage = this.PartyHealMessage
        };
    }
}

public class HealRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string HealSpellId { get; set; } = string.Empty;  // Reference to HealSpellConfiguration
    public int HpThresholdPercent { get; set; } = 70;        // Cast when HP below this %
    public bool IsCritical { get; set; } = false;             // Critical heals happen before cures
    
    // For party heals only
    public bool IsPartyHealRule { get; set; } = false;
    public int PartyPercentRequired { get; set; } = 50;       // % of party that must be below threshold
    
    public HealRule Clone()
    {
        return new HealRule
        {
            Id = this.Id,
            HealSpellId = this.HealSpellId,
            HpThresholdPercent = this.HpThresholdPercent,
            IsCritical = this.IsCritical,
            IsPartyHealRule = this.IsPartyHealRule,
            PartyPercentRequired = this.PartyPercentRequired
        };
    }
}

public class HealingConfiguration
{
    public bool HealingEnabled { get; set; } = true;
    public bool HealsPriorityOverBuffs { get; set; } = true;
    public List<HealSpellConfiguration> HealSpells { get; set; } = new();
    public List<HealRule> SelfHealRules { get; set; } = new();
    public List<HealRule> PartyHealRules { get; set; } = new();  // Single target heals on party members
    public List<HealRule> PartyWideHealRules { get; set; } = new();  // Party heal spells
}

// Ailment System
public class AilmentConfiguration
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DisplayName { get; set; } = string.Empty;  // e.g., "Poison", "Paralysis"
    public List<string> DetectionMessages { get; set; } = new();  // Messages that indicate this ailment on self
    public string? PartyIndicator { get; set; } = null;  // e.g., "P" for poison in party display
    public string? TelepathRequest { get; set; } = null;  // e.g., "@held" for paralysis
    
    public AilmentConfiguration Clone()
    {
        return new AilmentConfiguration
        {
            Id = this.Id,
            DisplayName = this.DisplayName,
            DetectionMessages = new List<string>(this.DetectionMessages),
            PartyIndicator = this.PartyIndicator,
            TelepathRequest = this.TelepathRequest
        };
    }
}

public class CureSpellConfiguration
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DisplayName { get; set; } = string.Empty;  // e.g., "Cure Poison"
    public string Command { get; set; } = string.Empty;  // e.g., "cure"
    public int ManaCost { get; set; } = 0;
    public string AilmentId { get; set; } = string.Empty;  // Reference to AilmentConfiguration
    public string SelfCastMessage { get; set; } = string.Empty;  // e.g., "You cast cure poison on {target}!"
    public string PartyCastMessage { get; set; } = string.Empty;  // e.g., "You cast cure poison on {target}!"
    public int Priority { get; set; } = 5;  // Lower = higher priority
    
    public CureSpellConfiguration Clone()
    {
        return new CureSpellConfiguration
        {
            Id = this.Id,
            DisplayName = this.DisplayName,
            Command = this.Command,
            ManaCost = this.ManaCost,
            AilmentId = this.AilmentId,
            SelfCastMessage = this.SelfCastMessage,
            PartyCastMessage = this.PartyCastMessage,
            Priority = this.Priority
        };
    }
}

public class ActiveAilment
{
    public string AilmentId { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;  // Empty = self
    public DateTime DetectedAt { get; set; } = DateTime.Now;
    
    public bool IsSelf => string.IsNullOrEmpty(TargetName);
}

public enum CastPriorityType
{
    CriticalHeals = 0,
    Cures = 1,
    RegularHeals = 2,
    Buffs = 3
}

public class CureConfiguration
{
    public bool CuringEnabled { get; set; } = true;
    public List<AilmentConfiguration> Ailments { get; set; } = new();
    public List<CureSpellConfiguration> CureSpells { get; set; } = new();
    
    // Priority order (lower index = higher priority)
    public List<CastPriorityType> PriorityOrder { get; set; } = new()
    {
        CastPriorityType.CriticalHeals,
        CastPriorityType.Cures,
        CastPriorityType.RegularHeals,
        CastPriorityType.Buffs
    };
}

public class ProxySettings
{
    public bool ParAutoEnabled { get; set; } = false;
    public int ParFrequencySeconds { get; set; } = 15;
    public bool ParAfterCombatTick { get; set; } = false;
    public bool HealthRequestEnabled { get; set; } = false;
    public int HealthRequestIntervalSeconds { get; set; } = 60;
    public int ManaReservePercent { get; set; } = 20;
}
