#requires -Version 7
<#
.SYNOPSIS
    Generate a complete new culture .resx for MarvinsAIRA from a JSON translation map.

.DESCRIPTION
    Adding a whole new language is NOT what transform-resx.ps1 does (that one edits
    a single key across all files). This script clones the base Resources.resx
    structure byte-for-faithful (same indentation, schema header, xml:space) via
    XmlDocument with PreserveWhitespace, then swaps each <value> by key from a JSON
    map, and writes the result with a UTF-8 BOM by default (matching he-IL and the
    other non-Latin/RTL culture files).

    Workflow it implements (see SKILL.md "Add a new language"):
      1. Dump every base key+English value (do this first to build the JSON):
             [xml]$x = Get-Content -Raw -Encoding UTF8 <base>
             $x.root.data | % { '{0}`t{1}' -f $_.name, $_.value }
      2. Author a JSON map { "Key": "translated value", ... }. Omit keys you want
         left as the English passthrough (SI symbols Nm/Hz/g/m·s/%/°, RPM, POV, ms,
         brand names, ✓/✗ glyphs) — runtime falls back to base for any missing key.
         Include "ThisLanguage" = "Native (Country) - English (Country)".
      3. Run this script. It prints how many keys it translated, which it left as
         base English (verify that list is ONLY your intended keep-list), and any
         JSON keys that don't exist in the base (typos).
      4. Register the file in MarvinsAIRARefactored.csproj as
             <EmbeddedResource Include="Resources\Resources.<code>.resx"><WithCulture>false</WithCulture></EmbeddedResource>
         in alphabetical order (EnableDefaultEmbeddedResourceItems is false, so the
         file is invisible to the build until listed). No other code change is
         needed: Localization.Initialize() auto-discovers the language from its
         ThisLanguage key, and RTL is automatic via CultureInfo.TextInfo.IsRightToLeft.
      5. Build (VS-MSBuild Debug/x64 per CLAUDE.md), confirm exit 0.

.EXAMPLE
    pwsh -NoProfile -File ./new-language.ps1 -LanguageCode ar-SA -Json ./ar.json
#>
param(
    [Parameter(Mandatory)] [string]$LanguageCode,                 # e.g. ar-SA, ko-KR, zh-Hant
    [Parameter(Mandatory)] [string]$Json,                         # path to { "Key": "value" } map
    [string]$ResourcesDir = "$PSScriptRoot/../../../MarvinsAIRARefactored/Resources",
    [string]$BaseResx,                                            # defaults to <ResourcesDir>/Resources.resx
    [string]$OutResx,                                             # defaults to <ResourcesDir>/Resources.<code>.resx
    [switch]$NoBom                                                # write without a UTF-8 BOM (default: with BOM)
)

if ([string]::IsNullOrEmpty($BaseResx)) { $BaseResx = Join-Path $ResourcesDir 'Resources.resx' }
if ([string]::IsNullOrEmpty($OutResx))  { $OutResx  = Join-Path $ResourcesDir "Resources.$LanguageCode.resx" }

if (-not (Test-Path $BaseResx)) { throw "Base resx not found: $BaseResx" }
if (-not (Test-Path $Json))     { throw "JSON map not found: $Json" }

# Sanity-check the culture code resolves (so the auto-discovery regex + RTL detection work).
try { $ci = [System.Globalization.CultureInfo]::GetCultureInfo($LanguageCode) }
catch { throw "Unknown culture code '$LanguageCode'. Use a valid BCP-47 code such as ar-SA." }
Write-Output ("Culture: {0}  RTL: {1}" -f $ci.Name, $ci.TextInfo.IsRightToLeft)

# Load translations (JSON -> hashtable)
$obj = (Get-Content -Raw -Encoding UTF8 $Json) | ConvertFrom-Json
$tr = @{}
foreach ($p in $obj.PSObject.Properties) { $tr[$p.Name] = [string]$p.Value }

if (-not $tr.ContainsKey('ThisLanguage')) {
    Write-Warning "JSON has no 'ThisLanguage' key — the language will not appear in the in-app list until you add one."
}

# Clone base structure, swap values by key.
$doc = New-Object System.Xml.XmlDocument
$doc.PreserveWhitespace = $true
$doc.Load($BaseResx)

$applied = 0
$untranslated = [System.Collections.Generic.List[string]]::new()
foreach ($data in $doc.root.SelectNodes('data')) {
    $name = $data.GetAttribute('name')
    if ([string]::IsNullOrEmpty($name)) { continue }
    $valueNode = $data.SelectSingleNode('value')
    if ($null -eq $valueNode) { continue }
    if ($tr.ContainsKey($name)) { $valueNode.InnerText = $tr[$name]; $applied++ }
    else { $untranslated.Add($name) }
}

# JSON keys that don't exist in the base = typos worth catching.
$baseNames = @($doc.root.SelectNodes('data') | ForEach-Object { $_.GetAttribute('name') })
$missingInBase = @($tr.Keys | Where-Object { $baseNames -notcontains $_ })

# Write, preserving the chosen BOM convention.
$settings = New-Object System.Xml.XmlWriterSettings
$settings.Encoding = New-Object System.Text.UTF8Encoding(-not $NoBom)
$settings.OmitXmlDeclaration = $false
$writer = [System.Xml.XmlWriter]::Create($OutResx, $settings)
$doc.Save($writer); $writer.Flush(); $writer.Close()

Write-Output ("Wrote {0}" -f $OutResx)
Write-Output ("  translated      : {0}" -f $applied)
Write-Output ("  left as English : {0}  ({1})" -f $untranslated.Count, ($untranslated -join ', '))
Write-Output ("  JSON typos      : {0}  ({1})" -f $missingInBase.Count, ($missingInBase -join ', '))
if ($missingInBase.Count) { Write-Warning "Some JSON keys are not in the base resx (see above) — fix the JSON and re-run." }
