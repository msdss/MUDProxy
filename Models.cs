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
    
    // Experience tracking
    public long TotalExperience { get; set; }
    public long ExperienceNeededForNextLevel { get; set; }
    public long TotalExperienceForNextLevel { get; set; }
    public int LevelProgressPercent { get; set; }
    
    public bool IsMelee => PartyMember.MeleeClasses.Contains(Class, StringComparer.OrdinalIgnoreCase);
    public bool IsCaster => PartyMember.CasterClasses.Contains(Class, StringComparer.OrdinalIgnoreCase);
    
    public double HpPercent => MaxHp > 0 ? (CurrentHp * 100.0 / MaxHp) : 100;
}

/// <summary>
/// Tracks experience gains over time to calculate rate
/// </summary>
public class ExperienceTracker
{
    private readonly List<(DateTime time, long expGained)> _expGains = new();
    private readonly object _lock = new();
    private DateTime _sessionStart;
    private long _sessionExpGained;
    
    public ExperienceTracker()
    {
        Reset();
    }
    
    public void Reset()
    {
        lock (_lock)
        {
            _expGains.Clear();
            _sessionStart = DateTime.Now;
            _sessionExpGained = 0;
        }
    }
    
    public void AddExpGain(long amount)
    {
        lock (_lock)
        {
            _expGains.Add((DateTime.Now, amount));
            _sessionExpGained += amount;
            
            // Keep only last hour of data
            var cutoff = DateTime.Now.AddHours(-1);
            _expGains.RemoveAll(e => e.time < cutoff);
        }
    }
    
    public long SessionExpGained
    {
        get { lock (_lock) { return _sessionExpGained; } }
    }
    
    public TimeSpan SessionDuration => DateTime.Now - _sessionStart;
    
    /// <summary>
    /// Calculate experience per hour based on recent gains
    /// </summary>
    public long GetExpPerHour()
    {
        lock (_lock)
        {
            if (_expGains.Count == 0)
                return 0;
            
            // Use the last hour of data, or session duration if less than an hour
            var now = DateTime.Now;
            var cutoff = now.AddHours(-1);
            var recentGains = _expGains.Where(e => e.time >= cutoff).ToList();
            
            if (recentGains.Count == 0)
                return 0;
            
            var totalExp = recentGains.Sum(e => e.expGained);
            var timeSpan = now - recentGains.First().time;
            
            if (timeSpan.TotalHours < 0.01) // Less than 36 seconds
            {
                // Not enough time to calculate rate, use session data if available
                if (SessionDuration.TotalHours >= 0.01)
                {
                    return (long)(_sessionExpGained / SessionDuration.TotalHours);
                }
                return 0;
            }
            
            return (long)(totalExp / timeSpan.TotalHours);
        }
    }
    
    /// <summary>
    /// Estimate time to reach a certain amount of experience
    /// </summary>
    public TimeSpan? EstimateTimeToExp(long expNeeded)
    {
        var expPerHour = GetExpPerHour();
        if (expPerHour <= 0 || expNeeded <= 0)
            return null;
        
        var hours = (double)expNeeded / expPerHour;
        return TimeSpan.FromHours(hours);
    }
    
    /// <summary>
    /// Format time span as human-readable string, or "Now!" if already leveled
    /// </summary>
    public static string FormatTimeSpan(TimeSpan? time, bool alreadyLeveled = false)
    {
        if (alreadyLeveled)
            return "Now!";
        
        if (time == null)
            return "N/A";
        
        var ts = time.Value;
        
        if (ts.TotalDays >= 1)
            return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        else if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        else if (ts.TotalMinutes >= 1)
            return $"{(int)ts.TotalMinutes}m";
        else
            return "<1m";
    }
    
    /// <summary>
    /// Format large numbers with commas
    /// </summary>
    public static string FormatNumber(long number)
    {
        return number.ToString("N0");
    }
    
    /// <summary>
    /// Format large numbers with abbreviations (K, M, B)
    /// Examples: 1500 -> "1.5K", 1500000 -> "1.5M", 1500000000 -> "1.5B"
    /// </summary>
    public static string FormatNumberAbbreviated(long number)
    {
        if (number < 0)
            return "-" + FormatNumberAbbreviated(-number);
        
        if (number >= 1_000_000_000)
            return $"{number / 1_000_000_000.0:0.#}B";
        
        if (number >= 1_000_000)
            return $"{number / 1_000_000.0:0.#}M";
        
        if (number >= 1_000)
            return $"{number / 1_000.0:0.#}K";
        
        return number.ToString();
    }
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

public enum HealRuleType
{
    Combat,   // Active during combat AND idle (not resting)
    Resting   // Active only while resting
}

public class HealRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string HealSpellId { get; set; } = string.Empty;  // Reference to HealSpellConfiguration
    public int HpThresholdPercent { get; set; } = 70;        // Cast when HP below this %
    
