using System.Collections.Generic;
using System.IO;
using System.Text;
using PianoVR.Midi;
using PianoVR.Piano;
using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PianoVR.Game
{
    /// <summary>
    /// Orquesta la app: biblioteca de canciones, controles (mandos de Quest y teclado del PC) y el panel de texto.
    ///
    /// Mando derecho:  A = play/pausa · B = reiniciar · stick ↑↓ = velocidad · stick ←→ = retroceder/avanzar
    /// Mando izquierdo: X = modo espera · Y = calibrar · stick ←→ = canción · stick ↑↓ = mano
    /// PC (editor): Espacio = play/pausa · Retroceso = reiniciar · 1 = mano · 2 = modo espera ·
    ///              3/4 = velocidad · 5/6 = canción · ←→ = mover · Enter = piano virtual frente a la cámara
    /// </summary>
    public class PianoVRController : MonoBehaviour
    {
        [SerializeField] SongPlayer player;
        [SerializeField] PianoLayout layout;
        [SerializeField] KeyboardCalibrator calibrator;
        [SerializeField] MidiInputHub hub;
        [Tooltip("Carpeta dentro de Resources con canciones .bytes (archivos .mid renombrados).")]
        [SerializeField] string resourcesFolder = "PianoVR/Songs";

        readonly List<(string name, System.Func<byte[]> load)> _library = new List<(string, System.Func<byte[]>)>();
        int _songIndex;
        TextMesh _hud;
        Transform _head;
        string _flash;
        float _flashUntil;

        readonly Dictionary<string, bool> _prevButtons = new Dictionary<string, bool>();
        float _rightStickCooldown, _leftStickCooldown;

        /// <summary>Carpeta del visor donde puedes copiar tus .mid con adb (ver README).</summary>
        public static string UserSongsFolder => Path.Combine(Application.persistentDataPath, "Songs");

        void Start()
        {
            if (player == null) player = FindFirstObjectByType<SongPlayer>();
            if (layout == null) layout = FindFirstObjectByType<PianoLayout>();
            if (calibrator == null) calibrator = FindFirstObjectByType<KeyboardCalibrator>();
            if (hub == null) hub = FindFirstObjectByType<MidiInputHub>();
            _head = Camera.main != null ? Camera.main.transform : transform;

            CreateHud();
            BuildLibrary();
            if (_library.Count > 0) LoadSong(0);

            if (!layout.IsCalibrated)
            {
                if (Application.isEditor) layout.PlaceInFrontOf(_head);
                else calibrator.Begin();
            }

            player.Finished += () => Flash($"¡Terminaste! Precisión {player.Accuracy:P0}");
        }

        // ---------- Canciones ----------

        void BuildLibrary()
        {
            _library.Clear();
            foreach (var asset in Resources.LoadAll<TextAsset>(resourcesFolder))
            {
                var a = asset;
                _library.Add((a.name, () => a.bytes));
            }

            try
            {
                Directory.CreateDirectory(UserSongsFolder);
                foreach (var file in Directory.GetFiles(UserSongsFolder))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".mid" && ext != ".midi") continue;
                    string f = file;
                    _library.Add((Path.GetFileNameWithoutExtension(f), () => File.ReadAllBytes(f)));
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[PianoVR] No se pudo leer la carpeta de canciones: " + e.Message);
            }

            _library.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
        }

        void LoadSong(int index)
        {
            if (_library.Count == 0) return;
            _songIndex = (index % _library.Count + _library.Count) % _library.Count;
            var entry = _library[_songIndex];
            try
            {
                var song = MidiFile.Parse(entry.load(), entry.name);
                player.Load(song);
                Flash($"Canción: {song.Name}  ({song.Notes.Count} notas)");
                if (song.MinPitch < layout.LowestPitch || song.MaxPitch > layout.HighestPitch)
                    Flash("Ojo: la canción tiene notas fuera de tu teclado");
            }
            catch (System.Exception e)
            {
                Flash($"No se pudo abrir {entry.name}: {e.Message}");
            }
        }

        // ---------- Entrada ----------

        void Update()
        {
            HandleControllers();
            HandleKeyboard();
            UpdateHud();
        }

        void HandleControllers()
        {
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            bool calibrating = calibrator.Current != KeyboardCalibrator.Step.Idle;

            if (Down(right, CommonUsages.primaryButton, "A") && !calibrating) player.TogglePause();
            if (Down(right, CommonUsages.secondaryButton, "B"))
            {
                if (calibrating) calibrator.Cancel(); else { player.Restart(); Flash("Reiniciado"); }
            }
            if (Down(left, CommonUsages.primaryButton, "X")) ToggleWait();
            if (Down(left, CommonUsages.secondaryButton, "Y")) { player.Pause(); calibrator.Begin(); }

            _rightStickCooldown -= Time.deltaTime;
            _leftStickCooldown -= Time.deltaTime;

            if (right.TryGetFeatureValue(CommonUsages.primary2DAxis, out var rs) && _rightStickCooldown <= 0f)
            {
                if (rs.y > 0.7f) { ChangeSpeed(+0.1f); _rightStickCooldown = 0.25f; }
                else if (rs.y < -0.7f) { ChangeSpeed(-0.1f); _rightStickCooldown = 0.25f; }
                else if (rs.x > 0.7f) { player.Seek(player.Time + 5f); _rightStickCooldown = 0.3f; }
                else if (rs.x < -0.7f) { player.Seek(player.Time - 5f); _rightStickCooldown = 0.3f; }
            }

            if (left.TryGetFeatureValue(CommonUsages.primary2DAxis, out var ls) && _leftStickCooldown <= 0f)
            {
                if (ls.x > 0.7f) { LoadSong(_songIndex + 1); _leftStickCooldown = 0.4f; }
                else if (ls.x < -0.7f) { LoadSong(_songIndex - 1); _leftStickCooldown = 0.4f; }
                else if (Mathf.Abs(ls.y) > 0.7f) { CycleHand(); _leftStickCooldown = 0.4f; }
            }
        }

        bool Down(InputDevice d, InputFeatureUsage<bool> usage, string id)
        {
            bool now = d.isValid && d.TryGetFeatureValue(usage, out bool v) && v;
            _prevButtons.TryGetValue(id, out bool before);
            _prevButtons[id] = now;
            return now && !before;
        }

        void HandleKeyboard()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return;
            if (k.spaceKey.wasPressedThisFrame) player.TogglePause();
            if (k.backspaceKey.wasPressedThisFrame) player.Restart();
            if (k.digit1Key.wasPressedThisFrame) CycleHand();
            if (k.digit2Key.wasPressedThisFrame) ToggleWait();
            if (k.digit3Key.wasPressedThisFrame) ChangeSpeed(-0.1f);
            if (k.digit4Key.wasPressedThisFrame) ChangeSpeed(+0.1f);
            if (k.digit5Key.wasPressedThisFrame) LoadSong(_songIndex - 1);
            if (k.digit6Key.wasPressedThisFrame) LoadSong(_songIndex + 1);
            if (k.leftArrowKey.wasPressedThisFrame) player.Seek(player.Time - 5f);
            if (k.rightArrowKey.wasPressedThisFrame) player.Seek(player.Time + 5f);
            if (k.enterKey.wasPressedThisFrame) layout.PlaceInFrontOf(_head);
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Space)) player.TogglePause();
            if (Input.GetKeyDown(KeyCode.Backspace)) player.Restart();
            if (Input.GetKeyDown(KeyCode.Alpha1)) CycleHand();
            if (Input.GetKeyDown(KeyCode.Alpha2)) ToggleWait();
            if (Input.GetKeyDown(KeyCode.Alpha3)) ChangeSpeed(-0.1f);
            if (Input.GetKeyDown(KeyCode.Alpha4)) ChangeSpeed(+0.1f);
            if (Input.GetKeyDown(KeyCode.Alpha5)) LoadSong(_songIndex - 1);
            if (Input.GetKeyDown(KeyCode.Alpha6)) LoadSong(_songIndex + 1);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) player.Seek(player.Time - 5f);
            if (Input.GetKeyDown(KeyCode.RightArrow)) player.Seek(player.Time + 5f);
            if (Input.GetKeyDown(KeyCode.Return)) layout.PlaceInFrontOf(_head);
