using System;
using System.Collections.Generic;
using PianoVR.Midi;
using UnityEngine;

namespace PianoVR.Game
{
    public enum PracticeHand { Both, Right, Left }

    /// <summary>
    /// Reloj de la canción y evaluación de lo que toca el usuario.
    /// - Modo concierto (por defecto): la canción avanza sola; lo que no tocas a tiempo es fallo.
    /// - Modo práctica (waitMode): la canción se detiene en cada nota hasta que la toques.
    /// El puntaje estilo Guitar Hero lo calcula <see cref="ScoreSystem"/> escuchando estos eventos.
    /// </summary>
    public class SongPlayer : MonoBehaviour
    {
        public enum NoteResult { Pending, Hit, Missed, Auto }

        [SerializeField] MidiInputHub hub;

        [Header("Juego")]
        [Range(0.25f, 1.5f)] public float speed = 1f;
        [Tooltip("Modo práctica: la canción espera a que toques cada nota.")]
        public bool waitMode;
        public PracticeHand practiceHand = PracticeHand.Both;
        [Tooltip("Segundos de cuenta regresiva antes de la primera nota.")]
        public float leadIn = 3f;
        [Tooltip("Tolerancia en segundos reales para contar una nota como acertada.")]
        public float hitWindow = 0.18f;

        public Song Song { get; private set; }
        public float Time { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsWaiting { get; private set; }
        public bool IsFinished { get; private set; }
        public bool IsFailed { get; private set; }

        public int Hits { get; private set; }
        public int Misses { get; private set; }
        public int WrongNotes { get; private set; }

        public float Accuracy
        {
            get
            {
                int total = Hits + Misses + WrongNotes;
                return total == 0 ? 1f : (float)Hits / total;
            }
        }

        /// <summary>(índice de nota, error de tiempo en segundos reales: negativo = antes, positivo = tarde)</summary>
        public event Action<int, float> NoteHit;
        public event Action<int> NoteMissed;
        /// <summary>Tono tocado que no correspondía a ninguna nota.</summary>
        public event Action<int> WrongNote;
        /// <summary>El reloj llegó al inicio de esta nota (para el acompañamiento).</summary>
        public event Action<int> NoteStarted;
        public event Action SongLoaded;
        public event Action Restarted;
        public event Action Finished;

        NoteResult[] _results = Array.Empty<NoteResult>();
        int _cursor;      // primera nota que aún no se ha evaluado
        int _startCursor; // primera nota cuyo inicio aún no ha llegado

        public NoteResult ResultOf(int index) => _results[index];
        public IReadOnlyList<SongNote> Notes => Song?.Notes;

        void Start()
        {
            if (hub == null) hub = FindFirstObjectByType<MidiInputHub>();
            if (hub != null) hub.NoteOn += OnNoteOn;
        }

        void OnDestroy()
        {
            if (hub != null) hub.NoteOn -= OnNoteOn;
        }

        public void Load(Song song, bool autoPlay = false)
        {
            Song = song;
            _results = new NoteResult[song.Notes.Count];
            ResetState();
            IsPlaying = autoPlay;
            SongLoaded?.Invoke();
        }

        public void Restart()
        {
            if (Song == null) return;
            ResetState();
            IsPlaying = true;
            Restarted?.Invoke();
        }

        void ResetState()
        {
            Array.Clear(_results, 0, _results.Length);
            _cursor = 0;
            _startCursor = 0;
            Time = -leadIn;
            Hits = Misses = WrongNotes = 0;
            IsFinished = IsFailed = IsWaiting = false;
        }

        public void Pause() => IsPlaying = false;

        public void TogglePause()
        {
            if (Song == null) return;
            if (IsFinished || IsFailed) { Restart(); return; }
            IsPlaying = !IsPlaying;
        }

        /// <summary>Lo llama ScoreSystem cuando se acaba la energía.</summary>
        public void Fail()
        {
            if (IsFinished || IsFailed) return;
            IsFailed = true;
            IsPlaying = false;
        }

        /// <summary>Retrocede/avanza en la canción (segundos de canción).</summary>
        public void Seek(float seconds)
        {
            if (Song == null) return;
            Time = Mathf.Clamp(seconds, -leadIn, Song.Length);
            for (int i = 0; i < _results.Length; i++)
                if (Song.Notes[i].Start >= Time) _results[i] = NoteResult.Pending;
            _cursor = LowerBound(Time - 1f);
            _startCursor = LowerBound(Time);
            IsFinished = false;
        }

        public bool IsActiveHand(Hand hand) =>
            practiceHand == PracticeHand.Both ||
            (practiceHand == PracticeHand.Right && hand == Hand.Right) ||
            (practiceHand == PracticeHand.Left && hand == Hand.Left);

        /// <summary>Si el jugador tiene que tocar esta nota (según dificultad y mano elegida).</summary>
        public bool IsPlayable(SongNote n) => n.Playable && IsActiveHand(n.Hand);

        float WindowSongSeconds => hitWindow * speed;

        void Update()
        {
            if (Song == null || !IsPlaying || IsFinished || IsFailed) return;

            float next = Time + UnityEngine.Time.deltaTime * speed;
            var notes = Song.Notes;
            IsWaiting = false;

            for (int i = _cursor; i < notes.Count && notes[i].Start <= next + WindowSongSeconds; i++)
            {
                var n = notes[i];
                if (_results[i] != NoteResult.Pending) continue;

                if (!IsPlayable(n))
                {
                    if (n.Start <= next) _results[i] = NoteResult.Auto;
                    continue;
                }

                if (waitMode)
                {
                    if (n.Start <= next)
                    {
                        next = Mathf.Max(Time, n.Start);
                        IsWaiting = true;
                        break;
                    }
                }
                else if (next > n.Start + WindowSongSeconds)
                {
                    _results[i] = NoteResult.Missed;
                    Misses++;
                    NoteMissed?.Invoke(i);
                }
            }

            while (_cursor < notes.Count && _results[_cursor] != NoteResult.Pending) _cursor++;

            Time = next;

            while (_startCursor < notes.Count && notes[_startCursor].Start <= Time)
                NoteStarted?.Invoke(_startCursor++);

            if (Time > Song.Length + 1f && _cursor >= notes.Count)
            {
                IsFinished = true;
                IsPlaying = false;
                Finished?.Invoke();
            }
        }

        void OnNoteOn(int pitch, int velocity)
        {
            if (Song == null || !IsPlaying) return;

            var notes = Song.Notes;
            float window = WindowSongSeconds;
            float early = waitMode ? Mathf.Max(window, 0.35f * speed) : window;

            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = LowerBound(Time - window); i < notes.Count && notes[i].Start <= Time + early; i++)
            {
                var n = notes[i];
                if (n.Pitch != pitch || _results[i] != NoteResult.Pending || !IsPlayable(n)) continue;
                float d = Mathf.Abs(n.Start - Time);
                if (d < bestDist) { bestDist = d; best = i; }
            }

            if (best >= 0)
            {
                _results[best] = NoteResult.Hit;
                Hits++;
                // En modo práctica el reloj espera en la nota, así que el error se mide solo si te adelantaste.
                float error = (Time - notes[best].Start) / Mathf.Max(speed, 0.01f);
                NoteHit?.Invoke(best, error);
            }
            else if (Time >= 0f)
            {
                WrongNotes++;
                WrongNote?.Invoke(pitch);
            }
        }

        /// <summary>Índice de la primera nota con inicio >= t.</summary>
        int LowerBound(float t)
        {
            var notes = Song.Notes;
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
