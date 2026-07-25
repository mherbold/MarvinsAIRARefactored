# update-donate-page.ps1
#
# Regenerates the donor name lists on the MAIRA donate page (Pages/DonatePage.xaml)
# from the two buymeacoffee.com CSV exports.
#
# Tiers (exclusive - each person appears only in their highest tier):
#   MEGA DONORS  - lifetime total >= $100
#   SUPER DONORS - lifetime total >= $30 and < $100
#   DONORS       - lifetime total < $30
#
# Monthly supporters are credited months-paid x monthly-amount (anniversaries of the
# start date through the cancellation date, or through -AsOfDate while still active).
#
# The script only rewrites the XAML between these marker comments, so it is idempotent:
#   <!-- BEGIN GENERATED: MEGA DONORS -->  ...  <!-- END GENERATED: MEGA DONORS -->
#   <!-- BEGIN GENERATED: SUPER DONORS --> ...  <!-- END GENERATED: SUPER DONORS -->
#   <!-- BEGIN GENERATED: DONORS -->       ...  <!-- END GENERATED: DONORS -->
#
# Usage (run with pwsh, never Windows PowerShell 5.1):
#   pwsh -File update-donate-page.ps1                # newest CSVs in ~/Downloads, write XAML
#   pwsh -File update-donate-page.ps1 -DryRun        # print the lists, do not touch the XAML
#   pwsh -File update-donate-page.ps1 -ManualCsv x.csv -MonthlyCsv y.csv

