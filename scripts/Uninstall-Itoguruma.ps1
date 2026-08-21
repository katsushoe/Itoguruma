[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA "Programs\Itoguruma")
)

$ErrorActionPreference = "Stop"
$destinationRoot = [System.IO.Path]::GetFullPath($InstallDirectory)
$taskName = "ItogurumaServer"
$existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($null -ne $existingTask) {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}

$serverPath = Join-Path $destinationRoot "bin\server\Itoguruma.Server.exe"
Get-Process -Name "Itoguruma.Server" -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $serverPath } |
    Stop-Process -Force

foreach ($name in @("ITOGURUMA_AUTH_TOKEN", "ITOGURUMA_DB", "ITOGURUMA_URL", "ITOGURUMA_CONFIG_DIR", "ITOGURUMA_LOG_DIR")) {
    [Environment]::SetEnvironmentVariable($name, $null, "User")
}

$removedPaths = @(
    (Join-Path $destinationRoot "bin\itoguruma"),
    (Join-Path $destinationRoot "bin\stop-codex"),
    (Join-Path $destinationRoot "bin\stop-claude")
)
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
$pathEntries = @($userPath -split ";" | Where-Object {
    $candidate = $_.TrimEnd("\")
    ![string]::IsNullOrWhiteSpace($candidate) -and !($removedPaths | Where-Object { $_.TrimEnd("\") -ieq $candidate })
})
[Environment]::SetEnvironmentVariable("Path", ($pathEntries -join ";"), "User")

if (Test-Path -LiteralPath $destinationRoot) {
    Remove-Item -LiteralPath $destinationRoot -Recurse -Force
}

Write-Host "Itoguruma was uninstalled. / Itogurumaをアンインストールしました。"
