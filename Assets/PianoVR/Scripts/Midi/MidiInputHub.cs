using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace PianoVR.Midi
{
    /// <summary>
    /// Punto único de entrada MIDI del juego. Las fuentes (teclado MIDI por USB en Android,
    /// teclado del PC en el editor) llaman a <see cref="Enqueue"/> desde cualquier hilo y
    /// el hub reparte los eventos en el hilo principal de Unity.
    /// </summary>
    public class MidiInputHub : MonoBehaviour
    {
        public static MidiInputHub Instance { get; private set; }

        /// <summary>(tono, velocidad)</summary>
        public event Action<int, int> NoteOn;
        /// <summary>(tono)</summary>
        public event Action<int> NoteOff;
        /// <summary>Mensajes de estado de dispositivos (conectado, desconectado, errores).</summary>
        public event Action<string> DeviceStatus;

        /// <summary>Teclas presionadas ahora mismo (índice = tono MIDI).</summary>
        public readonly bool[] Held = new bool[128];

        public string LastDeviceMessage { get; private set; } = "Sin teclado MIDI";

        readonly ConcurrentQueue<(int status, int d1, int d2)> _queue = new ConcurrentQueue<(int, int, int)>();
        readonly ConcurrentQueue<string> _statusQueue = new ConcurrentQueue<string>();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Seguro para llamar desde cualquier hilo.</summary>
        public void Enqueue(int status, int data1, int data2) => _queue.Enqueue((status, data1, data2));

        /// <summary>Seguro para llamar desde cualquier hilo.</summary>
        public void ReportStatus(string message) => _statusQueue.Enqueue(message);

        void Update()
        {
            while (_statusQueue.TryDequeue(out var msg))
            {
                LastDeviceMessage = msg;
                Debug.Log($"[PianoVR MIDI] {msg}");
                DeviceStatus?.Invoke(msg);
            }

            while (_queue.TryDequeue(out var m))
            {
                int kind = m.status & 0xF0;
                int pitch = m.d1 & 0x7F;
                if (kind == 0x90 && m.d2 > 0)
                {
                    Held[pitch] = true;
                    NoteOn?.Invoke(pitch, m.d2);
                }
                else if (kind == 0x80 || kind == 0x90)
                {
                    Held[pitch] = false;
                    NoteOff?.Invoke(pitch);
                }
            }
        }
    }
}
