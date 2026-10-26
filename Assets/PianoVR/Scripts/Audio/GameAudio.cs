using PianoVR.Game;
using PianoVR.Midi;
using UnityEngine;

namespace PianoVR.Audio
{
    /// <summary>
    /// Conecta el juego con el sintetizador, como en Guitar Hero:
    /// - Las notas que NO te tocan a ti suenan solas (acompañamiento).
    /// - Tus notas suenan solo si las tocas; si fallas, se escucha un "clank".
    /// - Con piano real tu piano ya suena: apaga <see cref="playOwnKeys"/>.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        [SerializeField] PianoSynth synth;
        [SerializeField] SongPlayer player;
        [SerializeField] ScoreSystem score;
        [SerializeField] MidiInputHub hub;

        [Tooltip("Sonar las teclas que tocas (piano virtual o teclados MIDI sin parlante).")]
        public bool playOwnKeys = true;
        public bool playAccompaniment = true;
        [Range(0f, 1f)] public float accompanimentLevel = 0.6f;
        public bool clankOnMiss = true;

        const int TagSong = 1, TagKeys = 2;
        bool _wasPlaying;
        float _lastClank;

        void Start()
        {
            if (synth == null) synth = FindFirstObjectByType<PianoSynth>();
            if (player == null) player = FindFirstObjectByType<SongPlayer>();
            if (score == null) score = FindFirstObjectByType<ScoreSystem>();
            if (hub == null) hub = FindFirstObjectByType<MidiInputHub>();

            player.NoteStarted += OnNoteStarted;
            player.Restarted += synth.AllNotesOff;
            player.SongLoaded += synth.AllNotesOff;
            score.Judged += OnJudged;
            score.Failed += OnFailed;
            hub.NoteOn += OnKeyDown;
            hub.NoteOff += OnKeyUp;
        }

        void OnDestroy()
        {
            if (player != null)
            {
                player.NoteStarted -= OnNoteStarted;
                if (synth != null) { player.Restarted -= synth.AllNotesOff; player.SongLoaded -= synth.AllNotesOff; }
            }
            if (score != null) { score.Judged -= OnJudged; score.Failed -= OnFailed; }
            if (hub != null) { hub.NoteOn -= OnKeyDown; hub.NoteOff -= OnKeyUp; }
        }

        void Update()
        {
            // Al pausar se corta lo que esté sonando del acompañamiento.
            if (_wasPlaying && !player.IsPlaying) synth.AllNotesOff();
            _wasPlaying = player.IsPlaying;
        }

        void OnNoteStarted(int index)
        {
            if (!playAccompaniment) return;
            var n = player.Song.Notes[index];
            if (player.IsPlayable(n)) return; // esa la tienes que tocar tú
            float realDuration = n.Duration / Mathf.Max(player.speed, 0.01f);
            synth.NoteOn(n.Pitch, n.Velocity / 127f * accompanimentLevel, realDuration, TagSong);
        }

        void OnKeyDown(int pitch, int velocity)
        {
            if (playOwnKeys) synth.NoteOn(pitch, velocity / 127f, -1f, TagKeys);
        }

        void OnKeyUp(int pitch)
        {
            if (playOwnKeys) synth.NoteOff(pitch, TagKeys);
        }

        void OnJudged(int index, Judgment j, int pitch)
        {
            if (!clankOnMiss || j != Judgment.Miss) return;
            if (Time.time - _lastClank < 0.12f) return;
            _lastClank = Time.time;
            synth.Clank();
        }

        void OnFailed()
        {
            synth.AllNotesOff();
            synth.Clank();
        }
    }
}