    // State-based rule type (only applies to self-healing rules)
    public HealRuleType RuleType { get; set; } = HealRuleType.Combat;
    
    // For party-wide heals only
    public bool IsPartyHealRule { get; set; } = false;
    public int PartyPercentRequired { get; set; } = 50;       // % of party that must be below threshold
    
    public HealRule Clone()
    {
        return new HealRule
        {
            Id = this.Id,
            HealSpellId = this.HealSpellId,
            HpThresholdPercent = this.HpThresholdPercent,
            RuleType = this.RuleType,
            IsPartyHealRule = this.IsPartyHealRule,
            PartyPercentRequired = this.PartyPercentRequired
        };
    }
}

public class HealingConfiguration
{
    public bool HealingEnabled { get; set; } = true;
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
    public DateTime? CureInitiatedAt { get; set; } = null;  // When we started casting a cure
    
    public bool IsSelf => string.IsNullOrEmpty(TargetName);
    public bool CurePending => CureInitiatedAt.HasValue;
    
    // Consider cure pending for 10 seconds (enough time for cast + server response)
    public bool IsCurePendingExpired => CureInitiatedAt.HasValue && 
        (DateTime.Now - CureInitiatedAt.Value).TotalSeconds > 10;
}

public enum CastPriorityType
{
    Heals = 0,
    Cures = 1,
    Buffs = 2
}

public class CureConfiguration
{
    public bool CuringEnabled { get; set; } = true;
    public List<AilmentConfiguration> Ailments { get; set; } = new();
    public List<CureSpellConfiguration> CureSpells { get; set; } = new();
    
    // Priority order (lower index = higher priority)
    public List<CastPriorityType> PriorityOrder { get; set; } = new()
    {
        CastPriorityType.Heals,
        CastPriorityType.Cures,
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
    public bool BuffWhileResting { get; set; } = true;  // Allow buffing while resting
    public bool BuffWhileInCombat { get; set; } = true;  // Allow buffing while in combat
    public bool AutoStartProxy { get; set; } = false;  // Legacy - no longer used
    public bool CombatAutoEnabled { get; set; } = false;  // Combat toggle state
    public bool AutoLoadLastCharacter { get; set; } = false;  // Auto-load last character on startup
    public string LastCharacterPath { get; set; } = string.Empty;  // Path to last loaded character
    
    // UI Settings (merged from ui_settings.json)
    public bool DisplaySystemLog { get; set; } = true;  // Show/hide system log panel
}

/// <summary>
/// A single logon automation sequence entry (trigger message and response)
/// </summary>
public class LogonSequence
{
    public string TriggerMessage { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    
    public LogonSequence() { }
    
    public LogonSequence(string trigger, string response)
    {
        TriggerMessage = trigger;
        Response = response;
    }
    
    public LogonSequence Clone()
    {
        return new LogonSequence
        {
            TriggerMessage = this.TriggerMessage,
            Response = this.Response
        };
    }
}

/// <summary>
/// BBS/Telnet connection settings for a character profile
/// </summary>
public class BbsSettings
{
    // Connection
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; } = 23;
    
    // Logon automation - only active during login phase
    public List<LogonSequence> LogonSequences { get; set; } = new();
    
    // Commands
    public string LogoffCommand { get; set; } = string.Empty;
    public string RelogCommand { get; set; } = string.Empty;
    public int PvpLevel { get; set; } = 0;
    
    public BbsSettings Clone()
    {
        return new BbsSettings
        {
            Address = this.Address,
            Port = this.Port,
            LogonSequences = this.LogonSequences.Select(s => s.Clone()).ToList(),
            LogoffCommand = this.LogoffCommand,
            RelogCommand = this.RelogCommand,
            PvpLevel = this.PvpLevel
        };
    }
}

/// <summary>
/// Character profile containing all character-specific settings
/// </summary>
public class CharacterProfile
{
    public string ProfileVersion { get; set; } = "1.0";
    public DateTime SavedAt { get; set; } = DateTime.Now;
    
