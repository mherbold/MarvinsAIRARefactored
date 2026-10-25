#requires -Version 7
<#
.SYNOPSIS
  Pulls recent activity from the MAIRA Discord server into a digest for support triage.

.DESCRIPTION
  Read-only. Walks every text / announcement channel and every forum or channel thread
  (active and archived) that had activity in the last -Days days, and writes:

    <OutDir>\digest.md       - index table + chronological transcript of every conversation
    <OutDir>\activity.json   - the same data, structured
    <OutDir>\attachments\    - images and text files (logs, configs) attached in the window

  Uses the bot token in the user environment variable DISCORD_TOKEN. The token is never printed.
  Staff = the guild owner (Marvin) + the bot itself + any -StaffUserIds.

.EXAMPLE
  pwsh -File fetch-discord-activity.ps1 -OutDir C:\temp\discord-2026-10-25 -Days 14
#>
[CmdletBinding()]
param(
	[Parameter( Mandatory )] [string] $OutDir,
	[int] $Days = 14,
	[string] $GuildId = '1353509415571165204',
	[int] $ContextDays = 3,
	[int] $MaxThreadMessages = 300,
	[int] $MaxChannelMessages = 2000,
	[string[]] $StaffUserIds = @(),
	[string[]] $SkipChannelNames = @(),
	[switch] $NoAttachments,
	[double] $MaxAttachmentMB = 8
)

$ErrorActionPreference = 'Stop'

$token = [Environment]::GetEnvironmentVariable( 'DISCORD_TOKEN', 'User' )
if ( -not $token ) { $token = $env:DISCORD_TOKEN }
if ( -not $token ) { throw 'DISCORD_TOKEN is not set (expected as a Windows user environment variable).' }

$apiBase = 'https://discord.com/api/v10'
# Discord rejects requests without a DiscordBot-style User-Agent (error 40333).
$requestHeaders = @{ Authorization = "Bot $token"; 'User-Agent' = 'DiscordBot (https://mairapp.com, 1.0)' }
$discordEpochMs = 1420070400000
$script:apiErrors = [System.Collections.Generic.List[string]]::new()
$script:hiddenChannels = [System.Collections.Generic.SortedSet[string]]::new()
$channelsById = $null

# Returns @{ Ok; Status; Data }. Body is decoded as UTF-8 explicitly (Discord omits the charset).
function Invoke-Discord( [string] $path )
{
	for ( $attempt = 1; $attempt -le 8; $attempt++ )
	{
		$webResponse = Invoke-WebRequest -Uri "$apiBase$path" -Headers $requestHeaders -SkipHttpErrorCheck
		$bodyText = [System.Text.Encoding]::UTF8.GetString( $webResponse.RawContentStream.ToArray() )
		$parsedBody = if ( $bodyText ) { $bodyText | ConvertFrom-Json -Depth 64 -NoEnumerate -DateKind String } else { $null }

		if ( $webResponse.StatusCode -eq 429 )
		{
			$retryAfterSeconds = if ( $parsedBody -and $parsedBody.retry_after ) { [double] $parsedBody.retry_after } else { 1.0 }
			Start-Sleep -Milliseconds ( [int] ( ( $retryAfterSeconds + 0.25 ) * 1000 ) )
			continue
		}

		$isOk = $webResponse.StatusCode -ge 200 -and $webResponse.StatusCode -lt 300
		if ( -not $isOk )
		{
			$errorMessage = if ( $parsedBody -and $parsedBody.message ) { $parsedBody.message } else { '' }
			$channelMatch = [regex]::Match( $path, '^/channels/(\d+)' )
			if ( $webResponse.StatusCode -eq 403 -and $channelMatch.Success -and $channelsById -and $channelsById[ $channelMatch.Groups[1].Value ] )
			{
				[void] $script:hiddenChannels.Add( "#$( $channelsById[ $channelMatch.Groups[1].Value ].name )" )
			}
			else
			{
				$script:apiErrors.Add( "$( $webResponse.StatusCode ) $path $errorMessage" )
			}
		}
		return [pscustomobject] @{ Ok = $isOk; Status = [int] $webResponse.StatusCode; Data = $parsedBody }
	}
	throw "Rate limited too many times on $path"
}

