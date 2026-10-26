using PianoVR.Midi;
using UnityEngine;

namespace PianoVR.Piano
{
    /// <summary>
    /// Piano virtual que se dibuja sobre una mesa (modo sin piano real).
    /// Las teclas bajan y se iluminan cuando las presionas.
    /// </summary>
    public class VirtualKeyboard : MonoBehaviour
    {
        [SerializeField] PianoLayout layout;
        [SerializeField] MidiInputHub hub;
        [SerializeField] Material material;

        public Color white = new Color(0.94f, 0.94f, 0.92f);
        public Color black = new Color(0.08f, 0.08f, 0.1f);
        public Color pressedWhite = new Color(0.65f, 0.82f, 1f);
        public Color pressedBlack = new Color(0.2f, 0.45f, 0.85f);
        [Tooltip("Grosor visual de las teclas (m).")]
        public float whiteThickness = 0.012f;
        public float pressDepth = 0.006f;

        readonly Transform[] _keys = new Transform[128];
        readonly Renderer[] _renderers = new Renderer[128];
        readonly bool[] _shownPressed = new bool[128];
        readonly float[] _offset = new float[128];
        MaterialPropertyBlock _mpb;
        Transform _base;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Start()
        {
            if (layout == null) layout = FindFirstObjectByType<PianoLayout>();
            if (hub == null) hub = FindFirstObjectByType<MidiInputHub>();
            _mpb = new MaterialPropertyBlock();
            layout.Changed += Rebuild;
            Rebuild();
        }

        void OnEnable() { if (layout != null) Rebuild(); }

        void OnDestroy()
        {
            if (layout != null) layout.Changed -= Rebuild;
        }

        Transform MakeCube(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            var r = go.GetComponent<Renderer>();
            if (material) r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        void Rebuild()
        {
            if (_mpb == null || !layout.IsCalibrated) return;

            if (_base == null) _base = MakeCube("Base");
            float span = layout.WhiteKeyCount * layout.WhiteKeyWidth;
            Vector3 center = layout.Origin + layout.Right * ((layout.WhiteKeyCount - 1) * layout.WhiteKeyWidth * 0.5f)
                             + layout.Forward * (layout.whiteKeyDepth * 0.5f + 0.02f) - layout.Up * (whiteThickness + 0.004f);
            _base.SetPositionAndRotation(center, layout.Rotation);
            _base.localScale = new Vector3(span + 0.03f, 0.006f, layout.whiteKeyDepth + 0.05f);
            Paint(_base.GetComponent<Renderer>(), new Color(0.15f, 0.15f, 0.17f));

            for (int p = 0; p < 128; p++)
            {
                if (!layout.Contains(p))
                {
                    if (_keys[p]) _keys[p].gameObject.SetActive(false);
                    continue;
                }
                if (_keys[p] == null)
                {
                    _keys[p] = MakeCube("VKey " + p);
                    _renderers[p] = _keys[p].GetComponent<Renderer>();
                }
                _keys[p].gameObject.SetActive(true);
                _shownPressed[p] = false;
                _offset[p] = 0f;
                Place(p);
                Paint(_renderers[p], PianoLayout.IsBlack(p) ? black : white);
            }
        }

        void Place(int p)
        {
            bool isBlack = PianoLayout.IsBlack(p);
            float thickness = isBlack ? whiteThickness + layout.blackKeyHeight : whiteThickness;
            // La cara superior queda a la altura de la superficie calibrada (o de las negras).
            Vector3 top = layout.KeyTopCenter(p);
            Vector3 c = top - layout.Up * (thickness * 0.5f + _offset[p]);
            _keys[p].SetPositionAndRotation(c, layout.Rotation);
            float width = isBlack ? layout.KeyWidth(p) : layout.WhiteKeyWidth * 0.94f;
            _keys[p].localScale = new Vector3(width, thickness, layout.KeyDepth(p) * 0.98f);
        }

        void Paint(Renderer r, Color c)
        {
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColor, c);
            _mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(_mpb);
        }

        void LateUpdate()
        {
            if (!layout.IsCalibrated || hub == null) return;
            for (int p = 0; p < 128; p++)
            {
                if (_keys[p] == null || !layout.Contains(p)) continue;
                bool down = hub.Held[p];
                float target = down ? pressDepth : 0f;
                if (!Mathf.Approximately(_offset[p], target))
                {
                    _offset[p] = Mathf.MoveTowards(_offset[p], target, Time.deltaTime * 0.15f);
                    Place(p);
                }
                if (down != _shownPressed[p])
                {
                    _shownPressed[p] = down;
                    bool isBlack = PianoLayout.IsBlack(p);
                    Paint(_renderers[p], down ? (isBlack ? pressedBlack : pressedWhite) : (isBlack ? black : white));
                }
            }
        }
    }
}
