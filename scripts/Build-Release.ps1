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
$zipPath = Join-Path $releaseRoot ("Itoguruma-" + $normalizedVersion + "-win-x64.zip")
$installerPath = Join-Path $releaseRoot "Install-Itoguruma.ps1"
$checksumPath = Join-Path $releaseRoot "SHA256SUMS.txt"

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $serverRoot, $cliRoot | Out-Null
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

Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination $payloadRoot
Copy-Item -LiteralPath (Join-Path $repoRoot "COMMANDS.md") -Destination $payloadRoot
Copy-Item -LiteralPath (Join-Path $repoRoot "HOOKS.md") -Destination $payloadRoot
$examplesRoot = Join-Path $payloadRoot "examples"
New-Item -ItemType Directory -Force -Path $examplesRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot ".claude\settings.example.json") -Destination (Join-Path $examplesRoot "claude-settings.json")

Compress-Archive -Path (Join-Path $payloadRoot "*") -DestinationPath $zipPath -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "scripts\Install-Itoguruma.ps1") -Destination $installerPath
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value ("$hash  " + [System.IO.Path]::GetFileName($zipPath)) -Encoding ascii

Write-Host "Binary ZIP: $zipPath"
Write-Host "Installer: $installerPath"
Write-Host "Checksums: $checksumPath"
