using System.ComponentModel;
using Emby.Web.GenericEdit;

namespace Emby.CreditsMarker
{
    public class PluginOptions : EditableOptionsBase
    {
        public override string EditorTitle => "Credits Marker";

        public override string EditorDescription =>
            "Marca dónde empiezan los créditos finales para que puedas saltártelos — algo que Emby no trae de fábrica.\n"
            + "Una tarea nocturna («Detect end credits») analiza cada episodio con ffmpeg y guarda el punto de inicio de los créditos. "
            + "En los episodios, Emby muestra la tarjeta «A continuación» justo en ese momento (un clic y al siguiente). "
            + "Si activas «Saltar créditos automáticamente», el servidor pasa de episodio él solo, en cualquier cliente.\n"
            + "— — —\n"
            + "Marks where the end credits start so you can skip them — something Emby has no built-in feature for. "
            + "A nightly task (\"Detect end credits\") analyses every episode with ffmpeg and stores the credits-start point. "
            + "On episodes, Emby shows the \"Up Next\" card exactly there (one click to the next one). "
            + "Turn on \"Auto-skip credits\" and the server advances episodes by itself, on any client.";

        // ─────────────────────────────  Qué analizar / What to scan  ─────────────────────────────

        [DisplayName("Procesar episodios · Process episodes")]
        [Description("Analiza los episodios y les pone el marcador de créditos. — Analyse episodes and mark where the credits start.")]
        public bool ProcessEpisodes { get; set; } = true;

        [DisplayName("Procesar películas · Process movies")]
        [Description("Añade un capítulo visible «Créditos» a las películas (la tarjeta «A continuación» de Emby no aplica a películas). Desactivado por defecto. "
            + "— Add a visible \"Credits\" chapter to movies (Emby's \"Up Next\" card doesn't apply to films). Off by default.")]
        public bool ProcessMovies { get; set; } = false;

        [DisplayName("Bibliotecas · Libraries")]
        [Description("Nombres de biblioteca separados por comas para limitar el análisis. Vacío = todas las bibliotecas de vídeo. "
            + "— Comma-separated library names to limit the scan to. Empty = every video library.")]
        public string LibraryNames { get; set; } = "";

        // ─────────────────────────────  Marcadores / Markers  ─────────────────────────────

        [DisplayName("Marca visible en la barra · Visible chapter on the seek bar")]
        [Description("Además del marcador oculto, añade una marca «Créditos» visible en la barra de progreso. "
            + "Sirve aunque el usuario haya apagado el aviso de «siguiente episodio». Recomendado. "
            + "— Besides the hidden marker, add a visible \"Credits\" tick on the progress bar. "
            + "Works even if the viewer turned off the \"next episode\" overlay. Recommended.")]
        public bool AlsoVisibleChapterOnEpisodes { get; set; } = true;

        // ─────────────────────────────  Salto automático / Auto-skip  ─────────────────────────────

        [DisplayName("Saltar créditos automáticamente · Auto-skip credits")]
        [Description("Cuando un episodio llega a los créditos, el servidor ordena al reproductor pasar al siguiente. "
            + "Funciona en TODOS los clientes (web, móvil, TV). Desactivado por defecto. "
            + "— When an episode reaches the credits, the server tells the player to jump to the next episode. "
            + "Works on every client (web, mobile, TV). Off by default.")]
        public bool AutoSkipCredits { get; set; } = false;

        [DisplayName("Usuarios del salto automático · Auto-skip users")]
        [Description("Nombres de usuario (separados por comas) a los que se aplica el salto automático. Vacío = todos. "
            + "— Comma-separated usernames the auto-skip applies to. Empty = everyone.")]
        public string AutoSkipUsers { get; set; } = "";

        [DisplayName("Margen antes de saltar (segundos) · Grace before skipping (seconds)")]
        [Description("Espera estos segundos ya dentro de los créditos antes de saltar. 0 = salta en cuanto empiezan. "
            + "— Wait this many seconds into the credits before skipping. 0 = skip as soon as they start.")]
        public int AutoSkipGraceSeconds { get; set; } = 0;

