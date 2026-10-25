#requires -Version 7
<#
.SYNOPSIS
  Posts one approved reply to the MAIRA Discord as the "MAIRA Support" bot.

.DESCRIPTION
  Works for text channels, threads and forum posts (a thread / forum post is its own channel id).
  - -ReplyToMessageId makes it a real Discord reply (quoted + pings that author unless -NoReplyPing).
  - -PingUserIds lets <@id> mentions in the text actually ping those users; nothing else can ping
    (no @everyone / @here / roles, whatever the text says).
  - Text over 2000 characters is split at paragraph / line breaks; only the first part is the reply.
  Read the text from a UTF-8 file (-ContentFile) so quoting and non-English characters survive.
  Use -DryRun to see exactly what would be sent without sending it.

.EXAMPLE
  pwsh -File post-discord-reply.ps1 -ChannelId 1379528081458466938 -ReplyToMessageId 1558240813987799152 -ContentFile reply-C1-1.txt
#>
[CmdletBinding()]
param(
	[Parameter( Mandatory )] [string] $ChannelId,
	[string] $ContentFile,
	[string] $Content,
	[string] $ReplyToMessageId,
	[string[]] $PingUserIds = @(),
	[switch] $NoReplyPing,
	[string] $GuildId = '1353509415571165204',
	[switch] $DryRun
)

$ErrorActionPreference = 'Stop'

if ( $ContentFile ) { $Content = Get-Content -Path $ContentFile -Raw -Encoding utf8 }
if ( -not $Content -or -not $Content.Trim() ) { throw 'Nothing to post: pass -ContentFile or -Content.' }
$Content = $Content.Trim() -replace "`r`n", "`n"

$token = [Environment]::GetEnvironmentVariable( 'DISCORD_TOKEN', 'User' )
if ( -not $token ) { $token = $env:DISCORD_TOKEN }
if ( -not $token -and -not $DryRun ) { throw 'DISCORD_TOKEN is not set (expected as a Windows user environment variable).' }

$requestHeaders = @{ Authorization = "Bot $token"; 'User-Agent' = 'DiscordBot (https://mairapp.com, 1.0)' }
$maxChunkLength = 1950

# Split at the last paragraph break, else line break, else space, before the limit.
$chunks = [System.Collections.Generic.List[string]]::new()
$remainingText = $Content
while ( $remainingText.Length -gt $maxChunkLength )
{
	$window = $remainingText.Substring( 0, $maxChunkLength )
	$splitAt = $window.LastIndexOf( "`n`n" )
	if ( $splitAt -lt $maxChunkLength / 2 ) { $splitAt = $window.LastIndexOf( "`n" ) }
	if ( $splitAt -lt $maxChunkLength / 2 ) { $splitAt = $window.LastIndexOf( ' ' ) }
	if ( $splitAt -lt 1 ) { $splitAt = $maxChunkLength }
	$chunks.Add( $remainingText.Substring( 0, $splitAt ).TrimEnd() )
	$remainingText = $remainingText.Substring( $splitAt ).TrimStart()
}
if ( $remainingText ) { $chunks.Add( $remainingText ) }

for ( $chunkIndex = 0; $chunkIndex -lt $chunks.Count; $chunkIndex++ )
{
	$isFirstChunk = $chunkIndex -eq 0
	# Typed arrays so ConvertTo-Json always emits JSON lists (a bare one-element array would become a string).
	[string[]] $usersToPing = @( if ( $isFirstChunk ) { $PingUserIds } )
	$payload = [ordered] @{
		content          = $chunks[ $chunkIndex ]
		allowed_mentions = [ordered] @{
			parse        = [string[]] @()
			users        = $usersToPing
			replied_user = [bool] ( $isFirstChunk -and $ReplyToMessageId -and -not $NoReplyPing )
		}
	}
	if ( $isFirstChunk -and $ReplyToMessageId )
	{
		$payload.message_reference = [ordered] @{
			message_id         = $ReplyToMessageId
			channel_id         = $ChannelId
			guild_id           = $GuildId
			fail_if_not_exists = $false
		}
	}

	$payloadJson = $payload | ConvertTo-Json -Depth 6

	if ( $DryRun )
	{
		Write-Output "----- DRY RUN: part $( $chunkIndex + 1 ) of $( $chunks.Count ) ($( $chunks[ $chunkIndex ].Length ) chars) -> channel $ChannelId -----"
		Write-Output $payloadJson
		continue
	}

	for ( $attempt = 1; $attempt -le 6; $attempt++ )
	{
		$webResponse = Invoke-WebRequest -Method Post -Uri "https://discord.com/api/v10/channels/$ChannelId/messages" -Headers $requestHeaders `
			-ContentType 'application/json; charset=utf-8' -Body ( [System.Text.Encoding]::UTF8.GetBytes( $payloadJson ) ) -SkipHttpErrorCheck
		$bodyText = [System.Text.Encoding]::UTF8.GetString( $webResponse.RawContentStream.ToArray() )
		$parsedBody = if ( $bodyText ) { $bodyText | ConvertFrom-Json -Depth 32 } else { $null }

		if ( $webResponse.StatusCode -eq 429 )
		{
			$retryAfterSeconds = if ( $parsedBody -and $parsedBody.retry_after ) { [double] $parsedBody.retry_after } else { 1.0 }
			Start-Sleep -Milliseconds ( [int] ( ( $retryAfterSeconds + 0.25 ) * 1000 ) )
			continue
		}
		if ( $webResponse.StatusCode -ge 300 )
		{
			throw "Discord returned $( $webResponse.StatusCode ) for part $( $chunkIndex + 1 ): $bodyText"
		}
		Write-Output "Posted part $( $chunkIndex + 1 ) of $( $chunks.Count ): https://discord.com/channels/$GuildId/$ChannelId/$( $parsedBody.id )"
		break
	}
}
