using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PianoVR.Midi
{
    /// <summary>
    /// Simula un teclado MIDI con el teclado del PC para probar sin gafas ni piano.
    /// Fila A-W-S-E-D-F-T-G-Y-H-U-J-K = Do a Do (como en los DAW). Z / X bajan / suben octava.
    /// </summary>
    public class EditorKeyboardInput : MonoBehaviour
    {
        [SerializeField] MidiInputHub hub;
        [Tooltip("Tono MIDI de la tecla A del PC (60 = Do central).")]
        [SerializeField] int basePitch = 60;
        [SerializeField] bool onlyInEditor = true;

#if ENABLE_INPUT_SYSTEM
        static readonly Key[] Keys =
        {
            Key.A, Key.W, Key.S, Key.E, Key.D, Key.F, Key.T, Key.G, Key.Y, Key.H, Key.U, Key.J,
            Key.K, Key.O, Key.L, Key.P, Key.Semicolon
        };
#else
        static readonly KeyCode[] Keys =
        {
            KeyCode.A, KeyCode.W, KeyCode.S, KeyCode.E, KeyCode.D, KeyCode.F, KeyCode.T, KeyCode.G,
            KeyCode.Y, KeyCode.H, KeyCode.U, KeyCode.J, KeyCode.K, KeyCode.O, KeyCode.L, KeyCode.P,
            KeyCode.Semicolon
        };
#endif

        readonly int[] _pressedPitch = new int[17];

        void Start()
        {
            if (onlyInEditor && !Application.isEditor) { enabled = false; return; }
            if (hub == null) hub = FindFirstObjectByType<MidiInputHub>();
            hub?.ReportStatus("Teclado del PC activo (A W S E D F ... = Do Do# Re ...)");
        }

        void Update()
        {
            if (hub == null) return;

            if (Pressed(OctaveDown)) basePitch = Mathf.Max(12, basePitch - 12);
            if (Pressed(OctaveUp)) basePitch = Mathf.Min(108, basePitch + 12);

            for (int i = 0; i < Keys.Length; i++)
            {
                if (Pressed(Keys[i]))
                {
                    _pressedPitch[i] = basePitch + i;
                    hub.Enqueue(0x90, _pressedPitch[i], 100);
                }
                else if (Released(Keys[i]))
                {
                    hub.Enqueue(0x80, _pressedPitch[i], 0);
                }
            }
        }

#if ENABLE_INPUT_SYSTEM
        const Key OctaveDown = Key.Z, OctaveUp = Key.X;
        static bool Pressed(Key k) => Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame;
        static bool Released(Key k) => Keyboard.current != null && Keyboard.current[k].wasReleasedThisFrame;
#else
        const KeyCode OctaveDown = KeyCode.Z, OctaveUp = KeyCode.X;
        static bool Pressed(KeyCode k) => Input.GetKeyDown(k);
        static bool Released(KeyCode k) => Input.GetKeyUp(k);
#endif
    }
}
