using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MediaBrowser.Model.Logging;

namespace Emby.CreditsMarker
{
    public class DetectionResult
    {
        public double CreditsStartSeconds;
        public string Source;          // "blackdetect" | "heuristic" | "none"
        public string Detail;
    }

    public class CreditsDetector
    {
        private static readonly Regex BlackRx = new Regex(
            @"black_start:(?<s>[0-9.]+)[^\n]*?black_end:(?<e>[0-9.]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly string _ffmpegPath;
        private readonly ILogger _log;

        public CreditsDetector(string ffmpegPath, ILogger log)
        {
            _ffmpegPath = ffmpegPath;
            _log = log;
        }

        public DetectionResult Detect(string filePath, double runtimeSeconds, PluginOptions o, CancellationToken ct)
        {
            var res = new DetectionResult { Source = "none" };
            if (runtimeSeconds <= 60 || string.IsNullOrEmpty(filePath))
            {
                res.Detail = "runtime too short or no path";
                return res;
            }

            double analyzeFrom = runtimeSeconds * (100 - Clamp(o.AnalyzeTailPercent, 5, 60)) / 100.0;
            double earliest = runtimeSeconds * Clamp(o.EarliestCreditsPercent, 50, 98) / 100.0;
            double latest = runtimeSeconds * Clamp(o.LatestCreditsPercent, 60, 100) / 100.0;
            double minBlack = o.MinBlackSeconds > 0 ? o.MinBlackSeconds : 1.0;

            // Fast mode decodes only keyframes (~10x quicker). Detection then resolves to the
            // keyframe grid (a couple of seconds), which is plenty for a 60-100s credits block.
            string fast = o.FastKeyframeScan ? "-skip_frame nokey " : string.Empty;
            double detDur = o.FastKeyframeScan ? 0.1 : minBlack;
            string args = string.Format(CultureInfo.InvariantCulture,
                "-hide_banner -nostdin {0}-ss {1:0.###} -i \"{2}\" -vf blackdetect=d={3:0.###}:pix_th=0.10 -an -sn -f null -",
                fast, analyzeFrom, filePath, detDur);

            string stderr;
            try
            {
                stderr = RunFfmpeg(args, Math.Max(60, o.FfmpegTimeoutSeconds), ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.ErrorException("CreditsMarker: ffmpeg failed for {0}", ex, filePath);
                res.Detail = "ffmpeg error: " + ex.Message;
                return res;
            }

            var segments = new List<Tuple<double, double>>();
            foreach (Match m in BlackRx.Matches(stderr))
            {
                double s = analyzeFrom + double.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture);
                double e = analyzeFrom + double.Parse(m.Groups["e"].Value, CultureInfo.InvariantCulture);
                segments.Add(Tuple.Create(s, e));
            }

            var candidates = segments
                .Where(seg => seg.Item1 >= earliest && seg.Item1 <= latest && (seg.Item2 - seg.Item1) >= minBlack)
                .OrderByDescending(seg => seg.Item2 - seg.Item1)
                .ToList();

            if (candidates.Count > 0)
            {
                var best = candidates[0];
                res.CreditsStartSeconds = best.Item1;
                res.Source = "blackdetect";
                res.Detail = string.Format(CultureInfo.InvariantCulture,
                    "black block {0:0}s..{1:0}s ({2:0}s), {3:0.0}% of runtime",
                    best.Item1, best.Item2, best.Item2 - best.Item1, 100.0 * best.Item1 / runtimeSeconds);
                return res;
            }

            if (o.HeuristicFallback)
            {
                res.CreditsStartSeconds = runtimeSeconds * Clamp(o.HeuristicPercent, 60, 99) / 100.0;
                res.Source = "heuristic";
                res.Detail = "no black block found, used " + Clamp(o.HeuristicPercent, 60, 99) + "%";
                return res;
            }

            res.Detail = "no black block found (" + segments.Count + " black segments seen)";
            return res;
        }

        private string RunFfmpeg(string args, int timeoutSeconds, CancellationToken ct)
        {
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
                var sb = new StringBuilder();
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                p.OutputDataReceived += (s, e) => { };
                p.Start();
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();

                var sw = Stopwatch.StartNew();
                while (!p.WaitForExit(500))
                {
                    if (ct.IsCancellationRequested)
                    {
                        TryKill(p);
                        throw new OperationCanceledException(ct);
                    }
                    if (sw.Elapsed.TotalSeconds > timeoutSeconds)
                    {
                        TryKill(p);
                        throw new TimeoutException("ffmpeg timed out after " + timeoutSeconds + "s");
                    }
                }
                p.WaitForExit();
                return sb.ToString();
            }
        }

        private static void TryKill(Process p)
        {
            try { if (!p.HasExited) p.Kill(); } catch { }
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
