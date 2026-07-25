---
name: commitpush
description: >-
  Commit and push the current MarvinsAIRA changes to origin — you write the
  commit message — after making sure any user-facing feature change is reflected
  in the documentation, then auto-deploy the changed doc pages to the live
  mairapp.com WordPress site and verify them. Use whenever the maintainer says
  "/commitpush", "commit and push", "push this", "commit these changes", "land
  this", or otherwise asks to commit/push the current work. This is the ONLY
  sanctioned path for you to write to git in this repo — outside this skill the
  maintainer handles git himself via GitHub Desktop and you never commit/push.
  The skill keeps Server/Wordpress/Documentation/*.txt in sync with the code and
  publishes doc edits to the live WordPress pages.
---

# /commitpush — commit, push, keep docs in sync, deploy to WordPress

One command that lands the current work: reconcile the docs with the code, write
a commit message, commit + push to origin, then deploy any changed documentation
pages to the live WordPress site and verify them.

## Golden rules

- **Only write to git from inside this skill (i.e. when the maintainer invokes
  it or clearly asks to commit/push).** By default you are hands-off git — the
  maintainer commits via GitHub Desktop ([[no-git-activity]]). This skill is the
  agreed exception: *he* decides **when** by invoking it; *you* do the mechanics
  and **write the commit message**.
- **Commit straight to the current branch (normally `main`).** The maintainer's
  workflow is direct-to-`main`, not feature-branch/PR. Do **not** create a
  branch or open a PR here (this intentionally overrides the generic "branch
  first on the default branch" default).
- **You write the commit message, in the maintainer's terse style.** Short
  imperative/descriptive subject, capitalized, **no trailing period**, no
  `type:` prefix — matching his history (e.g. `Batch and dedupe settings change
  logging`, `Rename wind docs to typhoon-wind`). Add a brief body only if the
  change genuinely needs explaining. End the message with a
  `Co-Authored-By: <the Claude model actually running, e.g. Claude Fable 5>
  <noreply@anthropic.com>` trailer (drop it only if he asks for clean
  trailer-free history).
- **The repo is the source of truth; the live WordPress pages are downstream.**
  Every code change that alters user-facing behavior must be reflected in the
  matching `Server/Wordpress/Documentation/*.txt` **before** committing, and
  deployed to WordPress **after** pushing.
- **Auto-deploy changed docs to WordPress and verify — no extra approval needed**
  (maintainer's choice). But always **back up the live page first** and confirm
  the update is **byte-exact**.
- **Only deploy docs that are part of this commit.** Never push a doc page to
  WordPress that wasn't just committed — the repo and the live site must not
  diverge.

## Step 0 — Survey the changes

```bash
git -C "<repo>" status --porcelain
git -C "<repo>" diff            # unstaged
git -C "<repo>" diff --staged   # staged
git -C "<repo>" log -6 --pretty=format:'%s'   # match the message style
```

Classify what changed: **code** (feature work), **docs**
(`Server/Wordpress/Documentation/*.txt`), or both.

## Step 1 — Reconcile the docs with the code

For any changed code that adds or updates a **user-facing feature**, update the
matching doc(s) now so they go into the same commit. If nothing user-facing
changed (pure refactor, build tweak, server PHP, etc.), skip to Step 2.

**Page / component → doc file:**

| Feature area (Pages\ / Components\) | Doc file |
|---|---|
| RacingWheelPage / RacingWheel, DirectInput, Drivers | `racing-wheel.txt` |
| SteeringEffectsPage / SteeringEffects | `steering-effects.txt` |
| PedalsPage / Pedals | `pedals.txt` |
| TyphoonWindPage / TyphoonWind | `typhoon-wind.txt` |
| GTensionerPage / GTensioner | `g-tensioner.txt` |
| AdminBoxxPage / AdminBoxx | `adminboxx.txt` |
| SpeechToTextPage / SpeechToText | `captions.txt` |
| CommentaryPage / Commentary, TextToSpeech, ElevenLabs | `commentary.txt` |
| SoundsPage / Sounds, AudioManager, LFE | `sounds.txt` (LFE routing also → `vb-cable-setup.txt`) |
| OverlaysPage / Windows overlays | `overlays.txt` |
| TradingPaintsPage / TradingPaints | `trading-paints.txt` |
| AppSettingsPage / Settings, CloudService | `app-settings.txt` |
| AppManagerPage / AppManager | `app-manager.txt` |
| ControllerProfilesPage / ControllerProfile, ButtonMappings | `controller-profiles.txt` |
| SimulatorPage / Simulator, Telemetry | `simulator.txt` |
| GameBridgePage / GameBridge, GameBridges\ | `game-bridge.txt` |
| GraphPage / Graph | `graph.txt` |
| Controls\Maira* (shared controls) | `common-controls.txt` |
| A brand-new feature area / capability | `overview-feature-list.txt` **and** a new per-feature doc |

**How to update a doc** (conventions in [[app-documentation-location]]): match the
WordPress-block format — section `<p class="section" id="anchor">` (id = the
page's `HelpTopic` anchor), setting `<p class="setting">`, plain `<p>`
description, default `<p class="default">Default: X</p>`. Source of truth for
each field:
- **Which controls exist + their order** → the Page `.xaml` (top-to-bottom /
  left-to-right).
- **Labels** → the *value* in `Resources\Resources.resx` (not the key name).
- **Defaults** → the `Settings.cs` field initializer — **not** the XAML
  `MairaKnob DefaultValue` (that is only the reset target and can differ).
- Preserve the prev/next nav footer.
- A **brand-new page**: create `<slug>.txt`, model it on a clean sibling
  (`typhoon-wind.txt`), wire its prev/next into the chain, and flag that a
  WordPress page must be created for it (new id) — it can't be deployed until it
  exists on WP.

## Step 2 — Commit & push

Stage exactly the intended files, write the message, commit, and push to origin
on the current branch.

```bash
git -C "<repo>" add <the code + doc files for this change>
git -C "<repo>" commit -m "<subject>" -m "Co-Authored-By: <running Claude model> <noreply@anthropic.com>"
git -C "<repo>" push
```

If `git push` fails on auth, **surface it** — the repo uses the maintainer's
stored GitHub credentials (same as GitHub Desktop); don't switch accounts or
fiddle with credentials silently. Report the commit sha + subject.

## Step 3 — Deploy changed docs to the live WordPress site (auto)

Do this **only** for `Server/Wordpress/Documentation/*.txt` files included in this
commit. The pages live on the [[mairapp-server]] Lightsail box; deploy with
SSH + WP-CLI (the WordPress MCP is read-only — see [[push-docs-to-wordpress]]).

SSH/scp (Git Bash), always `-o BatchMode=yes` (first connect also
`-o StrictHostKeyChecking=accept-new`):

```bash
KEY="C:\Users\marvi\Downloads\LightsailDefaultKey-us-west-2.pem"
HOST="bitnami@100.23.1.111"
WP="--path=/opt/bitnami/wordpress"
```

For each changed doc (`<slug>` = filename stem, `<id>` from the map below):

```bash
# 1. Back up the current live content OUTSIDE the repo (rollback safety)
ssh -o BatchMode=yes -i "$KEY" "$HOST" "sudo wp post get <id> --field=post_content $WP" \
  > "<scratchpad>/wp-backup-<id>-<slug>.html"

# 2. Ship the file, strip CRLF -> LF (repo working copies are CRLF), update verbatim
scp -o BatchMode=yes -i "$KEY" "<repo>/Server/Wordpress/Documentation/<slug>.txt" "$HOST:/home/bitnami/<slug>.txt"
ssh -o BatchMode=yes -i "$KEY" "$HOST" \
  "sed 's/\r$//' ~/<slug>.txt > ~/<slug>.lf.txt \
   && sudo wp post update <id> ~/<slug>.lf.txt $WP \
   && rm ~/<slug>.txt ~/<slug>.lf.txt"
```

`wp post update <id> <file>` sets `post_content` from the file **verbatim** — no
reproduction risk.

### slug → page-id → category map

Category is the URL segment: `https://mairapp.com/home/documentation/<category>/<slug>/`.

| slug (file stem) | id | category |
|---|---|---|
| overview-feature-list | 146 | basic |
| downloading-and-installing | 148 | basic |
| common-controls | 508 | basic |
| lets-go-just-the-basic-stuff | 150 | basic |
| app-manager | 852 | advanced |
| racing-wheel | 164 | advanced |
| steering-effects | 166 | advanced |
| pedals | 168 | advanced |
| typhoon-wind | 170 | advanced |
| g-tensioner | 849 | advanced |
| adminboxx | 858 | advanced |
| captions | 174 | advanced |
| commentary | 937 | advanced |
| sounds | 172 | advanced |
| overlays | 860 | advanced |
| trading-paints | 176 | advanced |
| app-settings | 182 | advanced |
| controller-profiles | 1034 | advanced |
| simulator | 180 | advanced |
| game-bridge | 1094 | advanced |
| graph | 178 | advanced |
| troubleshooting | 152 | other-stuff |
| wheel-force-settings | 451 | other-stuff |
| vb-cable-setup | 282 | other-stuff |
| help-translate-maira | 215 | other-stuff |

(New pages aren't here — a new doc needs a WordPress page created first, then add
its id.)

## Step 4 — Verify each deployed page

**Authoritative, immediate** (server-side raw content):

```bash
# Compare live post_content to the LF source; must be byte-identical.
ssh -o BatchMode=yes -i "$KEY" "$HOST" "sudo wp post get <id> --field=post_content $WP" \
  > "<scratchpad>/wp-live-<id>.txt"
sed 's/\r$//' "<repo>/Server/Wordpress/Documentation/<slug>.txt" > "<scratchpad>/<slug>.lf.txt"
cmp "<scratchpad>/wp-live-<id>.txt" "<scratchpad>/<slug>.lf.txt"
```

`cmp` should report no differences **except possibly a single trailing newline** —
`wp post get` appends one `\n` to its stdout. That one-byte tail difference is
expected and fine; any *other* diff means the update didn't take.

**End-user render check** (optional, secondary): WebFetch
`https://mairapp.com/home/documentation/<category>/<slug>/` and confirm a
distinctive new string is present. WordPress may serve a cached page for a short
while, so treat `wp post get` as the source of truth if the two disagree.

## Report

Summarize: files committed, commit sha + subject, push result, and for each
deployed doc — the page id, "updated", and the verify result (byte-exact ✓).
Note any doc that changed in the repo but couldn't be deployed (e.g. a brand-new
page with no WordPress id yet).

## Gotchas

- **CRLF.** Repo working copies are CRLF; always `sed 's/\r$//'` before
  `wp post update` and before `cmp`, or every line shows as different.
- **Trailing newline.** `wp post get` adds one `\n` — ignore that single-byte diff.
- **Backups.** Always capture the pre-update `post_content` before overwriting, so
  a bad deploy can be restored with another `wp post update` from the backup.
- **Scope.** Deploy/verify only docs in *this* commit. If a doc was edited but not
  committed, don't deploy it — fix the commit first.
- **New page.** A brand-new doc `.txt` can't be deployed until its WordPress page
  exists and its id is added to the map above.
