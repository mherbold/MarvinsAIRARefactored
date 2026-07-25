# Upserts localization keys across the resx files from per-language JSON maps: adds keys that are missing
# and REPLACES the value of keys that already exist with a different value (unlike the add-only pattern).
# Idempotent - a second run is a no-op. Preserves each file's BOM and line endings, validates XML before
# every write.
#
# JSON layout (one file per language in -JsonDir):
#   <prefix>.en.json      -> applied to the base Resources.resx
#   <prefix>.de-DE.json   -> applied to Resources.de-DE.resx     (one per culture, same key set as en)
#
# Usage:
#   pwsh -NoProfile -File upsert-resx-keys.ps1 -JsonDir <dir> -Prefix builtingraphdesc
#   pwsh -NoProfile -File upsert-resx-keys.ps1 -JsonDir <dir> -Prefix builtingraphdesc -Only en   # prove on base first

param(
	[Parameter( Mandatory )] [string] $JsonDir,
	[Parameter( Mandatory )] [string] $Prefix,
	[string] $Only = ""
)

if ( $PSVersionTable.PSVersion.Major -lt 7 )
{
	throw "This script requires PowerShell 7+ (pwsh) - Windows PowerShell corrupts UTF-8."
}

$repoRoot = ( git rev-parse --show-toplevel 2>$null )
if ( -not $repoRoot ) { throw "Not inside the git repository." }
$resourcesDir = Join-Path $repoRoot "MarvinsAIRARefactored\Resources"

function Escape-Xml( [string] $text )
{
	return $text.Replace( "&", "&amp;" ).Replace( "<", "&lt;" ).Replace( ">", "&gt;" )
}

$jsonFiles = Get-ChildItem -Path $JsonDir -Filter "$Prefix.*.json" | Sort-Object Name
if ( $Only -ne "" ) { $jsonFiles = $jsonFiles | Where-Object { $_.Name -eq "$Prefix.$Only.json" } }
if ( -not $jsonFiles ) { throw "No $Prefix.*.json files matched in $JsonDir." }

$totalAdded = 0
$totalReplaced = 0
$totalUnchanged = 0
$failures = @()

foreach ( $jsonFile in $jsonFiles )
{
	$code = $jsonFile.Name -replace "^$([regex]::Escape( $Prefix ))\.", '' -replace '\.json$', ''
	$resxPath = if ( $code -eq "en" ) { Join-Path $resourcesDir "Resources.resx" } else { Join-Path $resourcesDir "Resources.$code.resx" }

	if ( -not ( Test-Path $resxPath ) ) { $failures += "$code : missing resx $resxPath"; continue }

	$json = Get-Content -Raw -Encoding UTF8 $jsonFile.FullName | ConvertFrom-Json

	$bytes = [System.IO.File]::ReadAllBytes( $resxPath )
	$hasBom = ( $bytes.Length -ge 3 ) -and ( $bytes[ 0 ] -eq 0xEF ) -and ( $bytes[ 1 ] -eq 0xBB ) -and ( $bytes[ 2 ] -eq 0xBF )
	$text = [System.Text.Encoding]::UTF8.GetString( $bytes )
	if ( $hasBom ) { $text = $text.Substring( 1 ) }

	$eol = if ( $text.Contains( "`r`n" ) ) { "`r`n" } else { "`n" }

	$added = 0
	$replaced = 0
	$unchanged = 0
	$insertBlock = ""

	foreach ( $property in $json.PSObject.Properties )
	{
		$key = $property.Name
		$escaped = Escape-Xml ( [string] $property.Value )

		# match the whole existing data element for this key (value content is non-greedy across lines)
		$pattern = "(?s)(<data name=`"$([regex]::Escape( $key ))`" xml:space=`"preserve`">\s*<value>)(.*?)(</value>)"
		$match = [regex]::Match( $text, $pattern )

		if ( $match.Success )
		{
			if ( $match.Groups[ 2 ].Value -eq $escaped )
			{
				$unchanged++
			}
			else
			{
				$text = $text.Substring( 0, $match.Groups[ 2 ].Index ) + $escaped + $text.Substring( $match.Groups[ 2 ].Index + $match.Groups[ 2 ].Length )
				$replaced++
			}
		}
		else
		{
			$insertBlock += "  <data name=`"$key`" xml:space=`"preserve`">$eol    <value>$escaped</value>$eol  </data>$eol"
			$added++
		}
	}

	if ( $insertBlock -ne "" )
	{
		$rootIndex = $text.LastIndexOf( "</root>" )
		if ( $rootIndex -lt 0 ) { $failures += "$code : no </root> found"; continue }
		$text = $text.Substring( 0, $rootIndex ) + $insertBlock + $text.Substring( $rootIndex )
	}

	if ( ( $added -gt 0 ) -or ( $replaced -gt 0 ) )
	{
		try
		{
			$xmlDoc = [System.Xml.XmlDocument]::new()
			$xmlDoc.LoadXml( $text )
		}
		catch
		{
			$failures += "$code : XML validation failed - $($_.Exception.Message)"
			continue
		}

		[System.IO.File]::WriteAllText( $resxPath, $text, [System.Text.UTF8Encoding]::new( $hasBom ) )
	}

	$totalAdded += $added
	$totalReplaced += $replaced
	$totalUnchanged += $unchanged

	Write-Host ( "{0,-10} added={1,2} replaced={2,2} unchanged={3,2}" -f $code, $added, $replaced, $unchanged )
}

Write-Host "---"
Write-Host "total added=$totalAdded replaced=$totalReplaced unchanged=$totalUnchanged failures=$($failures.Count)"
$failures | ForEach-Object { Write-Host "FAILURE: $_" }
if ( $failures.Count -gt 0 ) { exit 1 }
