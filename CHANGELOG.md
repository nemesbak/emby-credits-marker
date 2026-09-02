# Changelog

## 1.14.0.0 — 2026-09-02
- **The settings page now actually shows in the server's language.** Emby 4.9
  renders a server-side plugin's `[DisplayNameL]` / `[DescriptionL]` verbatim -
  its `IHasTranslations` path only localises plugins that ship their own web UI.
  So the plugin now localises the form itself: a `TypeDescriptionProvider` swaps
  each label, description, the intro text and the coverage line for the current
  UI culture at render time (Emby sets the thread culture from the client's
  `X-Emby-Language`). English stays the source of truth; the same
  `strings/<locale>.json` files provide the translations. Ships Spanish; any
  other locale falls back to English exactly as before.
- No change to detection or auto-skip.

## 1.13.0.0 — 2026-09-02
- **Coverage line on the settings page.** After the nightly task runs, the plugin
  description shows a one-line summary: how many of the videos it checked have a
  credits marker, the percentage, how the last run ended (completed / stopped at
  the time cap / stopped at the per-run item cap) and how long ago it was. Points
  the reader at Dashboard → Scheduled Tasks for live progress and manual runs.
- The summary is process-lifetime only (resets on server restart, repopulates on
  the next run). No behaviour change to detection or auto-skip.

## 1.12.0.0 — 2026-09-01
- `NewItemMonitor` (analyse-new-episodes) now queries just the new episode's own
  series for the consensus check, instead of loading the whole episode library
  into memory on the tick.
- The embedded-chapter shortcut no longer counts against `Max items per run` -
  that cap is meant to pace the ffmpeg work, and reading a chapter is free.
- `NewItemMonitor` reuses the one chapter read for its consensus / analysis writes
  too.

## 1.11.0.0 — 2026-09-01
- **Scheduled task reads each item's chapters once** (was twice - one read to
  filter already-marked items, another for the embedded-chapter check). The
  "already marked?" test, the embedded-chapter test and the write all share that
  one read now. `Max items per run` counts items actually analysed, not skipped.
- **Settings page is translatable** (`IHasTranslations` + `[DisplayNameL]` /
  `[DescriptionL]`). English strings are the keys; `strings/es-ES.json` ships a
  full Spanish translation. Other locales just fall back to English.
- No behaviour change.

## 1.10.0.0 — 2026-09-01
- Polish pass for a public release:
  - Config UI text is now English (was bilingual); the notice text default is
    English too.
  - ffmpeg path comes from `IFfmpegManager` (the non-deprecated API); builds with
    zero warnings.
  - ffmpeg is invoked via `ProcessStartInfo.ArgumentList` instead of a formatted
    string — no quoting edge cases on odd paths.
  - Series grouping in the reconcile pass keys on a stable string, not
    `string.GetHashCode()` (which is randomised per process on .NET Core and made
    grouping inconsistent between runs for episodes with no SeriesId).
  - Added a unit-test project (`LooksLikeEndCredits`, `DensestCluster`,
    `LongestCommonRun`) wired into CI.
  - SPDX license headers; README badges.
- No behaviour change.

## 1.9.0.0 — 2026-09-01
- **On-screen notice on auto-skip** (`AutoSkipNotice`, on by default). Just before
  the server jumps to the next episode, it sends the client a short message
  ("Saltando créditos…", editable via `AutoSkipNoticeText`) via
  `ISessionManager.SendMessageCommand` — Emby renders it exactly like its own
  system toasts, so the skip reads as a genuine feature instead of a glitch.
  Emby has no "Skip credits" button of its own (only the "Up Next" card), so this
  is the closest to the skip-intro experience the server side can provide.

## 1.8.1.0 — 2026-09-01
- `Max hours per run` option (default 10, 0 = unlimited): the scheduled task stops
  after that long and resumes next run, on top of Emby's own runtime cap - so a
  manual API trigger can't run unbounded.
- README refreshed (embedded-chapter detection, per-series reconciliation, runaway
  protection).
- CI workflow declares `contents: write` for the Release step.

## 1.8.0.0 — 2026-08-31
- **Fixed: episodes whose file has an embedded "Credits" chapter were silently
  skipped forever.** Many release groups bake a chapter named "Credits" (or "End
  Credits", "Ending", "Dub credits", "Créditos"...) into the file. The old
  "already done?" check treated that as the plugin's marker, so the scheduled
  task excluded those episodes and never gave them a real `CreditsStart` - the
  "Up Next" card and auto-skip did nothing for them (thousands of episodes across
  major series). Now only a real `CreditsStart` marker counts as done.
- **New detection source: the embedded chapter itself.** If the file already
  carries a credits chapter in the 78-108% zone, its position is used directly -
  no ffmpeg, and more accurate than black detection. Positions past the runtime
  are clamped just inside it.
- The marker write no longer removes the file's own "Credits" chapter (only a
  duplicate right where our marker goes); the visible seek-bar tick is added only
  when there isn't already a credits chapter there.
- Movies: a late "Credits" chapter still counts as done (movies never get a
  `CreditsStart`).
- `NewItemMonitor` re-fetches each episode fresh when the timer fires instead of
  trusting the (often pre-analysis, runtime-less) object from the add event.
- Fingerprint pass: fps is derived from the real fingerprinted duration, not the
  over-requested window (was placing the marker ~1% off).
- `chromaprint` probe drains both pipes concurrently (was a latent deadlock).
- Runaway monitor: the "advancing" flag resets between separate failure bursts.

