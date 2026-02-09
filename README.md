# MUD Proxy Viewer - Knowledge Base

> **Version:** 1.0.0  
> **Last Updated:** February 2026  
> **Purpose:** Combat automation client for MajorMUD, replacing the deprecated MegaMUD client  
> **Platform:** Windows (.NET 8.0 WinForms)  
> **Status:** Active Development

---

## Table of Contents

1. [Game Overview](#game-overview)
2. [Application Architecture](#application-architecture)
3. [UI Styling Guidelines](#ui-styling-guidelines)
4. [Core Components](#core-components)
5. [Data Models](#data-models)
6. [Combat System](#combat-system)
7. [Message Parsing](#message-parsing)
8. [Game Data System](#game-data-system)
9. [Configuration & Persistence](#configuration--persistence)
10. [Character Profiles](#character-profiles)
11. [UI Components](#ui-components)
12. [Code Organization](#code-organization)
13. [Development Guidelines](#development-guidelines)
14. [Known Patterns & Solutions](#known-patterns--solutions)
15. [Future Plans](#future-plans)

---

## Game Overview

### MajorMUD

MajorMUD is a text-based Multi-User Dungeon (MUD) game accessed via telnet. The specific version being played is **ParaMUD** (also referred to as GreaterMUD). At its core, it is MajorMUD.

### MegaMUD Client (Legacy)

MegaMUD was the traditional client used to play MajorMUD. It is **very old and deprecated software**. This application **completely replaces MegaMUD** with a modern, direct telnet client.

### Network Architecture

```
┌─────────────────┐                    ┌─────────────────┐
│  MUD Proxy      │───── Telnet ──────▶│  MajorMUD       │
│  Viewer         │◀────────────────────│  Server (BBS)   │
│  (This App)     │                    │                 │
└─────────────────┘                    └─────────────────┘
   Direct Telnet                         Server IP:23
   Connection                            (configurable)
```

- **Default Telnet Port:** 23
- **Server Address:** Configured per character profile (IP or hostname)
- **Direct Mode:** Connects directly to server (no proxy mode)
- **ANSI Support:** Full ANSI color code rendering
- **IAC Handling:** Telnet protocol negotiation handled automatically

---

## Application Architecture

### Solution Structure

```
MudProxyViewer/
├── MainForm.cs                    # Main UI, telnet connection, message routing
├── BuffManager.cs                 # Central hub: buffs, party, settings, profiles
├── CombatManager.cs               # Combat automation, enemy detection, attacks
├── HealingManager.cs              # Heal spell management, HP monitoring
├── CureManager.cs                 # Ailment detection and cure automation
├── PlayerDatabaseManager.cs       # Player tracking (friends/enemies)
├── MonsterDatabaseManager.cs      # Monster data, CSV parsing, overrides
├── Models.cs                      # All data models and enums
├── SettingsDialog.cs              # Settings UI (tabbed configuration)
├── GameDataViewerDialog.cs        # Generic game data list viewer
├── GameDataCache.cs               # Singleton cache for loaded JSON data
├── MonsterDatabaseDialog.cs       # Monster-specific list with overrides
│
├── GameData/                      # Game data dialogs (Option A structure)
│   ├── AbilityNames.cs            # Ability ID → name lookup
│   ├── GenericDetailDialog.cs     # Fallback detail dialog
│   ├── RaceDialogs.cs             # RaceViewerConfig + RaceDetailDialog
│   ├── ClassDialogs.cs            # ClassViewerConfig + ClassDetailDialog
│   ├── ItemDialogs.cs             # ItemViewerConfig + ItemDetailDialog
│   ├── SpellDialogs.cs            # SpellViewerConfig + SpellDetailDialog (placeholder)
│   ├── MonsterDialogs.cs          # MonsterViewerConfig (stub - see MonsterDatabaseDialog)
│   ├── RoomDialogs.cs             # RoomViewerConfig + RoomDetailDialog (placeholder)
│   ├── ShopDialogs.cs             # ShopViewerConfig + ShopDetailDialog (placeholder)
│   ├── LairDialogs.cs             # LairViewerConfig + LairDetailDialog (placeholder)
│   └── TextBlockDialogs.cs        # TextBlockViewerConfig + TextBlockDetailDialog (placeholder)
│
└── MudProxyViewer.csproj          # .NET 8.0 Windows Forms project
```

### Component Relationships

```
MainForm (Entry Point)
    │
    ├── BuffManager (Central Hub)
    │       ├── HealingManager
    │       ├── CureManager
    │       ├── PlayerDatabaseManager
    │       ├── MonsterDatabaseManager
    │       └── CombatManager
    │
    ├── TcpClient (Direct Telnet Connection)
    │
    └── GameDataCache (Singleton)
            └── Game JSON files (Races, Classes, Items, etc.)
```

### Event Flow

1. **MUD Server → App:** Message received via telnet
2. **MainForm:** Parses message, renders ANSI, routes to managers
3. **Managers:** Process message, update state, trigger actions
4. **Automation:** Managers send commands back via telnet
5. **UI Updates:** Status panels, logs, and indicators refresh

---

## UI Styling Guidelines

### Color Palette

| Element | RGB Value | Usage |
|---------|-----------|-------|
| **Background (Main)** | `30, 30, 30` | Form background |
| **Background (Panels)** | `45, 45, 45` | Dialogs, panels, menus |
| **Background (Sections)** | `40, 40, 40` | Grouped sections within tabs |
| **Background (Inputs)** | `50, 50, 50` - `60, 60, 60` | TextBox, ListView, grids |
| **Background (Dark)** | `35, 35, 35` | Section panels with borders |
| **Background (Darker)** | `20, 20, 20` | Terminal/log backgrounds |
| **Foreground (Primary)** | `White` | Primary text |
| **Foreground (Secondary)** | `LightGray` | Labels, secondary text |
| **Foreground (Dimmed)** | `Gray` | Disabled, placeholder text |
| **Accent (Green)** | `0, 100, 0` | Save buttons |
| **Accent (Hover)** | `70, 70, 70` | Menu hover state |

### Standard Control Styling

```csharp
// TextBox
var textBox = new TextBox
{
    BackColor = Color.FromArgb(60, 60, 60),
    ForeColor = Color.White,
    BorderStyle = BorderStyle.FixedSingle,
    Font = new Font("Segoe UI", 9)
};

// Button (Standard)
var button = new Button
{
    BackColor = Color.FromArgb(60, 60, 60),
    ForeColor = Color.White,
    FlatStyle = FlatStyle.Flat,
    Font = new Font("Segoe UI", 9)
};

// Button (Save/Primary)
var saveButton = new Button
{
    BackColor = Color.FromArgb(0, 100, 0),
    ForeColor = Color.White,
    FlatStyle = FlatStyle.Flat
};

// Label
var label = new Label
{
    ForeColor = Color.White,  // or Color.LightGray for secondary
    Font = new Font("Segoe UI", 9),
    BackColor = Color.Transparent
};

// CheckBox (read-only appearance but interactive prevention)
var checkbox = new CheckBox
{
    ForeColor = Color.White,
    BackColor = Color.Transparent,
    Font = new Font("Segoe UI", 8.5f)
};
// To make read-only without graying out:
checkbox.Click += (s, e) => { if (s is CheckBox cb) cb.Checked = !cb.Checked; }; // revert

// NumericUpDown
var numeric = new NumericUpDown
{
    BackColor = Color.FromArgb(60, 60, 60),
    ForeColor = Color.White
};

// ListView/DataGridView
var listView = new ListView
{
    BackColor = Color.FromArgb(50, 50, 50),
    ForeColor = Color.White,
    GridLines = true,
    FullRowSelect = true
};

// Panel (Section)
var section = new Panel
{
    BackColor = Color.FromArgb(35, 35, 35),
    BorderStyle = BorderStyle.FixedSingle
};

// Section Title Bar
var titleLabel = new Label
{
    Dock = DockStyle.Top,
    Height = 25,
    BackColor = Color.FromArgb(50, 50, 50),
    ForeColor = Color.White,
    Font = new Font("Segoe UI", 9, FontStyle.Bold),
    TextAlign = ContentAlignment.MiddleLeft,
    Padding = new Padding(8, 0, 0, 0)
};
```

### Menu Styling (Dark Theme)

```csharp
// Custom renderer for dark menus
public class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColorTable()) { }
    
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected)
        {
            using var brush = new SolidBrush(Color.FromArgb(70, 70, 70));
            e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
        }
        else
        {
            using var brush = new SolidBrush(Color.FromArgb(45, 45, 45));
            e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
        }
    }
}

// Apply to MenuStrip
menuStrip.Renderer = new DarkMenuRenderer();
```

### Status Label Colors

| State | Color | Example |
|-------|-------|---------|
| Connected | `LimeGreen` | "Connected" |
| Disconnected | `White` | "Disconnected" |
| Connecting | `Yellow` | "Connecting..." |
| Error | `Red` | "Connection Failed" |
| In Combat | `Red` | Combat state |
| Idle | `LimeGreen` | Not in combat |

### Dialog Patterns

```csharp
// Standard dialog setup
this.Text = "Dialog Title";
this.Size = new Size(600, 500);
this.FormBorderStyle = FormBorderStyle.FixedDialog;
this.StartPosition = FormStartPosition.CenterParent;
this.MaximizeBox = false;
this.MinimizeBox = false;
this.BackColor = Color.FromArgb(45, 45, 45);

// Button panel at bottom
var buttonPanel = new Panel
{
    Dock = DockStyle.Bottom,
    Height = 50,
    BackColor = Color.FromArgb(40, 40, 40)
};
```

### Section Headers in Settings

Use box-drawing characters for visual separation:
```csharp
AddLabel(panel, "── Section Name ──", 10, y);
```

---

## Core Components

### MainForm.cs

The main application form handles:

- **Telnet Connection:** Direct TCP connection to MUD server
- **ANSI Rendering:** Full color support in MUD output
- **Message Routing:** Distributes server messages to managers
- **UI Layout:** Split view (MUD output / System log) + Combat panel
- **Combat State:** Tracks ticks, HP/Mana bars, engagement
- **Logon Automation:** Processes login sequences before HP bar appears
- **Reconnection:** Auto-reconnect on connection loss (configurable)

Key Features:
- Command input with history
- Auto-scroll toggle for both panes
- Timestamp display option
- Quick toggle buttons (Combat, Heal, Buff, Cure)
- Manual tick controls

### BuffManager.cs

Central management hub for:

- **Buff Configurations:** Spells with durations, mana costs, targets
- **Active Buff Tracking:** Expiration timers, auto-recast
- **Party Management:** Member list with HP/Mana percentages
- **Player Info:** Current character stats from `stat` command
- **Settings Persistence:** Global settings (JSON)
- **Character Profiles:** Complete character configurations
- **BBS Settings:** Connection parameters, logon sequences

Key Properties:
```csharp
BuffWhileResting        // Allow buff casting while resting
BuffWhileInCombat       // Allow buff casting during combat
ManaReservePercent      // Don't cast if mana below this %
ParAutoEnabled          // Automatic party status requests
InCombat                // Current combat state
```

### CombatManager.cs

Handles all combat automation:

- **Enemy Detection:** Parses "Also here:" lines (handles multi-chunk)
- **Attack Automation:** Melee and spell attacks
- **Spell Combat:** Multi-attack, pre-attack, single-target spells
- **Melee Fallback:** Uses melee when mana is low
- **Monster Overrides:** Per-monster attack customization
- **Target Tracking:** Current target, cast counts per engagement

Combat Settings (per character):
```csharp
AttackCommand           // "a", "attack", "bash", "smash"
AttackSpell             // 4-letter spell abbreviation
MultiAttackSpell        // Room attack spell
PreAttackSpell          // Cast before engaging
BackstabWeapon          // Weapon for backstab
DoBackstabAttack        // Enable backstab automation
MaxMonsters             // Max monsters to engage
```

### HealingManager.cs

Manages healing automation:

- **Heal Spells:** Configured with mana costs
- **Self Heal Rules:** HP thresholds (Combat vs Resting states)
- **Party Heal Rules:** Single-target heals on party members
- **Party-Wide Rules:** Group heals when multiple injured

Rule Types:
- `Combat` - Active during combat and idle
- `Resting` - Active only while resting (more aggressive)

### CureManager.cs

Handles ailment detection and curing:

- **Ailments:** Poison, Paralysis, Blindness, etc.
- **Cure Spells:** Mapped to specific ailments
- **Detection Methods:**
  - Self detection via game messages
  - Party detection via status indicators ("P" for poison)
  - Telepath requests (e.g., "@held" for paralysis)

### PlayerDatabaseManager.cs

Tracks players encountered:

- **Player Data:** First name (unique key), last name
- **Relationships:** Friend, Neutral, Enemy
- **Auto-Discovery:** Parses "who" command output
- **Last Seen:** Timestamp tracking

### MonsterDatabaseManager.cs

Manages monster data:

- **CSV Import:** Loads from MajorMUD data files
- **Monster Overrides:** Per-monster customizations
- **Custom Monsters:** Manually added entries
- **Attack Priority:** First, High, Normal, Low, Last

---

## Data Models

### Character Classes

**Melee Classes:**
- Warrior, Paladin, Warlock, Cleric
- Martial Artist, Ninja, Thief
- Missionary, Witchunter, Gypsy, Ranger, Bard, Mystic

**Caster Classes:**
- Mage, Priest, Druid

### Key Model Classes

```csharp
// BBS/Connection Settings
public class BbsSettings
{
    public string Address { get; set; }
    public int Port { get; set; } = 23;
    public List<LogonSequence> LogonSequences { get; set; }
    public string LogoffCommand { get; set; }
    public string RelogCommand { get; set; }
    public int PvpLevel { get; set; }
    
    // Reconnection settings
    public bool ReconnectOnConnectionFail { get; set; } = true;
    public bool ReconnectOnConnectionLost { get; set; } = true;
    public int MaxConnectionAttempts { get; set; } = 0;  // 0 = unlimited
    public int ConnectionRetryPauseSeconds { get; set; } = 5;
}

// Buff Configuration
public class BuffConfiguration
{
    public string DisplayName { get; set; }
    public string Command { get; set; }          // 4-letter spell abbrev
    public int DurationSeconds { get; set; }
    public int ManaCost { get; set; }
    public string Category { get; set; }         // Combat, Defense, Utility
    public string TargetType { get; set; }       // SelfOnly, MeleeParty, etc.
    public string SelfCastMessage { get; set; }
    public string PartyCastMessage { get; set; } // Use {target} placeholder
    public string ExpireMessage { get; set; }
    public bool AutoRecast { get; set; }
    public int RecastBufferSeconds { get; set; }
    public int Priority { get; set; }            // 1-10, lower = higher
}

// Combat Settings
public class CombatSettings
{
    public string CharacterName { get; set; }
    
    // Weapons
    public string AttackCommand { get; set; }
    public string BackstabWeapon { get; set; }
    public string NormalWeapon { get; set; }
    public string AlternateWeapon { get; set; }
    public string Shield { get; set; }
    
    // Spell Combat
    public string MultiAttackSpell { get; set; }
    public int MultiAttackMinEnemies { get; set; }
    public int MultiAttackMaxCast { get; set; }
    public int MultiAttackReqManaPercent { get; set; }
    
    public string PreAttackSpell { get; set; }
    public int PreAttackMaxCast { get; set; }
    public int PreAttackReqManaPercent { get; set; }
    
    public string AttackSpell { get; set; }
    public int AttackMaxCast { get; set; }
    public int AttackReqManaPercent { get; set; }
    
    // Options
    public bool DoBackstabAttack { get; set; }
    public int MaxMonsters { get; set; }
    public int RunDistance { get; set; }
}

// Monster Override
public class MonsterOverride
{
    public int MonsterNumber { get; set; }
    public string CustomName { get; set; }
    public string Relationship { get; set; }     // Neutral, Friend, Enemy
    public string PreAttackSpell { get; set; }
    public int PreAttackSpellMax { get; set; }
    public string AttackSpell { get; set; }
    public int AttackSpellMax { get; set; }
    public string Priority { get; set; }         // First, High, Normal, Low, Last
    public bool NotHostile { get; set; }
}

// Player Data
public class PlayerData
{
    public string FirstName { get; set; }        // Unique identifier
    public string LastName { get; set; }
    public string Relationship { get; set; }     // Friend, Neutral, Enemy
    public DateTime LastSeen { get; set; }
}
```

---

## Combat System

### Combat Ticks

Combat ticks are **critical** in MajorMUD. They occur approximately every 10 seconds and determine when combat actions process.

- **Tick Interval:** ~10,000ms (configurable via `TICK_INTERVAL_MS`)
- **Detection:** `*Combat Engaged*` message
- **Timer:** Countdown displayed in UI with progress bar
- **Color Coding:** Green (>2s), Orange (1-2s), Red (<1s)

### Attack Flow

```
1. Enemy Detection
   └── Parse "Also here:" for monsters
   
2. Pre-Attack Phase (if configured)
   └── Cast PreAttackSpell up to PreAttackMaxCast times
   
3. Multi-Attack Check
   └── If enemies >= MinEnemies AND mana >= threshold
       └── Cast MultiAttackSpell up to MultiAttackMaxCast times
   
4. Single Target Attack
   ├── If mana >= AttackReqManaPercent
   │   └── Cast AttackSpell up to AttackMaxCast times
   └── Else (melee fallback)
       └── Send AttackCommand
   
5. Combat Tick
   └── Reset cast counts, re-evaluate targets
```

### Melee Fallback Logic

When mana drops below threshold:
1. Stop casting attack spells
2. Switch to melee `AttackCommand`
3. Track with `_usedMeleeThisRound` flag
4. Reset on next combat tick

### "Also Here" Parsing

The "Also here:" line can span multiple TCP chunks. The parser:
1. Buffers partial lines until complete (ends with period)
2. Splits by comma to get entity list
3. Distinguishes players (have class/level) from monsters
4. Applies monster overrides for attack priority

---

## Message Parsing

### Key Regex Patterns

```csharp
// Combat tick
@"^\*Combat Engaged\*$"

// Health/Mana bar
@"\[HP=(\d+)/(\d+)\s+(?:MA|KA)=(\d+)/(\d+)\]"
// Groups: 1=current HP, 2=max HP, 3=current Mana/Kai, 4=max Mana/Kai

// Room contents
@"Also here:\s*(.+?)\."
// Note: Use Singleline mode for multi-line support

// Character stats (from 'stat' command)
@"Name:\s*(.+)"
@"Race:\s*(.+)"
@"Class:\s*(.+)"
@"Level:\s*(\d+)"

// Party status (from 'par' command)
// Format: "Name [##########] Frontrank 100% 100%"
// Or: "Name (Class Level) [##########] Backrank 50% 100%"
```

### ANSI Color Codes

The app fully renders ANSI escape sequences:
- Standard colors (30-37, 40-47)
- Bright colors (90-97, 100-107)
- Bold/Reset handling

### Special Indicators

| Indicator | Meaning |
|-----------|---------|
| `#` in health bar | HP units |
| `P` in party status | Poisoned |
| `H` in party status | Held/Paralyzed |
| `*Combat Engaged*` | Combat tick marker |

---

## Game Data System

### Data Files (JSON)

Located in a user-specified folder, typically exported from MajorMUD data:

| File | Contents |
|------|----------|
| `Races.json` | Race definitions with stat ranges |
| `Classes.json` | Class definitions with abilities |
| `Items.json` | Item database with stats |
| `Spells.json` | Spell definitions |
| `Monsters.json` | Monster database |
| `Rooms.json` | Room definitions |
| `Shops.json` | Shop data |
| `Lairs.json` | Monster spawn locations |
| `TextBlocks.json` | Text block content |

### GameDataCache (Singleton)

Caches loaded JSON data to avoid repeated file reads:

```csharp
// Get cached table
var items = GameDataCache.Instance.GetTable("Items");

// Force reload
GameDataCache.Instance.InvalidateTable("Items");

// Access pattern
var entry = table.FirstOrDefault(e => 
    e.TryGetValue("Number", out var n) && 
    Convert.ToInt64(n) == targetNumber);
```

### Ability System

Items, spells, and other entities use Abil-N / AbilVal-N column pairs:

```csharp
// AbilityNames.cs provides lookup
string name = AbilityNames.GetName(abilityId);  // e.g., 2 → "AC"

// Resolve all abilities from a data row
var abilities = AbilityNames.ResolveAbilities(rowData);
// Returns List<(string Name, string Value)>
```

### Name Resolution

Items have "Obtained From" field with references like:
- `Shop #40` → Look up in Shops.json
- `Monster #624` → Look up in Monsters.json  
- `Textblock #9125` → Look up in TextBlocks.json

Resolution pattern:
```csharp
private static string ResolveName(string tableName, int number)
{
    var table = GameDataCache.Instance.GetTable(tableName);
    var entry = table?.FirstOrDefault(e =>
        e.TryGetValue("Number", out var n) &&
        Convert.ToInt64(n) == number);
    
    return entry?.TryGetValue("Name", out var name) == true 
        ? name?.ToString() ?? "" 
        : "";
}
```

---

## Configuration & Persistence

### File Locations

```
%AppData%\MudProxyViewer\
├── settings.json              # Global application settings
├── buffs.json                 # Buff configurations
├── healing.json               # Heal spells and rules
├── cures.json                 # Ailments and cure spells
├── monster_settings.json      # Path to monster CSV
└── Characters\                # Character profiles
    ├── CharacterName.json
    └── AnotherChar.json
```

### Global Settings (settings.json)

```json
{
    "ParAutoEnabled": false,
    "ParFrequencySeconds": 15,
    "ParAfterCombatTick": false,
    "HealthRequestEnabled": false,
    "HealthRequestIntervalSeconds": 60,
    "ManaReservePercent": 20,
    "BuffWhileResting": true,
    "BuffWhileInCombat": true,
    "AutoStartProxy": false,
    "CombatAutoEnabled": false
}
```

---

## Character Profiles

### Profile Location

```
%AppData%\MudProxyViewer\Characters\
```

### Profile Structure

```json
{
    "ProfileVersion": "1.0",
    "SavedAt": "2025-01-31T00:00:00",
    
    "CharacterName": "Azii Ragequit",
    "CharacterClass": "Priest",
    "CharacterLevel": 25,
    
    "BbsSettings": {
        "Address": "server.ip.address",
        "Port": 23,
        "LogonSequences": [
            { "TriggerMessage": "Your choice?", "Response": "1" },
            { "TriggerMessage": "Enter your name:", "Response": "Username" },
            { "TriggerMessage": "Password:", "Response": "Password" }
        ],
        "LogoffCommand": "quit",
        "RelogCommand": "xx",
        "PvpLevel": 0,
        "ReconnectOnConnectionFail": true,
        "ReconnectOnConnectionLost": true,
        "MaxConnectionAttempts": 0,
        "ConnectionRetryPauseSeconds": 5
    },
    
    "CombatSettings": { ... },
    "Buffs": [ ... ],
    "HealSpells": [ ... ],
    "SelfHealRules": [ ... ],
    "PartyHealRules": [ ... ],
    "PartyWideHealRules": [ ... ],
    "Ailments": [ ... ],
    "CureSpells": [ ... ],
    "MonsterOverrides": [ ... ],
    "Players": [ ... ]
}
```

### Logon Automation

Sequences processed **only during login phase** (before HP bar detected):
- Each sequence fires once per connection
- Case-insensitive trigger matching
- Processed in order

---

## UI Components

### Main Window Layout

```
┌─────────────────────────────────────────────────────────────────────┐
│ File  Options  Game Data  Help                                      │
├─────────────────────────────────────────────────────────────────────┤
│ [Connect] server:23    │ [Pause] [Combat] [Heal] [Buff] [Cure]      │
├────────────────────────┴────────────────────────────────────────────┤
│                                │                                     │
│  📺 MUD Output                 │  ⚔️ Combat Panel                   │
│  (ANSI colors)                 │  ┌─────────────────────┐           │
│                                │  │ State: Idle         │           │
│                                │  │ Next Tick: 8.5s     │           │
│                                │  │ [████████░░] 85%    │           │
│                                │  ├─────────────────────┤           │
│ ───────────────────────────── │  │ Self Status         │           │
│ [Command Input              ] │  │ HP: ████████░░ 80%  │           │
├───────────────────────────────┤  │ MA: ██████░░░░ 60%  │           │
│  📋 System Log                 │  ├─────────────────────┤           │
│  (debug/status messages)      │  │ Party               │           │
│                                │  │ Member1 [████] 100% │           │
│                                │  │ Member2 [██░░]  50% │           │
│                                │  └─────────────────────┘           │
├─────────────────────────────────────────────────────────────────────┤
│ Status: Connected                                    [AutoScroll ✓] │
└─────────────────────────────────────────────────────────────────────┘
```

### Settings Dialog Tabs

1. **General** - Cast priority, mana reserve
2. **Combat** - Attack commands, spells, backstab
3. **Healing** - Heal spells and rules
4. **Cures** - Ailments and cure spells
5. **Buffs** - Buff configurations
6. **Party** - Party status polling
7. **BBS** - Telnet connection, logon automation, reconnection

### Menu Structure

```
File
├── Load Character...
├── Save Character
├── Save Character As...
├── ─────────────────
├── Save Log...
├── Clear Log
├── ─────────────────
└── Exit

Options
├── Settings...
├── ─────────────────
├── Export ▶ (Buffs, Heals, Cures)
└── Import ▶ (Buffs, Heals, Cures)

Game Data
├── Races...
├── Classes...
├── Items...
├── Spells...
├── Monsters...
├── Rooms...
├── Shops...
├── Lairs...
└── TextBlocks...

Help
└── About
```

---

## Code Organization

### GameData Folder Structure (Option A)

Each data type has its own file containing:
- `*ViewerConfig` - Static class with list view configuration
- `*DetailDialog` - Form for viewing single record details

```
GameData/
├── AbilityNames.cs           # Shared utility
├── GenericDetailDialog.cs    # Fallback dialog
├── RaceDialogs.cs            # Races (complete)
├── ClassDialogs.cs           # Classes (complete)
├── ItemDialogs.cs            # Items (complete)
├── SpellDialogs.cs           # Spells (placeholder)
├── MonsterDialogs.cs         # Monsters (stub - see MonsterDatabaseDialog)
├── RoomDialogs.cs            # Rooms (placeholder)
├── ShopDialogs.cs            # Shops (placeholder)
├── LairDialogs.cs            # Lairs (placeholder)
└── TextBlockDialogs.cs       # TextBlocks (placeholder)
```

### ViewerConfig Pattern

```csharp
public static class ItemViewerConfig
{
    public static readonly HashSet<string>? VisibleColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Number", "Name"
    };
    
    public static bool ShowSearchBar => true;
    public static bool NameColumnFills => true;
    public static int NumberColumnWidth => 80;
}
```

### DetailDialog Pattern

```csharp
public class ItemDetailDialog : Form
{
    private readonly Dictionary<string, object?> _data;
    
    public ItemDetailDialog(Dictionary<string, object?> data)
    {
        _data = data;
        InitializeComponent();
    }
    
    // Standard sections: CreateSection(), GetContentPanel(), AddLabelPair()
    // Ability resolution: AbilityNames.ResolveAbilities(_data)
    // Name resolution: ResolveName("Shops", shopNumber)
}
```

---

## Development Guidelines

### Code Style

- C# .NET 8.0 with nullable enabled
- WinForms for UI
- Event-driven architecture
- Managers communicate via events
- Consistent dark theme throughout

### Event Patterns

```csharp
// Logging
OnLogMessage?.Invoke("📝 Message here");

// Sending commands to MUD
OnSendCommand?.Invoke("cast spell");

// State changes
OnBuffsChanged?.Invoke();
OnPartyChanged?.Invoke();
OnCombatEnabledChanged?.Invoke();
```

### Emoji Usage in Logs

| Emoji | Meaning |
|-------|---------|
| ⚔️ | Combat |
| 🎯 | Target/enemy |
| 💚 | Healing |
| 🛡️ | Buff/defense |
| 📊 | Stats/status |
| 📝 | Database/logging |
| 💾 | Save |
| 📂 | Load |
| ⚠️ | Warning |
| ❌ | Error/disabled |
| 🔌 | Connection |
| 🔄 | Reconnecting |

### Adding New Features

1. **New Manager:** Create class, inject into BuffManager, wire events
2. **New Setting:** Add to appropriate model, update Load/Save methods
3. **New Character Data:** Add to CharacterProfile, update serialization
4. **New Game Data Type:** Create `*Dialogs.cs` in GameData folder
5. **New UI:** Follow dark theme patterns, consistent sizing

---

## Known Patterns & Solutions

### Read-Only Checkboxes (Visible but Non-Interactive)

```csharp
// Keep enabled for proper colors, but revert changes
var cb = new CheckBox
{
    Text = "Option",
    ForeColor = Color.White,
    BackColor = Color.Transparent,
    Checked = false
};
cb.Click += (s, e) => { if (s is CheckBox chk) chk.Checked = false; };
```

### Numeric-Only TextBox

```csharp
textBox.KeyPress += (s, e) =>
{
    if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
        e.Handled = true;
};
```

### Multi-Chunk Line Buffering

For messages split across TCP reads:
```csharp
private StringBuilder _buffer = new();
private bool _capturing = false;

// When "Also here:" detected, start capturing
// Continue until line ends with period
// Then process complete line
```

### Safe JSON Parsing

```csharp
var value = data.TryGetValue("Key", out var v) && v != null 
    ? v.ToString() ?? "" 
    : "";
```

---

## Future Plans

### Completed Features ✅

- Direct telnet mode (no proxy)
- ANSI color support
- Logon automation
- BBS settings with reconnection
- Character profiles
- Game data viewers (Races, Classes, Items)
- Code reorganization (Option A)

### Planned Features

- [ ] Complete remaining game data dialogs (Spells, Rooms, Shops, etc.)
- [ ] Scripting system for custom automation
- [ ] Map/navigation integration
- [ ] Combat logging and statistics
- [ ] Party coordination features
- [ ] Plugin architecture
- [ ] Logoff/Relog command automation
- [ ] PVP level-based player detection

---

## Quick Reference

### Essential Method Calls

```csharp
// Send command to MUD
await SendCommandAsync("command");
// or via event
_buffManager.OnSendCommand?.Invoke("command");

// Log message
LogMessage("message", MessageType.System);
// or
OnLogMessage?.Invoke("message");

// Check states
_buffManager.InCombat
_combatManager.CombatEnabled
_isConnected

// Get character settings
_combatManager.GetCurrentSettings()

// Access game data
var table = GameDataCache.Instance.GetTable("Items");
```

### Key Regex Patterns

```csharp
// Combat tick
if (line == "*Combat Engaged*") { /* tick */ }

// HP/Mana bar (login detection)
var match = Regex.Match(line, @"\[HP=(\d+)/(\d+)\s+(?:MA|KA)=(\d+)/(\d+)\]");
if (match.Success) { /* HP bar found = login complete */ }

// Room contents
var match = Regex.Match(line, @"Also here:\s*(.+?)\.", RegexOptions.Singleline);
```

---

## Version History

| Version | Changes |
|---------|---------|
| 1.0.0 | Code reorganization (Option A), comprehensive knowledge base |
| 0.9.0 | Direct telnet, ANSI colors, logon automation, BBS settings |
| 0.8.1 | Character profiles, monster/player DB in profiles |
| 0.8.0 | Combat system, healing, curing |
| 0.7.x | Buff management, party tracking |

---

## Important Notes for AI Assistants

1. **Always ask before assuming** - Design intent and purpose should be clarified
2. **Dark theme is mandatory** - All UI uses the color palette above
3. **Test checkbox visibility** - Disabled checkboxes show as gray; use click-revert pattern
4. **Combat ticks are critical** - Timing matters for all combat automation
5. **HP bar = login complete** - Logon sequences only fire before HP bar appears
6. **Status colors: White when disconnected** - Not red, not gray
7. **Character profiles are comprehensive** - They contain ALL character-specific settings
8. **GameDataCache is a singleton** - Use Instance property
9. **Ability columns are paired** - Abil-N and AbilVal-N must be processed together
10. **"Obtained From" field has a space** - Not "ObtainedFrom"

---

*This document provides comprehensive context for AI assistants working on this project. Keep it updated as features are added or changed.*