function Get-SnowflakeTime( [string] $snowflakeId )
{
	$milliseconds = [long] ( [UInt64] $snowflakeId -shr 22 ) + $discordEpochMs
	return [DateTimeOffset]::FromUnixTimeMilliseconds( $milliseconds )
}

# Newest-first pagination, stopping at messages older than $stopBefore. Returns a List (oldest first).
function Get-ChannelMessages( [string] $channelId, [DateTimeOffset] $stopBefore, [int] $maxMessages, [ref] $wasTruncated )
{
	$collectedMessages = [System.Collections.Generic.List[object]]::new()
	$beforeId = $null
	$wasTruncated.Value = $false

	while ( $true )
	{
		$query = "/channels/$channelId/messages?limit=100"
		if ( $beforeId ) { $query += "&before=$beforeId" }

		$result = Invoke-Discord $query
		if ( -not $result.Ok ) { return $null }

		$batch = @( $result.Data )
		if ( $batch.Count -eq 0 ) { break }

		$reachedStop = $false
		foreach ( $message in $batch )
		{
			if ( ( Get-SnowflakeTime $message.id ) -lt $stopBefore ) { $reachedStop = $true; break }
			if ( $collectedMessages.Count -ge $maxMessages ) { $wasTruncated.Value = $true; $reachedStop = $true; break }
			$collectedMessages.Add( $message )
		}

		if ( $reachedStop -or $batch.Count -lt 100 ) { break }
		$beforeId = $batch[-1].id
	}

	$collectedMessages.Reverse()
	return , $collectedMessages
}

function Get-SafeFileName( [string] $fileName )
{
	$invalidChars = [System.IO.Path]::GetInvalidFileNameChars()
	return ( -join ( $fileName.ToCharArray() | ForEach-Object { if ( $invalidChars -contains $_ ) { '_' } else { $_ } } ) )
}

# ---------------------------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------------------------

$nowTime = [DateTimeOffset]::UtcNow
$windowStart = $nowTime.AddDays( -$Days )
$contextStart = $windowStart.AddDays( -$ContextDays )

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$attachmentsDir = Join-Path $OutDir 'attachments'
if ( -not $NoAttachments ) { New-Item -ItemType Directory -Force -Path $attachmentsDir | Out-Null }

$botUser = ( Invoke-Discord '/users/@me' ).Data
$guild = ( Invoke-Discord "/guilds/$GuildId" ).Data
if ( -not $guild ) { throw "Could not read guild $GuildId (is the bot still in the server?)." }

$staffIds = [System.Collections.Generic.HashSet[string]]::new()
[void] $staffIds.Add( [string] $guild.owner_id )
[void] $staffIds.Add( [string] $botUser.id )
foreach ( $staffUserId in $StaffUserIds ) { [void] $staffIds.Add( $staffUserId ) }

$channelList = @( ( Invoke-Discord "/guilds/$GuildId/channels" ).Data )
$channelsById = @{}
foreach ( $channel in $channelList ) { $channelsById[ [string] $channel.id ] = $channel }

# Forum tag id -> name, for showing tags like "Solved" on forum posts.
$tagNamesById = @{}
foreach ( $channel in $channelList )
{
	foreach ( $availableTag in @( $channel.available_tags ) )
	{
		if ( $availableTag ) { $tagNamesById[ [string] $availableTag.id ] = $availableTag.name }
	}
}

$readableChannelTypes = @( 0, 5 )       # text, announcement
$threadParentTypes = @( 0, 5, 15, 16 )  # text, announcement, forum, media
$skipNames = @( $SkipChannelNames | ForEach-Object { $_.ToLowerInvariant() } )

