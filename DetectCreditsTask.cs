// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.CreditsMarker
{
    public class DetectCreditsTask : IScheduledTask, IConfigurableScheduledTask
    {
        private const long TicksPerSecond = 10_000_000L;

        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;
        private readonly IFfmpegManager _ffmpegManager;
        private readonly ILogger _log;
        private readonly MarkerWriter _markers;

        public DetectCreditsTask(
            ILibraryManager libraryManager,
            IItemRepository itemRepository,
            IFfmpegManager ffmpegManager,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _ffmpegManager = ffmpegManager;
            _log = logManager.GetLogger("CreditsMarker");
            _markers = new MarkerWriter(_itemRepository, _log);
        }

        public string Name => "Detect end credits";
        public string Key => "DetectEndCredits";
        public string Description => "Analyses video files for the start of the end credits and stores a CreditsStart marker.";
        public string Category => "Library";

        public bool IsHidden => false;
        public bool IsEnabled => true;
        public bool IsLogged => true;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerDaily,
                    TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
                }
            };
        }

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            return Task.Run(() => Run(cancellationToken, progress), cancellationToken);
        }

        private void Run(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var options = Plugin.Instance?.GetConfiguredOptions() ?? new PluginOptions();
            var runClock = System.Diagnostics.Stopwatch.StartNew();
            double maxHours = Math.Max(0, options.MaxRunHours);
            bool OutOfTime() => maxHours > 0 && runClock.Elapsed.TotalHours >= maxHours;

            var includeTypes = new List<string>();
            if (options.ProcessEpisodes) includeTypes.Add("Episode");
            if (options.ProcessMovies) includeTypes.Add("Movie");
            if (includeTypes.Count == 0)
            {
                _log.Info("CreditsMarker: nothing enabled (episodes/movies both off).");
                progress.Report(100);
                return;
            }

            var allowedLibraries = (options.LibraryNames ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            var query = new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = includeTypes.ToArray(),
                MediaTypes = new[] { "Video" }
            };

            BaseItem[] items;
            try
            {
                items = _libraryManager.GetItemList(query);
            }
            catch (Exception ex)
            {
                _log.ErrorException("CreditsMarker: failed to query library", ex);
                progress.Report(100);
                return;
            }

            // resolve allowed library names -> their on-disk locations (path-prefix match is
            // robust across mixed libraries where CollectionFolders can be unreliable)
            var allowedPaths = new List<string>();
            if (allowedLibraries.Count > 0)
            {
                try
                {
                    foreach (var vf in _libraryManager.GetVirtualFolders())
                    {
                        if (vf.Locations == null) continue;
                        if (allowedLibraries.Any(a => string.Equals(a, vf.Name, StringComparison.OrdinalIgnoreCase)))
                            allowedPaths.AddRange(vf.Locations.Where(l => !string.IsNullOrEmpty(l)));
                    }
                }
                catch (Exception ex) { _log.ErrorException("CreditsMarker: could not resolve library names", ex); }
                _log.Info("CreditsMarker: library filter -> {0}", string.Join(", ", allowedPaths));
            }

            var candidates = items
                .Where(i => i != null
                            && !i.IsVirtualItem
                            && i.IsFileProtocol
                            && !string.IsNullOrEmpty(i.Path)
                            && i.RunTimeTicks.HasValue
                            && i.RunTimeTicks.Value > 60L * TicksPerSecond)
                .Where(i => allowedPaths.Count == 0
                            || allowedPaths.Any(p => i.Path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            _log.Info("CreditsMarker: {0} item(s) to check.", candidates.Count);
            if (candidates.Count == 0)
            {
                progress.Report(100);
                return;
            }

            var ffmpeg = GetFfmpegPath();
            var detector = new CreditsDetector(ffmpeg, _log);
            int done = 0, blackMarked = 0, nativeMarked = 0, skipped = 0, failed = 0, analysed = 0;
            int maxAnalyse = options.MaxItemsPerRun;

            // pass 1: chapters are read ONCE per item, then reused for the "already marked?"
            // check, the embedded-chapter check and the write. Movies are saved immediately;
            // episodes are collected so we can reconcile each series as a whole afterwards.
            var epResults = new List<EpResult>();

            foreach (var item in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (OutOfTime())
                {
                    _log.Info("CreditsMarker: hit the {0}h run cap after {1} item(s) - stopping, the rest resumes next run.",
                        maxHours, done);
                    break;
                }
                done++;
                progress.Report(done * 80.0 / candidates.Count);
                try
                {
                    var ep = item as Episode;
                    var chapters = _itemRepository.GetChapters(item);   // the only DB read for this item

                    if (!options.Redetect && _markers.HasCreditsMarker(item, chapters)) { skipped++; continue; }

                    double? native = _markers.NativeCreditsSeconds(item, chapters);
                    if (native.HasValue)
                    {
                        _markers.SaveMarker(item, native.Value, ep != null,
                            ep != null && options.AlsoVisibleChapterOnEpisodes, chapters);
                        nativeMarked++;
                        _log.Info("CreditsMarker: '{0}' -> {1} (embedded chapter, no analysis).",
                            DisplayName(item), FormatTime(native.Value));
                        if (maxAnalyse > 0 && ++analysed >= maxAnalyse) { _log.Info("CreditsMarker: reached the {0}-item cap for this run.", maxAnalyse); break; }
                        continue;
                    }

                    double rt = item.RunTimeTicks.Value / (double)TicksPerSecond;
                    var result = detector.Detect(item.Path, rt, options, cancellationToken);

                    if (ep == null) // movie
                    {
                        if (result.Source != "none")
                        {
                            _markers.SaveMarker(item, result.CreditsStartSeconds, false, false, chapters);
                            blackMarked++;
                            _log.Info("CreditsMarker: '{0}' -> Chapter at {1} ({2})",
                                DisplayName(item), FormatTime(result.CreditsStartSeconds), result.Detail);
                        }
                    }
                    else
                    {
                        epResults.Add(new EpResult
                        {
                            Ep = ep,
                            Runtime = rt,
                            BlackSec = result.Source == "none" ? (double?)null : result.CreditsStartSeconds
                        });
                    }
                    if (maxAnalyse > 0 && ++analysed >= maxAnalyse) { _log.Info("CreditsMarker: reached the {0}-item cap for this run.", maxAnalyse); break; }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failed++;
                    _log.ErrorException("CreditsMarker: error processing '{0}'", ex, DisplayName(item));
                }
            }

            // pass 2: reconcile each series. Where black detection is consistent, use it (and
            // pull outliers onto the consensus). Where it isn't, hand the series to fingerprinting.
            int epMarked = 0, fpMarked = 0;
            var fpCandidates = new List<List<EpResult>>();

            foreach (var g in epResults.GroupBy(r => r.Ep.SeriesId != 0
                                                    ? "id:" + r.Ep.SeriesId.ToString(CultureInfo.InvariantCulture)
                                                    : "name:" + (r.Ep.SeriesName ?? "?")))
            {
                var eps = g.ToList();
                var fracs = eps.Where(e => e.BlackSec.HasValue)
                               .Select(e => e.BlackSec.Value / e.Runtime).OrderBy(f => f).ToList();
                string name = eps[0].Ep.SeriesName ?? ("series " + g.Key);

                if (eps.Count >= 4 && fracs.Count >= 2)
                {
                    var cl = DensestCluster(fracs, 0.03);
                    if (cl.Count >= Math.Max(2, (int)Math.Ceiling(eps.Count * 0.55)))
                    {
                        double med = cl[cl.Count / 2];
                        int fixedUp = 0;
                        foreach (var e in eps)
                        {
                            double f = e.BlackSec.HasValue ? e.BlackSec.Value / e.Runtime : -1;
                            double use = (f >= cl[0] - 1e-9 && f <= cl[cl.Count - 1] + 1e-9) ? f : med;
                            if (!e.BlackSec.HasValue || Math.Abs(f - use) > 1e-6) fixedUp++;
                            _markers.SaveMarker(e.Ep, e.Runtime * use, true, options.AlsoVisibleChapterOnEpisodes);
                            epMarked++;
                        }
                        _log.Info("CreditsMarker: '{0}' -> black detection consistent at ~{1:0.0}% ({2} eps, {3} snapped to consensus).",
                            name, 100 * med, eps.Count, fixedUp);
                        continue;
                    }
                }

                // black detection unreliable for this series
                if (options.EnableFingerprintFallback && eps.Count >= 3)
                {
                    fpCandidates.Add(eps);
                }
                else
                {
                    // small series or fingerprint disabled: keep whatever black detection gave
                    foreach (var e in eps.Where(e => e.BlackSec.HasValue))
                    {
                        _markers.SaveMarker(e.Ep, e.BlackSec.Value, true, options.AlsoVisibleChapterOnEpisodes);
                        epMarked++;
                    }
                }
            }

            if (fpCandidates.Count > 0 && !OutOfTime())
            {
                try
                {
                    fpMarked = FingerprintPass(fpCandidates, options, ffmpeg, cancellationToken, progress);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _log.ErrorException("CreditsMarker: fingerprint pass failed", ex); }
            }

            _log.Info("CreditsMarker: done. native-marked={0} black-marked={1} episode-marked={2} fingerprint-marked={3} already-had-one={4} failed={5}",
                nativeMarked, blackMarked, epMarked, fpMarked, skipped, failed);
            progress.Report(100);
        }

        /// <summary>
        /// For series where black detection is unreliable (credits over content, e.g. anime),
        /// fingerprint the tail of a few episodes, find the recurring end-theme, and apply its
        /// start position (as a % of runtime) to EVERY episode of that series.
        /// </summary>
        private int FingerprintPass(List<List<EpResult>> series, PluginOptions options, string ffmpeg,
            CancellationToken ct, IProgress<double> progress)
        {
            var fp = new OutroFingerprint(ffmpeg, _log);
            if (!fp.ChromaprintAvailable(ct))
            {
                _log.Info("CreditsMarker: {0} series need fingerprinting but chromaprint isn't available.", series.Count);
                return 0;
            }

            _log.Info("CreditsMarker: fingerprint pass over {0} series ({1} episodes).",
                series.Count, series.Sum(s => s.Count));

            int tailPct = Math.Min(45, Math.Max(10, options.FingerprintTailPercent));
            int minRunSec = Math.Max(8, options.FingerprintMinRunSeconds);
            int timeout = Math.Max(120, options.FfmpegTimeoutSeconds);
            int total = series.Count, seriesDone = 0, markedTotal = 0;

            foreach (var group in series)
            {
                ct.ThrowIfCancellationRequested();
                var eps = group.OrderBy(e => e.Ep.ParentIndexNumber ?? 0).ThenBy(e => e.Ep.IndexNumber ?? 0)
                               .Select(e => e.Ep).ToList();
                var refs = eps.Take(3).ToList();
                var seriesName = refs[0].SeriesName ?? ("series " + (refs[0].SeriesId));

                // fingerprint the tail of the reference episodes.
                // fps is derived from the actual frame count, not assumed.
                var prints = new List<FpRef>();
                foreach (var ep in refs)
                {
                    try
                    {
                        double rt = ep.RunTimeTicks.Value / (double)TicksPerSecond;
                        double tailStart = rt * (100 - tailPct) / 100.0;
                        double dur = rt - tailStart;
                        var pr = fp.Fingerprint(ep.Path, tailStart, dur + 5, timeout, ct);
                        // ffmpeg stops at EOF, so the fingerprint covers ~dur seconds, not dur+5.
                        if (pr.Length > 200)
                            prints.Add(new FpRef { Ep = ep, Fp = pr, TailStart = tailStart, Runtime = rt, Fps = pr.Length / Math.Max(1.0, dur) });
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { _log.Debug("CreditsMarker: fp extract failed for '{0}': {1}", DisplayName(ep), ex.Message); }
                }

                if (prints.Count < 2)
                {
                    _log.Info("CreditsMarker: '{0}' - could not fingerprint enough episodes ({1}).", seriesName, prints.Count);
                    seriesDone++; progress.Report(90 + seriesDone * 10.0 / total); continue;
                }
                _log.Debug("CreditsMarker: '{0}' fingerprinted {1} refs, {2} frames each.",
                    seriesName, prints.Count, prints[0].Fp.Length);

                // pairwise: collect outro-start as a fraction of runtime
                var fractions = new List<double>();
                for (int i = 0; i < prints.Count; i++)
                for (int j = i + 1; j < prints.Count; j++)
                {
                    var m = OutroFingerprint.LongestCommonRun(prints[i].Fp, prints[j].Fp, 3);
                    if (m.Length / prints[i].Fps < minRunSec) continue;
                    double ai = prints[i].TailStart + m.OffsetA / prints[i].Fps;
                    double aj = prints[j].TailStart + m.OffsetB / prints[j].Fps;
                    fractions.Add(ai / prints[i].Runtime);
                    fractions.Add(aj / prints[j].Runtime);
                }

                fractions.Sort();
                if (fractions.Count < 3)
                {
                    _log.Info("CreditsMarker: '{0}' - no recurring outro found ({1}).",
                        seriesName, string.Join(",", fractions.Select(f => f.ToString("0.00"))));
                    seriesDone++; progress.Report(90 + seriesDone * 10.0 / total); continue;
                }

                // consensus = the densest cluster of measurements within a +/-2.5% window.
                // (a single pair can match a shared BGM segment rather than the ED; the majority wins.)
                var cluster = DensestCluster(fractions, 0.025);
                double median = cluster.Count > 0 ? cluster[cluster.Count / 2] : 0;
                if (cluster.Count < 3 || median < 0.78 || median > 0.995)
                {
                    _log.Info("CreditsMarker: '{0}' - outro measurements unreliable (cluster {1}/{2}, median {3:0.000}): {4}",
                        seriesName, cluster.Count, fractions.Count, median,
                        string.Join(" ", fractions.Select(f => f.ToString("0.00"))));
                    seriesDone++; progress.Report(90 + seriesDone * 10.0 / total); continue;
                }

                // apply the median fraction to every unmarked episode of this series
                int marked = 0;
                foreach (var ep in eps)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        double rt = ep.RunTimeTicks.Value / (double)TicksPerSecond;
                        double creditsSec = rt * median;
                        _markers.SaveMarker(ep, creditsSec, true, options.AlsoVisibleChapterOnEpisodes);
                        marked++;
                    }
                    catch (Exception ex) { _log.Debug("CreditsMarker: fp save failed for '{0}': {1}", DisplayName(ep), ex.Message); }
                }
                markedTotal += marked;
                _log.Info("CreditsMarker: '{0}' -> outro at {1:0.0}% of runtime (fingerprint, {2} refs) -> {3} episode(s).",
                    seriesName, 100 * median, prints.Count, marked);

                seriesDone++;
                progress.Report(90 + seriesDone * 10.0 / total);
            }

            return markedTotal;
        }

        /// <summary>Largest subset of sorted values that fits inside a window of the given width.</summary>
        internal static List<double> DensestCluster(List<double> sorted, double window)
        {
            int bestStart = 0, bestLen = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                int j = i;
                while (j < sorted.Count && sorted[j] - sorted[i] <= window) j++;
                if (j - i > bestLen) { bestLen = j - i; bestStart = i; }
            }
            return sorted.GetRange(bestStart, bestLen);
        }

        private class EpResult
        {
            public Episode Ep;
            public double Runtime;
            public double? BlackSec;
        }

        private class FpRef
        {
            public Episode Ep;
            public uint[] Fp;
            public double TailStart;
            public double Runtime;
            public double Fps;
        }

        private string GetFfmpegPath() => _ffmpegManager.FfmpegConfiguration.EncoderPath;

        private static string DisplayName(BaseItem item)
        {
            var ep = item as Episode;
            if (ep != null && !string.IsNullOrEmpty(ep.SeriesName))
            {
                return ep.SeriesName + " - " + item.Name;
            }
            return item.Name;
        }

        private static string FormatTime(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0:0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
        }
    }
}
