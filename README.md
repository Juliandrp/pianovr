# PianoVR

Aprende piano jugando, **al estilo Guitar Hero**, en realidad mixta con las **Meta Quest 3**. Ves tu piano real (o uno virtual sobre una mesa) y encima caen las notas en 3D, alineadas con cada tecla. Ganas puntos, rachas, multiplicadores y Modo Estrella; si fallas mucho en modo concierto, el público te abuchea.

> Estado: **v0.2**. Todo el código está escrito, pero todavía no se ha compilado en Unity ni probado en las gafas. Si algo falla al abrirlo, abre un issue con el mensaje de la consola.

## Qué tiene

**Juego estilo Guitar Hero**
- Puntaje por precisión: **¡Perfecto!** (±50 ms), **¡Genial!** (±100 ms), **Bien** (±180 ms).
- **Racha y multiplicador**: x2, x3 y x4 cada 10 notas seguidas.
- **Medidor de público**: sube al acertar y baja al fallar. En modo concierto, si llega a cero, pierdes la canción.
- **Modo Estrella**: las frases estrella brillan. Complétalas sin fallar y se carga la barra. Con 50 % o más, **levanta ambas manos sobre la cabeza** (o grip, o el botón ¡ESTRELLA!) para duplicar los puntos.
- **4 dificultades**: Fácil (solo melodía), Medio (mano derecha), Difícil (ambas manos, izquierda simplificada) y Experto (todo).
- **Resultados** de 1 a 5 estrellas, desglose y **récord por canción y dificultad**.
- **Modo práctica**: la canción espera cada nota y no se pierde (no guarda récords).

**Tres formas de tocar**

| Modo | Para quién | Cómo detecta las notas |
|---|---|---|
| **Manos (piano real)** · por defecto | Teclados sin USB | Seguimiento de manos: la punta del dedo hunde la tecla |
| **Teclado MIDI** | Teclados con USB "to host" o MIDI OUT + adaptador | Cable USB-C a las gafas (lo más preciso). Se activa solo al conectarlo |
| **Manos (mesa)** | Sin piano | Piano virtual de 25 o 37 teclas sobre cualquier mesa, con sonido |

**Sonido**: sintetizador propio. Las notas que no te tocan a ti suenan como acompañamiento; en la mesa también suenan tus teclas; al fallar suena un "clank", como en Guitar Hero.

**Tamaño del teclado automático**: al calibrar, la app mide la distancia entre la tecla más grave y la más aguda y deduce si tu teclado es de 25, 37, 49, 61, 76 u 88 teclas. Las canciones se acomodan por octavas para caber en tu teclado.

## Cómo funciona

```
Teclado MIDI ──USB-C──▶ MidiBridge.java ─┐
Dedos (XR Hands) ──▶ HandKeyPresser ─────┼──▶ MidiInputHub ──▶ SongPlayer (reloj, aciertos, fallos)
Teclado del PC ──▶ EditorKeyboardInput ──┘                          │
                                                                    ├──▶ ScoreSystem (puntos, racha, público, estrella)
Archivo .mid ──▶ MidiFile ──▶ SongArranger (dificultad, octavas) ───┘        │
                                                                             ├──▶ GameHud / FeedbackFx (pantalla)
PianoLayout (calibración) ──▶ NoteHighway · KeyHighlighter · VirtualKeyboard └──▶ GameAudio ──▶ PianoSynth
```

| Carpeta | Qué hay |
|---|---|
| `Scripts/Midi` | Lector de MIDI, arreglos por dificultad, entrada MIDI de Android, simulador con teclado del PC |
| `Scripts/Hands` | Puntas de los dedos (XR Hands) y detección de teclas presionadas |
| `Scripts/Piano` | Geometría y calibración del piano, resaltado de teclas, piano virtual |
| `Scripts/Game` | Reloj de la canción, puntaje estilo Guitar Hero, notas que caen, controlador general |
| `Scripts/Audio` | Sintetizador y mezcla del juego |
| `Scripts/UI` | Panel de puntaje, efectos, menú de botones que se tocan con el dedo |
| `Scripts/Editor` | Menú **PianoVR > Agregar PianoVR a la escena** |
| `Assets/Plugins/Android/PianoVR` | `MidiBridge.java`, puente con `android.media.midi` |
| `Resources/PianoVR/Songs` | Canciones de ejemplo (archivos `.mid` con extensión `.bytes`) |

## Requisitos

- **Unity 6 (LTS)** con **Android Build Support** (incluye OpenJDK, SDK y NDK), desde Unity Hub.
- Quest 3 en **modo desarrollador**, con el **seguimiento de manos activado** (Configuración → Movimiento → Seguimiento de manos).
- Paquete **XR Hands** (`com.unity.xr.hands`). La plantilla Mixed Reality ya lo trae; si no, instálalo desde Package Manager. Sin él, la app funciona solo con teclado MIDI.
- Opcional: teclado MIDI USB y cable USB-B a USB-C.