# ---------------------------------------------------------------------------------------------
# Discover threads with activity in the window
# ---------------------------------------------------------------------------------------------

$threadsById = @{}

$activeThreads = Invoke-Discord "/guilds/$GuildId/threads/active"
if ( $activeThreads.Ok ) { foreach ( $thread in @( $activeThreads.Data.threads ) ) { if ( $thread ) { $threadsById[ [string] $thread.id ] = $thread } } }

foreach ( $parentChannel in ( $channelList | Where-Object { $threadParentTypes -contains $_.type } ) )
{
	if ( $skipNames -contains $parentChannel.name.ToLowerInvariant() ) { continue }

	$beforeTimestamp = $null
	while ( $true )
	{
		$query = "/channels/$( $parentChannel.id )/threads/archived/public?limit=100"
		if ( $beforeTimestamp ) { $query += "&before=$( [uri]::EscapeDataString( $beforeTimestamp ) )" }

		$archived = Invoke-Discord $query
		if ( -not $archived.Ok ) { break }

		$archivedThreads = @( $archived.Data.threads | Where-Object { $_ } )
		if ( $archivedThreads.Count -eq 0 ) { break }

		$olderThanWindow = $false
		foreach ( $thread in $archivedThreads )
		{
			$archiveTime = [DateTimeOffset]::Parse( [string] $thread.thread_metadata.archive_timestamp )
			if ( $archiveTime -lt $windowStart ) { $olderThanWindow = $true; continue }
			$threadsById[ [string] $thread.id ] = $thread
		}

		if ( $olderThanWindow -or -not $archived.Data.has_more ) { break }
		$beforeTimestamp = [string] $archivedThreads[-1].thread_metadata.archive_timestamp
	}
}

function Get-ThreadLastActivity( $thread )
{
	$candidateTimes = @( Get-SnowflakeTime $thread.id )
	if ( $thread.last_message_id ) { $candidateTimes += Get-SnowflakeTime $thread.last_message_id }
	if ( $thread.thread_metadata.archive_timestamp ) { $candidateTimes += [DateTimeOffset]::Parse( [string] $thread.thread_metadata.archive_timestamp ) }
	return ( $candidateTimes | Sort-Object -Descending | Select-Object -First 1 )
}

# ---------------------------------------------------------------------------------------------
# Build conversations
# ---------------------------------------------------------------------------------------------

$conversations = [System.Collections.Generic.List[object]]::new()
$authorNamesById = @{}

function Get-AuthorName( $author )
{
	if ( $author.global_name ) { return [string] $author.global_name }
	return [string] $author.username
}

