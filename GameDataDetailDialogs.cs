namespace MudProxyViewer;

/// <summary>
/// Static lookup for MajorMUD ability IDs to human-readable names.
/// Used by detail dialogs to resolve Abil-N / AbilVal-N column pairs.
/// </summary>
public static class AbilityNames
{
    private static readonly Dictionary<int, string> _names = new()
    {
        { 1, "DamageNoMR" },
        { 2, "AC" },
        { 3, "Rcol" },
        { 4, "MaxDamage" },
        { 5, "Rfir" },
        { 6, "Enslave" },
        { 7, "DR" },
        { 8, "Drain" },
        { 9, "ShadowStealth" },
        { 10, "ACBlur" },
        { 11, "AlterEnergyLevel" },
        { 12, "Summon" },
        { 13, "Illu" },
        { 14, "RoomIllu" },
        { 15, "GypsyFortune" },
        { 16, "Rinaldo" },
        { 17, "DamageWithMR" },
        { 18, "Heal" },
        { 19, "Poison" },
        { 20, "CurePoison" },
        { 21, "ImmuPoison" },
        { 22, "Accuracy" },
        { 23, "AffectsUndead" },
        { 24, "Prev" },
        { 25, "Prgd" },
        { 26, "DetectMagic" },
        { 27, "Stealth" },
        { 28, "Magical" },
        { 29, "Punch" },
        { 30, "Kick" },
        { 31, "Bash" },
        { 32, "Smash" },
        { 33, "KillBlow" },
        { 34, "Dodge" },
        { 35, "JumpKick" },
        { 36, "MagicRes" },
        { 37, "Picklocks" },
        { 38, "Tracking" },
        { 39, "Thievery" },
        { 40, "FindTraps" },
        { 41, "DisarmTraps" },
        { 42, "LearnSpell" },
        { 43, "CastSpell" },
        { 44, "Intel" },
        { 45, "Willpower" },
        { 46, "Strength" },
        { 47, "Health" },
        { 48, "Agility" },
        { 49, "Charm" },
        { 50, "Quest1" },
        { 51, "AntiMagic" },
        { 52, "EvilInCombat" },
        { 53, "BlindingLight" },
        { 54, "TargetIllu" },
        { 55, "AlterLightDuration" },
        { 56, "RechargeItem" },
        { 57, "SeeHidden" },
        { 58, "Crits" },
        { 59, "ClassOK" },
        { 60, "Fear" },
        { 61, "AffectExit" },
        { 62, "AlterEvilChance" },
        { 63, "AlterExperience" },
        { 64, "AddCP" },
        { 65, "Rsto" },
        { 66, "Rlit" },
        { 67, "Quickness" },
        { 68, "Slowness" },
        { 69, "MaxMana" },
        { 70, "SpellCasting" },
        { 71, "Confusion" },
        { 72, "ShockShield" },
        { 73, "DispellMagic" },
        { 74, "HoldPerson" },
        { 75, "Paralyze" },
        { 76, "Mute" },
        { 77, "Perception" },
        { 78, "Animal" },
        { 79, "MageBind" },
        { 80, "AffectsAnimals" },
        { 81, "Freedom" },
        { 82, "Cursed" },
        { 83, "MajorCurse" },
        { 84, "RemoveCurse" },
        { 85, "Shatter" },
        { 86, "Quality" },
        { 87, "Speed" },
        { 88, "MaxHP" },
        { 89, "PunchAv" },
        { 90, "KickAv" },
        { 91, "JumpKAv" },
        { 92, "PunchDmg" },
        { 93, "KickDmg" },
        { 94, "JumpKDmg" },
        { 95, "Slay" },
        { 96, "Encum" },
        { 97, "Good" },
        { 98, "Evil" },
        { 99, "AlterDRByPercent" },
        { 100, "LoyalItem" },
        { 101, "ConfuseMsg" },
        { 102, "RaceStealth" },
        { 103, "ClassStealth" },
        { 104, "DefenseModifier" },
        { 105, "Accuracy2" },
        { 106, "Accuracy3" },
        { 107, "BlindUser" },
        { 108, "AffectsLiving" },
        { 109, "NonLiving" },
        { 110, "NotGood" },
        { 111, "NotEvil" },
        { 112, "Neutral" },
        { 113, "NotNeutral" },
        { 114, "PercentSpell" },
        { 115, "DescMsg" },
        { 116, "BSAccu" },
        { 117, "BSMinDmg" },
        { 118, "BSMaxDmg" },
        { 119, "DelAtMaint" },
        { 120, "StartMsg" },
        { 121, "Recharge" },
        { 122, "RemovesSpell" },
        { 123, "HPRegen" },
        { 124, "NegateAbility" },
        { 125, "IceSorceressQuest" },
        { 126, "GoodQuest" },
        { 127, "NeutralQuest" },
        { 128, "EvilQuest" },
        { 129, "DarkDruidQuest" },
        { 130, "BloodChampQuest" },
        { 131, "SheDragonQuest" },
        { 132, "WereratQuest" },
        { 133, "PhoenixQuest" },
        { 134, "DaoLordQuest" },
        { 135, "MinLevel" },
        { 136, "MaxLevel" },
        { 137, "ShockMsg" },
        { 138, "RoomVisible" },
        { 139, "SpellImmu" },
        { 140, "TeleportRoom" },
        { 141, "TeleportMap" },
        { 142, "HitMagic" },
        { 143, "ClearItem" },
        { 144, "NonMagicalSpell" },
        { 145, "ManaRegen" },
        { 146, "MonsGuards" },
        { 147, "ResistWater" },
        { 148, "Textblock" },
        { 149, "RemoveAtMaint" },
        { 150, "HealMana" },
        { 151, "EndCast" },
        { 152, "Rune" },
        { 153, "KillSpell" },
        { 154, "VisibleAtMaint" },
        { 155, "DeathText" },
        { 156, "QuestItem" },
        { 157, "ScatterItems" },
        { 158, "ReqToHit" },
        { 159, "KaiBind" },
        { 160, "GiveTempSpell" },
        { 161, "OpenDoor" },
        { 162, "Lore" },
        { 163, "SpellComponent" },
        { 164, "CastOnEndPercent" },
        { 165, "AlterSpellDamage" },
        { 166, "AlterSpellLength" },
        { 167, "UnequipItem" },
        { 168, "EquipItem" },
        { 169, "CannotWearLocation" },
        { 170, "Sleep" },
        { 171, "Invisibility" },
        { 172, "SeeInvisible" },
        { 173, "Scry" },
        { 174, "StealMana" },
        { 175, "StealHPToMP" },
        { 176, "StealMPToHP" },
        { 177, "SpellColours" },
        { 178, "ShadowForm" },
        { 179, "FindTrapsValue" },
        { 180, "PicklocksValue" },
        { 181, "GangHouseDeed" },
        { 182, "GangHouseTax" },
        { 183, "GangHouseItem" },
        { 184, "GangShopController" },
        { 185, "NoAttackIfItemNum" },
        { 186, "PerfectStealth" },
        { 187, "Meditate" },
        { 188, "UniquePerPool" },
        { 189, "WitchyBadgeQuest" },
        { 190, "NoStock" },
        { 191, "QuestFlag191" },
        { 192, "QuestFlag192" },
        { 193, "QuestFlag193" },
        { 194, "QuestFlag194" },
        { 195, "QuestFlag195" },
        { 196, "QuestFlag196" },
        { 197, "QuestFlag197" },
        { 198, "QuestFlag198" },
        { 199, "QuestFlag199" },
        { 200, "MandosQuest" },
        { 201, "VolumsQuest" },
        { 202, "CartographersQuest" },
        { 203, "LoremastersQuest" },
        { 204, "GuildmasterQuest" },
        { 205, "DarkbaneQuest" },
        { 206, "GrizzledRanger" },
        { 207, "AmazonHuntress" },
        { 208, "Conquest" },
        { 209, "Conquest2" },
        { 210, "TarlChain" },
        { 211, "MerchantCaptain" },
        { 212, "TrendelQuest" },
        { 213, "LucaProdigio" },
        { 214, "EtherealWatcher" },
        { 215, "KatoQuest" },
        { 216, "QuestFlag216" },
        { 217, "QuestFlag217" },
        { 218, "QuestFlag218" },
        { 219, "QuestFlag219" },
        { 220, "NagaQuest" },
        { 221, "DreadWraith" },
        { 222, "CourtesanQuest" },
        { 223, "QuestFlag223" },
        { 224, "QuestFlag224" },
        { 225, "QuestFlag225" },
        { 226, "QuestFlag226" },
        { 227, "QuestFlag227" },
        { 228, "QuestFlag228" },
        { 229, "QuestFlag229" },
        { 230, "QuestFlag230" },
        { 231, "QuestFlag231" },
        { 232, "QuestFlag232" },
        { 233, "QuestFlag233" },
        { 234, "QuestFlag234" },
        { 235, "QuestFlag235" },
        { 236, "QuestFlag236" },
        { 237, "QuestFlag237" },
        { 238, "QuestFlag238" },
        { 239, "QuestFlag239" },
        { 240, "QuestFlag240" },
        { 1001, "GrantThievery" },
        { 1002, "GrantTraps" },
        { 1003, "GrantPicklocks" },
        { 1004, "GrantTracking" },
        { 1103, "ShadowHome" }
    };

