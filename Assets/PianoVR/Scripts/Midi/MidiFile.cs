using System;
using System.Collections.Generic;

namespace PianoVR.Midi
{
    /// <summary>Mano asignada a una nota (para colores y modo práctica por mano).</summary>
    public enum Hand { Right = 0, Left = 1 }

    /// <summary>Una nota de la canción, ya convertida a segundos.</summary>
    [Serializable]
    public class SongNote
    {
        public int Pitch;          // número MIDI (60 = Do central)
        public float Start;        // segundos desde el inicio
        public float Duration;     // segundos
        public int Velocity;
        public int Track;
        public int Channel;
        public Hand Hand;

        public float End => Start + Duration;
    }

    /// <summary>Canción lista para tocar: notas ordenadas por tiempo de inicio.</summary>
    public class Song
    {
        public string Name;
        public List<SongNote> Notes = new List<SongNote>();
        public float Length;
        public int MinPitch = 127;
        public int MaxPitch;
    }

    /// <summary>
    /// Lector mínimo de archivos MIDI estándar (SMF formato 0 y 1).
    /// Soporta running status, cambios de tempo y note-on con velocidad 0 como note-off.
    /// </summary>
    public static class MidiFile
    {
        /// <summary>Nota por debajo de la cual se asume mano izquierda cuando el archivo no separa manos por pista.</summary>
        public const int DefaultSplitPitch = 60;

        public static Song Parse(byte[] data, string name = "Canción")
        {
            if (data == null || data.Length < 14) throw new FormatException("Archivo MIDI vacío o muy corto.");

            var r = new Reader(data);
            if (r.ReadAscii(4) != "MThd") throw new FormatException("No es un archivo MIDI (falta MThd).");
            int headerLen = (int)r.ReadUInt32();
            int format = r.ReadUInt16();
            int trackCount = r.ReadUInt16();
            int division = r.ReadUInt16();
            r.Pos += headerLen - 6;

            if ((division & 0x8000) != 0)
                throw new NotSupportedException("Archivos MIDI con división SMPTE no están soportados.");
            if (format > 1)
                throw new NotSupportedException("Solo se soportan archivos MIDI formato 0 y 1.");

            var tempoChanges = new List<(long tick, int usPerQuarter)>();
            var rawNotes = new List<(long startTick, long endTick, int pitch, int vel, int track, int channel)>();

            for (int t = 0; t < trackCount && r.Pos < data.Length - 8; t++)
            {
                string id = r.ReadAscii(4);
                int len = (int)r.ReadUInt32();
                int end = Math.Min(r.Pos + len, data.Length);
                if (id != "MTrk") { r.Pos = end; t--; continue; }

                ParseTrack(r, end, t, tempoChanges, rawNotes);
                r.Pos = end;
            }

            tempoChanges.Sort((a, b) => a.tick.CompareTo(b.tick));
            var tempo = new TempoMap(division, tempoChanges);

            var song = new Song { Name = name };

            // ¿Las manos vienen separadas por pista? (típico: pista 1 derecha, pista 2 izquierda)
            var tracksWithNotes = new SortedSet<int>();
            foreach (var n in rawNotes) tracksWithNotes.Add(n.track);
            var trackList = new List<int>(tracksWithNotes);
            bool handsByTrack = trackList.Count == 2;

            foreach (var n in rawNotes)
            {
                float start = tempo.TickToSeconds(n.startTick);
                float endSec = tempo.TickToSeconds(Math.Max(n.endTick, n.startTick + 1));
                var note = new SongNote
                {
                    Pitch = n.pitch,
                    Start = start,
                    Duration = Math.Max(0.05f, endSec - start),
                    Velocity = n.vel,
                    Track = n.track,
                    Channel = n.channel,
                    Hand = handsByTrack
                        ? (n.track == trackList[0] ? Hand.Right : Hand.Left)
                        : (n.pitch < DefaultSplitPitch ? Hand.Left : Hand.Right)
                };
                song.Notes.Add(note);
                song.Length = Math.Max(song.Length, note.End);
                song.MinPitch = Math.Min(song.MinPitch, note.Pitch);
                song.MaxPitch = Math.Max(song.MaxPitch, note.Pitch);
            }

            song.Notes.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Pitch.CompareTo(b.Pitch));
            return song;
        }

