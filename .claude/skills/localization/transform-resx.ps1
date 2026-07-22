<#
================================================================================
  transform-resx.ps1  --  bulk, idempotent editor for MarvinsAIRA .resx files
================================================================================

  RUN WITH pwsh (PowerShell 7+) ONLY.  NEVER run under Windows PowerShell 5.1
  (powershell.exe) -- it corrupts UTF-8 and destroys the non-Latin translations
  (Armenian, Thai, Hebrew, Cyrillic, CJK, Welsh, ...). pwsh round-trips them.

  WHAT IT DOES
  ------------
  Applies ONE deterministic edit to a localization key across many
  Resources*.resx files in a single run, so you never loop the Edit tool
  file-by-file.

  It is format- and encoding-preserving:
    * BOM is detected per file and re-applied on write. The repo is MIXED:
      ~18 files have a UTF-8 BOM, ~10 (incl. the base Resources.resx) do not.
      Never normalize this -- preserve whatever each file already has.
    * Edits are surgical regex replacements on the exact <data>/<value> block,
      so surrounding indentation, comments, and unrelated keys are untouched.
      Some values contain embedded TABs (e.g. uk-UA) -- those are preserved.
    * CRLF line endings (the repo convention) are preserved.

  ELEMENT SHAPE (real localized strings):
      <data name="Key" xml:space="preserve">
        <value>the string</value>
      </data>

  OPERATIONS
  ----------
    Get      Print the <value> of -Key in each matched file.
    Set      Replace the <value> of -Key with -Value.
    Prepend  Prepend -Prefix to the <value> of -Key. IDEMPOTENT: skips a file
             whose value already starts with the prefix (second run = no-op).
    Rename   Change the name= attribute of -Key to -NewKey.
    Add      Append a new <data name="-Key" xml:space="preserve"> block with
             -Value before </root>. IDEMPOTENT: skips files already having -Key.
    Remove   Delete the whole <data> block for -Key. IDEMPOTENT.

  COMMON PARAMETERS
  -----------------
    -Path <glob|dir|file ...>  Targets. Default: the repo's Resources*.resx set.
                               Pass a single file to "prove on one" first.
    -Key <name>                The data name= to operate on (required except Get-all).
    -Value <string>            New value text (Set / Add). XML-escaped automatically
                               unless -NoEscape is given.
    -Prefix <string>           Text to prepend (Prepend).
    -NewKey <name>             New name (Rename).
    -WhatIf                    Dry run: print the would-be unified diff, write nothing.
    -NoEscape                  Treat -Value/-Prefix as already XML-escaped.
    -BaseOnly                  Restrict to the base Resources.resx only.

  EXAMPLES
  --------
    # Prove a value change on ONE file as a dry run, then fan out for real:
    pwsh transform-resx.ps1 -Operation Set -Key Wind -Value 'Typhoon Wind' `
         -Path ..\..\..\MarvinsAIRARefactored\Resources\Resources.de-DE.resx -WhatIf
    pwsh transform-resx.ps1 -Operation Set -Key Wind -Value 'Typhoon Wind'

    # Brand-prefix a caption everywhere, idempotently:
    pwsh transform-resx.ps1 -Operation Prepend -Key AppTitle -Prefix 'MAIRA '

    # Rename a key across all languages:
    pwsh transform-resx.ps1 -Operation Rename -Key OldName -NewKey NewName

    # Add a brand-new English string to the base file only (then translate):
    pwsh transform-resx.ps1 -Operation Add -Key Wind_Tooltip `
         -Value 'Adjusts wind fan strength' -BaseOnly

    # Read current value of a key in every language:
    pwsh transform-resx.ps1 -Operation Get -Key Wind

  Unusual one-offs may need a small bespoke tweak of the helper functions below
  rather than forcing the job through these fixed flags -- that is expected.
================================================================================
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Get', 'Set', 'Prepend', 'Rename', 'Add', 'Remove')]
    [string] $Operation,

    [string[]] $Path,
    [string]   $Key,
    [string]   $Value,
    [string]   $Prefix,
    [string]   $NewKey,
    [switch]   $WhatIf,
    [switch]   $NoEscape,
    [switch]   $BaseOnly
)

$ErrorActionPreference = 'Stop'

