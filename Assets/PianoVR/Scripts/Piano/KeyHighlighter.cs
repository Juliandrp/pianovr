using PianoVR.Game;
using PianoVR.Midi;
using UnityEngine;

namespace PianoVR.Piano
{
    /// <summary>
    /// Pinta encima de cada tecla real: azul = tócala ahora, verde = bien tocada, rojo = equivocada.
    /// </summary>
    public class KeyHighlighter : MonoBehaviour
    {
        [SerializeField] PianoLayout layout;
        [SerializeField] SongPlayer player;
        [SerializeField] MidiInputHub hub;
        [SerializeField] Material material;

        public Color expected = new Color(0.25f, 0.6f, 1f, 0.75f);
        public Color correct = new Color(0.3f, 1f, 0.45f, 0.8f);
        public Color wrong = new Color(1f, 0.25f, 0.25f, 0.8f);
        [Tooltip("Segundos (reales) de anticipación para iluminar la tecla que viene.")]
        public float anticipation = 0.25f;

        Renderer[] _keys = new Renderer[128];
        readonly bool[] _expected = new bool[128];
        readonly bool[] _sounding = new bool[128];
        MaterialPropertyBlock _mpb;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Start()
        {
            if (layout == null) layout = FindFirstObjectByType<PianoLayout>();
            if (player == null) player = FindFirstObjectByType<SongPlayer>();
            if (hub == null) hub = FindFirstObjectByType<MidiInputHub>();
            _mpb = new MaterialPropertyBlock();
            layout.Changed += Rebuild;
            if (layout.IsCalibrated) Rebuild();
        }

        void OnDestroy()
        {
            if (layout != null) layout.Changed -= Rebuild;
        }

        void Rebuild()
        {
            for (int p = 0; p < 128; p++)
            {
                if (!layout.Contains(p))
                {
                    if (_keys[p]) _keys[p].gameObject.SetActive(false);
                    continue;
                }

                if (_keys[p] == null)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "Key " + p;
                    Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(transform, false);
                    _keys[p] = go.GetComponent<Renderer>();
                    _keys[p].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (material) _keys[p].sharedMaterial = material;
                }

                var t = _keys[p].transform;
                // Un poco por encima de la superficie para no "hundirse" en la tecla real.
                t.SetPositionAndRotation(layout.KeyTopCenter(p) + layout.Up * 0.003f, layout.Rotation);
                t.localScale = new Vector3(layout.KeyWidth(p), 0.002f, layout.KeyDepth(p) * 0.9f);
                _keys[p].gameObject.SetActive(false);
            }
        }

        void LateUpdate()
        {
            if (!layout.IsCalibrated) return;

            System.Array.Clear(_expected, 0, 128);
            System.Array.Clear(_sounding, 0, 128);

            if (player.Song != null)
            {
                var notes = player.Song.Notes;
                float now = player.Time;
                float ahead = anticipation * player.speed;
                for (int i = 0; i < notes.Count; i++)
                {
                    var n = notes[i];
                    if (n.Start > now + ahead) break;
                    if (n.End < now - 0.5f) continue;
                    var res = player.ResultOf(i);
                    if (res == SongPlayer.NoteResult.Pending && player.IsActiveHand(n.Hand) && n.Start >= now - 0.5f)
                        _expected[n.Pitch] = true;
                    if (res == SongPlayer.NoteResult.Hit && n.Start <= now + ahead && n.End >= now)
                        _sounding[n.Pitch] = true;
                }
            }

            for (int p = 0; p < 128; p++)
            {
                var r = _keys[p];
                if (r == null || !layout.Contains(p)) continue;

                bool held = hub != null && hub.Held[p];
                Color? c = null;
                if (held) c = (_expected[p] || _sounding[p]) ? correct : wrong;
                else if (_expected[p]) c = expected;

                if (c.HasValue)
                {
                    if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
                    r.GetPropertyBlock(_mpb);
                    _mpb.SetColor(BaseColor, c.Value);
                    _mpb.SetColor(ColorId, c.Value);
                    r.SetPropertyBlock(_mpb);
                }
                else if (r.gameObject.activeSelf)
                {
                    r.gameObject.SetActive(false);
                }
            }
        }
    }
}
