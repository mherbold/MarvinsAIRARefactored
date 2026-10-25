# Knowledge sources for MAIRA support answers

Read this when verifying an answer or looking for the right page to link.

## Documentation page URLs

Docs are WordPress pages built from `Server/Wordpress/Documentation/<slug>.txt`. The live URL is
`https://mairapp.com/home/documentation/<category>/<slug>/`.

| Category | Slugs |
|---|---|
| `basic` | `lets-go-just-the-basic-stuff`, `downloading-and-installing`, `overview-feature-list`, `common-controls` |
| `advanced` | `racing-wheel`, `steering-effects`, `pedals`, `tuning-profiles`, `controller-profiles`, `simulator`, `game-bridge`, `accessibility`, `app-settings`, `app-manager`, `sounds`, `commentary`, `captions`, `overlays`, `trading-paints`, `g-tensioner`, `typhoon-wind`, `adminboxx` |
| `other-stuff` | `troubleshooting`, `wheel-force-settings`, `vb-cable-setup`, `help-translate-maira` |

Section anchors come from `<p class="section" id="anchor">` in the `.txt`. Link as
`.../<slug>/#anchor`. The anchors also match each settings page's XAML `HelpTopic`. Before linking
a page or anchor that's new to you, grep the `.txt` to confirm it exists. If the file and the map
disagree, the hrefs in the files' own prev/next navigation are the source of truth.

Old links to `herboldracing.com/marvins-awesome-iracing-app-maira/...` (still in some forum topics)
are MAIRA Classic-era. Point people to mairapp.com instead.

## Where answers usually come from

App source paths below are relative to the nested project folder `MarvinsAIRARefactored/`.

| Question type | Look at |
|---|---|
| "What does setting X do / what should I set" | docs page + `Pages/<Page>.xaml` (labels, HelpTopic) + `Components/<Component>.cs` |
| Default values | `DataContext/Settings.cs` / `ContextSettings.cs` field initializers |
| UI label wording | `Resources/Resources.resx` values (English); `Resources.<culture>.resx` for other languages |
| FFB feel / oscillation / graph questions | `racing-wheel.txt`, `wheel-force-settings.txt`, `Components/RacingWheel.cs`, `FFB/` modules, `BuiltInGraphs/` |
| Per-car settings resetting or "won't save" | `tuning-profiles.txt`, `DataContext/Context*.cs` |
| Wheel off/on, FFB lost, Logitech TrueForce | `troubleshooting.txt` (app.ini `resetWhenFFBLost`, `loadTrueForceAPI`) |
| Other sims (LMU, rF2, AC, ACC, RaceRoom) | `game-bridge.txt`, `Components/GameBridge.cs`, `GameBridges/` (one adapter per sim) |
| Crashes / won't start | ask for the log (`Documents\MAIRA\Logs`, newest file); `troubleshooting.txt#…` |
| Install / update / version | `downloading-and-installing.txt`, `gh release list` |
| G Tensioner, Typhoon Wind, AdminBoxx hardware | their doc pages + `Components/GTensioner.cs`, `TyphoonWind.cs`, `AdminBoxx.cs` |
| SimHub plugin | separate repo (see Marvin's memory note on the SimHub plugin) |

## Release facts

- `gh release list --limit 10` lists releases; the one marked Latest is what most people run.
  Alpha builds are pre-releases, which `#maira-alpha` testers use.
- Version numbers (`2.1.NNNN`) come from the build date, so a higher number means newer.
- The release notes body (`gh release view <tag>`) is the changelog. Quote it when telling
  someone a fix shipped.

## Marvin's guidance (corrections he has made to drafts)

These came from Marvin reviewing drafts. Treat them as settled unless the code has changed since.

- **LMU and the vJoy workaround:** don't recommend vJoy for Le Mans Ultimate. MAIRA should work with LMU directly; if it doesn't, that's a MAIRA bug to report, not a reason to work around it. vJoy is for games that can't turn their own FFB off, such as Assetto Corsa.
- **Torque in bridged games:** the game bridge passes the sim's steering torque through unscaled. LMU Hypercars peak around 130+ Nm, so a Max force like 160 Nm with a low Strength (e.g. 8% on a 12 Nm base) is a sensible choice that leaves headroom. Don't call it odd.
- **The Max force table in the docs is a rough estimate.** For what iRacing actually puts out in a given car, point people to the live **Current force** readout below **Max force** on the Racing wheel page.
- **Calibration and bridged games:** from the release after 2.1.7776, calibration files are disabled for game-bridge games, because there's no way to record them outside iRacing. Understeer, oversteer and the skid/slip indicator are therefore inactive in bridged games, while seat-of-pants still works. Don't judge seat-of-pants thresholds in bridged games against iRacing's scale; how other sims' Vy/Vx values scale is unknown.
- **Same-day fixes:** when a bug found during the sweep is fixed in the same session and Marvin says he's releasing that day, the reply says the fix "will be rolled out at some point today". Don't promise anything beyond that.

## Channel guide

| Channel | What happens there |
|---|---|
| `#maira` | Main help channel; busiest, many interleaved conversations |
| `#maira-alpha` | Alpha testers on pre-release builds. Check against `main` and recent commits, not just the last stable release |
| `#maira-deutsch` | German-speaking users; reply in German |
| `#maira-game-bridge` | Non-iRacing sims through the game bridge |
| `#maira-g-tensioner`, `#maira-typhoon-wind` | MAIRA hardware devices |
| `#maira-simhub` | SimHub plugin |
| `maira-bugs` (forum) | Bug reports; asks for wheelbase, car/track, issue, and a log for crashes |
| `maira-feature-requests` (forum) | Requests; check whether the feature already exists |
| `maira-faq` (forum) | Questions people file as FAQ posts; often real support questions |
| `suggested-wheelbase-settings` + car forums | Users sharing settings; usually no reply needed unless someone asks something |
| `#maira-videos`, `#off-topic-*`, announcements | Rarely need a reply |