    // Character identification
    public string CharacterName { get; set; } = string.Empty;
    public string CharacterClass { get; set; } = string.Empty;
    public int CharacterLevel { get; set; } = 0;
    
    // BBS/Telnet settings
    public BbsSettings BbsSettings { get; set; } = new();
    
    // Combat settings
    public CombatSettings CombatSettings { get; set; } = new();
    
    // Buff configurations
    public List<BuffConfiguration> Buffs { get; set; } = new();
    
    // Heal configurations
    public List<HealSpellConfiguration> HealSpells { get; set; } = new();
    public List<HealRule> SelfHealRules { get; set; } = new();
    public List<HealRule> PartyHealRules { get; set; } = new();
    public List<HealRule> PartyWideHealRules { get; set; } = new();
    
    // Cure configurations
    public List<AilmentConfiguration> Ailments { get; set; } = new();
    public List<CureSpellConfiguration> CureSpells { get; set; } = new();
    
    // Monster overrides (attack priorities, relationships, etc.)
    public List<MonsterOverride> MonsterOverrides { get; set; } = new();
    
// Player database (friends, enemies, etc.)
    public List<PlayerData> Players { get; set; } = new();
    
    // Window layout (per-character)
    public WindowSettings? WindowSettings { get; set; }
}

// Export/Import classes
public class BuffExport
{
    public string ExportVersion { get; set; } = "1.0";
    public DateTime ExportedAt { get; set; } = DateTime.Now;
    public List<BuffConfiguration> Buffs { get; set; } = new();
}

public class HealExport
{
    public string ExportVersion { get; set; } = "1.0";
    public DateTime ExportedAt { get; set; } = DateTime.Now;
    public List<HealSpellConfiguration> HealSpells { get; set; } = new();
    public List<HealRule> SelfHealRules { get; set; } = new();
    public List<HealRule> PartyHealRules { get; set; } = new();
    public List<HealRule> PartyWideHealRules { get; set; } = new();
}

public class CureExport
{
    public string ExportVersion { get; set; } = "1.0";
    public DateTime ExportedAt { get; set; } = DateTime.Now;
    public List<AilmentConfiguration> Ailments { get; set; } = new();
    public List<CureSpellConfiguration> CureSpells { get; set; } = new();
}

// Player Database
public enum PlayerRelationship
{
    Neutral,
    Friend,
    Enemy
}

/// <summary>
/// Remote permissions for a player - controls what remote actions they can perform on this client
/// </summary>
public class RemotePermissions
{
    /// <summary>Query this client's experience points</summary>
    public bool QueryExperience { get; set; } = false;
    
    /// <summary>Query this client's health and status</summary>
    public bool QueryHealth { get; set; } = false;
    
    /// <summary>Query this client's current location</summary>
    public bool QueryLocation { get; set; } = false;
    
    /// <summary>Query this client's inventory</summary>
    public bool QueryInventory { get; set; } = false;
    
    /// <summary>Request a party invite from this client</summary>
    public bool RequestInvite { get; set; } = false;
    
    /// <summary>Move this client's character (walk commands)</summary>
    public bool MovePlayer { get; set; } = false;
    
    /// <summary>Execute arbitrary commands on this client</summary>
    public bool ExecuteCommands { get; set; } = false;
    
    /// <summary>Hangup or disconnect this client</summary>
    public bool HangupDisconnect { get; set; } = false;
    
    /// <summary>Alter this client's settings</summary>
    public bool AlterSettings { get; set; } = false;
    
    /// <summary>Divert/redirect conversations to/from this client</summary>
    public bool DivertConversations { get; set; } = false;
    
    /// <summary>
    /// Create a deep copy of this permissions object
    /// </summary>
    public RemotePermissions Clone()
    {
        return new RemotePermissions
        {
            QueryExperience = this.QueryExperience,
            QueryHealth = this.QueryHealth,
            QueryLocation = this.QueryLocation,
            QueryInventory = this.QueryInventory,
            RequestInvite = this.RequestInvite,
            MovePlayer = this.MovePlayer,
            ExecuteCommands = this.ExecuteCommands,
            HangupDisconnect = this.HangupDisconnect,
            AlterSettings = this.AlterSettings,
            DivertConversations = this.DivertConversations
        };
    }
}

public class PlayerData
{
    public string FirstName { get; set; } = string.Empty;  // Unique identifier, never changes
    public string LastName { get; set; } = string.Empty;   // From game, may be empty
    public PlayerRelationship Relationship { get; set; } = PlayerRelationship.Neutral;
    public DateTime LastSeen { get; set; } = DateTime.Now;
    
