<div align="center">

# Emby Credits Marker

**Salta los créditos finales en Emby — algo que no trae de fábrica.**
**Skip the end credits in Emby — a feature it has no built-in equivalent for.**

</div>

---

## 🇪🇸 Español

Emby detecta las cabeceras de los episodios, pero **no los créditos finales**. Este
plugin (Emby 4.9) rellena ese hueco: encuentra dónde empiezan los créditos y lo marca,
para que puedas saltártelos con un clic — o automáticamente.

### Qué hace

| | |
|---|---|
| **Detección** | Una tarea nocturna (`Detect end credits`) localiza dónde arrancan los créditos de cada episodio/película y guarda el punto. |
| **Episodios** | Reciben un marcador `CreditsStart`. El reproductor de Emby lo lee y muestra la tarjeta **«A continuación»** justo ahí — un clic y al siguiente episodio. Funciona en todos los clientes que comparten el reproductor web de Emby (web, Android TV incluida la APK modificada, LG, Samsung). También se añade una marca **«Créditos»** visible en la barra de progreso (si el archivo no trae ya una). |
| **Salto automático** *(opcional)* | El plugin vigila la reproducción y, cuando un episodio pasa su `CreditsStart`, manda al cliente la orden de **«siguiente episodio»**. Es control remoto desde el servidor, así que funciona en **todos** los clientes, iOS incluido. Se activa con `Saltar créditos automáticamente` y se puede limitar a ciertos usuarios. |
| **Películas** | Reciben una marca «Créditos» visible (Emby no hace nada con `CreditsStart` en películas). Desactivado por defecto. |

### Cómo detecta los créditos

Por orden de prioridad:

1. **Capítulo incrustado.** Muchos releases traen un capítulo llamado `Credits` /
   `End Credits` / `Ending` / `Créditos`… en el propio `.mkv`. Si lo hay en la zona
   final (78–99 %), se usa esa posición tal cual — sin `ffmpeg`, y más preciso que
   cualquier análisis.
2. **Corte a negro** (`ffmpeg blackdetect` sobre el último ~20 %): coge el bloque de
   negro sostenido dentro de la ventana esperada. Los episodios se reconcilian **por
   serie**: si la detección es consistente en la serie, gana y arrastra los outliers.
3. **Huella de audio** (reserva, para créditos sobre imagen — típico en anime): saca la
   huella `chromaprint` de la cola de varios episodios, busca la **sintonía final que se
   repite** y marca la serie entera en ese punto.
4. Series donde nada funciona (p. ej. anime que cambia de *ending* entre arcos) se
   quedan **sin marcar** — mejor eso que un marcador en el sitio equivocado.

Un marcador ya existente se respeta; los capítulos y marcadores de intro del archivo
**nunca se tocan** (si al reconstruir la lista fuese a perderse alguno, el plugin
aborta la escritura de ese ítem).

### Protección anti-bucle (salto automático)

El salto automático nunca amplifica un bucle del cliente y trata de frenarlo:

- un salto por dispositivo cada 45 s, y el «siguiente» de la cola debe ser un episodio;
- un dispositivo que encadena fallos de reproducción (empieza y para en el segundo 0
  sin llegar a reproducir) queda **excluido** del salto automático;
- si esos fallos van avanzando por episodios distintos (bucle de cola, típico de Emby
  para iOS cuando no puede reproducir el códec), el servidor le manda un `Stop` — y si
  el cliente lo ignora, insiste y luego lo deshabilita 15 min con un aviso en el log.

### Instalar

Emby 4.9 no tiene catálogo de plugins de terceros, así que:

1. Descarga `Emby.CreditsMarker.dll` de la [última release](../../releases/latest).
2. Cópialo a la carpeta `plugins` del servidor (`/config/plugins` en Docker).
3. Reinicia Emby.
4. Configúralo en **Panel → Plugins → Credits Marker**.
5. Lanza la tarea **Detect end credits** una vez (o espera a la pasada nocturna).

> ⚠️ Emby **no relee** el archivo de configuración en caliente. Cambia los ajustes
> **desde la página del plugin y pulsa Guardar** (no editando el JSON a mano).

---

## 🇬🇧 English

Emby detects episode intros but has **no end-credits detection**. This Emby 4.9 plugin
fills the gap: it finds where the credits start and marks it, so you can skip them with
one click — or automatically.

### What it does