        [DisplayName("Aviso en pantalla al saltar · On-screen notice when skipping")]
        [Description("Muestra un mensajito en el reproductor justo antes del salto automático, para que no parezca un fallo. "
            + "Se ve igual que los avisos propios de Emby. "
            + "— Show a short message in the player just before the auto-skip, so it doesn't look like a glitch. "
            + "Rendered like Emby's own notices.")]
        public bool AutoSkipNotice { get; set; } = true;

        [DisplayName("Texto del aviso · Notice text")]
        [Description("El texto que aparece al saltar automáticamente. — The text shown when auto-skipping.")]
        public string AutoSkipNoticeText { get; set; } = "Saltando créditos…";

        [DisplayName("Cortar bucles de reproducción del cliente · Break client playback loops")]
        [Description("Si un reproductor se atasca en bucle (empieza un episodio tras otro a toda velocidad, típico de Emby para iOS "
            + "con una cola de reproducción corrupta), el servidor le manda un «Stop» para cortarlo. No afecta a la reproducción normal. Recomendado. "
            + "— If a player gets stuck in a loop (machine-gunning one episode after another — a known Emby for iOS bug with a corrupted "
            + "play queue), the server sends it a \"Stop\" to break the loop. Doesn't touch normal playback. Recommended.")]
        public bool BreakRunawayLoops { get; set; } = true;

        // ─────────────────────────────  Motor de detección / Detection engine  ─────────────────────────────

        [DisplayName("Análisis rápido · Fast scan")]
        [Description("Analiza solo fotogramas clave: ~9× más rápido, con ±2 s de margen (siempre cae dentro de los créditos, nunca antes). Recomendado. "
            + "— Keyframe-only analysis: ~9× faster, within ±2 s (always lands inside the credits, never early). Recommended.")]
        public bool FastKeyframeScan { get; set; } = true;

        [DisplayName("Huella de audio para créditos sobre imagen · Audio-fingerprint fallback")]
        [Description("Para series (p. ej. anime) cuyos créditos van sobre la imagen sin fundido a negro: busca la sintonía final que se repite entre episodios "
            + "y marca la serie entera. Recomendado. "
            + "— For series (e.g. anime) whose credits roll over content with no fade to black: finds the recurring end-theme across episodes "
            + "and marks the whole series. Recommended.")]
        public bool EnableFingerprintFallback { get; set; } = true;

        [DisplayName("Volver a analizar lo ya marcado · Re-scan already-marked items")]
        [Description("Vuelve a analizar los ítems que ya tienen marcador. Solo hace falta si cambiaste los ajustes avanzados de abajo. "
            + "— Re-analyse items that already have a marker. Only needed if you changed the advanced settings below.")]
        public bool Redetect { get; set; } = false;

        [DisplayName("Analizar episodios nuevos al momento · Analyse new episodes on the fly")]
        [Description("Cuando se añade un episodio, lo analiza a los pocos minutos en vez de esperar a la tarea nocturna. "
            + "Si la serie ya tiene consenso, lo aplica al instante sin ffmpeg. Desactivado por defecto. "
            + "— When an episode is added, analyse it within minutes instead of waiting for the nightly task. "
            + "If the series already has a consensus, it's applied instantly with no ffmpeg. Off by default.")]
        public bool AnalyzeNewEpisodes { get; set; } = false;

        [DisplayName("Avanzado · Espera antes de analizar lo nuevo (minutos) · Delay before analysing new items (minutes)")]
        [Description("Cuánto espera tras añadirse un episodio antes de analizarlo, para que una importación de temporada entera se asiente primero. "
            + "— How long to wait after an episode is added before analysing it, so a full-season import settles first.")]
        public int NewEpisodeDelayMinutes { get; set; } = 20;

