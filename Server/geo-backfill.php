<?php

// Geolocate distinct IPs in user_ips that aren't in ip_geo yet, using the ipinfo.io batch API.
// Idempotent + re-runnable -> use for the one-time backfill AND as a cron to pick up new IPs.
//
//   php geo-backfill.php <IPINFO_TOKEN>
//   IPINFO_TOKEN=xxxx php geo-backfill.php
//
// ipinfo free tier: 50k lookups/month. Batch endpoint accepts up to 1000 IPs per request.

const DB_HOST = '127.0.0.1';
const DB_NAME = 'maira-refactored';
const DB_USER = 'maira-refactored';
const DB_PASS = '<NEW_STRONG_PASSWORD>';            // injected server-side; repo keeps the placeholder

const BATCH_SIZE = 1000;

$token = $argv[ 1 ] ?? getenv( 'IPINFO_TOKEN' ) ?: '';

if ( $token === '' )
{
	fwrite( STDERR, "Usage: php geo-backfill.php <IPINFO_TOKEN>\n" );
	exit( 1 );
}

$pdo = new PDO(
	'mysql:host=' . DB_HOST . ';dbname=' . DB_NAME . ';charset=utf8mb4',
	DB_USER, DB_PASS,
	[ PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION, PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC ]
);

$pdo->exec( "CREATE TABLE IF NOT EXISTS ip_geo (
	ipAddress   varchar(45) NOT NULL PRIMARY KEY,
	country     varchar(64)  NULL,
	countryCode char(2)      NULL,
	region      varchar(128) NULL,
	city        varchar(128) NULL,
	lat         decimal(9,6) NULL,
	lon         decimal(9,6) NULL,
	org         varchar(255) NULL,
	located     tinyint(1)   NOT NULL DEFAULT 0,
	created     datetime     NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci" );

// Distinct public IPv4s not yet geolocated. Skip private/reserved ranges to save quota.
$rows = $pdo->query( "SELECT DISTINCT ui.ipAddress
	FROM user_ips ui
	LEFT JOIN ip_geo g ON g.ipAddress = ui.ipAddress
	WHERE g.ipAddress IS NULL
	  AND ui.ipAddress <> ''
	  AND ui.ipAddress NOT LIKE '10.%'
	  AND ui.ipAddress NOT LIKE '192.168.%'
	  AND ui.ipAddress NOT LIKE '127.%'
	  AND ui.ipAddress NOT LIKE '169.254.%'
	  AND ui.ipAddress NOT RLIKE '^172\\\\.(1[6-9]|2[0-9]|3[01])\\\\.'" )->fetchAll( PDO::FETCH_COLUMN );

$total = count( $rows );

echo "IPs to geolocate: $total\n";

if ( $total === 0 )
{
	echo "Nothing to do.\n";
	exit( 0 );
}

$insert = $pdo->prepare( "INSERT INTO ip_geo (ipAddress, country, countryCode, region, city, lat, lon, org, located)
	VALUES (:ip, :country, :cc, :region, :city, :lat, :lon, :org, :located)
	ON DUPLICATE KEY UPDATE country=VALUES(country), countryCode=VALUES(countryCode), region=VALUES(region),
		city=VALUES(city), lat=VALUES(lat), lon=VALUES(lon), org=VALUES(org), located=VALUES(located)" );

$done = 0;

foreach ( array_chunk( $rows, BATCH_SIZE ) as $chunk )
{
	$curl = curl_init( 'https://ipinfo.io/batch?token=' . urlencode( $token ) );

	curl_setopt_array( $curl, [
		CURLOPT_POST           => true,
		CURLOPT_POSTFIELDS     => json_encode( array_values( $chunk ) ),
		CURLOPT_HTTPHEADER     => [ 'Content-Type: application/json', 'Accept: application/json' ],
		CURLOPT_RETURNTRANSFER => true,
		CURLOPT_TIMEOUT        => 60,
	] );

	$response = curl_exec( $curl );
	$code     = curl_getinfo( $curl, CURLINFO_RESPONSE_CODE );

	curl_close( $curl );

	if ( $response === false || $code !== 200 )
	{
		fwrite( STDERR, "Batch failed (HTTP $code): " . substr( (string) $response, 0, 200 ) . "\n" );
		exit( 2 );
	}

	$data = json_decode( $response, true );

	$pdo->beginTransaction();

	foreach ( $chunk as $ip )
	{
		$d = $data[ $ip ] ?? [];

		$lat = null;
		$lon = null;

		if ( ! empty( $d[ 'loc' ] ) && str_contains( $d[ 'loc' ], ',' ) )
		{
			[ $lat, $lon ] = array_map( 'trim', explode( ',', $d[ 'loc' ], 2 ) );
		}

		$cc = $d[ 'country' ] ?? null;   // ipinfo returns the 2-letter ISO code here

		$insert->execute( [
			':ip'      => $ip,
			':country' => $cc,
			':cc'      => $cc,
			':region'  => $d[ 'region' ] ?? null,
			':city'    => $d[ 'city' ] ?? null,
			':lat'     => $lat,
			':lon'     => $lon,
			':org'     => $d[ 'org' ] ?? null,
			':located' => $cc ? 1 : 0,   // bogon/private come back with no country
		] );
	}

	$pdo->commit();

	$done += count( $chunk );

	echo "  located $done / $total\n";

	usleep( 300000 );   // be polite between batches
}

echo "Done. Geolocated $done IPs.\n";