    public static string? GetName(int abilityId)
    {
        return _names.TryGetValue(abilityId, out var name) ? name : null;
    }

    public static List<(string Name, string Value)> ResolveAbilities(Dictionary<string, object?> data)
    {
        var result = new List<(string Name, string Value)>();

        for (int i = 0; i < 100; i++)
        {
            var abilKey = $"Abil-{i}";
            if (!data.TryGetValue(abilKey, out var abilObj))
                continue;

            int abilId = 0;
            if (abilObj is long l) abilId = (int)l;
            else if (abilObj is int ii) abilId = ii;
            else if (abilObj is decimal d) abilId = (int)d;
            else if (abilObj != null) int.TryParse(abilObj.ToString(), out abilId);

            if (abilId == 0)
                continue;

            var valKey = $"AbilVal-{i}";
            string value = "0";
            if (data.TryGetValue(valKey, out var valObj) && valObj != null)
                value = valObj.ToString() ?? "0";

            var name = GetName(abilId) ?? $"Unknown({abilId})";
            result.Add((name, value));
        }

        return result;
    }

    public static bool IsAbilityColumn(string key)
    {
        return key.StartsWith("Abil-", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("AbilVal-", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Generic detail dialog for viewing any game data row as key-value pairs.
/// </summary>
public class GenericDetailDialog : Form
{
    private readonly Dictionary<string, object?> _data;
    private readonly string _title;
    
    public GenericDetailDialog(Dictionary<string, object?> data, string title)
    {
        _data = data;
        _title = title;
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        this.Text = _title;
        this.Size = new Size(600, 500);
        this.MinimumSize = new Size(500, 400);
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(15)
        };
        
        int row = 0;
        
        foreach (var kvp in _data)
        {
            if (kvp.Value == null) continue;
            if (AbilityNames.IsAbilityColumn(kvp.Key)) continue;
            
            var label = new Label
            {
                Text = $"{kvp.Key}:",
                Location = new Point(15, 15 + (row * 28)),
                Size = new Size(150, 20),
                ForeColor = Color.LightGray,
                Font = new Font("Segoe UI", 9)
            };
            contentPanel.Controls.Add(label);
            
            var valueBox = new TextBox
            {
                Text = kvp.Value.ToString(),
                Location = new Point(170, 12 + (row * 28)),
                Size = new Size(380, 23),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = true
            };
            contentPanel.Controls.Add(valueBox);
            row++;
        }
        
        var abilities = AbilityNames.ResolveAbilities(_data);
        if (abilities.Count > 0)
        {
            row++;
            var sectionLabel = new Label
            {
                Text = "── Abilities ──",
                Location = new Point(15, 15 + (row * 28)),
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            contentPanel.Controls.Add(sectionLabel);
            row++;
            
            foreach (var (name, value) in abilities)
            {
                var abilLabel = new Label
                {
                    Text = $"{name}:",
                    Location = new Point(15, 15 + (row * 28)),
                    Size = new Size(180, 20),
                    ForeColor = Color.LightGray,
                    Font = new Font("Segoe UI", 9)
                };
                contentPanel.Controls.Add(abilLabel);
                
                var abilValue = new TextBox
                {
                    Text = value,
                    Location = new Point(200, 12 + (row * 28)),
                    Size = new Size(100, 23),
                    BackColor = Color.FromArgb(50, 50, 50),
                    ForeColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle,
                    ReadOnly = true
                };
                contentPanel.Controls.Add(abilValue);
                row++;
            }
        }
        
        this.Controls.Add(contentPanel);
        
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = Color.FromArgb(40, 40, 40)
        };
        
        var closeButton = new Button
        {
            Text = "Close",
            Width = 80,
            Height = 30,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        closeButton.Location = new Point(buttonPanel.Width - 95, 10);
        buttonPanel.Controls.Add(closeButton);
        
        this.Controls.Add(buttonPanel);
        this.AcceptButton = closeButton;
    }
}

/// <summary>
/// Detail dialog for viewing a single Race
/// </summary>
public class RaceDetailDialog : Form
{
    private readonly Dictionary<string, object?> _data;
    
    private static readonly string[] AttributeFields = { "STR", "INT", "WIL", "AGL", "HEA", "CHM" };
    private static readonly string[] AttributeLabels = { "Strength", "Intellect", "Willpower", "Agility", "Health", "Charm" };
    
    private static readonly HashSet<string> DetailFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Number", "Name", "HPPerLVL", "ExpTable",
        "mSTR", "xSTR", "mINT", "xINT", "mWIL", "xWIL",
        "mAGL", "xAGL", "mHEA", "xHEA", "mCHM", "xCHM"
    };
    
    public RaceDetailDialog(Dictionary<string, object?> data)
    {
        _data = data;
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        var name = _data.GetValueOrDefault("Name")?.ToString() ?? "Unknown";
        
        this.Text = $"Race Details - {name}";
        this.Size = new Size(600, 510);
        this.MinimumSize = new Size(500, 510);
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(15)
        };
        
        var leftColumn = new Panel
        {
            Location = new Point(15, 15),
            Size = new Size(260, 400)
        };
        
        var detailsSection = CreateSection("Race Details", 0, 0, 260, 155);
        AddDetailField(detailsSection, "Number", GetValue("Number"), 0);
        AddDetailField(detailsSection, "Name", GetValue("Name"), 1);
        AddDetailField(detailsSection, "Bonus HP", GetValue("HPPerLVL"), 2);
        AddDetailField(detailsSection, "Experience", GetValue("ExpTable") + "%", 3);
        leftColumn.Controls.Add(detailsSection);
        
        var attrSection = CreateSection("Attributes (CPs)", 0, 165, 260, 220);
        AddAttributeHeader(attrSection);
        for (int i = 0; i < AttributeFields.Length; i++)
        {
            var field = AttributeFields[i];
            var minVal = GetValue($"m{field}");
            var maxVal = GetValue($"x{field}");
            AddAttributeRow(attrSection, AttributeLabels[i], minVal, maxVal, i + 1);
        }
        leftColumn.Controls.Add(attrSection);
        
        contentPanel.Controls.Add(leftColumn);
        
        var abilitiesSection = CreateSection("Abilities", 285, 15, 280, 400);
        var abilitiesContent = GetContentPanel(abilitiesSection);
        
        if (abilitiesContent != null)
        {
            var abilities = AbilityNames.ResolveAbilities(_data);
            
            var miscFields = new List<(string Name, string Value)>();
            foreach (var kvp in _data)
            {
                if (kvp.Value == null) continue;
                if (DetailFields.Contains(kvp.Key)) continue;
                if (AbilityNames.IsAbilityColumn(kvp.Key)) continue;
                miscFields.Add((kvp.Key, kvp.Value.ToString() ?? ""));
            }
            
            int row = 0;
            
            foreach (var (fieldName, fieldValue) in miscFields)
            {
                var label = new Label
                {
                    Text = $"{fieldName}: {fieldValue}",
                    Location = new Point(5, 5 + (row * 22)),
                    AutoSize = true,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9)
                };
                abilitiesContent.Controls.Add(label);
                row++;
            }
            
            foreach (var (abilName, abilValue) in abilities)
            {
                var nameLabel = new Label
                {
                    Text = $"{abilName}:",
                    Location = new Point(5, 5 + (row * 22)),
                    Size = new Size(160, 18),
                    ForeColor = Color.LightGray,
                    Font = new Font("Segoe UI", 9)
                };
                abilitiesContent.Controls.Add(nameLabel);
                
                var valueLabel = new Label
                {
                    Text = abilValue,
                    Location = new Point(170, 5 + (row * 22)),
                    AutoSize = true,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9)
                };
                abilitiesContent.Controls.Add(valueLabel);
                row++;
            }
            
            if (abilities.Count == 0 && miscFields.Count == 0)
            {
                var noneLabel = new Label
                {
                    Text = "(None)",
                    Location = new Point(5, 5),
                    AutoSize = true,
                    ForeColor = Color.Gray,
                    Font = new Font("Segoe UI", 9)
                };
                abilitiesContent.Controls.Add(noneLabel);
            }
        }
        
        contentPanel.Controls.Add(abilitiesSection);
        
        this.Controls.Add(contentPanel);
        
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = Color.FromArgb(40, 40, 40)
        };
        
