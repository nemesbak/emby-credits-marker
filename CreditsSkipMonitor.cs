// SPDX-License-Identifier: MIT
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Session;

namespace Emby.CreditsMarker
{
    /// <summary>
    /// Watches playback and, when an episode reaches its CreditsStart marker, tells the client
    /// to skip to the next episode. Server-side remote control, so it works on every client
    /// (web, iOS, Android TV, LG, Samsung...) and identically whether the stream is
    /// direct-played or transcoded - it only ever looks at playback position vs. the marker.
    /// Opt-in via PluginOptions.AutoSkipCredits.
    ///
    /// Cheap by design: when AutoSkipCredits is off the progress handler returns immediately.
    /// When on, it does at most ONE DB read per (device, episode) pair, and nothing at all
    /// until playback passes the 55% mark.
    ///
    /// Everything that de-dupes or rate-limits is keyed by DEVICE, not by PlaySessionId.
    /// Emby for iOS mints a fresh PlaySessionId on every progress report, which would defeat
    /// any PlaySessionId-keyed guard. DeviceId is stable, so the guards below actually hold.
    ///
    /// Runaway protection (the plugin never causes a loop - but it does its best to survive
    /// and stop a client that has got itself stuck, e.g. a player that can't decode the file
    /// and auto-advances through the whole series):
    ///   - one skip per device per <see cref="SessionCooldownSeconds"/>;
    ///   - before skipping, the NEXT queue item must resolve to an Episode (a corrupted queue
    ///     that mixes music tracks and episodes is left alone);
    ///   - a device racking up quick playback failures (start -> immediate stop at ~0, not
    ///     completed) is locked out of auto-skip;
    ///   - if those failures march through DIFFERENT items (a queue-advance runaway) the
    ///     plugin sends escalating Stop commands to break it, and if the client ignores them
    ///     it gives up loudly and disables auto-skip for that device for a while.
    /// </summary>
    public class CreditsSkipMonitor : IServerEntryPoint, IDisposable
    {
        private const long TicksPerSecond = 10_000_000L;
        private const long NoMarker = -1L;
        private const int SessionCooldownSeconds = 45;

        // soft guard: raw start rate (works even if stop events are missed)
        private const int RunawayStartsThreshold = 5;
        private const int RunawayWindowSeconds = 90;
        private const int RunawayLockoutSeconds = 300;

        // precise guard: quick playback failures (start -> stop at ~0, not played to completion)
        private const double QuickFailMaxPositionSeconds = 6.0;
        private const double QuickFailMaxLifetimeSeconds = 8.0;
        private const int QuickFailWindowSeconds = 60;
        private const int QuickFailLockoutThreshold = 2;

        // escalating loop-break
        private const int LoopBreakMaxAttempts = 4;
        private const int LoopBreakRetrySeconds = 12;
        private const int LoopBreakResetSeconds = 90;
        private const int HardLockoutSeconds = 900;

        private readonly ISessionManager _sessionManager;
        private readonly IItemRepository _itemRepository;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _log;

        // key = deviceKey + "|" + episode.InternalId
        private readonly ConcurrentDictionary<string, long> _creditsCache = new ConcurrentDictionary<string, long>();
        private readonly ConcurrentDictionary<string, byte> _actioned = new ConcurrentDictionary<string, byte>();

        // per deviceKey
        private readonly ConcurrentDictionary<string, DateTime> _lastFire = new ConcurrentDictionary<string, DateTime>();
        private readonly ConcurrentDictionary<string, DateTime> _lastSkipSent = new ConcurrentDictionary<string, DateTime>();
        private readonly ConcurrentDictionary<string, Queue<DateTime>> _recentStarts = new ConcurrentDictionary<string, Queue<DateTime>>();
        private readonly ConcurrentDictionary<string, StartInfo> _lastStart = new ConcurrentDictionary<string, StartInfo>();
        private readonly ConcurrentDictionary<string, FailHistory> _quickFails = new ConcurrentDictionary<string, FailHistory>();
        private readonly ConcurrentDictionary<string, DateTime> _lockout = new ConcurrentDictionary<string, DateTime>();
        private readonly ConcurrentDictionary<string, Breaker> _breakers = new ConcurrentDictionary<string, Breaker>();

        // cached options (BasePluginSimpleUI.GetOptions may touch disk)
        private PluginOptions _opts;
        private string[] _optUsers = new string[0];
        private DateTime _optsAt = DateTime.MinValue;

        private struct StartInfo
        {
            public DateTime At;
            public long ItemId;
        }

        private sealed class FailHistory
        {
            public readonly Queue<DateTime> Times = new Queue<DateTime>();
            public long LastItemId;
            public bool Advancing;
        }