function Convert-Message( $message, [string] $containerId )
{
	$messageTime = Get-SnowflakeTime $message.id
	$authorId = [string] $message.author.id
	$authorName = Get-AuthorName $message.author
	$authorNamesById[ $authorId ] = $authorName

	$contentText = [string] $message.content
	foreach ( $mentionedUser in @( $message.mentions | Where-Object { $_ } ) )
	{
		$mentionName = Get-AuthorName $mentionedUser
		$contentText = $contentText -replace "<@!?$( $mentionedUser.id )>", "@$mentionName"
	}
	$contentText = [regex]::Replace( $contentText, '<#(\d+)>', { param( $match ) $linked = $channelsById[ $match.Groups[1].Value ]; if ( $linked ) { "#$( $linked.name )" } else { $match.Value } } )

	# Forwarded messages carry their content in message_snapshots.
	foreach ( $snapshot in @( $message.message_snapshots | Where-Object { $_ } ) )
	{
		if ( $snapshot.message.content ) { $contentText += "`n[forwarded] $( $snapshot.message.content )" }
	}

	$replyTo = $null
	if ( $message.type -eq 19 -and $message.message_reference.message_id )
	{
		$referenced = $message.referenced_message
		$replyTo = [pscustomobject] @{
			messageId  = [string] $message.message_reference.message_id
			authorName = if ( $referenced ) { Get-AuthorName $referenced.author } else { '(deleted)' }
			snippet    = if ( $referenced ) { ( [string] $referenced.content ) -replace '\s+', ' ' } else { '' }
		}
	}

	$attachmentList = foreach ( $attachment in @( $message.attachments | Where-Object { $_ } ) )
	{
		[pscustomobject] @{
			name        = [string] $attachment.filename
			url         = [string] $attachment.url
			size        = [long] $attachment.size
			contentType = [string] $attachment.content_type
			localPath   = $null
		}
	}

	$embedList = foreach ( $embed in @( $message.embeds | Where-Object { $_ } ) )
	{
		( @( $embed.title, $embed.url ) | Where-Object { $_ } ) -join ' - '
	}

	$reactionList = foreach ( $reaction in @( $message.reactions | Where-Object { $_ } ) )
	{
		"$( $reaction.emoji.name )x$( $reaction.count )"
	}

	return [pscustomobject] @{
		id          = [string] $message.id
		link        = "https://discord.com/channels/$GuildId/$containerId/$( $message.id )"
		time        = $messageTime.ToString( 'o' )
		inWindow    = $messageTime -ge $windowStart
		authorId    = $authorId
		authorName  = $authorName
		isStaff     = $staffIds.Contains( $authorId )
		isBot       = [bool] $message.author.bot
		content     = $contentText
		replyTo     = $replyTo
		startedThread = if ( $message.thread ) { "$( $message.thread.name ) ($( $message.thread.id ))" } else { $null }
		attachments = @( $attachmentList )
		embeds      = @( $embedList | Where-Object { $_ } )
		reactions   = @( $reactionList )
	}
}

# Keep normal messages, replies, slash-command responses and thread-starter references; drop joins/boosts/pins.
$keptMessageTypes = @( 0, 19, 20, 21, 23 )

function Add-Conversation( [string] $kind, $channelObject, $parentObject, $rawMessages, [bool] $wasTruncated )
{
	$convertedMessages = @( $rawMessages | Where-Object { $keptMessageTypes -contains $_.type } | ForEach-Object { Convert-Message $_ $channelObject.id } )
	$windowMessages = @( $convertedMessages | Where-Object { $_.inWindow } )
	if ( $windowMessages.Count -eq 0 ) { return }

	$lastStaffMessage = $convertedMessages | Where-Object { $_.isStaff } | Select-Object -Last 1
	$lastUserMessage = $convertedMessages | Where-Object { -not $_.isStaff -and -not $_.isBot } | Select-Object -Last 1
	$lastMessage = $convertedMessages | Select-Object -Last 1

	$tagNames = @( @( $channelObject.applied_tags ) | Where-Object { $_ } | ForEach-Object { $tagNamesById[ [string] $_ ] } | Where-Object { $_ } )

	$conversations.Add( [pscustomobject] @{
		kind              = $kind
		id                = [string] $channelObject.id
		name              = [string] $channelObject.name
		parentName        = if ( $parentObject ) { [string] $parentObject.name } else { $null }
		link              = "https://discord.com/channels/$GuildId/$( $channelObject.id )"
		tags              = $tagNames
		archived          = [bool] $channelObject.thread_metadata.archived
		locked            = [bool] $channelObject.thread_metadata.locked
		truncated         = $wasTruncated
		lastActivity      = $lastMessage.time
		windowCount       = $windowMessages.Count
		windowUserCount   = @( $windowMessages | Where-Object { -not $_.isStaff -and -not $_.isBot } ).Count
		lastSpeaker       = if ( $lastMessage.isStaff ) { "staff ($( $lastMessage.authorName ))" } else { $lastMessage.authorName }
		lastStaffTime     = if ( $lastStaffMessage ) { $lastStaffMessage.time } else { $null }
		userAfterStaff    = [bool] ( $lastUserMessage -and ( -not $lastStaffMessage -or [DateTimeOffset]::Parse( $lastUserMessage.time ) -gt [DateTimeOffset]::Parse( $lastStaffMessage.time ) ) )
		userMsgsAfterStaff = @( $convertedMessages | Where-Object { $_.inWindow -and -not $_.isStaff -and -not $_.isBot -and ( -not $lastStaffMessage -or [DateTimeOffset]::Parse( $_.time ) -gt [DateTimeOffset]::Parse( $lastStaffMessage.time ) ) } ).Count
		messages          = $convertedMessages
	} )
}

