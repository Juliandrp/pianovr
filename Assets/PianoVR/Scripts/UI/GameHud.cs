using PianoVR.Game;
using PianoVR.Midi;
using PianoVR.Piano;
using UnityEngine;

namespace PianoVR.UI
{
    /// <summary>
    /// Panel estilo Guitar Hero que flota sobre el piano: puntaje, multiplicador, racha,
    /// medidor de energía (rojo-amarillo-verde), barra de Modo Estrella, progreso y pantalla de resultados.
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        [SerializeField] SongPlayer player;
        [SerializeField] ScoreSystem score;
        [SerializeField] PianoLayout layout;
        [SerializeField] Material material;

        public float height = 0.62f;
        public float width = 0.5f;

        Transform _root;
        TextMesh _score, _multiplier, _combo, _status, _title, _resultsText;
        Transform _energyNeedle, _starFill, _progressFill;
        Renderer _starFillR, _multiplierBadge;
        Transform _resultsRoot;
        readonly Transform[] _stars = new Transform[5];
        readonly Renderer[] _starR = new Renderer[5];
        Transform _head;
        Font _font;
        MaterialPropertyBlock _mpb;
        float _resultsShownAt = -1f;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        static readonly Color StarBlue = new Color(0.45f, 0.95f, 1f);

        void Start()
        {
            if (player == null) player = FindFirstObjectByType<SongPlayer>();
            if (score == null) score = FindFirstObjectByType<ScoreSystem>();
            if (layout == null) layout = FindFirstObjectByType<PianoLayout>();
            _head = Camera.main != null ? Camera.main.transform : transform;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _mpb = new MaterialPropertyBlock();
            Build();
            score.Completed += _ => _resultsShownAt = Time.time;
            score.Failed += () => _resultsShownAt = Time.time;
            player.Restarted += () => _resultsShownAt = -1f;
            player.SongLoaded += () => _resultsShownAt = -1f;
        }

        // ---------- Construcción ----------

