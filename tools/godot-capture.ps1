# Renders The Ranch to a PNG without disturbing whoever is using the PC.
#
# - The window opens off-screen, borderless, with no_focus set, through a temporary
#   game/override.cfg that is removed afterwards.
# - Godot is started through WMI, which breaks the chain Windows uses to let a new window take
#   the foreground, so keyboard and mouse stay with the user.
# - It plays no sound (dummy audio driver).
# - It starts at below-normal priority. Godot raises its own priority at startup, so this script
#   lowers it again while it runs.
#
# Usage:
#   powershell -File tools/godot-capture.ps1 -Godot <Godot .NET exe> -Out shot.png `
#       [-Camera "x,y,z,yaw,pitch"] [-Save file.sav] [-Frames 90] [-TimeoutSeconds 180]
# Camera values are in the original game's coordinates and degrees.
param(
    [Parameter(Mandatory)] [string] $Godot,
    [Parameter(Mandatory)] [string] $Out,
    [string] $Camera = "",
    [string] $Save = "",
    [int] $Frames = 90,
    [int] $TimeoutSeconds = 180,
    [int] $Width = 1280,
    [int] $Height = 720
)

$game = Resolve-Path (Join-Path $PSScriptRoot "..\game")
$override = Join-Path $game "override.cfg"
if (Test-Path $override) { throw "$override already exists; refusing to overwrite it." }
$outFull = [System.IO.Path]::GetFullPath($Out)
$log = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "openranch-capture-$PID.log")
if (Test-Path $outFull) { Remove-Item $outFull }

@"
[display]

window/size/no_focus=true
window/size/borderless=true
window/size/viewport_width=$Width
window/size/viewport_height=$Height
window/size/initial_position_type=0
window/size/initial_position=Vector2i(-20000, -20000)
"@ | Set-Content -Path $override -Encoding ascii

try {
    $userArgs = "--scene ranch --screenshot `"$outFull`" --frames $Frames"
    if ($Camera) { $userArgs += " --camera $Camera" }
    if ($Save) { $userArgs += " --save `"$([System.IO.Path]::GetFullPath($Save))`"" }
    $commandLine = "`"$Godot`" --path `"$game`" --audio-driver Dummy --log-file `"$log`" -- $userArgs"

    $startup = New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{ PriorityClass = [uint32]16384 }
    $result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
        CommandLine               = $commandLine
        CurrentDirectory          = "$game"
        ProcessStartupInformation = $startup
    }
    if ($result.ReturnValue -ne 0) { throw "Could not start Godot (WMI error $($result.ReturnValue))." }

    $process = Get-Process -Id $result.ProcessId -ErrorAction SilentlyContinue
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($process -and -not $process.HasExited) {
        try { if ($process.PriorityClass -ne 'BelowNormal') { $process.PriorityClass = 'BelowNormal' } } catch { }
        if ((Get-Date) -gt $deadline) {
            Stop-Process -Id $process.Id -Force
            throw "Godot did not finish within $TimeoutSeconds seconds."
        }
        Start-Sleep -Milliseconds 500
        $process.Refresh()
    }
}
finally {
    Remove-Item $override -ErrorAction SilentlyContinue
    if (Test-Path $log) {
        Get-Content $log | Where-Object { $_ -match 'zone|Saved|ERROR|error|Could not|wasn''t found' } | Select-Object -First 30
        if ($env:OPENRANCH_KEEP_LOG) { Write-Output "Log kept at $log" } else { Remove-Item $log -ErrorAction SilentlyContinue }
    }
}

if (-not (Test-Path $outFull)) { throw "No screenshot was written." }
Write-Output "Screenshot: $outFull"
