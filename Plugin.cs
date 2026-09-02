// SPDX-License-Identifier: MIT
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using MediaBrowser.Common;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Plugins;

[assembly: InternalsVisibleTo("Emby.CreditsMarker.Tests")]

namespace Emby.CreditsMarker
{
    /// <summary>
    /// Emby plugin that detects end credits and writes a CreditsStart marker,
    /// with an optional server-side auto-skip to the next episode.
    /// </summary>
    public class Plugin : BasePluginSimpleUI<PluginOptions>, IHasThumbImage, IHasTranslations
    {
        public static Plugin Instance { get; private set; }

        public Plugin(IApplicationHost applicationHost) : base(applicationHost)
        {
            Instance = this;

            // Emby renders a SimpleUI plugin's [DisplayNameL]/[DescriptionL] verbatim
            // (its translation system only covers plugins with their own JS UI), so
            // localise the settings form ourselves - see Localization.cs.
            try
            {
                TypeDescriptor.AddProvider(
                    new LocalizedTypeDescriptionProvider(TypeDescriptor.GetProvider(typeof(PluginOptions))),
                    typeof(PluginOptions));
            }
            catch
            {
                // non-fatal: without it the form just stays English
            }
        }

        public override Guid Id => new Guid("7c1f4d2e-8a63-4b91-b0e5-9d3a2f6c1e40");

        public override string Name => "Credits Marker";

        public override string Description =>
            "Detects where the end credits start and marks it, so the \"Up Next\" card shows on time "
            + "- plus an optional server-side auto-skip to the next episode, on any client.";

        public PluginOptions GetConfiguredOptions() => GetOptions();

        // Plugin icon shown in Dashboard -> Plugins.
        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".thumb.png");
        }

        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        // Settings-page translations. Keys are the English strings in PluginOptions;
        // strings/<locale>.json maps them to the target language.
        public TranslationInfo[] GetTranslations() => new[]
        {
            new TranslationInfo { Locale = "en-US", EmbeddedResourcePath = GetType().Namespace + ".strings.en-US.json" },
            new TranslationInfo { Locale = "es-ES", EmbeddedResourcePath = GetType().Namespace + ".strings.es-ES.json" },
        };
    }
}
