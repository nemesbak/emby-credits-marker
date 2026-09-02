// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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

public class LocalizationTests
{
    private static void WithCulture(string name, System.Action body)
    {
        var prevUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentUICulture = prevUi;
        }
    }

    [Fact]
    public void Translates_a_known_string_for_spanish()
        => WithCulture("es", () => Assert.Equal("Procesar episodios", Localization.T("Process episodes")));

    [Fact]
    public void Regional_spanish_falls_back_to_neutral_table()
        => WithCulture("es-MX", () => Assert.Equal("Procesar episodios", Localization.T("Process episodes")));

    [Fact]
    public void Unknown_language_stays_english()
        => WithCulture("de", () => Assert.Equal("Process episodes", Localization.T("Process episodes")));

    [Fact]
    public void Unknown_key_is_returned_unchanged()
        => WithCulture("es", () => Assert.Equal("not a real label", Localization.T("not a real label")));

    [Fact]
    public void Format_helper_fills_placeholders_from_translated_string()
        => WithCulture("es", () => Assert.Equal("hace 5 min", Localization.TF("{0} min ago", 5)));
}

public class LocalizedDescriptorTests
{
    private static readonly object Gate = new object();
    private static bool _registered;

    private static PropertyDescriptorCollection LocalisedProps()
    {
        lock (Gate)
        {
            if (!_registered)
            {
                // mirror what Plugin's constructor does
                TypeDescriptor.AddProvider(
                    new LocalizedTypeDescriptionProvider(TypeDescriptor.GetProvider(typeof(PluginOptions))),
                    typeof(PluginOptions));
                _registered = true;
            }
        }
        return TypeDescriptor.GetProperties(new PluginOptions());
    }

    [Fact]
    public void Descriptor_display_name_and_description_are_localised()
    {
        var prevUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es");
            var p = LocalisedProps()["ProcessEpisodes"];
            Assert.Equal("Procesar episodios", p.DisplayName);
            Assert.Equal("Analiza los episodios y marca dónde empiezan los créditos finales.", p.Description);

            // Emby reads the label off the attribute, not the descriptor
            var dn = p.Attributes.OfType<DisplayNameAttribute>().First();
            Assert.Equal("Procesar episodios", dn.DisplayName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = prevUi;
        }
    }

    [Fact]
    public void Descriptor_stays_english_for_other_languages()
    {
        var prevUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr");
            var p = LocalisedProps()["ProcessEpisodes"];
            Assert.Equal("Process episodes", p.DisplayName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = prevUi;
        }
    }
}
