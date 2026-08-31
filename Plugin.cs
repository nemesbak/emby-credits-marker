using System;
using System.IO;
using MediaBrowser.Common;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Drawing;

namespace Emby.CreditsMarker
{
    /// <summary>
    /// Emby plugin that detects end credits and writes a CreditsStart marker,
    /// with an optional server-side auto-skip to the next episode.
    /// </summary>
    public class Plugin : BasePluginSimpleUI<PluginOptions>, IHasThumbImage
    {
        public static Plugin Instance { get; private set; }

        public Plugin(IApplicationHost applicationHost) : base(applicationHost)
        {
            Instance = this;
        }

        public override Guid Id => new Guid("7c1f4d2e-8a63-4b91-b0e5-9d3a2f6c1e40");

        public override string Name => "Credits Marker";

        public override string Description =>
            "Marca dónde empiezan los créditos finales para poder saltarlos: tarjeta «A continuación» a tiempo "
            + "y salto automático opcional al siguiente episodio en cualquier cliente. "
            + "// Marks where the end credits start so you can skip them: an on-time \"Up Next\" card "
            + "plus an optional server-side auto-skip to the next episode, on any client.";

        public PluginOptions GetConfiguredOptions() => GetOptions();

        // Plugin icon shown in Dashboard → Plugins.
        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".thumb.png");
        }

        public ImageFormat ThumbImageFormat => ImageFormat.Png;
    }
}
