# Runs a command at below-normal CPU priority and waits for it, so builds, imports
# and tests never slow down whatever else is running on the PC. Processes the
# command starts (MSBuild nodes, test hosts) inherit the lower priority.
#
# Usage: powershell -NoProfile -File tools/run-low.ps1 dotnet build -m:2
# Arguments are taken as-is ($args) so flags like -v:minimal reach the command.

if ($args.Count -lt 1) {
    Write-Error "Usage: run-low.ps1 <program> [arguments...]"
    exit 2
}

$exe = (Get-Command $args[0] -ErrorAction Stop).Source
$rest = @($args | Select-Object -Skip 1 | ForEach-Object {
    if ("$_" -match '[\s"]') { '"' + ("$_" -replace '"', '\"') + '"' } else { "$_" }
})

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.Arguments = ($rest -join ' ')
$psi.UseShellExecute = $false
$psi.WorkingDirectory = (Get-Location).Path

$process = [System.Diagnostics.Process]::Start($psi)
try { $process.PriorityClass = 'BelowNormal' } catch { }
$process.WaitForExit()
exit $process.ExitCode