foreach ( $channel in ( $channelList | Where-Object { $readableChannelTypes -contains $_.type } | Sort-Object position ) )
{
	if ( $skipNames -contains $channel.name.ToLowerInvariant() ) { continue }
	if ( $channel.last_message_id -and ( Get-SnowflakeTime $channel.last_message_id ) -lt $windowStart ) { continue }

	$truncated = $false
	$channelMessages = Get-ChannelMessages $channel.id $contextStart $MaxChannelMessages ([ref] $truncated)
	if ( $null -eq $channelMessages ) { continue }
	Add-Conversation 'channel' $channel $null $channelMessages $truncated
}

foreach ( $thread in $threadsById.Values )
{
	if ( ( Get-ThreadLastActivity $thread ) -lt $windowStart ) { continue }
	$parentChannel = $channelsById[ [string] $thread.parent_id ]
	if ( $parentChannel -and ( $skipNames -contains $parentChannel.name.ToLowerInvariant() ) ) { continue }

	$truncated = $false
	$threadMessages = Get-ChannelMessages $thread.id ([DateTimeOffset]::MinValue) $MaxThreadMessages ([ref] $truncated)
	if ( $null -eq $threadMessages ) { continue }

	$threadKind = if ( $parentChannel -and $parentChannel.type -in @( 15, 16 ) ) { 'forum post' } else { 'thread' }
	Add-Conversation $threadKind $thread $parentChannel $threadMessages $truncated
}

# ---------------------------------------------------------------------------------------------
# Download attachments (images + text-like files) from messages inside the window
# ---------------------------------------------------------------------------------------------

$textExtensions = @( '.log', '.txt', '.json', '.yaml', '.yml', '.csv', '.xml', '.ini', '.cfg', '.mairagraph', '.md' )
$maxAttachmentBytes = [long] ( $MaxAttachmentMB * 1MB )

if ( -not $NoAttachments )
{
	foreach ( $conversation in $conversations )
	{
		foreach ( $message in ( $conversation.messages | Where-Object { $_.inWindow } ) )
		{
			$attachmentIndex = 0
			foreach ( $attachment in $message.attachments )
			{
				$attachmentIndex++
				$extension = [System.IO.Path]::GetExtension( $attachment.name ).ToLowerInvariant()
				$isWanted = ( $attachment.contentType -like 'image/*' ) -or ( $attachment.contentType -like 'text/*' ) -or ( $textExtensions -contains $extension )
				if ( -not $isWanted -or $attachment.size -gt $maxAttachmentBytes ) { continue }

				# Index in the name: one message often has several files all called "image.png".
				$localPath = Join-Path $attachmentsDir "$( $message.id )-$attachmentIndex-$( Get-SafeFileName $attachment.name )"
				try
				{
					Invoke-WebRequest -Uri $attachment.url -Headers @{ 'User-Agent' = $requestHeaders['User-Agent'] } -OutFile $localPath | Out-Null
					$attachment.localPath = $localPath
				}
				catch
				{
					$script:apiErrors.Add( "attachment download failed: $( $attachment.name ) ($( $_.Exception.Message ))" )
				}
			}
		}
	}
}

