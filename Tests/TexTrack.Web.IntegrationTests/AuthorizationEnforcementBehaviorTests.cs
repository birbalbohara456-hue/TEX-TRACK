using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class AuthorizationEnforcementBehaviorTests
{
    [Fact]
    public async Task Ledger_save_is_refused_for_a_role_without_manage_masters_before_touching_the_database()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        var repository = environment.CreateLedgerRepository(SecurityRoleCodes.Operator, companyId: 1);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => repository.SaveAsync(new LedgerEditModel { Name = "Should Never Be Created" }));

        await using var db = environment.CreateDb();
        Assert.False(await db.Ledgers.AnyAsync(x => x.Name == "Should Never Be Created"));
    }

    [Fact]
    public async Task Ledger_save_actually_succeeds_for_a_role_with_manage_masters()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        var groupId = await environment.CreateLedgerGroupAsync(companyId: 1, name: "ZZ Test Group Debtors");
        var repository = environment.CreateLedgerRepository(SecurityRoleCodes.Manager, companyId: 1);

        var result = await repository.SaveAsync(new LedgerEditModel { Name = "Real Customer Ltd", LedgerGroupId = groupId });

        Assert.True(result.Success, result.Message);
        await using var db = environment.CreateDb();
        Assert.True(await db.Ledgers.AnyAsync(x => x.CompanyId == 1 && x.Name == "Real Customer Ltd"));
    }

    [Fact]
    public async Task A_ledger_created_under_one_company_is_invisible_and_unmodifiable_from_another_company_context()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        var companyOneGroupId = await environment.CreateLedgerGroupAsync(companyId: 1, name: "ZZ Test Group Creditors");
        var ownerRepository = environment.CreateLedgerRepository(SecurityRoleCodes.Manager, companyId: 1);
        var created = await ownerRepository.SaveAsync(new LedgerEditModel { Name = "Company One Only Ledger", LedgerGroupId = companyOneGroupId });
        Assert.True(created.Success, created.Message);
        var ledgerId = created.EntityId!.Value;

        var secondCompanyId = await environment.CreateCompanyAsync("Second Tenant Pvt Ltd");
        var companyTwoGroupId = await environment.CreateLedgerGroupAsync(companyId: secondCompanyId, name: "ZZ Test Group Creditors");
        var strangerRepository = environment.CreateLedgerRepository(SecurityRoleCodes.Manager, companyId: secondCompanyId);

        var listFromOtherCompany = await strangerRepository.GetListAsync();
        Assert.DoesNotContain(listFromOtherCompany, x => x.Id == ledgerId);

        // Using company two's own valid ledger group isolates the id-scoped lookup this test
        // targets - if the group check itself failed instead, that would prove a different
        // (also correct) guard, not the one this test is about.
        await Assert.ThrowsAsync<InvalidOperationException>(() => strangerRepository.SaveAsync(
            new LedgerEditModel { Id = ledgerId, Name = "Hijacked Name", LedgerGroupId = companyTwoGroupId }));

        await using var db = environment.CreateDb();
        Assert.Equal("Company One Only Ledger", await db.Ledgers.Where(x => x.Id == ledgerId).Select(x => x.Name).SingleAsync());
    }

    [Fact]
    public async Task Assistant_provider_settings_are_refused_for_a_non_developer_role_before_any_file_access()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        var store = environment.CreateAssistantSettingsStore(SecurityRoleCodes.Manager, companyId: 1);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => store.SaveAsync(new AssistantSettingsUpdate { BaseUrl = "https://example.test/api", ApiKey = "irrelevant" }));
    }
}
