public class Note
{
    public int Index, Octave, PitchClass;
    public string Name;
    public double Hz;
}

// Twelve-tone equal temperament. Octaves begin at C; every note is generated exactly once.
public static class MusicalNotes
{
    public const string Sharp = "\u266F", Flat = "\u266D";
    static readonly string[] Names = { "C", "C" + Sharp, "D", "D" + Sharp, "E", "F", "F" + Sharp, "G", "G" + Sharp, "A", "A" + Sharp, "B" };
    static readonly string Letters = "CDEFGAB";
    static readonly int[] LetterOffsets = { 0, 2, 4, 5, 7, 9, 11 };

    public static double Frequency(int index, double concertAHz) => concertAHz * Math.Pow(2, (index - 69) / 12.0);

    public static Note[] Generate(double concertAHz, int minimumOctave, int maximumOctave)
    {
        var notes = new List<Note>();
        for (int o = minimumOctave; o <= maximumOctave; o++)
            for (int p = 0; p < 12; p++)
            {
                int n = 12 * (o + 1) + p;
                notes.Add(new Note { Index = n, Octave = o, PitchClass = p, Name = Names[p] + o, Hz = Frequency(n, concertAHz) });
            }
        return notes.ToArray();
    }

    public static string PitchClassName(int pc) => Names[pc];

    // Full note index of a spelling such as B-sharp-3; the octave number follows the letter, so it can cross C.
    public static int Spelled(char letter, int accidental, int octave) => 12 * (octave + 1) + LetterOffsets[Letters.IndexOf(letter)] + accidental;
    public static int Octave(int index) => (int)Math.Floor(index / 12.0) - 1;
    public static int PitchClass(int index) => ((index % 12) + 12) % 12;

    public static string Accidentals(int accidental) => accidental > 0 ? string.Concat(Enumerable.Repeat(Sharp, accidental)) : string.Concat(Enumerable.Repeat(Flat, -accidental));

    // Column header text: sharp and flat equivalents for black keys.
    public static string Header(int pc) => pc is 1 or 3 or 6 or 8 or 10 ? Names[pc] + "/" + Names[(pc + 1) % 12 == 0 ? 0 : (pc + 1) % 12][..1] + Flat : Names[pc];

    // Other spellings (at most double accidentals) of each pitch class, excluding its main labels.
    public static string[] Aliases(int pc)
    {
        var result = new List<string>();
        foreach (char letter in Letters)
            for (int acc = -2; acc <= 2; acc++)
            {
                int offset = LetterOffsets[Letters.IndexOf(letter)] + acc;
                if (((offset % 12) + 12) % 12 != pc) continue;
                string name = letter + Accidentals(acc);
                if (Header(pc).Split('/').Contains(name)) continue;
                result.Add(name);
            }
        return result.ToArray();
    }
}
