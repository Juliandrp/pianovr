using System.Collections.Generic;
using PianoVR.Midi;
using PianoVR.Piano;
using UnityEngine;

namespace PianoVR.Game
{
    /// <summary>
    /// Dibuja las notas como barras que caen verticalmente sobre el fondo de cada tecla del piano real.
    /// La barra toca la tecla exactamente cuando hay que presionarla, y su largo es la duración de la nota.
    /// </summary>
    public class NoteHighway : MonoBehaviour
    {
        [SerializeField] SongPlayer player;
        [SerializeField] PianoLayout layout;
        [Tooltip("Material sin iluminación (URP Unlit). El menú PianoVR > Crear escena lo genera.")]
        [SerializeField] Material noteMaterial;

        [Header("Aspecto")]
        [Tooltip("Metros que cae una nota por cada segundo de canción.")]
        public float fallSpeed = 0.16f;
        [Tooltip("Altura máxima (m) a la que aparecen las notas.")]
        public float maxHeight = 0.55f;
        public float noteThickness = 0.012f;

        public Color rightHand = new Color(0.25f, 0.62f, 1f);
        public Color leftHand = new Color(0.35f, 0.9f, 0.45f);
        public Color inactiveHand = new Color(0.55f, 0.55f, 0.6f, 0.5f);
        public Color hitColor = new Color(1f, 0.92f, 0.35f);
        public Color missedColor = new Color(0.75f, 0.2f, 0.2f);

        readonly List<Renderer> _pool = new List<Renderer>();
        MaterialPropertyBlock _mpb;
        Transform _landingLine;
        float _maxDuration;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Start()
        {
            if (player == null) player = FindFirstObjectByType<SongPlayer>();
            if (layout == null) layout = FindFirstObjectByType<PianoLayout>();
            _mpb = new MaterialPropertyBlock();
            player.SongLoaded += OnSongLoaded;
            if (player.Song != null) OnSongLoaded();
            CreateLandingLine();
        }

        void OnDestroy()
        {
            if (player != null) player.SongLoaded -= OnSongLoaded;
        }

        void OnSongLoaded()
        {
            _maxDuration = 0f;
            foreach (var n in player.Song.Notes) _maxDuration = Mathf.Max(_maxDuration, n.Duration);
        }

        void CreateLandingLine()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "LandingLine";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            var r = go.GetComponent<Renderer>();
            if (noteMaterial) r.sharedMaterial = noteMaterial;
            SetColor(r, new Color(1f, 1f, 1f, 0.6f));
            _landingLine = go.transform;
        }

        Renderer Get(int index)
        {
            while (_pool.Count <= index)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Note";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);
                var r = go.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (noteMaterial) r.sharedMaterial = noteMaterial;
                _pool.Add(r);
            }
            return _pool[index];
        }

        void SetColor(Renderer r, Color c)
        {
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColor, c);
            _mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(_mpb);
        }

        void LateUpdate()
        {
            int used = 0;
            bool show = layout.IsCalibrated && player.Song != null;
            _landingLine.gameObject.SetActive(layout.IsCalibrated);

            if (layout.IsCalibrated)
            {
                float span = (layout.WhiteKeyCount - 1) * layout.WhiteKeyWidth + layout.WhiteKeyWidth;
                Vector3 mid = layout.Origin + layout.Right * ((layout.WhiteKeyCount - 1) * layout.WhiteKeyWidth * 0.5f)
                              + layout.Forward * (layout.whiteKeyDepth * 0.92f);
                _landingLine.SetPositionAndRotation(mid, layout.Rotation);
                _landingLine.localScale = new Vector3(span, 0.002f, 0.004f);
            }

            if (show)
            {
                var notes = player.Song.Notes;
                float t = player.Time;
                float horizon = t + maxHeight / fallSpeed;
                int first = FirstNoteStartingAfter(t - _maxDuration);

                for (int i = first; i < notes.Count && notes[i].Start <= horizon; i++)
                {
                    var n = notes[i];
                    if (!layout.Contains(n.Pitch)) continue;

                    float bottom = (n.Start - t) * fallSpeed;
                    float top = Mathf.Min((n.End - t) * fallSpeed, maxHeight);
                    if (top <= 0f) continue;
                    bottom = Mathf.Max(bottom, 0f);
                    if (top - bottom < 0.002f) continue;

                    var r = Get(used++);
                    if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);

                    Vector3 basePos = layout.LandingPoint(n.Pitch);
                    r.transform.SetPositionAndRotation(basePos + layout.Up * ((bottom + top) * 0.5f), layout.Rotation);
                    r.transform.localScale = new Vector3(layout.KeyWidth(n.Pitch) * 0.95f, top - bottom, noteThickness);

                    SetColor(r, ColorFor(i, n));
                }
            }

            for (int i = used; i < _pool.Count; i++)
                if (_pool[i].gameObject.activeSelf) _pool[i].gameObject.SetActive(false);
        }

        Color ColorFor(int index, SongNote n)
        {
            if (!player.IsActiveHand(n.Hand)) return inactiveHand;
            switch (player.ResultOf(index))
            {
                case SongPlayer.NoteResult.Hit: return hitColor;
                case SongPlayer.NoteResult.Missed: return missedColor;
                default:
                    Color c = n.Hand == Hand.Left ? leftHand : rightHand;
                    return PianoLayout.IsBlack(n.Pitch) ? c * 0.75f + new Color(0, 0, 0, 0.25f) : c;
            }
        }

        int FirstNoteStartingAfter(float t)
        {
            var notes = player.Song.Notes;
            int lo = 0, hi = notes.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (notes[mid].Start < t) lo = mid + 1; else hi = mid;
            }
            return lo;
        }
    }
}
