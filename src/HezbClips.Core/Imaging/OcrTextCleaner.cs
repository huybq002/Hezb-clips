using System.Text;

namespace HezbClips.Core.Imaging;

/// <summary>Cleans raw OCR output: removes the "noise" OCR invents from borders, lines, icons and shadows
/// (stray | _ ~ [ ] marks, lone letters at the edges of a line, lines that are mostly symbols), keeps letters and digits
/// together with normal punctuation inside the text, and returns Unicode-normalised (NFC) text so Vietnamese diacritics
/// are one character each and paste correctly everywhere.</summary>
public static class OcrTextCleaner
{
    // characters that are fine to keep around words
    const string KeepPunct = ".,:;!?()-–%/@#&+$'\"’“”…";
    // characters OCR commonly produces for borders / dividers / bullets when it sees a box edge as a letter
    const string EdgeLookalikes = "lI1|i!¦";

    static bool Alnum(char c) => char.IsLetterOrDigit(c);

    /// <summary>One cleaned line from its words; empty string when the line is only noise.</summary>
    public static string CleanLine(IEnumerable<string> words)
    {
        var tokens = new List<string>();
        foreach (var raw in words)
        {
            var t = Trim(raw);
            if (t.Length == 0 || !t.Any(Alnum)) continue; // pure symbols
            tokens.Add(FixDigits(t));
        }
        if (tokens.Count == 0) return "";

        // a lone I / l / 1 / | at the very start or end of a longer line is almost always a box edge
        if (tokens.Count >= 3)
        {
            if (tokens[0].Length == 1 && EdgeLookalikes.Contains(tokens[0][0])) tokens.RemoveAt(0);
            if (tokens.Count >= 3 && tokens[^1].Length == 1 && EdgeLookalikes.Contains(tokens[^1][0])) tokens.RemoveAt(tokens.Count - 1);
        }

        var line = string.Join(' ', tokens);
        var visible = line.Count(c => !char.IsWhiteSpace(c));
        var alnum = line.Count(Alnum);
        if (visible == 0 || alnum * 2 < visible) return ""; // mostly symbols
        if (tokens.Count == 1 && tokens[0].Length == 1 && !char.IsDigit(tokens[0][0])) return ""; // one stray letter
        return line.Normalize(NormalizationForm.FormC);
    }

    /// <summary>Clean all lines and join them with line breaks.</summary>
    public static string Clean(IEnumerable<IEnumerable<string>> lines, string newLine = "\n") =>
        string.Join(newLine, lines.Select(CleanLine).Where(l => l.Length > 0));

    // "1.250.OOO" / "2O24" / "l5": OCR reads digits as O/l/I; when a token already has digits and its only letters are those lookalikes, make them digits
    static string FixDigits(string t)
    {
        if (!t.Any(char.IsDigit)) return t;
        foreach (var c in t) if (char.IsLetter(c) && "OoIl".IndexOf(c) < 0) return t;
        var sb = new StringBuilder(t.Length);
        foreach (var c in t) sb.Append(c is 'O' or 'o' ? '0' : c is 'I' or 'l' ? '1' : c);
        return sb.ToString();
    }

    // cut everything that is not a letter, digit or normal punctuation from both ends of a word,
    // and drop odd symbols inside it
    static string Trim(string w)
    {
        var sb = new StringBuilder(w.Length);
        foreach (var c in w) if (Alnum(c) || KeepPunct.Contains(c)) sb.Append(c);
        var s = sb.ToString().Trim();
        int a = 0, b = s.Length;
        // keep opening/closing marks that belong to the word, but not dangling ones at the ends of a stray fragment
        while (a < b && !Alnum(s[a]) && !"(\"“'#$@".Contains(s[a])) a++;
        while (b > a && !Alnum(s[b - 1]) && !".,:;!?)%\"”'’…".Contains(s[b - 1])) b--;
        return s[a..b];
    }
}