        static void ParseTrack(Reader r, int end, int trackIndex,
            List<(long, int)> tempoChanges,
            List<(long, long, int, int, int, int)> notes)
        {
            long tick = 0;
            int runningStatus = 0;
            // Notas abiertas por (canal, tono). Cola para soportar notas repetidas superpuestas.
            var open = new Dictionary<int, Queue<(long tick, int vel)>>();

            while (r.Pos < end)
            {
                tick += r.ReadVarLen();
                if (r.Pos >= end) break;

                int status = r.PeekByte();
                if (status < 0x80)
                {
                    // Running status: reutiliza el último byte de estado de canal.
                    if (runningStatus == 0) throw new FormatException("Running status sin estado previo.");
                    status = runningStatus;
                }
                else
                {
                    r.Pos++;
                }

                if (status == 0xFF)
                {
                    int type = r.ReadByte();
                    int len = (int)r.ReadVarLen();
                    if (type == 0x51 && len == 3)
                    {
                        int us = (r.ReadByte() << 16) | (r.ReadByte() << 8) | r.ReadByte();
                        tempoChanges.Add((tick, us));
                    }
                    else
                    {
                        r.Pos += len;
                    }
                    if (type == 0x2F) break; // fin de pista
                    continue;
                }

                if (status == 0xF0 || status == 0xF7)
                {
                    int len = (int)r.ReadVarLen();
                    r.Pos += len;
                    continue;
                }

                runningStatus = status;
                int kind = status & 0xF0;
                int channel = status & 0x0F;

                switch (kind)
                {
                    case 0x80:
                    case 0x90:
                    {
                        int pitch = r.ReadByte();
                        int vel = r.ReadByte();
                        int key = (channel << 8) | pitch;
                        bool isOn = kind == 0x90 && vel > 0;
                        if (isOn)
                        {
                            if (!open.TryGetValue(key, out var q)) open[key] = q = new Queue<(long, int)>();
                            q.Enqueue((tick, vel));
                        }
                        else if (open.TryGetValue(key, out var q) && q.Count > 0)
                        {
                            var on = q.Dequeue();
                            notes.Add((on.tick, tick, pitch, on.vel, trackIndex, channel));
                        }
                        break;
                    }
                    case 0xA0:
                    case 0xB0:
                    case 0xE0:
                        r.Pos += 2;
                        break;
                    case 0xC0:
                    case 0xD0:
                        r.Pos += 1;
                        break;
                    default:
                        throw new FormatException($"Byte de estado MIDI inesperado: 0x{status:X2}");
                }
            }

            // Notas que nunca se cerraron: se cierran al final de la pista.
            foreach (var kv in open)
                foreach (var on in kv.Value)
                    notes.Add((on.tick, tick, kv.Key & 0xFF, on.vel, trackIndex, kv.Key >> 8));
        }

        /// <summary>Convierte ticks a segundos respetando todos los cambios de tempo.</summary>
        class TempoMap
        {
            readonly int _ticksPerQuarter;
            readonly List<(long tick, double seconds, int usPerQuarter)> _segments = new List<(long, double, int)>();

            public TempoMap(int ticksPerQuarter, List<(long tick, int usPerQuarter)> changes)
            {
                _ticksPerQuarter = Math.Max(1, ticksPerQuarter);
                long lastTick = 0;
                double lastSec = 0;
                int lastTempo = 500000; // 120 BPM por defecto
                _segments.Add((0, 0, lastTempo));
                foreach (var c in changes)
                {
                    lastSec += (c.tick - lastTick) * (lastTempo / 1_000_000.0) / _ticksPerQuarter;
                    lastTick = c.tick;
                    lastTempo = c.usPerQuarter;
                    _segments.Add((lastTick, lastSec, lastTempo));
                }
            }

            public float TickToSeconds(long tick)
            {
                var seg = _segments[0];
                for (int i = 1; i < _segments.Count && _segments[i].tick <= tick; i++) seg = _segments[i];
                return (float)(seg.seconds + (tick - seg.tick) * (seg.usPerQuarter / 1_000_000.0) / _ticksPerQuarter);
            }
        }

        class Reader
        {
            readonly byte[] _d;
            public int Pos;
            public Reader(byte[] d) { _d = d; }

            public int PeekByte() => Pos < _d.Length ? _d[Pos] : throw new FormatException("Fin de archivo inesperado.");
            public int ReadByte() => Pos < _d.Length ? _d[Pos++] : throw new FormatException("Fin de archivo inesperado.");
            public int ReadUInt16() => (ReadByte() << 8) | ReadByte();
            public uint ReadUInt32() => (uint)((ReadByte() << 24) | (ReadByte() << 16) | (ReadByte() << 8) | ReadByte());

            public string ReadAscii(int n)
            {
                var chars = new char[n];
                for (int i = 0; i < n; i++) chars[i] = (char)ReadByte();
                return new string(chars);
            }

            public long ReadVarLen()
            {
                long value = 0;
                for (int i = 0; i < 4; i++)
                {
                    int b = ReadByte();
                    value = (value << 7) | (uint)(b & 0x7F);
                    if ((b & 0x80) == 0) break;
                }
                return value;
            }
        }
    }
}
