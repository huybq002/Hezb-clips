using HezbClips.Core.Imaging;

namespace HezbClips.Core.Tests;

public class OcrTextCleanerTests
{
    static string[] W(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    [Fact] public void Keeps_normal_text() =>
        Assert.Equal("Tổng cộng: 1.250.000 đ", OcrTextCleaner.CleanLine(W("Tổng cộng: 1.250.000 đ")));

    [Fact] public void Drops_border_marks_around_words() =>
        Assert.Equal("Họ và tên", OcrTextCleaner.CleanLine(W("| Họ và tên |")));

    [Fact] public void Drops_symbol_only_lines_and_tokens()
    {
        Assert.Equal("", OcrTextCleaner.CleanLine(W("_ _ — ~ | ] [")));
        Assert.Equal("Mã đơn 12345", OcrTextCleaner.CleanLine(W("~ Mã ■ đơn 12345 ]")));
    }

    [Fact] public void Drops_edge_lookalike_letters_on_longer_lines() =>
        Assert.Equal("Số tài khoản 0123456789", OcrTextCleaner.CleanLine(W("I Số tài khoản 0123456789 l")));

    [Fact] public void Fixes_digit_lookalikes_only_in_numeric_tokens()
    {
        Assert.Equal("Tổng 1.250.000", OcrTextCleaner.CleanLine(W("Tổng 1.250.OOO")));
        Assert.Equal("năm 2024", OcrTextCleaner.CleanLine(W("năm 2O24")));
        Assert.Equal("Hello World A1", OcrTextCleaner.CleanLine(W("Hello World A1"))); // real letters stay
        Assert.Equal("Oil 3", OcrTextCleaner.CleanLine(W("Oil 3")));
    }

    [Fact] public void Keeps_lone_I_on_short_lines() =>
        Assert.Equal("I am", OcrTextCleaner.CleanLine(W("I am")));

    [Fact] public void Drops_mostly_symbol_lines() =>
        Assert.Equal("", OcrTextCleaner.CleanLine(W("a ~~~~ ==== ---- ^^^^ ####")));

    [Fact] public void Drops_single_stray_letter_but_keeps_single_digit()
    {
        Assert.Equal("", OcrTextCleaner.CleanLine(W("x")));
        Assert.Equal("7", OcrTextCleaner.CleanLine(W("7")));
    }

    [Fact] public void Normalises_vietnamese_to_precomposed()
    {
        var decomposed = "Tống"; // ô + combining acute written as separate marks
        var cleaned = OcrTextCleaner.CleanLine(new[] { decomposed });
        Assert.Equal(cleaned.Normalize(System.Text.NormalizationForm.FormC), cleaned);
    }

    [Fact] public void Clean_joins_non_empty_lines()
    {
        var text = OcrTextCleaner.Clean(new[] { W("Dòng một"), W("| _ |"), W("Dòng hai") }, "\n");
        Assert.Equal("Dòng một\nDòng hai", text);
    }
}