# ---------------------------------------------------------------------------------------------
# Write outputs
# ---------------------------------------------------------------------------------------------

$sortedConversations = @( $conversations | Sort-Object { [DateTimeOffset]::Parse( $_.lastActivity ) } -Descending )
for ( $index = 0; $index -lt $sortedConversations.Count; $index++ ) { $sortedConversations[ $index ] | Add-Member -NotePropertyName key -NotePropertyValue "C$( $index + 1 )" }

$activity = [pscustomobject] @{
	generatedAt   = $nowTime.ToString( 'o' )
	windowStart   = $windowStart.ToString( 'o' )
	days          = $Days
	guildId       = $GuildId
	guildName     = $guild.name
	ownerId       = [string] $guild.owner_id
	botId         = [string] $botUser.id
	staffIds      = @( $staffIds )
	errors        = @( $script:apiErrors )
	hiddenChannels = @( $script:hiddenChannels )
	conversations = $sortedConversations
}
$activity | ConvertTo-Json -Depth 12 | Set-Content -Path ( Join-Path $OutDir 'activity.json' ) -Encoding utf8

function Format-LocalTime( [string] $isoTime ) { return [DateTimeOffset]::Parse( $isoTime ).ToLocalTime().ToString( 'yyyy-MM-dd HH:mm' ) }

$digest = [System.Text.StringBuilder]::new()
[void] $digest.AppendLine( "# MAIRA Discord activity - last $Days days" )
[void] $digest.AppendLine()
[void] $digest.AppendLine( "Window starts $( Format-LocalTime $windowStart.ToString( 'o' ) ) local time (text channels include $ContextDays extra days marked ``[ctx]`` for context). Generated $( Format-LocalTime $nowTime.ToString( 'o' ) )." )
[void] $digest.AppendLine( "Staff: owner $( $authorNamesById[ [string] $guild.owner_id ] ?? '' ) ($( $guild.owner_id )), bot $( $botUser.username ) ($( $botUser.id ))$( if ( $StaffUserIds.Count ) { ', plus ' + ( $StaffUserIds -join ', ' ) } )." )
[void] $digest.AppendLine( "``User msg after staff`` = someone other than staff posted after the last staff message (or staff never posted); the number is how many such messages fall in the window. A hint for forum posts and threads; in busy channels it says little." )
if ( $script:hiddenChannels.Count ) { [void] $digest.AppendLine( "Not visible to the bot (skipped): $( @( $script:hiddenChannels ) -join ', ' )." ) }
[void] $digest.AppendLine()
[void] $digest.AppendLine( '## Index' )
[void] $digest.AppendLine()
[void] $digest.AppendLine( '| Key | Where | Kind | Tags | Last activity | Msgs (users) in window | Last speaker | User msg after staff |' )
[void] $digest.AppendLine( '|---|---|---|---|---|---|---|---|' )
foreach ( $conversation in $sortedConversations )
{
	$whereText = if ( $conversation.parentName ) { "$( $conversation.parentName ) > $( $conversation.name )" } else { "#$( $conversation.name )" }
	$whereText = $whereText -replace '\|', '/'
	[void] $digest.AppendLine( "| $( $conversation.key ) | $whereText | $( $conversation.kind ) | $( $conversation.tags -join ', ' ) | $( Format-LocalTime $conversation.lastActivity ) | $( $conversation.windowCount ) ($( $conversation.windowUserCount )) | $( $conversation.lastSpeaker -replace '\|', '/' ) | $( if ( $conversation.userAfterStaff ) { "YES ($( $conversation.userMsgsAfterStaff ))" } else { '' } ) |" )
}
[void] $digest.AppendLine()

