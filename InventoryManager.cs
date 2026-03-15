using System.Text.RegularExpressions;

namespace MudProxyViewer;

/// <summary>
/// Tracks the player's inventory by parsing the output of the 'i' command
/// and detecting incremental changes (pickup, drop, equip, etc.).
/// Thread-safe: all state access is protected by _inventoryLock.
/// </summary>
public class InventoryManager
{
    // ── Dependencies ────────────────────────────────────────────────────
    private readonly Action<string> _sendCommand;
    private readonly Action<string> _logMessage;
    private readonly Func<int, string?> _getItemNameById;

    // ── Thread-safe state ───────────────────────────────────────────────
    private readonly object _inventoryLock = new();
    private InventoryState _inventory = new();
    private bool _isInventoryLoaded;

    // ── Multi-line capture state (only accessed from ProcessLine, single-threaded) ──
    private bool _capturingInventory;
    private readonly List<string> _captureBuffer = new();
    private const int MaxCaptureLines = 50;

    // ── Equipment slot names (for regex) ────────────────────────────────
    private static readonly HashSet<string> ValidSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        "Head", "Ears", "Face", "Neck", "Back", "Torso", "Arms", "Wrist",
        "Hands", "Finger", "Waist", "Legs", "Feet", "Worn", "Off-Hand", "Weapon Hand"
    };

    // Matches an equipped slot suffix at the end of an item token: "silver hood (Head)"
    private static readonly Regex SlotSuffixRegex = new(
        @"\s+\((Head|Ears|Face|Neck|Back|Torso|Arms|Wrist|Hands|Finger|Waist|Legs|Feet|Worn|Off-Hand|Weapon Hand)\)$",
        RegexOptions.Compiled);

    // Matches a leading quantity: "2 rope and grapple" → groups: (2, "rope and grapple")
    private static readonly Regex QuantityPrefixRegex = new(
        @"^(\d+)\s+(.+)$",
        RegexOptions.Compiled);

    // ── Currency regexes ────────────────────────────────────────────────
    private static readonly Regex RunicCoinRegex = new(@"(\d+) runic coins?", RegexOptions.Compiled);
    private static readonly Regex PlatinumRegex = new(@"(\d+) platinum pieces?", RegexOptions.Compiled);
    private static readonly Regex GoldRegex = new(@"(\d+) gold crowns?", RegexOptions.Compiled);
    private static readonly Regex SilverRegex = new(@"(\d+) silver nobles?", RegexOptions.Compiled);
    private static readonly Regex CopperRegex = new(@"(\d+) copper farthings?", RegexOptions.Compiled);

    // ── Wealth and Encumbrance ──────────────────────────────────────────
    private static readonly Regex WealthRegex = new(
        @"^Wealth:\s+(\d+)\s+copper farthings?$",
        RegexOptions.Compiled);

    private static readonly Regex EncumbranceRegex = new(
        @"^Encumbrance:\s+(\d+)/(\d+)\s+-\s+(\w+)\s+\[(\d+)%\]$",
        RegexOptions.Compiled);

    // ── Incremental tracking regexes ────────────────────────────────────
    // Items
    private static readonly Regex YouTookRegex = new(@"^You took (.+)\.$", RegexOptions.Compiled);
    private static readonly Regex YouDroppedItemRegex = new(@"^You dropped (.+)\.$", RegexOptions.Compiled);
    private static readonly Regex YouWearingRegex = new(@"^You are now wearing (.+)\.$", RegexOptions.Compiled);
    private static readonly Regex YouRemovedRegex = new(@"^You have removed (.+)\.$", RegexOptions.Compiled);
    private static readonly Regex YouGiveRegex = new(@"^You give (.+) to (.+)\.$", RegexOptions.Compiled);
    private static readonly Regex GivesYouRegex = new(@"^(.+) gives you (.+)\.$", RegexOptions.Compiled);
    private static readonly Regex YouBoughtRegex = new(@"^You just bought (.+) for (\d+) copper farthings\.$", RegexOptions.Compiled);
    private static readonly Regex YouSoldRegex = new(@"^You sold (.+) for (\d+) copper farthings\.$", RegexOptions.Compiled);

    // Currency (no trailing period — key disambiguation from item drops)
    private static readonly Regex PickedUpCurrencyRegex = new(
        @"^You picked up (\d+) (runic coins?|platinum pieces?|gold crowns?|silver nobles?|copper farthings?)$",
        RegexOptions.Compiled);
    private static readonly Regex DroppedCurrencyRegex = new(
        @"^You dropped (\d+) (runic coins?|platinum pieces?|gold crowns?|silver nobles?|copper farthings?)$",
        RegexOptions.Compiled);

    // Currency type names for matching (singular and plural forms)
    private static readonly HashSet<string> CurrencyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "runic coin", "runic coins",
        "platinum piece", "platinum pieces",
        "gold crown", "gold crowns",
        "silver noble", "silver nobles",
        "copper farthing", "copper farthings"
    };

    // ── Events ──────────────────────────────────────────────────────────
    public event Action? OnInventoryChanged;

    // ── Constructor ─────────────────────────────────────────────────────
    public InventoryManager(
        Action<string> sendCommand,
        Action<string> logMessage,
        Func<int, string?> getItemNameById)
    {
        _sendCommand = sendCommand;
        _logMessage = logMessage;
        _getItemNameById = getItemNameById;
    }

    // ── Public API (thread-safe) ────────────────────────────────────────

    /// <summary>True after at least one successful full inventory parse.</summary>
    public bool IsInventoryLoaded
    {
        get { lock (_inventoryLock) return _isInventoryLoaded; }
    }

    /// <summary>Check if the player has an item by name (case-insensitive). Searches equipped, carried, and keys.</summary>
    public bool HasItem(string itemName)
    {
        lock (_inventoryLock)
        {
            return FindItem(_inventory.EquippedItems, itemName) != null
                || FindItem(_inventory.CarriedItems, itemName) != null
                || FindItem(_inventory.Keys, itemName) != null;
        }
    }

    /// <summary>Check if the player has an item by its game database ID.</summary>
    public bool HasItemById(int itemId)
    {
        var name = _getItemNameById(itemId);
        if (string.IsNullOrEmpty(name)) return false;
        return HasItem(name);
    }

    /// <summary>Check if the player has a specific key by name.</summary>
    public bool HasKey(string keyName)
    {
        lock (_inventoryLock)
        {
            return FindItem(_inventory.Keys, keyName) != null;
        }
    }

    /// <summary>Get the total quantity of an item across all inventory sections.</summary>
    public int GetItemCount(string itemName)
    {
        lock (_inventoryLock)
        {
            int count = 0;
            var eq = FindItem(_inventory.EquippedItems, itemName);
            if (eq != null) count += eq.Quantity;
            var carried = FindItem(_inventory.CarriedItems, itemName);
            if (carried != null) count += carried.Quantity;
            var key = FindItem(_inventory.Keys, itemName);
            if (key != null) count += key.Quantity;
            return count;
        }
    }

    /// <summary>Get total wealth in copper farthings.</summary>
    public long GetWealth()
    {
        lock (_inventoryLock) return _inventory.Currency.TotalWealthInCopper;
    }

    /// <summary>Get a copy of the current encumbrance state.</summary>
    public EncumbranceState GetEncumbrance()
    {
        lock (_inventoryLock)
        {
            var e = _inventory.Encumbrance;
            return new EncumbranceState
            {
                CurrentWeight = e.CurrentWeight,
                MaxWeight = e.MaxWeight,
                Percentage = e.Percentage,
                Category = e.Category
            };
        }
    }

    /// <summary>Get a deep copy of the full inventory state.</summary>
    public InventoryState GetInventorySnapshot()
    {
        lock (_inventoryLock)
        {
            return new InventoryState
            {
                Currency = new CurrencyState
                {
                    RunicCoins = _inventory.Currency.RunicCoins,
                    PlatinumPieces = _inventory.Currency.PlatinumPieces,
                    GoldCrowns = _inventory.Currency.GoldCrowns,
                    SilverNobles = _inventory.Currency.SilverNobles,
                    CopperFarthings = _inventory.Currency.CopperFarthings,
                    TotalWealthInCopper = _inventory.Currency.TotalWealthInCopper
                },
                EquippedItems = _inventory.EquippedItems.Select(CloneItem).ToList(),
                CarriedItems = _inventory.CarriedItems.Select(CloneItem).ToList(),
                Keys = _inventory.Keys.Select(CloneItem).ToList(),
                Encumbrance = new EncumbranceState
                {
                    CurrentWeight = _inventory.Encumbrance.CurrentWeight,
                    MaxWeight = _inventory.Encumbrance.MaxWeight,
                    Percentage = _inventory.Encumbrance.Percentage,
                    Category = _inventory.Encumbrance.Category
                },
                LastUpdated = _inventory.LastUpdated
            };
        }
    }

    /// <summary>Request a full inventory refresh by sending the 'i' command.</summary>
    public void RequestRefresh()
    {
        _sendCommand("i");
    }

    /// <summary>Load inventory state from a saved profile.</summary>
    public void LoadFromState(InventoryState state)
    {
        lock (_inventoryLock)
        {
            _inventory = state;
            _isInventoryLoaded = state.LastUpdated > DateTime.MinValue;
        }
    }

    /// <summary>Mark inventory as stale (e.g., after player death). Does not clear data.</summary>
    public void MarkStale()
    {
        lock (_inventoryLock)
        {
            _isInventoryLoaded = false;
        }
    }

    // ── Line Processing (called from MessageRouter) ─────────────────────

    /// <summary>
    /// Process a single line of server output. Returns true if the line was consumed
    /// (part of inventory output) and should NOT be passed to RoomTracker.
    /// </summary>
    public bool ProcessLine(string line)
    {
        var trimmed = line.TrimEnd('\r', '\n');

        // ── Currently capturing multi-line inventory output ──
        if (_capturingInventory)
        {
            _captureBuffer.Add(trimmed);

            // Check for end trigger: "Encumbrance:" line
            if (trimmed.TrimStart().StartsWith("Encumbrance:", StringComparison.Ordinal))
            {
                ParseFullInventory();
                _capturingInventory = false;
                _captureBuffer.Clear();
                return true; // consumed
            }

            // Safety: abort if too many lines without finding Encumbrance
            if (_captureBuffer.Count >= MaxCaptureLines)
            {
                _logMessage("⚠️ Inventory capture aborted: exceeded max buffer lines without finding Encumbrance");
                _capturingInventory = false;
                _captureBuffer.Clear();
                return false; // let remaining lines flow normally
            }

            return true; // consumed — still capturing
        }

        // ── Check for inventory start trigger ──
        if (trimmed.StartsWith("You are carrying ", StringComparison.Ordinal)
            || trimmed == "You are carrying nothing.")
        {
            if (trimmed == "You are carrying nothing.")
            {
                // Empty inventory — don't start multi-line capture, but we still
                // need Wealth + Encumbrance lines that follow. Start capture.
                _captureBuffer.Clear();
                _captureBuffer.Add(trimmed);
                _capturingInventory = true;
                return true;
            }

            _captureBuffer.Clear();
            _captureBuffer.Add(trimmed);
            _capturingInventory = true;
            return true; // consumed
        }

        // ── Incremental tracking (single-line messages, NOT consumed) ──
        ProcessIncrementalMessage(trimmed);

        return false; // not consumed — let other managers see it
    }

    // ── Full Inventory Parsing ──────────────────────────────────────────

    private void ParseFullInventory()
    {
        var newState = new InventoryState();

        // Separate the buffer into sections:
        // 1. Items section: from "You are carrying" to "You have no keys."/"You have the following keys:"
        // 2. Keys line
        // 3. Wealth line
        // 4. Encumbrance line

        var itemLines = new List<string>();
        string? keysLine = null;
        string? wealthLine = null;
        string? encumbranceLine = null;

        foreach (var line in _captureBuffer)
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("You have no keys", StringComparison.Ordinal))
            {
                keysLine = trimmed;
            }
            else if (trimmed.StartsWith("You have the following keys:", StringComparison.Ordinal))
            {
                keysLine = trimmed;
            }
            else if (trimmed.StartsWith("Wealth:", StringComparison.Ordinal))
            {
                wealthLine = trimmed;
            }
            else if (trimmed.StartsWith("Encumbrance:", StringComparison.Ordinal))
            {
                encumbranceLine = trimmed;
            }
            else
            {
                itemLines.Add(trimmed);
            }
        }

        // ── Parse items section ──
        // Join all item lines with space to reconstruct word-wrapped text
        var itemsText = string.Join(" ", itemLines);

        // Remove "You are carrying " prefix
        const string prefix = "You are carrying ";
        if (itemsText.StartsWith(prefix, StringComparison.Ordinal))
        {
            itemsText = itemsText.Substring(prefix.Length);
        }
        else if (itemsText.StartsWith("You are carrying nothing", StringComparison.Ordinal))
        {
            itemsText = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(itemsText))
        {
            ParseItemsSection(itemsText, newState);
        }

        // ── Parse keys ──
        if (keysLine != null)
        {
            ParseKeysSection(keysLine, newState);
        }

        // ── Parse wealth ──
        if (wealthLine != null)
        {
            var wealthMatch = WealthRegex.Match(wealthLine);
            if (wealthMatch.Success && long.TryParse(wealthMatch.Groups[1].Value, out var wealthCopper))
            {
                newState.Currency.TotalWealthInCopper = wealthCopper;
            }
        }

        // ── Parse encumbrance ──
        if (encumbranceLine != null)
        {
            var encMatch = EncumbranceRegex.Match(encumbranceLine);
            if (encMatch.Success)
            {
                int.TryParse(encMatch.Groups[1].Value, out var curWeight);
                int.TryParse(encMatch.Groups[2].Value, out var maxWeight);
                int.TryParse(encMatch.Groups[4].Value, out var pct);
                newState.Encumbrance = new EncumbranceState
                {
                    CurrentWeight = curWeight,
                    MaxWeight = maxWeight,
                    Percentage = pct,
                    Category = encMatch.Groups[3].Value
                };
            }
        }

        newState.LastUpdated = DateTime.Now;

        // ── Swap state under lock ──
        lock (_inventoryLock)
        {
            _inventory = newState;
            _isInventoryLoaded = true;
        }

        _logMessage($"📦 Inventory parsed: {newState.EquippedItems.Count} equipped, " +
                    $"{newState.CarriedItems.Count} carried, {newState.Keys.Count} keys, " +
                    $"Wealth: {newState.Currency.TotalWealthInCopper:N0} copper, " +
                    $"Encumbrance: {newState.Encumbrance.CurrentWeight}/{newState.Encumbrance.MaxWeight} " +
                    $"{newState.Encumbrance.Category} [{newState.Encumbrance.Percentage}%]");

        OnInventoryChanged?.Invoke();
    }

    /// <summary>
    /// Parse the comma-separated items text (after "You are carrying " prefix removed).
    /// Identifies currency, equipped items, and carried items.
    /// </summary>
    private void ParseItemsSection(string itemsText, InventoryState state)
    {
        // Split by ", " — but we need to be careful because item names can contain commas?
        // In practice, MUD items are always comma+space separated.
        var tokens = SplitItemTokens(itemsText);

        foreach (var token in tokens)
        {
            var trimmedToken = token.Trim();
            if (string.IsNullOrWhiteSpace(trimmedToken))
                continue;

            // ── Check if this is a currency token ──
            if (TryParseCurrency(trimmedToken, state.Currency))
                continue;

            // ── Check for equipped item (has slot suffix) ──
            var slotMatch = SlotSuffixRegex.Match(trimmedToken);
            if (slotMatch.Success)
            {
                var slotName = slotMatch.Groups[1].Value;
                var itemPart = trimmedToken.Substring(0, slotMatch.Index).Trim();
                var (name, qty) = ParseNameAndQuantity(itemPart);

                state.EquippedItems.Add(new InventoryItem
                {
                    Name = name,
                    Quantity = qty,
                    EquippedSlot = slotName,
                    IsKey = false
                });
                continue;
            }

            // ── Carried item (no slot suffix, after last equipped) ──
            var (carriedName, carriedQty) = ParseNameAndQuantity(trimmedToken);

            state.CarriedItems.Add(new InventoryItem
            {
                Name = carriedName,
                Quantity = carriedQty,
                EquippedSlot = null,
                IsKey = false
            });
        }
    }

    /// <summary>
    /// Parse the keys line: "You have the following keys: silver key, adamantite key, moldy key."
    /// Keys can have quantities: "2 black star key, 3 ornate bronze key."
    /// </summary>
    private void ParseKeysSection(string keysLine, InventoryState state)
    {
        const string keysPrefix = "You have the following keys: ";
        if (!keysLine.StartsWith(keysPrefix, StringComparison.Ordinal))
            return; // "You have no keys." — nothing to parse

        var keysText = keysLine.Substring(keysPrefix.Length).TrimEnd('.', ' ');
        var keyTokens = keysText.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var keyToken in keyTokens)
        {
            var trimmed = keyToken.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var (name, qty) = ParseNameAndQuantity(trimmed);
            state.Keys.Add(new InventoryItem
            {
                Name = name,
                Quantity = qty,
                EquippedSlot = null,
                IsKey = true
            });
        }
    }

    /// <summary>
    /// Check if a token is a currency entry (e.g., "2 runic coins", "30 platinum pieces").
    /// If so, update the currency state and return true.
    /// </summary>
    private static bool TryParseCurrency(string token, CurrencyState currency)
    {
        var runicMatch = RunicCoinRegex.Match(token);
        if (runicMatch.Success && runicMatch.Index == 0 && runicMatch.Length == token.Length)
        {
            int.TryParse(runicMatch.Groups[1].Value, out var count);
            currency.RunicCoins = count;
            return true;
        }

        var platMatch = PlatinumRegex.Match(token);
        if (platMatch.Success && platMatch.Index == 0 && platMatch.Length == token.Length)
        {
            int.TryParse(platMatch.Groups[1].Value, out var count);
            currency.PlatinumPieces = count;
            return true;
        }

        var goldMatch = GoldRegex.Match(token);
        if (goldMatch.Success && goldMatch.Index == 0 && goldMatch.Length == token.Length)
        {
            int.TryParse(goldMatch.Groups[1].Value, out var count);
            currency.GoldCrowns = count;
            return true;
        }

        var silverMatch = SilverRegex.Match(token);
        if (silverMatch.Success && silverMatch.Index == 0 && silverMatch.Length == token.Length)
        {
            int.TryParse(silverMatch.Groups[1].Value, out var count);
            currency.SilverNobles = count;
            return true;
        }

        var copperMatch = CopperRegex.Match(token);
        if (copperMatch.Success && copperMatch.Index == 0 && copperMatch.Length == token.Length)
        {
            int.TryParse(copperMatch.Groups[1].Value, out var count);
            currency.CopperFarthings = count;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Split the items text by ", " while being mindful of the flat comma-separated format.
    /// </summary>
    private static List<string> SplitItemTokens(string text)
    {
        return text.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(t => t.Trim())
                   .ToList();
    }

    /// <summary>
    /// Parse "2 rope and grapple" → (name: "rope and grapple", qty: 2).
    /// If no leading number, returns (name: full string, qty: 1).
    /// </summary>
    private static (string name, int quantity) ParseNameAndQuantity(string token)
    {
        var match = QuantityPrefixRegex.Match(token);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var qty))
        {
            var name = match.Groups[2].Value.Trim();
            // Sanity: don't parse "1st legion sword" as qty=1, name="st legion sword"
            // Only treat as quantity if the remaining name doesn't start with a letter
            // that would indicate it's part of an ordinal number.
            // Actually, MUD items with leading numbers always have the number followed by a space
            // and then the item name, which won't start with "st", "nd", "rd", "th" ordinals
            // because items are named plainly. However, to be safe, we only treat it as a
            // quantity if qty > 0 and the name is non-empty.
            if (qty > 0 && !string.IsNullOrWhiteSpace(name))
            {
                return (name, qty);
            }
        }

        return (token.Trim(), 1);
    }

    // ── Incremental Message Processing ──────────────────────────────────

    private void ProcessIncrementalMessage(string line)
    {
        // ── Currency pickup (no trailing period) ──
        var pickedUpCurrency = PickedUpCurrencyRegex.Match(line);
        if (pickedUpCurrency.Success)
        {
            if (int.TryParse(pickedUpCurrency.Groups[1].Value, out var amount))
            {
                var coinType = pickedUpCurrency.Groups[2].Value;
                lock (_inventoryLock)
                {
                    AdjustCurrency(_inventory.Currency, coinType, amount);
                }
                OnInventoryChanged?.Invoke();
            }
            return;
        }

        // ── Currency drop (no trailing period) ──
        var droppedCurrency = DroppedCurrencyRegex.Match(line);
        if (droppedCurrency.Success)
        {
            if (int.TryParse(droppedCurrency.Groups[1].Value, out var amount))
            {
                var coinType = droppedCurrency.Groups[2].Value;
                lock (_inventoryLock)
                {
                    AdjustCurrency(_inventory.Currency, coinType, -amount);
                }
                OnInventoryChanged?.Invoke();
            }
            return;
        }

        // ── Item pickup: "You took rope and grapple." ──
        var tookMatch = YouTookRegex.Match(line);
        if (tookMatch.Success)
        {
            var itemName = tookMatch.Groups[1].Value;
            lock (_inventoryLock)
            {
                IncrementItem(_inventory.CarriedItems, itemName, 1);
            }
            OnInventoryChanged?.Invoke();
            return;
        }

        // ── Item drop: "You dropped rope and grapple." (has trailing period — item, not currency) ──
        var droppedItemMatch = YouDroppedItemRegex.Match(line);
        if (droppedItemMatch.Success)
        {
            var itemName = droppedItemMatch.Groups[1].Value;
            // Make sure it's not a currency drop that somehow matched (shouldn't happen due to period)
            if (!IsCurrencyName(itemName))
            {
                lock (_inventoryLock)
                {
                    DecrementItem(_inventory.CarriedItems, itemName);
                    DecrementItem(_inventory.EquippedItems, itemName);
                    DecrementItem(_inventory.Keys, itemName);
                }
                OnInventoryChanged?.Invoke();
            }
            return;
        }

        // ── Equip: "You are now wearing black silk robes." ──
        var wearingMatch = YouWearingRegex.Match(line);
        if (wearingMatch.Success)
        {
            var itemName = wearingMatch.Groups[1].Value;
            lock (_inventoryLock)
            {
                // Remove from carried (it was in inventory), add to equipped
                DecrementItem(_inventory.CarriedItems, itemName);
                // We don't know the slot from this message, so leave EquippedSlot null
                IncrementItem(_inventory.EquippedItems, itemName, 1);
            }
            OnInventoryChanged?.Invoke();
            return;
        }

        // ── Remove: "You have removed black silk robes." ──
        var removedMatch = YouRemovedRegex.Match(line);
        if (removedMatch.Success)
        {
            var itemName = removedMatch.Groups[1].Value;
            lock (_inventoryLock)
            {
                DecrementItem(_inventory.EquippedItems, itemName);
                IncrementItem(_inventory.CarriedItems, itemName, 1);
            }
            OnInventoryChanged?.Invoke();
            return;
        }

        // ── Give: "You give moldy key to Azii." ──
        var giveMatch = YouGiveRegex.Match(line);
        if (giveMatch.Success)
        {
            var itemName = giveMatch.Groups[1].Value;
            lock (_inventoryLock)
            {
                DecrementItem(_inventory.CarriedItems, itemName);
                DecrementItem(_inventory.EquippedItems, itemName);
                DecrementItem(_inventory.Keys, itemName);
            }
            OnInventoryChanged?.Invoke();
            return;
        }

        // ── Receive: "Azii gives you moldy key." ──
        var receivedMatch = GivesYouRegex.Match(line);
        if (receivedMatch.Success)
        {
            var itemName = receivedMatch.Groups[2].Value;
            lock (_inventoryLock)
            {
                IncrementItem(_inventory.CarriedItems, itemName, 1);
            }
            OnInventoryChanged?.Invoke();
            return;
        }

        // ── Buy: "You just bought lantern for 396 copper farthings." ──
        var boughtMatch = YouBoughtRegex.Match(line);
        if (boughtMatch.Success)
        {
            var itemName = boughtMatch.Groups[1].Value;
            lock (_inventoryLock)
            {
                IncrementItem(_inventory.CarriedItems, itemName, 1);
            }
            OnInventoryChanged?.Invoke();
            return;
        }

        // ── Sell: "You sold lantern for 101 copper farthings." ──
        var soldMatch = YouSoldRegex.Match(line);
        if (soldMatch.Success)
        {
            var itemName = soldMatch.Groups[1].Value;
            lock (_inventoryLock)
            {
                DecrementItem(_inventory.CarriedItems, itemName);
            }
            OnInventoryChanged?.Invoke();
            return;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static InventoryItem? FindItem(List<InventoryItem> list, string name)
    {
        return list.FirstOrDefault(i =>
            i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static void IncrementItem(List<InventoryItem> list, string name, int amount)
    {
        var existing = FindItem(list, name);
        if (existing != null)
        {
            existing.Quantity += amount;
        }
        else
        {
            list.Add(new InventoryItem
            {
                Name = name,
                Quantity = amount,
                EquippedSlot = null,
                IsKey = false
            });
        }
    }

    private static void DecrementItem(List<InventoryItem> list, string name)
    {
        var existing = FindItem(list, name);
        if (existing != null)
        {
            existing.Quantity--;
            if (existing.Quantity <= 0)
            {
                list.Remove(existing);
            }
        }
    }

    private static void AdjustCurrency(CurrencyState currency, string coinType, int amount)
    {
        // Normalize to singular for matching
        var normalized = coinType.TrimEnd('s');

        if (normalized.StartsWith("runic coin", StringComparison.OrdinalIgnoreCase))
            currency.RunicCoins = Math.Max(0, currency.RunicCoins + amount);
        else if (normalized.StartsWith("platinum piece", StringComparison.OrdinalIgnoreCase))
            currency.PlatinumPieces = Math.Max(0, currency.PlatinumPieces + amount);
        else if (normalized.StartsWith("gold crown", StringComparison.OrdinalIgnoreCase))
            currency.GoldCrowns = Math.Max(0, currency.GoldCrowns + amount);
        else if (normalized.StartsWith("silver noble", StringComparison.OrdinalIgnoreCase))
            currency.SilverNobles = Math.Max(0, currency.SilverNobles + amount);
        else if (normalized.StartsWith("copper farthing", StringComparison.OrdinalIgnoreCase))
            currency.CopperFarthings = Math.Max(0, currency.CopperFarthings + amount);
    }

    private static bool IsCurrencyName(string name)
    {
        return CurrencyNames.Contains(name);
    }

    private static InventoryItem CloneItem(InventoryItem item)
    {
        return new InventoryItem
        {
            Name = item.Name,
            Quantity = item.Quantity,
            EquippedSlot = item.EquippedSlot,
            IsKey = item.IsKey
        };
    }
}