        // ─────────────────────────────  Ajustes avanzados / Advanced  ─────────────────────────────
        // Los valores por defecto funcionan bien; toca esto solo si la detección falla en tu contenido.
        // The defaults work well; only touch these if detection misses on your content.

        [DisplayName("Avanzado · Negro mínimo (segundos) · Minimum black block (seconds)")]
        [Description("Duración mínima de fundido a negro que cuenta como inicio de créditos. "
            + "— Shortest black-frame block that counts as the start of the credits.")]
        public double MinBlackSeconds { get; set; } = 3.0;

        [DisplayName("Avanzado · Ventana de búsqueda: desde (% de duración) · Search window start (% of runtime)")]
        [Description("Ignora los negros que empiecen antes de este punto de la duración total. "
            + "— Ignore black blocks that start before this point of the total runtime.")]
        public int EarliestCreditsPercent { get; set; } = 82;

        [DisplayName("Avanzado · Ventana de búsqueda: hasta (% de duración) · Search window end (% of runtime)")]
        [Description("Ignora los negros que empiecen después de este punto. "
            + "— Ignore black blocks that start after this point.")]
        public int LatestCreditsPercent { get; set; } = 99;

        [DisplayName("Avanzado · Cola a analizar (% del archivo) · Tail to analyse (% of file)")]
        [Description("Analiza solo el último N % del archivo, para ir rápido. "
            + "— Only analyse the last N% of the file, for speed.")]
        public int AnalyzeTailPercent { get; set; } = 20;

        [DisplayName("Avanzado · Huella: cola a comparar (%) · Fingerprint: tail compared (%)")]
        [Description("Qué parte final de cada episodio se compara al buscar la sintonía repetida. "
            + "— How much of each episode's tail is compared when looking for the recurring theme.")]
        public int FingerprintTailPercent { get; set; } = 30;

        [DisplayName("Avanzado · Huella: coincidencia mínima (segundos) · Fingerprint: minimum match (seconds)")]
        [Description("Longitud mínima del tramo de audio común para aceptarlo como sintonía final. "
            + "— Minimum length of shared audio to accept it as the end-theme.")]
        public int FingerprintMinRunSeconds { get; set; } = 20;

        [DisplayName("Avanzado · Marcar a un % fijo si no se detecta nada · Fixed-percentage fallback")]
        [Description("Si ni el negro ni la huella encuentran nada, marca a un porcentaje fijo de la duración. Impreciso; desactivado por defecto. "
            + "— If neither black analysis nor fingerprinting find anything, mark at a fixed % of the runtime. Rough; off by default.")]
        public bool HeuristicFallback { get; set; } = false;

        [DisplayName("Avanzado · Porcentaje fijo · Fixed percentage")]
        [Description("El punto (% de la duración) donde marca el ajuste anterior. "
            + "— The point (% of runtime) used by the setting above.")]
        public int HeuristicPercent { get; set; } = 95;

        [DisplayName("Avanzado · Máx. ítems por pasada · Max items per run")]
        [Description("0 = sin límite. Útil para repartir un primer análisis grande en varias noches. "
            + "— 0 = unlimited. Useful to spread a big first scan over several nights.")]
        public int MaxItemsPerRun { get; set; } = 0;

        [DisplayName("Avanzado · Máx. horas por pasada · Max hours per run")]
        [Description("Corta la tarea tras estas horas y reanuda en la siguiente pasada (además del tope de Emby). 0 = sin límite. "
            + "— Stop the task after this many hours and resume next run (on top of Emby's own cap). 0 = unlimited.")]
        public double MaxRunHours { get; set; } = 10;

        [DisplayName("Avanzado · Tiempo máximo de ffmpeg por archivo (segundos) · ffmpeg timeout per file (seconds)")]
        [Description("Aborta el análisis de un archivo si ffmpeg tarda más de esto. "
            + "— Abort a file's analysis if ffmpeg takes longer than this.")]
        public int FfmpegTimeoutSeconds { get; set; } = 900;
    }
}
