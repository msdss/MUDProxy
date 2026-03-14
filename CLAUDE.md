# MUD Proxy Viewer — Claude Code Instructions

> .NET 8.0 WinForms combat automation client for MajorMUD (ParaMUD/GreaterMUD)
> See `../README.md` for full architecture reference, `../AutoPathing_Plan.md` for pathing details

## Build

```
dotnet build
```

Zero warnings policy — all builds must produce 0 warnings, 0 errors.

## Architecture Rules

- **GameManager** is the central coordinator — owns all 16 sub-managers. Access via `_gameManager.CombatManager`, `_gameManager.RoomTracker`, etc.
- **MainForm** is a partial class split across 3 files: `MainForm.cs` (core), `MainForm_MenuHandlers.cs` (events), `MainForm_DisplayUpdates.cs` (UI refresh)
- **BuffManager** only handles buff tracking — it does NOT own other managers
- **GameDataCache** is a singleton accessed via `GameDataCache.Instance`
- **Event-driven architecture** — managers communicate via C# events, not direct calls between peers

## Coding Conventions

- C# .NET 8.0 with `<Nullable>enable</Nullable>`
- All fields must be initialized or marked `= null!;` (e.g., `private Label _someLabel = null!;`)
- Dark theme mandatory: background `Color.FromArgb(45, 45, 45)`, `FlatStyle.Flat` buttons
- WinForms dialogs center on parent via `FormStartPosition.Manual` + `OnLoad` override
- Thread-safe UI: always check `InvokeRequired` / `BeginInvoke` for cross-thread updates

## Where to Add Code

| Feature Area | File |
|---|---|
| Network/telnet | `TelnetConnection.cs` |
| Message parsing | `MessageRouter.cs` |
| Player state | `PlayerStateManager.cs` |
| Combat automation | `CombatManager.cs` |
| Room graph/pathfinding | `RoomGraphManager.cs` |
| Room detection | `RoomTracker.cs` (changes here regress easily — verify carefully) |
| Walk execution | `AutoWalkManager.cs` |
| Remote action linearization | `RemoteActionPathExpander.cs` |
| Loop execution | `LoopManager.cs` |
| Walk UI / blocking reasons | `WalkToDialog.cs` |
| Exit filter (level/class/race/stats) | `GameManager.cs` → `GetExitFilter()` |
| Data models | `Models.cs` |
| Menu handlers | `MainForm_MenuHandlers.cs` |
| Health/mana automation | `HealthManager.cs` |
| Party wait coordination | `PartyManager.cs` (wait logic), `RemoteCommandManager.cs` (@wait/@ok recognition) |
| Display updates | `MainForm_DisplayUpdates.cs` |

## Auto-Pathing Key Concepts

**9 exit types** in `RoomExitType` enum: Normal, Door, Locked, Hidden, SearchableHidden, Text, MultiActionHidden, RemoteAction, Teleport

**5-pass graph build** in `RoomGraphManager.BuildGraph()`:
1. Pass 1 — Parse rooms/exits from Rooms.json (type classification, restriction parsing)
2. Pass 2 — Associate Action entries with target exits
3. Pass 2b — Evaluate automatability, identify remote-action exits
4. Pass 3 — Apply user overrides from RoomOverrides.json
5. Pass 4 — Parse CMD TextBlocks into virtual Teleport exits

**Exit filter** (`GameManager.GetExitFilter()`): checks door stats, LevelRestriction, ClassRestriction, RaceRestriction — with guards when player data not loaded

**Blocking reasons**: When no filtered path exists, `WalkToDialog` re-runs BFS with `includeAllExits: true` and displays per-exit blocking reasons via `DescribeBlockingReason()`

**Non-visible exits**: Text, Hidden, SearchableHidden, MultiActionHidden never appear in "Obvious exits:". Use `GetDirectionalExitKeys()` for ANY exit comparison against visible exits.

## Critical Patterns

**Buffer clearing** — `RoomTracker.ClearLineBuffer()` + `OnBufferCleared` event must fire together to prevent contamination between room detection cycles

**Guard system** — Guards 1/2/3 in RoomTracker suppress false room detections. All use `GetDirectionalExitKeys()` and `IsSubsetOf` (not `SetEquals`)

**Combat heartbeat** — 10s timer resets on damage/dodge/miss. `ClearAlsoHereDedup()` clears enemy/player lists but NOT `_currentTarget` (preserves attack order)

**Disconnect resume** — Walks and loops survive disconnections. RoomTracker clears command queue, GameManager chains resume with 5s delay