# Guard: refuse to run under Windows PowerShell 5.1.
if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "This script requires pwsh (PowerShell 7+). You are on $($PSVersionTable.PSVersion). " +
          "powershell.exe will corrupt the non-Latin translations -- aborting."
}

# ---------------------------------------------------------------------------
#  Encoding / IO helpers
# ---------------------------------------------------------------------------

function Test-Bom([string] $file) {
    $bytes = [System.IO.File]::ReadAllBytes($file)
    return ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
}

function Read-Text([string] $file) {
    # StreamReader strips a leading BOM automatically; we track it separately.
    return [System.IO.File]::ReadAllText($file, [System.Text.UTF8Encoding]::new($false))
}

function Write-Text([string] $file, [string] $text, [bool] $hasBom) {
    # Preserve the file's original BOM convention exactly.
    [System.IO.File]::WriteAllText($file, $text, [System.Text.UTF8Encoding]::new($hasBom))
}

function ConvertTo-XmlText([string] $s) {
    if ($null -eq $s) { return $s }
    return $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
}

# ---------------------------------------------------------------------------
#  Single-file string transforms (XML-aware, format-preserving)
#  Each returns the (possibly) modified text. They never touch disk.
# ---------------------------------------------------------------------------

function Get-ResxValue([string] $text, [string] $key) {
    $k = [regex]::Escape($key)
    $m = [regex]::Match($text, "(?s)<data\s+name=""$k""[^>]*>\s*<value>(?<val>.*?)</value>")
    if ($m.Success) { return $m.Groups['val'].Value } else { return $null }
}

function Set-ResxValue([string] $text, [string] $key, [string] $newVal) {
    $k = [regex]::Escape($key)
    $pattern = "(?s)(<data\s+name=""$k""[^>]*>\s*<value>)(.*?)(</value>)"
    return [regex]::Replace($text, $pattern, { param($m) $m.Groups[1].Value + $newVal + $m.Groups[3].Value })
}

function Add-ResxPrefix([string] $text, [string] $key, [string] $prefix) {
    $k = [regex]::Escape($key)
    $pattern = "(?s)(<data\s+name=""$k""[^>]*>\s*<value>)(.*?)(</value>)"
    return [regex]::Replace($text, $pattern, {
            param($m)
            $cur = $m.Groups[2].Value
            if ($cur.StartsWith($prefix)) { return $m.Value }   # idempotent: already prefixed
            return $m.Groups[1].Value + $prefix + $cur + $m.Groups[3].Value
        })
}

function Rename-ResxKey([string] $text, [string] $oldKey, [string] $newKey) {
    $k = [regex]::Escape($oldKey)
    $pattern = "(<data\s+name="")$k("")"
    return [regex]::Replace($text, $pattern, { param($m) $m.Groups[1].Value + $newKey + $m.Groups[2].Value })
}

function Remove-ResxKey([string] $text, [string] $key) {
    $k = [regex]::Escape($key)
    # Match the whole block plus its leading indentation and trailing newline.
    $pattern = "(?s)[ \t]*<data\s+name=""$k""[^>]*>.*?</data>\r?\n"
    return [regex]::Replace($text, $pattern, '', 1)
}

function Add-ResxKey([string] $text, [string] $key, [string] $newVal, [string] $nl) {
    $k = [regex]::Escape($key)
    if ([regex]::IsMatch($text, "<data\s+name=""$k""")) { return $text }   # idempotent: already present
    $block = "  <data name=""$key"" xml:space=""preserve"">$nl    <value>$newVal</value>$nl  </data>$nl"
    # anchor at the start of the </root> line so the preceding newline is not consumed --
    # the old "\s*</root>" pattern glued the new block onto the last </data> line
    return [regex]::Replace($text, "(?m)^([ \t]*</root>)", { param($m) $block + $m.Groups[1].Value }, 1)
}

# ---------------------------------------------------------------------------
#  Diff display (for -WhatIf)
# ---------------------------------------------------------------------------

