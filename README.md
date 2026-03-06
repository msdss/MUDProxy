# MUD Proxy Viewer — AI Knowledge Base

> **Version:** 2.10.0
> **Last Updated:** March 6, 2026
> **Purpose:** Combat automation client for MajorMUD, replacing the deprecated MegaMUD client
> **Platform:** Windows (.NET 8.0 WinForms)
> **Status:** Active Development — **TextBlock Teleport Exits + Level/Class/Race Restrictions + Blocking Reasons UI**

---

## 🎉 Recent Major Update (v2.10.0)

**TextBlock Teleport Exits + Level/Class/Race Restrictions + Remote Action Orchestration + Blocking Reasons UI**

Four major areas of auto-pathing expansion in this release:

**1. TextBlock Teleport Exits (Phase 5f)** — 1,071 rooms have a CMD field linking to TextBlock entries that define special commands (e.g., "go portal", "enter hole") which teleport players to distant rooms. A new Pass 4 in `BuildGraph()` parses these TextBlocks — following LinkTo chains with cycle detection — to create virtual `Teleport` exits on the room graph. Conditions (level gates, item requirements, skill checks, alignment) are parsed from the TextBlock format. Unconditional and level-only-gated teleports are BFS-traversable; teleports requiring items, skills, monsters, or alignment are marked non-traversable. Level gates are promoted to standard `ExitLevelRestriction` for filter handling.

**2. Level/Class/Race Exit Restrictions (Phase 5d)** — Directional exits can now have level, class, and race restrictions parsed from the `ExitExtra` field in room data. `ExitLevelRestriction` (min/max level), `ExitClassRestriction` (allowed class IDs), and `ExitRaceRestriction` (allowed race IDs) are typed model classes with `IsSatisfiedBy()` methods. `GameManager.GetExitFilter()` checks all three restriction types, with guards to skip filtering when player data isn't loaded yet.

**3. Remote Action Path Orchestration (Phase 5e)** — Multi-action hidden exits that require actions in remote rooms (not the exit room itself) are now traversable. `RemoteActionPathExpander` post-processes a `PathResult` to linearize the multi-room prerequisite sequence: walk to each prerequisite room → send action command → walk back to exit room → traverse the exit. This converts a single `MultiActionHidden` step into a flat sequence the walker can process without a nested state machine. Item-gated remote actions remain deferred.

**4. Blocking Reasons UI (Phase 5g)** — When no path exists through eligible exits, `WalkToDialog` re-runs BFS with `includeAllExits: true` to find a path through ALL exits. Each non-traversable exit on the unfiltered path is analyzed and displayed with specific blocking reasons: locked doors with key item names resolved from Items.json, level/class/race restrictions with player values shown, teleport conditions (items, skills, alignment), stat-gated doors, and multi-action requirements.

### New/Modified Files (v2.10.0)

| File | Change |
|------|--------|
| `Models.cs` | Added `RemoteAction` and `Teleport` to `RoomExitType` enum; `TeleportConditions` class; `ExitLevelRestriction`, `ExitClassRestriction`, `ExitRaceRestriction` typed restriction classes; fields on `RoomExit`, `PathStep`, `PathRequirements` |
| `RoomGraphManager.cs` | Pass 4 (`BuildTeleportExits`) parses CMD TextBlocks into virtual teleport exits; level/class/race restriction parsing from `ExitExtra`; `_itemIdToName` dictionary for item name lookups; remote-action automatability evaluation in Pass 2b |
| `RemoteActionPathExpander.cs` | New file — linearizes remote-action multi-step sequences into flat walk/action/walk/traverse steps |
| `AutoWalkManager.cs` | Teleport exit handling (sends command like Text exits); remote action step logging |
| `WalkToDialog.cs` | Blocking reasons display via `DescribeBlockingReason()` with item name resolution; `includeAllExits` diagnostic path; path info label updates |
| `GameManager.cs` | `GetExitFilter()` expanded with level, class, and race restriction checks alongside door stat filtering |

---

## Table of Contents