## 1.7.0.0 — 2026-08-31
- **Escalating loop-break.** A quick playback failure is now defined tightly
  (start and stop within ~8 s, position under ~6 s, not completed) so it can't be
  confused with a human clicking through episodes. Two of those advancing through
  different items triggers a Stop; if the client ignores it the plugin re-sends
  Stop every ~12 s up to 4 times, then gives up loudly and disables auto-skip for
  that device for 15 minutes with a log line pointing at the client's codec /
  transcode settings.
- Every event handler is fully wrapped now - nothing the monitor does can throw
  into Emby's playback pipeline.
- Restated: auto-skip works identically whether the stream is direct-played or
  transcoded; it only ever compares playback position to the marker.

## 1.6.0.0 — 2026-08-31
- **Runaway detection rewritten around quick playback failures.** The real signature
  of a client "continuous play" loop is: playback starts, then stops almost
  immediately at position ~0 without completing, over and over. The plugin now
  tracks exactly that (per device) instead of a crude start-rate, so it reacts
  faster and doesn't misfire on ordinary fast navigation. The old start-rate lock
  (5 starts / 90 s) stays as a cheap backstop.
- **The loop-breaking Stop is now sent only when the failures are marching through
  DIFFERENT items** (a queue-advance runaway, e.g. Emby for iOS with a mixed
  music+episode queue). A client retrying the *same* file is left alone — a Stop
  wouldn't help and would just interrupt its own recovery.
- **Before auto-skipping, the next item in the client's queue must resolve to an
  episode.** If the queue is corrupted (a music track sitting where the next
  episode should be), the plugin doesn't skip.
- If a quick failure lands right after the plugin's own NextTrack, that device is
  backed off immediately instead of waiting for a pile-up.

## 1.5.0.0 — 2026-08-31
- **Auto-skip guards are now keyed by device, not by PlaySessionId.** Emby for iOS
  mints a fresh PlaySessionId on every progress report, which defeated the
  "already skipped this playback" guard and the per-session cooldown — the plugin
  could fire the same NextTrack repeatedly on iOS. DeviceId is stable, so the
  cooldown (one skip / 45 s / device), the "skipped this episode already" guard,
  and the runaway lockout now actually hold on iOS.
- **Break client playback loops** (`BreakRunawayLoops`, on by default). When a
  device machine-guns playbacks (8 starts in 60 s — a broken client "continuous
  play" loop, e.g. Emby for iOS with a corrupted play queue), the plugin sends it
  a single `Stop` to break the loop instead of only standing down. Rate-limited to
  one Stop per 2 min per device. Normal playback is untouched.

## 1.4.0.0 — 2026-08-30
- **Analyse new episodes on the fly** (opt-in, `AnalyzeNewEpisodes`, off by
  default). A background monitor picks up newly-added episodes and, after a
  settle delay (`NewEpisodeDelayMinutes`, default 20), marks them — instantly
  from the series' existing consensus when there is one, otherwise with a black
  detection pass. New episodes become skippable within minutes instead of
  waiting for the nightly run. Processed in small batches, one file at a time,
  with the queue dropped whenever the option is off.
- Includes the 1.3.4 marker-write safety net.

## 1.3.4.0 — 2026-08-30
- Safety net around the marker write: the plugin already only ever touches the
  end of an item (it can't detect anything before 82% of runtime, and it
  preserves every existing marker on save). This adds a hard guard — if the
  rebuilt chapter list is ever missing a marker that was already there
  (an intro marker, a real chapter), the write is aborted and the item is left
  untouched. Opening/intro markers can never be lost.

## 1.3.3.0 — 2026-08-30
- Plugin icon (shown in Dashboard → Plugins).
- Every setting now has a bilingual (Spanish / English) label and a plain-language
  description; advanced knobs are grouped under an "Avanzado / Advanced" prefix.
- Rewritten plugin description and settings intro.
- No behaviour change.

## 1.3.2.0 — 2026-08-30
- Per-series reconciliation: episodes are marked as a whole series, not one by one.
  Where black detection agrees across a series it wins (and pulls stray episodes
  onto the consensus); where it doesn't, the series goes to fingerprinting even
  if some episodes got a (wrong) black marker. Much better on anime.
- More logging in the fingerprint pass.

## 1.3.1.0 — 2026-08-30
- Library Names filter now matches by on-disk location (works with mixed-content
  libraries where the previous folder-name match returned nothing).

## 1.3.0.0 — 2026-08-30
- **Fingerprint fallback**: for series where black detection finds nothing (end
  credits over content, e.g. anime), fingerprint the tail of a few episodes,
  find the recurring end-theme, and mark every episode of the series. Needs
  chromaprint in ffmpeg (probed once; disabled cleanly if absent).
- **Auto-skip safety**: per-session cooldown (one skip / 45 s / session),
  runaway-session lockout (a client stuck in a "continuous play" loop is ignored),
  and a floor on the marker position (never skip on a marker before 75 % of
  runtime). The plugin can no longer amplify a client-side playback loop.

## 1.2.0.0 — 2026-08-30
- `Also Visible Chapter On Episodes` on by default (works even if the viewer
  disabled the "next episode" overlay).
- Auto-skip: skip cleanly when the client reports a queue whose current item is
  the last one; log the queue position.

## 1.1.0.0 — 2026-08-29
- **Auto-skip** (`CreditsSkipMonitor`): watches playback and sends a "next
  episode" command at the credits point. Works on every client. Opt-in.
- **Fast Keyframe Scan**: `-skip_frame nokey`, ~9× faster detection.
- Optional visible `Credits` chapter on episodes.

## 1.0.0.0 — 2026-08-29
- Initial release: `Detect end credits` scheduled task, `CreditsStart` markers on
  episodes, visible `Credits` chapter on movies. ffmpeg black-frame detection.