function Show-Diff([string] $file, [string] $old, [string] $new) {
    if ($old -eq $new) {
        Write-Host "  (no change) $file" -ForegroundColor DarkGray
        return
    }
    $oldLines = $old -split "`r?`n"
    $newLines = $new -split "`r?`n"
    Write-Host "--- $file" -ForegroundColor Cyan
    $diff = Compare-Object -ReferenceObject $oldLines -DifferenceObject $newLines -SyncWindow 5
    foreach ($d in $diff) {
        if ($d.SideIndicator -eq '<=') { Write-Host "  - $($d.InputObject)" -ForegroundColor Red }
        else { Write-Host "  + $($d.InputObject)" -ForegroundColor Green }
    }
}

# ---------------------------------------------------------------------------
#  Resolve target files
# ---------------------------------------------------------------------------

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$defaultResourceDir = Resolve-Path (Join-Path $scriptDir '..\..\..\MarvinsAIRARefactored\Resources') -ErrorAction SilentlyContinue

$files = @()
if ($BaseOnly) {
    if (-not $defaultResourceDir) { throw "Could not locate the Resources directory for -BaseOnly." }
    $files = @((Join-Path $defaultResourceDir 'Resources.resx'))
}
elseif ($Path) {
    foreach ($p in $Path) {
        $resolved = Get-ChildItem -Path $p -ErrorAction SilentlyContinue
        if ($resolved) { $files += $resolved.FullName } else { $files += $p }
    }
}
else {
    if (-not $defaultResourceDir) { throw "Could not locate the default Resources directory; pass -Path." }
    $files = (Get-ChildItem -Path (Join-Path $defaultResourceDir 'Resources*.resx')).FullName
}

$files = $files | Where-Object { Test-Path $_ } | Sort-Object -Unique
if (-not $files) { throw "No .resx files matched the given target(s)." }

Write-Host "Operation: $Operation   Files: $($files.Count)   WhatIf: $([bool]$WhatIf)" -ForegroundColor Yellow

# ---------------------------------------------------------------------------
#  Apply
# ---------------------------------------------------------------------------

$changed = 0
$skipped = 0

foreach ($file in $files) {
    $hasBom = Test-Bom $file
    $old = Read-Text $file
    $nl = if ($old.Contains("`r`n")) { "`r`n" } else { "`n" }
    $new = $old

    switch ($Operation) {
        'Get' {
            $v = Get-ResxValue $old $Key
            if ($null -eq $v) { Write-Host ("{0,-32} <missing>" -f (Split-Path -Leaf $file)) -ForegroundColor DarkYellow }
            else { Write-Host ("{0,-32} {1}" -f (Split-Path -Leaf $file), $v) }
            continue
        }
        'Set' {
            if (-not $Key) { throw "-Key is required for Set." }
            $v = if ($NoEscape) { $Value } else { ConvertTo-XmlText $Value }
            $new = Set-ResxValue $old $Key $v
        }
        'Prepend' {
            if (-not $Key) { throw "-Key is required for Prepend." }
            $p = if ($NoEscape) { $Prefix } else { ConvertTo-XmlText $Prefix }
            $new = Add-ResxPrefix $old $Key $p
        }
        'Rename' {
            if (-not $Key -or -not $NewKey) { throw "-Key and -NewKey are required for Rename." }
            $new = Rename-ResxKey $old $Key $NewKey
        }
        'Add' {
            if (-not $Key) { throw "-Key is required for Add." }
            $v = if ($NoEscape) { $Value } else { ConvertTo-XmlText $Value }
            $new = Add-ResxKey $old $Key $v $nl
        }
        'Remove' {
            if (-not $Key) { throw "-Key is required for Remove." }
            $new = Remove-ResxKey $old $Key
        }
    }

    if ($new -eq $old) {
        $skipped++
        if ($WhatIf) { Write-Host "  (no change / idempotent skip) $(Split-Path -Leaf $file)" -ForegroundColor DarkGray }
        continue
    }

    if ($WhatIf) {
        Show-Diff (Split-Path -Leaf $file) $old $new
    }
    else {
        Write-Text $file $new $hasBom
        Write-Host "  updated $(Split-Path -Leaf $file)$(if($hasBom){' [BOM]'}else{''})" -ForegroundColor Green
    }
    $changed++
}

if ($Operation -ne 'Get') {
    Write-Host "Done. Changed: $changed   Skipped/unchanged: $skipped$(if($WhatIf){'   (dry run -- nothing written)'})" -ForegroundColor Yellow
}