        private sealed class Breaker
        {
            public DateTime LastAttemptAt;
            public int Attempts;
            public bool GaveUp;
        }

        public CreditsSkipMonitor(
            ISessionManager sessionManager,
            IItemRepository itemRepository,
            ILibraryManager libraryManager,
            ILogManager logManager)
        {
            _sessionManager = sessionManager;
            _itemRepository = itemRepository;
            _libraryManager = libraryManager;
            _log = logManager.GetLogger("CreditsMarker");
        }

        public void Run()
        {
            _sessionManager.PlaybackStart += OnPlaybackStart;
            _sessionManager.PlaybackProgress += OnPlaybackProgress;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            _log.Info("CreditsMarker: auto-skip monitor started.");
        }

        public void Dispose()
        {
            _sessionManager.PlaybackStart -= OnPlaybackStart;
            _sessionManager.PlaybackProgress -= OnPlaybackProgress;
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        }

        /// <summary>Stable per-device identity. DeviceId first, Session.Id only as a fallback.</summary>
        private static string DeviceKey(PlaybackProgressEventArgs e)
        {
            if (e == null) return null;
            if (!string.IsNullOrEmpty(e.DeviceId)) return e.DeviceId;
            var s = e.Session;
            if (s == null) return null;
            if (!string.IsNullOrEmpty(s.DeviceId)) return s.DeviceId;
            return string.IsNullOrEmpty(s.Id) ? null : s.Id;
        }

