using PianoVR.Midi;
using PianoVR.Piano;
using UnityEngine;

namespace PianoVR.Hands
{
    /// <summary>
    /// Detecta pulsaciones de teclas con el seguimiento de manos, sin teclado MIDI.
    /// Una tecla se presiona cuando la punta de un dedo BAJA y cruza la superficie de las teclas,
    /// y se suelta cuando el dedo vuelve a subir. Los eventos salen por <see cref="MidiInputHub"/>,
    /// así que para el resto del juego es igual que un teclado MIDI.
    /// </summary>
    public class HandKeyPresser : MonoBehaviour
    {
        public enum Surface { RealPiano, Table }

        [SerializeField] PianoLayout layout;
        [SerializeField] MidiInputHub hub;

        [Tooltip("Piano real: el dedo hunde la tecla. Mesa: el dedo toca la superficie.")]
        public Surface surface = Surface.Table;
        public bool useThumbs = true;

        [Header("Umbrales (metros sobre la superficie de las teclas blancas)")]
        public float tablePress = 0.012f;
        public float tableRelease = 0.024f;
        public float realPress = -0.004f;
        public float realRelease = 0.006f;

        public int ActiveFingers { get; private set; }

        const int Fingers = 5;
        readonly int[,] _pressed = new int[2, Fingers];
        readonly float[,] _prevHeight = new float[2, Fingers];
        readonly bool[,] _hadPrev = new bool[2, Fingers];
        readonly int[] _holdCount = new int[128];

        float PressHeight => surface == Surface.Table ? tablePress : realPress;
        float ReleaseHeight => surface == Surface.Table ? tableRelease : realRelease;

        void Awake()
        {
            for (int s = 0; s < 2; s++)
                for (int f = 0; f < Fingers; f++)
                    _pressed[s, f] = -1;
        }

        void Start()
        {
            if (layout == null) layout = FindFirstObjectByType<PianoLayout>();
            if (hub == null) hub = FindFirstObjectByType<MidiInputHub>();
        }

        void OnDisable() => ReleaseAll();

        void Update()
        {
            if (layout == null || hub == null || !layout.IsCalibrated) return;
            ActiveFingers = 0;

            for (int s = 0; s < 2; s++)
            {
                var side = (Side)s;
                for (int f = 0; f < Fingers; f++)
                {
                    if (f == (int)Finger.Thumb && !useThumbs) { Release(s, f); continue; }

                    if (!HandInput.TryGetFingerTip(side, (Finger)f, out var tip))
                    {
                        Release(s, f);
                        _hadPrev[s, f] = false;
                        continue;
                    }

                    ActiveFingers++;
                    Vector3 p = layout.ToPianoSpace(tip);
                    bool hadPrev = _hadPrev[s, f];
                    float prev = _prevHeight[s, f];
                    _prevHeight[s, f] = p.y;
                    _hadPrev[s, f] = true;
                    if (!hadPrev) continue; // primer cuadro rastreado: aún no sabemos si viene bajando

                    if (_pressed[s, f] >= 0)
                    {
                        bool outside = p.z < -0.03f || p.z > layout.whiteKeyDepth + 0.04f;
                        if (p.y > ReleaseHeight || outside) Release(s, f);
                        continue;
                    }

                    // Solo cuenta si el dedo viene de arriba (evita pulsaciones al deslizar la mano apoyada).
                    bool crossedDown = prev > PressHeight && p.y <= PressHeight && p.y > PressHeight - 0.05f;
                    if (!crossedDown) continue;

                    int pitch = layout.KeyAt(p);
                    if (pitch < 0 || !layout.Contains(pitch)) continue;

                    float speed = (prev - p.y) / Mathf.Max(Time.deltaTime, 0.001f);
                    int velocity = Mathf.RoundToInt(Mathf.Lerp(45f, 120f, Mathf.InverseLerp(0.05f, 0.6f, speed)));
                    Press(s, f, pitch, velocity);
                }
            }
        }

        void Press(int s, int f, int pitch, int velocity)
        {
            _pressed[s, f] = pitch;
            if (_holdCount[pitch]++ == 0) hub.Enqueue(0x90, pitch, velocity);
        }

        void Release(int s, int f)
        {
            int pitch = _pressed[s, f];
            if (pitch < 0) return;
            _pressed[s, f] = -1;
            if (--_holdCount[pitch] <= 0)
            {
                _holdCount[pitch] = 0;
                hub?.Enqueue(0x80, pitch, 0);
            }
        }

        public void ReleaseAll()
        {
            for (int s = 0; s < 2; s++)
                for (int f = 0; f < Fingers; f++)
                    Release(s, f);
        }
    }
}