#endif
        }

        void ChangeSpeed(float delta)
        {
            player.speed = Mathf.Clamp(Mathf.Round((player.speed + delta) * 10f) / 10f, 0.3f, 1.5f);
            Flash($"Velocidad {player.speed:P0}");
        }

        void ToggleWait()
        {
            player.waitMode = !player.waitMode;
            Flash(player.waitMode ? "Modo espera: la canción te espera" : "Modo libre: la canción no se detiene");
        }

        void CycleHand()
        {
            player.practiceHand = (PracticeHand)(((int)player.practiceHand + 1) % 3);
            Flash("Practicar: " + HandName(player.practiceHand));
        }

        static string HandName(PracticeHand h) =>
            h == PracticeHand.Both ? "ambas manos" : h == PracticeHand.Right ? "mano derecha" : "mano izquierda";

        void Flash(string msg)
        {
            _flash = msg;
            _flashUntil = Time.time + 2.5f;
        }

        // ---------- Panel ----------

        void CreateHud()
        {
            var go = new GameObject("HUD");
            go.transform.SetParent(transform, false);
            _hud = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _hud.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            _hud.fontSize = 60;
            _hud.characterSize = 0.0042f;
            _hud.anchor = TextAnchor.LowerCenter;
            _hud.alignment = TextAlignment.Center;
            _hud.color = Color.white;
        }

        void UpdateHud()
        {
            var sb = new StringBuilder();
            bool calibrating = calibrator.Current != KeyboardCalibrator.Step.Idle;

            if (calibrating)
            {
                sb.AppendLine(calibrator.Instructions);
                sb.Append("<size=40>(B = cancelar)</size>");
                PlaceHudInFrontOfHead();
            }
            else
            {
                if (player.Song != null)
                {
                    sb.AppendLine($"<b>{player.Song.Name}</b>   {FormatTime(player.Time)} / {FormatTime(player.Song.Length)}");
                    string state = player.IsFinished ? "Terminada (A = otra vez)"
                        : !player.IsPlaying ? "Pausa (A = tocar)"
                        : player.Time < 0 ? $"Empieza en {Mathf.CeilToInt(-player.Time)}…"
                        : player.IsWaiting ? "Esperando tu nota…" : "Tocando";
                    sb.AppendLine(state);
                    sb.AppendLine($"<size=44>Velocidad {player.speed:P0} · {(player.waitMode ? "Espera" : "Libre")} · {HandName(player.practiceHand)}</size>");
                    sb.AppendLine($"<size=44>Aciertos {player.Hits}   Fallos {player.Misses + player.WrongNotes}   Racha {player.Streak}   Precisión {player.Accuracy:P0}</size>");
                }
                else
                {
                    sb.AppendLine("No hay canciones. Agrega .bytes en Resources/PianoVR/Songs");
                }

                if (Time.time < _flashUntil) sb.AppendLine($"<color=#FFE066>{_flash}</color>");
                sb.Append($"<size=36><color=#AAAAAA>{hub?.LastDeviceMessage}</color></size>");

                if (layout.IsCalibrated) PlaceHudAbovePiano(); else PlaceHudInFrontOfHead();
            }

            _hud.text = sb.ToString();
        }

        void PlaceHudAbovePiano()
        {
            float span = (layout.WhiteKeyCount - 1) * layout.WhiteKeyWidth;
            Vector3 pos = layout.Origin + layout.Right * (span * 0.5f) + layout.Forward * (layout.whiteKeyDepth + 0.05f)
                          + layout.Up * 0.62f;
            _hud.transform.SetPositionAndRotation(pos, layout.Rotation);
        }

        void PlaceHudInFrontOfHead()
        {
            Vector3 fwd = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            _hud.transform.SetPositionAndRotation(_head.position + fwd * 0.8f - Vector3.up * 0.1f, Quaternion.LookRotation(fwd));
        }

        static string FormatTime(float s)
        {
            s = Mathf.Max(0f, s);
            return $"{(int)(s / 60)}:{(int)(s % 60):00}";
        }
    }
}
