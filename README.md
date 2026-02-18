# MUD Proxy Viewer - AI Knowledge Base

> **Version:** 2.1.0  
> **Last Updated:** February 17, 2026  
> **Purpose:** Combat automation client for MajorMUD, replacing the deprecated MegaMUD client  
> **Platform:** Windows (.NET 8.0 WinForms)  
> **Status:** Active Development - **BuffManager Refactoring In Progress**

---

## 🎉 Recent Major Update (v2.1.0)

**BuffManager.cs reduced from 2,237 lines to ~1,147 lines (49% reduction!) — ongoing refactoring**

BuffManager refactoring is decomposing the monolithic hub into focused, single-responsibility classes:
- ✅ Extracted `PartyManager.cs` — party tracking, par automation, health requests
- ✅ Extracted `PlayerStateManager.cs` — HP/mana, stats, exp, training screen, resting state
- ✅ Extracted `AppSettings.cs` — app-level settings persistence
- ✅ Removed 15+ pass-through properties, callers access sub-managers directly
- ✅ Automation toggles (Combat/Heal/Buff/Cure) default ON, no longer persisted
- ✅ Backscroll history viewer with ANSI color and search
- 🔄 Next: Message routing extraction, profile management extraction, GameManager creation

---

## Table of Contents

1. [Game Overview](#game-overview)
2. [Application Architecture](#application-architecture)
3. [File Structure](#file-structure)
4. [UI Styling Guidelines](#ui-styling-guidelines)
5. [Core Components](#core-components)
6. [Data Models](#data-models)
7. [Combat System](#combat-system)
8. [Message Parsing](#message-parsing)
9. [Game Data System](#game-data-system)
10. [Configuration & Persistence](#configuration--persistence)
11. [Character Profiles](#character-profiles)
12. [UI Components](#ui-components)
13. [Code Organization](#code-organization)
14. [Development Guidelines](#development-guidelines)
15. [Known Patterns & Solutions](#known-patterns--solutions)
16. [Refactoring History](#refactoring-history)

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
- **ANSI Support:** Full ANSI color code rendering via VT100 emulation
- **IAC Handling:** Telnet protocol negotiation handled automatically

---

## Application Architecture

### High-Level Component Diagram

```
┌─────────────────────────────────────────────────────────┐
│                      MainForm                           │
│  (UI Orchestration)                                    │
│                                                         │
│  ┌──────────────────────┐  ┌──────────────────────┐   │
│  │ MenuHandlers.cs      │  │ DisplayUpdates.cs    │   │
│  └──────────────────────┘  └──────────────────────┘   │
└─────────────────────────────────────────────────────────┘
           │               │               │
           ▼               ▼               ▼
┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│ Telnet       │  │ Message      │  │ Terminal     │
│ Connection   │  │ Router       │  │ Control      │
└──────────────┘  └──────────────┘  └──────────────┘
                        │
                        ▼
┌──────────────────────────────────────────────────────┐
│              BuffManager (Hub — being refactored)    │
│  ┌──────────────┐  ┌──────────────┐                 │
│  │ PlayerState  │  │ PartyManager │                 │
│  │ Manager      │  │              │                 │
│  └──────────────┘  └──────────────┘                 │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐    │
│  │ Combat Mgr │  │ Healing Mgr│  │  Cure Mgr  │    │
│  └────────────┘  └────────────┘  └────────────┘    │
│  ┌────────────┐  ┌──────────────┐ ┌────────────┐   │
│  │ Remote Cmd │  │ Room Tracker │ │ AppSettings│   │
│  │ Manager    │  │ + GraphMgr  │ │            │   │
│  └────────────┘  └──────────────┘ └────────────┘   │
│  ┌──────────────┐  ┌──────────────┐                 │
│  │ PlayerDB Mgr │  │ MonsterDB   │                 │
│  │              │  │ Manager     │                 │
│  └──────────────┘  └──────────────┘                 │
└──────────────────────────────────────────────────────┘
```

### Component Relationships

```
MainForm (Entry Point - Partial Class Split)
    │
    ├── TelnetConnection (Network Layer)
    │       └── Handles IAC, NAWS, reconnection
    │
    ├── MessageRouter (Message Processing)
    │       ├── Combat state detection
    │       ├── HP/Mana parsing
    │       └── Tick detection
    │
    ├── TerminalControl (VT100 Terminal)
    │       ├── ScreenBuffer (2D character grid + scrollback history)
    │       └── AnsiVtParser (ANSI/VT100 parsing)
    │
    ├── LogRenderer (Log Display)
    │       └── ANSI color rendering for logs
    │
    ├── BuffManager (Central Hub — being decomposed)
    │       ├── PlayerStateManager (HP, mana, stats, exp, resting, training)
    │       ├── PartyManager (party tracking, par, health requests)
    │       ├── HealingManager (heal spells, HP threshold rules)
    │       ├── CureManager (ailment detection, cure automation)
    │       ├── CombatManager (enemy detection, attack automation)
    │       ├── RemoteCommandManager (telepath-based remote control)
    │       ├── PlayerDatabaseManager (friend/enemy tracking)
    │       ├── MonsterDatabaseManager (monster data, overrides)
    │       ├── RoomGraphManager + RoomTracker (room detection, mapping)
    │       └── AppSettings (app-level persistence)
    │
    └── GameDataCache (Singleton)
            └── Game JSON files (Races, Classes, Items, etc.)
```

### Event Flow

1. **MUD Server → TelnetConnection:** Raw telnet data received
2. **TelnetConnection → MainForm:** Decoded text via OnDataReceived event
3. **MainForm → MessageRouter:** Process message for game state
4. **MainForm → TerminalControl:** Display in VT100 terminal
5. **MessageRouter → Managers:** Route to BuffManager, CombatManager, etc.
6. **Managers:** Update state, trigger automation
7. **Automation → TelnetConnection:** Send commands back to server
8. **MainForm → UI:** Update status panels, logs, indicators

---

## File Structure

### Solution Structure (Post-Refactoring)

```
MudProxyViewer/
│
├── Core Application (Main UI)
│   ├── MainForm.cs                    # UI orchestration
│   ├── MainForm.MenuHandlers.cs       # Menu/button event handlers
│   └── MainForm.DisplayUpdates.cs     # UI refresh methods
│
├── Network Layer
│   └── TelnetConnection.cs            # TCP, IAC, NAWS, reconnection
│
├── Message Processing
│   └── MessageRouter.cs               # Combat detection, HP parsing, ticks
│
├── Terminal Emulation
│   ├── TerminalControl.cs             # VT100 terminal UserControl
│   ├── ScreenBuffer.cs                # 2D character buffer + scrollback history
│   ├── AnsiVtParser.cs                # ANSI escape sequence parser
│   └── TerminalCell.cs                # Terminal cell struct
│
├── UI Components
│   ├── LogRenderer.cs                 # ANSI log rendering
│   ├── MessageType.cs                 # Log message type enum
│   ├── DarkMenuRenderer.cs            # Dark theme menu renderer
│   └── DarkColorTable.cs              # Dark theme color table
│
├── Game Managers
│   ├── BuffManager.cs                 # Buff configs, auto-recast, cast priority (hub — being decomposed)
│   ├── PlayerStateManager.cs          # HP/mana, stats, exp, resting/combat/training state
│   ├── PartyManager.cs                # Party tracking, par automation, health requests
│   ├── CombatManager.cs               # Combat automation, enemy detection, attacks
│   ├── HealingManager.cs              # Heal spell management, HP monitoring
│   ├── CureManager.cs                 # Ailment detection and cure automation
│   ├── RemoteCommandManager.cs        # Telepath-based remote command handling
│   ├── PlayerDatabaseManager.cs       # Player tracking (friends/enemies)
│   ├── MonsterDatabaseManager.cs      # Monster data, CSV parsing, overrides
│   ├── RoomGraphManager.cs            # Room graph from game data
│   ├── RoomTracker.cs                 # Current room detection from server output
│   └── ExperienceTracker.cs           # Exp/hour calculation, time-to-level
│
├── Settings & Persistence
│   ├── AppSettings.cs                 # App-level settings (settings.json)
│   └── ProfileManager.cs             # Character profile file I/O (partial — not yet wired)
│
├── Data & Models
│   ├── Models.cs                      # All data models and enums
│   ├── GameDataCache.cs               # Singleton cache for loaded JSON data
│   └── GameDataViewerDialog.cs        # Generic game data list viewer
│
├── Dialogs
│   ├── SettingsDialog.cs              # Settings UI (tabbed configuration)
│   ├── BackscrollDialog.cs            # Scrollback history viewer with search
│   ├── BuffConfigDialog.cs            # Buff configuration editor
│   ├── HealingConfigDialog.cs         # Healing rules configuration
│   ├── CureConfigDialog.cs            # Cure/ailment configuration
│   └── MonsterDatabaseDialog.cs       # Monster-specific list with overrides
│
├── Controls/
│   └── CombatStatusPanel.cs           # Combat panel UI component
│
├── GameData/                          # Game data viewers
│   ├── AbilityNames.cs                # Ability ID → name lookup
│   ├── GenericDetailDialog.cs         # Fallback detail dialog
│   ├── RaceDialogs.cs                 # RaceViewerConfig + RaceDetailDialog
│   ├── ClassDialogs.cs                # ClassViewerConfig + ClassDetailDialog
│   ├── ItemDialogs.cs                 # ItemViewerConfig + ItemDetailDialog
│   ├── SpellDialogs.cs                # SpellViewerConfig + SpellDetailDialog
│   ├── MonsterDialogs.cs              # MonsterViewerConfig
│   ├── RoomDialogs.cs                 # RoomViewerConfig + RoomDetailDialog
│   ├── ShopDialogs.cs                 # ShopViewerConfig + ShopDetailDialog
│   ├── LairDialogs.cs                 # LairViewerConfig + LairDetailDialog
│   └── TextBlockDialogs.cs            # TextBlockViewerConfig + TextBlockDetailDialog
│
└── MudProxyViewer.csproj              # .NET 8.0 Windows Forms project
```

### File Size Summary

| File | Lines | Purpose |
|------|-------|---------|
| **BuffManager.cs** | ~1,147 | Buff management + hub (being decomposed) |
| PlayerStateManager.cs | ~300 | Player state, stats, exp tracking |
| PartyManager.cs | ~400 | Party tracking, automation |
| CombatManager.cs | ~700 | Combat automation |
| HealingManager.cs | ~450 | Heal spell management |
| CureManager.cs | ~700 | Ailment/cure automation |
| RemoteCommandManager.cs | ~400 | Telepath remote commands |
| RoomTracker.cs | ~350 | Room detection from server output |
| RoomGraphManager.cs | ~300 | Room graph from game data |
| MainForm.cs | ~600 | Core UI orchestration |
| MainForm.MenuHandlers.cs | ~400 | All menu/button handlers |
| MainForm.DisplayUpdates.cs | ~300 | UI refresh methods |
| TelnetConnection.cs | ~400 | Network layer |
| MessageRouter.cs | ~180 | Message processing |
| TerminalControl.cs | ~480 | VT100 terminal |
| ScreenBuffer.cs | ~400 | Terminal buffer + scrollback |
| AnsiVtParser.cs | ~365 | ANSI parser |
| SettingsDialog.cs | ~900 | Tabbed settings UI |

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
```

### Menu Styling (Dark Theme)

Menu styling is now handled by dedicated classes:

```csharp
// Apply dark theme to menu (in MainForm)
menuStrip.Renderer = new DarkMenuRenderer();
```

Implementation is in `DarkMenuRenderer.cs` and `DarkColorTable.cs`.

### Status Label Colors

| State | Color | Example |
|-------|-------|---------|
| Connected | `LimeGreen` | "Connected" |
| Disconnected | `White` | "Disconnected" |
| Connecting | `Yellow` | "Connecting..." |
| Error | `Red` | "Connection Failed" |
| In Combat | `Red` | Combat state |
| Idle | `LimeGreen` | Not in combat |

---

## Core Components

### MainForm (Partial Class)

The main application form is now split across three files for better organization:

**MainForm.cs** (~600 lines) - Core orchestration:
- Connection management
- Timer initialization
- Core event routing
- Form initialization

**MainForm.MenuHandlers.cs** (~400 lines) - Event handlers:
- All menu item click handlers
- All button click handlers
- Import/Export handlers
- Settings dialogs

**MainForm.DisplayUpdates.cs** (~300 lines) - UI updates:
- `RefreshBuffDisplay()`
- `RefreshPartyDisplay()`
- `UpdateToggleButtonStates()`
- `UpdateTickDisplay()`
- Other UI refresh methods

### TelnetConnection.cs

Handles all network communication:

```csharp
public class TelnetConnection
{
    // Events
    public event Action<string>? OnDataReceived;      // Text received from server
    public event Action<bool>? OnStatusChanged;       // Connection state changed
    public event Action<string>? OnLogMessage;        // Log message for UI
    
    // Methods
    public async Task<bool> ConnectAsync(string address, int port, BbsSettings settings);
    public void Disconnect();
    public async Task SendCommandAsync(string command);
    public async Task SendDataAsync(byte[] data);
}
```

**Features:**
- Full telnet IAC negotiation (WILL/WONT/DO/DONT)
- NAWS (window size) support
- Terminal type negotiation (ANSI)
- Automatic reconnection with configurable retry logic
- CP437 encoding support

### MessageRouter.cs

Routes and processes messages from the MUD server:

```csharp
public class MessageRouter
{
    // Events
    public event Action<bool>? OnCombatStateChanged;
    public event Action<int, int, int, int, string>? OnPlayerStatsUpdated;
    public event Action? OnCombatTickDetected;
    public event Action? OnPlayerDeath;
    public event Action? OnLoginComplete;
    
    // Methods
    public void ProcessMessage(string text);
    public void SetNextTickTime(DateTime nextTick);
    public void ResetLoginPhase();
}
```

**Responsibilities:**
- Combat state detection (*Combat Engaged* / *Combat Off*)
- HP/Mana parsing from `[HP=100/100/MA=50/50]`
- Combat tick detection via damage clustering
- Death detection
- Login phase tracking

### Terminal Components

**TerminalControl.cs** - VT100 terminal emulator:
- Custom UserControl for rendering terminal
- Supports cursor positioning, colors, scrolling
- Pass-through mode for training screen
- Keyboard input handling

**ScreenBuffer.cs** - Virtual terminal buffer:
- 2D character grid with colors
- Cursor positioning and scrolling
- Line insertion/deletion
- DEC line drawing characters

**AnsiVtParser.cs** - ANSI escape sequence parser:
- Full VT100/ANSI support
- CSI sequences (colors, cursor movement)
- SGR (Select Graphic Rendition)
- Scroll regions

**TerminalCell.cs** - Simple struct:
```csharp
public readonly struct TerminalCell
{
    public readonly char Ch;
    public readonly ConsoleColor Fg;
    public readonly ConsoleColor Bg;
}
```

### LogRenderer.cs

Handles all log rendering with ANSI color support:

```csharp
public class LogRenderer
{
    public void LogMessage(string message, MessageType type, 
        RichTextBox textBox, CheckBox autoScroll, bool showTimestamp);
        
    public void LogMessageWithAnsi(string message, MessageType type,
        RichTextBox textBox, CheckBox autoScroll, bool showTimestamp);
}
```

**Features:**
- ANSI color code parsing for logs
- Automatic log trimming (500KB → 300KB)
- Timestamp support
- Auto-scroll support
- Color brightening for bold codes

### BuffManager.cs

Central management hub (**currently being decomposed** — see Refactoring Plan):
- Buff configurations CRUD, import/export
- Active buff tracking (activate, expire, clear)
- Auto-recast system with heal/cure/buff cast priority
- Cast failure detection (blocked until next tick)
- Still owns: constructor wiring, message dispatching, profile save/load (moving out in next phases)

### PlayerStateManager.cs

Player state tracking (extracted from BuffManager):
- HP, Mana, MaxHP, MaxMana, ManaType
- Resting, InCombat, InTrainingScreen, IsInLoginPhase states
- PlayerInfo (name, race, class, level)
- ExperienceTracker (exp/hour, time-to-level)
- Stat and exp command parsing
- Training screen detection

### PartyManager.cs

Party management (extracted from BuffManager):
- Party member tracking from `par` output
- Join/leave/disband detection
- Auto `par` command on interval or after combat tick
- Health request polling via telepath
- Auto-invite players from room

### CombatManager.cs

Combat automation:
- Enemy detection from "Also here:" lines
- Attack automation (melee and spell)
- Monster override support (attack/ignore/flee)
- Target tracking, round management
- Break command on disable, room rescan on enable

### HealingManager.cs & CureManager.cs

Health management:
- Heal spell configurations with self/party/party-wide targeting
- HP threshold rules (combat vs resting states)
- Ailment detection and cure automation
- Party healing rules
- Toggle state is runtime-only (defaults ON, not persisted)

### RemoteCommandManager.cs

Telepath-based remote control:
- Permission-based command system via player database
- Toggle automation (combat, heal, cure, buff) remotely
- Query health, exp, location
- Execute arbitrary commands, request party invite
- Hangup/relog commands

### AppSettings.cs

App-level settings persistence (extracted from BuffManager):
- `AutoLoadLastCharacter`, `LastCharacterPath`, `DisplaySystemLog`
- Reads/writes `settings.json` (separate from character profiles)

---

## Data Models

*(This section remains largely unchanged - see original README sections)*

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
    public string Command { get; set; }
    public int DurationSeconds { get; set; }
    public int ManaCost { get; set; }
    public string Category { get; set; }
    public string TargetType { get; set; }
    public bool AutoRecast { get; set; }
    // ... additional properties
}

// Combat Settings
public class CombatSettings
{
    public string AttackCommand { get; set; }
    public string AttackSpell { get; set; }
    public string MultiAttackSpell { get; set; }
    public string PreAttackSpell { get; set; }
    public int MaxMonsters { get; set; }
    // ... additional properties
}
```

---

## Combat System

*(Combat system documentation unchanged from previous version)*

### Combat Ticks

Combat ticks occur approximately every 10 seconds (configurable via `TICK_INTERVAL_MS` constant).

- **Detection:** `*Combat Engaged*` message or damage clustering
- **Timer:** Countdown displayed with progress bar
- **Color Coding:** Green (>2s), Orange (1-2s), Red (<1s)

---

## Message Parsing

### Key Detection Patterns

**Combat Tick:**
```csharp
if (line == "*Combat Engaged*") { /* tick detected */ }
```

**HP/Mana Bar (processed in MessageRouter):**
```csharp
var match = Regex.Match(line, @"\[HP=(\d+)/(\d+)\s+(?:MA|KA)=(\d+)/(\d+)\]");
```

**Room Contents (processed in CombatManager):**
```csharp
var match = Regex.Match(line, @"Also here:\s*(.+?)\.", RegexOptions.Singleline);
```

### ANSI Color Rendering

- **Terminal Display:** Handled by `AnsiVtParser` → `ScreenBuffer` → `TerminalControl`
- **Log Display:** Handled by `LogRenderer.LogMessageWithAnsi()`

Both support full ANSI color codes (30-37, 90-97) with bold/bright variants.

---

## Game Data System

*(Game data system unchanged from previous version)*

### Data Files (JSON)

Located in user-specified folder:
- `Races.json`, `Classes.json`, `Items.json`
- `Spells.json`, `Monsters.json`, `Rooms.json`
- `Shops.json`, `Lairs.json`, `TextBlocks.json`

### GameDataCache (Singleton)

```csharp
var items = GameDataCache.Instance.GetTable("Items");
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

---

## Character Profiles

Character profiles contain ALL character-specific settings in a single JSON file.

**Structure:**
```json
{
    "ProfileVersion": "1.0",
    "CharacterName": "Azii Ragequit",
    "CharacterClass": "Priest",
    "CharacterLevel": 25,
    "BbsSettings": { ... },
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
│  📺 VT100 Terminal             │  ⚔️ Combat Panel                   │
│  (TerminalControl)             │  ┌─────────────────────┐           │
│                                │  │ State: Idle         │           │
│                                │  │ Next Tick: 8.5s     │           │
│                                │  │ [████████░░] 85%    │           │
│                                │  ├─────────────────────┤           │
│                                │  │ Self Status         │           │
│                                │  │ HP: ████████░░ 80%  │           │
│ ───────────────────────────── │  │ MA: ██████░░░░ 60%  │           │
│ [Command handled by terminal] │  ├─────────────────────┤           │
├───────────────────────────────┤  │ Party               │           │
│  📋 System Log                 │  │ Member1 [████] 100% │           │
│  (RichTextBox + LogRenderer)  │  │ Member2 [██░░]  50% │           │
│                                │  └─────────────────────┘           │
├─────────────────────────────────────────────────────────────────────┤
│ Status: Connected                                    [AutoScroll ✓] │
└─────────────────────────────────────────────────────────────────────┘
```

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

### Partial Class Pattern

MainForm uses the partial class pattern for organization:

```csharp
// MainForm.cs - Core logic
public partial class MainForm : Form
{
    // Fields, initialization, core methods
}

// MainForm.MenuHandlers.cs - Event handlers
public partial class MainForm
{
    private void LoadCharacter_Click(object? sender, EventArgs e) { }
    private void SaveCharacter_Click(object? sender, EventArgs e) { }
    // ... all menu/button handlers
}

// MainForm.DisplayUpdates.cs - UI updates
public partial class MainForm
{
    private void RefreshBuffDisplay() { }
    private void RefreshPartyDisplay() { }
    // ... all display update methods
}
```

### Extracted Component Pattern

All major subsystems are extracted into focused classes:

```csharp
// Network layer
var telnet = new TelnetConnection();
telnet.OnDataReceived += HandleData;

// Message processing
var router = new MessageRouter(buffManager);
router.OnCombatStateChanged += HandleCombatState;

// Terminal display
var terminal = new TerminalControl();
terminal.SetScreenBuffer(screenBuffer);

// Log rendering
var logRenderer = new LogRenderer();
logRenderer.LogMessage(msg, type, textBox, autoScroll, showTimestamp);
```

### GameData Folder Structure

*(Unchanged from previous version)*

Each data type has its own file with:
- `*ViewerConfig` - Static configuration
- `*DetailDialog` - Detail view form

---

## Development Guidelines

### Code Style

- C# .NET 8.0 with nullable enabled
- WinForms for UI
- Event-driven architecture
- Partial classes for large UI forms
- Extracted classes for focused responsibilities
- Consistent dark theme throughout
- Zero build warnings policy

### Event Patterns

```csharp
// Network events
_telnetConnection.OnDataReceived += HandleData;
_telnetConnection.OnStatusChanged += HandleStatus;

// Message routing events
_messageRouter.OnCombatStateChanged += HandleCombat;
_messageRouter.OnPlayerStatsUpdated += UpdateStats;

// Manager events
OnLogMessage?.Invoke("📝 Message here");
OnSendCommand?.Invoke("cast spell");
```

### Nullable Reference Types

All fields must be initialized or marked with `= null!;`:

```csharp
private Label _someLabel = null!;  // Initialized in InitializeComponent
private TelnetConnection _telnetConnection = null!;  // Initialized in constructor
```

### Adding New Features

1. **New Manager:** Create class with delegate injection, wire into BuffManager constructor
2. **New UI Component:** Extract to separate UserControl or Form
3. **New Network Feature:** Add to TelnetConnection.cs
4. **New Message Processing:** Add to MessageRouter.cs
5. **New Display Logic:** Add to MainForm.DisplayUpdates.cs
6. **New Player State:** Add to PlayerStateManager.cs
7. **New Party Feature:** Add to PartyManager.cs
8. **New Menu/Button:** Add to MainForm.MenuHandlers.cs **New Menu Handler:** Add to MainForm.MenuHandlers.cs

---

## Known Patterns & Solutions

### Read-Only Checkboxes

```csharp
var cb = new CheckBox { /* ... */ };
cb.Click += (s, e) => { if (s is CheckBox chk) chk.Checked = !chk.Checked; };
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

Handled in CombatManager for "Also here:" parsing:
```csharp
// Buffer partial lines until complete (ends with period)
// Then process complete line for enemy detection
```

### Thread-Safe UI Updates

```csharp
private void SomeMethod(string data)
{
    if (InvokeRequired)
    {
        BeginInvoke(() => SomeMethod(data));
        return;
    }
    
    // Update UI here
}
```

---

## Refactoring History

### Version 2.1.0 - BuffManager Decomposition (February 2026)

**Objective:** Decompose BuffManager.cs from 2,237-line monolithic hub into focused single-responsibility classes.

#### Phase 1: Extract PartyManager ✅
- **Created:** `PartyManager.cs` (~400 lines)
- **Removed from BuffManager:** ~350 lines (party tracking, par automation, health requests, regex patterns)
- **Result:** BuffManager reduced to ~1,890 lines

#### Phase 2: Extract PlayerStateManager ✅
- **Created:** `PlayerStateManager.cs` (~300 lines)
- **Removed from BuffManager:** ~250 lines (HP/mana, stats, exp, training screen, resting state)
- **Result:** BuffManager reduced to ~1,640 lines

#### Phase 3a: Extract AppSettings ✅
- **Created:** `AppSettings.cs` (~75 lines)
- **Removed from BuffManager:** ~69 lines (app-level settings persistence)
- **Result:** BuffManager reduced to ~1,570 lines

#### Phase 3b: Pass-Through Property Cleanup ✅
- **Removed from BuffManager:** 15+ pass-through properties
- **Updated:** 7 caller files to access sub-managers directly
- **Result:** BuffManager reduced to ~1,147 lines

#### Additional Fixes in v2.1.0
- Automation toggles (Combat/Heal/Buff/Cure) default ON, no longer persisted
- SettingsDialog no longer auto-saves on every change
- Backscroll history viewer (`BackscrollDialog.cs`) with ANSI color and search
- ScreenBuffer scrollback capture (500 line buffer)
- Combat toggle sends `break` on disable, rescans room on enable
- Race/class regex fix for hyphenated names (e.g., "Dark-Elf")
- HealthRequestIntervalSeconds minimum lowered from 30 to 15
- Removed `CombatAutoEnabled` from character profile persistence

#### Remaining (Phases 4-6 — Planned)
- **Phase 4:** Move message dispatching from BuffManager to MessageRouter
- **Phase 5:** Move profile save/load/new to ProfileManager
- **Phase 6:** Create GameManager as central coordinator, BuffManager becomes buff-only

### Version 2.0.0 - Major Refactoring (February 2026)

**Objective:** Reduce MainForm.cs from 4,552 lines to manageable size for better maintainability and AI collaboration.

#### Phase 1: Network Extraction
- **Extracted:** `TelnetConnection.cs` (400 lines)
- **Removed from MainForm:** Network connection logic, IAC handling, reconnection
- **Result:** MainForm reduced to 4,348 lines

#### Phase 2: Message Processing Extraction
- **Extracted:** `MessageRouter.cs` (200 lines)
- **Removed from MainForm:** Combat detection, HP parsing, tick detection
- **Result:** MainForm reduced to 4,139 lines

#### Phase 3: Terminal Classes Extraction
- **Extracted:** 
  - `TerminalControl.cs` (480 lines)
  - `ScreenBuffer.cs` (340 lines)
  - `AnsiVtParser.cs` (365 lines)
  - `TerminalCell.cs` (17 lines)
- **Removed from MainForm:** Entire VT100 terminal emulation (1,200 lines)
- **Result:** MainForm reduced to 2,930 lines

#### Phase 4: UI Helper Extraction
- **Extracted:**
  - `MessageType.cs` (10 lines)
  - `DarkMenuRenderer.cs` (55 lines)
  - `DarkColorTable.cs` (22 lines)
- **Removed from MainForm:** Enums and UI theming classes (77 lines)
- **Result:** MainForm reduced to 2,853 lines

#### Phase 5: Log Rendering Extraction
- **Extracted:** `LogRenderer.cs` (260 lines)
- **Removed from MainForm:** ANSI log parsing, log formatting (260 lines)
- **Result:** MainForm reduced to 2,593 lines

#### Phase 6: Partial Class Organization
- **Created:**
  - `MainForm.MenuHandlers.cs` (400 lines)
  - `MainForm.DisplayUpdates.cs` (300 lines)
- **Reorganized:** Moved methods to appropriate partial class files
- **Result:** MainForm.cs core logic reduced to ~600 lines
- **Total project:** ~2,400 lines across organized files

#### Phase 7: Warning Resolution
- **Fixed:** All CS8618 nullable reference warnings in CombatStatusPanel.cs
- **Result:** Zero build warnings

### Final Statistics

| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| MainForm.cs Lines | 4,552 | ~600 | **87% reduction** |
| Total Project Lines | 4,552 | ~2,400 | **47% reduction** |
| Number of Files | 1 large | 14 focused | **Better organization** |
| Build Warnings | 9 | 0 | **100% clean** |
| Largest File Size | 4,552 | ~600 | **AI-friendly** |

---

## Quick Reference

### Essential Calls

```csharp
// Send command via telnet
await _telnetConnection.SendCommandAsync("command");

// Log message
_logRenderer.LogMessage("message", MessageType.System, _systemLogTextBox, 
    _autoScrollLogsCheckBox, _showTimestampsCheckBox.Checked);

// Process server message
_messageRouter.ProcessMessage(text);

// Access player state
_buffManager.PlayerStateManager.CurrentHp
_buffManager.PlayerStateManager.InCombat
_buffManager.PlayerStateManager.PlayerInfo.Name

// Access party
_buffManager.PartyManager.PartyMembers
_buffManager.PartyManager.IsInParty

// Access automation toggles
_buffManager.CombatAutoEnabled
_buffManager.AutoRecastEnabled
_buffManager.HealingManager.HealingEnabled
_buffManager.CureManager.CuringEnabled

// Access game data
var table = GameDataCache.Instance.GetTable("Items");
```

### Key Components

```csharp
// Main components in MainForm
private TelnetConnection _telnetConnection;
private MessageRouter _messageRouter;
private BuffManager _buffManager;
private LogRenderer _logRenderer;
private TerminalControl _terminalControl;
private ScreenBuffer _screenBuffer;
private AnsiVtParser _ansiParser;
```

---

## Version History

| Version | Changes |
|---------|---------|
| **2.1.0** | **BuffManager decomposition** — Extracted PartyManager, PlayerStateManager, AppSettings. Pass-through cleanup. Automation toggle fix. Backscroll viewer. Combat toggle improvements. |
| **2.0.0** | **Major refactoring complete** — Extracted network, message routing, terminal, logging into separate classes. MainForm reduced 87%. Zero warnings. |
| 1.0.0 | Code reorganization, comprehensive knowledge base |
| 0.9.0 | Direct telnet, ANSI colors, logon automation, BBS settings |
| 0.8.1 | Character profiles, monster/player DB in profiles |
| 0.8.0 | Combat system, healing, curing |
| 0.7.x | Buff management, party tracking |

---

## Important Notes for AI Assistants

1. **BuffManager is being decomposed** — Sub-managers (PlayerStateManager, PartyManager, etc.) now own their data. Access via `_buffManager.PlayerStateManager`, `_buffManager.PartyManager`, etc.
2. **No more pass-through properties** — Don't use `_buffManager.CurrentHp`, use `_buffManager.PlayerStateManager.CurrentHp`
3. **MainForm is a partial class** — Check MenuHandlers.cs and DisplayUpdates.cs for methods
4. **Network logic is in TelnetConnection** — Don't add network code to MainForm
5. **Message processing is in MessageRouter** — Don't add parsing to MainForm
6. **Terminal rendering is in TerminalControl** — Complete VT100 emulator with scrollback
7. **Log rendering is in LogRenderer** — ANSI color support for logs
8. **Dark theme is mandatory** — All UI uses consistent color palette
9. **Zero warnings policy** — All nullable references must be initialized or marked `= null!`
10. **Automation toggles are runtime-only** — Combat, Heal, Buff, Cure all default ON on launch, never persisted
11. **Delegate injection pattern** — All managers receive dependencies as `Func<>` delegates, not direct references
12. **Character profiles are comprehensive** — ALL character settings in one JSON file

### When Adding New Features

- **Network features** → Add to `TelnetConnection.cs`
- **Message processing** → Add to `MessageRouter.cs` (not BuffManager)
- **Player state tracking** → Add to `PlayerStateManager.cs`
- **Party features** → Add to `PartyManager.cs`
- **UI event handlers** → Add to `MainForm.MenuHandlers.cs`
- **Display updates** → Add to `MainForm.DisplayUpdates.cs`
- **Core orchestration** → Add to `MainForm.cs`
- **Game logic** → Add to appropriate Manager class
- **UI components** → Create new UserControl or Form

---

*This document provides comprehensive context for AI assistants working on this project. Version 2.1.0 continues the decomposition of BuffManager into focused, single-responsibility classes. See BuffManager_Refactoring_Plan_Revised.md for the full plan. Keep updated as features are added.*
