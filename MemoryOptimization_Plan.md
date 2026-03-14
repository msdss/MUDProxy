# Memory Optimization Plan for MudProxyViewer

## Problem Statement

The app uses **600MB–1GB** of RAM vs. the legacy MegaMUD's **3–7MB**. With users running 2–6 instances, this can consume 2–6 GB of system memory.

## Root Cause Analysis

The codebase has **no memory leaks** — all collections are properly bounded. The issue is **how game data is stored in memory**:

### Memory Breakdown (Estimated)

| Component | Low | High | Notes |
|-----------|-----|------|-------|
| **GameDataCache** | 150 MB | 200 MB | All 9 JSON tables as `Dict<string, object?>` |
| **RoomGraphManager** | 74 MB | 134 MB | Typed room graph (duplicates Rooms data) |
| .NET runtime + WinForms | 50 MB | 80 MB | Unavoidable baseline |
| GC/LOH fragmentation | 50 MB | 200 MB | Unsized collections, large allocations |
| Other (profiles, combat, temp) | 15 MB | 60 MB | Well-bounded, not a concern |
| **Total** | **~340 MB** | **~674 MB** | |

### Why So Much?

1. **GameDataCache** stores 41 MB of JSON (Rooms.json alone is 23 MB) deserialized into `List<Dictionary<string, object?>>`. Each row is an untyped dictionary with boxed primitives — a single `int` costs 16+ bytes as a boxed `object?` vs 4 bytes typed. This balloons 41 MB on disk to ~150–200 MB in memory.

2. **Data duplication**: Rooms.json is loaded into GameDataCache (~120–160 MB) AND then a separate typed room graph is built by RoomGraphManager (~74–134 MB). **Both copies persist permanently.**

3. **String duplication**: Direction strings ("N", "S", "E", etc.) and commands ("n", "s", "e") are duplicated across ~60,000 RoomExit objects without interning.

4. **Dead fields**: Each RoomExit stores a `RawValue` string (never read after construction). Each RoomNode stores `Light`, `Shop`, `Lair` fields (never read after construction). ~60,000 exits × ~50 bytes avg per RawValue = ~3–10 MB wasted.

5. **LOH fragmentation**: Lists with 12,000+ elements land on the Large Object Heap. Without pre-sizing, repeated list resizing causes fragmentation that can double effective memory use.

### What's NOT the Problem

- Terminal scrollback: capped at 500 lines (~2.4 MB max)
- Combat/room tracking: all collections bounded and pruned
- Event handlers: some minor unsubscribe gaps but no significant leak vectors
- Timers: all properly disposed on form close
- GDI+ resources: properly wrapped in `using` statements

---

## Implementation Plan — 4 Phases

### Phase 1: Evict Consumed Tables from GameDataCache

**Estimated savings: 150–170 MB** | Complexity: Low–Medium

After `RoomGraphManager` and `MonsterDatabaseManager` convert raw dictionary data into typed models, the original cache entries are redundant. `GameDataViewerDialog` already has a `LoadJsonToDataTable()` fallback that reads from disk when cache is empty.

#### Changes

**`GameDataCache.cs`** — Add eviction method:
```csharp
public void EvictTable(string tableName)
{
    _cache.TryRemove(tableName, out _);
}
```
Also remove the 4 unused `Find*` methods (lines 209–255) — confirmed never called externally.

**`RoomGraphManager.cs`** — In `LoadFromGameData()`, after `BuildGraph()` + `BuildClassRaceLookups()` succeed:
```csharp
GameDataCache.Instance.EvictTable("Rooms");      // ~120-160 MB freed
GameDataCache.Instance.EvictTable("TextBlocks");  // ~3 MB freed
GameDataCache.Instance.EvictTable("Classes");     // tiny
GameDataCache.Instance.EvictTable("Races");       // tiny
GameDataCache.Instance.EvictTable("Items");       // ~15-20 MB freed
```

**`MonsterDatabaseManager.cs`** — After converting to typed `MonsterData` objects:
```csharp
GameDataCache.Instance.EvictTable("Monsters");    // ~30-50 MB freed
```

**`ItemDialogs.cs`** — `ResolveName()` (line 432) scans `GameDataCache.Instance.GetTable()` linearly. After eviction these return null. Refactor to use lookup dictionaries already held by managers:
- Items → expose `RoomGraphManager._itemIdToName` via public method
- Monsters → use `MonsterDatabaseManager` (already has typed list)
- Spells → keep Spells table in cache (only 2 MB on disk)

**`GameManager.cs`** — After all managers finish loading, trigger one-time GC compaction to return freed memory to the OS:
```csharp
GC.Collect(2, GCCollectionMode.Aggressive, true, true);
GC.WaitForPendingFinalizers();
```

#### Verification
- Check Task Manager memory before/after
- Walk-To dialog search and pathfinding still work
- Data Viewer for Rooms/Monsters loads from disk fallback
- MDB re-import correctly reloads everything

