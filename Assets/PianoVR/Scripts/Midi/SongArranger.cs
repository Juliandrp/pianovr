using System;
using System.Collections.Generic;

namespace PianoVR.Midi
{
    public enum Difficulty { Facil, Medio, Dificil, Experto }

    /// <summary>
    /// Prepara una canción para jugarla, al estilo Guitar Hero:
    /// - Ajusta las octavas para que quepa en el teclado que tienes (útil con teclados virtuales pequeños).
    /// - Según la dificultad decide qué notas tocas tú y cuáles suenan solas como acompañamiento.
    /// - Agrupa las notas en frases y marca algunas como "frases estrella".
    /// </summary>
    public static class SongArranger
    {
        const float ChordWindow = 0.035f;
        const int ChordsPerPhrase = 8;
        const float PhraseGap = 1.5f;

        public static string Label(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Facil: return "Fácil";
                case Difficulty.Medio: return "Medio";
                case Difficulty.Dificil: return "Difícil";
                default: return "Experto";
            }
        }

        public static string Description(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Facil: return "Solo la melodía, una nota a la vez";
                case Difficulty.Medio: return "Mano derecha completa";
                case Difficulty.Dificil: return "Ambas manos, izquierda simplificada";
                default: return "Todas las notas";
            }
        }

        public static Song Arrange(Song source, Difficulty difficulty, int minPitch, int maxPitch)
        {
            var song = new Song { Name = source.Name };
            foreach (var n in source.Notes) song.Notes.Add(n.Clone());

            FitToRange(song.Notes, minPitch, maxPitch);
            ChoosePlayable(song.Notes, difficulty);
            RemoveDuplicates(song.Notes);
            BuildPhrases(song.Notes);

            song.Notes.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Pitch.CompareTo(b.Pitch));
            foreach (var n in song.Notes)
            {
                song.Length = Math.Max(song.Length, n.End);
                song.MinPitch = Math.Min(song.MinPitch, n.Pitch);
                song.MaxPitch = Math.Max(song.MaxPitch, n.Pitch);
            }
            return song;
        }

        /// <summary>Transpone por octavas completas y "dobla" las notas que aún quedan fuera.</summary>
        static void FitToRange(List<SongNote> notes, int min, int max)
        {
            if (notes.Count == 0) return;

            int bestShift = 0, bestCount = -1;
            for (int shift = -48; shift <= 48; shift += 12)
            {
                int count = 0;
                foreach (var n in notes) if (n.Pitch + shift >= min && n.Pitch + shift <= max) count++;
                if (count > bestCount || (count == bestCount && Math.Abs(shift) < Math.Abs(bestShift)))
                {
                    bestCount = count;
                    bestShift = shift;
                }
            }

            bool canFold = max - min >= 11;
            foreach (var n in notes)
            {
                n.Pitch += bestShift;
                if (!canFold) continue;
                while (n.Pitch < min) n.Pitch += 12;
                while (n.Pitch > max) n.Pitch -= 12;
            }
        }

        static void ChoosePlayable(List<SongNote> notes, Difficulty d)
        {
            foreach (var chord in Chords(notes))
            {
                // Más aguda y más grave de cada mano dentro del acorde.
                SongNote topRight = null, bottomLeft = null;
                foreach (var n in chord)
                {
                    if (n.Hand == Hand.Right && (topRight == null || n.Pitch > topRight.Pitch)) topRight = n;
                    if (n.Hand == Hand.Left && (bottomLeft == null || n.Pitch < bottomLeft.Pitch)) bottomLeft = n;
                }

                foreach (var n in chord)
                {
                    switch (d)
                    {
                        case Difficulty.Facil: n.Playable = n == topRight; break;
                        case Difficulty.Medio: n.Playable = n.Hand == Hand.Right; break;
                        case Difficulty.Dificil: n.Playable = n.Hand == Hand.Right || n == bottomLeft; break;
                        default: n.Playable = true; break;
                    }
                }
            }
        }

        /// <summary>Si al doblar octavas quedan dos notas iguales a la vez, solo una se juega.</summary>
        static void RemoveDuplicates(List<SongNote> notes)
        {
            foreach (var chord in Chords(notes))
            {
                var seen = new HashSet<int>();
                foreach (var n in chord)
                    if (n.Playable && !seen.Add(n.Pitch)) n.Playable = false;
            }
        }

        static void BuildPhrases(List<SongNote> notes)
        {
            int phrase = 0, chordsInPhrase = 0;
            float lastStart = float.NegativeInfinity;

            foreach (var chord in Chords(notes))
            {
                bool any = false;
                foreach (var n in chord) if (n.Playable) { any = true; break; }
                if (!any) continue;

                float start = chord[0].Start;
                if (chordsInPhrase >= ChordsPerPhrase || (chordsInPhrase > 0 && start - lastStart > PhraseGap))
                {
                    phrase++;
                    chordsInPhrase = 0;
                }

                bool star = phrase % 4 == 2; // frases 2, 6, 10... son estrella
                foreach (var n in chord)
                {
                    if (!n.Playable) { n.Phrase = -1; n.Star = false; continue; }
                    n.Phrase = phrase;
                    n.Star = star;
                }

                chordsInPhrase++;
                lastStart = start;
            }
        }

        /// <summary>Agrupa notas que empiezan casi al mismo tiempo (la lista viene ordenada por inicio).</summary>
        static IEnumerable<List<SongNote>> Chords(List<SongNote> notes)
        {
            var sorted = new List<SongNote>(notes);
            sorted.Sort((a, b) => a.Start.CompareTo(b.Start));

            var current = new List<SongNote>();
            foreach (var n in sorted)
            {
                if (current.Count > 0 && n.Start - current[0].Start > ChordWindow)
                {
                    yield return current;
                    current = new List<SongNote>();
                }
                current.Add(n);
            }
            if (current.Count > 0) yield return current;
        }
    }
}