## Montar el proyecto

1. En Unity Hub: **New project → plantilla "Mixed Reality"** → nombre `PianoVR`.
2. Con Unity cerrado, trae este código dentro de la carpeta del proyecto:
   ```bash
   cd ruta/a/PianoVR
   git init
   git remote add origin https://github.com/Juliandrp/pianovr.git
   git pull origin main
   ```
3. Abre el proyecto y, en la escena de la plantilla, usa **PianoVR → Agregar PianoVR a la escena**. Si ya lo tenías de la versión anterior, te ofrece reemplazarlo.
4. Para versionar todo: `git add . && git commit -m "Proyecto Unity" && git push origin main`.

## Probar en el PC (sin gafas)

Dale **Play**: aparece un piano frente a la cámara.

| Tecla | Acción |
|---|---|
| `A W S E D F T G Y H U J K O L P ;` | Do, Do#, Re, Re#, Mi… · `Z` / `X` bajan / suben octava |
| `Espacio` | tocar / pausa |
| `Retroceso` | otra vez |
| `1` | mano (ambas / derecha / izquierda) |
| `2` | práctica / concierto |
| `3` / `4` | menos / más velocidad |
| `5` / `6` | canción anterior / siguiente |
| `7` | dificultad |
| `8` | forma de tocar (MIDI / manos / mesa) |
| `9` | activar Modo Estrella |
| `←` / `→` | retroceder / avanzar 5 s |
| `Enter` | recolocar el piano frente a la cámara |

## Compilar para las Quest 3

1. **File → Build Profiles → Meta Quest** (o Android) → **Switch Platform**.
2. Conecta las gafas por USB y acepta la depuración en el visor.
3. **Build And Run**.

## Jugar

1. **Calibrar** (la primera vez arranca sola). Marca el borde frontal de la tecla más grave y luego el de la más aguda:
   - **con las manos**: deja la punta del índice derecho quieta ~1,5 s en cada punto;
   - **con el control**: toca con la punta y aprieta el gatillo.

   En modo mesa marcas dónde quieres los extremos del piano virtual.
2. En el **menú de la izquierda del piano** (se toca con el dedo): elige canción, dificultad, modo y dale **TOCAR**.

| Control | Acción |
|---|---|
| **A** | tocar / pausa |
| **B** | otra vez (o cancelar calibración) |
| **X** | práctica / concierto |
| **Y** | recalibrar |
| **Grip** (cualquiera) | Modo Estrella |
| Stick derecho ↑ ↓ / ← → | velocidad / mover en la canción |
| Stick izquierdo ← → / ↑ ↓ | canción / dificultad |

**Colores**: azul = mano derecha, verde = mano izquierda, blanco brillante = frase estrella, gris = suena sola. Sobre las teclas: azul = tócala ya, verde = bien, rojo = equivocada.

**Consejos para tocar con las manos**: buena luz, toca con la punta de los dedos y levántalos entre nota y nota. El seguimiento de manos tiene un margen de error de alrededor de un centímetro, así que funciona mejor en Fácil y Medio. Las notas equivocadas por ruido no rompen la racha en este modo.

## Agregar tus canciones

**Dentro del proyecto:** copia el `.mid` a `Assets/PianoVR/Resources/PianoVR/Songs/` y cámbiale la extensión a `.bytes`.

**Sin recompilar:**

```bash
adb push "mi cancion.mid" /sdcard/Android/data/<id.del.paquete>/files/Songs/
```

El id del paquete está en **Project Settings → Player → Android → Package Name**.

Funcionan mejor los MIDI **de piano solo con dos pistas** (mano derecha y mano izquierda). Si el archivo tiene una sola pista, lo que queda por debajo del Do central se asigna a la izquierda. Las canciones de ejemplo son de dominio público.

## Hoja de ruta

- [ ] Detectar notas con el **micrófono** de las gafas (pianos sin USB, más preciso que las manos)
- [ ] Teclados MIDI por **Bluetooth**
- [ ] **Anclas espaciales** para que la calibración sobreviva a cambios de guardian
- [ ] Partitura sobre el atril y estadísticas de progreso

## Notas técnicas

- El parser MIDI soporta SMF formato 0 y 1, running status y cambios de tempo (no división SMPTE).
- `PianoVR.asmdef` define `PIANOVR_XR_HANDS` solo si está instalado `com.unity.xr.hands`; sin el paquete compila igual.
- Una tecla se cuenta presionada cuando la punta del dedo **baja y cruza** la superficie (con histéresis), para no disparar notas al deslizar la mano apoyada.
- El récord solo se guarda en modo concierto a velocidad 100 % o más.