**Health management** — HealthManager evaluates 7 priorities: Hang > Run > Meditate (priority) > Rest HP > Meditate/Rest Mana > Stop Resting > Stop Meditating. `ShouldPauseWalking` pauses AutoWalkManager without blocking CastCoordinator. `_restingForMana` flag tracks whether rest was triggered for HP (Priority 4) or mana (Priority 5) so Priority 6 checks the correct max threshold.

**Post-combat delay** — 250ms delay in HealthManager.Evaluate() after `*Combat Off*` prevents rest/meditate on stale enemy data. `_postCombatPauseWalking` keeps walking paused during the delay. Deferred timer in OnCombatEnded() calls Evaluate() when delay expires.

**ALL OFF gating** — HealthManager.Evaluate(), OnCombatEnded(), OnRestingStateChanged() check `_isAnyAutomationEnabled()`. PartyManager.CheckParCommand() and OnCombatTick() check `_isAutomationEnabled()`. Hangup has its own `AllowHangInAllOff` gate above the ALL OFF check.

**Party wait coordination** — Party members coordinate rest stops via `@Wait`/`@Ok` telepath commands. **Leader side:** `RemoteCommandManager` recognizes `@wait`/`@ok` (bypasses `HasPermission`), fires events → `GameManager` wires to `PartyManager.HandleWaitCommand()`/`HandleOkCommand()`. Tracks waiting members in `_waitingMembers` HashSet; all must send `@Ok` before `ShouldPauseForPartyWait` clears. Proactive health monitoring: `ShouldPauseForPartyWait` also checks `EffectiveHealthPercent < PartyWaitHealthThreshold` dynamically. Configurable timeout (`PartyWaitTimeoutMinutes`, default 2m) silently resumes. **Follower side:** `HealthManager.OnHealthActionChanged` → `PartyManager.OnHealthActionChanged()` sends `/{leaderName} @Wait` on Resting/Meditating, `/{leaderName} @Ok` on recovery. `_followerWaitSent` flag prevents duplicate sends. `_partyLeaderName` captured from "You are now following {name}" message. **Pause mechanism:** `AutoWalkManager._shouldPauseForPartyWait` delegate (same pattern as `_shouldPauseForHealth`), checked in `SendNextStep()` and `OnPausePollTick()`. Loops pause naturally since LoopManager delegates to AutoWalkManager. **ALL OFF:** Leader wait stays active (social feature). Follower wait only fires when HealthManager is active (requires automation on).

**Session messages** — `DisplayMudTextDirect()` with ANSI codes injects `[Session ended]` / `[Connection lost]` / `[EMERGENCY HANGUP]` into the terminal on disconnect. Must call `_terminalControl.InvalidateTerminal()` after because `RenderTimer_Tick` requires `_isConnected` which is already false.

## Common Pitfalls

- Never add exit types to `GetDirectionalExitKeys()` unless they appear in "Obvious exits:" display
- `_currentTarget` must be preserved during enemy list clears — re-attacking changes combat order
- RoomTracker command queue uses `_currentRoom.Key` at consumption time, not pre-computed keys
- Teleport level gates must be promoted to `ExitLevelRestriction` (not just stored in TeleportConditions) for filter handling
- Item-gated remote actions are intentionally deferred (not traversable) — they need inventory tracking (Phase 6)
- Priority 6 (STOP RESTING) must check `_restingForMana` to know which max threshold to use — checking both HP and mana causes 20+ second idle waits; checking only HP causes ping-pong when resting for mana
- `RenderTimer_Tick` requires `_isConnected` — force `InvalidateTerminal()` when injecting terminal text during disconnect
- `OnStatusChanged` fires for intermediate states ("Connecting...", "Retrying...") — filter by specific statusMessage values, not catch-all else
- `_postCombatPauseWalking` must be cleared before the ALL OFF early-return in Evaluate() to prevent stuck walking pause
- `@wait`/`@ok` bypass `HasPermission` in RemoteCommandManager — party membership validated by PartyManager instead
- Leader acknowledges `@Wait` with `{Ok}` (informational telepath), NOT `@Ok` (which would be parsed as a command)
- `_followerWaitSent` must gate `@Wait` sends — HealthManager re-fires Resting on interruption, causing duplicate sends without the flag
- `_waitingMembers` must be cleaned up when members leave party, party disbands, or on disconnect — stale entries block walking permanently
- `ShouldPauseForPartyWait` is NOT gated by ALL OFF — it's a social coordination feature that should work regardless of automation state
