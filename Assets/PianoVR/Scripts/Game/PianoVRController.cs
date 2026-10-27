using System.Collections.Generic;
using System.IO;
using PianoVR.Audio;
using PianoVR.Hands;
using PianoVR.Midi;
using PianoVR.Piano;
using PianoVR.UI;
using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
// UnityEngine.InputSystem también define InputDevice y CommonUsages: fijamos las de XR.
using InputDevice = UnityEngine.XR.InputDevice;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace PianoVR.Game
{
    public enum InputMode { MidiKeyboard, HandsOnPiano, HandsOnTable }

    /// <summary>
    /// Orquesta la app: biblioteca de canciones, dificultad, forma de tocar (MIDI / manos), menú táctil,
    /// mandos de Quest y teclado del PC.
    ///
    /// Mando derecho:   A = tocar/pausa · B = otra vez · stick ↑↓ = velocidad · stick ←→ = mover · grip = Estrella
    /// Mando izquierdo: X = práctica/concierto · Y = calibrar · stick ←→ = canción · stick ↑↓ = dificultad · grip = Estrella
    /// PC (editor): Espacio = tocar/pausa · Retroceso = otra vez · 1 = mano · 2 = práctica/concierto · 3/4 = velocidad ·
    ///              5/6 = canción · 7 = dificultad · 8 = forma de tocar · 9 = Estrella · ←→ = mover · Enter = piano frente a la cámara
    /// </summary>
    public class PianoVRController : MonoBehaviour
    {
        [SerializeField] SongPlayer player;
        [SerializeField] ScoreSystem score;
        [SerializeField] PianoLayout layout;
        [SerializeField] KeyboardCalibrator calibrator;
        [SerializeField] MidiInputHub hub;
        [SerializeField] HandKeyPresser handPresser;
        [SerializeField] VirtualKeyboard virtualKeyboard;
        [SerializeField] GameAudio gameAudio;
        [SerializeField] GameMenu menu;
        [Tooltip("Carpeta dentro de Resources con canciones .bytes (archivos .mid renombrados).")]
        [SerializeField] string resourcesFolder = "PianoVR/Songs";

        public InputMode Mode { get; private set; } = InputMode.MidiKeyboard;

        /// <summary>Una canción de la biblioteca. group = compositor o "Mis canciones".</summary>
        class SongEntry
        {
            public string name, group, level, license;
            public System.Func<byte[]> load;
        }

        readonly List<SongEntry> _library = new List<SongEntry>();
        MemoryEngine _memory;
        const string MyGroup = "Mis canciones";
        const string ClassicalIndex = "PianoVR/Classical/index";
        int _songIndex;
        Song _rawSong;
        int _arrangedFor = -1; // rango de teclas para el que se arregló la canción
        TextMesh _message;
        Transform _head;
        string _flash;
        float _flashUntil;
        float _starGestureTime;

        readonly Dictionary<string, bool> _prevButtons = new Dictionary<string, bool>();
        float _rightStickCooldown, _leftStickCooldown;

        const string PrefMode = "PianoVR.Mode", PrefDifficulty = "PianoVR.Difficulty", PrefSong = "PianoVR.Song";

        /// <summary>Carpeta del visor donde puedes copiar tus .mid con adb (ver README).</summary>
        public static string UserSongsFolder => Path.Combine(Application.persistentDataPath, "Songs");

        void Awake()
        {
            // El Memory Engine se agrega solo si la escena es de una versión anterior.
            _memory = FindFirstObjectByType<MemoryEngine>();
            if (_memory == null) _memory = gameObject.AddComponent<MemoryEngine>();
        }

        void Start()
        {
            if (player == null) player = FindFirstObjectByType<SongPlayer>();
            if (score == null) score = FindFirstObjectByType<ScoreSystem>();
            if (layout == null) layout = FindFirstObjectByType<PianoLayout>();
            if (calibrator == null) calibrator = FindFirstObjectByType<KeyboardCalibrator>();
            if (hub == null) hub = FindFirstObjectByType<MidiInputHub>();
            if (handPresser == null) handPresser = FindFirstObjectByType<HandKeyPresser>();
            if (virtualKeyboard == null) virtualKeyboard = FindFirstObjectByType<VirtualKeyboard>(FindObjectsInactive.Include);
            if (gameAudio == null) gameAudio = FindFirstObjectByType<GameAudio>();
            if (menu == null) menu = FindFirstObjectByType<GameMenu>();
            _head = Camera.main != null ? Camera.main.transform : transform;

            CreateMessage();
            BuildMenu();

            score.difficulty = (Difficulty)PlayerPrefs.GetInt(PrefDifficulty, (int)Difficulty.Medio);
            // Por defecto: manos sobre el piano real. Si se conecta un teclado MIDI, cambia solo a MIDI.
            SetMode((InputMode)PlayerPrefs.GetInt(PrefMode, (int)InputMode.HandsOnPiano), announce: false);

            BuildLibrary();
            if (_library.Count > 0) LoadSong(Mathf.Clamp(PlayerPrefs.GetInt(PrefSong, 0), 0, _library.Count - 1));

            layout.Changed += OnLayoutChanged;
            _memory.LevelChanged += (level, up) =>
                Flash(up ? $"¡Memoria nivel {level}: {MemoryEngine.LevelName(level)}!"
                         : $"Memoria baja a nivel {level}: {MemoryEngine.LevelName(level)}");

            if (!layout.IsCalibrated)
            {
                if (Application.isEditor) layout.PlaceInFrontOf(_head);
                else calibrator.Begin();
            }

            hub.DeviceStatus += msg =>
            {
                // Si conectan un teclado MIDI, cambiamos solos a ese modo.
                if (msg.StartsWith("Conectado") && Mode != InputMode.MidiKeyboard) SetMode(InputMode.MidiKeyboard);
                Flash(msg);
            };
        }

        void OnDestroy()
        {
            if (layout != null) layout.Changed -= OnLayoutChanged;
        }

        // ---------- Menú táctil ----------

        bool InMenu => !player.IsPlaying || player.IsFinished || player.IsFailed;

        void BuildMenu()
        {
            if (menu == null) return;
            int row = 0;
            menu.Add(row++, 0, 1, () => player.IsPlaying ? "PAUSA" : player.IsFinished || player.IsFailed ? "OTRA VEZ" : "TOCAR",
                () => player.TogglePause(), highlighted: () => !player.IsPlaying);
            menu.Add(row++, 0, 1, () => "¡ESTRELLA!", () => score.ActivateStar(), visible: () => score.CanActivateStar,
                highlighted: () => true);
            menu.Add(row++, 0, 1, () => "OTRA VEZ", () => { player.Restart(); }, visible: () => InMenu && !player.IsFinished && !player.IsFailed);
            menu.Add(row, 0, 2, () => "< Canción", () => LoadSong(_songIndex - 1), visible: () => InMenu);
            menu.Add(row++, 1, 2, () => "Canción >", () => LoadSong(_songIndex + 1), visible: () => InMenu);
            menu.Add(row, 0, 2, () => "< Autor", () => JumpGroup(-1), visible: () => InMenu && _library.Count > 30);
            menu.Add(row++, 1, 2, () => "Autor >", () => JumpGroup(+1), visible: () => InMenu && _library.Count > 30);
            menu.Add(row++, 0, 1, MemoryLabel, ToggleMemory, visible: () => InMenu,
                highlighted: () => _memory.mode == MemoryEngine.Mode.Auto);
            menu.Add(row++, 0, 1, () => "Dificultad: " + SongArranger.Label(score.difficulty), CycleDifficulty, visible: () => InMenu);
            menu.Add(row++, 0, 1, () => player.waitMode ? "Modo: Práctica" : "Modo: Concierto", ToggleWait, visible: () => InMenu);
            menu.Add(row, 0, 2, () => "Vel -", () => ChangeSpeed(-0.1f), visible: () => InMenu);
            menu.Add(row++, 1, 2, () => "Vel +", () => ChangeSpeed(+0.1f), visible: () => InMenu);
            menu.Add(row++, 0, 1, () => "Tocar con: " + ModeLabel(Mode), CycleMode, visible: () => InMenu);
            menu.Add(row++, 0, 1, () => PianoLayout.SizeLabel(layout.Size), CycleSize, visible: () => InMenu);
            menu.Add(row++, 0, 1, () => "Calibrar", BeginCalibration, visible: () => InMenu,
                highlighted: () => !layout.IsCalibrated);
        }

        // ---------- Canciones ----------

        void BuildLibrary()
        {
            _library.Clear();

            // 1) Ejemplos y tus canciones dentro del proyecto.
            foreach (var asset in Resources.LoadAll<TextAsset>(resourcesFolder))
            {
                var a = asset;
                _library.Add(new SongEntry { name = a.name, group = MyGroup, load = () => a.bytes });
            }

            // 2) Tus canciones copiadas al visor con adb.
            try
            {
                Directory.CreateDirectory(UserSongsFolder);
                foreach (var file in Directory.GetFiles(UserSongsFolder))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".mid" && ext != ".midi") continue;
                    string f = file;
                    _library.Add(new SongEntry { name = Path.GetFileNameWithoutExtension(f), group = MyGroup, load = () => File.ReadAllBytes(f) });
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[PianoVR] No se pudo leer la carpeta de canciones: " + e.Message);
            }

            // 3) Biblioteca clásica (Mutopia). Se carga a pedido para no gastar memoria.
            var index = Resources.Load<TextAsset>(ClassicalIndex);
            if (index != null)
            {
                foreach (var line in index.text.Split('\n'))
                {
                    var f = line.TrimEnd('\r').Split('\t');
                    if (f.Length < 5 || f[0].StartsWith("#")) continue;
                    string path = f[0];
                    _library.Add(new SongEntry
                    {
                        name = f[1] + " - " + f[2], group = f[1], level = f[3], license = f[4],
                        load = () =>
                        {
                            var asset = Resources.Load<TextAsset>(path);
                            if (asset == null) throw new System.IO.FileNotFoundException(path);
                            var bytes = asset.bytes;
                            Resources.UnloadAsset(asset);
                            return bytes;
                        }
                    });
                }
            }

            // Mis canciones primero; luego por compositor, de más fácil a más difícil.
            _library.Sort((a, b) =>
            {
                bool am = a.group == MyGroup, bm = b.group == MyGroup;
                if (am != bm) return am ? -1 : 1;
                int g = string.Compare(a.group, b.group, System.StringComparison.OrdinalIgnoreCase);
                if (g != 0) return g;
                int l = string.CompareOrdinal(LevelOrder(a.level), LevelOrder(b.level));
                return l != 0 ? l : string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
            });
        }

        static string LevelOrder(string level) =>
            level == "Principiante" ? "1" : level == "Intermedio" ? "2" : level == "Avanzado" ? "3" : "0";

        void LoadSong(int index)
        {
            if (_library.Count == 0) return;
            _songIndex = (index % _library.Count + _library.Count) % _library.Count;
            PlayerPrefs.SetInt(PrefSong, _songIndex);
            var entry = _library[_songIndex];
            try
            {
                _rawSong = MidiFile.Parse(entry.load(), entry.name);
                Rearrange();
                int hs = ScoreSystem.HighScore(_rawSong.Name, score.difficulty);
                string info = string.IsNullOrEmpty(entry.level) ? "" : $"\n<size=40>{entry.level} · {_songIndex + 1}/{_library.Count}</size>";
                Flash((hs > 0 ? $"{_rawSong.Name} · Récord {hs:N0}" : _rawSong.Name) + info);
            }
            catch (System.Exception e)
            {
                Flash($"No se pudo abrir {entry.name}: {e.Message}");
            }
        }

        /// <summary>Salta a la primera canción del compositor anterior/siguiente.</summary>
        void JumpGroup(int dir)
        {
            if (_library.Count == 0) return;
            string current = _library[_songIndex].group;
            int i = _songIndex;
            for (int step = 0; step < _library.Count; step++)
            {
                i = (i + dir + _library.Count) % _library.Count;
                if (_library[i].group == current) continue;
                // Al ir hacia atrás, retrocede hasta la primera canción de ese grupo.
                string target = _library[i].group;
                while (dir < 0 && _library[(i - 1 + _library.Count) % _library.Count].group == target && i != 0) i--;
                LoadSong(i);
                return;
            }
        }

        string MemoryLabel()
        {
            if (_memory.mode == MemoryEngine.Mode.Off) return "Memoria: apagada";
            return $"Memoria: nivel {_memory.StoredLevel}";
        }

        void ToggleMemory()
        {
            _memory.ToggleMode();
            Flash(_memory.mode == MemoryEngine.Mode.Off
                ? "Memoria apagada: siempre verás todas las ayudas"
                : $"Memoria activa · nivel {_memory.StoredLevel}: {MemoryEngine.LevelName(_memory.StoredLevel)}\n<size=40>Toca bien la canción completa (90 %) para subir de nivel</size>");
        }

        /// <summary>Vuelve a preparar la canción con la dificultad y el tamaño de teclado actuales.</summary>
        void Rearrange()
        {
            if (_rawSong == null) return;
            var song = SongArranger.Arrange(_rawSong, score.difficulty, layout.LowestPitch, layout.HighestPitch);
            _arrangedFor = layout.LowestPitch * 1000 + layout.HighestPitch;
            player.Load(song);
        }

        void OnLayoutChanged()
        {
            int range = layout.LowestPitch * 1000 + layout.HighestPitch;
            if (range != _arrangedFor && !player.IsPlaying) Rearrange();
            if (layout.IsCalibrated && Mode != InputMode.HandsOnTable)
                Flash($"Piano listo: {PianoLayout.SizeLabel(layout.Size)} (si no es así, cámbialo en el menú)");
        }

        // ---------- Opciones ----------

        void CycleDifficulty() => SetDifficulty((Difficulty)(((int)score.difficulty + 1) % 4));

        void SetDifficulty(Difficulty d)
        {
            score.difficulty = d;
            PlayerPrefs.SetInt(PrefDifficulty, (int)d);
            Rearrange();
            Flash($"{SongArranger.Label(d)}: {SongArranger.Description(d)}");
        }

        void CycleMode() => SetMode((InputMode)(((int)Mode + 1) % 3));

        void SetMode(InputMode mode, bool announce = true)
        {
            Mode = mode;
            PlayerPrefs.SetInt(PrefMode, (int)mode);

            bool hands = mode != InputMode.MidiKeyboard;
            if (handPresser != null)
            {
                handPresser.enabled = hands;
                handPresser.surface = mode == InputMode.HandsOnTable ? HandKeyPresser.Surface.Table : HandKeyPresser.Surface.RealPiano;
            }
            // En el editor no hay piano real debajo: siempre se dibuja el virtual y suenan tus teclas.
            bool showVirtual = mode == InputMode.HandsOnTable || Application.isEditor;
            if (virtualKeyboard != null) virtualKeyboard.gameObject.SetActive(showVirtual);
            calibrator.TableMode = mode == InputMode.HandsOnTable;
            // Con piano real (MIDI o manos) tu piano ya suena; en la mesa suena el sintetizador.
            if (gameAudio != null) gameAudio.playOwnKeys = showVirtual;
            // El seguimiento de manos tiene más ruido: una nota fantasma no debería romperte la racha.
            score.wrongBreaksCombo = !hands;

            if (!announce) return;
            if (hands && !HandInput.Available) Flash("Activa el seguimiento de manos en las Quest (y suelta los controles)");
            else if (mode == InputMode.HandsOnTable)
            {
                if (layout.Size != KeyboardSize.Keys25 && layout.Size != KeyboardSize.Keys37) layout.Size = KeyboardSize.Keys25;
                Flash("Piano virtual: pulsa Calibrar y marca los extremos sobre una mesa");
            }
            else Flash("Tocar con: " + ModeLabel(mode));
        }

        static string ModeLabel(InputMode m) =>
            m == InputMode.MidiKeyboard ? "teclado MIDI" : m == InputMode.HandsOnPiano ? "manos (piano real)" : "manos (mesa)";

        void CycleSize()
        {
            KeyboardSize[] order = { KeyboardSize.Keys25, KeyboardSize.Keys37, KeyboardSize.Keys49, KeyboardSize.Keys61, KeyboardSize.Keys76, KeyboardSize.Keys88 };
            int i = System.Array.IndexOf(order, layout.Size);
            layout.Size = order[(i + 1) % order.Length];
            Flash(PianoLayout.SizeLabel(layout.Size) + " · recalibra si cambió tu teclado");
        }

        void BeginCalibration()
        {
            player.Pause();
            calibrator.Begin();
        }

        void ChangeSpeed(float delta)
        {
            player.speed = Mathf.Clamp(Mathf.Round((player.speed + delta) * 10f) / 10f, 0.3f, 1.5f);
            Flash($"Velocidad {player.speed:P0}" + (player.speed < 1f ? " (sin récords)" : ""));
        }

        void ToggleWait()
        {
            player.waitMode = !player.waitMode;
            Flash(player.waitMode ? "Práctica: la canción te espera y no pierdes" : "Concierto: ¡no dejes que te abucheen!");
        }

        void CycleHand()
        {
            player.practiceHand = (PracticeHand)(((int)player.practiceHand + 1) % 3);
            score.ResetScore();
            Flash("Practicar: " + (player.practiceHand == PracticeHand.Both ? "ambas manos" : player.practiceHand == PracticeHand.Right ? "mano derecha" : "mano izquierda"));
        }

        void Flash(string msg)
        {
            _flash = msg;
            _flashUntil = Time.time + 3f;
        }

        // ---------- Entrada ----------

        void Update()
        {
            HandleControllers();
            HandleKeyboard();
            HandleStarGesture();
            UpdateMessage();
        }

        void HandleControllers()
        {
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            bool calibrating = calibrator.Current != KeyboardCalibrator.Step.Idle;

            if (Down(right, CommonUsages.primaryButton, "A") && !calibrating) player.TogglePause();
            if (Down(right, CommonUsages.secondaryButton, "B"))
            {
                if (calibrating) calibrator.Cancel(); else player.Restart();
            }
            if (Down(left, CommonUsages.primaryButton, "X")) ToggleWait();
            if (Down(left, CommonUsages.secondaryButton, "Y")) BeginCalibration();
            if (Down(right, CommonUsages.gripButton, "RG") || Down(left, CommonUsages.gripButton, "LG")) score.ActivateStar();

            _rightStickCooldown -= Time.deltaTime;
            _leftStickCooldown -= Time.deltaTime;

            if (right.TryGetFeatureValue(CommonUsages.primary2DAxis, out var rs) && _rightStickCooldown <= 0f)
            {
                if (rs.y > 0.7f) { ChangeSpeed(+0.1f); _rightStickCooldown = 0.25f; }
                else if (rs.y < -0.7f) { ChangeSpeed(-0.1f); _rightStickCooldown = 0.25f; }
                else if (rs.x > 0.7f) { player.Seek(player.Time + 5f); _rightStickCooldown = 0.3f; }
                else if (rs.x < -0.7f) { player.Seek(player.Time - 5f); _rightStickCooldown = 0.3f; }
            }

            if (left.TryGetFeatureValue(CommonUsages.primary2DAxis, out var ls) && _leftStickCooldown <= 0f && InMenu)
            {
                if (ls.x > 0.7f) { LoadSong(_songIndex + 1); _leftStickCooldown = 0.4f; }
                else if (ls.x < -0.7f) { LoadSong(_songIndex - 1); _leftStickCooldown = 0.4f; }
                else if (Mathf.Abs(ls.y) > 0.7f) { CycleDifficulty(); _leftStickCooldown = 0.4f; }
            }
        }

        /// <summary>Levantar ambas manos por encima de la cabeza activa el Modo Estrella (como inclinar la guitarra).</summary>
        void HandleStarGesture()
        {
            if (!score.CanActivateStar) { _starGestureTime = 0f; return; }
            bool l = HandInput.TryGetFingerTip(Side.Left, Finger.Index, out var lt);
            bool r = HandInput.TryGetFingerTip(Side.Right, Finger.Index, out var rt);
            float headY = _head.position.y;
            if (l && r && lt.y > headY + 0.05f && rt.y > headY + 0.05f)
            {
                _starGestureTime += Time.deltaTime;
                if (_starGestureTime > 0.15f) { score.ActivateStar(); _starGestureTime = 0f; }
            }
            else _starGestureTime = 0f;
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
            if (k.digit7Key.wasPressedThisFrame) CycleDifficulty();
            if (k.digit8Key.wasPressedThisFrame) CycleMode();
            if (k.digit9Key.wasPressedThisFrame) score.ActivateStar();
            if (k.digit0Key.wasPressedThisFrame) ToggleMemory();
            if (k.mKey.wasPressedThisFrame) _memory.SetLevel((_memory.StoredLevel + 1) % (MemoryEngine.MaxLevel + 1));
            if (k.pageUpKey.wasPressedThisFrame) JumpGroup(-1);
            if (k.pageDownKey.wasPressedThisFrame) JumpGroup(+1);
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
            if (Input.GetKeyDown(KeyCode.Alpha7)) CycleDifficulty();
            if (Input.GetKeyDown(KeyCode.Alpha8)) CycleMode();
            if (Input.GetKeyDown(KeyCode.Alpha9)) score.ActivateStar();
            if (Input.GetKeyDown(KeyCode.Alpha0)) ToggleMemory();
            if (Input.GetKeyDown(KeyCode.M)) _memory.SetLevel((_memory.StoredLevel + 1) % (MemoryEngine.MaxLevel + 1));
            if (Input.GetKeyDown(KeyCode.PageUp)) JumpGroup(-1);
            if (Input.GetKeyDown(KeyCode.PageDown)) JumpGroup(+1);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) player.Seek(player.Time - 5f);
            if (Input.GetKeyDown(KeyCode.RightArrow)) player.Seek(player.Time + 5f);
            if (Input.GetKeyDown(KeyCode.Return)) layout.PlaceInFrontOf(_head);
#endif
        }

        // ---------- Mensajes ----------

        void CreateMessage()
        {
            var go = new GameObject("Message");
            go.transform.SetParent(transform, false);
            _message = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _message.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            _message.fontSize = 60;
            _message.characterSize = 0.0035f;
            _message.anchor = TextAnchor.MiddleCenter;
            _message.alignment = TextAlignment.Center;
            _message.color = new Color(1f, 0.92f, 0.5f);
        }

        void UpdateMessage()
        {
            bool calibrating = calibrator.Current != KeyboardCalibrator.Step.Idle;
            string text = "";

            if (calibrating)
            {
                text = calibrator.Instructions;
                if (calibrator.UsingHands && calibrator.DwellProgress > 0f)
                    text += $"\n{new string('|', Mathf.RoundToInt(calibrator.DwellProgress * 20))}";
                text += "\n<size=40>(B = cancelar)</size>";
            }
            else if (Time.time < _flashUntil)
            {
                text = _flash;
            }

            _message.text = text;
            _message.gameObject.SetActive(text.Length > 0);
            if (text.Length == 0) return;

            if (calibrating || !layout.IsCalibrated)
            {
                Vector3 fwd = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                _message.transform.SetPositionAndRotation(_head.position + fwd * 0.75f + Vector3.up * 0.05f, Quaternion.LookRotation(fwd));
            }
            else
            {
                float span = (layout.WhiteKeyCount - 1) * layout.WhiteKeyWidth;
                Vector3 pos = layout.Origin + layout.Right * (span * 0.5f) + layout.Forward * (layout.whiteKeyDepth + 0.06f) + layout.Up * 0.82f;
                _message.transform.SetPositionAndRotation(pos, layout.Rotation);
            }
        }
    }
}
