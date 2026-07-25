# Normalizes the identity of every shipped built-in graph file in MarvinsAIRARefactored/BuiltInGraphs:
#   <Name>      -> the file name stem (the maintainer updates a built-in by saving a differently-named
#                  clone over the shipped file, so the clone's name leaks into the file)
#   <GraphId>   -> the id from the last committed version of the same file (clones mint a fresh id; the
#                  shipped id must stay stable so testers' per-context values and import matching survive)
#   <IsBuiltIn> -> true (exports from clones say false; the loader forces true anyway, but keep the file honest)
# Idempotent - a second run reports no changes. Only the graph-level header is touched; modules,
# descriptions, and settings are left exactly as saved.

if ( $PSVersionTable.PSVersion.Major -lt 7 )
{
	throw "This script requires PowerShell 7+ (pwsh)."
}

$repoRoot = ( git rev-parse --show-toplevel 2>$null )

if ( -not $repoRoot )
{
	throw "Not inside the git repository."
}

$graphsDir = Join-Path $repoRoot "MarvinsAIRARefactored\BuiltInGraphs"
$changes = 0
$seenIds = @{}

foreach ( $file in Get-ChildItem -Path $graphsDir -Filter "*.mairagraph" | Sort-Object Name )
{
	$stem = [System.IO.Path]::GetFileNameWithoutExtension( $file.Name )

	$bytes = [System.IO.File]::ReadAllBytes( $file.FullName )
	$hasBom = ( $bytes.Length -ge 3 ) -and ( $bytes[ 0 ] -eq 0xEF ) -and ( $bytes[ 1 ] -eq 0xBB ) -and ( $bytes[ 2 ] -eq 0xBF )
	$text = [System.Text.Encoding]::UTF8.GetString( $bytes )
	if ( $hasBom ) { $text = $text.Substring( 1 ) }

	$original = $text

	# the graph-level header fields all appear before the first <Modules> element - restrict edits there
	# so a module named/killed field can never be touched
	$modulesIndex = $text.IndexOf( "<Modules>" )
	if ( $modulesIndex -lt 0 ) { Write-Host "SKIP (no <Modules>): $($file.Name)"; continue }
	$header = $text.Substring( 0, $modulesIndex )

	# 1. Name = file name stem. Escape only & < > — the app's serializer leaves apostrophes/quotes literal
	# in element content, and matching its output exactly keeps future exports from churning the line.
	$escapedStem = $stem.Replace( "&", "&amp;" ).Replace( "<", "&lt;" ).Replace( ">", "&gt;" )
	$header = [regex]::Replace( $header, "<Name>.*?</Name>", "<Name>$escapedStem</Name>" )

	# 2. GraphId = the id from the last committed version of this file (falls back to keeping the current
	# id when the file is brand-new to git)
	$relPath = [System.IO.Path]::GetRelativePath( $repoRoot, $file.FullName ).Replace( "\", "/" )
	$headContent = git -C $repoRoot show "HEAD:$relPath" 2>$null
	if ( $LASTEXITCODE -eq 0 -and $headContent )
	{
		$headMatch = [regex]::Match( ( $headContent -join "`n" ), "<GraphId>([0-9a-f]{32})</GraphId>" )
		if ( $headMatch.Success )
		{
			$canonicalId = $headMatch.Groups[ 1 ].Value
			$header = [regex]::Replace( $header, "<GraphId>[0-9a-f]{32}</GraphId>", "<GraphId>$canonicalId</GraphId>" )
		}
	}

	# 3. IsBuiltIn = true
	$header = $header.Replace( "<IsBuiltIn>false</IsBuiltIn>", "<IsBuiltIn>true</IsBuiltIn>" )

	$text = $header + $text.Substring( $modulesIndex )

	# uniqueness check across the shipped set
	$idMatch = [regex]::Match( $header, "<GraphId>([0-9a-f]{32})</GraphId>" )
	if ( $idMatch.Success )
	{
		$id = $idMatch.Groups[ 1 ].Value
		if ( $seenIds.ContainsKey( $id ) ) { throw "DUPLICATE GraphId $id in '$($file.Name)' and '$($seenIds[ $id ])'" }
		$seenIds[ $id ] = $file.Name
	}

	if ( $text -ne $original )
	{
		# validate before writing
		$xmlDoc = [System.Xml.XmlDocument]::new()
		$xmlDoc.LoadXml( $text )

		[System.IO.File]::WriteAllText( $file.FullName, $text, [System.Text.UTF8Encoding]::new( $hasBom ) )
		$changes++
		Write-Host "FIXED: $($file.Name)"
	}
	else
	{
		Write-Host "ok:    $($file.Name)"
	}
}

Write-Host "---"
Write-Host "files changed: $changes"
