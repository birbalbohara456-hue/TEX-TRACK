using TexTrack.Web.Models;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class UsedVariantAttributeIntegrityTests
{
    [Fact]
    public async Task Unused_colour_and_size_identity_remain_editable()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();

        var colour = await environment.MasterRepository.GetForEditAsync(MasterKind.Colour, seed.ColourId);
        Assert.NotNull(colour);
        Assert.False(colour.IsIdentityLocked);
        colour.Name = "Jet Black";
        colour.ColourCode = "JBLK";
        var colourResult = await environment.MasterRepository.SaveAsync(MasterKind.Colour, colour);
        Assert.True(colourResult.Success, colourResult.Message);

        var size = await environment.MasterRepository.GetForEditAsync(MasterKind.Size, seed.SizeId);
        Assert.NotNull(size);
        Assert.False(size.IsIdentityLocked);
        size.Name = "M-Regular";
        var sizeResult = await environment.MasterRepository.SaveAsync(MasterKind.Size, size);
        Assert.True(sizeResult.Success, sizeResult.Message);
    }

    [Fact]
    public async Task Used_colour_and_size_identity_are_locked_but_can_be_deactivated()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        var seed = await environment.SeedAsync();
        await environment.ReplaceBaseVariantWithStructuralMappingsAsync(seed.StockItemId);
        await environment.AddSizeAllocationUsageAsync(10);

        var colour = await environment.MasterRepository.GetForEditAsync(MasterKind.Colour, seed.ColourId);
        Assert.NotNull(colour);
        Assert.True(colour.IsIdentityLocked);
        colour.Name = "Must Not Replace Black";
        var blockedColour = await environment.MasterRepository.SaveAsync(MasterKind.Colour, colour);
        Assert.False(blockedColour.Success);
        Assert.Contains("saved transaction history", blockedColour.Message, StringComparison.Ordinal);

        colour = await environment.MasterRepository.GetForEditAsync(MasterKind.Colour, seed.ColourId);
        Assert.NotNull(colour);
        colour.IsActive = false;
        var deactivatedColour = await environment.MasterRepository.SaveAsync(MasterKind.Colour, colour);
        Assert.True(deactivatedColour.Success, deactivatedColour.Message);

        var size = await environment.MasterRepository.GetForEditAsync(MasterKind.Size, seed.SizeId);
        Assert.NotNull(size);
        Assert.True(size.IsIdentityLocked);
        size.Name = "Must Not Replace Medium";
        var blockedSize = await environment.MasterRepository.SaveAsync(MasterKind.Size, size);
        Assert.False(blockedSize.Success);
        Assert.Contains("saved transaction history", blockedSize.Message, StringComparison.Ordinal);

        size = await environment.MasterRepository.GetForEditAsync(MasterKind.Size, seed.SizeId);
        Assert.NotNull(size);
        size.IsActive = false;
        var deactivatedSize = await environment.MasterRepository.SaveAsync(MasterKind.Size, size);
        Assert.True(deactivatedSize.Success, deactivatedSize.Message);

        var colourRow = Assert.Single(
            (await environment.MasterRepository.GetListAsync(MasterKind.Colour))
            .Where(x => x.Id == seed.ColourId));
        var sizeRow = Assert.Single(
            (await environment.MasterRepository.GetListAsync(MasterKind.Size))
            .Where(x => x.Id == seed.SizeId));
        Assert.False(colourRow.IsActive);
        Assert.False(sizeRow.IsActive);

        var itemLookups = await environment.Repository.GetLookupsAsync();
        Assert.DoesNotContain(itemLookups.Colours, x => x.Id == seed.ColourId);
        Assert.DoesNotContain(itemLookups.Sizes, x => x.Id == seed.SizeId);

        await environment.UseCanonicalJwoTypeCodeAsync();
        var jwoLookups = await environment.JobWorkOrderRepository.GetLookupsAsync();
        var component = Assert.Single(jwoLookups.Components.Where(x => x.Id == seed.StockItemId));
        Assert.Empty(component.Colours);
        Assert.Empty(component.Sizes);
        Assert.Empty(component.Variants);
    }
}
