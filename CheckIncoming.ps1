$rooms = Get-Content 'C:\Users\MSD\AppData\Roaming\MudProxyViewer\Game Data\Rooms.json' -Raw | ConvertFrom-Json
$target = "10/261"
$directions = @("N","S","E","W","NE","NW","SE","SW","U","D")
$found = @()
foreach ($room in $rooms) {
    $sourceKey = "$($room.'Map Number')/$($room.'Room Number')"
    foreach ($dir in $directions) {
        $exitVal = $room.$dir
        if ($exitVal -and $exitVal -ne "0" -and -not $exitVal.StartsWith("Action")) {
            if ($exitVal -match '^\s*(\d+/\d+)' -and $matches[1] -eq $target) {
                $found += "$sourceKey ($($room.Name)) via $dir -> $exitVal"
            }
        }
    }
}
if ($found.Count -eq 0) {
    Write-Host "No incoming connections found for $target"
} else {
    Write-Host "Incoming connections to ${target}:"
    $found | ForEach-Object { Write-Host "  $_" }
}
