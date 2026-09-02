// SPDX-License-Identifier: MIT
using System;

namespace Emby.CreditsMarker
{
    /// <summary>
    /// Summary of the last scheduled scan, shown on the plugin's settings page.
    /// Process-lifetime only - resets when the server restarts (then it reads
    /// "hasn't run yet" until the next nightly run).
    /// </summary>
    internal static class ScanStats
    {
        public static DateTime LastRunUtc;
        public static string Outcome = "";   // "completed" | "stopped at the time cap" | "cancelled"
        public static int Checked;           // videos looked at this run
        public static int WithMarker;        // of those, how many had a marker or got one

        public static void Record(string outcome, int checkedCount, int withMarker)
        {
            Outcome = outcome;
            Checked = checkedCount;
            WithMarker = withMarker;
            LastRunUtc = DateTime.UtcNow;
        }

        /// <summary>One-line status, or null if no scan has run since startup.</summary>
        public static string Describe()
        {
            if (LastRunUtc == default) return null;

            double pct = Checked > 0 ? 100.0 * WithMarker / Checked : 0;
            var ago = DateTime.UtcNow - LastRunUtc;
            string when =
                ago.TotalMinutes < 2  ? Localization.T("just now") :
                ago.TotalMinutes < 90 ? Localization.TF("{0} min ago", (int)Math.Round(ago.TotalMinutes)) :
                ago.TotalHours < 48   ? Localization.TF("{0} h ago", (int)Math.Round(ago.TotalHours)) :
                                        Localization.TF("{0} days ago", (int)ago.TotalDays);

            return Localization.TF(
                "Coverage: {0:N0} of {1:N0} videos checked have a credits marker ({2:0}%). "
                + "Last scan: {3}, {4}. Live progress and manual runs: Dashboard → Scheduled Tasks → \"Detect end credits\".",
                WithMarker, Checked, pct, Localization.T(Outcome), when);
        }
    }
}
