<?php

// Standalone version-check endpoint for mairapp.com. NOT a WordPress file.
// Deploy to /opt/bitnami/wordpress/get-current-version/index.php on the Lightsail server.
// It is served directly by Apache because this is a real directory in the docroot, so
// WordPress's permalink rewrite (which only rewrites non-file/non-dir paths) never touches it.

header( 'Content-Type: application/json' );

const DB_HOST = '127.0.0.1';
const DB_NAME = 'maira-refactored';
const DB_USER = 'maira-refactored';
const DB_PASS = '<NEW_STRONG_PASSWORD>';            // TODO: replace, then move out of the docroot
const GH_REPO = 'mherbold/MarvinsAIRARefactored';
const CACHE_TTL = 300;                               // seconds; protects the GitHub API rate limit

function client_ip(): string
{
	foreach ( [ 'HTTP_CLIENT_IP', 'HTTP_X_FORWARDED_FOR', 'REMOTE_ADDR' ] as $key )
	{
		if ( !empty( $_SERVER[ $key ] ) )
		{
			return trim( explode( ',', $_SERVER[ $key ] )[ 0 ] );
		}
	}

	return '';
}

function log_hit( string $id ): void
{
	try
	{
		$pdo = new PDO(
			'mysql:host=' . DB_HOST . ';dbname=' . DB_NAME . ';charset=utf8mb4',
			DB_USER, DB_PASS,
			[ PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION, PDO::ATTR_EMULATE_PREPARES => false ]
		);

		// Look up first so the common (repeat) path is an UPDATE and never burns an
		// auto-increment id. INSERT runs only for a genuinely new networkId. (The old
		// INSERT ... ON DUPLICATE KEY UPDATE pre-allocated and discarded an id on every
		// duplicate, which is why the counter ran away to ~446040.)

		$selectUser = $pdo->prepare( 'SELECT `id` FROM `users` WHERE `networkId` = ?' );
		$selectUser->execute( [ $id ] );
		$userId = $selectUser->fetchColumn();

		if ( $userId !== false )
		{
			$pdo->prepare( 'UPDATE `users` SET `hits` = `hits` + 1 WHERE `id` = ?' )->execute( [ $userId ] );
		}
		else
		{
			try
			{
				$pdo->prepare( 'INSERT INTO `users` (`networkId`,`hits`,`created`) VALUES (?,1,now())' )->execute( [ $id ] );

				$userId = $pdo->lastInsertId();
			}
			catch ( PDOException $exception )
			{
				// A concurrent request inserted it first (unique networkId). Re-read and bump.

				$selectUser->execute( [ $id ] );
				$userId = $selectUser->fetchColumn();

				$pdo->prepare( 'UPDATE `users` SET `hits` = `hits` + 1 WHERE `id` = ?' )->execute( [ $userId ] );
			}
		}

		// Same lookup-first pattern for user_ips (keyed on userId + ipAddress).

		$ipAddress = client_ip();

		$selectIp = $pdo->prepare( 'SELECT `id` FROM `user_ips` WHERE `userId` = ? AND `ipAddress` = ?' );
		$selectIp->execute( [ $userId, $ipAddress ] );
		$ipRowId = $selectIp->fetchColumn();

		if ( $ipRowId !== false )
		{
			$pdo->prepare( 'UPDATE `user_ips` SET `lastModified` = now() WHERE `id` = ?' )->execute( [ $ipRowId ] );
		}
		else
		{
			try
			{
				$pdo->prepare( 'INSERT INTO `user_ips` (`userId`,`ipAddress`,`created`) VALUES (?,?,now())' )->execute( [ $userId, $ipAddress ] );
			}
			catch ( PDOException $exception )
			{
				// Lost the race; the row now exists, nothing more to do.
			}
		}
	}
	catch ( Throwable $throwable )
	{
		// Analytics must never break the version response. Swallow.
	}
}

function fetch_latest_release(): array
{
	$cacheFile = sys_get_temp_dir() . '/maira_latest_release.json';

	if ( is_file( $cacheFile ) && ( ( time() - filemtime( $cacheFile ) ) < CACHE_TTL ) )
	{
		return json_decode( file_get_contents( $cacheFile ), true );
	}

	$curl = curl_init( 'https://api.github.com/repos/' . GH_REPO . '/releases/latest' );

	curl_setopt_array( $curl, [
		CURLOPT_RETURNTRANSFER => true,
		CURLOPT_HTTPHEADER     => [ 'User-Agent: MarvinsAIRARefactored', 'Accept: application/vnd.github+json' ],
		CURLOPT_TIMEOUT        => 10,
	] );

	$body = curl_exec( $curl );
	$code = curl_getinfo( $curl, CURLINFO_RESPONSE_CODE );

	curl_close( $curl );

	if ( ( $body === false ) || ( $code !== 200 ) )
	{
		// On API failure (e.g. rate limit) serve the last good cache if we have one.

		if ( is_file( $cacheFile ) )
		{
			return json_decode( file_get_contents( $cacheFile ), true );
		}

		throw new RuntimeException( "GitHub API returned $code" );
	}

	$release = json_decode( $body, true );

	$currentVersion = $release[ 'tag_name' ] ?? '';

	$downloadUrl = '';

	foreach ( $release[ 'assets' ] ?? [] as $asset )
	{
		if ( str_ends_with( $asset[ 'name' ] ?? '', '.exe' ) )
		{
			$downloadUrl = $asset[ 'browser_download_url' ];

			break;
		}
	}

	if ( ( $downloadUrl === '' ) && ( $currentVersion !== '' ) )
	{
		$downloadUrl = 'https://github.com/' . GH_REPO . "/releases/download/$currentVersion/MarvinsAIRARefactored-Setup-$currentVersion.exe";
	}

	// changeLog carries the raw GitHub release body (proper markdown) so the client can render
	// it directly and no longer needs its own second connection to the GitHub API.

	$result = [ 'currentVersion' => $currentVersion, 'downloadUrl' => $downloadUrl, 'changeLog' => $release[ 'body' ] ?? '' ];

	file_put_contents( $cacheFile, json_encode( $result ) );

	return $result;
}

$id = filter_input( INPUT_GET, 'id', FILTER_VALIDATE_REGEXP, [ 'options' => [ 'regexp' => '/^[a-f0-9\-]+$/' ] ] );

if ( ( $id !== false ) && ( $id !== null ) )
{
	log_hit( $id );
}

try
{
	echo json_encode( fetch_latest_release() );
}
catch ( Throwable $throwable )
{
	http_response_code( 502 );

	echo json_encode( [ 'error' => $throwable->getMessage() ] );
}
