# PianoVR

App de realidad mixta para **Meta Quest 3** que enseña a tocar piano sobre tu **piano real**: con passthrough ves tu teclado y encima caen las notas en 3D, alineadas con cada tecla. Conectas un teclado MIDI por USB-C y la app sabe qué tocaste.

> Estado: **v0.1, base jugable**. Calibración, notas que caen, resaltado de teclas, evaluación de aciertos, modo espera, práctica por mano y 3 canciones de ejemplo.

## Cómo funciona

```
Teclado MIDI ──USB-C──▶ Quest 3 (Android MIDI API) ──▶ MidiBridge.java ──▶ MidiInputHub
                                                                              │
Archivo .mid ──▶ MidiFile (parser) ──▶ SongPlayer (reloj + evaluación) ◀─────┘
                                            │
                PianoLayout (calibración) ──┼──▶ NoteHighway   (barras que caen)
                                            └──▶ KeyHighlighter (azul / verde / rojo sobre cada tecla)
```

| Carpeta | Qué hay |
|---|---|
| `Assets/PianoVR/Scripts/Midi` | Lector de archivos MIDI, entrada MIDI de Android y simulador con el teclado del PC |
| `Assets/PianoVR/Scripts/Piano` | Geometría del piano, calibración con el control y resaltado de teclas |
| `Assets/PianoVR/Scripts/Game` | Reloj de la canción, notas que caen y controlador general (mandos + panel) |
| `Assets/PianoVR/Scripts/Editor` | Menú **PianoVR > Agregar PianoVR a la escena** |
| `Assets/Plugins/Android/PianoVR` | `MidiBridge.java`, puente con `android.media.midi` |
| `Assets/PianoVR/Resources/PianoVR/Songs` | Canciones de ejemplo (archivos `.mid` con extensión `.bytes`) |

El código no depende de ningún SDK concreto: usa la API XR estándar de Unity (`UnityEngine.XR`), así que funciona con OpenXR o con el Meta XR SDK.

## Requisitos

- **Unity 6 (LTS)** con el módulo **Android Build Support** (incluye OpenJDK, SDK y NDK). Se instala desde Unity Hub.
- Quest 3 en **modo desarrollador**.
- Un **teclado MIDI USB** (casi todos los pianos digitales modernos tienen puerto USB-B "to host") y un cable **USB-B a USB-C**. Si tu teclado es USB-A, un adaptador OTG USB-C.

## Montar el proyecto

1. En Unity Hub: **New project → plantilla "Mixed Reality"** → nombre `PianoVR`. La plantilla ya trae OpenXR, el soporte de Meta Quest y el passthrough configurados.
2. Trae este código dentro de la carpeta del proyecto (con Unity cerrado):
   ```bash
   cd ruta/a/PianoVR
   git init
   git remote add origin https://github.com/USUARIO/REPO.git
   git pull origin main
   ```
3. Abre el proyecto. En la escena de ejemplo de la plantilla usa el menú **PianoVR → Agregar PianoVR a la escena**. Arma todo y crea los materiales.
4. Si quieres versionar el proyecto completo: `git add . && git commit -m "Proyecto Unity" && git push origin main` (el `.gitignore` ya excluye `Library/` y las compilaciones).

## Probar en el PC (sin gafas)

Dale **Play** en el editor. Aparece un piano virtual frente a la cámara y tocas con el teclado del PC:

| Tecla | Acción |
|---|---|
| `A W S E D F T G Y H U J K O L P ;` | Do, Do#, Re, Re#, Mi… (como en los DAW) |
| `Z` / `X` | bajar / subir una octava |
| `Espacio` | play / pausa |
| `Retroceso` | reiniciar |
| `1` | cambiar mano (ambas / derecha / izquierda) |
| `2` | modo espera on/off |
| `3` / `4` | menos / más velocidad |
| `5` / `6` | canción anterior / siguiente |
| `←` / `→` | retroceder / avanzar 5 s |
| `Enter` | recolocar el piano virtual frente a la cámara |

## Compilar para las Quest 3

1. **File → Build Profiles → Meta Quest** (o Android) → **Switch Platform**.
2. Conecta las gafas por USB, acepta la depuración USB en el visor.
3. **Build And Run**.

## Usarlo en las gafas

1. Conecta el teclado MIDI al puerto USB-C de las Quest. El panel muestra `Conectado: <nombre del teclado>`.
2. **Calibración** (la primera vez arranca sola): con la punta del **control derecho** toca el **borde frontal de la tecla más grave** y aprieta el gatillo; luego el de la **más aguda**. Queda guardada. Si mueves el piano, recalibra con **Y**.
3. Toca.

| Control | Acción |
|---|---|
| **A** | play / pausa |
| **B** | reiniciar (o cancelar calibración) |
| Stick derecho ↑ ↓ | velocidad |
| Stick derecho ← → | retroceder / avanzar 5 s |
| **X** | modo espera on/off |
| **Y** | recalibrar |
| Stick izquierdo ← → | cambiar canción |
| Stick izquierdo ↑ ↓ | cambiar mano |

**Colores:** barras azules = mano derecha, verdes = mano izquierda, grises = la mano que no estás practicando. Sobre las teclas: azul = tócala ya, verde = bien, rojo = equivocada.

El tamaño del teclado se elige en el objeto **Piano** (componente `PianoLayout`): 49, 61 (por defecto), 76 u 88 teclas.

## Agregar tus canciones

**Dentro del proyecto:** copia tu archivo `.mid` a `Assets/PianoVR/Resources/PianoVR/Songs/` y cámbiale la extensión a `.bytes`.

**Sin recompilar:** cópialo directo al visor con adb:

```bash
adb push "mi cancion.mid" /sdcard/Android/data/<id.del.paquete>/files/Songs/
```

El id del paquete está en **Project Settings → Player → Android → Package Name**. La app lista la carpeta al arrancar.

Si el archivo trae **dos pistas** con notas, la primera se toma como mano derecha y la segunda como izquierda; si no, todo lo que está por debajo del Do central se asigna a la izquierda. Las canciones de ejemplo (escala de Do, Estrellita e Himno de la Alegría) son de dominio público.

## Hoja de ruta

- [ ] Teclados MIDI por **Bluetooth** (BLE MIDI)
- [ ] Modo **sin teclado MIDI**: detectar las pulsaciones con el seguimiento de manos
- [ ] **Anclas espaciales** para que la calibración sobreviva aunque cambie el origen del guardian
- [ ] Sonido propio para teclados sin parlante
- [ ] Menú en realidad mixta (selector de canciones, ajustes)
- [ ] Partitura sobre el atril y estadísticas por sesión

## Notas técnicas

- El parser MIDI soporta SMF formato 0 y 1, running status y cambios de tempo. Los archivos con división SMPTE (raros) no están soportados.
- `MidiBridge.java` abre cualquier dispositivo MIDI con puerto de salida, también los que se conectan después de abrir la app, y descarta mensajes de reloj y sysex.
- En modo espera la canción se detiene en cada nota hasta que la toques; en modo libre las notas que pasan sin tocarse cuentan como fallo.