        private PluginOptions GetOpts()
        {
            if ((DateTime.UtcNow - _optsAt).TotalSeconds < 30 && _opts != null) return _opts;
            _opts = Plugin.Instance?.GetConfiguredOptions() ?? new PluginOptions();
            _optUsers = (_opts.AutoSkipUsers ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            _optsAt = DateTime.UtcNow;
            return _opts;
        }

        private bool LockedOut(string dk, DateTime now)
            => _lockout.TryGetValue(dk, out var until) && until > now;

        private void LockOut(string dk, DateTime now, int seconds)
        {
            var until = now.AddSeconds(seconds);
            _lockout.AddOrUpdate(dk, until, (_, cur) => cur > until ? cur : until);
        }

        private void OnPlaybackStart(object sender, PlaybackProgressEventArgs e)
        {
            try
            {
                var dk = DeviceKey(e);
                if (string.IsNullOrEmpty(dk)) return;

                var now = DateTime.UtcNow;
                _lastStart[dk] = new StartInfo { At = now, ItemId = e.Item != null ? e.Item.InternalId : 0 };

                int inWindow;
                var q = _recentStarts.GetOrAdd(dk, _ => new Queue<DateTime>());
                lock (q)
                {
                    q.Enqueue(now);
                    while (q.Count > 0 && (now - q.Peek()).TotalSeconds > RunawayWindowSeconds) q.Dequeue();
                    inWindow = q.Count;
                }

                if (inWindow >= RunawayStartsThreshold && !LockedOut(dk, now))
                {
                    LockOut(dk, now, RunawayLockoutSeconds);
                    _log.Warn("CreditsMarker: device {0} ({1}) started {2} playbacks in {3}s - auto-skip locked out for {4}s.",
                        dk, e.Session != null ? e.Session.Client : "?", inWindow, RunawayWindowSeconds, RunawayLockoutSeconds);
                }
            }
            catch (Exception ex)
            {
                _log.ErrorException("CreditsMarker: OnPlaybackStart error", ex);
            }
        }

        private void OnPlaybackStopped(object sender, PlaybackStopEventArgs e)
        {
            try
            {
                var dk = DeviceKey(e);
                var now = DateTime.UtcNow;
                long stoppedItemId = e.Item != null ? e.Item.InternalId : 0;

                if (!string.IsNullOrEmpty(dk))
                {
                    if (stoppedItemId != 0)
                    {
                        var key = dk + "|" + stoppedItemId;
                        _creditsCache.TryRemove(key, out _);
                        _actioned.TryRemove(key, out _);
                    }

                    double pos = (e.PlaybackPositionTicks ?? 0L) / (double)TicksPerSecond;
                    bool notReallyPlayed = !e.PlayedToCompletion && pos <= QuickFailMaxPositionSeconds;
                    if (notReallyPlayed && _lastStart.TryGetValue(dk, out var st)
                        && (now - st.At).TotalSeconds <= QuickFailMaxLifetimeSeconds)
                    {
                        RecordQuickFail(e.Session, dk, now, stoppedItemId);
                    }
                }

                // belt and braces
                if (_creditsCache.Count > 500) _creditsCache.Clear();
                if (_actioned.Count > 500) _actioned.Clear();
                foreach (var k in _lockout.Where(kv => kv.Value < now).Select(kv => kv.Key).ToList())
                    _lockout.TryRemove(k, out _);
            }
            catch (Exception ex)
            {
                _log.ErrorException("CreditsMarker: OnPlaybackStopped error", ex);
            }
        }

        private void RecordQuickFail(SessionInfo session, string dk, DateTime now, long itemId)
        {
            var h = _quickFails.GetOrAdd(dk, _ => new FailHistory());
            int count;
            bool advancing;
            lock (h)
            {
                while (h.Times.Count > 0 && (now - h.Times.Peek()).TotalSeconds > QuickFailWindowSeconds) h.Times.Dequeue();
                if (h.Times.Count == 0)   // fresh burst - forget the previous one
                {
                    h.Advancing = false;
                    h.LastItemId = 0;
                }
                h.Times.Enqueue(now);
                if (itemId != 0 && h.LastItemId != 0 && itemId != h.LastItemId) h.Advancing = true;
                if (itemId != 0) h.LastItemId = itemId;
                count = h.Times.Count;
                advancing = h.Advancing;
            }

            // A failure right after our own NextTrack means our skip may have jumped onto an
            // item this client can't play. Don't wait for a pile-up.
            bool rightAfterOurSkip = _lastSkipSent.TryGetValue(dk, out var sent)
                && (now - sent).TotalSeconds <= QuickFailMaxLifetimeSeconds;

            if (count < QuickFailLockoutThreshold && !rightAfterOurSkip)
                return;

            if (!LockedOut(dk, now))
            {
                LockOut(dk, now, RunawayLockoutSeconds);
                _log.Warn("CreditsMarker: device {0} ({1}) had {2} quick playback failure(s) in {3}s (advancing={4}, afterOurSkip={5}) - auto-skip locked out for {6}s.",
                    dk, session != null ? session.Client : "?", count, QuickFailWindowSeconds, advancing, rightAfterOurSkip, RunawayLockoutSeconds);
            }

            // Only a queue-advance runaway is worth interrupting. A client retrying the SAME
            // file is its own problem and a Stop won't help.
            if (advancing)
                EscalateBreak(session, dk, now);
        }

        private void EscalateBreak(SessionInfo session, string dk, DateTime now)
        {
            if (session == null || string.IsNullOrEmpty(session.Id) || !session.SupportsRemoteControl) return;
            if (!GetOpts().BreakRunawayLoops) return;

            var b = _breakers.GetOrAdd(dk, _ => new Breaker());
            bool send = false;
            bool giveUpNow = false;
            int attempt = 0;
            lock (b)
            {
                if ((now - b.LastAttemptAt).TotalSeconds > LoopBreakResetSeconds)
                {
                    b.Attempts = 0;
                    b.GaveUp = false;
                }
                if (b.GaveUp) return;
                if (b.Attempts > 0 && (now - b.LastAttemptAt).TotalSeconds < LoopBreakRetrySeconds) return;

                if (b.Attempts >= LoopBreakMaxAttempts)
                {
                    b.GaveUp = true;
                    giveUpNow = true;
                }
                else
                {
                    b.Attempts++;
                    b.LastAttemptAt = now;
                    attempt = b.Attempts;
                    send = true;
                }
            }

            if (giveUpNow)
            {
                LockOut(dk, now, HardLockoutSeconds);
                _log.Error("CreditsMarker: device {0} ({1}) keeps failing playback and ignoring Stop after {2} attempts - auto-skip disabled for this device for {3} min. This client probably can't play the content it is being handed (codec / transcode setting) - check its settings.",
                    dk, session.Client, LoopBreakMaxAttempts, HardLockoutSeconds / 60);
                return;
            }

            if (!send) return;

            _log.Warn("CreditsMarker: device {0} ({1}) is advancing through a queue on failed playbacks - sending Stop (attempt {2}/{3}).",
                dk, session.Client, attempt, LoopBreakMaxAttempts);
            try
            {
                _sessionManager.SendPlaystateCommand(
                    null, session.Id,
                    new PlaystateRequest { Command = PlaystateCommand.Stop },
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.ErrorException("CreditsMarker: failed to send loop-breaking Stop", ex);
            }
        }

        /// <summary>
        /// True unless the client reported a play queue whose NEXT item resolves to something
        /// that isn't an episode. Unknown / unresolvable queues return true (clients that don't
        /// report a queue keep the old behaviour).
        /// </summary>
        private bool NextInQueueIsSkippable(SessionInfo s)
        {
            var q = s.NowPlayingQueue;
            if (q == null || q.Length == 0) return true;

            int idx = s.PlaylistIndex;
            if (idx < 0) return true;
            if (idx + 1 >= q.Length) return false;

            long nextId = q[idx + 1].Id;
            if (nextId <= 0) return true;

            try
            {
                var item = _libraryManager.GetItemById(nextId);
                if (item == null) return true;
                return item is Episode;
            }
            catch
            {
                return true;
            }
        }

        private void OnPlaybackProgress(object sender, PlaybackProgressEventArgs e)
        {
            try
            {
                var opts = GetOpts();
                if (!opts.AutoSkipCredits) return;                 // opt-in: off => do nothing
                if (e.IsPaused) return;

                var episode = e.Item as Episode;
                if (episode == null) return;

                long? posN = e.PlaybackPositionTicks;
                if (posN == null || posN.Value <= 0) return;
                long pos = posN.Value;

                long runtime = episode.RunTimeTicks ?? 0;
                if (runtime <= 0) return;

                if (pos < (long)(runtime * 0.55)) return;          // not far enough in

                var session = e.Session;
                if (session == null || !session.SupportsRemoteControl) return;

                var dk = DeviceKey(e);
                if (string.IsNullOrEmpty(dk)) return;

                if (LockedOut(dk, DateTime.UtcNow)) return;

                if (_lastFire.TryGetValue(dk, out var last)
                    && (DateTime.UtcNow - last).TotalSeconds < SessionCooldownSeconds)
                    return;

                var key = dk + "|" + episode.InternalId;
                if (_actioned.ContainsKey(key)) return;

                long credits;
                if (!_creditsCache.TryGetValue(key, out credits))
                {
                    credits = NoMarker;
                    try
                    {
                        var ch = _itemRepository.GetChapters(episode.InternalId,
                            new[] { MarkerType.CreditsStart }, CancellationToken.None);
                        if (ch != null && ch.Count > 0) credits = ch[0].StartPositionTicks;
                    }
                    catch { /* leave as NoMarker */ }
                    _creditsCache[key] = credits;
                }
                if (credits == NoMarker) return;

                long grace = Math.Max(0, opts.AutoSkipGraceSeconds) * TicksPerSecond;
                if (pos < credits + grace) return;

                // marker must be a sane distance in (guards a bad detection at, say, 56%)
                if (credits < (long)(runtime * 0.75)) return;

                // already near the real end -> let EnableNextEpisodeAutoPlay handle it
                if (runtime - pos < 6 * TicksPerSecond) return;

                if (_optUsers.Length > 0 &&
                    !_optUsers.Any(u => string.Equals(u, session.UserName, StringComparison.OrdinalIgnoreCase)))
                    return;

                // If the client reported a queue, there must be a "next" and it must be an episode.
                int qlen = session.PlaylistLength;
                if (qlen <= 1 && session.NowPlayingQueue != null) qlen = session.NowPlayingQueue.Length;
                if (qlen > 1 && session.PlaylistIndex >= qlen - 1) return;
                if (!NextInQueueIsSkippable(session))
                {
                    _log.Info("CreditsMarker: not skipping '{0}' - next item in the client's queue isn't an episode (corrupted queue?).",
                        episode.Name);
                    return;
                }

                if (!_actioned.TryAdd(key, 1)) return;
                var nowUtc = DateTime.UtcNow;
                _lastFire[dk] = nowUtc;
                _lastSkipSent[dk] = nowUtc;

                _log.Info("CreditsMarker: auto-skip -> next. '{0}' user={1} client={2} pos={3}s credits={4}s ({5:0}%) queue={6}/{7}",
                    episode.Name, session.UserName, session.Client,
                    pos / TicksPerSecond, credits / TicksPerSecond,
                    100.0 * credits / runtime, session.PlaylistIndex, qlen);

                // brief on-screen notice so the skip doesn't look like a glitch - sent just
                // before the jump. This is Emby's own transient toast (GeneralCommand
                // "DisplayMessage" with a timeout -> the same toast component as "Playing next");
                // there is no server API for the in-video "Skip Intro"-style button.
                // Text: the admin's custom string if set, otherwise a built-in one in the
                // server's UI language (no per-client language exists outside a request).
                if (opts.AutoSkipNotice)
                {
                    var custom = (opts.AutoSkipNoticeText ?? string.Empty).Trim();
                    var text = custom.Length > 0
                        ? custom
                        : Localization.TFor(Localization.ServerLocale, "Skipping credits");
                    try
                    {
                        _sessionManager.SendMessageCommand(
                            null, session.Id,
                            new MessageCommand { Header = string.Empty, Text = text, TimeoutMs = 2500 },
                            CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _log.ErrorException("CreditsMarker: could not send skip notice", ex);
                    }
                }

                _sessionManager.SendPlaystateCommand(
                    null, session.Id,
                    new PlaystateRequest { Command = PlaystateCommand.NextTrack },
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.ErrorException("CreditsMarker: auto-skip monitor error", ex);
            }
        }
    }
}
