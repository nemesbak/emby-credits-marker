using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using MediaBrowser.Model.Logging;

namespace Emby.CreditsMarker
{
    /// <summary>
    /// Audio-fingerprint outro detection: for series whose end credits roll over content
    /// (no hard cut to black), the recurring end-theme / ED music is the signal. We take a
    /// chromaprint fingerprint of the tail of two episodes and find the longest run of frames
    /// that match — that shared run is the recurring outro; where it starts is the credits.
    ///
    /// chromaprint emits ~7.9 uint32 sub-fingerprints per second.
    /// </summary>
    public class OutroFingerprint
    {
        private const double FramesPerSecond = 7.9365; // chromaprint default (11025/1387 ~ 7.94)

        private readonly string _ffmpegPath;
        private readonly ILogger _log;
        private int _chromaprintOk = -1; // -1 unknown, 0 no, 1 yes

        public OutroFingerprint(string ffmpegPath, ILogger log)
        {
            _ffmpegPath = ffmpegPath;
            _log = log;
        }

        public bool ChromaprintAvailable(CancellationToken ct)
        {
            if (_chromaprintOk >= 0) return _chromaprintOk == 1;
            try
            {
                // feed a short tone through the chromaprint muxer (silence produces no output)
                var psi = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = "-hide_banner -nostdin -f lavfi -i sine=frequency=440:sample_rate=11025:duration=6 -ac 1 -f chromaprint -fp_format raw -",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var p = new Process { StartInfo = psi })
                {
                    p.Start();
                    // drain both pipes concurrently - reading one to completion while the
                    // other's buffer fills would deadlock the child.
                    var outTask = System.Threading.Tasks.Task.Run(() => ReadAll(p.StandardOutput.BaseStream));
                    var errTask = System.Threading.Tasks.Task.Run(() => p.StandardError.ReadToEnd());
                    if (!p.WaitForExit(20000)) { TryKill(p); }
                    p.WaitForExit();
                    var bytes = outTask.GetAwaiter().GetResult();
                    try { errTask.GetAwaiter().GetResult(); } catch { }
                    _chromaprintOk = (p.HasExited && p.ExitCode == 0 && bytes.Length >= 8) ? 1 : 0;
                }
            }
            catch (Exception ex)
            {
                _log.ErrorException("CreditsMarker: chromaprint probe failed", ex);
                _chromaprintOk = 0;
            }
            if (_chromaprintOk == 0)
                _log.Info("CreditsMarker: ffmpeg has no chromaprint muxer - fingerprint detection disabled.");
            return _chromaprintOk == 1;
        }

        /// <summary>Raw chromaprint sub-fingerprints (uint32) for [startSec, startSec+durSec).</summary>
        public uint[] Fingerprint(string filePath, double startSec, double durSec, int timeoutSeconds, CancellationToken ct)
        {
            var args = string.Format(CultureInfo.InvariantCulture,
                "-hide_banner -nostdin -ss {0:0.###} -i \"{1}\" -t {2:0.###} -ac 1 -map 0:a:0 -f chromaprint -fp_format raw -",
                startSec, filePath, durSec);

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var p = new Process { StartInfo = psi })
            {
                p.Start();
                var task = System.Threading.Tasks.Task.Run(() => ReadAll(p.StandardOutput.BaseStream));
                var errTask = System.Threading.Tasks.Task.Run(() => p.StandardError.ReadToEnd());

                var sw = Stopwatch.StartNew();
                while (!p.WaitForExit(500))
                {
                    if (ct.IsCancellationRequested) { TryKill(p); throw new OperationCanceledException(ct); }
                    if (sw.Elapsed.TotalSeconds > Math.Max(30, timeoutSeconds)) { TryKill(p); throw new TimeoutException("chromaprint timed out"); }
                }
                p.WaitForExit();
                var bytes = task.GetAwaiter().GetResult();
                int n = bytes.Length / 4;
                var vals = new uint[n];
                for (int i = 0; i < n; i++)
                    vals[i] = BitConverter.ToUInt32(bytes, i * 4);
                return vals;
            }
        }

        public struct Match
        {
            public int Length;   // frames
            public int OffsetA;  // start frame in a
            public int OffsetB;  // start frame in b
        }

        /// <summary>
        /// Longest run where a[oa+k] and b[ob+k] differ by at most maxErrBits, for k in [0,len).
        /// </summary>
        public static Match LongestCommonRun(uint[] a, uint[] b, int maxErrBits = 3)
        {
            var best = new Match();
            int na = a.Length, nb = b.Length;
            for (int off = -(nb - 1); off < na; off++)
            {
                int ia = off > 0 ? off : 0;
                int ib = off < 0 ? -off : 0;
                int run = 0, rsA = ia, rsB = ib;
                while (ia < na && ib < nb)
                {
                    if (PopCount(a[ia] ^ b[ib]) <= maxErrBits)
                    {
                        if (run == 0) { rsA = ia; rsB = ib; }
                        run++;
                        if (run > best.Length) { best.Length = run; best.OffsetA = rsA; best.OffsetB = rsB; }
                    }
                    else run = 0;
                    ia++; ib++;
                }
            }
            return best;
        }

        public static double FramesToSeconds(int frames) => frames / FramesPerSecond;

        private static int PopCount(uint x)
        {
            x = x - ((x >> 1) & 0x55555555u);
            x = (x & 0x33333333u) + ((x >> 2) & 0x33333333u);
            x = (x + (x >> 4)) & 0x0F0F0F0Fu;
            return (int)((x * 0x01010101u) >> 24);
        }

        private static byte[] ReadAll(Stream s)
        {
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms, 65536);
                return ms.ToArray();
            }
        }

        private static void TryKill(Process p)
        {
            try { if (!p.HasExited) p.Kill(); } catch { }
        }
    }
}
