[CmdletBinding()]
param(
    [string]$Version = "latest",
    [string]$PackagePath = "",
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA "Programs\Itoguruma"),
    [switch]$NoPath,
    [switch]$SkipCodex,
    [switch]$SkipClaude
)

$ErrorActionPreference = "Stop"
$repository = "katsushoe/Itoguruma"
$destinationRoot = [System.IO.Path]::GetFullPath($InstallDirectory)
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("itoguruma-install-" + [guid]::NewGuid().ToString("N"))

function Invoke-ClientCommand {
    param([System.Management.Automation.CommandInfo]$Command, [string[]]$Arguments)

    & $Command.Source @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$($Command.Name) failed with exit code $LASTEXITCODE."
    }
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
        "examples\claude-settings.json",
        "examples\codex-hooks.json",
        "README.md",
        "COMMANDS.md",
        "HOOKS.md"
    )
    foreach ($relativePath in $requiredFiles) {
        if (!(Test-Path -LiteralPath (Join-Path $extractRoot $relativePath) -PathType Leaf)) {
            throw "The binary ZIP is incomplete. Missing file: $relativePath"
        }
    }

    New-Item -ItemType Directory -Force -Path $destinationRoot | Out-Null
    Copy-Item -Path (Join-Path $extractRoot "*") -Destination $destinationRoot -Recurse -Force
    $dataRoot = Join-Path $destinationRoot "data"
    New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null

    $cliDirectory = Join-Path $destinationRoot "bin\itoguruma"
    & (Join-Path $cliDirectory "itoguruma.exe") --help | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Installation verification failed."
    }

    if (!$NoPath) {
        $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
        $pathEntries = @($userPath -split ";" | Where-Object { ![string]::IsNullOrWhiteSpace($_) })
        if (!($pathEntries | Where-Object { $_.TrimEnd("\") -ieq $cliDirectory.TrimEnd("\") })) {
            [Environment]::SetEnvironmentVariable("Path", (($pathEntries + $cliDirectory) -join ";"), "User")
        }
    }

    $serverPath = Join-Path $destinationRoot "bin\server\Itoguruma.Server.exe"
    $databasePath = Join-Path $dataRoot "messages.db"
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
            Invoke-ClientCommand $codex @("mcp", "add", "itoguruma", "--env", "ITOGURUMA_DB=$databasePath", "--", $serverPath)
        }
    }
    if (!$SkipClaude) {
        $claude = Get-Command claude -ErrorAction SilentlyContinue
        if ($null -ne $claude) {
            & $claude.Source mcp remove --scope user itoguruma 2>$null | Out-Null
            Invoke-ClientCommand $claude @("mcp", "add", "--scope", "user", "--env", "ITOGURUMA_DB=$databasePath", "itoguruma", "--", $serverPath)
        }
    }

    Write-Host "Itoguruma installed: $destinationRoot"
    Write-Host "Database: $databasePath"
    Write-Host "Claude Code Hook example: $(Join-Path $examplesRoot 'claude-settings.json')"
    Write-Host "Codex Hook example: $(Join-Path $examplesRoot 'codex-hooks.json')"
    if (!$NoPath) {
        Write-Host "Open a new terminal to use itoguruma from PATH."
    }
    Write-Host "Restart Codex and Claude Code before using the MCP server."
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
