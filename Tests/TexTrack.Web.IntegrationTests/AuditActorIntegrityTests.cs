using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class AuditActorIntegrityTests
{
    [Fact]
    public async Task Mutation_services_do_not_write_generic_developer_or_tally_xml_actors()
    {
        var services = Path.Combine(FindProjectRoot(), "Services");
        var source = string.Join('\n', await Task.WhenAll(
            Directory.GetFiles(services, "*.cs").Select(path => File.ReadAllTextAsync(path))));

        Assert.DoesNotContain("PerformedBy = \"Developer\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedBy = \"Developer\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ModifiedBy = \"Developer\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedBy = \"Tally XML\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ModifiedBy = \"Tally XML\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedBy = \"JWO snapshot\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Current_context_exposes_claim_bound_actor_and_source_attribution()
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(FindProjectRoot(), "Services", "DatabaseStatus.cs"));

        Assert.Contains("ClaimTypes.NameIdentifier", source, StringComparison.Ordinal);
        Assert.Contains("public string Actor", source, StringComparison.Ordinal);
        Assert.Contains("public string ActorFor", source, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TexTrack.Web.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("TexTrack project root was not found.");
    }
}
