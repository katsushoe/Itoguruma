[CmdletBinding()]
param(
    [string]$InstallDirectory = "C:\Itoguruma"
)

$ErrorActionPreference = "Stop"
$destinationRoot = [System.IO.Path]::GetFullPath($InstallDirectory)
$cliPath = Join-Path $destinationRoot "bin\itoguruma\itoguruma.exe"
if (Test-Path -LiteralPath $cliPath -PathType Leaf) {
    foreach ($client in @("codex", "claude")) {
        & $cliPath unconfigure $client
        if ($LASTEXITCODE -ne 0) { throw "Itoguruma $client configuration cleanup failed." }
    }
}
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

# The tray Viewer locks bin\viewer; stop it so the managed bin directory can be removed.
$viewerPath = Join-Path $destinationRoot "bin\viewer\itoguruma-viewer.exe"
$runningViewers = @(Get-Process -Name "itoguruma-viewer" -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -ieq $viewerPath })
$runningViewers | Stop-Process -Force
$runningViewers | Wait-Process -Timeout 30 -ErrorAction SilentlyContinue
if (@(Get-Process -Name "itoguruma-viewer" -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -ieq $viewerPath }).Count -ne 0) {
    throw "The Itoguruma Viewer process did not stop."
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

$managedPaths = @(
    "bin",
    "examples",
    "README.md",
    "README.ja.md",
    "COMMANDS.md",
    "COMMANDS.ja.md",
    "CONFIG.md",
    "CONFIG.ja.md",
    "MCP_SETUP.md",
    "MCP_SETUP.ja.md",
    "PACKAGES.md",
    "PACKAGES.ja.md",
    "SECURITY.md",
    "SECURITY.ja.md"
)
foreach ($relativePath in $managedPaths) {
    $managedPath = Join-Path $destinationRoot $relativePath
    if (Test-Path -LiteralPath $managedPath) {
        Remove-Item -LiteralPath $managedPath -Recurse -Force
    }
}

# User data is intentionally retained across uninstall and MSI upgrades.
# In particular, never remove data, config, or logs from the installation root.
if ((Test-Path -LiteralPath $destinationRoot) -and
    @(Get-ChildItem -LiteralPath $destinationRoot -Force).Count -eq 0) {
    Remove-Item -LiteralPath $destinationRoot -Force
}

Write-Host "Itoguruma was uninstalled. User data was retained. / Itogurumaをアンインストールしました。ユーザーデータは保持されています。"
