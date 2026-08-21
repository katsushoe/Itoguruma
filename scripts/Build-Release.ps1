[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Configuration = "Release",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"

function Invoke-Checked {
    param([string]$FileName, [string[]]$Arguments)

    & $FileName @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FileName failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$normalizedVersion = $Version.TrimStart("v")
$releaseRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $repoRoot ("release\" + $normalizedVersion)
}
elseif ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    [System.IO.Path]::GetFullPath($OutputRoot)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
}

$repoPrefix = $repoRoot.TrimEnd("\") + "\"
if (!$releaseRoot.StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to write outside the repository: $releaseRoot"
}

$payloadRoot = Join-Path $releaseRoot "files"
$serverRoot = Join-Path $payloadRoot "bin\server"
$cliRoot = Join-Path $payloadRoot "bin\itoguruma"
$viewerRoot = Join-Path $payloadRoot "bin\viewer"
$stopCodexRoot = Join-Path $payloadRoot "bin\stop-codex"
$stopClaudeRoot = Join-Path $payloadRoot "bin\stop-claude"
$zipPath = Join-Path $releaseRoot ("Itoguruma-" + $normalizedVersion + "-win-x64.zip")
$installerPath = Join-Path $releaseRoot "Install-Itoguruma.ps1"
$checksumPath = Join-Path $releaseRoot "SHA256SUMS.txt"
$msiPath = Join-Path $releaseRoot ("Itoguruma-" + $normalizedVersion + "-win-x64.msi")
$uninstallerPath = Join-Path $repoRoot "scripts\Uninstall-Itoguruma.ps1"
$msiInstallCommand = Join-Path $repoRoot "scripts\Install-Itoguruma-Msi.cmd"
$msiUninstallCommand = Join-Path $repoRoot "scripts\Uninstall-Itoguruma-Msi.cmd"

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $serverRoot, $cliRoot, $viewerRoot, $stopCodexRoot, $stopClaudeRoot | Out-Null
$publishArguments = @(
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:Version=$normalizedVersion",
    "-p:InformationalVersion=$normalizedVersion"
)
Invoke-Checked "dotnet" (@("publish", (Join-Path $repoRoot "src\Itoguruma.Server\Itoguruma.Server.csproj")) + $publishArguments + @("-o", $serverRoot))
Invoke-Checked "dotnet" (@("publish", (Join-Path $repoRoot "src\Itoguruma.Cli\Itoguruma.Cli.csproj")) + $publishArguments + @("-o", $cliRoot))
Invoke-Checked "dotnet" (@("publish", (Join-Path $repoRoot "src\Itoguruma.Viewer\Itoguruma.Viewer.csproj")) + $publishArguments + @("-o", $viewerRoot))
Invoke-Checked "dotnet" (@("publish", (Join-Path $repoRoot "src\Itoguruma.StopCodex\Itoguruma.StopCodex.csproj")) + $publishArguments + @("-o", $stopCodexRoot))
Invoke-Checked "dotnet" (@("publish", (Join-Path $repoRoot "src\Itoguruma.StopClaude\Itoguruma.StopClaude.csproj")) + $publishArguments + @("-o", $stopClaudeRoot))

$documentFiles = @(
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
foreach ($documentFile in $documentFiles) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $documentFile) -Destination $payloadRoot
}
$examplesRoot = Join-Path $payloadRoot "examples"
New-Item -ItemType Directory -Force -Path $examplesRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot ".claude\settings.example.json") -Destination (Join-Path $examplesRoot "claude-settings.json")
Copy-Item -LiteralPath (Join-Path $repoRoot ".codex\hooks.example.json") -Destination (Join-Path $examplesRoot "codex-hooks.json")

Compress-Archive -Path (Join-Path $payloadRoot "*") -DestinationPath $zipPath -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "scripts\Install-Itoguruma.ps1") -Destination $installerPath
dotnet build (Join-Path $repoRoot "installer\Itoguruma.Installer.wixproj") `
    -t:Rebuild `
    -c $Configuration `
    -p:ProductVersion=$normalizedVersion `
    -p:InstallerScript=$installerPath `
    -p:UninstallerScript=$uninstallerPath `
    -p:MsiInstallCommand=$msiInstallCommand `
    -p:MsiUninstallCommand=$msiUninstallCommand `
    -p:ReleaseArchive=$zipPath `
    -o $releaseRoot
if ($LASTEXITCODE -ne 0) { throw "WiX MSI build failed with exit code $LASTEXITCODE." }

$checksumLines = @($zipPath, $msiPath) | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([System.IO.Path]::GetFileName($_))"
}
Set-Content -LiteralPath $checksumPath -Value $checksumLines -Encoding ascii

Write-Host "Binary ZIP: $zipPath"
Write-Host "Installer: $installerPath"
Write-Host "MSI: $msiPath"
Write-Host "Checksums: $checksumPath"
