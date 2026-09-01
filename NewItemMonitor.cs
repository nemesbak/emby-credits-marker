// SPDX-License-Identifier: MIT
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;

namespace Emby.CreditsMarker
{
    /// <summary>
    /// Analyses newly-added episodes shortly after they appear, so a new episode is
    /// skippable within minutes instead of waiting for the nightly task.
    ///
    /// Opt-in via <see cref="PluginOptions.AnalyzeNewEpisodes"/>. Conservative by design:
    ///   - waits <see cref="PluginOptions.NewEpisodeDelayMinutes"/> after an episode is
    ///     added (so a full-season import settles first),
    ///   - processes a small batch per tick, one file at a time, with a pause between,
    ///   - if the series already has a consistent credits % on its other episodes it
    ///     applies that with no ffmpeg at all.
    /// When the option is off it does nothing and drops anything queued.
    /// </summary>
    public class NewItemMonitor : IServerEntryPoint, IDisposable
    {
        private const long TicksPerSecond = 10_000_000L;
        private const int TickSeconds = 300;
        private const int MaxPerTick = 12;

        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;
        private readonly IFfmpegManager _ffmpegManager;
        private readonly ILogger _log;
        private readonly MarkerWriter _markers;

        private readonly ConcurrentDictionary<long, DateTime> _pending = new ConcurrentDictionary<long, DateTime>();
        private Timer _timer;
        private int _busy;

        public NewItemMonitor(ILibraryManager libraryManager, IItemRepository itemRepository,
            IFfmpegManager ffmpegManager, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _ffmpegManager = ffmpegManager;
            _log = logManager.GetLogger("CreditsMarker");
            _markers = new MarkerWriter(_itemRepository, _log);
        }

        public void Run()
        {
            _libraryManager.ItemAdded += OnItemAdded;
            _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(TickSeconds), TimeSpan.FromSeconds(TickSeconds));
            _log.Info("CreditsMarker: new-episode monitor started.");
        }

        public void Dispose()
        {
            try { _libraryManager.ItemAdded -= OnItemAdded; } catch { }
            var t = _timer;
            _timer = null;
            t?.Dispose();
        }

