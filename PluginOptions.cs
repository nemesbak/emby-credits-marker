// SPDX-License-Identifier: MIT
using System.ComponentModel;
using Emby.Web.GenericEdit;

namespace Emby.CreditsMarker
{
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

        [DisplayName("Process episodes")]
        [Description("Analyse episodes and mark where the end credits start.")]
        public bool ProcessEpisodes { get; set; } = true;

        [DisplayName("Process movies")]
        [Description("Add a visible \"Credits\" chapter to movies (Emby's \"Up Next\" card doesn't apply to films). Off by default.")]
        public bool ProcessMovies { get; set; } = false;

        [DisplayName("Libraries")]
        [Description("Comma-separated library names to limit the scan to. Empty = every video library.")]
        public string LibraryNames { get; set; } = "";

        // ─────────────────────────────  Markers  ─────────────────────────────

        [DisplayName("Visible chapter on the seek bar")]
        [Description("Besides the hidden marker, add a visible \"Credits\" tick on the progress bar. "
            + "Works even if the viewer turned the \"next episode\" overlay off. Recommended.")]
        public bool AlsoVisibleChapterOnEpisodes { get; set; } = true;

        // ─────────────────────────────  Auto-skip  ─────────────────────────────

        [DisplayName("Auto-skip credits")]
        [Description("When an episode reaches the credits, the server tells the player to jump to the "
            + "next episode. Works on every client, including iOS. Off by default.")]
        public bool AutoSkipCredits { get; set; } = false;

        [DisplayName("Auto-skip users")]
        [Description("Comma-separated usernames the auto-skip applies to. Empty = everyone.")]
        public string AutoSkipUsers { get; set; } = "";

        [DisplayName("Grace before skipping (seconds)")]
        [Description("Wait this many seconds into the credits before skipping. 0 = skip as soon as they start.")]
        public int AutoSkipGraceSeconds { get; set; } = 0;

        [DisplayName("On-screen notice when skipping")]
        [Description("Show a short message in the player just before the auto-skip, so it doesn't look "
            + "like a glitch. Rendered the same way as Emby's own notices.")]
        public bool AutoSkipNotice { get; set; } = true;

        [DisplayName("Notice text")]
        [Description("The message shown when auto-skipping.")]
        public string AutoSkipNoticeText { get; set; } = "Skipping credits…";

        [DisplayName("Break client playback loops")]
        [Description("If a player gets stuck in a loop (starting one episode after another at full "
            + "speed - a known Emby for iOS bug with a corrupted play queue), the server sends it a "
            + "\"Stop\" to break the loop. Doesn't touch normal playback. Recommended.")]
        public bool BreakRunawayLoops { get; set; } = true;

        // ─────────────────────────────  Detection engine  ─────────────────────────────

        [DisplayName("Fast scan")]
        [Description("Keyframe-only analysis: ~9x faster, within about 2 s (always lands inside the "
            + "credits, never early). Recommended.")]
        public bool FastKeyframeScan { get; set; } = true;

        [DisplayName("Audio-fingerprint fallback")]
        [Description("For series (e.g. anime) whose credits roll over content with no fade to black: "
            + "finds the recurring end-theme across episodes and marks the whole series. Recommended.")]
        public bool EnableFingerprintFallback { get; set; } = true;

        [DisplayName("Re-scan already-marked items")]
        [Description("Re-analyse items that already have a marker. Only needed if you changed the advanced settings below.")]
        public bool Redetect { get; set; } = false;

        [DisplayName("Analyse new episodes on the fly")]
        [Description("When an episode is added, analyse it within minutes instead of waiting for the "
            + "nightly task. If the series already has a consensus it's applied instantly, no ffmpeg. Off by default.")]
        public bool AnalyzeNewEpisodes { get; set; } = false;

        [DisplayName("Advanced · Delay before analysing new items (minutes)")]
        [Description("How long to wait after an episode is added before analysing it, so a full-season import settles first.")]
        public int NewEpisodeDelayMinutes { get; set; } = 20;

        // ─────────────────────────────  Advanced  ─────────────────────────────
        // The defaults work well; only touch these if detection misses on your content.

        [DisplayName("Advanced · Minimum black block (seconds)")]
        [Description("Shortest black-frame block that counts as the start of the credits.")]
        public double MinBlackSeconds { get; set; } = 3.0;

        [DisplayName("Advanced · Search window start (% of runtime)")]
        [Description("Ignore black blocks that start before this point of the total runtime.")]
        public int EarliestCreditsPercent { get; set; } = 82;

        [DisplayName("Advanced · Search window end (% of runtime)")]
        [Description("Ignore black blocks that start after this point.")]
        public int LatestCreditsPercent { get; set; } = 99;

        [DisplayName("Advanced · Tail to analyse (% of file)")]
        [Description("Only analyse the last N% of the file, for speed.")]
        public int AnalyzeTailPercent { get; set; } = 20;

        [DisplayName("Advanced · Fingerprint: tail compared (%)")]
        [Description("How much of each episode's tail is compared when looking for the recurring theme.")]
        public int FingerprintTailPercent { get; set; } = 30;

        [DisplayName("Advanced · Fingerprint: minimum match (seconds)")]
        [Description("Minimum length of shared audio to accept it as the end-theme.")]
        public int FingerprintMinRunSeconds { get; set; } = 20;

        [DisplayName("Advanced · Fixed-percentage fallback")]
        [Description("If neither black analysis nor fingerprinting find anything, mark at a fixed % of the runtime. Rough; off by default.")]
        public bool HeuristicFallback { get; set; } = false;

        [DisplayName("Advanced · Fixed percentage")]
        [Description("The point (% of runtime) used by the setting above.")]
        public int HeuristicPercent { get; set; } = 95;

        [DisplayName("Advanced · Max items per run")]
        [Description("0 = unlimited. Useful to spread a big first scan over several nights.")]
        public int MaxItemsPerRun { get; set; } = 0;

        [DisplayName("Advanced · Max hours per run")]
        [Description("Stop the task after this many hours and resume next run (on top of Emby's own cap). 0 = unlimited.")]
        public double MaxRunHours { get; set; } = 10;

        [DisplayName("Advanced · ffmpeg timeout per file (seconds)")]
        [Description("Abort a file's analysis if ffmpeg takes longer than this.")]
        public int FfmpegTimeoutSeconds { get; set; } = 900;
    }
}