        var closeButton = new Button
        {
            Text = "Close",
            Width = 80,
            Height = 30,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        closeButton.Location = new Point(buttonPanel.Width - 95, 10);
        buttonPanel.Controls.Add(closeButton);
        
        this.Controls.Add(buttonPanel);
        this.AcceptButton = closeButton;
    }
    
    private string GetValue(string key)
    {
        if (_data.TryGetValue(key, out var val) && val != null)
            return val.ToString() ?? "";
        return "";
    }
    
    private Panel CreateSection(string title, int x, int y, int width, int height)
    {
        var section = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(width, height),
            BackColor = Color.FromArgb(35, 35, 35),
            BorderStyle = BorderStyle.FixedSingle
        };
        
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 25,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)
        };
        section.Controls.Add(titleLabel);
        
        var contentPanel = new Panel
        {
            Name = "ContentPanel",
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            AutoScroll = true
        };
        section.Controls.Add(contentPanel);
        contentPanel.BringToFront();
        
        return section;
    }
    
    private Panel? GetContentPanel(Panel section)
    {
        foreach (Control ctrl in section.Controls)
        {
            if (ctrl is Panel panel && panel.Name == "ContentPanel")
                return panel;
        }
        foreach (Control ctrl in section.Controls)
        {
            if (ctrl is Panel panel && panel.Dock == DockStyle.Fill)
                return panel;
        }
        return null;
    }
    
    private void AddDetailField(Panel section, string label, string value, int row)
    {
        var contentPanel = GetContentPanel(section);
        if (contentPanel == null) return;
        
        int yPos = 8 + (row * 28);
        
        var labelCtrl = new Label
        {
            Text = label,
            Location = new Point(5, yPos + 3),
            Size = new Size(80, 20),
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 9)
        };
        contentPanel.Controls.Add(labelCtrl);
        
        var valueCtrl = new TextBox
        {
            Text = value,
            Location = new Point(90, yPos),
            Size = new Size(150, 23),
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true
        };
        contentPanel.Controls.Add(valueCtrl);
    }
    
    private void AddAttributeHeader(Panel section)
    {
        var contentPanel = GetContentPanel(section);
        if (contentPanel == null) return;
        
        var minLabel = new Label
        {
            Text = "Min",
            Location = new Point(95, 8),
            Size = new Size(50, 20),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };
        contentPanel.Controls.Add(minLabel);
        
        var maxLabel = new Label
        {
            Text = "Max",
            Location = new Point(155, 8),
            Size = new Size(50, 20),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };
        contentPanel.Controls.Add(maxLabel);
    }
    
    private void AddAttributeRow(Panel section, string label, string minVal, string maxVal, int row)
    {
        var contentPanel = GetContentPanel(section);
        if (contentPanel == null) return;
        
        int yPos = 8 + (row * 26);
        
        var labelCtrl = new Label
        {
            Text = label,
            Location = new Point(5, yPos + 3),
            Size = new Size(80, 20),
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 9)
        };
        contentPanel.Controls.Add(labelCtrl);
        
        var minCtrl = new TextBox
        {
            Text = minVal,
            Location = new Point(95, yPos),
            Size = new Size(50, 23),
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true,
            TextAlign = HorizontalAlignment.Center
        };
        contentPanel.Controls.Add(minCtrl);
        
        var maxCtrl = new TextBox
        {
            Text = maxVal,
            Location = new Point(155, yPos),
            Size = new Size(50, 23),
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true,
            TextAlign = HorizontalAlignment.Center
        };
        contentPanel.Controls.Add(maxCtrl);
    }
}

