<#
.SYNOPSIS
    Builds, publishes, signs, and packages the AdminBoxx installer (the stripped-down
    sibling build of MAIRA that ships to its owner, "Fish").

.DESCRIPTION
    AdminBoxx is the same project compiled with the ADMINBOXX define. This script does
    the whole thing in one command so you no longer have to toggle the define in Visual
    Studio by hand:
      1. (optional) Verification build of Release|x64 with the ADMINBOXX define.
      2. Publishes (FolderProfile) with the ADMINBOXX define into bin\publish.
      3. Code-signs the published exe with Azure Artifact Signing.
      4. Runs Inno Setup against AdminBoxx.iss (which signs the installer + uninstaller).
      5. Verifies the installer signature and extracts the version from its filename.

    The ADMINBOXX define is injected via /p:BuildAdminBoxx=true (see the csproj). That is
    independent of whatever you toggle manually in Visual Studio, so the two never fight.
    Unlike the maintainer's old flow, this builds AdminBoxx as Release (not Debug).

    On success the final lines of stdout are:
        ADMINBOXX_BUILD_OK
        VERSION=<three-part version>
        INSTALLER=<full path to the AdminBoxx installer .exe>
    Then hand that installer to Fish. This script does NOT touch GitHub.

.NOTES
    Mirrors scripts\build-release.ps1 (MAIRA). Same signing prerequisites apply — see the
    "Code signing" section of the release SKILL.md. The signing block below is intentionally
    a copy of the MAIRA one so each script is self-contained and runnable on its own.
#>

[CmdletBinding()]
param(
    # Skip the standalone verification build (step 1). Publish builds Release anyway.
    [switch]$SkipVerifyBuild,

    # Skip code signing entirely. Produces an UNSIGNED installer — local testing only.
    [switch]$SkipSigning
)

$ErrorActionPreference = 'Stop'

function Fail($message) {
    Write-Host "ADMINBOXX_BUILD_FAILED"
    Write-Error $message
    exit 1
}

# --- Signing configuration (Azure Artifact Signing, East US) — mirrors build-release.ps1 ---
$signingEndpoint = "https://eus.codesigning.azure.net"
$signingAccount  = "mairasigning"
$signingProfile  = "maira-public-trust"
$timestampUrl    = "http://timestamp.acs.microsoft.com"

# Refresh PATH from persisted Machine+User scopes so a freshly-installed az / client-tools
# is visible even if this shell predates the install. Build tools are found by absolute
# path below, so this can't disturb them.
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" +
            [System.Environment]::GetEnvironmentVariable("Path","User")

# --- Resolve paths from the script location (repo root is 4 levels up) ---
$repoRoot     = (Get-Item $PSScriptRoot).Parent.Parent.Parent.Parent.FullName
$solution     = Join-Path $repoRoot 'MarvinsAIRARefactored.sln'
$project      = Join-Path $repoRoot 'MarvinsAIRARefactored\MarvinsAIRARefactored.csproj'
$issScript    = Join-Path $repoRoot 'InnoSetup\AdminBoxx.iss'
$publishDir   = Join-Path $repoRoot 'MarvinsAIRARefactored\bin\publish'
$publishedExe = Join-Path $publishDir 'MarvinsAIRARefactored.exe'
$installerDir = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'AdminBoxx'

foreach ($p in @($solution, $project, $issScript)) {
    if (-not (Test-Path $p)) { Fail "Expected file not found: $p" }
}

# --- Locate VS MSBuild (amd64 build) ---
Write-Host "[adminboxx] Locating Visual Studio MSBuild..."
$msbuild = (Get-ChildItem "C:\Program Files\Microsoft Visual Studio" -Recurse -Filter "MSBuild.exe" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like "*amd64*" } | Select-Object -First 1).FullName
if (-not $msbuild) { Fail "Could not find Visual Studio MSBuild.exe (amd64). Is Visual Studio installed?" }
Write-Host "[adminboxx] MSBuild: $msbuild"

# --- Locate Inno Setup compiler ---
$iscc = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { Fail "Could not find Inno Setup compiler (ISCC.exe). Is Inno Setup 6 installed?" }
Write-Host "[adminboxx] ISCC: $iscc"

# --- Signing setup: locate signtool + dlib, write metadata.json + wrapper, pre-flight az ---
$signtool     = $null
$signDlib     = $null
$signMeta     = $null
$isccSignArgs = @()
if ($SkipSigning) {
    Write-Host "[adminboxx] Signing: SKIPPED (-SkipSigning). Output will be UNSIGNED — do not ship it."
} else {
    Write-Host "[adminboxx] Locating signing tools..."

    $signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.Directory.Parent.Name) } -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $signtool) { Fail "signtool.exe (Windows SDK, x64) not found. Install the Windows SDK via the Visual Studio Installer." }

    $signDlib = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\MicrosoftArtifactSigningClientTools\Azure.CodeSigning.Dlib.dll" -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $signDlib) {
        Fail "Artifact Signing dlib not found. Install it once with:`n  winget install -e --id Microsoft.Azure.ArtifactSigningClientTools"
    }

    az account show 1>$null 2>$null
    if ($LASTEXITCODE -ne 0) {
        Fail "Azure CLI is not logged in (the signing credential). Run:`n  az login --tenant d6010dea-824b-4de3-90ca-086b0b51ca2e"
    }

    $signMeta = Join-Path $env:TEMP "maira-signing-metadata.json"
    @{
        Endpoint               = $signingEndpoint
        CodeSigningAccountName = $signingAccount
        CertificateProfileName = $signingProfile
        ExcludeCredentials     = @("ManagedIdentityCredential","WorkloadIdentityCredential")
    } | ConvertTo-Json | Set-Content -Path $signMeta -Encoding UTF8

    Write-Host "[adminboxx] signtool: $signtool"
    Write-Host "[adminboxx] dlib:     $signDlib"
    Write-Host "[adminboxx] profile:  $signingAccount / $signingProfile ($signingEndpoint)"

    # Wrapper .cmd holds the quoted paths internally so ISCC's command line carries no
    # inner quotes (PowerShell would mangle them). See build-release.ps1 for the full
    # rationale. Launched via `cmd /c` because Inno runs the sign tool with CreateProcess.
    $signWrapper = Join-Path $env:TEMP "maira-signtool.cmd"
    @"