    /// <summary>Remote permissions - what actions this player is allowed to perform on this client</summary>
    public RemotePermissions AllowedRemotes { get; set; } = new();
    
    /// <summary>Auto-invite this player to party when seen in room</summary>
    public bool InviteToPartyIfSeen { get; set; } = false;
    
    /// <summary>Auto-join this player's party when invited</summary>
    public bool JoinPartyIfInvited { get; set; } = false;
    
    public string FullName => string.IsNullOrEmpty(LastName) ? FirstName : $"{FirstName} {LastName}";
}

public class PlayerDatabase
{
    public List<PlayerData> Players { get; set; } = new();
}

// Monster Database (from CSV)
public class MonsterAttack
{
    public string Name { get; set; } = string.Empty;
    public int Min { get; set; }
    public int Max { get; set; }
    public int Accuracy { get; set; }
}

public class MonsterDrop
{
    public int ItemId { get; set; }
    public int DropPercent { get; set; }
}

public class MonsterData
{
    public int Number { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ArmourClass { get; set; }
    public int DamageResist { get; set; }
    public int MagicRes { get; set; }
    public int BSDefense { get; set; }
    public int EXP { get; set; }
    public int HP { get; set; }
    public double AvgDmg { get; set; }
    public int HPRegen { get; set; }
    public int Type { get; set; }
    public bool Undead { get; set; }
    public int Align { get; set; }
    public bool InGame { get; set; }
    public int Energy { get; set; }
    public int CharmLVL { get; set; }  // Enslave Level
    public int Weapon { get; set; }
    public int FollowPercent { get; set; }
    
    // Attacks (up to 5)
    public List<MonsterAttack> Attacks { get; set; } = new();
    
    // Drops (up to 10)
    public List<MonsterDrop> Drops { get; set; } = new();
}

// Monster user overrides (stored separately from CSV data)
public enum MonsterRelationship
{
    Neutral,
    Friend,
    Enemy
}

public enum AttackPriority
{
    First,
    High,
    Normal,
    Low,
    Last
}

public class MonsterOverride
{
    public int MonsterNumber { get; set; }
    public string CustomName { get; set; } = string.Empty;  // For manually added monsters
    public MonsterRelationship Relationship { get; set; } = MonsterRelationship.Neutral;
    public string PreAttackSpell { get; set; } = string.Empty;
    public int PreAttackSpellMax { get; set; }
    public string AttackSpell { get; set; } = string.Empty;
    public int AttackSpellMax { get; set; }
    public AttackPriority Priority { get; set; } = AttackPriority.Normal;
    public bool NotHostile { get; set; } = false;
}

public class MonsterOverrideDatabase
{
    public List<MonsterOverride> Overrides { get; set; } = new();
}

// Combat Settings (per character)
public class CombatSettings
{
    public string CharacterName { get; set; } = string.Empty;
    
    // Weapon Combat
    public string AttackCommand { get; set; } = string.Empty;
    public string BackstabWeapon { get; set; } = string.Empty;
    public string NormalWeapon { get; set; } = string.Empty;
    public string AlternateWeapon { get; set; } = string.Empty;
    public string Shield { get; set; } = string.Empty;
    public bool UseShieldWithBSWeapon { get; set; } = false;
    public bool UseNormalWeaponForAtkSpells { get; set; } = false;
    
    // Spell Combat - Multi-Attack
    public string MultiAttackSpell { get; set; } = string.Empty;
    public int MultiAttackMinEnemies { get; set; } = 2;
    public int MultiAttackMaxCast { get; set; } = 1;
    public int MultiAttackReqManaPercent { get; set; } = 0;
    
    // Spell Combat - Pre-Attack
    public string PreAttackSpell { get; set; } = string.Empty;
    public int PreAttackMaxCast { get; set; } = 1;
    public int PreAttackReqManaPercent { get; set; } = 0;
    
    // Spell Combat - Attack
    public string AttackSpell { get; set; } = string.Empty;
    public int AttackMaxCast { get; set; } = 1;
    public int AttackReqManaPercent { get; set; } = 0;
    
    // Options
    public bool DoBackstabAttack { get; set; } = false;
    public int MaxMonsters { get; set; } = 99;
    public int RunDistance { get; set; } = 0;
}

public class CombatSettingsDatabase
{
    public List<CombatSettings> Characters { get; set; } = new();
}

/// Window position and size settings, stored per-character profile
public class WindowSettings
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsMaximized { get; set; }
}