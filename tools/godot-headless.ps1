# Runs the game without a window (Godot --headless) for checks such as --collision-check, at
# below-normal priority, and prints its output. Godot raises its own priority at startup, so this
# script keeps lowering it while it runs. There is no window, so it can't take focus.
#
# Usage:
#   powershell -File tools/godot-headless.ps1 -Godot <Godot .NET exe> -GameArgs "--scene ranch --collision-check"
param(
    [Parameter(Mandatory)] [string] $Godot,
    [string] $GameArgs = "",
    [int] $TimeoutSeconds = 300
)

$game = Resolve-Path (Join-Path $PSScriptRoot "..\game")
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $Godot
$psi.Arguments = "--headless --fixed-fps 60 --path `"$game`" -- $GameArgs"
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true

$process = [System.Diagnostics.Process]::Start($psi)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not $process.HasExited) {
    try { if ($process.PriorityClass -ne 'BelowNormal') { $process.PriorityClass = 'BelowNormal' } } catch { }
    if ((Get-Date) -gt $deadline) {
        $process.Kill()
        Write-Error "Godot did not finish within $TimeoutSeconds seconds."
        exit 1
    }
    Start-Sleep -Milliseconds 300
}
$stdout.Result
$stderr.Result | Where-Object { $_ } | ForEach-Object { $_ -split "`n" | Where-Object { $_ -match 'ERROR|error' } | Select-Object -First 20 }
exit $process.ExitCode