| | |
|---|---|
| **Detection** | A nightly task (`Detect end credits`) works out where the end credits start on every episode/movie and stores the point. |
| **Episodes** | Get a `CreditsStart` marker. Emby's player reads it and shows the **"Up Next" card** right there — one click to the next episode. Works on every client sharing the Emby web player (web, Android TV incl. the modded APK, LG, Samsung). A visible **"Credits"** tick is also added to the seek bar (unless the file already has one). |
| **Auto-skip** *(opt-in)* | The plugin watches playback and, when an episode passes its `CreditsStart`, sends the client a **"next episode"** command. Server-driven, so it works on **every** client including iOS. Enable with `Auto-skip credits`; restrict to specific users if you want. |
| **Movies** | Get a visible "Credits" chapter (Emby does nothing with `CreditsStart` on movies). Off by default. |

### How detection works

In priority order:

1. **Embedded chapter.** Lots of releases ship a chapter literally named `Credits` /
   `End Credits` / `Ending` / `Dub credits`… inside the `.mkv`. If one sits in the end
   zone (78–99%), its position is used as-is — no `ffmpeg`, and more accurate than any
   analysis.
2. **Hard cut to black** (`ffmpeg blackdetect` over the last ~20%): pick the sustained
   black block inside the expected window. Episodes are reconciled **per series**: where
   detection agrees across the series it wins and pulls stray episodes onto the consensus.
3. **Audio fingerprint** (fallback, for credits over content — common in anime): take a
   `chromaprint` fingerprint of several episodes' tails, find the **recurring end-theme**,
   and mark the whole series there.
4. Series where nothing works (e.g. anime that switches ending song between arcs) are
   **left unmarked** — better that than a marker in the wrong place.

An existing marker is respected; the file's own chapters and intro markers are **never
touched** (if rebuilding the list would drop one, the plugin aborts that item's write).

### Runaway protection (auto-skip)

Auto-skip never amplifies a client-side loop, and tries to stop one:

- one skip per device per 45 s, and the next queue item must be an episode;
- a device racking up quick playback failures (starts, then stops at ~0 without playing)
  is **locked out** of auto-skip;
- if those failures march through different episodes (a queue-advance runaway — typical
  of Emby for iOS when it can't play the codec) the server sends it a `Stop`, and if the
  client ignores it, keeps trying and then disables auto-skip for 15 min with a log line.

### Install

Emby 4.9 has no third-party plugin catalog, so:

1. Download `Emby.CreditsMarker.dll` from the [latest release](../../releases/latest).
2. Drop it in your Emby server's `plugins` folder (`/config/plugins` on Docker).
3. Restart Emby.
4. Configure at **Dashboard → Plugins → Credits Marker**.
5. Run the **Detect end credits** task once (or wait for the nightly run).

> ⚠️ Emby does **not** hot-reload the config file. Change settings **from the plugin
> page and hit Save** (not by editing the JSON by hand).

---

## Settings / Ajustes

| Setting | Default | |
|---|---|---|
| Process episodes / Procesar episodios | on | |
| Process movies / Procesar películas | off | visible chapter, not `CreditsStart` |
| Libraries / Bibliotecas | *(all)* | CSV of library names |
| Visible chapter on the seek bar / Marca visible | on | |
| **Auto-skip credits / Saltar automáticamente** | **off** | server sends "next episode" at the credits point |
| Auto-skip users / Usuarios | *(everyone)* | CSV of usernames |
| Break client playback loops / Cortar bucles | on | send `Stop` to a client stuck auto-advancing |
| Grace before skipping / Margen | 0 s | wait N s into the credits before skipping |
| Analyse new episodes on the fly / Analizar nuevos | off | mark a new episode within minutes instead of waiting for the nightly run |
| Fast scan / Análisis rápido | on | ~9× faster, keyframe-only |
| Audio-fingerprint fallback / Huella de audio | on | for credits-over-content |
| Re-scan marked items / Volver a analizar | off | |
| *Advanced / Avanzado* | | detection tuning — the defaults are fine |

## Build

`./build.sh` builds `out/Emby.CreditsMarker.dll` in a .NET 8 SDK container. It needs the
Emby reference assemblies (`MediaBrowser.*.dll`, `Emby.*.dll`) in `./lib/` — copy them
from an Emby install (`.../system/`). CI does this automatically and publishes a Release
on every `v*` tag (`.github/workflows/build.yml`).

## License

MIT.
