<?php

// MAIRA user-metrics dashboard. Standalone (bypasses WordPress: real directory in the docroot).
// Deploy to /opt/bitnami/wordpress/user-metrics/index.php
//   - default          -> HTML dashboard
//   - ?format=json     -> the same metrics as JSON (used by the page's auto-refresh poll)

const DB_HOST = '127.0.0.1';
const DB_NAME = 'maira-refactored';
const DB_USER = 'maira-refactored';
const DB_PASS = '<NEW_STRONG_PASSWORD>';            // injected server-side; repo keeps the placeholder

// Exclude junk/sentinel users (blank + all-zero networkId) from every metric.
const REAL_USERS = "networkId NOT IN ('', '00000000-0000-0000-0000-000000000000')";

function db(): PDO
{
	return new PDO(
		'mysql:host=' . DB_HOST . ';dbname=' . DB_NAME . ';charset=utf8mb4',
		DB_USER, DB_PASS,
		[ PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION, PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC ]
	);
}

function compute_metrics(): array
{
	$pdo = db();

	// One pass over the (tiny) users table for all the headline counters.
	$kpi = $pdo->query( "SELECT
		COUNT(*)                                            AS total_users,
		COALESCE(SUM(hits),0)                               AS total_checks,
		ROUND(AVG(hits),1)                                  AS avg_checks,
		COALESCE(MAX(hits),0)                               AS max_checks,
		SUM(hits >= 20)                                     AS returning_users,
		SUM(lastModified >= NOW() - INTERVAL 15 MINUTE)     AS active_15m,
		SUM(lastModified >= NOW() - INTERVAL 1 HOUR)        AS active_1h,
		SUM(lastModified >= NOW() - INTERVAL 24 HOUR)       AS active_24h,
		SUM(lastModified >= NOW() - INTERVAL 7 DAY)         AS active_7d,
		SUM(lastModified >= NOW() - INTERVAL 30 DAY)        AS active_30d,
		SUM(lastModified >= NOW() - INTERVAL 365 DAY)       AS active_1y,
		SUM(created >= NOW() - INTERVAL 24 HOUR)            AS new_24h,
		SUM(created >= NOW() - INTERVAL 7 DAY)              AS new_7d,
		SUM(created >= NOW() - INTERVAL 30 DAY)             AS new_30d,
		MIN(created)                                        AS first_seen,
		MAX(lastModified)                                   AS last_activity
		FROM users WHERE " . REAL_USERS )->fetch();

	$kpi[ 'distinct_ips' ] = (int) $pdo->query( "SELECT COUNT(DISTINCT ipAddress) FROM user_ips" )->fetchColumn();

	// Daily new-user signups (all time) -> filled into a continuous date series for the charts.
	$byDay = [];
	foreach ( $pdo->query( "SELECT DATE(created) d, COUNT(*) n FROM users WHERE " . REAL_USERS . " GROUP BY DATE(created)" ) as $row )
	{
		$byDay[ $row[ 'd' ] ] = (int) $row[ 'n' ];
	}

	$labels = [];
	$daily  = [];
	$cumul  = [];
	$run    = 0;

	$day = new DateTime( substr( $kpi[ 'first_seen' ] ?? gmdate( 'Y-m-d' ), 0, 10 ) );
	$end = new DateTime( gmdate( 'Y-m-d' ) );

	for ( ; $day <= $end; $day->modify( '+1 day' ) )
	{
		$key = $day->format( 'Y-m-d' );
		$n   = $byDay[ $key ] ?? 0;
		$run += $n;

		$labels[] = $key;
		$daily[]  = $n;
		$cumul[]  = $run;
	}

	// Geolocation: assign each user the country/city of their most recently used IP, then aggregate.
	// (ip_geo is populated by geo-backfill.php; until then these come back empty and the UI shows "pending".)
	$latestIpPerUser = "SELECT ui.userId,
			SUBSTRING_INDEX(GROUP_CONCAT(ui.ipAddress ORDER BY ui.lastModified DESC, ui.id DESC), ',', 1) AS ip
		FROM user_ips ui GROUP BY ui.userId";

	$countries = [];
	$cities    = [];
	$geoUsers  = 0;

	if ( (int) $pdo->query( "SELECT COUNT(*) FROM ip_geo" )->fetchColumn() > 0 )
	{
		foreach ( $pdo->query( "SELECT g.countryCode AS cc, COUNT(*) AS users
			FROM ( $latestIpPerUser ) latest
			JOIN ip_geo g ON g.ipAddress = latest.ip
			JOIN users  u ON u.id = latest.userId AND u." . REAL_USERS . "
			WHERE g.countryCode IS NOT NULL AND g.countryCode <> ''
			GROUP BY g.countryCode ORDER BY users DESC" ) as $row )
		{
			$countries[] = [ 'cc' => $row[ 'cc' ], 'users' => (int) $row[ 'users' ] ];
			$geoUsers   += (int) $row[ 'users' ];
		}

		foreach ( $pdo->query( "SELECT g.countryCode AS cc, g.city AS city,
				ROUND(AVG(g.lat),4) AS lat, ROUND(AVG(g.lon),4) AS lon, COUNT(*) AS users,
				COALESCE(ROUND(AVG(u.hits),1),0) AS avg_hits, COALESCE(SUM(u.hits),0) AS total_hits
			FROM ( $latestIpPerUser ) latest
			JOIN ip_geo g ON g.ipAddress = latest.ip
			JOIN users  u ON u.id = latest.userId AND u." . REAL_USERS . "
			WHERE g.lat IS NOT NULL AND g.city IS NOT NULL AND g.city <> ''
			GROUP BY g.countryCode, g.city ORDER BY users DESC" ) as $row )
		{
			$cities[] = [ 'cc' => $row[ 'cc' ], 'city' => $row[ 'city' ],
				'lat' => (float) $row[ 'lat' ], 'lon' => (float) $row[ 'lon' ], 'users' => (int) $row[ 'users' ],
				'avg_hits' => (float) $row[ 'avg_hits' ], 'hits' => (int) $row[ 'total_hits' ] ];
		}
	}

	$geoCoverageIps = (int) $pdo->query( "SELECT COUNT(*) FROM ip_geo WHERE countryCode IS NOT NULL" )->fetchColumn();

	return [
		'kpi'            => $kpi,
		'series'         => [ 'labels' => $labels, 'daily' => $daily, 'cumulative' => $cumul ],
		'geo'            => [ 'countries' => $countries, 'cities' => $cities, 'mapped_users' => $geoUsers ],
		'geo_coverage'   => [ 'ips_located' => $geoCoverageIps, 'ips_total' => $kpi[ 'distinct_ips' ] ],
		'generated_utc'  => gmdate( 'Y-m-d H:i:s' ) . ' UTC',
	];
}

try
{
	$metrics = compute_metrics();
}
catch ( Throwable $throwable )
{
	http_response_code( 500 );

	if ( ( $_GET[ 'format' ] ?? '' ) === 'json' )
	{
		header( 'Content-Type: application/json' );
		echo json_encode( [ 'error' => $throwable->getMessage() ] );
	}
	else
	{
		echo '<h1>Metrics unavailable</h1><pre>' . htmlspecialchars( $throwable->getMessage() ) . '</pre>';
	}

	exit;
}

if ( ( $_GET[ 'format' ] ?? '' ) === 'json' )
{
	header( 'Content-Type: application/json' );
	echo json_encode( $metrics );
	exit;
}

?>
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<title>MAIRA · User Metrics</title>
<link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css">
<style>
	:root {
		--bg: #0c0e13; --panel: #151922; --panel2: #1b2230; --line: #262e3d;
		--text: #e8ecf4; --muted: #8a94a8; --accent: #ff7a18; --accent2: #ffb020; --good: #21d07a;
	}
	* { box-sizing: border-box; }
	body { margin: 0; background: radial-gradient(1200px 600px at 70% -10%, #1a2030 0%, var(--bg) 60%); color: var(--text);
		font: 15px/1.45 "Segoe UI", system-ui, -apple-system, Roboto, Helvetica, Arial, sans-serif; }
	a { color: var(--accent2); }
	.wrap { max-width: 1240px; margin: 0 auto; padding: 28px 20px 64px; }
	header { display: flex; align-items: center; gap: 14px; flex-wrap: wrap; margin-bottom: 22px; }
	.brand { font-size: 22px; font-weight: 700; letter-spacing: .3px; }
	.brand .a { color: var(--accent); }
	.sub { color: var(--muted); font-size: 13px; }
	.live { display: inline-flex; align-items: center; gap: 7px; margin-left: auto; color: var(--muted); font-size: 13px; }
	.dot { width: 9px; height: 9px; border-radius: 50%; background: var(--good); box-shadow: 0 0 0 0 rgba(33,208,122,.7); animation: pulse 2s infinite; }
	@keyframes pulse { 0% { box-shadow: 0 0 0 0 rgba(33,208,122,.6); } 70% { box-shadow: 0 0 0 10px rgba(33,208,122,0); } 100% { box-shadow: 0 0 0 0 rgba(33,208,122,0); } }
	.grid { display: grid; gap: 14px; }
	.kpis { grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); margin-bottom: 18px; }
	.card { background: linear-gradient(180deg, var(--panel2), var(--panel)); border: 1px solid var(--line); border-radius: 14px; padding: 16px 16px 14px; }
	.card .label { color: var(--muted); font-size: 12px; text-transform: uppercase; letter-spacing: .6px; }
	.card .value { font-size: 30px; font-weight: 700; margin-top: 6px; font-variant-numeric: tabular-nums; }
	.card .foot { color: var(--muted); font-size: 12px; margin-top: 4px; }
	.card.hero { grid-column: span 2; }
	.card.hero .value { font-size: 46px; color: var(--accent2); }
	.card .value.accent { color: var(--accent); }
	.value.good { color: var(--good); }
	.section { margin-top: 26px; }
	.section h2 { font-size: 15px; text-transform: uppercase; letter-spacing: .8px; color: var(--muted); margin: 0 0 12px; font-weight: 600; }
	.panels { grid-template-columns: 2fr 1fr; }
	.panel { background: var(--panel); border: 1px solid var(--line); border-radius: 14px; padding: 16px; }
	.panel h3 { margin: 0 0 12px; font-size: 14px; color: var(--text); font-weight: 600; }
	.chartbox { position: relative; height: 280px; }
	.chartbox.short { height: 240px; }
	#map { height: 420px; border-radius: 12px; overflow: hidden; }
	.leaflet-container { background: #0d1017; }
	.maplegend { background: rgba(20,25,34,.92); color: var(--text); padding: 8px 10px; border-radius: 8px; font-size: 12px; line-height: 1.5; border: 1px solid var(--line); }
	.maplegend .row { display: flex; align-items: center; gap: 5px; margin-top: 5px; }
	.maplegend .row.sz { color: var(--muted); margin-top: 6px; }
	.maplegend .sw { width: 13px; height: 13px; border-radius: 50%; display: inline-block; }
	.maplegend .heatbar { height: 10px; width: 132px; border-radius: 5px; margin-top: 5px;
		background: linear-gradient(90deg, rgb(38,70,220), rgb(0,180,216), rgb(80,200,120), rgb(255,214,64), rgb(255,138,30), rgb(224,36,36)); }
	.maplegend .heatlabels { display: flex; justify-content: space-between; color: var(--muted); margin-top: 2px; }
	.countries { list-style: none; margin: 0; padding: 0; max-height: 420px; overflow: auto; }
	.countries li { display: flex; align-items: center; gap: 10px; padding: 7px 4px; border-bottom: 1px solid var(--line); }
	.countries .flag { font-size: 18px; width: 24px; }
	.countries .name { flex: 1; }
	.countries .bar { height: 6px; background: var(--accent); border-radius: 3px; }
	.countries .cnt { color: var(--muted); font-variant-numeric: tabular-nums; min-width: 46px; text-align: right; }
	.pending { color: var(--muted); padding: 30px; text-align: center; }
	footer { margin-top: 30px; color: var(--muted); font-size: 12px; }
	@media (max-width: 820px) { .panels { grid-template-columns: 1fr; } .card.hero { grid-column: span 2; } }
</style>
</head>
<body>
<div class="wrap">
	<header>
		<div>
			<div class="brand"><span class="a">MAIRA</span> · User Metrics</div>
			<div class="sub">Marvin's Awesome iRacing App — live usage</div>
		</div>
		<div class="live"><span class="dot"></span> updated <span id="updated">—</span></div>
	</header>

	<div class="grid kpis">
		<div class="card hero"><div class="label">Total users who've tried MAIRA</div><div class="value" id="k_total">—</div><div class="foot" id="k_since">—</div></div>
		<div class="card"><div class="label">Active · 1 hour</div><div class="value good" id="k_1h">—</div></div>
		<div class="card"><div class="label">Active · 24 hours</div><div class="value" id="k_24h">—</div></div>
		<div class="card"><div class="label">Active · 7 days</div><div class="value" id="k_7d">—</div></div>
		<div class="card"><div class="label">Active · 30 days</div><div class="value" id="k_30d">—</div><div class="foot" id="k_sticky">—</div></div>
		<div class="card"><div class="label">Active · past year</div><div class="value" id="k_1y">—</div></div>
		<div class="card"><div class="label">New · 24 hours</div><div class="value accent" id="k_new24h">—</div></div>
		<div class="card"><div class="label">New · 7 days</div><div class="value accent" id="k_new7d">—</div></div>
		<div class="card"><div class="label">New · 30 days</div><div class="value accent" id="k_new30d">—</div></div>
		<div class="card"><div class="label">Total version checks</div><div class="value" id="k_checks">—</div><div class="foot" id="k_avg">—</div></div>
		<div class="card"><div class="label">Returning users</div><div class="value" id="k_return">—</div><div class="foot">checked 20+ times</div></div>
		<div class="card"><div class="label">Distinct networks</div><div class="value" id="k_ips">—</div><div class="foot">unique IP addresses</div></div>
	</div>

	<div class="section">
		<h2>Growth</h2>
		<div class="grid">
			<div class="panel"><h3>Cumulative users</h3><div class="chartbox"><canvas id="chartCumulative"></canvas></div></div>
			<div class="panel"><h3>New users · last 30 days</h3><div class="chartbox"><canvas id="chartNew"></canvas></div></div>
		</div>
	</div>

	<div class="section">
		<h2>Engagement</h2>
		<div class="panel"><h3>Active users by recency</h3><div class="chartbox short"><canvas id="chartRecency"></canvas></div></div>
	</div>

	<div class="section">
		<h2>Where MAIRA is used <span id="geoCoverage" class="sub"></span></h2>
		<div class="grid panels">
			<div class="panel"><h3>User locations</h3><div id="map"></div><div id="mapPending" class="pending" style="display:none">Geolocation backfill pending…</div></div>
			<div class="panel"><h3>Top countries</h3><ul class="countries" id="countries"><li class="pending">Geolocation backfill pending…</li></ul></div>
		</div>
	</div>

	<footer>
		Auto-refreshes every 30s. Times in UTC. Excludes sentinel rows (blank / all-zero network IDs).
		Generated <span id="genat">—</span>.
	</footer>
</div>

<script>window.__DATA__ = <?= json_encode( $metrics ) ?>;</script>
<script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
<script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.3/dist/chart.umd.min.js"></script>
<script>
const fmt = n => (n ?? 0).toLocaleString('en-US');
const regionName = (() => { try { return new Intl.DisplayNames(['en'], {type:'region'}); } catch { return null; } })();
const countryName = cc => { try { return regionName ? regionName.of(cc) : cc; } catch { return cc; } };
const flag = cc => cc && cc.length === 2 ? cc.toUpperCase().replace(/./g, c => String.fromCodePoint(127397 + c.charCodeAt(0))) : '🏁';
const daysBetween = iso => Math.floor((Date.now() - new Date(iso.replace(' ','T')+'Z')) / 86400000);

let charts = {}, map = null, markerLayer = null;

function paintKpis(d) {
	const k = d.kpi;
	const set = (id, v) => { const el = document.getElementById(id); if (el) el.textContent = v; };
	set('k_total', fmt(k.total_users));
	set('k_since', k.first_seen ? `serving since ${k.first_seen.slice(0,10)} · ${fmt(daysBetween(k.first_seen))} days` : '');
	set('k_1h', fmt(k.active_1h));
	set('k_24h', fmt(k.active_24h));
	set('k_7d',  fmt(k.active_7d));
	set('k_30d', fmt(k.active_30d));
	set('k_sticky', k.total_users ? `${Math.round(100*k.active_30d/k.total_users)}% of all users` : '');
	set('k_1y',  fmt(k.active_1y));
	set('k_new24h', fmt(k.new_24h));
	set('k_new7d',  fmt(k.new_7d));
	set('k_new30d', fmt(k.new_30d));
	set('k_checks', fmt(k.total_checks));
	set('k_avg', `avg ${fmt(Math.round(k.avg_checks))}/user · busiest ${fmt(k.max_checks)}`);
	set('k_return', k.total_users ? `${Math.round(100*k.returning_users/k.total_users)}%` : '0%');
	set('k_ips', fmt(k.distinct_ips));
	document.getElementById('updated').textContent = d.generated_utc.replace(' UTC','') + ' UTC';
	document.getElementById('genat').textContent = d.generated_utc;
}

const gridColor = 'rgba(255,255,255,.06)', tick = '#8a94a8';
function baseOpts(extra={}) {
	return Object.assign({ responsive:true, maintainAspectRatio:false, plugins:{legend:{display:false}},
		scales:{ x:{grid:{color:gridColor},ticks:{color:tick,maxRotation:0,autoSkip:true,maxTicksLimit:8}},
		         y:{grid:{color:gridColor},ticks:{color:tick},beginAtZero:true} } }, extra);
}

function paintCharts(d) {
	const s = d.series;
	const last30 = n => n.slice(-30);
	const cfgs = {
		chartCumulative: { type:'line', data:{ labels:s.labels, datasets:[{ data:s.cumulative, borderColor:'#ffb020', backgroundColor:'rgba(255,176,32,.12)', fill:true, pointRadius:0, borderWidth:2, tension:.25 }] }, options: baseOpts() },
		chartNew: { type:'bar', data:{ labels:last30(s.labels), datasets:[{ data:last30(s.daily), backgroundColor:'#ff7a18', borderRadius:3 }] }, options: baseOpts() },
		chartRecency: { type:'bar', data:{ labels:['15 min','1 hour','24 hours','7 days','30 days','past year'],
			datasets:[{ data:[d.kpi.active_15m,d.kpi.active_1h,d.kpi.active_24h,d.kpi.active_7d,d.kpi.active_30d,d.kpi.active_1y],
			backgroundColor:['#21d07a','#3fbf8f','#36a2c9','#5b8def','#8a6bef','#b85ad6'], borderRadius:4 }] },
			options: baseOpts({ indexAxis:'y' }) },
	};
	for (const [id,cfg] of Object.entries(cfgs)) {
		if (charts[id]) { charts[id].data = cfg.data; charts[id].update('none'); }
		else charts[id] = new Chart(document.getElementById(id), cfg);
	}
}

function paintCountries(d) {
	const ul = document.getElementById('countries');
	const list = d.geo.countries || [];
	if (!list.length) { ul.innerHTML = '<li class="pending">Geolocation backfill pending…</li>'; return; }
	const max = list[0].users || 1;
	ul.innerHTML = list.slice(0,40).map(c => `<li>
		<span class="flag">${flag(c.cc)}</span>
		<span class="name">${countryName(c.cc)}</span>
		<span class="bar" style="width:${Math.max(6, 90*c.users/max)}px"></span>
		<span class="cnt">${fmt(c.users)}</span></li>`).join('');
	const cov = d.geo_coverage;
	document.getElementById('geoCoverage').textContent = cov.ips_total ? `· ${fmt(cov.ips_located)} of ${fmt(cov.ips_total)} IPs located` : '';
}

// Heatmap color by total checks in the city: blue (cold) -> red (hot), saturating at 150+.
const HEAT = [
	[0.00, [ 38,  70, 220]],   // blue
	[0.30, [  0, 180, 216]],   // cyan
	[0.55, [ 80, 200, 120]],   // green
	[0.75, [255, 214,  64]],   // yellow
	[0.90, [255, 138,  30]],   // orange
	[1.00, [224,  36,  36]],   // red
];
function hitColor(h) {
	const t = Math.max(0, Math.min(1, (h - 1) / 149));
	for (let i = 1; i < HEAT.length; i++) {
		if (t <= HEAT[i][0]) {
			const t0 = HEAT[i-1][0], c0 = HEAT[i-1][1], t1 = HEAT[i][0], c1 = HEAT[i][1];
			const f = (t - t0) / (t1 - t0);
			const c = c0.map((v, j) => Math.round(v + (c1[j] - v) * f));
			return `rgb(${c[0]},${c[1]},${c[2]})`;
		}
	}
	return 'rgb(224,36,36)';
}

function addMapLegend() {
	const lg = L.control({ position:'bottomright' });
	lg.onAdd = () => {
		const div = L.DomUtil.create('div', 'maplegend');
		div.innerHTML = '<b>Total checks</b>'
			+ '<div class="heatbar"></div>'
			+ '<div class="heatlabels"><span>1</span><span>150+</span></div>'
			+ '<div class="row sz">size = users</div>';
		return div;
	};
	lg.addTo(map);
}

function paintMap(d) {
	const cities = d.geo.cities || [];
	const pending = document.getElementById('mapPending');
	if (!cities.length) { pending.style.display='block'; document.getElementById('map').style.display='none'; return; }
	pending.style.display='none'; document.getElementById('map').style.display='block';
	if (!map) {
		map = L.map('map', { worldCopyJump:true, attributionControl:true }).setView([25,5], 2);
		L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
			attribution:'© OpenStreetMap, © CARTO', subdomains:'abcd', maxZoom:7, minZoom:1 }).addTo(map);
		markerLayer = L.layerGroup().addTo(map);
		addMapLegend();
	}
	markerLayer.clearLayers();
	const max = Math.max(...cities.map(c=>c.users));
	// Draw coldest first so the hottest (highest total checks) bubbles render on top.
	[...cities].sort((a, b) => a.hits - b.hits).forEach(c => {
		const r = 4 + 16 * Math.sqrt(c.users / max);
		const col = hitColor(c.hits);
		L.circleMarker([c.lat, c.lon], { radius:r, color:col, weight:1, fillColor:col, fillOpacity:.6 })
			.bindPopup(`<b>${c.city}</b>, ${countryName(c.cc)}<br>${fmt(c.users)} user${c.users===1?'':'s'} · avg ${c.avg_hits} checks<br>${fmt(c.hits)} total checks`)
			.addTo(markerLayer);
	});
}

function render(d) { paintKpis(d); paintCharts(d); paintCountries(d); paintMap(d); }
render(window.__DATA__);

async function refresh() {
	try { const r = await fetch('?format=json', {cache:'no-store'}); const d = await r.json(); if (!d.error) render(d); } catch (e) {}
}
setInterval(refresh, 30000);
</script>
</body>
</html>
