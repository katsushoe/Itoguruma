[CmdletBinding()]
param(
    [string]$Version = "latest",
    [string]$PackagePath = "",
    [string]$InstallDirectory = "C:\Itoguruma",
    [string]$ConfigDirectory = "",
    [string]$LogDirectory = "",
    [ValidateSet("", "en", "ja")]
    [string]$Language = "",
    [string]$ServerUrl = "http://127.0.0.1:47631",
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
$destinationRoot = [System.IO.Path]::GetFullPath($InstallDirectory)
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

function Invoke-ClientCommand {
    param([System.Management.Automation.CommandInfo]$Command, [string[]]$Arguments)

    & $Command.Source @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$($Command.Name) failed with exit code $LASTEXITCODE."
    }
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

    $taskName = "ItogurumaServer"
    $existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    $serverPaths = @((Join-Path $destinationRoot "bin\server\Itoguruma.Server.exe"))
    if ($null -ne $existingTask) {
        $serverPaths += @($existingTask.Actions.Execute | Where-Object { ![string]::IsNullOrWhiteSpace($_) })
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    }
    $installedServers = @(Get-Process -Name "Itoguruma.Server" -ErrorAction SilentlyContinue |
        Where-Object { $serverPaths -contains $_.Path })
    $installedServers | Stop-Process -Force
    $installedServers | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
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
    New-Item -ItemType Directory -Force -Path $destinationRoot | Out-Null
    Copy-Item -Path (Join-Path $extractRoot "*") -Destination $destinationRoot -Recurse -Force
    New-Item -ItemType Directory -Force -Path $configRoot, $logRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $destinationRoot "bin\server\appsettings.json") `
        -Destination (Join-Path $configRoot "appsettings.json") -Force
    Remove-Item -LiteralPath (Join-Path $destinationRoot "bin\server\appsettings.json") -Force
    $settingsPath = Join-Path $configRoot "appsettings.json"
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $settings.Itoguruma.Language = $Language
    $settingsJson = $settings | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($settingsPath, $settingsJson, (New-Object System.Text.UTF8Encoding($false)))
    $dataRoot = Join-Path $destinationRoot "data"
    New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null
    $databasePath = Join-Path $dataRoot "messages.db"
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
        $pathEntries = @($userPath -split ";" | Where-Object { ![string]::IsNullOrWhiteSpace($_) })
        foreach ($pathDirectory in @($cliDirectory, $stopCodexDirectory, $stopClaudeDirectory)) {
            if (!($pathEntries | Where-Object { $_.TrimEnd("\") -ieq $pathDirectory.TrimEnd("\") })) {
                $pathEntries += $pathDirectory
            }
        }
        [Environment]::SetEnvironmentVariable("Path", ($pathEntries -join ";"), "User")
    }

    $serverPath = Join-Path $destinationRoot "bin\server\Itoguruma.Server.exe"
    $mcpUrl = $ServerUrl.TrimEnd("/") + "/mcp"
    $authenticationToken = [Environment]::GetEnvironmentVariable("ITOGURUMA_AUTH_TOKEN", "User")
    if ([string]::IsNullOrWhiteSpace($authenticationToken)) {
        $tokenBytes = New-Object byte[] 32
        $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        try { $random.GetBytes($tokenBytes) }
        finally { $random.Dispose() }
        $authenticationToken = [Convert]::ToBase64String($tokenBytes).TrimEnd("=").Replace("+", "-").Replace("/", "_")
    }
    [Environment]::SetEnvironmentVariable("ITOGURUMA_AUTH_TOKEN", $authenticationToken, "User")
    [Environment]::SetEnvironmentVariable("ITOGURUMA_DB", $databasePath, "User")
    [Environment]::SetEnvironmentVariable("ITOGURUMA_URL", $ServerUrl, "User")
    [Environment]::SetEnvironmentVariable("ITOGURUMA_CONFIG_DIR", $configRoot, "User")
    [Environment]::SetEnvironmentVariable("ITOGURUMA_LOG_DIR", $logRoot, "User")
    $env:ITOGURUMA_AUTH_TOKEN = $authenticationToken
    $env:ITOGURUMA_DB = $databasePath
    $env:ITOGURUMA_URL = $ServerUrl
    $env:ITOGURUMA_CONFIG_DIR = $configRoot
    $env:ITOGURUMA_LOG_DIR = $logRoot
    $cliPath = Join-Path $cliDirectory "itoguruma.exe"
    function New-HookSettings {
        param([string]$AgentId)

        $hookCommand = '"' + $cliPath + '" hook --agent ' + $AgentId + ' --db "' + $databasePath + '"'
        $cliPowerShellPath = $cliPath.Replace("'", "''")
        $databasePowerShellPath = $databasePath.Replace("'", "''")
        $hookCommandWindows = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "& ''' + $cliPowerShellPath + ''' hook --agent ' + $AgentId + ' --db ''' + $databasePowerShellPath + '''"'
        $hookEntry = @{
            hooks = @(@{
                type = "command"
                command = $hookCommand
                commandWindows = $hookCommandWindows
                timeout = 15
            })
        }
        return @{
            hooks = @{
                SessionStart = @($hookEntry)
                UserPromptSubmit = @($hookEntry)
                Stop = @($hookEntry)
            }
        }
    }
    $examplesRoot = Join-Path $destinationRoot "examples"
    New-Item -ItemType Directory -Force -Path $examplesRoot | Out-Null
    New-HookSettings "claude-main" | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $examplesRoot "claude-settings.json") -Encoding utf8
    New-HookSettings "codex-main" | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $examplesRoot "codex-hooks.json") -Encoding utf8
    if (!$SkipCodex) {
        $codex = Get-Command codex -ErrorAction SilentlyContinue
        if ($null -ne $codex) {
            & $codex.Source mcp remove itoguruma 2>$null | Out-Null
            Invoke-ClientCommand $codex @("mcp", "add", "itoguruma", "--url", $mcpUrl, "--bearer-token-env-var", "ITOGURUMA_AUTH_TOKEN")
        }
    }
    if (!$SkipClaude) {
        $claude = Find-ClientCommand "claude" @(
            (Join-Path $env:APPDATA "npm\claude.cmd"),
            (Join-Path $env:USERPROFILE ".local\bin\claude.exe")
        )
        if ($null -ne $claude) {
            $previousErrorActionPreference = $ErrorActionPreference
            try {
                $ErrorActionPreference = "Continue"
                & $claude.Source mcp remove --scope user itoguruma 2>$null | Out-Null
            }
            finally {
                $ErrorActionPreference = $previousErrorActionPreference
            }
            Invoke-ClientCommand $claude @(
                "mcp", "add", "--transport", "http", "--scope", "user",
                "itoguruma", $mcpUrl, "--header", 'Authorization: Bearer ${ITOGURUMA_AUTH_TOKEN}')
        }
    }

    $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    Remove-ItemProperty -Path $runKey -Name "ItogurumaServer" -ErrorAction SilentlyContinue
    $taskUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $taskAction = New-ScheduledTaskAction -Execute $serverPath -WorkingDirectory (Split-Path $serverPath)
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
    Start-ScheduledTask -TaskName $taskName
    $healthUrl = $ServerUrl.TrimEnd("/") + "/health"
    $serverReady = $false
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            $health = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 2
            if ($health.status -eq "ok") {
                $serverReady = $true
                break
            }
        }
        catch {
            if ($attempt -eq 20) { throw }
        }
        Start-Sleep -Milliseconds 250
    }
    if (!$serverReady) { throw "Itoguruma.Server health check failed: $healthUrl" }
    $task = Get-ScheduledTask -TaskName $taskName
    if ($task.State -ne "Running") { throw "ItogurumaServer scheduled task is not running." }

    Write-Host ((Get-LocalizedText "Itoguruma installed" "Itogurumaをインストールしました") + ": $destinationRoot")
    Write-Host ((Get-LocalizedText "Configuration" "設定") + ": $configRoot")
    Write-Host ((Get-LocalizedText "Language" "言語") + ": $Language")
    Write-Host ((Get-LocalizedText "Logs" "ログ") + ": $logRoot")
    Write-Host ((Get-LocalizedText "Database" "データベース") + ": $databasePath")
    Write-Host ((Get-LocalizedText "MCP endpoint" "MCPエンドポイント") + ": $mcpUrl")
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
