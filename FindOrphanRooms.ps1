# FindOrphanRooms.ps1
# Finds "orphan" rooms: rooms with outgoing exits but no incoming connections,
# and fully isolated rooms with no incoming or outgoing connections.

$ErrorActionPreference = "Stop"

$roomsPath = "C:\Users\MSD\AppData\Roaming\MudProxyViewer\Game Data\Rooms.json"
$textBlocksPath = "C:\Users\MSD\AppData\Roaming\MudProxyViewer\Game Data\TextBlocks.json"

Write-Host "Loading Rooms.json..."
$rooms = Get-Content $roomsPath -Raw | ConvertFrom-Json
Write-Host "Loaded $($rooms.Count) rooms."

Write-Host "Loading TextBlocks.json..."
$textBlocks = Get-Content $textBlocksPath -Raw | ConvertFrom-Json
Write-Host "Loaded $($textBlocks.Count) text blocks."

$directions = @("N","S","E","W","NE","NW","SE","SW","U","D")

# Build set of all room keys and a lookup for room data
Write-Host "Building room key set..."
$allRoomKeys = New-Object System.Collections.Generic.HashSet[string]
$roomLookup = @{}
foreach ($room in $rooms) {
    $key = "$($room.'Map Number')/$($room.'Room Number')"
    [void]$allRoomKeys.Add($key)
    $roomLookup[$key] = $room
}
Write-Host "Total room keys: $($allRoomKeys.Count)"

# Build set of all destination room keys (rooms that are targets of exits from OTHER rooms)
Write-Host "Building incoming connections set..."
$hasIncoming = New-Object System.Collections.Generic.HashSet[string]

function Get-ExitDestination($exitVal) {
    if ([string]::IsNullOrWhiteSpace($exitVal)) { return $null }
    $exitVal = $exitVal.Trim()
    if ($exitVal -eq "0") { return $null }
    if ($exitVal.StartsWith("Action")) { return $null }
    if ($exitVal -match '^\s*(\d+/\d+)') {
        return $matches[1]
    }
    return $null
}

foreach ($room in $rooms) {
    $sourceKey = "$($room.'Map Number')/$($room.'Room Number')"
    foreach ($dir in $directions) {
        $exitVal = $room.$dir
        $dest = Get-ExitDestination $exitVal
        if ($dest -and $dest -ne $sourceKey) {
            [void]$hasIncoming.Add($dest)
        }
    }
}
Write-Host "Rooms with incoming directional connections: $($hasIncoming.Count)"

# Process TextBlock teleports
Write-Host "Processing TextBlocks for teleport destinations..."

$tbLookup = @{}
foreach ($tb in $textBlocks) {
    $tbLookup[$tb.Number] = $tb
}

function Get-TextBlockChain($startNum) {
    $chain = New-Object System.Collections.Generic.List[int]
    $visited = New-Object System.Collections.Generic.HashSet[int]
    $current = $startNum
    while ($current -ne 0 -and $tbLookup.ContainsKey($current) -and !$visited.Contains($current)) {
        [void]$visited.Add($current)
        $chain.Add($current)
        $current = $tbLookup[$current].LinkTo
    }
    return $chain
}

foreach ($room in $rooms) {
    $cmd = $room.CMD
    if ($cmd -eq 0 -or $cmd -eq $null) { continue }
    
    $sourceKey = "$($room.'Map Number')/$($room.'Room Number')"
    $chainNums = Get-TextBlockChain $cmd
    
    foreach ($tbNum in $chainNums) {
        $tb = $tbLookup[$tbNum]
        $action = $tb.Action
        if ([string]::IsNullOrWhiteSpace($action) -or $action -eq "`0") { continue }
        
        $teleportMatches = [regex]::Matches($action, 'teleport\s+(\d+)\s+(\d+)')
        foreach ($m in $teleportMatches) {
            $destRoom = $m.Groups[1].Value
            $destMap = $m.Groups[2].Value
            $destKey = "$destMap/$destRoom"
            if ($destKey -ne $sourceKey) {
                [void]$hasIncoming.Add($destKey)
            }
        }
    }
}