1. [Game Overview](#game-overview)
2. [Application Architecture](#application-architecture)
3. [File Structure](#file-structure)
4. [Auto-Pathing System](#auto-pathing-system)
5. [Core Components](#core-components)
6. [UI Styling Guidelines](#ui-styling-guidelines)
7. [Code Organization](#code-organization)
8. [Development Guidelines](#development-guidelines)
9. [Known Patterns & Solutions](#known-patterns--solutions)
10. [Refactoring History](#refactoring-history)
11. [Version History](#version-history)
12. [Important Notes for AI Assistants](#important-notes-for-ai-assistants)

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
│  (UI Orchestration — Partial Class Split)               │
│                                                         │
│  ┌──────────────────────┐  ┌──────────────────────┐   │
│  │ MenuHandlers.cs      │  │ DisplayUpdates.cs    │   │
│  └──────────────────────┘  └──────────────────────┘   │
└─────────────────────────────────────────────────────────┘
      │            │            │            │
      ▼            ▼            ▼            ▼
┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐
│ Telnet   │ │ Message  │ │ Terminal │ │  Log     │
│ Connect  │ │ Router   │ │ Control  │ │ Renderer │
└──────────┘ └──────────┘ └──────────┘ └──────────┘
                  │
                  ▼
┌──────────────────────────────────────────────────────────┐
│            GameManager (Central Coordinator)             │
│                                                          │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │ PlayerState  │  │ PartyManager │  │ ProfileMgr   │  │
│  │ Manager      │  │              │  │              │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │ Combat Mgr   │  │ Healing Mgr  │  │  Cure Mgr    │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │ Remote Cmd   │  │ Room Tracker │  │ RoomGraph    │  │
│  │ Manager      │  │              │  │ Manager      │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │ AutoWalk Mgr │  │ Loop Manager │  │ CastCoord    │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │ PlayerDB Mgr │  │ MonsterDB    │  │ BuffManager  │  │
│  │              │  │ Manager      │  │              │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
└──────────────────────────────────────────────────────────┘
```

### Component Relationships

```
MainForm (Entry Point — Partial Class Split)
    │
    ├── TelnetConnection (Network Layer)
    │       └── Handles IAC, NAWS, reconnection
    │
    ├── MessageRouter (Message Processing — routes via GameManager)
    │       ├── Combat state detection
    │       ├── HP/Mana parsing
    │       ├── Tick detection
    │       └── Partial line buffering / OnBufferCleared
    │
    ├── TerminalControl (VT100 Terminal)
    │       ├── ScreenBuffer (2D character grid + scrollback history)
    │       └── AnsiVtParser (ANSI/VT100 parsing)
    │
    ├── LogRenderer (Log Display)
    │       └── ANSI color rendering for logs
    │
    ├── GameManager (Central Coordinator — owns all managers)
    │       ├── ProfileManager (character profile persistence)
    │       ├── PlayerStateManager (HP, mana, stats, exp, resting, training)
    │       ├── PartyManager (party tracking, par, health requests)
    │       ├── HealingManager (heal spells, HP threshold rules)
    │       ├── CureManager (ailment detection, cure automation)
    │       ├── CombatManager (enemy detection, attack automation)
    │       ├── RemoteCommandManager (telepath-based remote control)
    │       ├── PlayerDatabaseManager (friend/enemy tracking)
    │       ├── MonsterDatabaseManager (monster data, overrides)
    │       ├── RoomGraphManager (room graph, BFS pathfinding)
    │       ├── RoomTracker (room detection, 7-strategy disambiguation)
    │       ├── AutoWalkManager (walk execution, special exit handling)
    │       ├── LoopManager (waypoint loop execution, lap counting)
    │       ├── BuffManager (buff tracking and configuration)
    │       └── CastCoordinator (priority-based casting across heals/cures/buffs)
    │
    └── GameDataCache (Singleton — accessed directly via GameDataCache.Instance)
            └── Game JSON files (Races, Classes, Items, etc.)
```

### Event Flow

1. **MUD Server → TelnetConnection:** Raw telnet data received
2. **TelnetConnection → MainForm:** Decoded text via OnDataReceived event
3. **MainForm → MessageRouter:** Process message for game state
4. **MainForm → TerminalControl:** Display in VT100 terminal
5. **MessageRouter → Managers:** Route to CombatManager, RoomTracker, etc. via GameManager
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
│   ├── Program.cs                     # Application entry point
│   ├── MainForm.cs                    # UI orchestration
│   ├── MainForm_MenuHandlers.cs       # Menu/button event handlers
│   └── MainForm_DisplayUpdates.cs     # UI refresh methods
│
├── Network Layer
│   └── TelnetConnection.cs            # Telnet protocol, IAC, reconnection
│
├── Message Processing
│   └── MessageRouter.cs               # Combat detection, HP parsing, partial line buffering
│
├── Terminal Emulation
│   ├── TerminalControl.cs             # VT100 terminal (UserControl)
│   ├── ScreenBuffer.cs                # 2D character grid + scrollback
│   ├── AnsiVtParser.cs                # ANSI escape sequence parser
│   ├── TerminalCell.cs                # Character cell struct
│   └── BackscrollDialog.cs            # Scrollback history viewer with search
│
├── Logging
│   ├── LogRenderer.cs                 # ANSI color log rendering
│   └── DebugLogWriter.cs              # Per-session walk/loop debug logging
│
├── Managers
│   ├── GameManager.cs                 # Central coordinator (owns all managers)
│   ├── BuffManager.cs                 # Buff tracking and configuration
│   ├── PlayerStateManager.cs          # HP, mana, stats, experience
│   ├── PartyManager.cs                # Party tracking, auto-invite
│   ├── HealingManager.cs              # Heal spell automation
│   ├── CureManager.cs                 # Ailment cure automation
│   ├── CombatManager.cs               # Enemy detection, attack automation
│   ├── CastCoordinator.cs             # Priority-based casting (heals > cures > buffs)
│   ├── RemoteCommandManager.cs        # Telepath-based remote control
│   ├── PlayerDatabaseManager.cs       # Known player tracking, permissions
│   └── MonsterDatabaseManager.cs      # Monster data, per-character overrides
│
├── Auto-Pathing System
│   ├── RoomGraphManager.cs            # Room graph engine, BFS pathfinding, exit classification
│   ├── RoomTracker.cs                 # Real-time room detection, 7-strategy disambiguation
│   ├── AutoWalkManager.cs             # Walk execution, door/search/teleport state machines
│   ├── RemoteActionPathExpander.cs    # Linearizes remote-action multi-room prerequisite sequences
│   ├── LoopManager.cs                 # Waypoint loop execution, lap counting
│   └── LoopDefinition.cs             # Loop data model, .loop.json file I/O
│
├── Dialogs
│   ├── WalkToDialog.cs                # Walk destination search, path preview, blocking reasons, loop UI
│   ├── LoopDialog.cs                  # Loop editor, live stats, collapse-on-run UI
│   ├── SettingsDialog.cs              # Character settings including Navigation tab
│   ├── PathfindingTestDialog.cs       # Debug: manual path verification
│   ├── PlayerDatabaseDialog.cs        # Known player database management UI
│   └── MonsterDatabaseDialog.cs       # Monster override management UI
│
├── Configuration Dialogs
│   ├── BuffConfigDialog.cs            # Individual buff spell configuration
│   ├── BuffListDialog.cs              # Buff list management
│   ├── HealingConfigDialog.cs         # Healing system configuration
│   ├── HealSpellConfigDialog.cs       # Individual heal spell configuration
│   ├── HealRuleConfigDialog.cs        # Heal rule (HP threshold) configuration
│   ├── CureConfigDialog.cs            # Cure system configuration
│   ├── CureSpellConfigDialog.cs       # Individual cure spell configuration
│   └── AilmentConfigDialog.cs         # Ailment detection pattern configuration
│
├── UI Controls
│   ├── PlayerStatusPanel.cs           # Player HP/mana bars, buff durations, status indicators
│   └── Controls/CombatStatusPanel.cs  # Combat dashboard: tick timer, HP/mana, party list
│
├── UI Helpers
│   ├── MessageType.cs                 # Log message type enum
│   ├── DarkMenuRenderer.cs            # Dark theme menu renderer
│   └── DarkColorTable.cs              # Dark theme colors
│
├── Data
│   ├── Models.cs                      # All data models (buffs, combat, pathing, etc.)
│   ├── GameDataCache.cs               # Singleton game data loader
│   ├── ProfileManager.cs              # Character profile persistence + AppSettings
│   ├── AbilityNames.cs                # Numeric ability ID → name lookup dictionary
│   ├── MdbImporter.cs                 # MDB → JSON game data import engine
│   └── MdbImportDialog.cs             # Import UI with progress bar and logging
│
└── Game Data Viewers
    ├── GameDataViewerDialog.cs        # Searchable data grid browser for game data tables
    ├── GenericDetailDialog.cs         # Fallback detail view (key-value pairs + abilities)
    ├── RoomDialogs.cs                 # Room detail viewer
    ├── ItemDialogs.cs                 # Item detail viewer
    ├── MonsterDialogs.cs              # Monster detail viewer
    ├── SpellDialogs.cs                # Spell detail viewer
    ├── ClassDialogs.cs                # Class detail viewer
    ├── RaceDialogs.cs                 # Race detail viewer
    ├── ShopDialogs.cs                 # Shop detail viewer
    ├── LairDialogs.cs                 # Lair detail viewer
    └── TextBlockDialogs.cs            # Text block detail viewer
```

---

## Auto-Pathing System

### Overview

The auto-pathing system navigates a player through a 56,375-room game world automatically. It handles directional movement, doors (bash/picklock), text command exits, invisible hidden passages, searchable hidden exits, multi-action hidden exits (including remote-action sequences across multiple rooms), TextBlock teleport commands, door action bypasses (levers/switches), and exit restrictions based on player level, class, and race. When no eligible path exists, blocking reasons are displayed with specific details (required items, levels, classes, stats).

### Exit Type Classification (9 Types)

| Exit Type | BFS Traversable | Walk Handling | Example Data |
|-----------|----------------|---------------|--------------|
| **Normal** | ✅ Yes | Send direction command (`n`, `se`, etc.) | `1/298` |
| **Text** | ✅ Yes | Send text command (`go crimson`, etc.) | `1/298 (Text: go crimson, enter crimson)` |
| **Door** | ✅ Yes | Bash/pick then move; configurable strategy | `1/298 (Door)` |
| **Door (stat-gated)** | ✅ Filtered | Player stats checked via `exitFilter`; action bypass if available | `1/298 (Door [1000 picklocks/strength])` + `Action [on E exit...]` |
| **SearchableHidden** | ✅ Yes | Send `search`, parse result, retry on fail, then move | `1/298 (Hidden/Needs 1 Actions, any order)` + `Action#1` |
| **Hidden (Passable)** | ✅ Yes | Send direction command (exit is invisible but passable) | `1/298 (Hidden/Passable)` |
| **MultiActionHidden** | ✅ If automatable | Send action commands with timer delays, then move | `1/298 (Hidden/Needs 3 Actions, any order)` + `Action#1..#3` |
| **RemoteAction** | ✅ Via expander | In-place action command sent in a prerequisite room; `RemoteActionPathExpander` linearizes the multi-room sequence | Injected steps — no room data source |
| **Teleport** | ✅ If filterable | Send text command (same as Text); virtual exit from CMD TextBlock | `1/2684 CMD → TextBlock → teleport 8/1366` |
| **Locked** | ❌ No | No action bypass, 999+ stat requirements, or key/ticket-gated | `1/298 (Door [1000 picklocks/strength])` (no Action entry) |

**Exit Restrictions** (applied via `exitFilter` predicate on any traversable exit):

| Restriction | Filter Behavior |
|-------------|----------------|
| **Level** | Exit has min/max level range; player level must satisfy `IsSatisfiedBy()` |
| **Class** | Exit allows specific class IDs; player class must be in allowed list |
| **Race** | Exit allows specific race IDs; player race must be in allowed list |
| **Door Stats** | Door requires minimum strength or picklocks; bypassed if action bypass exists |

### Key Components

**RoomGraphManager.cs** — Graph engine and pathfinding:
- 56,375 rooms, ~136,500+ total exits parsed (including teleport virtual exits)
- BFS pathfinding in ~2ms
- Exit type classification determines traversability (9 types)
- `FindPath()` returns `PathResult` with `PathStep` list including `ExitType` per step
- Optional `Func<RoomExit, bool>? exitFilter` parameter for stat-gated doors, level/class/race restrictions
- Optional `includeAllExits` flag for blocking reasons diagnostic (includes non-traversable exits in BFS)
- 5-pass graph building:
  - **Pass 1:** Parse all rooms and exits from Rooms.json (exit type classification, restriction parsing)
  - **Pass 2:** Associate Action entries with target exits (MultiActionHidden and Door bypasses)
  - **Pass 2b:** Clean up action data, evaluate automatability, identify remote-action exits
  - **Pass 3:** Apply user-defined overrides from RoomOverrides.json
  - **Pass 4:** Parse CMD TextBlocks into virtual `Teleport` exits (LinkTo chain following, condition parsing)
- Door action bypass: unnumbered `Action [on DIR exit...]` entries stored as `DoorActionBypass` on Door exits
- Item name lookups: `_itemIdToName` dictionary (2,678 items) for key/ticket exit descriptions and blocking reasons
- Level/class/race restrictions: parsed from `ExitExtra` field into typed `ExitLevelRestriction`, `ExitClassRestriction`, `ExitRaceRestriction` objects

**RoomTracker.cs** (~1,200 lines) — Room detection:
- Parses MUD server output to determine current room
- 7-strategy disambiguation (movement prediction, neighbor check, exact name, history, exit matching, tiebreaker, fallback)
- 3-guard suppression system: Guard 1 (no-move redisplay), Guard 2 (party-follow within 750ms), Guard 3 (late follow with prediction check)
- Guards use `IsSubsetOf` (not `SetEquals`) — tolerates extra visible exits from action commands
- `GetDirectionalExitKeys()` — filters `RoomExitType.Text`, `Hidden`, `SearchableHidden`, and `MultiActionHidden` from exit comparisons (none appear in "Obvious exits:")
- Command queue: FIFO `Queue<PendingMove>` replaces single-slot tracking; uses `_currentRoom.Key` at consumption time; 10s staleness pruning, 15-command cap
- HP prompt stripping, TCP reassembly handling, verbose mode support
- `ClearLineBuffer()` for search/door state machine coordination
- `OnBufferCleared` event to sync MessageRouter partial line clearing
- `OnDisconnected()` clears command queue and stale state while preserving current room and history
- `ClearPendingMoves()` clears pending move queue and buffer state without resetting current room (used by timeout re-sync)
- Noise filtering: entity arrivals (`"into the room"`), departures (`"just left to the"`), sneaking messages (`(from|to) the {direction}`), combat actions, command echoes

**AutoWalkManager.cs** — Walk execution:
- State machine: Idle → Walking → WaitingForRoom → WaitingForCombat → Complete/Failed/Disconnected
- Door state machine: sends bash/pick → parses response → opens → moves; door action bypass when player lacks stats (lever/switch/panel)
- Search state machine: sends `search` → parses success/failure → retries → moves
- Multi-action state machine: sends action commands with timer delays → moves
- Teleport exits: sends text command (same as Text exits) — no special walker logic needed
- Remote action steps: sends command and waits delay (no room change expected) — `RemoteActionPathExpander` pre-linearizes the sequence
- Combat pause/resume with RoomTracker→CombatManager direct forwarding
- Combat verification: 5s timer clears stale enemies if combat never engages
- Combat heartbeat: 10s timer resets on damage ticks and combat activity (dodge/miss). On timeout, sends non-aggressive room refresh. Clears stale enemy/player lists (but not `_currentTarget`) so empty rooms correctly read as 0 enemies. Resumes walking if clear, restarts heartbeat if enemies present.
- Disconnect/reconnect: preserves step list and position, resumes in-place after reconnect (with bounds check for edge case)
- Timeout re-sync: on second step timeout, clears pending moves, sends empty command for room redisplay, three-way `FromKey`/`ToKey` check (mirrors disconnect recovery logic) to determine actual position before failing
- `TryResolveNearbyStep()`: 3-pass position resolver (nearby keys, full list, name-based proximity) before recalculation
- Duplicate send prevention: `_lastSentStepIndex` + 500ms guard prevents rapid combat cycling from double-sending
- Recursion guard: prevents infinite loops when position resolution doesn't make progress
- Combat retry counter: resets only on combat start (not every state change) to prevent infinite retry loops
- Configurable `MaxSearchAttempts` from profile settings
- Per-session debug logging via `DebugLogWriter`

**LoopManager.cs** — Loop execution:
- Room-key waypoint sequences for experience grinding
- Continuous cycling with lap counter
- Integrates with AutoWalkManager for path execution between waypoints
- Disconnect/reconnect: preserves loop definition, lap count, gets first resume priority
- Per-session debug logging

### Navigation Settings (SettingsDialog → Navigation Tab)

- **Door Handling:** Bash only / Picklock only / Both (bash first, then pick)
- **Max Search Attempts:** Configurable retry limit for searchable hidden exits (default: 5)
- **Exit Filtering:** Level, class, and race restrictions automatically applied based on current player data; door stat requirements checked against player strength/picklocks
- Settings persisted per character profile

---

## Core Components

### MessageRouter.cs

```csharp
public class MessageRouter
{
    // Events
    public event Action<bool>? OnCombatStateChanged;
    public event Action<int, int, string>? OnPlayerStatsUpdated;  // currentHP, currentMana, manaType
    public event Action? OnCombatTickDetected;
    public event Action? OnPlayerDeath;
    public event Action? OnLoginComplete;
    public event Action<bool>? OnPauseStateChanged;

    // Methods
    public void ProcessMessage(string text);
    public void SetNextTickTime(DateTime nextTick);
    public void ResetLoginPhase();
    public void ClearPartialLine();
}
```

**Responsibilities:**
- Combat state detection (*Combat Engaged* / *Combat Off*)
- HP/Mana parsing from `[HP=100/100/MA=50/50]`
- Combat tick detection via damage clustering
- Death detection
- Login phase tracking
- Partial line buffering for TCP reassembly
- `OnBufferCleared` event coordination with RoomTracker

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

### LogRenderer.cs

Handles all log rendering with ANSI color support:
- ANSI color code parsing for logs
- Automatic log trimming (500KB → 300KB)
- Timestamp support
- Auto-scroll support
- Color brightening for bold codes

### GameManager.cs

Central coordinator (owns all 15 sub-managers):
- Instantiates and wires all managers in constructor
- Inter-manager event wiring (e.g., RoomTracker → CombatManager, Party → CureManager)
- Profile save/load via ProfileManager
- Disconnect/reconnect lifecycle coordination
- Forwards events to MainForm for UI updates
- `GetExitFilter()` builds exit filter predicate checking: door stats (strength/picklocks with action bypass passthrough), level restrictions, class restrictions, race restrictions — with guards to skip filtering when player data isn't loaded

### ProfileManager.cs

Character profile persistence:
- Save/load character profiles as JSON files
- App-level settings persistence (auto-load last character, display preferences)
- Profile path management, unsaved-changes tracking

### PlayerStateManager.cs

Player state tracking:
- HP, mana, combat/resting status, login phase
- Parses stat and exp data from game output via regex
- Fires events on state changes (player info updated, training screen, game exited)

### PartyManager.cs

Party management:
- Party membership, leader status, member health tracking
- Automated party commands (periodic `par`, health requests)
- Detects party join/leave/disband events from game text
- Auto-invite players detected in room

### BuffManager.cs

Buff tracking and configuration:
- Buff configurations and active buff tracking
- Buff recast logic (delegates to CastCoordinator via injected handler)
- Receives PlayerStateManager and PartyManager via constructor injection

### CombatManager.cs

Combat automation:
- Enemy detection from "Also here:" lines (with direct RoomTracker forwarding)
- Attack automation (melee and spell)
- Monster override support, target tracking
- Combat activity detection (dodge/miss patterns) via `OnCombatActivityDetected` event

### HealingManager.cs

Heal spell automation:
- Heal spell configurations with HP threshold rules
- Priority-based heal target selection (self and party)
- Mana reserve awareness

### CureManager.cs

Ailment cure automation:
- Ailment detection from game text (poison, disease, etc.)
- Cure spell configurations with priority ordering
- Party cure support, poison status tracking

### CastCoordinator.cs

Priority-based casting system:
- Orchestrates casting priority: heals > cures > buffs
- Cast timing, cooldown enforcement, failure detection
- Delegates evaluation to HealingManager, CureManager, BuffManager

### RemoteCommandManager.cs

Telepath-based remote control:
- Processes commands from other players via telepath/say/gangpath messages
- Permission checking through PlayerDatabaseManager before execution
- Supports toggling combat/heal/buff, reporting stats, hangup/relog

### PlayerDatabaseManager.cs

Known player tracking:
- In-memory database of known players with CRUD operations
- Per-player data: alignment, class, permission level
- Populated from character profiles and in-game `who` command

### MonsterDatabaseManager.cs

Monster data management:
- Loads monster data from imported MajorMUD game data JSON
- Per-character monster overrides (custom danger levels, attack preferences)
- Overrides stored in character profile

---

## UI Styling Guidelines

### Dark Theme Color Palette

```csharp
Background:      Color.FromArgb(45, 45, 45)      // Main background
Panel/GroupBox:  Color.FromArgb(50, 50, 50)      // Panels
Input Fields:    Color.FromArgb(60, 60, 60)      // Text boxes
Buttons:         Color.FromArgb(60, 60, 60)      // Standard buttons
Active/Selected: Color.FromArgb(70, 100, 130)    // Selected items
Text:            Color.White                       // Primary text
Secondary Text:  Color.LightGray                   // Labels, descriptions
```

All UI uses `FlatStyle.Flat` for buttons and consistent dark theme throughout.

---

## Code Organization

### Partial Class Pattern

```csharp
// MainForm.cs - Core logic
public partial class MainForm : Form
{
    private TelnetConnection _telnetConnection;
    // ... core initialization and orchestration
}

// MainForm_MenuHandlers.cs - Event handlers
public partial class MainForm
{
    private void ConnectMenuItem_Click(object? sender, EventArgs e) { }
    private void SaveCharacter_Click(object? sender, EventArgs e) { }
}

// MainForm_DisplayUpdates.cs - UI updates
public partial class MainForm
{
    private void RefreshBuffDisplay() { }
    private void RefreshPartyDisplay() { }
}
```

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

### Nullable Reference Types

All fields must be initialized or marked with `= null!;`:

```csharp
private Label _someLabel = null!;  // Initialized in InitializeComponent
private TelnetConnection _telnetConnection = null!;  // Initialized in constructor
```

### Adding New Features

1. **New Manager:** Create class, inject into GameManager constructor, wire events
2. **New UI Component:** Extract to separate UserControl or Form
3. **New Network Feature:** Add to TelnetConnection.cs
4. **New Message Processing:** Add to MessageRouter.cs
5. **New Display Logic:** Add to MainForm_DisplayUpdates.cs
6. **New Menu Handler:** Add to MainForm_MenuHandlers.cs

---

## Known Patterns & Solutions

### Buffer Clearing for State Machine Coordination

```csharp
// When search/door state machines need clean room detection:
_roomTracker.ClearLineBuffer();  // Clears room detection buffer
// OnBufferCleared event fires → MessageRouter clears _partialLine
// Prevents HP prompt fragments from contaminating next room detection
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

### Version 2.10.0 — TextBlock Teleport Exits + Level/Class/Race Restrictions + Remote Actions + Blocking Reasons (March 2026)

- **TextBlock teleport exits (Phase 5f):** New Pass 4 in `BuildGraph()` parses CMD TextBlocks into virtual `Teleport` exits. 1,071 rooms have CMD fields linking to TextBlock entries. Parser follows LinkTo chains (with cycle detection), splits action lines, extracts user commands and conditions (level, items, skills, alignment, monsters), and creates `RoomExit` objects with `ExitType = Teleport`. Unconditional and level-only-gated teleports are BFS-traversable. Level gates promoted to standard `ExitLevelRestriction` for filter handling.
- **`TeleportConditions` model:** New class tracking all teleport conditions (`MinLevel`, `MaxLevel`, `RoomItemId`, `CheckItemId`, `RequiresNoMonsters`, `RequiresMonster`, `TestSkill`, `RequiresAbilityCheck`, `RequiresEvil`, `RequiresGood`). `IsGraphTimeFilterable` returns true when only level conditions exist (no runtime state needed).
- **Level/class/race exit restrictions (Phase 5d):** Directional exits now support `ExitLevelRestriction` (min/max level), `ExitClassRestriction` (allowed class IDs), and `ExitRaceRestriction` (allowed race IDs) parsed from `ExitExtra` field. Typed model classes with `IsSatisfiedBy()` methods. `GameManager.GetExitFilter()` expanded to check all three, with guards when player data not loaded.
- **Remote action path orchestration (Phase 5e):** `RemoteActionPathExpander` post-processes `PathResult` to linearize remote-action exit sequences. Converts a single `MultiActionHidden` step requiring remote prerequisite rooms into: walk to prereq room → send action → walk back to exit → traverse. New `RemoteAction` exit type for injected in-place action steps.
- **Blocking reasons UI (Phase 5g):** `WalkToDialog.DescribeBlockingReason()` analyzes non-traversable exits when filtered path fails. Handles 8+ exit type/restriction cases: locked doors (with key item names from Items.json), searchable hidden, multi-action (items, remote, deferred), teleport conditions (level, items, skills, alignment, monsters), stat-gated doors, level/class/race restrictions with player values shown.
- **`includeAllExits` BFS flag:** `FindPath()` accepts `includeAllExits: true` to include non-traversable exits in BFS, used by blocking reasons to find any path and identify which exits block the player.
- **Item name resolution:** `_itemIdToName` dictionary (2,678 items from Items.json) provides human-readable names for key items, ticket items, and teleport condition items in blocking reasons and exit descriptions.
- **Item/Ticket exits as Locked:** Exits requiring a key item (`DoorKey > 0`) or a ticket (`Param > 0` on specific exit types) are classified as `Locked` with the item name stored in `LockDescription`.
- **Teleport blocking reason fix:** Level-gated teleports now correctly show "Requires level X+" in blocking reasons instead of "unknown condition" — the level restriction is checked from `TeleportConditions` when the exit's `LevelRestriction` is null.

### Version 2.9.5 — Room Tracker Noise Filters + Timeout Re-sync (February 2026)

- **Player departure message filters:** Added `"just left to the"` filter for `"{Player} just left to the {direction}"` departure messages. Expanded `"You notice"` sneaking regex from `from the {direction}` to `(from|to) the {direction}` to catch both arrival (`"sneak in from the"`) and departure (`"sneaking out to the"`) patterns. These messages were corrupting the room detection buffer, causing room name parsing to return empty and desyncing the room tracker from the character's actual position.
- **Timeout re-sync:** On second step timeout, AutoWalkManager now attempts a room re-sync before failing. Mirrors the disconnect recovery pattern: clears stale pending moves via `RoomTracker.ClearPendingMoves()`, sends an empty command to trigger a room redisplay, then performs a three-way check comparing the detected room against the step's `FromKey` and `ToKey`. If the step actually completed (`ToKey` match), advances and continues. If still at `FromKey` or ambiguous, fails normally so LoopManager/UI can retry from the correct position. Includes a 10-second safety timeout.
- **`RoomTracker.ClearPendingMoves()`:** New public method that clears the pending move queue and line buffer without resetting the current room or history. Used by timeout re-sync to ensure the next room display is treated as a fresh detection rather than consumed as movement confirmation.

### Version 2.9.4 — Door Action Bypass + Guard Fix + UI Improvements (February 2026)

- **Guard bypass fix:** Guards 1/2/3 in `RoomTracker.ProcessRoomDetection()` changed from `SetEquals` to `IsSubsetOf` — database directional exits must be a subset of visible exits, tolerating extra visible exits from action commands. `GetDirectionalExitKeys()` now excludes `MultiActionHidden` (Phase 5e omission).
- **BFS door stat filtering:** Added `Func<RoomExit, bool>? exitFilter` parameter to `FindPath()`. Filter checks player strength/picklocks against `DoorStatRequirement`, allowing doors the player can traverse or those with action bypasses.
- **Door action bypass:** Pass 2 of `BuildGraph()` expanded to recognize unnumbered `Action [on DIR exit...]` entries targeting Door exits. Commands stored as `DoorActionBypass` on `RoomExit`. AutoWalkManager sends bypass command (lever/switch/panel) with timer delay when player lacks stats to bash/pick.
- **Current room display:** `_roomLabel` in MainForm status bar shows `"Current Room: 9 / 68"` (white) or `"Current Room: ? / ?"` (gray). Lifecycle-aware: updates on profile load, game entry, room changes, disconnect.
- **WalkToDialog UI:** Fixed focus stealing (`ActiveForm == this` guard), added map/room number input (regex → `GetRoom()` lookup), prevented multiple instances (field tracking + `Activate()`), fixed positioning (`Manual` + `OnLoad()` centering).

### Version 2.9.3 — Combat Heartbeat Safety Net + Loop Editor UI (February 2026)

- **Combat Heartbeat:** 10-second damage heartbeat timer in `WaitingForCombat`. Resets on damage ticks (`OnCombatTickDetected`) and combat activity messages (dodge/miss patterns via `OnCombatActivityDetected`). On timeout, sends non-aggressive room refresh to check if combat has ended. Resumes walking if room is clear, restarts heartbeat if enemies present.
- **Combat activity detection:** `CombatManager` now detects dodge/miss patterns (`but you dodge!`, `but he dodges!`, `but she dodges!`, `at you`, `at {playerName}`) and fires `OnCombatActivityDetected` event. Covers ~99% of non-damaging combat messages as supplemental heartbeat signals.
- **Empty room fix:** `ClearAlsoHereDedup()` expanded to also clear `_currentRoomEnemies` and `_playersInRoom`. When server responds without "Also here:" (empty room), stale enemy lists no longer cause infinite heartbeat loops. Preserves `_currentTarget` to prevent harmful re-attack commands that change combat attack order.
- **Loop Editor — center on parent:** `FormStartPosition.Manual` with `OnLoad` override that centers on `Owner.Bounds`, clamped to screen working area. Fixes wrong-monitor placement for modeless dialogs.
- **Loop Editor — collapse on run:** When loop starts, dialog collapses to show only loop name, status panel, and control buttons. Steps list, add-room controls, validation label, and notes are hidden. Form locks to collapsed size. Expands back on stop or failure.

### Version 2.9.2 — Combat Verification Safety Net (February 2026)

- **`OnCombatStateChanged(true)` fix:** After `StopTimers()`, restarts the combat verification timer. Previously, the timer was killed and never restarted, leaving the walker stuck forever if `*Combat Off*` was never detected (1,219 laps soak test failure — walker stuck ~2 minutes until manually stopped).
- **`OnCombatVerificationRecheck` fix:** When enemies are confirmed present after a room refresh, restarts the verification timer to schedule another check. Previously returned with no timer, creating the same permanent-stuck condition.

### Version 2.9.1 — Hidden Exit Contamination Fix (February 2026)

- **`GetDirectionalExitKeys()` expanded:** Now excludes `Hidden` and `SearchableHidden` exit types in addition to `Text`. All three never appear in "Obvious exits:", so including them caused `SetEquals` to always fail for rooms with non-visible exits, breaking Guard 1/2/3 suppression and causing phantom room displays to corrupt the tracker's position in same-name areas (Darkwood Forest at 188 laps).
- **Strategy 1.5 fix:** Neighbor check now uses `GetDirectionalExitKeys()` instead of raw `room.Exits` for exit comparison, preventing false negatives for rooms with hidden exits.
- **Strategy 4 fix:** Exit matching now uses `GetDirectionalExitKeys()` instead of raw `room.Exits` for candidate scoring, improving disambiguation accuracy in areas with hidden exits.

### Version 2.9.0 — Same-Name Room Guards + Command Queue (February 2026)

- **Guard 1/2 exit comparison:** Both guards now compare directional exits (via `GetDirectionalExitKeys()`) before suppressing same-name room displays, fixing false suppression for rooms sharing names but having different exits
- **Guard 3 (new):** Late follow redisplay suppression — catches party-follow room displays arriving beyond 750ms by comparing movement prediction name to display name
- **Strategy 1.5 neighbor check:** Login desync recovery — when save file restores wrong room, checks neighbors for name + exit match instead of falling through to wrong strategy
- **Text exit contamination fix:** `GetDirectionalExitKeys()` excludes `RoomExitType.Text` from all exit comparisons — text exits never appear in "Obvious exits:" display, so including them caused `SetEquals` to always return false
- **Command queue:** FIFO `Queue<PendingMove>` replaces single-slot `_pendingMoveCommand`/`_pendingMoveFromKey` — handles rapid manual navigation without losing commands; uses `_currentRoom.Key` at consumption time; 10s staleness pruning; 15-command cap
- **Duplicate send prevention:** `_lastSentStepIndex` + 500ms guard prevents rapid combat cycling from sending same walk step twice
- **Recursion guard:** Prevents infinite recursion in `SendNextStep()` when position resolution doesn't make progress
- **Reconnect bounds check:** Prevents index-out-of-range crash when step index is past end of step list after disconnect
- **Combat-pause log fix:** Moved "Auto-walk paused" log message before state change so it actually fires
- **Combat retry reset fix:** `_combatResumeRetries` only resets on combat start, not on every state change — prevents infinite retry loops in areas with rapid combat cycling

### Version 2.8.0 — Disconnect/Reconnect Resume + Combat Verification Fix (February 2026)

- **Disconnect/reconnect resume:** Walks and loops survive server disconnections — state preserved, 5s startup delay, automatic resume
- **Combat verification timer:** 5-second timer in `WaitingForCombat` clears stale enemies, sends room refresh, resumes if clear — eliminates stuck-in-combat bug (912+ laps clean)
- **TryResolveNearbyStep:** 3-pass position resolver (nearby keys ±3, full step list, name-based proximity) corrects off-by-one drift from disconnect without full path recalculation
- **RoomTracker.OnDisconnected():** Clears stale pending move commands that caused false room advancement in duplicate-name areas
- **GameManager disconnect chain:** `OnDisconnected()` calls managers in order (RoomTracker first), `OnLoginComplete()` waits 5s then resumes loop or walk
- **WalkToDialog non-modal:** Changed from `ShowDialog()` to `Show()` so main window remains interactive during walks

### Version 2.7.0 — Phase 5c Searchable Hidden Exits + Bug Fixes (February 2026)

- **Phase 5c complete:** Searchable hidden exits with `search` command, success/failure parsing, configurable retry attempts
- **SearchableHidden enum value** added to `RoomExitType` in Models.cs
- **Search state machine** in AutoWalkManager — sends search, waits for response, retries on failure, moves on success
- **Buffer pollution fix:** `RoomTracker.ClearLineBuffer()` and `MessageRouter._partialLine` clearing via `OnBufferCleared` event
- **HP prompt concatenation fix:** Search responses glued to HP prompts now handled correctly
- **Walker race condition improvement:** RoomTracker→CombatManager direct "Also here:" forwarding before `OnRoomChanged` fires

### Version 2.6.0 — Phase 5a/5b + Loop System (February 2026)

- **Phase 5a complete:** Text command exits and hidden/passable exits enabled in BFS pathfinding
- **Phase 5b complete:** Door exits with bash/picklock state machine, Navigation settings tab, stat-gated door requirements display
- **Loop System complete:** LoopManager with room-key waypoints, continuous cycling, lap counter
- **Debug logging:** DebugLogWriter utility class, per-session logs to `Loops/` directory
- **Text exit disambiguation fix** in RoomTracker
- **101-lap test validated** with debug logging

### Version 2.5.0 — Auto-Pathing Operational (February 2026)

- AutoWalkManager, WalkToDialog, RoomTracker hardened with 10 bug fixes
- 56,375 rooms, 131,538 traversable exits, BFS pathfinding in ~2ms
- Reliably walks multi-step routes through hostile areas

### Version 2.4.0 — AppSettings Consolidation (February 2026)
- AppSettings.cs consolidated into ProfileManager.cs
- Combat re-attack bug fix in CombatManager

### Version 2.3.0 — CastCoordinator Extraction (February 2026)
- **Created:** `CastCoordinator.cs` — priority-based casting system extracted from BuffManager

### Version 2.2.0 — GameManager Architecture (February 2026)
- **Created:** `GameManager.cs` — central coordinator replacing BuffManager-as-hub pattern

### Version 2.0.0 — Major Refactoring (February 2026)

**Objective:** Reduce MainForm.cs from 4,552 lines to manageable size.

| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| MainForm.cs Lines | 4,552 | ~600 | **87% reduction** |
| Number of Files | 1 large | 14 focused | **Better organization** |
| Build Warnings | 9 | 0 | **100% clean** |

---

## Version History

| Version | Changes |
|---------|---------|
| **2.10.0** | **TextBlock Teleport Exits + Level/Class/Race Restrictions + Remote Actions + Blocking Reasons UI** — Pass 4 parses CMD TextBlocks into virtual Teleport exits (1,071 rooms with CMD). Level/class/race restrictions on directional exits with typed model classes and `IsSatisfiedBy()`. `RemoteActionPathExpander` linearizes multi-room prerequisite sequences. Blocking reasons UI shows specific conditions (items, levels, classes, stats, teleport conditions) when no eligible path exists. Item name resolution from Items.json. `includeAllExits` BFS flag for diagnostic paths. |
| **2.9.5** | **Room Tracker Noise Filters + Timeout Re-sync** — Player departure messages (`"just left to the"`, `"sneaking out to the"`) now filtered from room detection buffer. Expanded sneaking regex from `from the` to `(from\|to) the` to catch both arrivals and departures. Timeout re-sync: on second step timeout, clears pending moves, sends empty command for room redisplay, three-way FromKey/ToKey check (mirrors disconnect recovery) before failing. New `ClearPendingMoves()` on RoomTracker. |
| **2.9.4** | **Door Action Bypass + Guard Fix + UI Improvements** — Guards 1/2/3 changed to `IsSubsetOf`, `MultiActionHidden` excluded from exit comparisons. BFS door stat filtering via `exitFilter` predicate. Door action bypass (levers/switches) recognized and executed when player lacks stats. Current room status display on status bar. WalkToDialog: focus fix, map/room input, single instance, positioning. |
| **2.9.3** | **Combat Heartbeat Safety Net + Loop Editor UI** — 10s damage heartbeat timer resets on combat activity (damage + dodge/miss). On timeout, non-aggressive room refresh checks if combat ended. Empty room fix: `ClearAlsoHereDedup()` clears stale enemy/player lists while preserving `_currentTarget`. Loop Editor: centers on parent monitor, collapses to minimal view during execution. |
| **2.9.2** | **Combat verification safety net** — `OnCombatStateChanged(true)` now restarts the verification timer after `StopTimers()`. `OnCombatVerificationRecheck` restarts timer when enemies confirmed. Fixes permanent WaitingForCombat deadlock when `*Combat Off*` is never detected (1,219 lap soak test). |
| **2.9.1** | **Hidden exit contamination fix** — `GetDirectionalExitKeys()` now excludes Hidden + SearchableHidden exits (not just Text). Strategy 1.5 and Strategy 4 use filtered exits instead of raw exits. Fixes Guard 1/2/3 suppression failure in rooms with hidden exits that caused tracker position corruption during combat pause in same-name areas. |
| **2.9.0** | **Same-name room guards + command queue** — Guards 1/2 hardened with exit comparison, Guard 3 added for late follow redisplays, Strategy 1.5 neighbor check for login desync, text exit contamination fix, FIFO command queue for rapid navigation, comprehensive audit fixes (reconnect crash, combat log, retry reset, duplicate send, recursion guard). |
| **2.8.0** | **Disconnect/reconnect resume + combat fix** — Walks and loops survive disconnections with 5s resume delay. Combat verification timer eliminates stuck-in-combat (912+ laps). TryResolveNearbyStep 3-pass position resolver. WalkToDialog non-modal. |
| **2.7.0** | **Phase 5c + bug fixes** — Searchable hidden exits with retry logic, buffer pollution fix, HP prompt concatenation fix, walker race condition improvement. |
| **2.6.0** | **Phase 5a/5b + Loop System** — Text/passable exits enabled, door bash/pick state machine, Navigation settings tab, loop waypoint system, 101-lap validation. |
| **2.5.0** | **Auto-pathing operational** — 10 bug fixes, reliably walks through hostile areas. |
| **2.4.0** | **AppSettings consolidated** into ProfileManager, combat re-attack bug fix |
| **2.3.0** | **CastCoordinator extracted** — priority-based casting system |
| **2.2.0** | **GameManager created** — central coordinator |
| **2.1.0** | **BuffManager decomposition** — Extracted sub-managers |
| **2.0.0** | **Major refactoring complete** — MainForm reduced 87%. Zero warnings. |
| 1.0.0 | Code reorganization, comprehensive knowledge base |
| 0.9.0 | Direct telnet, ANSI colors, logon automation, BBS settings |
| 0.8.x | Character profiles, combat system, healing, curing |
| 0.7.x | Buff management, party tracking |

---

## Important Notes for AI Assistants

1. **GameManager is the central coordinator** — Owns all 15 sub-managers. Access via `_gameManager.CombatManager`, `_gameManager.RoomTracker`, etc. MainForm's `_buffManager` is a shortcut reference to `_gameManager.BuffManager`.
2. **BuffManager is focused** — Only handles buff tracking/configuration. It does NOT own other managers.
3. **Code is now highly modular** — Look for logic in appropriate extracted classes
4. **MainForm is a partial class** — Check `MainForm_MenuHandlers.cs` and `MainForm_DisplayUpdates.cs` for methods
5. **Network logic is in TelnetConnection** — Don't add network code to MainForm
6. **Message processing is in MessageRouter** — Don't add parsing to MainForm
7. **Terminal rendering is in TerminalControl** — Complete VT100 emulator with scrollback
8. **Log rendering is in LogRenderer** — ANSI color support for logs
9. **Dark theme is mandatory** — All UI uses consistent color palette
10. **Zero warnings policy** — All nullable references must be initialized or marked `= null!`
11. **Combat ticks are critical** — Timing handled by MessageRouter
12. **Character profiles are comprehensive** — ALL settings in one JSON file
13. **Auto-pathing handles 9 exit types** — Normal, Text, Door, SearchableHidden, Hidden/Passable, MultiActionHidden (automatable), RemoteAction (injected prerequisite steps), and Teleport (CMD TextBlock virtual exits) are all traversable. Stat-gated doors are filtered by player stats but traversable via action bypass (lever/switch) when available. Level/class/race restrictions filter exits based on player data. Locked doors (key/ticket-gated or no action bypass), non-automatable multi-action exits, and runtime-conditional teleports (items, skills, alignment) remain excluded.
14. **RoomTracker is complex (~1,200 lines)** — 7 disambiguation strategies, 3 suppression guards, command queue, HP prompt stripping, TCP reassembly. Changes require careful verification.
15. **Buffer clearing coordination** — `ClearLineBuffer()` + `OnBufferCleared` must fire to prevent contamination between room detection cycles.
16. **Disconnect/reconnect resume** — Walks and loops survive disconnections. RoomTracker clears command queue, GameManager chains resume with 5s delay, TryResolveNearbyStep handles drift. Timeout re-sync uses the same FromKey/ToKey pattern to recover from tracker desync during walk step timeouts.
17. **Combat verification timer** — 5s timer in WaitingForCombat clears stale enemies. Runs continuously: restarted after combat engages and after each recheck that finds enemies. This ensures the walker never gets permanently stuck if `*Combat Off*` is missed.
18. **Combat heartbeat** — 10s damage heartbeat timer resets on damage ticks and combat activity (dodge/miss messages). On timeout, `ClearAlsoHereDedup()` clears enemy/player lists (but NOT `_currentTarget`) and sends a room refresh. If room is clear, walker resumes. If enemies present, heartbeat restarts. `_currentTarget` must be preserved to prevent the "already fighting" guard from being bypassed — sending a redundant attack changes combat attack order (monster attacks first).
19. **WalkToDialog is non-modal** — Uses `Show()` not `ShowDialog()` so main window stays interactive during walks.
20. **Command queue** — `_pendingMoveQueue` (`Queue<PendingMove>`) replaces single-slot `_pendingMoveCommand`. Uses `_currentRoom.Key` at consumption time (not pre-computed FromKey). 10s staleness pruning, 15-command cap. Dequeues one entry per room detection.
21. **Guard system** — Guard 1 (no-move redisplay), Guard 2 (party-follow within 750ms), Guard 3 (late follow with prediction). All use `GetDirectionalExitKeys()` to exclude non-visible exits and `IsSubsetOf` (not `SetEquals`) to tolerate extra visible exits from action commands.
22. **Non-visible exit filtering** — `RoomExitType.Text`, `Hidden`, `SearchableHidden`, and `MultiActionHidden` exits never appear in "Obvious exits:" display. `GetDirectionalExitKeys()` must be used for ANY exit comparison against visible exits. Guards use `IsSubsetOf` (database exits ⊆ visible exits) to tolerate extra visible exits.
23. **Loop Editor dialog** — `LoopDialog.cs` creates/edits/runs loops. Centers on parent form via manual positioning in `OnLoad`. Collapses to minimal view (name + status + buttons) when loop starts; expands on stop/fail. Each open creates a new instance — no state persistence between opens.
24. **Exit filtering** — `FindPath()` accepts optional `Func<RoomExit, bool>? exitFilter` predicate. `GameManager.GetExitFilter()` builds a combined filter checking: door stats (strength/picklocks with action bypass passthrough), level restrictions, class restrictions, and race restrictions. Guards skip each check when player data isn't loaded. AutoWalkManager and WalkToDialog both use this filter.
25. **Door action bypass** — Unnumbered `Action [on DIR exit...]` entries target Door exits (not MultiActionHidden). Pass 2 stores commands as `DoorActionBypass` on `RoomExit`. AutoWalkManager sends bypass command with timer delay only when player lacks stats — if player can bash/pick, normal door mechanics are used.
26. **Current room status display** — `_roomLabel` in MainForm status bar shows map/room number. Lifecycle-aware: profile load, game entry, room changes, disconnect. Located at Point(200, 5) in the status panel.
27. **WalkToDialog/LoopDialog instance tracking** — `_walkToDialog` and `_loopDialog` fields on MainForm prevent multiple instances. Click handlers use `Activate()` to bring existing dialog to front. `FormClosed` event nulls the reference.
28. **WalkToDialog map/room input** — `PerformSearch()` detects `map/room` patterns (e.g., "9/62") via `RoomKeyRegex` and routes to `GetRoom(map, room)` direct lookup instead of name search.
29. **TextBlock teleport exits** — Pass 4 in `BuildGraph()` parses CMD TextBlocks into virtual `Teleport` exits. Follows LinkTo chains with cycle detection, splits action lines by `\n`, extracts user command (text before first `:`), parses conditions between colons, extracts destination from `teleport MapNum RoomNum`. Deduplicates by destination (keeps shortest command). `TeleportConditions.IsGraphTimeFilterable` determines traversability — only level-gated teleports pass.
30. **Level/class/race restrictions** — `ExitLevelRestriction`, `ExitClassRestriction`, `ExitRaceRestriction` are typed model classes on `RoomExit`. Parsed from `ExitExtra` field in Pass 1. Each has `IsSatisfiedBy()` method. Teleport level gates are promoted to `ExitLevelRestriction` in Pass 4. All checked in `GameManager.GetExitFilter()`.
31. **Remote action path expansion** — `RemoteActionPathExpander.ExpandRemoteActions()` post-processes `PathResult` to replace single `MultiActionHidden` steps with linearized sequences: sub-path to each prerequisite room → `RemoteAction` step (send command, no room change) → sub-path back to exit room → original exit traversal. Requires `RoomGraphManager` for sub-path BFS.
32. **Blocking reasons** — `WalkToDialog.DescribeBlockingReason()` provides human-readable reasons when exits on a diagnostic path are non-traversable. Item names resolved from `_itemIdToName` dictionary. Handles: Locked (key items), SearchableHidden, MultiActionHidden (item-gated, remote, deferred), Teleport (8 condition types), Door (stat-gated), LevelRestriction, ClassRestriction, RaceRestriction. Fallback for unknown restrictions.
33. **`includeAllExits` BFS mode** — `FindPath(includeAllExits: true)` includes non-traversable exits (Locked, non-filterable Teleport, etc.) in BFS. Used by WalkToDialog to find any path when the filtered path fails, then analyze blocking reasons for each non-traversable step.
34. **Item name dictionary** — `RoomGraphManager._itemIdToName` maps item IDs to names from Items.json (2,678 items). Accessed via `GetItemName(id)`. Used for key/ticket lock descriptions and teleport condition item names in blocking reasons.

### When Adding New Features

- **Network features** → Add to `TelnetConnection.cs`
- **Message processing** → Add to `MessageRouter.cs`
- **Player state tracking** → Add to `PlayerStateManager.cs`
- **Party features** → Add to `PartyManager.cs`
- **UI event handlers** → Add to `MainForm_MenuHandlers.cs`
- **Display updates** → Add to `MainForm_DisplayUpdates.cs`
- **Core orchestration** → Add to `MainForm.cs`
- **Game logic** → Add to appropriate Manager class
- **Auto-pathing / walk execution** → Add to `AutoWalkManager.cs`
- **Loop execution** → Add to `LoopManager.cs`
- **Room detection / disambiguation** → Add to `RoomTracker.cs` (verify with diff — regressions are common)
- **Room graph / pathfinding** → Add to `RoomGraphManager.cs`
- **UI components** → Create new UserControl or Form

---

*This document provides comprehensive context for AI assistants working on this project. Version 2.10.0 adds TextBlock teleport exits (CMD→TextBlock→virtual Teleport exit on room graph), level/class/race exit restrictions with typed model classes, remote action path orchestration via RemoteActionPathExpander, blocking reasons UI with item name resolution, and exit filter expansion to cover all restriction types. See AutoPathing_Plan.md for the full pathing plan.*