        private void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            try
            {
                var ep = e.Item as Episode;
                if (ep == null || ep.InternalId == 0) return;
                // store only the id: the Episode object here is often pre-analysis
                // (no RunTimeTicks yet) - we re-fetch it fresh when the timer fires.
                _pending[ep.InternalId] = DateTime.UtcNow;

                if (_pending.Count > 5000)
                {
                    foreach (var k in _pending.OrderBy(kv => kv.Value).Take(1500).Select(kv => kv.Key).ToList())
                        _pending.TryRemove(k, out _);
                }
            }
            catch (Exception ex)
            {
                _log.ErrorException("CreditsMarker: new-episode enqueue failed", ex);
            }
        }

        private void Tick()
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            try
            {
                var opts = Plugin.Instance?.GetConfiguredOptions() ?? new PluginOptions();
                if (!opts.AnalyzeNewEpisodes || !opts.ProcessEpisodes)
                {
                    if (!_pending.IsEmpty) _pending.Clear();
                    return;
                }

                int delayMin = Math.Max(2, opts.NewEpisodeDelayMinutes);
                var cutoff = DateTime.UtcNow.AddMinutes(-delayMin);
                var due = _pending
                    .Where(kv => kv.Value <= cutoff)
                    .OrderBy(kv => kv.Value)
                    .Take(MaxPerTick)
                    .Select(kv => kv.Key)
                    .ToList();
                if (due.Count == 0) return;

                var allowedPaths = ResolveAllowedPaths(opts);

                // one library read for the whole tick, to find already-marked siblings
                Dictionary<long, List<Episode>> bySeries = null;
                var consensusCache = new Dictionary<long, double?>();

                string ffmpeg = null;
                CreditsDetector detector = null;

                int marked = 0, skipped = 0, failed = 0;

                foreach (var id in due)
                {
                    _pending.TryRemove(id, out _);
                    // re-fetch fresh: at ItemAdded time the episode often has no media info yet
                    var ep = _libraryManager.GetItemById(id) as Episode;
                    if (ep == null) { skipped++; continue; }
                    try
                    {
                        if (ep.IsVirtualItem || !ep.IsFileProtocol || string.IsNullOrEmpty(ep.Path)) { skipped++; continue; }
                        long rtt = ep.RunTimeTicks ?? 0;
                        if (rtt <= 60L * TicksPerSecond) { skipped++; continue; }  // no media info (yet) - nightly task will get it
                        if (allowedPaths.Count > 0 &&
                            !allowedPaths.Any(x => ep.Path.StartsWith(x, StringComparison.OrdinalIgnoreCase))) { skipped++; continue; }
                        if (_markers.HasCreditsMarker(ep)) { skipped++; continue; }

                        double rt = rtt / (double)TicksPerSecond;

                        // 0) embedded "Credits" chapter -> use it, no analysis
                        var nativeSec = _markers.NativeCreditsSeconds(ep);
                        if (nativeSec.HasValue)
                        {
                            _markers.SaveMarker(ep, nativeSec.Value, true, opts.AlsoVisibleChapterOnEpisodes);
                            marked++;
                            _log.Info("CreditsMarker: new episode '{0}' -> {1} (embedded chapter, no analysis).",
                                MarkerWriter.Describe(ep), FormatTime(nativeSec.Value));
                            continue;
                        }

                        // 1) reuse a series consensus if it has one
                        double? consensus;
                        long sid = ep.SeriesId;
                        if (!consensusCache.TryGetValue(sid, out consensus))
                        {
                            if (bySeries == null) bySeries = LoadEpisodesBySeries();
                            consensus = SeriesConsensusFraction(sid, bySeries);
                            consensusCache[sid] = consensus;
                        }

                        if (consensus.HasValue)
                        {
                            _markers.SaveMarker(ep, rt * consensus.Value, true, opts.AlsoVisibleChapterOnEpisodes);
                            marked++;
                            _log.Info("CreditsMarker: new episode '{0}' -> {1:0.0}% (series consensus, no analysis).",
                                MarkerWriter.Describe(ep), 100 * consensus.Value);
                            continue;
                        }

                        // 2) analyse this file (black + silence)
                        if (detector == null)
                        {
                            ffmpeg = GetFfmpegPath();
                            detector = new CreditsDetector(ffmpeg, _log);
                        }
                        var det = detector.Detect(ep.Path, rt, opts, CancellationToken.None);
                        if (det.Source != "none")
                        {
                            _markers.SaveMarker(ep, det.CreditsStartSeconds, true, opts.AlsoVisibleChapterOnEpisodes);
                            marked++;
                            _log.Info("CreditsMarker: new episode '{0}' -> {1} ({2}).",
                                MarkerWriter.Describe(ep), FormatTime(det.CreditsStartSeconds), det.Source);
                        }
                        else
                        {
                            skipped++;
                            _log.Debug("CreditsMarker: new episode '{0}' - nothing found, left for the nightly task ({1}).",
                                MarkerWriter.Describe(ep), det.Detail);
                        }

                        Thread.Sleep(500); // be gentle on a live server
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _log.ErrorException("CreditsMarker: new-episode analysis failed for '{0}'", ex, MarkerWriter.Describe(ep));
                    }
                }

                if (marked + skipped + failed > 0)
                {
                    _log.Info("CreditsMarker: new-episode tick done. marked={0} skipped={1} failed={2} still-pending={3}",
                        marked, skipped, failed, _pending.Count);
                }
            }
            catch (Exception ex)
            {
                _log.ErrorException("CreditsMarker: new-episode monitor tick error", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        private Dictionary<long, List<Episode>> LoadEpisodesBySeries()
        {
            var map = new Dictionary<long, List<Episode>>();
            try
            {
                var all = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Recursive = true,
                    IncludeItemTypes = new[] { "Episode" }
                });
                foreach (var it in all)
                {
                    var ep = it as Episode;
                    if (ep == null || ep.SeriesId == 0) continue;
                    if (!map.TryGetValue(ep.SeriesId, out var list))
                    {
                        list = new List<Episode>();
                        map[ep.SeriesId] = list;
                    }
                    list.Add(ep);
                }
            }
            catch (Exception ex)
            {
                _log.ErrorException("CreditsMarker: could not list episodes for consensus", ex);
            }
            return map;
        }

        /// <summary>Median credits fraction of a series' already-marked episodes, if they agree.</summary>
        private double? SeriesConsensusFraction(long seriesId, Dictionary<long, List<Episode>> bySeries)
        {
            if (seriesId == 0 || !bySeries.TryGetValue(seriesId, out var eps)) return null;

            var fracs = new List<double>();
            foreach (var s in eps)
            {
                long rtt = s.RunTimeTicks ?? 0;
                if (rtt <= 0) continue;
                var c = _markers.GetCreditsSeconds(s);
                if (!c.HasValue) continue;
                fracs.Add(c.Value / (rtt / (double)TicksPerSecond));
            }
            if (fracs.Count < 3) return null;

            fracs.Sort();
            // densest cluster within +/-1.5%
            int bestStart = 0, bestLen = 0;
            for (int i = 0; i < fracs.Count; i++)
            {
                int j = i;
                while (j < fracs.Count && fracs[j] - fracs[i] <= 0.015) j++;
                if (j - i > bestLen) { bestLen = j - i; bestStart = i; }
            }
            // the cluster must be a clear majority of the marked episodes, so we don't
            // propagate a shaky per-episode guess (e.g. anime with a changing ED).
            int needed = Math.Max(3, (int)Math.Ceiling(fracs.Count * 0.6));
            if (bestLen < needed) return null;

            var cluster = fracs.GetRange(bestStart, bestLen);
            double median = cluster[cluster.Count / 2];
            if (median < 0.75 || median > 0.995) return null;
            return median;
        }

        private List<string> ResolveAllowedPaths(PluginOptions opts)
        {
            var names = (opts.LibraryNames ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var paths = new List<string>();
            if (names.Count == 0) return paths;
            try
            {
                foreach (var vf in _libraryManager.GetVirtualFolders())
                {
                    if (vf.Locations == null) continue;
                    if (names.Any(a => string.Equals(a, vf.Name, StringComparison.OrdinalIgnoreCase)))
                        paths.AddRange(vf.Locations.Where(l => !string.IsNullOrEmpty(l)));
                }
            }
            catch (Exception ex) { _log.ErrorException("CreditsMarker: could not resolve library names", ex); }
            return paths;
        }

        private string GetFfmpegPath() => _ffmpegManager.FfmpegConfiguration.EncoderPath;

        private static string FormatTime(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0:0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
        }
    }
}