Write-Host "Rooms with incoming connections (including teleports): $($hasIncoming.Count)"

# Find orphan rooms (have outgoing exits but no incoming)
Write-Host ""
Write-Host "============================================================"
Write-Host "  CATEGORY 1: ORPHAN ROOMS"
Write-Host "  (Have outgoing exits but NO incoming connections)"
Write-Host "============================================================"
Write-Host ""

$orphans = New-Object System.Collections.Generic.List[PSObject]
$isolated = New-Object System.Collections.Generic.List[PSObject]

foreach ($room in $rooms) {
    $key = "$($room.'Map Number')/$($room.'Room Number')"
    
    # Skip if this room has incoming connections
    if ($hasIncoming.Contains($key)) { continue }
    
    # Check outgoing exits
    $outgoingExits = @{}
    $hasActionFields = @{}
    foreach ($dir in $directions) {
        $exitVal = $room.$dir
        if ([string]::IsNullOrWhiteSpace($exitVal) -or $exitVal.Trim() -eq "0") { continue }
        $dest = Get-ExitDestination $exitVal
        if ($dest) {
            $outgoingExits[$dir] = $exitVal
        } elseif ($exitVal.StartsWith("Action")) {
            $hasActionFields[$dir] = $exitVal
        }
    }
    
    if ($outgoingExits.Count -gt 0) {
        $orphans.Add([PSCustomObject]@{
            Key = $key
            MapNumber = $room.'Map Number'
            RoomNumber = $room.'Room Number'
            Name = $room.Name
            Exits = $outgoingExits
            ActionFields = $hasActionFields
            CMD = $room.CMD
        })
    } elseif ($hasActionFields.Count -gt 0) {
        # Room with only Action fields and no real exits - fully isolated but has data
        $isolated.Add([PSCustomObject]@{
            Key = $key
            MapNumber = $room.'Map Number'
            RoomNumber = $room.'Room Number'
            Name = $room.Name
            ActionFields = $hasActionFields
            CMD = $room.CMD
        })
    }
}

$orphans = $orphans | Sort-Object { $_.MapNumber }, { $_.RoomNumber }

foreach ($orphan in $orphans) {
    Write-Host "Room: $($orphan.Key)  Name: $($orphan.Name)"
    foreach ($dir in ($orphan.Exits.Keys | Sort-Object)) {
        Write-Host "  $dir -> $($orphan.Exits[$dir])"
    }
    if ($orphan.ActionFields.Count -gt 0) {
        foreach ($dir in ($orphan.ActionFields.Keys | Sort-Object)) {
            Write-Host "  $dir -> $($orphan.ActionFields[$dir]) [ACTION/prereq, not a real exit]"
        }
    }
    if ($orphan.CMD -ne 0) {
        Write-Host "  CMD: TextBlock #$($orphan.CMD)"
    }
    Write-Host ""
}

Write-Host "--- Total orphan rooms (Category 1): $($orphans.Count) ---"

Write-Host ""
Write-Host "============================================================"
Write-Host "  CATEGORY 2: FULLY ISOLATED ROOMS"
Write-Host "  (No incoming connections, no real outgoing exits,"
Write-Host "   but have Action fields indicating they were designed"
Write-Host "   as part of the map)"
Write-Host "============================================================"
Write-Host ""

$isolated = $isolated | Sort-Object { $_.MapNumber }, { $_.RoomNumber }

foreach ($iso in $isolated) {
    Write-Host "Room: $($iso.Key)  Name: $($iso.Name)"
    foreach ($dir in ($iso.ActionFields.Keys | Sort-Object)) {
        Write-Host "  $dir -> $($iso.ActionFields[$dir]) [ACTION/prereq]"
    }
    if ($iso.CMD -ne 0) {
        Write-Host "  CMD: TextBlock #$($iso.CMD)"
    }
    Write-Host ""
}

Write-Host "--- Total fully isolated rooms (Category 2): $($isolated.Count) ---"
Write-Host ""
Write-Host "=== GRAND TOTAL: $($orphans.Count + $isolated.Count) unreachable rooms ==="