@echo off
"$signtool" sign /fd SHA256 /tr $timestampUrl /td SHA256 /dlib "$signDlib" /dmdf "$signMeta" %1
"@ | Set-Content -Path $signWrapper -Encoding ASCII
    if ($signWrapper -match ' ') { Fail "Sign-tool wrapper path contains spaces ($signWrapper); ISCC can't reference it without quotes. Set TEMP to a space-free path." }
    $isccSignArgs = @("/DSignedBuild", "/Smssign=cmd /c $signWrapper `$f")
}

function Invoke-SignFile($path) {
    if ($SkipSigning) { return }
    Write-Host "[adminboxx] Signing: $path"
    & $signtool sign /v /fd SHA256 /tr $timestampUrl /td SHA256 /dlib $signDlib /dmdf $signMeta $path
    if ($LASTEXITCODE -ne 0) { Fail "Signing failed for '$path' (signtool exit $LASTEXITCODE)." }
    & $signtool verify /pa /q $path
    if ($LASTEXITCODE -ne 0) { Fail "Signature verification failed for '$path' (signtool exit $LASTEXITCODE)." }
    Write-Host "[adminboxx] Signed + verified: $path"
}

# --- Step 1: verification build (Release|x64, ADMINBOXX) ---
if (-not $SkipVerifyBuild) {
    Write-Host "[adminboxx] Step 1/5: Release build with ADMINBOXX (verifying it compiles)..."
    & $msbuild $solution /t:Build /p:Configuration=Release /p:Platform=x64 /p:BuildAdminBoxx=true /m /nologo /v:minimal
    if ($LASTEXITCODE -ne 0) { Fail "AdminBoxx release build failed (exit $LASTEXITCODE). Fix compile errors before building." }
    Write-Host "[adminboxx] Release build OK."
} else {
    Write-Host "[adminboxx] Step 1/5: skipped (-SkipVerifyBuild)."
}

# --- Step 2: publish via FolderProfile with the ADMINBOXX define ---
Write-Host "[adminboxx] Step 2/5: Publishing (FolderProfile + ADMINBOXX -> bin\publish)..."
if (Test-Path $publishDir) { Remove-Item "$publishDir\*" -Recurse -Force -ErrorAction SilentlyContinue }
# SolutionDir must be passed explicitly (the csproj PostBuild xcopy steps use $(SolutionDir),
# which MSBuild only auto-defines when building THROUGH the .sln).
$solutionDirProp = "/p:SolutionDir=$repoRoot\"
& $msbuild $project /restore /t:Publish /p:PublishProfile=FolderProfile /p:RuntimeIdentifier=win-x64 /p:Configuration=Release /p:Platform=x64 /p:BuildAdminBoxx=true $solutionDirProp /m /nologo /v:minimal
if ($LASTEXITCODE -ne 0) { Fail "Publish failed (exit $LASTEXITCODE)." }
if (-not (Test-Path $publishedExe)) { Fail "Publish completed but $publishedExe was not produced." }
Write-Host "[adminboxx] Publish OK."

# --- Step 3: sign the published app exe (before Inno packages + renames it to AdminBoxx.exe) ---
Write-Host "[adminboxx] Step 3/5: Signing the published app..."
Invoke-SignFile $publishedExe

# --- Step 4: build the installer with Inno Setup (which also signs it + the uninstaller) ---
Write-Host "[adminboxx] Step 4/5: Building installer with Inno Setup..."
$beforeIscc = Get-Date
& $iscc @isccSignArgs $issScript
if ($LASTEXITCODE -ne 0) { Fail "Inno Setup compile failed (exit $LASTEXITCODE)." }

# --- Step 5: locate the installer, verify its signature, extract its version ---
Write-Host "[adminboxx] Step 5/5: Locating installer, verifying signature, extracting version..."
if (-not (Test-Path $installerDir)) { Fail "Installer output folder not found: $installerDir" }

$installer = Get-ChildItem -Path $installerDir -Filter 'AdminBoxx-Setup-*.exe' |
    Where-Object { $_.LastWriteTime -ge $beforeIscc.AddSeconds(-5) } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $installer) {
    Fail "No freshly built installer (AdminBoxx-Setup-*.exe) found in $installerDir. Did ISCC actually write output there?"
}

if (-not $SkipSigning) {
    & $signtool verify /pa /q $installer.FullName
    if ($LASTEXITCODE -ne 0) { Fail "Installer was built but is NOT validly signed: $($installer.FullName)" }
    Write-Host "[adminboxx] Installer signature verified."
}

if ($installer.Name -notmatch '^AdminBoxx-Setup-(?<ver>\d+\.\d+\.\d+(\.\d+)?)\.exe$') {
    Fail "Installer filename '$($installer.Name)' did not match the expected pattern AdminBoxx-Setup-<x.y.buildStamp>.exe. Refusing to guess a version."
}
$version = $Matches['ver']

Write-Host ""
Write-Host "ADMINBOXX_BUILD_OK"
Write-Host "VERSION=$version"
Write-Host "INSTALLER=$($installer.FullName)"
exit 0
