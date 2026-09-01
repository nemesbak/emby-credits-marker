// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using System.Linq;
using Emby.CreditsMarker;
using Xunit;

public class LooksLikeEndCreditsTests
{
    [Theory]
    [InlineData("Credits")]
    [InlineData("credits")]
    [InlineData("End Credits")]
    [InlineData("end credit")]
    [InlineData("Dub credits")]
    [InlineData("Closing Credits")]
    [InlineData("Credits Start")]
    [InlineData("Ending")]
    [InlineData("Outro")]
    [InlineData("Créditos")]
    [InlineData("23. End Credits")]
    [InlineData("ED")]
    [InlineData("ED 01 - \"Heroes\" by Brian The Sun")]
    public void Accepts_real_credits_labels(string name)
        => Assert.True(MarkerWriter.LooksLikeEndCredits(name));

    [Theory]
    [InlineData("Opening Credits")]
    [InlineData("2. Opening Credits")]
    [InlineData("Matthews Takes the Credit")]
    [InlineData("Sheldon Watches Penny Struggle, Opening Credits")]
    [InlineData("Scene 1")]
    [InlineData("Recap")]
    [InlineData("Chapter 5")]
    [InlineData("Capítulo 10")]
    [InlineData("Credits End")]
    [InlineData("Escena Poscréditos")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_non_credits_names(string name)
        => Assert.False(MarkerWriter.LooksLikeEndCredits(name));
}

public class DensestClusterTests
{
    [Fact]
    public void Picks_the_tightest_run()
    {
        var xs = new List<double> { 0.10, 0.95, 0.96, 0.965, 0.97, 0.50 };
        xs.Sort();
        var cl = DetectCreditsTask.DensestCluster(xs, 0.03);
        Assert.Equal(4, cl.Count);
        Assert.All(cl, v => Assert.InRange(v, 0.95, 0.97));
    }

    [Fact]
    public void Single_element_when_all_spread_out()
    {
        var xs = new List<double> { 0.1, 0.5, 0.9 };
        Assert.Single(DetectCreditsTask.DensestCluster(xs, 0.03));
    }
}

public class LongestCommonRunTests
{
    [Fact]
    public void Finds_a_shared_run_at_an_offset()
    {
        // b == a shifted right by 3, with a matching 10-frame run in the middle
        var a = new uint[] { 1, 2, 3, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 9, 8 };
        var b = new uint[] { 7, 7, 7, 1, 2, 3, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109 };
        var m = OutroFingerprint.LongestCommonRun(a, b, 0);
        Assert.True(m.Length >= 10);
    }

    [Fact]
    public void FramesToSeconds_is_monotonic()
        => Assert.True(OutroFingerprint.FramesToSeconds(100) < OutroFingerprint.FramesToSeconds(200));
}