foreach ( $conversation in $sortedConversations )
{
	$heading = if ( $conversation.parentName ) { "$( $conversation.parentName ) > `"$( $conversation.name )`"" } else { "#$( $conversation.name )" }
	$flags = @( $conversation.kind )
	if ( $conversation.tags.Count ) { $flags += "tags: $( $conversation.tags -join ', ' )" }
	if ( $conversation.archived ) { $flags += 'archived' }
	if ( $conversation.locked ) { $flags += 'locked' }
	if ( $conversation.truncated ) { $flags += 'TRUNCATED (oldest messages not fetched)' }

	[void] $digest.AppendLine( "## [$( $conversation.key )] $heading" )
	[void] $digest.AppendLine()
	[void] $digest.AppendLine( "$( $flags -join ' | ' ) | post to channelId ``$( $conversation.id )`` | $( $conversation.link )" )
	[void] $digest.AppendLine()

	foreach ( $message in $conversation.messages )
	{
		$markers = @()
		if ( -not $message.inWindow ) { $markers += '[ctx]' }
		if ( $message.isStaff ) { $markers += '[STAFF]' } elseif ( $message.isBot ) { $markers += '[bot]' }

		$line = "- $( $markers -join ' ' ) $( Format-LocalTime $message.time ) | msg ``$( $message.id )`` | **$( $message.authorName )** (``$( $message.authorId )``)".Replace( '-  ', '- ' )
		if ( $message.replyTo )
		{
			$snippet = $message.replyTo.snippet
			if ( $snippet.Length -gt 100 ) { $snippet = $snippet.Substring( 0, 100 ) + '...' }
			$line += " replying to ``$( $message.replyTo.messageId )`` ($( $message.replyTo.authorName ): `"$snippet`")"
		}
		[void] $digest.AppendLine( $line )

		if ( $message.content )
		{
			foreach ( $contentLine in ( $message.content -split "`r?`n" ) ) { [void] $digest.AppendLine( "    $contentLine" ) }
		}
		foreach ( $attachment in $message.attachments )
		{
			$location = if ( $attachment.localPath ) { $attachment.localPath } elseif ( -not $message.inWindow ) { 'not downloaded (context message; URL in activity.json)' } else { 'not downloaded (type or size skipped; URL in activity.json)' }
			[void] $digest.AppendLine( "    [attachment] $( $attachment.name ) ($( [math]::Round( $attachment.size / 1KB ) ) KB, $( $attachment.contentType )) -> $location" )
		}
		foreach ( $embed in $message.embeds ) { [void] $digest.AppendLine( "    [embed] $embed" ) }
		if ( $message.reactions.Count ) { [void] $digest.AppendLine( "    [reactions] $( $message.reactions -join ' ' )" ) }
		if ( $message.startedThread ) { [void] $digest.AppendLine( "    [started thread] $( $message.startedThread )" ) }
	}
	[void] $digest.AppendLine()
}

if ( $script:apiErrors.Count )
{
	[void] $digest.AppendLine( '## Fetch errors' )
	[void] $digest.AppendLine()
	foreach ( $apiError in $script:apiErrors ) { [void] $digest.AppendLine( "- $apiError" ) }
}

$digestPath = Join-Path $OutDir 'digest.md'
Set-Content -Path $digestPath -Value $digest.ToString() -Encoding utf8

$awaitingCount = @( $sortedConversations | Where-Object { $_.userAfterStaff } ).Count
Write-Output "Conversations with activity in the last $Days days: $( $sortedConversations.Count ) ($awaitingCount with a user message after the last staff message)."
Write-Output "Messages in window: $( ( $sortedConversations | Measure-Object -Property windowCount -Sum ).Sum )"
Write-Output "Digest:  $digestPath"
Write-Output "JSON:    $( Join-Path $OutDir 'activity.json' )"
if ( $script:hiddenChannels.Count ) { Write-Output "Not visible to the bot: $( @( $script:hiddenChannels ) -join ', ' )" }
if ( $script:apiErrors.Count ) { Write-Output "Fetch errors: $( $script:apiErrors.Count ) (listed at the end of the digest)" }
