<#
.SYNOPSIS
    Builds, publishes, and packages a MarvinsAIRA Refactored release installer.

.DESCRIPTION
    Runs the deterministic part of the release pipeline:
      1. Compiles a Release|x64 build of the solution (fails the run if it doesn't compile).
      2. Publishes the app via the FolderProfile publish profile into bin\publish.
      3. Runs Inno Setup (ISCC) to build the installer .exe.
      4. Locates the freshly built installer and extracts the version from its filename.

    On success the final lines of stdout are a machine-readable result block:
        RELEASE_BUILD_OK
        VERSION=<four-part version, e.g. 2.0.439.1234>
        INSTALLER=<full path to the installer .exe>

    Any failure writes an error and exits non-zero, so the caller can stop the pipeline.

.NOTES
    This project uses COM references that require full Visual Studio MSBuild — NOT the
    .NET SDK's `dotnet build` / `dotnet publish` (those fail with MSB4803). The script
    locates the VS MSBuild.exe automatically.
#>

[CmdletBinding()]
param(
    # Skip the standalone verification build (step 1). Publish builds Release anyway,
    # so this is a time saver when you've already confirmed the build compiles.
    [switch]$SkipVerifyBuild
)

$ErrorActionPreference = 'Stop'

function Fail($message) {
    Write-Host "RELEASE_BUILD_FAILED"
    Write-Error $message
    exit 1
}

# --- Resolve paths from the script location (repo root is 4 levels up) ---
# .claude/skills/release/scripts/build-release.ps1  ->  repo root
$repoRoot     = (Get-Item $PSScriptRoot).Parent.Parent.Parent.Parent.FullName
$solution     = Join-Path $repoRoot 'MarvinsAIRARefactored.sln'
$project      = Join-Path $repoRoot 'MarvinsAIRARefactored\MarvinsAIRARefactored.csproj'
$issScript    = Join-Path $repoRoot 'InnoSetup\MarvinsAIRA.iss'
$publishDir   = Join-Path $repoRoot 'MarvinsAIRARefactored\bin\publish'
$publishedExe = Join-Path $publishDir 'MarvinsAIRARefactored.exe'
$installerDir = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'MarvinsAIRA Refactored'

foreach ($p in @($solution, $project, $issScript)) {
    if (-not (Test-Path $p)) { Fail "Expected file not found: $p" }
}

# --- Locate VS MSBuild (amd64 build) ---
Write-Host "[release] Locating Visual Studio MSBuild..."
$msbuild = (Get-ChildItem "C:\Program Files\Microsoft Visual Studio" -Recurse -Filter "MSBuild.exe" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like "*amd64*" } | Select-Object -First 1).FullName
if (-not $msbuild) { Fail "Could not find Visual Studio MSBuild.exe (amd64). Is Visual Studio installed?" }
Write-Host "[release] MSBuild: $msbuild"

# --- Locate Inno Setup compiler ---
$iscc = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { Fail "Could not find Inno Setup compiler (ISCC.exe). Is Inno Setup 6 installed?" }
Write-Host "[release] ISCC: $iscc"

# --- Step 1: verification build (Release|x64) ---
if (-not $SkipVerifyBuild) {
    Write-Host "[release] Step 1/4: Release build (verifying it compiles)..."
    & $msbuild $solution /t:Build /p:Configuration=Release /p:Platform=x64 /m /nologo /v:minimal
    if ($LASTEXITCODE -ne 0) { Fail "Release build failed (exit $LASTEXITCODE). Fix compile errors before releasing." }
    Write-Host "[release] Release build OK."
} else {
    Write-Host "[release] Step 1/4: skipped (-SkipVerifyBuild)."
}

# --- Step 2: publish via FolderProfile ---
Write-Host "[release] Step 2/4: Publishing (FolderProfile -> bin\publish)..."
# Clear stale output so nothing leftover leaks into the installer.
if (Test-Path $publishDir) { Remove-Item "$publishDir\*" -Recurse -Force -ErrorAction SilentlyContinue }
# The profile publishes self-contained for win-x64, which needs a RID-aware
# restore. The earlier verification build restored without a RID, so force a
# fresh restore (/restore) with the RID here, otherwise publish fails NETSDK1047.
#
# SolutionDir must be passed explicitly: the csproj's PostBuild xcopy steps use
# $(SolutionDir), which MSBuild only auto-defines when building THROUGH the .sln.
# Publishing the project directly leaves it "*Undefined*" and the copies fail.
$solutionDirProp = "/p:SolutionDir=$repoRoot\"
& $msbuild $project /restore /t:Publish /p:PublishProfile=FolderProfile /p:RuntimeIdentifier=win-x64 /p:Configuration=Release /p:Platform=x64 $solutionDirProp /m /nologo /v:minimal
if ($LASTEXITCODE -ne 0) { Fail "Publish failed (exit $LASTEXITCODE)." }
if (-not (Test-Path $publishedExe)) { Fail "Publish completed but $publishedExe was not produced." }
Write-Host "[release] Publish OK."

# --- Step 3: build the installer with Inno Setup ---
Write-Host "[release] Step 3/4: Building installer with Inno Setup..."
$beforeIscc = Get-Date
& $iscc $issScript
if ($LASTEXITCODE -ne 0) { Fail "Inno Setup compile failed (exit $LASTEXITCODE)." }

# --- Step 4: locate the installer just built and extract its version ---
Write-Host "[release] Step 4/4: Locating installer and extracting version..."
if (-not (Test-Path $installerDir)) { Fail "Installer output folder not found: $installerDir" }

$installer = Get-ChildItem -Path $installerDir -Filter 'MarvinsAIRARefactored-Setup-*.exe' |
    Where-Object { $_.LastWriteTime -ge $beforeIscc.AddSeconds(-5) } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $installer) {
    Fail "No freshly built installer (MarvinsAIRARefactored-Setup-*.exe) found in $installerDir. Did ISCC actually write output there?"
}

if ($installer.Name -notmatch '^MarvinsAIRARefactored-Setup-(?<ver>\d+\.\d+\.\d+\.\d+)\.exe$') {
    Fail "Installer filename '$($installer.Name)' did not match the expected pattern MarvinsAIRARefactored-Setup-<x.y.z.w>.exe. Refusing to guess a version."
}
$version = $Matches['ver']

Write-Host ""
Write-Host "RELEASE_BUILD_OK"
Write-Host "VERSION=$version"
Write-Host "INSTALLER=$($installer.FullName)"
exit 0
