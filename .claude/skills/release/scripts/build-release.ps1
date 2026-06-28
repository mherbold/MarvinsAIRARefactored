<#
.SYNOPSIS
    Builds, publishes, signs, and packages a MarvinsAIRA Refactored release installer.

.DESCRIPTION
    Runs the deterministic part of the release pipeline:
      1. Compiles a Release|x64 build of the solution (fails the run if it doesn't compile).
      2. Publishes the app via the FolderProfile publish profile into bin\publish.
      3. Code-signs the published MarvinsAIRARefactored.exe with Azure Artifact Signing.
      4. Runs Inno Setup (ISCC), which signs the installer + embedded uninstaller too.
      5. Verifies the installer signature, then extracts the version from its filename.

    On success the final lines of stdout are a machine-readable result block:
        RELEASE_BUILD_OK
        VERSION=<four-part version, e.g. 2.0.439.1234>
        INSTALLER=<full path to the installer .exe>

    Any failure writes an error and exits non-zero, so the caller can stop the pipeline.

.NOTES
    This project uses COM references that require full Visual Studio MSBuild — NOT the
    .NET SDK's `dotnet build` / `dotnet publish` (those fail with MSB4803). The script
    locates the VS MSBuild.exe automatically.

    Signing uses Azure Artifact Signing (formerly Trusted Signing) via SignTool + the
    Artifact Signing dlib, authenticating with the Azure CLI credential (`az login`).
    The signing certificate is issued to "Marvin Herbold". Prerequisites on the build
    machine (one-time):
      * winget install -e --id Microsoft.Azure.ArtifactSigningClientTools
      * winget install -e --id Microsoft.AzureCLI   (then: az login)
      * The Azure account must hold the "Artifact Signing Certificate Profile Signer"
        role on the signing account.
    See the release SKILL.md "Code signing" section for details and troubleshooting.
#>

[CmdletBinding()]
param(
    # Skip the standalone verification build (step 1). Publish builds Release anyway,
    # so this is a time saver when you've already confirmed the build compiles.
    [switch]$SkipVerifyBuild,

    # Skip code signing entirely. Produces an UNSIGNED installer — only for local
    # testing/dev. Never ship an unsigned build to users.
    [switch]$SkipSigning
)

$ErrorActionPreference = 'Stop'

function Fail($message) {
    Write-Host "RELEASE_BUILD_FAILED"
    Write-Error $message
    exit 1
}

# --- Signing configuration (Azure Artifact Signing, East US) ---
# These three values come from the Azure Artifact Signing account + certificate profile.
# None of them are secret (the credential is your interactive `az login`, not a key).
$signingEndpoint = "https://eus.codesigning.azure.net"
$signingAccount  = "mairasigning"
$signingProfile  = "maira-public-trust"
$timestampUrl    = "http://timestamp.acs.microsoft.com"

# AzureCliCredential (used by the signing dlib) shells out to `az`, and the dlib lives
# under LOCALAPPDATA — both are found via PATH/env. Refresh PATH from the persisted
# Machine+User scopes so a freshly-installed az/client-tools is visible even if this
# shell predates the install. Build tools below are located by absolute path, so this
# refresh can't disturb them.
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" +
            [System.Environment]::GetEnvironmentVariable("Path","User")

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

# --- Signing setup: locate signtool + dlib, write metadata.json, pre-flight the credential ---
$signtool   = $null
$signDlib   = $null
$signMeta   = $null
$isccSignArgs = @()   # extra ISCC args that enable installer/uninstaller signing
if ($SkipSigning) {
    Write-Host "[release] Signing: SKIPPED (-SkipSigning). Output will be UNSIGNED — do not ship it."
} else {
    Write-Host "[release] Locating signing tools..."

    # signtool.exe from the newest installed Windows 10/11 SDK (x64). The Artifact
    # Signing dlib needs a recent SDK signtool (>= 10.0.2261.755; 20348 unsupported).
    $signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.Directory.Parent.Name) } -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $signtool) { Fail "signtool.exe (Windows SDK, x64) not found. Install the Windows SDK via the Visual Studio Installer." }

    # Artifact Signing dlib, installed by Microsoft.Azure.ArtifactSigningClientTools.
    $signDlib = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\MicrosoftArtifactSigningClientTools\Azure.CodeSigning.Dlib.dll" -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $signDlib) {
        Fail "Artifact Signing dlib not found. Install it once with:`n  winget install -e --id Microsoft.Azure.ArtifactSigningClientTools"
    }

    # Pre-flight the Azure credential so we fail fast with a clear message instead of a
    # cryptic signtool 403/credential error mid-build.
    az account show 1>$null 2>$null
    if ($LASTEXITCODE -ne 0) {
        Fail "Azure CLI is not logged in (the signing credential). Run:`n  az login --tenant d6010dea-824b-4de3-90ca-086b0b51ca2e"
    }

    # metadata.json describing the signing account + certificate profile. Cloud-only
    # credential probes are excluded so DefaultAzureCredential doesn't hang on a dev box.
    $signMeta = Join-Path $env:TEMP "maira-signing-metadata.json"
    @{
        Endpoint               = $signingEndpoint
        CodeSigningAccountName = $signingAccount
        CertificateProfileName = $signingProfile
        ExcludeCredentials     = @("ManagedIdentityCredential","WorkloadIdentityCredential")
    } | ConvertTo-Json | Set-Content -Path $signMeta -Encoding UTF8

    Write-Host "[release] signtool: $signtool"
    Write-Host "[release] dlib:     $signDlib"
    Write-Host "[release] profile:  $signingAccount / $signingProfile ($signingEndpoint)"

    # Inno signs the installer + embedded uninstaller via a registered "sign tool". We
    # can't put the signtool command directly on ISCC's command line: the paths contain
    # spaces, so the command needs embedded quotes, and PowerShell mangles those when
    # handing the argument to ISCC (which then reads the broken tokens as extra script
    # filenames and aborts). Instead, write a tiny wrapper .cmd that holds the quoted
    # paths INTERNALLY and point Inno at it, so ISCC's command line carries no inner
    # quotes. The wrapper must sit at a space-free path and is launched via `cmd /c`
    # (Inno runs the sign tool with CreateProcess, which can't execute a .cmd directly).
    # Inno substitutes $f with the (quoted) file to sign, which the wrapper passes as %1.
    $signWrapper = Join-Path $env:TEMP "maira-signtool.cmd"
    @"
