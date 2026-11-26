[CmdletBinding()]
param(
    [string]$Version = "latest",
    [string]$PackagePath = "",
    [string]$InstallDirectory = "C:\Itoguruma",
    [string]$PreviousInstallDirectory = "",
    [string]$ConfigDirectory = "",
    [string]$LogDirectory = "",
    [ValidateSet("", "en", "ja")]
    [string]$Language = "",
    [string]$ServerUrl = "http://127.0.0.1:47631",
    [ValidateRange(5, 120)]
    [int]$ServerStopTimeoutSeconds = 30,
    [switch]$NoPath,
    [switch]$SkipCodex,
    [switch]$SkipClaude
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Language)) {
    Write-Host "Select language / 言語を選択してください:"
    Write-Host "  1. English"
    Write-Host "  2. 日本語"
    $languageSelection = Read-Host "Language [1/2]"
    $Language = if ($languageSelection -eq "2") { "ja" } else { "en" }
}
function Get-LocalizedText {
    param([string]$English, [string]$Japanese)
    if ($Language -eq "ja") { return $Japanese }
    return $English
}
$repository = "katsushoe/Itoguruma"
$destinationRoot = [System.IO.Path]::GetFullPath($InstallDirectory).TrimEnd("\")
$previousRoot = if ([string]::IsNullOrWhiteSpace($PreviousInstallDirectory)) {
    $destinationRoot
} else {
    [System.IO.Path]::GetFullPath($PreviousInstallDirectory).TrimEnd("\")
}
# Relocation moves an existing installation (binaries, data, config, logs) to a new install root.
$isRelocation = !$previousRoot.Equals($destinationRoot, [System.StringComparison]::OrdinalIgnoreCase)
if ($isRelocation -and
    (($destinationRoot + "\").StartsWith($previousRoot + "\", [System.StringComparison]::OrdinalIgnoreCase) -or
     ($previousRoot + "\").StartsWith($destinationRoot + "\", [System.StringComparison]::OrdinalIgnoreCase))) {
    throw "The new install directory must not be inside the previous one, or the reverse: $previousRoot -> $destinationRoot"
}
$configRoot = if ([string]::IsNullOrWhiteSpace($ConfigDirectory)) {
    Join-Path $destinationRoot "config"
} else {
    [System.IO.Path]::GetFullPath($ConfigDirectory)
}
$logRoot = if ([string]::IsNullOrWhiteSpace($LogDirectory)) {
    Join-Path $destinationRoot "logs"
} else {
    [System.IO.Path]::GetFullPath($LogDirectory)
}
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("itoguruma-install-" + [guid]::NewGuid().ToString("N"))

function Read-ExistingSettings {
    # Returns the first readable settings file among the candidates, or $null when none exists.
    param([string[]]$CandidatePaths)
    foreach ($candidate in $CandidatePaths) {
        if (!(Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        try { return Get-Content -LiteralPath $candidate -Raw | ConvertFrom-Json }
        catch { Write-Warning "Ignoring unreadable settings file: $candidate" }
    }
    return $null
}

function Merge-Settings {
    # Copies every value from $Existing over $Defaults, recursing into nested sections; keys only in $Defaults keep their default.
    param($Defaults, $Existing)
    foreach ($property in $Existing.PSObject.Properties) {
        $default = $Defaults.PSObject.Properties[$property.Name]
        if ($null -ne $default -and $default.Value -is [System.Management.Automation.PSCustomObject] -and
            $property.Value -is [System.Management.Automation.PSCustomObject]) {
            Merge-Settings $default.Value $property.Value
        }
        elseif ($null -ne $default) {
            $Defaults.($property.Name) = $property.Value
        }
        else {
            $Defaults | Add-Member -NotePropertyName $property.Name -NotePropertyValue $property.Value
        }
    }
}

function Restore-UserDirectory {
    # Copies a saved user directory back, except appsettings.json, which is already merged into the new configuration.
    param([string]$Source, [string]$Destination)
    if (!(Test-Path -LiteralPath $Source -PathType Container)) { return }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -LiteralPath $Source -Force |
        Where-Object { $_.Name -ine "appsettings.json" } |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force }
}

function Find-ClientCommand {
    param([string]$Name, [string[]]$CandidatePaths)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command }

    foreach ($candidatePath in $CandidatePaths) {
        if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
            return Get-Command $candidatePath -ErrorAction Stop
        }
    }

    return $null
}

function Invoke-DownloadFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,
        [Parameter(Mandatory = $true)]
        [string]$Destination,
        [int]$MaximumAttempts = 3,
        [int]$TimeoutSeconds = 300
    )

    $originalProgressPreference = $ProgressPreference
    try {
        # Windows PowerShell 5.1 can become extremely slow while rendering
        # Invoke-WebRequest progress for large release assets.
        $ProgressPreference = "SilentlyContinue"
        for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
            try {
                Invoke-WebRequest -Uri $Uri -OutFile $Destination -Headers @{ "User-Agent" = "Itoguruma-Installer" } -TimeoutSec $TimeoutSeconds
                return
            }
            catch {
                Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
                if ($attempt -eq $MaximumAttempts) {
                    throw
                }
                Start-Sleep -Seconds ([Math]::Pow(2, $attempt - 1))
            }
        }
    }
    finally {
        $ProgressPreference = $originalProgressPreference
    }
}

try {
    New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null
    $archivePath = Join-Path $temporaryRoot "package.zip"
    if ([string]::IsNullOrWhiteSpace($PackagePath)) {
        $releaseUri = if ($Version -eq "latest") {
            "https://api.github.com/repos/$repository/releases/latest"
        }
        else {
            $tag = if ($Version.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) { $Version } else { "v$Version" }
            "https://api.github.com/repos/$repository/releases/tags/$tag"
        }
        $release = Invoke-RestMethod -Uri $releaseUri -Headers @{ "User-Agent" = "Itoguruma-Installer" }
        $normalizedVersion = $release.tag_name.TrimStart("v")
        $assetName = "Itoguruma-$normalizedVersion-win-x64.zip"
        $asset = $release.assets | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
        if ($null -eq $asset) {
            throw "Release asset was not found: $assetName"
        }
        Invoke-DownloadFile -Uri $asset.browser_download_url -Destination $archivePath
        $checksumAsset = $release.assets | Where-Object { $_.name -eq "SHA256SUMS.txt" } | Select-Object -First 1
        if ($null -eq $checksumAsset) {
            throw "Release checksum asset was not found."
        }
        $checksumText = Invoke-RestMethod -Uri $checksumAsset.browser_download_url -Headers @{ "User-Agent" = "Itoguruma-Installer" }
        $checksumLine = $checksumText -split "`r?`n" | Where-Object { $_ -match [regex]::Escape($assetName) } | Select-Object -First 1
        $checksumMatch = [regex]::Match([string]$checksumLine, "^([0-9a-fA-F]{64})\s+")
        if (!$checksumMatch.Success) {
            throw "Release checksum entry is invalid: $assetName"
        }
        $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
        if (!$actualHash.Equals($checksumMatch.Groups[1].Value, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Release checksum verification failed: $assetName"
        }
    }
    else {
        Copy-Item -LiteralPath ([System.IO.Path]::GetFullPath($PackagePath)) -Destination $archivePath
    }

    $extractRoot = Join-Path $temporaryRoot "files"
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force
    $requiredFiles = @(
        "bin\server\Itoguruma.Server.exe",
        "bin\itoguruma\itoguruma.exe",
        "bin\viewer\itoguruma-viewer.exe",
        "bin\database-migrator\itoguruma-database-migrator.exe",
        "bin\stop-codex\stop-codex.exe",
        "bin\stop-claude\stop-claude.exe",
        "examples\claude-settings.json",
        "examples\codex-hooks.json",
        "README.md",
        "README.ja.md",
        "COMMANDS.md",
        "COMMANDS.ja.md",
        "CONFIG.md",
        "CONFIG.ja.md",
        "MCP_SETUP.md",
        "MCP_SETUP.ja.md",
        "PACKAGES.md",
        "SECURITY.md"
    )
    foreach ($relativePath in $requiredFiles) {
        if (!(Test-Path -LiteralPath (Join-Path $extractRoot $relativePath) -PathType Leaf)) {
            throw "The binary ZIP is incomplete. Missing file: $relativePath"
        }
    }
    $packageProxy = @(Get-ChildItem -LiteralPath (Join-Path $extractRoot "bin\mcp-proxy") `
        -Filter "Itoguruma.McpProxy.exe" -File -Recurse)
    if ($packageProxy.Count -ne 1) {
        throw "The binary ZIP must contain exactly one versioned Itoguruma.McpProxy.exe."
    }
    $packageProxyRelativePath = $packageProxy[0].FullName.Substring($extractRoot.TrimEnd("\").Length).TrimStart("\")

    $taskName = "ItogurumaServer"
    $existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    $serverPaths = @((Join-Path $destinationRoot "bin\server\Itoguruma.Server.exe"),
        (Join-Path $previousRoot "bin\server\Itoguruma.Server.exe"))
    if ($null -ne $existingTask) {
        $serverPaths += @($existingTask.Actions.Execute | Where-Object { ![string]::IsNullOrWhiteSpace($_) })
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    }
    $installedServers = @(Get-Process -Name "Itoguruma.Server" -ErrorAction SilentlyContinue |
        Where-Object { $serverPaths -contains $_.Path })
    $installedServers | Stop-Process -Force -ErrorAction SilentlyContinue
    $stopDeadline = [DateTimeOffset]::UtcNow.AddSeconds($ServerStopTimeoutSeconds)
    do {
        $installedServers = @(Get-Process -Name "Itoguruma.Server" -ErrorAction SilentlyContinue |
            Where-Object { $serverPaths -contains $_.Path })
        if ($installedServers.Count -eq 0) { break }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $stopDeadline)
    if ($installedServers.Count -ne 0) {
        $remainingProcessIds = ($installedServers.Id -join ", ")
        throw "The existing Itoguruma.Server process did not stop within $ServerStopTimeoutSeconds seconds. Process IDs: $remainingProcessIds"
    }
    $installedServerPath = Join-Path $destinationRoot "bin\server\Itoguruma.Server.exe"
    if (Test-Path -LiteralPath $installedServerPath) {
        $serverUnlocked = $false
        for ($attempt = 1; $attempt -le 40; $attempt++) {
            try {
                $serverStream = [System.IO.File]::Open(
                    $installedServerPath,
                    [System.IO.FileMode]::Open,
                    [System.IO.FileAccess]::ReadWrite,
                    [System.IO.FileShare]::None)
                $serverStream.Dispose()
                $serverUnlocked = $true
                break
            }
            catch [System.IO.IOException] {
                Start-Sleep -Milliseconds 250
            }
        }
        if (!$serverUnlocked) {
            throw "The existing Itoguruma.Server process did not release its executable."
        }
    }
    # The tray Viewer keeps its executable locked; stop it before copying and restart it after installation.
    $viewerPath = Join-Path $destinationRoot "bin\viewer\itoguruma-viewer.exe"
    $viewerPaths = @($viewerPath, (Join-Path $previousRoot "bin\viewer\itoguruma-viewer.exe"))
    $runningViewers = @(Get-Process -Name "itoguruma-viewer" -ErrorAction SilentlyContinue |
        Where-Object { $viewerPaths -contains $_.Path })
    $runningViewers | Stop-Process -Force -ErrorAction SilentlyContinue
    $viewerStopDeadline = [DateTimeOffset]::UtcNow.AddSeconds($ServerStopTimeoutSeconds)
    do {
        $runningViewers = @(Get-Process -Name "itoguruma-viewer" -ErrorAction SilentlyContinue |
            Where-Object { $viewerPaths -contains $_.Path })
        if ($runningViewers.Count -eq 0) { break }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $viewerStopDeadline)
    if ($runningViewers.Count -ne 0) {
        $remainingViewerIds = ($runningViewers.Id -join ", ")
        throw "The existing Itoguruma Viewer process did not stop within $ServerStopTimeoutSeconds seconds. Process IDs: $remainingViewerIds"
    }
    $installedPackageProxyPath = Join-Path $destinationRoot $packageProxyRelativePath
    if ((Test-Path -LiteralPath $installedPackageProxyPath -PathType Leaf) -and
        ((Get-FileHash -LiteralPath $installedPackageProxyPath -Algorithm SHA256).Hash -eq
         (Get-FileHash -LiteralPath $packageProxy[0].FullName -Algorithm SHA256).Hash)) {
        Remove-Item -LiteralPath $packageProxy[0].FullName -Force
    }
    New-Item -ItemType Directory -Force -Path $destinationRoot | Out-Null
    Copy-Item -Path (Join-Path $extractRoot "*") -Destination $destinationRoot -Recurse -Force
    New-Item -ItemType Directory -Force -Path $configRoot, $logRoot | Out-Null
    $settingsPath = Join-Path $configRoot "appsettings.json"
    $upgradeBackupRoot = Join-Path $env:LOCALAPPDATA "Itoguruma Upgrade Backup"
    # Keep the user's settings across upgrades: read the existing file before writing anything, then merge it
    # over the packaged defaults so values such as CrRoot survive and newly added keys receive their defaults.
    $existingSettings = Read-ExistingSettings @(
        (Join-Path $upgradeBackupRoot "config\appsettings.json"),
        $settingsPath,
        (Join-Path $previousRoot "config\appsettings.json"))
    $packagedSettingsPath = Join-Path $destinationRoot "bin\server\appsettings.json"
    $settings = Get-Content -LiteralPath $packagedSettingsPath -Raw | ConvertFrom-Json
    Remove-Item -LiteralPath $packagedSettingsPath -Force
    if ($null -ne $existingSettings) { Merge-Settings $settings $existingSettings }
    # Installer arguments only fill values the existing settings do not already define.
    if ([string]::IsNullOrWhiteSpace([string]$settings.Itoguruma.Language)) { $settings.Itoguruma.Language = $Language }
    if ([string]::IsNullOrWhiteSpace([string]$settings.Itoguruma.ServerUrl)) { $settings.Itoguruma.ServerUrl = $ServerUrl }
    $Language = [string]$settings.Itoguruma.Language
    $ServerUrl = [string]$settings.Itoguruma.ServerUrl
    $dataRoot = Join-Path $destinationRoot "data"
    New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null
    if ([string]::IsNullOrWhiteSpace([string]$settings.Itoguruma.DatabasePath)) {
        $settings.Itoguruma.DatabasePath = Join-Path $dataRoot "messages.db"
    }
    $databasePath = [string]$settings.Itoguruma.DatabasePath
    $settingsJson = $settings | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($settingsPath, $settingsJson, (New-Object System.Text.UTF8Encoding($false)))
    $legacyRoot = Join-Path $env:LOCALAPPDATA "Programs\Itoguruma"
    $legacyDatabasePath = Join-Path $legacyRoot "data\messages.db"
    if (![System.IO.Path]::GetFullPath($legacyRoot).Equals($destinationRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $legacyDatabasePath -PathType Leaf)) {
        $migratorPath = Join-Path $destinationRoot "bin\database-migrator\itoguruma-database-migrator.exe"
        & $migratorPath --destination $databasePath --source $legacyDatabasePath --backup-directory (Join-Path $dataRoot "migration-backups")
        if ($LASTEXITCODE -ne 0) {
            throw "Database migration failed with exit code $LASTEXITCODE."
        }
    }

    if (Test-Path -LiteralPath $upgradeBackupRoot -PathType Container) {
        foreach ($name in @("data", "config", "logs")) {
            Restore-UserDirectory (Join-Path $upgradeBackupRoot $name) (Join-Path $destinationRoot $name)
        }
        Remove-Item -LiteralPath $upgradeBackupRoot -Recurse -Force
    }
    elseif ($isRelocation) {
        # Without an MSI upgrade backup, carry the user data over from the previous install root directly.
        foreach ($name in @("data", "config", "logs")) {
            Restore-UserDirectory (Join-Path $previousRoot $name) (Join-Path $destinationRoot $name)
        }
    }
    if ($isRelocation) {
        # Restored settings still point at the previous root; re-point paths inside it to the new root.
        $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        $restoredDatabasePath = [string]$settings.Itoguruma.DatabasePath
        if (![string]::IsNullOrWhiteSpace($restoredDatabasePath) -and
            ([System.IO.Path]::GetFullPath($restoredDatabasePath)).StartsWith($previousRoot + "\", [System.StringComparison]::OrdinalIgnoreCase)) {
            $settings.Itoguruma.DatabasePath = $destinationRoot + [System.IO.Path]::GetFullPath($restoredDatabasePath).Substring($previousRoot.Length)
            $settingsJson = $settings | ConvertTo-Json -Depth 10
            [System.IO.File]::WriteAllText($settingsPath, $settingsJson, (New-Object System.Text.UTF8Encoding($false)))
        }
        $databasePath = $settings.Itoguruma.DatabasePath
    }

    $cliDirectory = Join-Path $destinationRoot "bin\itoguruma"
    $stopCodexDirectory = Join-Path $destinationRoot "bin\stop-codex"
    $stopClaudeDirectory = Join-Path $destinationRoot "bin\stop-claude"
    & (Join-Path $cliDirectory "itoguruma.exe") --help | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Installation verification failed."
    }
    & (Join-Path $stopCodexDirectory "stop-codex.exe") --list | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "stop-codex installation verification failed."
    }
    & (Join-Path $stopClaudeDirectory "stop-claude.exe") --list | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "stop-claude installation verification failed."
    }

    if (!$NoPath) {
        $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
        $previousBinRoot = (Join-Path $previousRoot "bin").TrimEnd("\") + "\"
        $pathEntries = @($userPath -split ";" | Where-Object {
            ![string]::IsNullOrWhiteSpace($_) -and
            !($isRelocation -and ($_.TrimEnd("\") + "\").StartsWith($previousBinRoot, [System.StringComparison]::OrdinalIgnoreCase))
        })
        foreach ($pathDirectory in @($cliDirectory, $stopCodexDirectory, $stopClaudeDirectory)) {
            if (!($pathEntries | Where-Object { $_.TrimEnd("\") -ieq $pathDirectory.TrimEnd("\") })) {
                $pathEntries += $pathDirectory
            }
        }
        [Environment]::SetEnvironmentVariable("Path", ($pathEntries -join ";"), "User")
    }

    $serverPath = Join-Path $destinationRoot "bin\server\Itoguruma.Server.exe"
    $proxyPath = Join-Path $destinationRoot $packageProxyRelativePath
    $mcpUrl = $ServerUrl.TrimEnd("/") + "/mcp"
    $cliPath = Join-Path $cliDirectory "itoguruma.exe"
    $authStatus = @(& $cliPath auth status)
    if ($LASTEXITCODE -ne 0) { throw "Authentication status check failed." }
    # Keep an existing token so running Codex and Claude Code sessions stay authorized across upgrades.
    # The "Generation ID:" line is printed only when a token is configured and is not localized.
    if (!($authStatus | Where-Object { $_ -like "Generation ID:*" })) {
        "ROTATE" | & $cliPath auth rotate | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Authentication credential creation failed." }
    }
    foreach ($variableName in @("ITOGURUMA_AUTH_TOKEN", "ITOGURUMA_DB", "ITOGURUMA_URL", "ITOGURUMA_CONFIG_DIR", "ITOGURUMA_LOG_DIR")) {
        [Environment]::SetEnvironmentVariable($variableName, $null, "User")
    }
    if (!$SkipCodex) {
        $codexCandidates = @(Get-ChildItem -LiteralPath (Join-Path $env:LOCALAPPDATA "OpenAI\Codex\bin") `
            -Filter "codex.exe" -File -Recurse -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -ExpandProperty FullName)
        $codex = Find-ClientCommand "codex" $codexCandidates
        if ($null -ne $codex) {
            & $cliPath configure codex --url $mcpUrl --proxy-path $proxyPath
            if ($LASTEXITCODE -ne 0) { throw "Itoguruma Codex configuration failed." }
        }
    }
    if (!$SkipClaude) {
        $claude = Find-ClientCommand "claude" @(
            (Join-Path $env:APPDATA "npm\claude.cmd"),
            (Join-Path $env:USERPROFILE ".local\bin\claude.exe")
        )
        if ($null -ne $claude) {
            & $cliPath configure claude --url $mcpUrl --proxy-path $proxyPath
            if ($LASTEXITCODE -ne 0) { throw "Itoguruma Claude Code configuration failed." }
        }
    }

    $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    Remove-ItemProperty -Path $runKey -Name "ItogurumaServer" -ErrorAction SilentlyContinue
    # Keep an enabled Viewer startup entry pointing at this installation; a tray start never re-registers it.
    if ($null -ne (Get-ItemProperty -Path $runKey -Name "ItogurumaViewer" -ErrorAction SilentlyContinue)) {
        $viewerStartupCommand = '"' + (Join-Path $destinationRoot "bin\viewer\itoguruma-viewer.exe") + '" --tray'
        Set-ItemProperty -Path $runKey -Name "ItogurumaViewer" -Value $viewerStartupCommand
    }
    $taskUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $taskArguments = '--config-dir "' + $configRoot + '" --log-dir "' + $logRoot + '"'
    $taskAction = New-ScheduledTaskAction -Execute $serverPath -Argument $taskArguments -WorkingDirectory (Split-Path $serverPath)
    $taskTrigger = New-ScheduledTaskTrigger -AtLogOn -User $taskUser
    $taskPrincipal = New-ScheduledTaskPrincipal -UserId $taskUser -LogonType Interactive -RunLevel Limited
    $taskSettings = New-ScheduledTaskSettingsSet `
        -MultipleInstances IgnoreNew `
        -RestartCount 3 `
        -RestartInterval (New-TimeSpan -Minutes 1) `
        -ExecutionTimeLimit ([TimeSpan]::Zero) `
        -StartWhenAvailable
    Register-ScheduledTask `
        -TaskName $taskName `
        -Action $taskAction `
        -Trigger $taskTrigger `
        -Principal $taskPrincipal `
        -Settings $taskSettings `
        -Description "Run the per-user Itoguruma MCP Streamable HTTP server." `
        -Force | Out-Null
    $healthUrl = $ServerUrl.TrimEnd("/") + "/health"
    function Wait-ServerHealth {
        param([int]$Attempts)
        for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
            try {
                $health = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 2
                if ($health.status -eq "ok") { return $true }
            }
            catch { }
            Start-Sleep -Milliseconds 250
        }
        return $false
    }
    # Start through the scheduled task first. A server launched from the Windows Installer custom-action
    # context cannot see some drive paths (for example CR files under a junction on another volume) even
    # though it runs as the same user; the task starts it in the normal sign-in context.
    $serverProcess = $null
    Start-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    $serverReady = Wait-ServerHealth -Attempts 60
    if ($serverReady) {
        $serverProcess = Get-Process -Name "Itoguruma.Server" -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -ieq $serverPath } | Select-Object -First 1
    }
    else {
        # Fall back to a direct start so installation still succeeds; the next sign-in starts the task normally.
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        Get-Process -Name "Itoguruma.Server" -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -ieq $serverPath } | Stop-Process -Force -ErrorAction SilentlyContinue
        Write-Warning "The scheduled task did not start Itoguruma.Server; starting it directly. Some drive paths may be unavailable to the server until the next sign-in."
        $serverProcess = Start-Process `
            -FilePath $serverPath `
            -ArgumentList @("--config-dir", $configRoot, "--log-dir", $logRoot) `
            -WorkingDirectory (Split-Path $serverPath) `
            -WindowStyle Hidden `
            -PassThru
        $serverReady = Wait-ServerHealth -Attempts 20
    }
    if (!$serverReady -or $null -eq $serverProcess) { throw "Itoguruma.Server health check failed: $healthUrl" }
    $serverProcess.Refresh()
    if ($serverProcess.HasExited) { throw "Itoguruma.Server exited after installation." }

    Start-Process `
        -FilePath $viewerPath `
        -ArgumentList "--tray" `
        -WorkingDirectory (Split-Path $viewerPath) `
        -WindowStyle Hidden | Out-Null

    if ($isRelocation -and (Test-Path -LiteralPath $previousRoot -PathType Container)) {
        # The new installation is running; remove the previous binaries and documents that this package replaces.
        # data, config, and logs stay at the previous root as a fallback copy of the migrated user data.
        foreach ($item in @(Get-ChildItem -LiteralPath $extractRoot -Force)) {
            $previousItem = Join-Path $previousRoot $item.Name
            if (Test-Path -LiteralPath $previousItem) {
                Remove-Item -LiteralPath $previousItem -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        Write-Host ((Get-LocalizedText "Moved from" "移行元") + ": $previousRoot (" +
            (Get-LocalizedText "data, config, and logs were kept there" "data、config、logsは移行元に残しています") + ")")
    }

    Write-Host ((Get-LocalizedText "Itoguruma installed" "Itogurumaをインストールしました") + ": $destinationRoot")
    Write-Host ((Get-LocalizedText "Configuration" "設定") + ": $configRoot")
    Write-Host ((Get-LocalizedText "Language" "言語") + ": $Language")
    Write-Host ((Get-LocalizedText "Logs" "ログ") + ": $logRoot")
    Write-Host ((Get-LocalizedText "Database" "データベース") + ": $databasePath")
    Write-Host ((Get-LocalizedText "MCP endpoint" "MCPエンドポイント") + ": $mcpUrl")
    $examplesRoot = Join-Path $destinationRoot "examples"
    Write-Host "Claude Code Hook example: $(Join-Path $examplesRoot 'claude-settings.json')"
    Write-Host "Codex Hook example: $(Join-Path $examplesRoot 'codex-hooks.json')"
    Write-Host "Message Viewer: $(Join-Path $destinationRoot 'bin\viewer\itoguruma-viewer.exe')"
    Write-Host "Codex process stopper: $(Join-Path $stopCodexDirectory 'stop-codex.exe')"
    Write-Host "Claude process stopper: $(Join-Path $stopClaudeDirectory 'stop-claude.exe')"
    if (!$NoPath) {
        Write-Host (Get-LocalizedText "Open a new terminal to use itoguruma, stop-codex, and stop-claude from PATH." "PATHからitoguruma、stop-codex、stop-claudeを使用するには、新しいターミナルを開いてください。")
    }
    Write-Host (Get-LocalizedText "Restart Codex and Claude Code before using the MCP server." "MCPサーバーを使用する前にCodexとClaude Codeを再起動してください。")
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