/// <summary>
/// Detail dialog for viewing a single Class.
/// </summary>
public class ClassDetailDialog : Form
{
    private readonly Dictionary<string, object?> _data;
    
    private static readonly HashSet<string> DetailFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Number", "Name", "ExpTable", "CombatLVL", "MinHits", "MaxHits",
        "WeaponType", "ArmourType", "MageryType", "MageryLVL"
    };
    
    public ClassDetailDialog(Dictionary<string, object?> data)
    {
        _data = data;
        InitializeComponent();
    }
    
    private string GetValue(string key)
    {
        if (_data.TryGetValue(key, out var val) && val != null)
            return val.ToString() ?? "";
        return "";
    }
    
    private void InitializeComponent()
    {
        var name = _data.GetValueOrDefault("Name")?.ToString() ?? "Unknown";
        
        this.Text = $"Class Details - {name}";
        this.Size = new Size(600, 370);
        this.MinimumSize = new Size(600, 370);
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(15)
        };
        
        var detailsSection = CreateSection("Class Details", 15, 15, 260, 265);
        var detailsContent = GetContentPanel(detailsSection);
        
        if (detailsContent != null)
        {
            var mageryType = GetValue("MageryType");
            var mageryLvl = GetValue("MageryLVL");
            var magicDisplay = $"{mageryType}-{mageryLvl}";
            if (string.IsNullOrEmpty(mageryType) && string.IsNullOrEmpty(mageryLvl))
                magicDisplay = "";
            
            var minHits = GetValue("MinHits");
            var maxHits = GetValue("MaxHits");
            var hpDisplay = $"{minHits} - {maxHits}";
            
            var fields = new (string Label, string Value)[]
            {
                ("Number", GetValue("Number")),
                ("Name", GetValue("Name")),
                ("Experience", GetValue("ExpTable") + "%"),
                ("Combat", GetValue("CombatLVL")),
                ("HPs/Level", hpDisplay),
                ("Weapons", GetValue("WeaponType")),
                ("Armour", GetValue("ArmourType")),
                ("Magic", magicDisplay)
            };
            
            for (int i = 0; i < fields.Length; i++)
            {
                int yPos = 8 + (i * 26);
                
                var labelCtrl = new Label
                {
                    Text = fields[i].Label,
                    Location = new Point(5, yPos + 2),
                    Size = new Size(80, 18),
                    ForeColor = Color.LightGray,
                    Font = new Font("Segoe UI", 9)
                };
                detailsContent.Controls.Add(labelCtrl);
                
                var valueCtrl = new Label
                {
                    Text = fields[i].Value,
                    Location = new Point(90, yPos + 2),
                    AutoSize = true,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9)
                };
                detailsContent.Controls.Add(valueCtrl);
            }
        }
        
        contentPanel.Controls.Add(detailsSection);
        
        var abilitiesSection = CreateSection("Abilities", 285, 15, 280, 265);
        var abilitiesContent = GetContentPanel(abilitiesSection);
        
        if (abilitiesContent != null)
        {
            var abilities = AbilityNames.ResolveAbilities(_data);
            
            var miscFields = new List<(string Name, string Value)>();
            foreach (var kvp in _data)
            {
                if (kvp.Value == null) continue;
                if (DetailFields.Contains(kvp.Key)) continue;
                if (AbilityNames.IsAbilityColumn(kvp.Key)) continue;
                miscFields.Add((kvp.Key, kvp.Value.ToString() ?? ""));
            }
            
            int row = 0;
            
            foreach (var (fieldName, fieldValue) in miscFields)
            {
                var label = new Label
                {
                    Text = $"{fieldName}: {fieldValue}",
                    Location = new Point(5, 5 + (row * 22)),
                    AutoSize = true,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9)
                };
                abilitiesContent.Controls.Add(label);
                row++;
            }
            
            foreach (var (abilName, abilValue) in abilities)
            {
                var nameLabel = new Label
                {
                    Text = $"{abilName}:",
                    Location = new Point(5, 5 + (row * 22)),
                    Size = new Size(160, 18),
                    ForeColor = Color.LightGray,
                    Font = new Font("Segoe UI", 9)
                };
                abilitiesContent.Controls.Add(nameLabel);
                
                var valueLabel = new Label
                {
                    Text = abilValue,
                    Location = new Point(170, 5 + (row * 22)),
                    AutoSize = true,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9)
                };
                abilitiesContent.Controls.Add(valueLabel);
                row++;
            }
            
            if (abilities.Count == 0 && miscFields.Count == 0)
            {
                var noneLabel = new Label
                {
                    Text = "(None)",
                    Location = new Point(5, 5),
                    AutoSize = true,
                    ForeColor = Color.Gray,
                    Font = new Font("Segoe UI", 9)
                };
                abilitiesContent.Controls.Add(noneLabel);
            }
        }
        
        contentPanel.Controls.Add(abilitiesSection);
        
        this.Controls.Add(contentPanel);
        
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = Color.FromArgb(40, 40, 40)
        };
        
        var closeButton = new Button
        {
            Text = "Close",
            Width = 80,
            Height = 30,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        closeButton.Location = new Point(buttonPanel.Width - 95, 10);
        buttonPanel.Controls.Add(closeButton);
        
        this.Controls.Add(buttonPanel);
        this.AcceptButton = closeButton;
    }
    
    private Panel CreateSection(string title, int x, int y, int width, int height)
    {
        var section = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(width, height),
            BackColor = Color.FromArgb(35, 35, 35),
            BorderStyle = BorderStyle.FixedSingle
        };
        
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 25,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)
        };
        section.Controls.Add(titleLabel);
        
        var contentPanel = new Panel
        {
            Name = "ContentPanel",
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            AutoScroll = true
        };
        section.Controls.Add(contentPanel);
        contentPanel.BringToFront();
        
        return section;
    }
    
    private Panel? GetContentPanel(Panel section)
    {
        foreach (Control ctrl in section.Controls)
        {
            if (ctrl is Panel panel && panel.Name == "ContentPanel")
                return panel;
        }
        foreach (Control ctrl in section.Controls)
        {
            if (ctrl is Panel panel && panel.Dock == DockStyle.Fill)
                return panel;
        }
        return null;
    }
}

