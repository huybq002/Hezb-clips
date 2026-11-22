using HezbClips.Core.Imaging;

namespace HezbClips.Core.Tests;

public class OcrScalingTests
{
    static OcrLineInfo Line(params (string T, double? C)[] w) => new(w.Select(x => new OcrWordInfo(x.T, default, x.C)).ToList());

    [Fact] public void Small_picture_tries_1_2_3()
        => Assert.Equal([1.0, 2.0, 3.0], OcrScaling.Candidates(500, 200, 8000));

    [Fact] public void Medium_picture_tries_1_2() => Assert.Equal([1.0, 2.0], OcrScaling.Candidates(900, 500, 8000));

    [Fact] public void Big_picture_stays_1() => Assert.Equal([1.0], OcrScaling.Candidates(3000, 1500, 8000));

    [Fact] public void Engine_limit_caps_candidates() => Assert.Equal([1.0, 2.0], OcrScaling.Candidates(1000, 200, 2500));

    [Fact] public void Fixed_scale_follows_old_rule_and_limit()
    {
        Assert.Equal(3, OcrScaling.Fixed(400, 200, 8000));
        Assert.Equal(2, OcrScaling.Fixed(900, 500, 8000));
        Assert.Equal(1, OcrScaling.Fixed(3000, 1500, 8000));
        Assert.Equal(2.5, OcrScaling.Fixed(1000, 200, 2500), 3);
    }

    [Fact] public void MeanConfidence_is_weighted_by_length()
        => Assert.Equal(90.625, OcrScaling.MeanConfidence([Line(("ab", 100.0), ("cdefgh", 87.5))])!.Value, 3);

    [Fact] public void MeanConfidence_null_without_confidence_zero_without_words()
    {
        Assert.Null(OcrScaling.MeanConfidence([Line(("abc", null))]));
        Assert.Equal(0, OcrScaling.MeanConfidence([]));
    }
}