---

### Phase 2: Don't Preload Non-Essential Tables

**Estimated savings: 10–15 MB** | Complexity: Low

Shops (581 KB), Lairs (276 KB), and Spells (2 MB) are only used when the user opens data viewer dialogs. Don't preload them — the viewer dialog's disk fallback handles on-demand loading.

#### Changes

**`GameDataCache.cs`** — Restrict `StartPreload()` to essential tables:
```csharp
private static readonly HashSet<string> _preloadTables = new(StringComparer.OrdinalIgnoreCase)
{
    "Rooms", "Monsters", "Classes", "Races", "TextBlocks", "Items"
};
```
Skip files not in this set during the preload loop (lines 68–72).

#### Verification
- Open Data Viewer for Shops/Lairs/Spells — should load correctly from disk

---

### Phase 3: String Interning + Dead Field Removal

**Estimated savings: 15–30 MB** | Complexity: Low

~60,000 RoomExit objects each store duplicate direction/command strings and fields that are never read.

#### Changes

**`RoomGraphManager.cs`** — Add static intern pool:
```csharp
private static readonly Dictionary<string, string> _internPool = new(StringComparer.Ordinal);
private static string Intern(string s)
{
    if (_internPool.TryGetValue(s, out var existing)) return existing;
    _internPool[s] = s;
    return s;
}
```
Apply `Intern()` to `Direction`, `Command`, `DestinationKey` in `ParseExit()` (line ~1507) and `BuildTeleportExits()` (line ~1222). Also intern `RoomNode.Key` strings so dictionary keys and node keys share references.

**`Models.cs`** — Remove dead fields:
- Remove `RawValue` from `RoomExit` (line 1002) — assigned in 2 places, never read
- Remove `Light`, `Shop`, `Lair` from `RoomNode` (lines 970–976) — confirmed never read after construction

**`RoomGraphManager.cs`** — Remove the corresponding assignments:
- 2 `RawValue = ...` assignments (lines 1222, 1519)
- 3 field assignments (`Light =`, `Shop =`, `Lair =`) during node construction

#### Verification
- Zero build warnings
- Pathfinding produces identical results
- Room detection guards still function
- Walk-To dialog search unchanged

---

### Phase 4: Collection Pre-sizing + LOH Optimization

**Estimated savings: 20–50 MB** (from reduced fragmentation) | Complexity: Low

Large unsized collections cause repeated resizing and Large Object Heap fragmentation.

#### Changes

**`GameDataCache.cs`** line 181 — Pre-size result list in `ParseJson()`:
```csharp
var count = root.GetArrayLength();
var result = new List<Dictionary<string, object?>>(count);
```

**`RoomGraphManager.cs`** — Pre-size `_rooms` dictionary:
```csharp
_rooms.EnsureCapacity(roomRows.Count);
```

**`RoomGraphManager.cs`** — Default RoomNode exits to capacity 4 (most rooms have 1–4 exits):
```csharp
Exits = new List<RoomExit>(4)
```

#### Verification
- Build succeeds
- Memory usage stable or reduced after startup

---

## Expected Results Summary

| Phase | Savings | Complexity |
|-------|---------|------------|
| 1 — Evict consumed tables | 150–170 MB | Low–Medium |
| 2 — Skip non-essential preload | 10–15 MB | Low |
| 3 — String intern + dead fields | 15–30 MB | Low |
| 4 — Pre-sizing + LOH | 20–50 MB | Low |
| **Total** | **~195–265 MB** | |

**Post-optimization expected memory: ~300–450 MB** (down from 600MB–1GB).

The remaining memory consists of:
- Typed room graph: ~60–90 MB after interning (12K rooms, 60K exits)
- .NET/WinForms runtime: ~80 MB (unavoidable)
- Working state: ~20–50 MB

### Further Optimization (Future Consideration)

If ~300–450 MB is still too high, the next frontier would be converting the room graph from **class-based** (`RoomNode`/`RoomExit` as heap objects) to **struct-based arrays** (flat arrays of room/exit data with index-based references instead of object references). This would eliminate per-object overhead (24 bytes/object on x64) across ~72,000 objects and improve cache locality, potentially saving another 50–100 MB — but it requires significant architectural changes.

---

## Key Files Reference

| File | Phases | Purpose |
|------|--------|---------|
| `GameDataCache.cs` | 1, 2, 4 | Add eviction, selective preload, pre-sizing |
| `RoomGraphManager.cs` | 1, 3, 4 | Evict after build, string interning, pre-sizing |
| `MonsterDatabaseManager.cs` | 1 | Evict after typed conversion |
| `Models.cs` | 3 | Remove RawValue, Light, Shop, Lair |
| `ItemDialogs.cs` | 1 | Refactor ResolveName to use typed lookups |
| `GameManager.cs` | 1 | Post-load GC compaction |
