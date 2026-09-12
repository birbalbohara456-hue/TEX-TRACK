using TexTrack.Web.Models;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class AgeBandFilterTests
{
    [Fact]
    public void Defaults_define_the_four_expected_material_age_bands()
    {
        Assert.Equal(
            new[] { "0–45 Days", "46–90 Days", "91–120 Days", "121+ Days" },
            AgeBandFilterValue.DefaultBands.Select(x => x.Label));
    }

    [Fact]
    public void Empty_selection_includes_all_ages_including_no_material_out()
    {
        var filter = AgeBandFilterValue.All();

        Assert.True(filter.Matches(null));
        Assert.True(filter.Matches(0));
        Assert.True(filter.Matches(500));
    }

    [Fact]
    public void Multiple_selected_bands_use_inclusive_or_matching()
    {
        var filter = AgeBandFilterValue.Create(AgeBandFilterValue.DefaultBands, [0, 2]);

        Assert.False(filter.Matches(null));
        Assert.True(filter.Matches(0));
        Assert.True(filter.Matches(45));
        Assert.False(filter.Matches(46));
        Assert.True(filter.Matches(91));
        Assert.True(filter.Matches(120));
        Assert.False(filter.Matches(121));
    }

    [Fact]
    public void Custom_bands_allow_one_to_four_non_overlapping_ranges()
    {
        var filter = AgeBandFilterValue.Create(
            [new AgeBandRange(10, 20), new AgeBandRange(30, null)],
            [1]);

        Assert.False(filter.Matches(20));
        Assert.False(filter.Matches(29));
        Assert.True(filter.Matches(30));
        Assert.True(filter.Matches(300));

        Assert.Throws<ArgumentException>(() => AgeBandFilterValue.Create([]));
        Assert.Throws<ArgumentException>(() => AgeBandFilterValue.Create(
            [new(0, 10), new(10, 20)]));
        Assert.Throws<ArgumentException>(() => AgeBandFilterValue.Create(
            [new(0, 1), new(2, 3), new(4, 5), new(6, 7), new(8, null)]));
    }

    [Fact]
    public async Task Job_worker_control_uses_the_reusable_age_band_component()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TexTrack.Web.csproj"))) root = root.Parent;
        Assert.NotNull(root);

        var page = await File.ReadAllTextAsync(Path.Combine(root!.FullName, "Components", "Pages", "Reports", "JobWorkerControlCenter.razor"));
        var component = await File.ReadAllTextAsync(Path.Combine(root.FullName, "Components", "AgeBandFilter.razor"));

        Assert.Contains("<AgeBandFilter", page, StringComparison.Ordinal);
        Assert.Contains("OldestOutstandingMaterialAge", page, StringComparison.Ordinal);
        Assert.DoesNotContain("AgingQuickLabel", page, StringComparison.Ordinal);
        Assert.Contains("Customize bands (1–4)", component, StringComparison.Ordinal);
    }
}
