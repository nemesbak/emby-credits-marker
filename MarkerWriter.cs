using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

namespace Emby.CreditsMarker
{
    /// <summary>
    /// Writes the credits marker(s) onto an item while preserving every other marker
    /// (intro markers, the file's real chapters, ...). Shared by the scheduled task
    /// and the new-episode monitor.
    /// </summary>
    internal sealed class MarkerWriter
    {
        public const string MarkerName = "Credits";
        private const long TicksPerSecond = 10_000_000L;
        // a "Credits" chapter within this distance of our target is treated as ours (or a
        // duplicate) - anything further is left alone (it's the release's own chapter).
        private const long OwnMarkerToleranceTicks = 30 * TicksPerSecond;

        private readonly IItemRepository _itemRepository;
        private readonly ILogger _log;

        public MarkerWriter(IItemRepository itemRepository, ILogger log)
        {
            _itemRepository = itemRepository;
            _log = log;
        }

        /// <summary>
        /// True once the item has a real <see cref="MarkerType.CreditsStart"/> marker - that is
        /// the thing that drives the "Up Next" card and the auto-skip. A plain chapter that
        /// happens to be named "Credits" (many release groups embed one) does NOT count for
        /// episodes: it is not a marker and the plugin must still add a CreditsStart.
        /// For movies (which never get a CreditsStart) a late "Credits" chapter is accepted.
        /// </summary>
        public bool HasCreditsMarker(BaseItem item)
        {
            try
            {
                var chapters = _itemRepository.GetChapters(item);
                if (chapters == null || chapters.Count == 0) return false;

                if (chapters.Any(c => c.MarkerType == MarkerType.CreditsStart)) return true;

                if (!(item is Episode))
                {
                    long rt = item.RunTimeTicks ?? 0;
                    return chapters.Any(c => c.MarkerType == MarkerType.Chapter
                        && string.Equals(c.Name, MarkerName, StringComparison.Ordinal)
                        && (rt <= 0 || c.StartPositionTicks >= (long)(rt * 0.55)));
                }

                return false;
            }
            catch (Exception ex)
            {
                _log.ErrorException("CreditsMarker: GetChapters failed for '{0}'", ex, Describe(item));
                return false;
            }
        }

