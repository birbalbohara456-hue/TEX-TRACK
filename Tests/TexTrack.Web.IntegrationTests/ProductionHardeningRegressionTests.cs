using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class ProductionHardeningRegressionTests
{
    [Fact]
    public void Material_out_stage_assignment_uses_the_migrated_postgresql_column()
    {
        var options = new DbContextOptionsBuilder<TexTrackDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=metadata_only;Password=metadata_only")
            .Options;
        using var db = new TexTrackDbContext(options);
        var entity = db.Model.FindEntityType(typeof(MaterialOutLine))!;
        var table = StoreObjectIdentifier.Table("material_out_lines", null);
        Assert.Equal("stage_assignment_id", entity.FindProperty(nameof(MaterialOutLine.StageAssignmentId))!.GetColumnName(table));
    }

    // Business_reset_source_preserves_identity_and_role_tables was a source-text assertion test
    // (H1-2 finding). Replaced by a real behavioral test that actually runs
    // ClearAllBusinessDataAsync against a disposable database and asserts what survives -
    // see DatabaseMaintenanceServiceTests.cs.

    // Modal_pruning_keeps_temporarily_hidden_keyboard_contexts_registered was a source-text
    // assertion test (H1-2 finding). Reassessed per Codex's direction: the existing Node.js
    // harness in Tests/JavaScript/textrack-alt-delete.test.cjs already had everything needed
    // (a FakeElement with independent connected/visible flags, and direct inspection of
    // controller.contexts) to prove this behaviorally without any new framework - see
    // 'a context hidden by an inert modal stays registered - only detached roots are pruned'
    // in that file. No C# equivalent needed; removed rather than left duplicating JS coverage.

    [Fact]
    public async Task Job_work_order_entry_does_not_require_preexisting_transactional_masters()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            FindProjectRoot(), "Components", "Pages", "Vouchers", "JobWorkOutOrder.razor"));

        var canCreateLine = source.Split('\n').Single(x => x.Contains("private bool CanCreate =>", StringComparison.Ordinal));
        Assert.Contains("lookups.VoucherTypes.Count > 0", canCreateLine, StringComparison.Ordinal);
        Assert.Contains("lookups.Godowns.Count > 0", canCreateLine, StringComparison.Ordinal);
        Assert.DoesNotContain("lookups.JobWorkers", canCreateLine, StringComparison.Ordinal);
        Assert.DoesNotContain("lookups.FinishedGoods", canCreateLine, StringComparison.Ordinal);
        Assert.Contains("else await OpenCreate();", source, StringComparison.Ordinal);
    }

    // Reassessed per Codex's direction, more precisely than "needs new infrastructure": the
    // existing Tests/JavaScript harness pattern (Node's built-in vm/node:test, no external
    // framework) could cover this without adding a framework, but unlike the modal-pruning test
    // above, no existing harness models tables, sessionStorage, or location for
    // textrack-header-filters.js - covering it behaviorally means a new harness FILE of
    // comparable size to the existing ones, not a small addition to one that already exists.
    // Left as a source-text test for now; flagged as a real, buildable next step rather than
    // something requiring new tooling.
    [Fact]
    public async Task Shared_header_filters_cover_reusable_data_grids_and_restore_navigation_state()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(FindProjectRoot(), "wwwroot", "js", "textrack-header-filters.js"));
        Assert.Contains("table.data-grid, table.selectable-grid", source, StringComparison.Ordinal);
        Assert.Contains("sessionStorage.setItem(state.storageKey", source, StringComparison.Ordinal);
        Assert.Contains("location.pathname", source, StringComparison.Ordinal);
        Assert.Contains("Clear all filters", source, StringComparison.Ordinal);
        Assert.Contains("data-action=\"current\"", source, StringComparison.Ordinal);
        Assert.Contains("Deselect all", source, StringComparison.Ordinal);
        Assert.Contains("matchingValues(search.value || '')", source, StringComparison.Ordinal);
        Assert.Contains("shouldDeselect", source, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TexTrack.Web.csproj"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("TexTrack project root was not found.");
    }
}
