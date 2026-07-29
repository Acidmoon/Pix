[CmdletBinding()]
param(
    [string]$OutputDirectory = "",
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "dist\PixApp"
}
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$buildIdPath = Join-Path $repoRoot ".next\BUILD_ID"

if (-not (Test-Path -LiteralPath $buildIdPath -PathType Leaf)) {
    throw "Production .next artifacts are missing. Run npm run build before publishing PixApp."
}
if (Test-Path -LiteralPath $outputPath) {
    throw "Output directory already exists: $outputPath. Choose a new empty path."
}

$outputParent = Split-Path -Parent $outputPath
$stagingPath = Join-Path $outputParent (".{0}-staging-{1}" -f (Split-Path -Leaf $outputPath), $PID)
if (Test-Path -LiteralPath $stagingPath) {
    throw "Staging directory already exists: $stagingPath"
}

New-Item -ItemType Directory -Path $outputParent -Force | Out-Null

try {
    # Publish the native launcher first; the web runtime is copied into the same
    # root so PiWebProcessManager can discover it without PI_WEB_ROOT.
    & dotnet publish (Join-Path $PSScriptRoot "PixLauncher.csproj") `
        --configuration Release `
        --runtime $RuntimeIdentifier `
        --self-contained true `
        --output $stagingPath
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    foreach ($directory in @("bin", "public")) {
        $source = Join-Path $repoRoot $directory
        if (-not (Test-Path -LiteralPath $source -PathType Container)) {
            throw "Required runtime directory is missing: $source"
        }
        Copy-Item -LiteralPath $source -Destination (Join-Path $stagingPath $directory) -Recurse
    }

    # .next/dev and .next/cache can contain Windows links that are neither
    # runtime inputs nor portable without administrator privileges.
    $nextSource = Join-Path $repoRoot ".next"
    $nextDestination = Join-Path $stagingPath ".next"
    & robocopy.exe $nextSource $nextDestination /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP `
        /XD (Join-Path $nextSource "cache") (Join-Path $nextSource "dev") | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "Copying .next failed with robocopy exit code $LASTEXITCODE" }

    foreach ($file in @("next.config.ts", "package.json", "package-lock.json")) {
        $source = Join-Path $repoRoot $file
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Required runtime file is missing: $source"
        }
        Copy-Item -LiteralPath $source -Destination (Join-Path $stagingPath $file)
    }

    Push-Location $stagingPath
    try {
        & npm.cmd ci --omit=dev --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE" }
    }
    finally {
        Pop-Location
    }

    New-Item -ItemType Directory -Path (Join-Path $stagingPath "logs") | Out-Null
    Move-Item -LiteralPath $stagingPath -Destination $outputPath
    Write-Output "PixApp published to $outputPath"
}
catch {
    if (Test-Path -LiteralPath $stagingPath) {
        # The staging path is constructed under the requested output parent and
        # contains this process id, so cleanup cannot target the repository root.
        Remove-Item -LiteralPath $stagingPath -Recurse -Force
    }
    throw
}