/// <summary>
/// Detail dialog for viewing a single Item.
/// Left: Item info, Options (placeholder checkboxes), Details
/// Right: Other Info (remaining fields + resolved abilities)
/// </summary>
public class ItemDetailDialog : Form
{
    private readonly Dictionary<string, object?> _data;
    
    // Fields shown in the structured left-side sections (excluded from Other Info)
    private static readonly HashSet<string> DetailFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Number", "Name", "Encum", "Price", "Currency", "ItemType", "Obtained"
    };
    
    public ItemDetailDialog(Dictionary<string, object?> data)
    {
        _data = data;
        InitializeComponent();
    }
    
    private string GetValue(string key)
    {
        if (_data.TryGetValue(key, out var val) && val != null)
            return val.ToString() ?? "";
        return "";
    }
    
    private void InitializeComponent()
    {
        var name = _data.GetValueOrDefault("Name")?.ToString() ?? "Unknown";
        
        this.Text = $"Item Details - {name}";
        this.Size = new Size(640, 530);
        this.MinimumSize = new Size(640, 530);
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(15)
        };
        
        // ════════════════════════════════════════
        // LEFT SIDE — stacked sections
        // ════════════════════════════════════════
        int leftWidth = 295;
        int leftX = 15;
        
        // ── Item section ──
        var itemSection = CreateSection("Item", leftX, 15, leftWidth, 80);
        var itemContent = GetContentPanel(itemSection);
        if (itemContent != null)
        {
            AddLabelPair(itemContent, "Number", GetValue("Number"), 0);
            AddLabelPair(itemContent, "Name", GetValue("Name"), 1);
        }
        contentPanel.Controls.Add(itemSection);
        
        // ── Options section (placeholder — all disabled) ──
        var optionsSection = CreateSection("Options", leftX, 103, leftWidth, 195);
        var optionsContent = GetContentPanel(optionsSection);
        if (optionsContent != null)
        {
            // Left column checkboxes
            string[] leftChecks = { "Auto-collect", "Auto-discard", "Auto-equip", "Auto-find", "Auto-open", "Auto-buy", "Auto-sell" };
            for (int i = 0; i < leftChecks.Length; i++)
            {
                var cb = new CheckBox
                {
                    Text = leftChecks[i],
                    Location = new Point(5, 5 + (i * 22)),
                    AutoSize = true,
                    ForeColor = Color.LightGray,
                    Font = new Font("Segoe UI", 8.5f),
                    Enabled = false,
                    Checked = false
                };
                optionsContent.Controls.Add(cb);
            }
            
            // Right column checkboxes
            string[] rightChecks = { "Cannot be taken", "Can use to backstab", "Must have minimum", "Loyal item" };
            for (int i = 0; i < rightChecks.Length; i++)
            {
                var cb = new CheckBox
                {
                    Text = rightChecks[i],
                    Location = new Point(145, 5 + (i * 22)),
                    AutoSize = true,
                    ForeColor = Color.LightGray,
                    Font = new Font("Segoe UI", 8.5f),
                    Enabled = false,
                    Checked = false
                };
                optionsContent.Controls.Add(cb);
            }
        }
        contentPanel.Controls.Add(optionsSection);
        
        // ── Details section ──
        var detailsSection = CreateSection("Details", leftX, 306, leftWidth, 155);
        var detailsContent = GetContentPanel(detailsSection);
        if (detailsContent != null)
        {
            // Row 0: Min. to keep [____]    Weight [Encum]
            AddLabelPair(detailsContent, "Min. to keep", "", 0, 85);
            AddLabelPair(detailsContent, "Weight", GetValue("Encum"), 0, 85, 150);
            
            // Row 1: Max to get [____]    Price [Price Currency]
            AddLabelPair(detailsContent, "Max to get", "", 1, 85);
            var priceVal = GetValue("Price");
            var currVal = GetValue("Currency");
            var priceDisplay = string.IsNullOrEmpty(currVal) ? priceVal : $"{priceVal} {currVal}";
            AddLabelPair(detailsContent, "Price", priceDisplay, 1, 85, 150);
            
            // Row 2: Item type [ItemType]
            AddLabelPair(detailsContent, "Item type", GetValue("ItemType"), 2, 85);
            
            // Row 3: If needed, do [____]
            AddLabelPair(detailsContent, "If needed, do", "", 3, 85);
            
            // Row 4: Bought/sold [Shop name resolved from Obtained]
            var obtainedVal = GetValue("Obtained");
            var shopDisplay = ResolveShopName(obtainedVal);
            AddLabelPair(detailsContent, "Bought/sold", shopDisplay, 4, 85);
        }
        contentPanel.Controls.Add(detailsSection);
        
        // ════════════════════════════════════════
        // RIGHT SIDE — Other Info
        // ════════════════════════════════════════
        var otherInfoSection = CreateSection("Other Info", 320, 15, 290, 446);
        var otherInfoContent = GetContentPanel(otherInfoSection);
        if (otherInfoContent != null)
        {
            var abilities = AbilityNames.ResolveAbilities(_data);
            
            // Collect non-detail, non-ability fields
            var miscFields = new List<(string Name, string Value)>();
            foreach (var kvp in _data)
            {
                if (kvp.Value == null) continue;
                if (DetailFields.Contains(kvp.Key)) continue;
                if (AbilityNames.IsAbilityColumn(kvp.Key)) continue;
                miscFields.Add((kvp.Key, kvp.Value.ToString() ?? ""));
            }
            
            int row = 0;
            
            foreach (var (fieldName, fieldValue) in miscFields)
            {
                var fLabel = new Label
                {
                    Text = $"{fieldName}:",
                    Location = new Point(5, 5 + (row * 22)),
                    Size = new Size(160, 18),
                    ForeColor = Color.LightGray,
                    Font = new Font("Segoe UI", 9)
                };
                otherInfoContent.Controls.Add(fLabel);
                
                var fValue = new Label
                {
                    Text = fieldValue,
                    Location = new Point(170, 5 + (row * 22)),
                    AutoSize = true,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9)
                };
                otherInfoContent.Controls.Add(fValue);
                row++;
            }
            
            // Resolved abilities
            foreach (var (abilName, abilValue) in abilities)
            {
                var nameLabel = new Label
                {
                    Text = $"{abilName}:",
                    Location = new Point(5, 5 + (row * 22)),
                    Size = new Size(160, 18),
                    ForeColor = Color.LightGray,
                    Font = new Font("Segoe UI", 9)
                };
                otherInfoContent.Controls.Add(nameLabel);
                
                var valueLabel = new Label
                {
                    Text = abilValue,
                    Location = new Point(170, 5 + (row * 22)),
                    AutoSize = true,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9)
                };
                otherInfoContent.Controls.Add(valueLabel);
                row++;
            }
            
            if (abilities.Count == 0 && miscFields.Count == 0)
            {
                var noneLabel = new Label
                {
                    Text = "(None)",
                    Location = new Point(5, 5),
                    AutoSize = true,
                    ForeColor = Color.Gray,
                    Font = new Font("Segoe UI", 9)
                };
                otherInfoContent.Controls.Add(noneLabel);
            }
        }
        contentPanel.Controls.Add(otherInfoSection);
        
        this.Controls.Add(contentPanel);
        
        // Button panel
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = Color.FromArgb(40, 40, 40)
        };
        
        var closeButton = new Button
        {
            Text = "Close",
            Width = 80,
            Height = 30,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        closeButton.Location = new Point(buttonPanel.Width - 95, 10);
        buttonPanel.Controls.Add(closeButton);
        
        this.Controls.Add(buttonPanel);
        this.AcceptButton = closeButton;
    }
    
    /// <summary>
    /// Resolve a shop number to its name via GameDataCache.
    /// Returns "ShopName (#N)" if found, or the raw value if not.
    /// </summary>
    private static string ResolveShopName(string obtainedValue)
    {
        if (string.IsNullOrEmpty(obtainedValue) || obtainedValue == "0")
            return "";
        
        if (!int.TryParse(obtainedValue, out var shopNumber))
            return obtainedValue;
        
        var shops = GameDataCache.Instance.GetTable("Shops");
        if (shops != null)
        {
            var shop = shops.FirstOrDefault(s =>
                s.TryGetValue("Number", out var n) &&
                n != null && Convert.ToInt64(n) == shopNumber);
            
            if (shop != null && shop.TryGetValue("Name", out var shopName) && shopName != null)
            {
                return $"{shopName} (#{shopNumber})";
            }
        }
        
        return $"Shop #{shopNumber}";
    }
    
    /// <summary>
    /// Add a label + value pair at a given row, with optional x offset for side-by-side layout.
    /// </summary>
    private static void AddLabelPair(Panel content, string label, string value, int row, int labelWidth = 80, int xOffset = 0)
    {
        int yPos = 5 + (row * 24);
        
        var labelCtrl = new Label
        {
            Text = label,
            Location = new Point(xOffset + 5, yPos),
            Size = new Size(labelWidth, 18),
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 9)
        };
        content.Controls.Add(labelCtrl);
        
        var valueCtrl = new Label
        {
            Text = value,
            Location = new Point(xOffset + labelWidth + 8, yPos),
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9)
        };
        content.Controls.Add(valueCtrl);
    }
    
    private Panel CreateSection(string title, int x, int y, int width, int height)
    {
        var section = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(width, height),
            BackColor = Color.FromArgb(35, 35, 35),
            BorderStyle = BorderStyle.FixedSingle
        };
        
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 25,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)
        };
        section.Controls.Add(titleLabel);
        
        var contentPanel = new Panel
        {
            Name = "ContentPanel",
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            AutoScroll = true
        };
        section.Controls.Add(contentPanel);
        contentPanel.BringToFront();
        
        return section;
    }
    
    private Panel? GetContentPanel(Panel section)
    {
        foreach (Control ctrl in section.Controls)
        {
            if (ctrl is Panel panel && panel.Name == "ContentPanel")
                return panel;
        }
        foreach (Control ctrl in section.Controls)
        {
            if (ctrl is Panel panel && panel.Dock == DockStyle.Fill)
                return panel;
        }
        return null;
    }
}