@echo off
"$signtool" sign /fd SHA256 /tr $timestampUrl /td SHA256 /dlib "$signDlib" /dmdf "$signMeta" %1
"@ | Set-Content -Path $signWrapper -Encoding ASCII
    if ($signWrapper -match ' ') { Fail "Sign-tool wrapper path contains spaces ($signWrapper); ISCC can't reference it without quotes. Set TEMP to a space-free path." }
    $isccSignArgs = @("/DSignedBuild", "/Smssign=cmd /c $signWrapper `$f")
}

# Signs a single file with Azure Artifact Signing, then verifies the result. Aborts the
# whole run on any failure — an unsigned/half-signed artifact must never reach users.
function Invoke-SignFile($path) {
    if ($SkipSigning) { return }
    Write-Host "[release] Signing: $path"
    & $signtool sign /v /fd SHA256 /tr $timestampUrl /td SHA256 /dlib $signDlib /dmdf $signMeta $path
    if ($LASTEXITCODE -ne 0) { Fail "Signing failed for '$path' (signtool exit $LASTEXITCODE)." }
    & $signtool verify /pa /q $path
    if ($LASTEXITCODE -ne 0) { Fail "Signature verification failed for '$path' (signtool exit $LASTEXITCODE)." }
    Write-Host "[release] Signed + verified: $path"
}

# --- Step 1: verification build (Release|x64) ---
if (-not $SkipVerifyBuild) {
    Write-Host "[release] Step 1/5: Release build (verifying it compiles)..."
    & $msbuild $solution /t:Build /p:Configuration=Release /p:Platform=x64 /m /nologo /v:minimal
    if ($LASTEXITCODE -ne 0) { Fail "Release build failed (exit $LASTEXITCODE). Fix compile errors before releasing." }
    Write-Host "[release] Release build OK."
} else {
    Write-Host "[release] Step 1/5: skipped (-SkipVerifyBuild)."
}

# --- Step 2: publish via FolderProfile ---
Write-Host "[release] Step 2/5: Publishing (FolderProfile -> bin\publish)..."
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

# --- Step 3: sign the published app exe (before Inno packages it) ---
Write-Host "[release] Step 3/5: Signing the published app..."
Invoke-SignFile $publishedExe

# --- Step 4: build the installer with Inno Setup (which also signs it + the uninstaller) ---
Write-Host "[release] Step 4/5: Building installer with Inno Setup..."
$beforeIscc = Get-Date
& $iscc @isccSignArgs $issScript
if ($LASTEXITCODE -ne 0) { Fail "Inno Setup compile failed (exit $LASTEXITCODE)." }

# --- Step 5: locate the installer just built, verify its signature, extract its version ---
Write-Host "[release] Step 5/5: Locating installer, verifying signature, extracting version..."
if (-not (Test-Path $installerDir)) { Fail "Installer output folder not found: $installerDir" }

$installer = Get-ChildItem -Path $installerDir -Filter 'MarvinsAIRARefactored-Setup-*.exe' |
    Where-Object { $_.LastWriteTime -ge $beforeIscc.AddSeconds(-5) } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $installer) {
    Fail "No freshly built installer (MarvinsAIRARefactored-Setup-*.exe) found in $installerDir. Did ISCC actually write output there?"
}

# Confirm the installer Inno produced is actually signed (belt-and-suspenders over the
# in-Inno signing), unless signing was deliberately skipped.
if (-not $SkipSigning) {
    & $signtool verify /pa /q $installer.FullName
    if ($LASTEXITCODE -ne 0) { Fail "Installer was built but is NOT validly signed: $($installer.FullName)" }
    Write-Host "[release] Installer signature verified."
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
