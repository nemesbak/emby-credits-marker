// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Emby.CreditsMarker
{
    /// <summary>
    /// Localises the plugin's settings page ourselves.
    ///
    /// Emby 4.9 does NOT run a server-side SimpleUI plugin's <c>[DisplayNameL]</c> /
    /// <c>[DescriptionL]</c> through its translation system - the form renderer
    /// (EditorBuilder / genericedit.js) uses those strings verbatim. Emby's
    /// <c>IHasTranslations</c> + <c>/web/strings</c> path only helps plugins that
    /// ship their own JavaScript UI. So we translate at render time: a
    /// <see cref="LocalizedTypeDescriptionProvider"/> swaps each label/description
    /// for the current UI culture (Emby sets the thread culture from the client's
    /// <c>X-Emby-Language</c> / <c>ClientLocale</c>), and this class is the lookup.
    ///
    /// English stays the source of truth; the translations are the same
    /// <c>strings/&lt;locale&gt;.json</c> files Emby's own mechanism would use,
    /// keyed by the English string.
    /// </summary>
    internal static class Localization
    {
        // language tag (lower-case, e.g. "es" or "es-es") -> (english -> translated)
        private static readonly Dictionary<string, Dictionary<string, string>> _tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The server's configured UI language (<c>ServerConfiguration.UICulture</c>), set once
        /// by <see cref="Plugin"/>. Used for text that isn't produced in a request context - e.g.
        /// the auto-skip notice pushed to a player - where there is no per-client culture to read.
        /// </summary>
        public static string ServerLocale { get; set; }

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        static Localization()
        {
            try
            {
                var asm = typeof(Localization).Assembly;
                const string prefix = "Emby.CreditsMarker.strings.";
                foreach (var res in asm.GetManifestResourceNames())
                {
                    if (!res.StartsWith(prefix, StringComparison.Ordinal) ||
                        !res.EndsWith(".json", StringComparison.Ordinal))
                        continue;

                    var locale = res.Substring(prefix.Length, res.Length - prefix.Length - 5); // strip ".json"
                    Dictionary<string, string> table;
                    using (var s = asm.GetManifestResourceStream(res))
                    {
                        if (s == null) continue;
                        using var r = new StreamReader(s);
                        table = JsonSerializer.Deserialize<Dictionary<string, string>>(r.ReadToEnd(), JsonOpts);
                    }
                    if (table == null || table.Count == 0) continue;

                    _tables[locale] = table;                 // e.g. "es-ES"
                    var dash = locale.IndexOf('-');
                    if (dash > 0)
                    {
                        var neutral = locale.Substring(0, dash);   // "es"
                        if (!_tables.ContainsKey(neutral)) _tables[neutral] = table;
                    }
                }
            }
            catch
            {
                // localisation is best-effort - English is always a valid result
            }
        }

        /// <summary>
        /// Translates an English UI string for <see cref="CultureInfo.CurrentUICulture"/>.
        /// Returns <paramref name="english"/> unchanged when there is no translation.
        /// </summary>
        public static string T(string english)
        {
            if (string.IsNullOrEmpty(english) || _tables.Count == 0) return english;
            try
            {
                for (var c = CultureInfo.CurrentUICulture;
                     c != null && !string.IsNullOrEmpty(c.Name);
                     c = c.Parent)
                {
                    if (_tables.TryGetValue(c.Name, out var table) &&
                        table.TryGetValue(english, out var translated) &&
                        !string.IsNullOrEmpty(translated))
                        return translated;
                }
            }
            catch
            {
                // fall through to English
            }
            return english;
        }

        /// <summary><c>string.Format</c> with the translated format string and the current culture.</summary>
        public static string TF(string englishFormat, params object[] args)
        {
            var fmt = T(englishFormat);
            try { return string.Format(CultureInfo.CurrentUICulture, fmt, args); }
            catch { return string.Format(CultureInfo.InvariantCulture, fmt, args); }
        }

        /// <summary>
        /// Translates an English string for a specific language tag (e.g. <c>"es"</c> or
        /// <c>"es-ES"</c>), independent of the current thread culture. A null/empty locale,
        /// or one with no translation, returns <paramref name="english"/> unchanged.
        /// </summary>
        public static string TFor(string locale, string english)
        {
            if (string.IsNullOrEmpty(english) || string.IsNullOrEmpty(locale) || _tables.Count == 0)
                return english;
            try
            {
                var c = CultureInfo.GetCultureInfo(locale.Replace('_', '-'));
                for (; c != null && !string.IsNullOrEmpty(c.Name); c = c.Parent)
                {
                    if (_tables.TryGetValue(c.Name, out var table) &&
                        table.TryGetValue(english, out var translated) &&
                        !string.IsNullOrEmpty(translated))
                        return translated;
                }
            }
            catch
            {
                // unknown locale name -> English
            }
            return english;
        }
    }
}
