using System;
using System.Collections.Generic;
using PianoVR.Midi;
using UnityEngine;

namespace PianoVR.Game
{
    public enum PracticeHand { Both, Right, Left }

    /// <summary>
    /// Reloj de la canción y evaluación de lo que toca el usuario: aciertos, fallos y notas equivocadas.
    /// En "modo espera" la canción se detiene en cada nota hasta que la toques (ideal para aprender).
    /// </summary>
    public class SongPlayer : MonoBehaviour
    {
        public enum NoteResult { Pending, Hit, Missed, Auto }

        [SerializeField] MidiInputHub hub;

        [Header("Práctica")]
        [Range(0.25f, 1.5f)] public float speed = 1f;
        public bool waitMode = true;
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

        public int Hits { get; private set; }
        public int Misses { get; private set; }
        public int WrongNotes { get; private set; }
        public int Streak { get; private set; }
        public int BestStreak { get; private set; }

        public float Accuracy
        {
            get
            {
                int total = Hits + Misses + WrongNotes;
                return total == 0 ? 1f : (float)Hits / total;
            }
        }

        public event Action<int> NoteHit;       // índice de la nota
        public event Action<int> NoteMissed;    // índice de la nota
        public event Action<int> WrongNote;     // tono tocado
        public event Action SongLoaded;
        public event Action Finished;

        NoteResult[] _results = Array.Empty<NoteResult>();
        int _cursor; // primera nota que aún no ha terminado de evaluarse

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

        public void Load(Song song)
        {
            Song = song;
            _results = new NoteResult[song.Notes.Count];
            Restart();
            IsPlaying = false;
            SongLoaded?.Invoke();
        }

        public void Restart()
        {
            if (Song == null) return;
            Array.Clear(_results, 0, _results.Length);
            _cursor = 0;
            Time = -leadIn;
            Hits = Misses = WrongNotes = Streak = BestStreak = 0;
            IsFinished = false;
            IsWaiting = false;
            IsPlaying = true;
        }

        public void Pause() => IsPlaying = false;

        public void TogglePause()
        {
            if (Song == null) return;
            if (IsFinished) { Restart(); return; }
            IsPlaying = !IsPlaying;
        }

        /// <summary>Retrocede/avanza en la canción (segundos de canción).</summary>
        public void Seek(float seconds)
        {
            if (Song == null) return;
            Time = Mathf.Clamp(seconds, -leadIn, Song.Length);
            // Todo lo que queda después del nuevo tiempo vuelve a quedar pendiente.
            for (int i = 0; i < _results.Length; i++)
                if (Song.Notes[i].Start >= Time) _results[i] = NoteResult.Pending;
            _cursor = LowerBound(Time - 1f);
            IsFinished = false;
        }

        public bool IsActiveHand(Hand hand) =>
            practiceHand == PracticeHand.Both ||
            (practiceHand == PracticeHand.Right && hand == Hand.Right) ||
            (practiceHand == PracticeHand.Left && hand == Hand.Left);

        float WindowSongSeconds => hitWindow * speed;

        void Update()
        {
            if (Song == null || !IsPlaying || IsFinished) return;

            float next = Time + UnityEngine.Time.deltaTime * speed;
            var notes = Song.Notes;
            IsWaiting = false;

            // Avanza el cursor y resuelve notas que ya pasaron.
            for (int i = _cursor; i < notes.Count && notes[i].Start <= next + WindowSongSeconds; i++)
            {
                var n = notes[i];
                if (_results[i] != NoteResult.Pending) continue;

                if (!IsActiveHand(n.Hand))
                {
                    if (n.Start <= next) _results[i] = NoteResult.Auto;
                    continue;
                }

                if (waitMode)
                {
                    // Detiene el reloj en la primera nota pendiente hasta que la toquen.
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
                    Streak = 0;
                    NoteMissed?.Invoke(i);
                }
            }

            while (_cursor < notes.Count && _results[_cursor] != NoteResult.Pending) _cursor++;

            Time = next;

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
            // En modo espera se acepta tocar un poco antes de que llegue la nota.
            float early = waitMode ? Mathf.Max(window, 0.35f * speed) : window;

            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = LowerBound(Time - window); i < notes.Count && notes[i].Start <= Time + early; i++)
            {
                var n = notes[i];
                if (n.Pitch != pitch || _results[i] != NoteResult.Pending || !IsActiveHand(n.Hand)) continue;
                float d = Mathf.Abs(n.Start - Time);
                if (d < bestDist) { bestDist = d; best = i; }
            }

            if (best >= 0)
            {
                _results[best] = NoteResult.Hit;
                Hits++;
                Streak++;
                BestStreak = Mathf.Max(BestStreak, Streak);
                NoteHit?.Invoke(best);
            }
            else if (Time >= 0f)
            {
                WrongNotes++;
                Streak = 0;
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
