using System;
using PianoVR.Midi;
using UnityEngine;

namespace PianoVR.Game
{
    /// <summary>
    /// Memory Engine: a medida que dominas una canción, las ayudas visuales se van retirando
    /// hasta que la tocas de memoria. El nivel se guarda por canción y dificultad.
    ///
    /// 0 Guía completa   – notas que caen + teclas iluminadas
    /// 1 Menos guía      – las notas se desvanecen antes de llegar; teclas iluminadas
    /// 2 Solo anticipo   – ves las notas solo al aparecer arriba; sin teclas iluminadas
    /// 3 Solo ritmo      – sin notas; la línea de llegada late cuando toca una nota
    /// 4 De memoria      – sin ayudas; solo ves si acertaste
    ///
    /// Si fallas, las ayudas vuelven completas unos segundos (pista) y luego se retiran otra vez.
    /// </summary>
    public class MemoryEngine : MonoBehaviour
    {
        public enum Mode { Off, Auto }

        public static MemoryEngine Instance { get; private set; }

        public const int MaxLevel = 4;

        [SerializeField] SongPlayer player;
        [SerializeField] ScoreSystem score;

        public Mode mode = Mode.Auto;
        [Tooltip("Precisión mínima (0..1) para subir de nivel al terminar la canción.")]
        public float levelUpAccuracy = 0.9f;
        [Tooltip("Por debajo de esta precisión se baja un nivel.")]
        public float levelDownAccuracy = 0.6f;
        [Tooltip("Segundos (reales) que vuelven las ayudas completas después de un fallo.")]
        public float hintSeconds = 2.5f;
        [Tooltip("Velocidad mínima para que cuente el progreso.")]
        public float minSpeedForProgress = 0.9f;

        /// <summary>Nivel de la canción actual (0..4), aunque el modo esté apagado.</summary>
        public int StoredLevel { get; private set; }
        /// <summary>Nivel que se está aplicando ahora mismo (0 si está apagado o durante una pista).</summary>
        public int EffectiveLevel => mode == Mode.Off || HintActive ? 0 : StoredLevel;
        public bool HintActive => Time.time < _hintUntil;

        /// <summary>(nivel nuevo, subió)</summary>
        public event Action<int, bool> LevelChanged;

        float _hintUntil;
        string _key;
        const string PrefMode = "PianoVR.MemoryMode";

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            mode = (Mode)PlayerPrefs.GetInt(PrefMode, (int)Mode.Auto);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (player != null) { player.SongLoaded -= OnSongLoaded; player.Restarted -= ClearHint; }
            if (score != null) { score.Judged -= OnJudged; score.Completed -= OnCompleted; score.Failed -= OnFailed; }
        }

        void Start()
        {
            if (player == null) player = FindFirstObjectByType<SongPlayer>();
            if (score == null) score = FindFirstObjectByType<ScoreSystem>();
            player.SongLoaded += OnSongLoaded;
            player.Restarted += ClearHint;
            score.Judged += OnJudged;
            score.Completed += OnCompleted;
            score.Failed += OnFailed;
            if (player.Song != null) OnSongLoaded();
        }

        public static string LevelName(int level)
        {
            switch (level)
            {
                case 0: return "Guía completa";
                case 1: return "Menos guía";
                case 2: return "Solo anticipo";
                case 3: return "Solo ritmo";
                default: return "De memoria";
            }
        }

        public void ToggleMode()
        {
            mode = mode == Mode.Off ? Mode.Auto : Mode.Off;
            PlayerPrefs.SetInt(PrefMode, (int)mode);
        }

        /// <summary>Cambia el nivel a mano (para practicar un nivel concreto).</summary>
        public void SetLevel(int level)
        {
            int clamped = Mathf.Clamp(level, 0, MaxLevel);
            bool up = clamped > StoredLevel;
            StoredLevel = clamped;
            if (_key != null) { PlayerPrefs.SetInt(_key, StoredLevel); PlayerPrefs.Save(); }
            LevelChanged?.Invoke(StoredLevel, up);
        }

        // ---------- Lo que consultan las vistas ----------

        /// <summary>
        /// Opacidad de una nota que cae según su altura: 0 = está llegando a la tecla, 1 = recién aparece arriba.
        /// </summary>
        public float NoteAlpha(float heightFraction)
        {
            switch (EffectiveLevel)
            {
                case 0: return 1f;
                case 1: return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.55f, heightFraction));
                case 2: return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 0.85f, heightFraction));
                default: return 0f;
            }
        }

        /// <summary>Iluminar la tecla que viene (azul). Los colores de acierto/error se muestran siempre.</summary>
        public bool ShowExpectedKeys => EffectiveLevel <= 1;

        /// <summary>Latido de la línea de llegada cuando hay que tocar (solo nivel 3).</summary>
        public bool ShowRhythmPulse => EffectiveLevel == 3;

        // ---------- Progreso ----------

        void OnSongLoaded()
        {
            ClearHint();
            _key = $"PianoVR.Memory.{player.Song.Name}.{score.difficulty}";
            StoredLevel = PlayerPrefs.GetInt(_key, 0);
        }

        void ClearHint() => _hintUntil = 0f;

        void OnJudged(int index, Judgment j, int pitch)
        {
            if (mode == Mode.Off || StoredLevel == 0) return;
            if (j == Judgment.Miss || (j == Judgment.Wrong && score.wrongBreaksCombo))
                _hintUntil = Time.time + hintSeconds;
        }

        void OnCompleted(ScoreSystem.Results r)
        {
            if (mode == Mode.Off || player.speed < minSpeedForProgress) return;

            if (r.accuracy >= levelUpAccuracy && StoredLevel < MaxLevel) SetLevel(StoredLevel + 1);
            else if (r.accuracy < levelDownAccuracy && StoredLevel > 0) SetLevel(StoredLevel - 1);
        }

        void OnFailed()
        {
            if (mode == Mode.Off || StoredLevel == 0) return;
            SetLevel(StoredLevel - 1);
        }
    }
}
