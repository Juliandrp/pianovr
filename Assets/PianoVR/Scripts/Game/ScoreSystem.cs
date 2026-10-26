using System;
using System.Collections.Generic;
using PianoVR.Midi;
using UnityEngine;

namespace PianoVR.Game
{
    public enum Judgment { Perfect, Great, Good, Miss, Wrong }

    /// <summary>
    /// Puntaje estilo Guitar Hero:
    /// - Cada nota vale según la precisión (Perfecto / Genial / Bien) por el multiplicador.
    /// - Racha: cada 10 notas seguidas sube el multiplicador hasta x4.
    /// - Energía (el "medidor de público"): sube al acertar, baja al fallar. En modo concierto, si llega a 0, pierdes.
    /// - Modo Estrella: completar frases estrella lo carga; actívalo con 50 % o más para duplicar puntos.
    /// </summary>
    public class ScoreSystem : MonoBehaviour
    {
        [Serializable]
        public struct Results
        {
            public string song;
            public Difficulty difficulty;
            public int score;
            public int stars;          // 0 = abucheado
            public int perfect, great, good, missed, wrong;
            public int bestCombo;
            public float accuracy;     // 0..1 sobre notas jugables
            public bool failed;
            public bool practice;
            public bool newHighScore;
            public int highScore;
        }

        [SerializeField] SongPlayer player;

        [Header("Ventanas de tiempo (segundos reales)")]
        public float perfectWindow = 0.05f;
        public float greatWindow = 0.10f;

        [Header("Reglas")]
        [Tooltip("Las notas equivocadas rompen la racha. Conviene apagarlo con seguimiento de manos (más ruido).")]
        public bool wrongBreaksCombo = true;
        [Tooltip("Sin fallar: la energía nunca te saca de la canción.")]
        public bool noFail;
        public Difficulty difficulty = Difficulty.Medio;

        public int Score { get; private set; }
        public int Combo { get; private set; }
        public int BestCombo { get; private set; }
        public int Multiplier => Mathf.Min(4, 1 + Combo / 10) * (StarActive ? 2 : 1);
        public int BaseMultiplier => Mathf.Min(4, 1 + Combo / 10);
        /// <summary>0..1</summary>
        public float Energy { get; private set; } = 0.5f;
        /// <summary>0..1</summary>
        public float StarMeter { get; private set; }
        public bool StarActive { get; private set; }
        public bool CanActivateStar => !StarActive && StarMeter >= 0.5f && player.IsPlaying;
        public Results LastResults { get; private set; }
        public bool HasResults { get; private set; }

        /// <summary>(índice de nota o -1, juicio, tono)</summary>
        public event Action<int, Judgment, int> Judged;
        public event Action<int> MultiplierUp;
        public event Action StarPhraseCompleted;
        public event Action<bool> StarChanged;
        public event Action<Results> Completed;
        public event Action Failed;

        int _perfect, _great, _good, _missed, _wrong, _playable;
        int _basePoints;
        readonly Dictionary<int, int> _phraseRemaining = new Dictionary<int, int>();
        readonly HashSet<int> _brokenPhrases = new HashSet<int>();

        const int PerfectPoints = 100, GreatPoints = 75, GoodPoints = 50;
        const float StarDrainPerSecond = 0.0625f; // 50 % dura 8 s

        void Start()
        {
            if (player == null) player = FindFirstObjectByType<SongPlayer>();
            player.SongLoaded += ResetScore;
            player.Restarted += ResetScore;
            player.NoteHit += OnHit;
            player.NoteMissed += OnMissed;
            player.WrongNote += OnWrong;
            player.Finished += OnFinished;
            if (player.Song != null) ResetScore();
        }

        void OnDestroy()
        {
            if (player == null) return;
            player.SongLoaded -= ResetScore;
            player.Restarted -= ResetScore;
            player.NoteHit -= OnHit;
            player.NoteMissed -= OnMissed;
            player.WrongNote -= OnWrong;
            player.Finished -= OnFinished;
        }

        public void ResetScore()
        {
            Score = Combo = BestCombo = 0;
            _perfect = _great = _good = _missed = _wrong = 0;
            _basePoints = 0;
            Energy = 0.5f;
            StarMeter = 0f;
            if (StarActive) { StarActive = false; StarChanged?.Invoke(false); }
            HasResults = false;

            _phraseRemaining.Clear();
            _brokenPhrases.Clear();
            _playable = 0;
            if (player.Song == null) return;
            foreach (var n in player.Song.Notes)
            {
                if (!player.IsPlayable(n)) continue;
                _playable++;
                if (n.Star && n.Phrase >= 0)
                {
                    _phraseRemaining.TryGetValue(n.Phrase, out int c);
                    _phraseRemaining[n.Phrase] = c + 1;
                }
            }
        }

