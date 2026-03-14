$rooms = Get-Content 'C:\Users\MSD\AppData\Roaming\MudProxyViewer\Game Data\Rooms.json' -Raw | ConvertFrom-Json
$r = $rooms | Where-Object { $_.'Map Number' -eq 10 -and $_.'Room Number' -eq 261 }
$r | Format-List
