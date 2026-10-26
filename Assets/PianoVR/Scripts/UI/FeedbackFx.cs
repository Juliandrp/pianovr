using System.Collections.Generic;
using PianoVR.Game;
using PianoVR.Piano;
using UnityEngine;

namespace PianoVR.UI
{
    /// <summary>
    /// Efectos de juego: "¡PERFECTO!" flotando sobre la tecla, destellos al acertar,
    /// anuncios de multiplicador y Modo Estrella.
    /// </summary>
    public class FeedbackFx : MonoBehaviour
    {
        [SerializeField] ScoreSystem score;
        [SerializeField] PianoLayout layout;
        [SerializeField] Material flashMaterial;

        public Color perfect = new Color(1f, 0.85f, 0.2f);
        public Color great = new Color(0.4f, 1f, 0.5f);
        public Color good = new Color(0.4f, 0.75f, 1f);
        public Color miss = new Color(1f, 0.3f, 0.3f);
        public Color star = new Color(0.45f, 0.95f, 1f);

        class Popup { public TextMesh text; public float age, life; public Vector3 start; public Color color; }
        class Flash { public Transform t; public Renderer r; public float age, life; public Color color; public float size; }

        readonly List<Popup> _popups = new List<Popup>();
        readonly List<Flash> _flashes = new List<Flash>();
        Font _font;
        MaterialPropertyBlock _mpb;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        float _lastJudgmentText;

        void Start()
        {
            if (score == null) score = FindFirstObjectByType<ScoreSystem>();
            if (layout == null) layout = FindFirstObjectByType<PianoLayout>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _mpb = new MaterialPropertyBlock();

            score.Judged += OnJudged;
            score.MultiplierUp += m => Announce($"¡x{m}!", perfect, 0.9f);
            score.StarPhraseCompleted += () => Announce("¡Frase estrella!", star, 0.8f);
            score.StarChanged += on => { if (on) Announce("¡MODO ESTRELLA!", star, 1.3f); };
            score.Failed += () => Announce("¡Te abuchearon!", miss, 2f);
        }

        void OnJudged(int index, Judgment j, int pitch)
        {
            if (!layout.IsCalibrated || !layout.Contains(pitch)) return;
            Color c = j == Judgment.Perfect ? perfect : j == Judgment.Great ? great : j == Judgment.Good ? good : miss;
            if (score.StarActive && j <= Judgment.Good) c = star;

            Vector3 at = layout.LandingPoint(pitch);
            if (j <= Judgment.Good) SpawnFlash(at, c, j == Judgment.Perfect ? 0.05f : 0.035f);

            // No saturar de textos en pasajes rápidos.
            if (Time.time - _lastJudgmentText < 0.08f && j != Judgment.Miss) return;
            _lastJudgmentText = Time.time;
            SpawnText(ScoreSystem.Label(j), at + layout.Up * 0.04f, c, j == Judgment.Perfect ? 0.0036f : 0.003f, 0.6f);
        }

        void Announce(string message, Color c, float life)
        {
            if (!layout.IsCalibrated) return;
            float span = (layout.WhiteKeyCount - 1) * layout.WhiteKeyWidth;
            Vector3 at = layout.Origin + layout.Right * (span * 0.5f) + layout.Forward * (layout.whiteKeyDepth + 0.02f) + layout.Up * 0.28f;
            SpawnText(message, at, c, 0.0065f, life);
        }

        void SpawnText(string msg, Vector3 at, Color c, float size, float life)
        {
            Popup p = _popups.Find(x => !x.text.gameObject.activeSelf);
            if (p == null)
            {
                var go = new GameObject("Popup");
                go.transform.SetParent(transform, false);
                var tm = go.AddComponent<TextMesh>();
                tm.font = _font;
                go.GetComponent<MeshRenderer>().sharedMaterial = _font.material;
                tm.fontSize = 60;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.fontStyle = FontStyle.Bold;
                p = new Popup { text = tm };
                _popups.Add(p);
            }
            p.text.gameObject.SetActive(true);
            p.text.text = msg;
            p.text.characterSize = size;
            p.text.color = c;
            p.color = c;
            p.start = at;
            p.age = 0f;
            p.life = life;
            p.text.transform.SetPositionAndRotation(at, layout.Rotation);
        }

        void SpawnFlash(Vector3 at, Color c, float size)
        {
            Flash f = _flashes.Find(x => !x.t.gameObject.activeSelf);
            if (f == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "HitFlash";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);
                var r = go.GetComponent<Renderer>();
                if (flashMaterial) r.sharedMaterial = flashMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                f = new Flash { t = go.transform, r = r };
                _flashes.Add(f);
            }
            f.t.gameObject.SetActive(true);
            f.age = 0f;
            f.life = 0.25f;
            f.color = c;
            f.size = size;
            f.t.SetPositionAndRotation(at + layout.Up * 0.01f, layout.Rotation);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            foreach (var p in _popups)
            {
                if (!p.text.gameObject.activeSelf) continue;
                p.age += dt;
                float k = p.age / p.life;
                if (k >= 1f) { p.text.gameObject.SetActive(false); continue; }
                p.text.transform.position = p.start + layout.Up * (0.05f * k);
                var c = p.color; c.a = 1f - k * k;
                p.text.color = c;
                float pop = k < 0.15f ? Mathf.Lerp(0.6f, 1.1f, k / 0.15f) : Mathf.Lerp(1.1f, 1f, (k - 0.15f) / 0.85f);
                p.text.transform.localScale = Vector3.one * pop;
            }

            foreach (var f in _flashes)
            {
                if (!f.t.gameObject.activeSelf) continue;
                f.age += dt;
                float k = f.age / f.life;
                if (k >= 1f) { f.t.gameObject.SetActive(false); continue; }
                f.t.localScale = Vector3.one * Mathf.Lerp(f.size * 0.5f, f.size * 2f, k);
                var c = f.color; c.a = (1f - k) * 0.9f;
                f.r.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColor, c);
                _mpb.SetColor(ColorId, c);
                f.r.SetPropertyBlock(_mpb);
            }
        }
    }
}