        /// <summary>Existing CreditsStart position (seconds) for an item, or null.</summary>
        public double? GetCreditsSeconds(BaseItem item)
        {
            try
            {
                var chapters = _itemRepository.GetChapters(item);
                var c = chapters?.FirstOrDefault(x => x.MarkerType == MarkerType.CreditsStart);
                if (c != null) return c.StartPositionTicks / (double)TicksPerSecond;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// If the file has an embedded chapter that marks the start of the end credits
        /// ("Credits", "End Credits", "Ending", "Outro", "Dub credits", "Créditos"...), return
        /// its position in seconds - clamped just inside the runtime. This is free and more
        /// accurate than any analysis. Returns null if there is no such chapter in the
        /// credits zone (78%..108% of runtime; some containers time chapters past the stream
        /// duration).
        /// </summary>
        public double? NativeCreditsSeconds(BaseItem item)
        {
            List<ChapterInfo> chapters;
            try { chapters = _itemRepository.GetChapters(item); }
            catch { return null; }
            if (chapters == null || chapters.Count == 0) return null;

            long rt = item.RunTimeTicks ?? 0;
            if (rt <= 0) return null;
            double rtSec = rt / (double)TicksPerSecond;
            double lo = rtSec * 0.78, hi = rtSec * 1.08;

            double? best = null;
            foreach (var c in chapters)
            {
                if (c.MarkerType != MarkerType.Chapter) continue;
                if (!LooksLikeEndCredits(c.Name)) continue;
                double s = c.StartPositionTicks / (double)TicksPerSecond;
                if (s < lo || s > hi) continue;
                if (best == null || s < best.Value) best = s;   // earliest credits-named chapter in the zone
            }
            if (best == null) return null;
            return Math.Min(best.Value, rtSec * 0.985);
        }

        private static readonly HashSet<string> EndCreditsNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "credits", "credit", "end credits", "endcredits", "end credit",
            "closing credits", "closing credit", "dub credits", "dub credit",
            "credits start", "credits roll", "credit roll", "credits roll credits",
            "créditos", "creditos", "ending", "ending credits", "end credits scene",
            "outro", "outro credits", "roll credits", "final credits"
        };

        /// <summary>
        /// Does a chapter name look like the START of the end credits? Deliberately strict -
        /// it must essentially BE a credits label, not merely contain the word "credit"
        /// (episodes with per-scene chapters have names like "..., Opening Credits" or
        /// "Matthews Takes the Credit"). "Opening Credits" is excluded here and, in any case,
        /// the caller only looks in the last fifth of the runtime.
        /// </summary>
        public static bool LooksLikeEndCredits(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var n = name.Trim().ToLowerInvariant();

            // strip a leading "12. " / "12) " / "12 - " index
            int k = 0;
            while (k < n.Length && char.IsDigit(n[k])) k++;
            if (k > 0 && k < n.Length)
            {
                int j = k;
                while (j < n.Length && (n[j] == '.' || n[j] == ')' || n[j] == '-' || n[j] == '–' || n[j] == ' ')) j++;
                if (j > k) n = n.Substring(j).Trim();
            }

            // anime ED (ending theme) chapters: "ed", "ed 01 - ...", "ed01 ..."
            if (n == "ed" || n.StartsWith("ed ") || n.StartsWith("ed-")
                || n.StartsWith("ed0") || n.StartsWith("ed1") || n.StartsWith("ed2"))
                return true;

            return EndCreditsNames.Contains(n);
        }

        public void SaveMarker(BaseItem item, double seconds, bool isEpisode, bool alsoVisibleChapter)
        {
            var original = _itemRepository.GetChapters(item) ?? new List<ChapterInfo>();
            long ticks = (long)Math.Round(seconds * TicksPerSecond);

            // Keep everything that isn't ours: intro markers, the file's real chapters -
            // INCLUDING a release group's own "Credits" chapter, unless it sits right where
            // our marker goes (then it's our own previous write / a duplicate).
            var keep = original
                .Where(c => c.MarkerType != MarkerType.CreditsStart
                            && !(c.MarkerType == MarkerType.Chapter
                                 && string.Equals(c.Name, MarkerName, StringComparison.Ordinal)
                                 && Math.Abs(c.StartPositionTicks - ticks) <= OwnMarkerToleranceTicks))
                .ToList();

            var chapters = new List<ChapterInfo>(keep)
            {
                new ChapterInfo { StartPositionTicks = ticks, MarkerType = MarkerType.CreditsStart, Name = MarkerName }
            };

            // A visible tick on the seek bar - but only if there isn't already a credits-ish
            // chapter near that spot (the file's own, or one we wrote before).
            bool visibleNearby = keep.Any(c => c.MarkerType == MarkerType.Chapter
                && LooksLikeEndCredits(c.Name)
                && Math.Abs(c.StartPositionTicks - ticks) <= OwnMarkerToleranceTicks);

            if (isEpisode)
            {
                if (alsoVisibleChapter && !visibleNearby)
                    chapters.Add(new ChapterInfo { StartPositionTicks = ticks, MarkerType = MarkerType.Chapter, Name = MarkerName });
            }
            else
            {
                if (!visibleNearby)
                    chapters.Add(new ChapterInfo { StartPositionTicks = ticks, MarkerType = MarkerType.Chapter, Name = MarkerName });
            }

            chapters = chapters.OrderBy(c => c.StartPositionTicks).ThenBy(c => (int)c.MarkerType).ToList();
            for (int i = 0; i < chapters.Count; i++)
            {
                chapters[i].ChapterIndex = i;
            }

            // Safety net. Emby's SaveChapters replaces the whole list, so a bug here could wipe
            // an intro marker or a real chapter. If anything that was already there is missing
            // from the rebuilt list, don't write at all.
            foreach (var c in keep)
            {
                bool stillThere = chapters.Any(x => x.MarkerType == c.MarkerType
                                                    && x.StartPositionTicks == c.StartPositionTicks
                                                    && string.Equals(x.Name, c.Name, StringComparison.Ordinal));
                if (!stillThere)
                {
                    _log.Error("CreditsMarker: '{0}' - write aborted, it would drop an existing marker ({1} @ {2} ticks). Item left untouched.",
                        Describe(item), c.MarkerType, c.StartPositionTicks);
                    return;
                }
            }

            _itemRepository.SaveChapters(item.InternalId, chapters);
        }

        public static string Describe(BaseItem item)
        {
            var ep = item as Episode;
            if (ep != null && !string.IsNullOrEmpty(ep.SeriesName))
            {
                return ep.SeriesName + " - " + item.Name;
            }
            return item.Name;
        }
    }
}
