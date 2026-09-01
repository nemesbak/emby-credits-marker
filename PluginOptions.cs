// SPDX-License-Identifier: MIT
using Emby.Web.GenericEdit;
using MediaBrowser.Model.LocalizationAttributes;

namespace Emby.CreditsMarker
{
    // The [DisplayNameL] / [DescriptionL] keys are the English strings themselves.
    // Translations live in strings/<locale>.json (embedded, see Plugin.GetTranslations);
    // with no translation the key is shown as-is, i.e. English.
    public class PluginOptions : EditableOptionsBase
    {
        public override string EditorTitle => "Credits Marker";

        public override string EditorDescription =>
            "Marks where the end credits start so you can skip them - Emby has no built-in "
            + "end-credits detection.\n"
            + "A nightly task (\"Detect end credits\") works out the credits-start point of every "
            + "episode and movie and stores it. On episodes, Emby's player shows the \"Up Next\" "
            + "card exactly there (one click to the next episode). Turn on \"Auto-skip credits\" "
            + "and the server advances episodes by itself, on any client.";

        // ─────────────────────────────  What to scan  ─────────────────────────────

        [DisplayNameL("Process episodes")]
        [DescriptionL("Analyse episodes and mark where the end credits start.")]
        public bool ProcessEpisodes { get; set; } = true;

        [DisplayNameL("Process movies")]
        [DescriptionL("Add a visible \"Credits\" chapter to movies (Emby's \"Up Next\" card doesn't apply to films). Off by default.")]
        public bool ProcessMovies { get; set; } = false;

        [DisplayNameL("Libraries")]
        [DescriptionL("Comma-separated library names to limit the scan to. Empty = every video library.")]
        public string LibraryNames { get; set; } = "";

        // ─────────────────────────────  Markers  ─────────────────────────────

        [DisplayNameL("Visible chapter on the seek bar")]
        [DescriptionL("Besides the hidden marker, add a visible \"Credits\" tick on the progress bar. Works even if the viewer turned the \"next episode\" overlay off. Recommended.")]
        public bool AlsoVisibleChapterOnEpisodes { get; set; } = true;

        // ─────────────────────────────  Auto-skip  ─────────────────────────────

        [DisplayNameL("Auto-skip credits")]
        [DescriptionL("When an episode reaches the credits, the server tells the player to jump to the next episode. Works on every client, including iOS. Off by default.")]
        public bool AutoSkipCredits { get; set; } = false;

        [DisplayNameL("Auto-skip users")]
        [DescriptionL("Comma-separated usernames the auto-skip applies to. Empty = everyone.")]
        public string AutoSkipUsers { get; set; } = "";

        [DisplayNameL("Grace before skipping (seconds)")]
        [DescriptionL("Wait this many seconds into the credits before skipping. 0 = skip as soon as they start.")]
        public int AutoSkipGraceSeconds { get; set; } = 0;

        [DisplayNameL("On-screen notice when skipping")]
        [DescriptionL("Show a short message in the player just before the auto-skip, so it doesn't look like a glitch. Rendered the same way as Emby's own notices.")]
        public bool AutoSkipNotice { get; set; } = true;

        [DisplayNameL("Notice text")]
        [DescriptionL("The message shown when auto-skipping.")]
        public string AutoSkipNoticeText { get; set; } = "Skipping credits…";

        [DisplayNameL("Break client playback loops")]
        [DescriptionL("If a player gets stuck in a loop (starting one episode after another at full speed - a known Emby for iOS bug with a corrupted play queue), the server sends it a \"Stop\" to break the loop. Doesn't touch normal playback. Recommended.")]
        public bool BreakRunawayLoops { get; set; } = true;

        // ─────────────────────────────  Detection engine  ─────────────────────────────

        [DisplayNameL("Fast scan")]
        [DescriptionL("Keyframe-only analysis: ~9x faster, within about 2 s (always lands inside the credits, never early). Recommended.")]
        public bool FastKeyframeScan { get; set; } = true;

        [DisplayNameL("Audio-fingerprint fallback")]
        [DescriptionL("For series (e.g. anime) whose credits roll over content with no fade to black: finds the recurring end-theme across episodes and marks the whole series. Recommended.")]
        public bool EnableFingerprintFallback { get; set; } = true;

        [DisplayNameL("Re-scan already-marked items")]
        [DescriptionL("Re-analyse items that already have a marker. Only needed if you changed the advanced settings below.")]
        public bool Redetect { get; set; } = false;

        [DisplayNameL("Analyse new episodes on the fly")]
        [DescriptionL("When an episode is added, analyse it within minutes instead of waiting for the nightly task. If the series already has a consensus it's applied instantly, no ffmpeg. Off by default.")]
        public bool AnalyzeNewEpisodes { get; set; } = false;

        [DisplayNameL("Advanced · Delay before analysing new items (minutes)")]
        [DescriptionL("How long to wait after an episode is added before analysing it, so a full-season import settles first.")]
        public int NewEpisodeDelayMinutes { get; set; } = 20;

        // ─────────────────────────────  Advanced  ─────────────────────────────
        // The defaults work well; only touch these if detection misses on your content.

        [DisplayNameL("Advanced · Minimum black block (seconds)")]
        [DescriptionL("Shortest black-frame block that counts as the start of the credits.")]
        public double MinBlackSeconds { get; set; } = 3.0;

        [DisplayNameL("Advanced · Search window start (% of runtime)")]
        [DescriptionL("Ignore black blocks that start before this point of the total runtime.")]
        public int EarliestCreditsPercent { get; set; } = 82;

        [DisplayNameL("Advanced · Search window end (% of runtime)")]
        [DescriptionL("Ignore black blocks that start after this point.")]
        public int LatestCreditsPercent { get; set; } = 99;

        [DisplayNameL("Advanced · Tail to analyse (% of file)")]
        [DescriptionL("Only analyse the last N% of the file, for speed.")]
        public int AnalyzeTailPercent { get; set; } = 20;

        [DisplayNameL("Advanced · Fingerprint: tail compared (%)")]
        [DescriptionL("How much of each episode's tail is compared when looking for the recurring theme.")]
        public int FingerprintTailPercent { get; set; } = 30;

        [DisplayNameL("Advanced · Fingerprint: minimum match (seconds)")]
        [DescriptionL("Minimum length of shared audio to accept it as the end-theme.")]
        public int FingerprintMinRunSeconds { get; set; } = 20;

        [DisplayNameL("Advanced · Fixed-percentage fallback")]
        [DescriptionL("If neither black analysis nor fingerprinting find anything, mark at a fixed % of the runtime. Rough; off by default.")]
        public bool HeuristicFallback { get; set; } = false;

        [DisplayNameL("Advanced · Fixed percentage")]
        [DescriptionL("The point (% of runtime) used by the setting above.")]
        public int HeuristicPercent { get; set; } = 95;

        [DisplayNameL("Advanced · Max items per run")]
        [DescriptionL("0 = unlimited. Useful to spread a big first scan over several nights.")]
        public int MaxItemsPerRun { get; set; } = 0;

        [DisplayNameL("Advanced · Max hours per run")]
        [DescriptionL("Stop the task after this many hours and resume next run (on top of Emby's own cap). 0 = unlimited.")]
        public double MaxRunHours { get; set; } = 10;

        [DisplayNameL("Advanced · ffmpeg timeout per file (seconds)")]
        [DescriptionL("Abort a file's analysis if ffmpeg takes longer than this.")]
        public int FfmpegTimeoutSeconds { get; set; } = 900;
    }
}
