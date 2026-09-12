using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class RepositoryMutationAuthorizationTests
{
    [Theory]
    [InlineData("StockItemRepository.cs")]
    [InlineData("LedgerRepository.cs")]
    [InlineData("MasterRepository.cs")]
    [InlineData("StockItemFieldRepository.cs")]
    [InlineData("BillOfMaterialRepository.cs")]
    [InlineData("VoucherTypeRepository.cs")]
    [InlineData("JobWorkerRepository.cs")]
    public async Task Master_repository_mutations_require_manage_masters_policy(string fileName)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(FindProjectRoot(), "Services", fileName));

        var expectedGuard = fileName == "StockItemFieldRepository.cs"
            ? "await company.RequirePolicyAsync(SecurityPolicies.ManageMasters, ct)"
            : "await companyContext.RequirePolicyAsync(SecurityPolicies.ManageMasters, cancellationToken)";
        Assert.Contains(expectedGuard, source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tally_import_apply_requires_export_data_policy()
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(FindProjectRoot(), "Services", "TallyXmlExchangeService.cs"));

        Assert.Contains("await companyContext.RequirePolicyAsync(SecurityPolicies.ExportData, cancellationToken)", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tally_export_requires_export_data_policy()
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(FindProjectRoot(), "Services", "TallyXmlExporter.cs"));

        Assert.Contains("await companyContext.RequirePolicyAsync(SecurityPolicies.ExportData, cancellationToken)", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ai_provider_configuration_requires_developer_policy_below_the_page()
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(FindProjectRoot(), "Services", "AssistantSettingsStore.cs"));

        Assert.Contains("await companyContext.RequirePolicyAsync(SecurityPolicies.DeveloperOnly, cancellationToken)", source, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TexTrack.Web.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("TexTrack project root was not found.");
    }
}