        public void ActivateStar()
        {
            if (!CanActivateStar) return;
            StarActive = true;
            StarChanged?.Invoke(true);
        }

        void Update()
        {
            if (!StarActive || !player.IsPlaying || player.IsWaiting) return;
            StarMeter -= StarDrainPerSecond * Time.deltaTime * player.speed;
            if (StarMeter <= 0f)
            {
                StarMeter = 0f;
                StarActive = false;
                StarChanged?.Invoke(false);
            }
        }

        void OnHit(int index, float error)
        {
            var note = player.Song.Notes[index];
            float e = Mathf.Abs(error);
            Judgment j = e <= perfectWindow ? Judgment.Perfect : e <= greatWindow ? Judgment.Great : Judgment.Good;
            int points = j == Judgment.Perfect ? PerfectPoints : j == Judgment.Great ? GreatPoints : GoodPoints;
            if (j == Judgment.Perfect) _perfect++; else if (j == Judgment.Great) _great++; else _good++;

            int before = BaseMultiplier;
            Combo++;
            BestCombo = Mathf.Max(BestCombo, Combo);
            if (BaseMultiplier > before) MultiplierUp?.Invoke(BaseMultiplier);

            _basePoints += points;
            Score += points * Multiplier;
            AddEnergy(StarActive ? 0.03f : 0.02f);

            if (note.Star && note.Phrase >= 0 && !_brokenPhrases.Contains(note.Phrase)
                && _phraseRemaining.TryGetValue(note.Phrase, out int left))
            {
                left--;
                _phraseRemaining[note.Phrase] = left;
                if (left <= 0)
                {
                    StarMeter = Mathf.Min(1f, StarMeter + 0.25f);
                    StarPhraseCompleted?.Invoke();
                }
            }

            Judged?.Invoke(index, j, note.Pitch);
        }

        void OnMissed(int index)
        {
            var note = player.Song.Notes[index];
            _missed++;
            Combo = 0;
            if (note.Star && note.Phrase >= 0) _brokenPhrases.Add(note.Phrase);
            AddEnergy(-0.06f);
            Judged?.Invoke(index, Judgment.Miss, note.Pitch);
        }

        void OnWrong(int pitch)
        {
            _wrong++;
            if (wrongBreaksCombo) Combo = 0;
            AddEnergy(wrongBreaksCombo ? -0.04f : -0.015f);
            Judged?.Invoke(-1, Judgment.Wrong, pitch);
        }

        void AddEnergy(float delta)
        {
            Energy = Mathf.Clamp01(Energy + delta);
            bool canFail = !noFail && !player.waitMode;
            if (Energy <= 0f && canFail)
            {
                player.Fail();
                BuildResults(failed: true);
                Failed?.Invoke();
            }
        }

        void OnFinished() => BuildResults(failed: false);

        void BuildResults(bool failed)
        {
            float ratio = _playable == 0 ? 0f : (float)_basePoints / (_playable * PerfectPoints);
            int stars = failed ? 0 : ratio >= 0.95f ? 5 : ratio >= 0.85f ? 4 : ratio >= 0.70f ? 3 : ratio >= 0.50f ? 2 : 1;

            var r = new Results
            {
                song = player.Song != null ? player.Song.Name : "",
                difficulty = difficulty,
                score = Score,
                stars = stars,
                perfect = _perfect, great = _great, good = _good, missed = _missed, wrong = _wrong,
                bestCombo = BestCombo,
                accuracy = _playable == 0 ? 0f : (float)(_perfect + _great + _good) / _playable,
                failed = failed,
                practice = player.waitMode
            };

            // Récords solo en modo concierto, a velocidad normal o mayor.
            string key = $"PianoVR.HS.{r.song}.{difficulty}";
            r.highScore = PlayerPrefs.GetInt(key, 0);
            if (!failed && !r.practice && player.speed >= 1f && Score > r.highScore)
            {
                r.newHighScore = true;
                r.highScore = Score;
                PlayerPrefs.SetInt(key, Score);
                PlayerPrefs.Save();
            }

            LastResults = r;
            HasResults = true;
            if (StarActive) { StarActive = false; StarChanged?.Invoke(false); }
            if (!failed) Completed?.Invoke(r);
        }

        public static int HighScore(string song, Difficulty d) => PlayerPrefs.GetInt($"PianoVR.HS.{song}.{d}", 0);

        public static string Label(Judgment j)
        {
            switch (j)
            {
                case Judgment.Perfect: return "¡PERFECTO!";
                case Judgment.Great: return "¡GENIAL!";
                case Judgment.Good: return "BIEN";
                case Judgment.Miss: return "FALLO";
                default: return "¡UPS!";
            }
        }
    }
}
