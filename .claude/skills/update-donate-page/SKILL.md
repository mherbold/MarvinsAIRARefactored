---
name: update-donate-page
description: Regenerate the donor name lists on the MAIRA donate page (Pages/DonatePage.xaml) from the two buymeacoffee.com CSV exports in the Downloads folder. Use this whenever the user mentions updating the donate page, refreshing/processing the donor or supporter lists, new buymeacoffee CSV downloads (Marvinherbold_*.csv / Supporters_list_*.csv), mega/super donors, or adding new donors to the app — even if they don't name this skill explicitly.
---

# Update the donate page donor lists

The donate page ([DonatePage.xaml](../../../MarvinsAIRARefactored/Pages/DonatePage.xaml)) shows three
alphabetized donor lists. They are regenerated — never hand-edited — from two CSV exports the user
downloads from buymeacoffee.com into `C:\Users\marvi\Downloads`:

| File | Contents |
|---|---|
| `Marvinherbold_<date>.csv` | **Monthly** supporters (recurring subscriptions) |
| `Supporters_list_<date>.csv` | **Manual** (one-off) donations |

## Workflow

1. Confirm both CSVs exist in Downloads. If the newest pair is old (check the date in the file names
   against today), point that out — the user may have forgotten to download fresh exports — but
   proceed with the newest pair unless told otherwise.
2. Run the script (always `pwsh`, never `powershell.exe`):

   ```
   pwsh -File .claude/skills/update-donate-page/scripts/update-donate-page.ps1
   ```

   It finds the newest CSV pair automatically; pass `-MonthlyCsv` / `-ManualCsv` to override, or
   `-DryRun` to preview without touching the XAML.
3. Read the summary it prints:
   - **"Scrubbed names (eyeball these)"** — verify each scrub looks sane (no one reduced to an
     empty or silly name). Fix genuine scrubbing gaps in the script, not in the XAML.
   - **Warnings** — surface any (paused subscriptions, non-USD, missing markers) to the user.
4. Build the solution (VS MSBuild per CLAUDE.md) to confirm the regenerated XAML compiles.
5. Report the tier counts and any notable tier promotions to the user. Per the no-git rule, do not
   commit — the user handles git unless he invokes /commitpush.

## The rules the script implements

Keep these in mind when reviewing output or improving the script:

- **Totals are lifetime and deduped by supporter email** (lowercased; a trailing
  `_is_deleted_<digits>` suffix is stripped). The same person in both CSVs gets one combined total.
  People who used two emails but the exact same display name are also merged.
- **Monthly supporters** are credited *months paid × monthly amount*: one payment per monthly
  anniversary of the start date, through the cancellation date (inclusive) or through today while
  the subscription is active.
- **Tiers are exclusive** — each person appears only in their highest tier:
  MEGA DONORS ❤️❤️❤️❤️❤️ ≥ $100 · SUPER DONORS ❤️❤️❤️ $30–$99.99 · DONORS ❤️ < $30.
- **Anonymous donors are removed** — buymeacoffee exports them as "Someone". If someone donated
  both anonymously and under a name, the name (their most-used one) wins and all their money counts.
- **Name scrubbing** (privacy — never show an email address):
  - HTML entities decoded (`Ren&eacute;` → `René`).
  - Any token with an `@` mid-string keeps only the part before it (`mcalbols@gmail.com` →
    `mcalbols`, `Mattyice6723@twitchTV` → `Mattyice6723`); leading-`@` social handles are kept.
  - A trailing `@handle` after a real name is dropped (`Jean Paul Vieira @jeanpaulvieira` →
    `Jean Paul Vieira`).
  - URLs become their `@handle` or last path segment; 10+ digit account-number tokens are dropped.
- **Out-of-band donations** — money received outside buymeacoffee.com lives in
  [extra-donations.csv](extra-donations.csv) (`Name,Email,Amount,Note`) in this skill folder and is
  merged into the totals on every run. Fill in `Email` whenever it is known — those rows join the
  normal per-email dedup, so a later buymeacoffee donation from the same address combines no matter
  what display name it arrives under; rows without an email merge by exact display name instead.
  When the user mentions a donation that didn't come through buymeacoffee, add a row there — never
  hand-edit the XAML.
- **Sorting** is case-insensitive and ignores leading `@`/punctuation, so `@lucciano.netto` files
  under L.
- The script only rewrites the XAML between `<!-- BEGIN GENERATED: … -->` /
  `<!-- END GENERATED: … -->` marker comments (one pair per tier), preserving the UTF-8 BOM, so it
  is idempotent and the surrounding page layout is safe. If a marker is missing the script throws
  instead of guessing.

## Changing thresholds or sections

Thresholds are script parameters (`-MegaThreshold`, `-SuperThreshold`). Adding a new tier means:
a new `MairaGroupBox` in the XAML with its own marker pair and heart `SubLabel`, a new localized
header key (use the `localization` skill), and a matching `Replace-Section` call in the script.
