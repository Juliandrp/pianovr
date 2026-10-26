using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace PianoVR.Audio
{
    /// <summary>
    /// Sintetizador de piano liviano (sin samples), generado en tiempo real con una tabla de onda.
    /// Suena el acompañamiento, tus teclas en el piano virtual y el "clank" de los fallos.
    /// Se puede llamar desde el hilo principal; el audio se genera en el hilo de audio.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class PianoSynth : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.6f;
        [Range(4, 48)] public int maxVoices = 32;

        enum CmdType { On, Off, AllOff, Clank }
        struct Cmd { public CmdType type; public int pitch; public float velocity; public float duration; public int tag; }

        class Voice
        {
            public bool active;
            public int pitch, tag;
            public double phase, step;
            public float amp, env, decay, release;
            public bool releasing;
            public int samplesLeft; // -1 = hasta NoteOff
            public bool noise;
            public uint rng;
            public float lp;
        }

        const int TableSize = 2048;
        static float[] _table;
        Voice[] _voices;
        readonly ConcurrentQueue<Cmd> _cmds = new ConcurrentQueue<Cmd>();
        int _sampleRate;
        volatile float _volume;

        void Awake()
        {
            _sampleRate = AudioSettings.outputSampleRate;
            BuildTable();
            _voices = new Voice[Mathf.Max(4, maxVoices)];
            for (int i = 0; i < _voices.Length; i++) _voices[i] = new Voice();

            // OnAudioFilterRead necesita un AudioSource sonando: usamos un clip de silencio en bucle.
            var src = GetComponent<AudioSource>();
            src.clip = AudioClip.Create("PianoVR_Silence", _sampleRate, 1, _sampleRate, false);
            src.loop = true;
            src.spatialBlend = 0f;
            src.playOnAwake = true;
            src.Play();
        }

        void Update() => _volume = volume;

        static void BuildTable()
        {
            if (_table != null) return;
            _table = new float[TableSize];
            // Armónicos con pesos que recuerdan a un piano eléctrico/acústico suave.
            float[] h = { 1f, 0.55f, 0.28f, 0.18f, 0.09f, 0.06f, 0.03f };
            float max = 0f;
            for (int i = 0; i < TableSize; i++)
            {
                double x = 2 * Math.PI * i / TableSize;
                double v = 0;
                for (int k = 0; k < h.Length; k++) v += h[k] * Math.Sin((k + 1) * x);
                _table[i] = (float)v;
                max = Mathf.Max(max, Mathf.Abs((float)v));
            }
            for (int i = 0; i < TableSize; i++) _table[i] /= max;
        }

        /// <summary>Toca una nota. duration &lt;= 0 = suena hasta <see cref="NoteOff"/>. tag permite apagar por grupo.</summary>
        public void NoteOn(int pitch, float velocity01, float duration = -1f, int tag = 0) =>
            _cmds.Enqueue(new Cmd { type = CmdType.On, pitch = pitch, velocity = velocity01, duration = duration, tag = tag });

        public void NoteOff(int pitch, int tag = 0) =>
            _cmds.Enqueue(new Cmd { type = CmdType.Off, pitch = pitch, tag = tag });

        public void AllNotesOff() => _cmds.Enqueue(new Cmd { type = CmdType.AllOff });

        /// <summary>Sonido de fallo (como la cuerda "muteada" de Guitar Hero).</summary>
        public void Clank() => _cmds.Enqueue(new Cmd { type = CmdType.Clank });

        void ProcessCommands()
        {
            while (_cmds.TryDequeue(out var c))
            {
                switch (c.type)
                {
                    case CmdType.On: StartVoice(c); break;
                    case CmdType.Off:
                        foreach (var v in _voices)
                            if (v.active && !v.noise && v.pitch == c.pitch && v.tag == c.tag) v.releasing = true;
                        break;
                    case CmdType.AllOff:
                        foreach (var v in _voices) if (v.active) v.releasing = true;
                        break;
                    case CmdType.Clank: StartClank(); break;
                }
            }
        }

        Voice Steal()
        {
            Voice best = null;
            foreach (var v in _voices)
            {
                if (!v.active) return v;
                if (best == null || v.env < best.env) best = v;
            }
            return best;
        }

        void StartVoice(Cmd c)
        {
            var v = Steal();
            double freq = 440.0 * Math.Pow(2.0, (c.pitch - 69) / 12.0);
            v.active = true;
            v.noise = false;
            v.pitch = c.pitch;
            v.tag = c.tag;
            v.phase = 0;
            v.step = freq * TableSize / _sampleRate;
            v.amp = Mathf.Lerp(0.15f, 0.5f, c.velocity);
            v.env = 1f;
            // Graves suenan más tiempo que agudos.
            float decaySeconds = Mathf.Lerp(3.5f, 0.9f, Mathf.InverseLerp(36, 96, c.pitch));
            v.decay = Mathf.Exp(-1f / (decaySeconds * _sampleRate));
            v.release = Mathf.Exp(-1f / (0.12f * _sampleRate));
            v.releasing = false;
            v.samplesLeft = c.duration > 0 ? Mathf.CeilToInt(c.duration * _sampleRate) : -1;
        }

        void StartClank()
        {
            var v = Steal();
            v.active = true;
            v.noise = true;
            v.pitch = -1;
            v.amp = 0.35f;
            v.env = 1f;
            v.decay = Mathf.Exp(-1f / (0.06f * _sampleRate));
            v.release = v.decay;
            v.releasing = false;
            v.samplesLeft = -1;
            v.rng = 0x9E3779B9u;
            v.lp = 0f;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (_voices == null) return;
            ProcessCommands();
            float master = _volume * 0.5f;
            int frames = data.Length / channels;

            for (int f = 0; f < frames; f++)
            {
                float s = 0f;
                for (int i = 0; i < _voices.Length; i++)
                {
                    var v = _voices[i];
                    if (!v.active) continue;

                    float sample;
                    if (v.noise)
                    {
                        v.rng ^= v.rng << 13; v.rng ^= v.rng >> 17; v.rng ^= v.rng << 5;
                        float white = (v.rng & 0xFFFF) / 32768f - 1f;
                        v.lp += (white - v.lp) * 0.25f; // pasa-bajos: suena más "apagado"
                        sample = v.lp;
                    }
                    else
                    {
                        int idx = (int)v.phase;
                        float frac = (float)(v.phase - idx);
                        float a = _table[idx & (TableSize - 1)], b = _table[(idx + 1) & (TableSize - 1)];
                        sample = a + (b - a) * frac;
                        v.phase += v.step;
                        if (v.phase >= TableSize) v.phase -= TableSize;
                    }

                    s += sample * v.amp * v.env;

                    if (v.samplesLeft > 0 && --v.samplesLeft == 0) v.releasing = true;
                    v.env *= v.releasing ? v.release : v.decay;
                    if (v.env < 0.0005f) v.active = false;
                }

                s *= master;
                s = s / (1f + Mathf.Abs(s)); // saturación suave, nunca recorta feo
                for (int c = 0; c < channels; c++) data[f * channels + c] += s;
            }
        }
    }
}