        void Build()
        {
            _root = new GameObject("HUD").transform;
            _root.SetParent(transform, false);

            float half = width * 0.5f;

            // Fila superior: multiplicador | puntaje | racha
            _multiplierBadge = Box("MultBadge", new Vector3(-half + 0.035f, 0.05f, 0.002f), new Vector3(0.06f, 0.06f, 0.004f), new Color(0.15f, 0.15f, 0.2f, 0.85f));
            _multiplier = Text("Mult", new Vector3(-half + 0.035f, 0.05f, 0f), 0.0055f, TextAnchor.MiddleCenter, Color.white);
            _score = Text("Score", new Vector3(0f, 0.05f, 0f), 0.0075f, TextAnchor.MiddleCenter, Color.white);
            _combo = Text("Combo", new Vector3(half - 0.01f, 0.05f, 0f), 0.0035f, TextAnchor.MiddleRight, new Color(1f, 1f, 1f, 0.85f));

            // Energía: tres tramos de color y una aguja.
            float barW = width * 0.62f, barY = 0.0f;
            Box("EnergyRed", new Vector3(-barW / 3f, barY, 0.002f), new Vector3(barW / 3f, 0.012f, 0.004f), new Color(0.85f, 0.2f, 0.2f, 0.8f));
            Box("EnergyYellow", new Vector3(0f, barY, 0.002f), new Vector3(barW / 3f, 0.012f, 0.004f), new Color(0.95f, 0.8f, 0.2f, 0.8f));
            Box("EnergyGreen", new Vector3(barW / 3f, barY, 0.002f), new Vector3(barW / 3f, 0.012f, 0.004f), new Color(0.3f, 0.9f, 0.35f, 0.8f));
            _energyNeedle = Box("EnergyNeedle", new Vector3(0f, barY, -0.001f), new Vector3(0.004f, 0.024f, 0.004f), Color.white).transform;
            Text("EnergyLabel", new Vector3(-barW * 0.5f - 0.01f, barY, 0f), 0.0026f, TextAnchor.MiddleRight, new Color(1f, 1f, 1f, 0.7f)).text = "Público";

            // Modo Estrella
            float starY = -0.022f;
            Box("StarBack", new Vector3(0f, starY, 0.002f), new Vector3(barW, 0.008f, 0.004f), new Color(0.1f, 0.2f, 0.3f, 0.7f));
            var fill = Box("StarFill", new Vector3(0f, starY, 0f), new Vector3(barW, 0.008f, 0.004f), StarBlue);
            _starFill = fill.transform;
            _starFillR = fill;
            Text("StarLabel", new Vector3(-barW * 0.5f - 0.01f, starY, 0f), 0.0026f, TextAnchor.MiddleRight, StarBlue).text = "Estrella";

            // Progreso de la canción
            float progY = -0.04f;
            Box("ProgBack", new Vector3(0f, progY, 0.002f), new Vector3(width, 0.003f, 0.004f), new Color(1f, 1f, 1f, 0.15f));
            _progressFill = Box("ProgFill", new Vector3(0f, progY, 0f), new Vector3(width, 0.003f, 0.004f), new Color(1f, 1f, 1f, 0.7f)).transform;

            _status = Text("Status", new Vector3(0f, -0.065f, 0f), 0.0035f, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.5f));
            _title = Text("Title", new Vector3(0f, 0.1f, 0f), 0.0032f, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f));

            // Resultados
            _resultsRoot = new GameObject("Results").transform;
            _resultsRoot.SetParent(_root, false);
            _resultsRoot.localPosition = new Vector3(0f, -0.2f, -0.01f);
            var bg = Box("ResultsBack", new Vector3(0f, 0f, 0.004f), new Vector3(width, 0.24f, 0.004f), new Color(0.05f, 0.05f, 0.08f, 0.85f));
            bg.transform.SetParent(_resultsRoot, false);
            var starMesh = StarMesh();
            for (int i = 0; i < 5; i++)
            {
                var go = new GameObject("Star" + i);
                go.transform.SetParent(_resultsRoot, false);
                go.transform.localPosition = new Vector3((i - 2) * 0.055f, 0.075f, 0f);
                go.transform.localScale = Vector3.one * 0.022f;
                go.AddComponent<MeshFilter>().sharedMesh = starMesh;
                var r = go.AddComponent<MeshRenderer>();
                if (material) r.sharedMaterial = material;
                _stars[i] = go.transform;
                _starR[i] = r;
            }
            _resultsText = Text("ResultsText", new Vector3(0f, -0.03f, 0f), 0.003f, TextAnchor.MiddleCenter, Color.white);
            _resultsText.transform.SetParent(_resultsRoot, false);
            _resultsText.transform.localPosition = new Vector3(0f, -0.035f, 0f);
            _resultsRoot.gameObject.SetActive(false);
        }

        Renderer Box(string name, Vector3 localPos, Vector3 scale, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_root, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (material) r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Paint(r, c);
            return r;
        }

        TextMesh Text(string name, Vector3 localPos, float size, TextAnchor anchor, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = _font;
            go.GetComponent<MeshRenderer>().sharedMaterial = _font.material;
            tm.fontSize = 60;
            tm.characterSize = size;
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Center;
            tm.color = c;
            return tm;
        }

        void Paint(Renderer r, Color c)
        {
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColor, c);
            _mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(_mpb);
        }

        /// <summary>Estrella de 5 puntas, de dos caras, de radio 1.</summary>
        static Mesh StarMesh()
        {
            var v = new Vector3[11];
            v[0] = Vector3.zero;
            for (int i = 0; i < 10; i++)
            {
                float r = i % 2 == 0 ? 1f : 0.45f;
                float a = Mathf.Deg2Rad * (90f - i * 36f);
                v[i + 1] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
            }
            var t = new int[60];
            for (int i = 0; i < 10; i++)
            {
                int a = i + 1, b = (i + 1) % 10 + 1;
                t[i * 6 + 0] = 0; t[i * 6 + 1] = a; t[i * 6 + 2] = b; // cara frontal
                t[i * 6 + 3] = 0; t[i * 6 + 4] = b; t[i * 6 + 5] = a; // cara trasera
            }
            var m = new Mesh { name = "Star", vertices = v, triangles = t };
            m.RecalculateBounds();
            return m;
        }

        // ---------- Actualización ----------

        void LateUpdate()
        {
            if (_root == null) return;
            Place();

            bool hasSong = player.Song != null;
            var mem = PianoVR.Game.MemoryEngine.Instance;
            string memory = mem != null && mem.mode == PianoVR.Game.MemoryEngine.Mode.Auto
                ? $"  ·  Memoria {mem.StoredLevel}{(mem.HintActive ? " (pista)" : "")}" : "";
            _title.text = hasSong ? $"{player.Song.Name}  ·  {SongArranger.Label(score.difficulty)}{memory}" : "";

            _score.text = score.Score.ToString("N0");
            _multiplier.text = "x" + score.Multiplier;
            _multiplier.color = score.StarActive ? StarBlue : score.BaseMultiplier >= 4 ? new Color(1f, 0.85f, 0.2f) : Color.white;
            Paint(_multiplierBadge, score.StarActive ? new Color(0.1f, 0.35f, 0.5f, 0.9f) : new Color(0.15f, 0.15f, 0.2f, 0.85f));
            _combo.text = score.Combo >= 3 ? $"Racha {score.Combo}" : "";

            float barW = width * 0.62f;
            _energyNeedle.localPosition = new Vector3(Mathf.Lerp(-barW * 0.5f, barW * 0.5f, score.Energy), 0f, -0.001f);

            float sm = Mathf.Clamp01(score.StarMeter);
            _starFill.localScale = new Vector3(Mathf.Max(0.0001f, barW * sm), 0.008f, 0.004f);
            _starFill.localPosition = new Vector3(-barW * 0.5f + barW * sm * 0.5f, -0.022f, 0f);
            bool ready = score.CanActivateStar;
            Paint(_starFillR, ready && Mathf.PingPong(Time.time * 3f, 1f) > 0.5f ? Color.white : StarBlue);

            float progress = hasSong && player.Song.Length > 0 ? Mathf.Clamp01(player.Time / player.Song.Length) : 0f;
            _progressFill.localScale = new Vector3(Mathf.Max(0.0001f, width * progress), 0.003f, 0.004f);
            _progressFill.localPosition = new Vector3(-width * 0.5f + width * progress * 0.5f, -0.04f, 0f);

            _status.text = Status(ready);

            bool showResults = score.HasResults && _resultsShownAt >= 0f;
            _resultsRoot.gameObject.SetActive(showResults);
            if (showResults) UpdateResults();
        }

        string Status(bool starReady)
        {
            if (player.Song == null) return "Sin canciones";
            if (!layout.IsCalibrated) return "Calibra tu piano para ver las notas (botón Calibrar o Y)";
            if (player.IsFailed) return "Perdiste... ¡inténtalo otra vez!";
            if (player.IsFinished) return "";
            if (!player.IsPlaying) return player.Time <= -player.leadIn + 0.01f ? "Toca  TOCAR  para empezar" : "Pausa";
            if (player.Time < 0f) return Mathf.CeilToInt(-player.Time / Mathf.Max(player.speed, 0.01f)).ToString();
            if (player.IsWaiting) return "Esperando tu nota...";
            if (starReady) return "¡Modo Estrella listo! Levanta las manos";
            return "";
        }

        void UpdateResults()
        {
            var r = score.LastResults;
            float t = Time.time - _resultsShownAt;
            for (int i = 0; i < 5; i++)
            {
                bool earned = i < r.stars;
                // Aparecen una por una con un pequeño rebote.
                float k = Mathf.Clamp01((t - 0.3f - i * 0.25f) / 0.25f);
                float s = earned ? Mathf.Lerp(0f, 1f, k) * (k < 1f ? 1.25f : 1f) : 1f;
                _stars[i].localScale = Vector3.one * 0.022f * s;
                Paint(_starR[i], earned ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 1f, 1f, 0.15f));
            }

            string head = r.failed ? "<color=#FF6666>¡TE ABUCHEARON!</color>" : r.practice ? "Práctica terminada" : "¡CANCIÓN COMPLETADA!";
            string record = r.newHighScore ? "<color=#FFD94D>¡NUEVO RÉCORD!</color>" : r.highScore > 0 ? $"Récord: {r.highScore:N0}" : "";
            _resultsText.text =
                $"{head}\n<size=90>{r.score:N0}</size>\n{record}\n" +
                $"<size=45>Perfecto {r.perfect} · Genial {r.great} · Bien {r.good} · Fallos {r.missed} · Ups {r.wrong}\n" +
                $"Mejor racha {r.bestCombo} · Notas acertadas {r.accuracy:P0}</size>\n" +
                "<size=45><color=#AAAAAA>OTRA VEZ para repetir</color></size>";
        }

        void Place()
        {
            if (layout.IsCalibrated)
            {
                float span = (layout.WhiteKeyCount - 1) * layout.WhiteKeyWidth;
                Vector3 pos = layout.Origin + layout.Right * (span * 0.5f) + layout.Forward * (layout.whiteKeyDepth + 0.06f)
                              + layout.Up * height;
                _root.SetPositionAndRotation(pos, layout.Rotation);
            }
            else
            {
                Vector3 fwd = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                _root.SetPositionAndRotation(_head.position + fwd * 0.9f + Vector3.up * 0.15f, Quaternion.LookRotation(fwd));
            }
        }
    }
}