param(
	[string]$MonthlyCsv,   # Marvinherbold_*.csv - monthly subscription supporters
	[string]$ManualCsv,    # Supporters_list_*.csv - one-off donations
	[string]$XamlPath,
	[datetime]$AsOfDate = (Get-Date).Date,
	[decimal]$MegaThreshold = 100,
	[decimal]$SuperThreshold = 30,
	[switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$culture = [System.Globalization.CultureInfo]::InvariantCulture

# --- Locate inputs ---------------------------------------------------------

$downloads = Join-Path $env:USERPROFILE 'Downloads'

if ( -not $MonthlyCsv )
{
	$MonthlyCsv = ( Get-ChildItem ( Join-Path $downloads 'Marvinherbold_*.csv' ) | Sort-Object LastWriteTime -Descending | Select-Object -First 1 ).FullName
}

if ( -not $ManualCsv )
{
	$ManualCsv = ( Get-ChildItem ( Join-Path $downloads 'Supporters_list_*.csv' ) | Sort-Object LastWriteTime -Descending | Select-Object -First 1 ).FullName
}

if ( -not $MonthlyCsv -or -not $ManualCsv )
{
	throw "Could not find the buymeacoffee CSV exports. Expected Marvinherbold_*.csv and Supporters_list_*.csv in $downloads (or pass -MonthlyCsv / -ManualCsv)."
}

if ( -not $XamlPath )
{
	# script lives at [repo]/.claude/skills/update-donate-page/scripts/
	$repoRoot = Resolve-Path ( Join-Path $PSScriptRoot '..\..\..\..' )
	$XamlPath = Join-Path $repoRoot 'MarvinsAIRARefactored\Pages\DonatePage.xaml'
}

Write-Host "Monthly CSV : $MonthlyCsv"
Write-Host "Manual CSV  : $ManualCsv"
Write-Host "Donate page : $XamlPath"
Write-Host "As-of date  : $( $AsOfDate.ToString( 'yyyy-MM-dd' ) )"
Write-Host ''

# --- Aggregate totals per supporter email ----------------------------------

# email -> @{ Total = [decimal]; Names = hashtable( rawName -> count ) }
$supporters = @{}
$warnings = [System.Collections.Generic.List[string]]::new()
$anonymousCount = 0
[decimal]$anonymousTotal = 0

function Normalize-Email( [string]$email )
{
	$email = $email.Trim().ToLowerInvariant()

	# deleted accounts export as "user@host.com_is_deleted_1234567"
	return $email -replace '_is_deleted_\d+$', ''
}

function Add-Contribution( [string]$email, [string]$name, [decimal]$amount )
{
	$email = Normalize-Email $email

	if ( -not $script:supporters.ContainsKey( $email ) )
	{
		$script:supporters[ $email ] = @{ Total = [decimal]0; Names = @{} }
	}

	$entry = $script:supporters[ $email ]
	$entry.Total += $amount

	$name = $name.Trim()

	if ( $name -and ( $name -ne 'Someone' ) )
	{
		$entry.Names[ $name ] = 1 + [int]$entry.Names[ $name ]
	}
}

# manual (one-off) donations
$manualRows = Import-Csv $ManualCsv

foreach ( $row in $manualRows )
{
	$amount = [decimal]::Parse( $row.'Coffee Count', $culture ) * [decimal]::Parse( $row.'Coffee Price', $culture )

	if ( $row.'Support Currency' -ne 'USD' )
	{
		$warnings.Add( "Non-USD manual donation from $( $row.'Supporter Name' ) ($( $row.'Support Currency' )) - counted at face value." )
	}

	Add-Contribution $row.'Supporter Email' $row.'Supporter Name' $amount
}

# monthly subscriptions - credit one payment per monthly anniversary of the start
# date, up to the cancellation date (inclusive) or the as-of date while active
$monthlyRows = Import-Csv $MonthlyCsv
$monthlyFolded = 0

foreach ( $row in $monthlyRows )
{
	$monthlyAmount = [decimal]::Parse( $row.Amount, $culture )
	$startDate = ( [datetime]::Parse( $row.'Start date', $culture ) ).Date

	$cutoff = $AsOfDate

	if ( $row.'Subscription cancelled on' )
	{
		$cutoff = ( [datetime]::Parse( $row.'Subscription cancelled on', $culture ) ).Date
	}

	if ( $row.Paused -and ( $row.Paused -ne 'No' ) )
	{
		$warnings.Add( "Monthly supporter $( $row.'Supporter Name' ) is paused - pause date is not in the export, so all months through $( $cutoff.ToString( 'yyyy-MM-dd' ) ) were counted." )
	}

	$payments = 0

	for ( $chargeDate = $startDate; $chargeDate -le $cutoff; $chargeDate = $chargeDate.AddMonths( 1 ) )
	{
		$payments++
	}

	if ( $payments -gt 0 )
	{
		Add-Contribution $row.'Supporter Email' $row.'Supporter Name' ( $payments * $monthlyAmount )
		$monthlyFolded++
	}

	if ( $row.'Support Currency' -and ( $row.'Support Currency' -ne 'USD' ) )
	{
		$warnings.Add( "Non-USD monthly supporter $( $row.'Supporter Name' ) - counted at face value." )
	}
	elseif ( $row.Currency -and ( $row.Currency -ne 'USD' ) )
	{
		$warnings.Add( "Non-USD monthly supporter $( $row.'Supporter Name' ) - counted at face value." )
	}
}

# --- Out-of-band donations --------------------------------------------------
# extra-donations.csv (in the skill folder) records money received outside
# buymeacoffee.com. Rows with an Email join the normal per-email aggregation
# (so a later buymeacoffee donation from the same address combines, whatever
# name it arrives under); rows without one are merged by display name below.

$extraCsv = Join-Path $PSScriptRoot '..\extra-donations.csv'
$extraCount = 0
$extraByName = [System.Collections.Generic.List[object]]::new()

if ( Test-Path $extraCsv )
{
	foreach ( $row in ( Import-Csv $extraCsv ) )
	{
		$amount = [decimal]::Parse( $row.Amount, $culture )

		if ( $row.Email )
		{
			Add-Contribution $row.Email $row.Name.Trim() $amount
		}
		else
		{
			$extraByName.Add( @{ Name = $row.Name.Trim(); Amount = $amount } )
		}

		$extraCount++
	}
}

# --- Pick and scrub display names ------------------------------------------

function Scrub-Name( [string]$name )
{
	# CSV exports HTML-encode accents ("Ren&eacute; Bo" -> "René Bo")
	$name = [System.Net.WebUtility]::HtmlDecode( $name ).Trim()

	# URLs become their @handle ("https://www.youtube.com/@minimotojon4605" -> "@minimotojon4605")
	if ( $name -match '^(https?://|www\.)' )
	{
		if ( $name -match '(@[A-Za-z0-9._-]+)' )
		{
			return $Matches[ 1 ]
		}

		$name = ( $name -replace '^https?://', '' -replace '^www\.', '' ).TrimEnd( '/' )

		return ( $name -split '/' )[ -1 ]
	}

	$tokens = [System.Collections.Generic.List[string]]::new()

	foreach ( $token in ( $name -split '\s+' ) )
	{
		# scrub email addresses and site handles to the part before the @
		# ("mcalbols@gmail.com" -> "mcalbols", "Mattyice6723@twitchTV" -> "Mattyice6723")
		# but keep leading-@ social handles ("@bretsalmon") intact
		if ( ( $token.IndexOf( '@' ) -gt 0 ) )
		{
			$token = $token.Substring( 0, $token.IndexOf( '@' ) )
		}

		# drop long account-number junk ("bobbybueshea4501 863527475005620255")
		if ( $token -match '^\d{10,}$' )
		{
			continue
		}

		if ( $token )
		{
			$tokens.Add( $token )
		}
	}

	# drop a trailing @handle when a real name precedes it
	# ("Jean Paul Vieira @jeanpaulvieira" -> "Jean Paul Vieira")
	if ( ( $tokens.Count -gt 1 ) -and ( $tokens[ -1 ] -match '^@.+' ) -and ( $tokens[ 0 ] -notmatch '^@' ) )
	{
		$tokens.RemoveAt( $tokens.Count - 1 )
	}

	return ( $tokens -join ' ' ).Trim()
}

# display name -> total (people who used two emails but the same name are merged)
$byName = @{}
$scrubbedForReview = [System.Collections.Generic.List[string]]::new()

foreach ( $email in $supporters.Keys )
{
	$entry = $supporters[ $email ]

	if ( $entry.Names.Count -eq 0 )
	{
		$anonymousCount++
		$anonymousTotal += $entry.Total
		continue
	}

	$rawName = ( $entry.Names.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1 ).Key
	$displayName = Scrub-Name $rawName

	if ( -not $displayName -or ( $displayName -ieq 'someone' ) )
	{
		$anonymousCount++
		$anonymousTotal += $entry.Total
		continue
	}

	if ( $displayName -cne $rawName )
	{
		$scrubbedForReview.Add( "'$rawName' -> '$displayName'" )
	}

	$byName[ $displayName ] = $entry.Total + [decimal]$( if ( $byName.ContainsKey( $displayName ) ) { $byName[ $displayName ] } else { 0 } )
}

# out-of-band rows that had no email are merged by display name here
foreach ( $extra in $extraByName )
{
	$byName[ $extra.Name ] = $extra.Amount + [decimal]$( if ( $byName.ContainsKey( $extra.Name ) ) { $byName[ $extra.Name ] } else { 0 } )
}

# --- Split into tiers and sort ---------------------------------------------

function Sort-Names( $names )
{
	# alphabetize ignoring leading @/punctuation so "@lucciano.netto" files under L
	return $names | Sort-Object { $_.TrimStart( '@', ' ', '.', '_' ).ToLowerInvariant() }
}

$megaNames = Sort-Names ( $byName.Keys | Where-Object { $byName[ $_ ] -ge $MegaThreshold } )
$superNames = Sort-Names ( $byName.Keys | Where-Object { ( $byName[ $_ ] -ge $SuperThreshold ) -and ( $byName[ $_ ] -lt $MegaThreshold ) } )
$donorNames = Sort-Names ( $byName.Keys | Where-Object { $byName[ $_ ] -lt $SuperThreshold } )

# --- Rewrite the XAML between the generated markers ------------------------

function Escape-Xml( [string]$text )
{
	return $text.Replace( '&', '&amp;' ).Replace( '<', '&lt;' ).Replace( '>', '&gt;' ).Replace( '"', '&quot;' )
}

function Replace-Section( [string]$xaml, [string]$sectionName, $names )
{
	$begin = "<!-- BEGIN GENERATED: $sectionName -->"
	$end = "<!-- END GENERATED: $sectionName -->"

	$pattern = "(?s)([ \t]*)$( [regex]::Escape( $begin ) ).*?$( [regex]::Escape( $end ) )"

	if ( $xaml -notmatch $pattern )
	{
		throw "Marker comments for section '$sectionName' not found in the donate page XAML."
	}

	$indent = [regex]::Match( $xaml, $pattern ).Groups[ 1 ].Value

	$lines = [System.Collections.Generic.List[string]]::new()
	$lines.Add( "$indent$begin" )

	foreach ( $name in $names )
	{
		$lines.Add( "$indent<TextBlock Text=`"$( Escape-Xml $name )`" />" )
	}

	$lines.Add( "$indent$end" )

	$replacement = ( $lines -join "`r`n" ).Replace( '$', '$$' )

	return [regex]::Replace( $xaml, $pattern, $replacement )
}

$xamlText = [System.IO.File]::ReadAllText( $XamlPath )

$xamlText = Replace-Section $xamlText 'MEGA DONORS' $megaNames
$xamlText = Replace-Section $xamlText 'SUPER DONORS' $superNames
$xamlText = Replace-Section $xamlText 'DONORS' $donorNames

if ( -not $DryRun )
{
	# preserve the file's UTF-8 BOM
	$utf8Bom = [System.Text.UTF8Encoding]::new( $true )
	[System.IO.File]::WriteAllText( $XamlPath, $xamlText, $utf8Bom )
}

# --- Summary ---------------------------------------------------------------

Write-Host "MEGA DONORS  (>= `$$MegaThreshold)          : $( @( $megaNames ).Count )"
Write-Host "SUPER DONORS (`$$SuperThreshold - `$$( $MegaThreshold - 0.01 )) : $( @( $superNames ).Count )"
Write-Host "DONORS       (< `$$SuperThreshold)          : $( @( $donorNames ).Count )"
Write-Host "Anonymous supporters skipped     : $anonymousCount (`$$anonymousTotal)"
Write-Host "Monthly supporters folded in     : $monthlyFolded of $( @( $monthlyRows ).Count )"
Write-Host "Out-of-band donations folded in  : $extraCount (extra-donations.csv)"
Write-Host ''

if ( $scrubbedForReview.Count -gt 0 )
{
	Write-Host 'Scrubbed names (eyeball these):'
	$scrubbedForReview | Sort-Object | ForEach-Object { Write-Host "  $_" }
	Write-Host ''
}

if ( $warnings.Count -gt 0 )
{
	Write-Host 'Warnings:'
	$warnings | ForEach-Object { Write-Host "  $_" }
	Write-Host ''
}

if ( $DryRun )
{
	Write-Host '-DryRun: the donate page was NOT modified. Generated lists:'
	Write-Host ''
	Write-Host "MEGA DONORS: $( $megaNames -join ', ' )"
	Write-Host ''
	Write-Host "SUPER DONORS: $( $superNames -join ', ' )"
	Write-Host ''
	Write-Host "DONORS: $( $donorNames -join ', ' )"
}
else
{
	Write-Host 'Donate page updated.'
}
