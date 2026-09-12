using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed class JobWorkerRepository(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext)
{
    private static readonly Regex GstinPattern = new(
        "^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<PagedReportResult<JobWorkerListItem>> GetPageAsync(
        string? searchText = null,
        int page = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Ledgers.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.IsJobWorker);

        var search = Normalize(searchText);
        if (search.Length > 0)
        {
            query = query.Where(x =>
                x.NameNormalized.Contains(search) ||
                x.Alias.ToUpper().Contains(search) ||
                x.TallyLedgerName.ToUpper().Contains(search) ||
                x.City.ToUpper().Contains(search) ||
                x.State.ToUpper().Contains(search) ||
                x.ContactPerson.ToUpper().Contains(search) ||
                x.Mobile.ToUpper().Contains(search) ||
                x.LedgerGroup.NameNormalized.Contains(search));
        }

        pageSize = Math.Clamp(pageSize, 1, 250);
        page = Math.Max(1, page);
        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        var rows = await query
            .OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new JobWorkerListItem
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                LedgerGroupName = x.LedgerGroup.Name,
                TallyLedgerName = x.TallyLedgerName,
                City = x.City,
                State = x.State,
                ContactPerson = x.ContactPerson,
                Mobile = x.Mobile,
                MaterialOutGodownName = x.DefaultMaterialOutDestinationGodown == null ? string.Empty : x.DefaultMaterialOutDestinationGodown.Name,
                MaterialInGodownName = x.DefaultMaterialInConsumptionGodown == null ? string.Empty : x.DefaultMaterialInConsumptionGodown.Name,
                ConcurrencyToken = x.ConcurrencyToken
            })
            .ToListAsync(cancellationToken);

        return new PagedReportResult<JobWorkerListItem>
        {
            Rows = rows,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<IReadOnlyList<JobWorkerListItem>> GetListAsync(
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Ledgers.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.IsJobWorker);

        var search = Normalize(searchText);
        if (search.Length > 0)
        {
            query = query.Where(x =>
                x.NameNormalized.Contains(search) ||
                x.Alias.ToUpper().Contains(search) ||
                x.TallyLedgerName.ToUpper().Contains(search) ||
                x.City.ToUpper().Contains(search) ||
                x.State.ToUpper().Contains(search) ||
                x.ContactPerson.ToUpper().Contains(search) ||
                x.Mobile.ToUpper().Contains(search) ||
                x.LedgerGroup.NameNormalized.Contains(search));
        }

        return await query
            .OrderBy(x => x.Name)
            .Select(x => new JobWorkerListItem
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                LedgerGroupName = x.LedgerGroup.Name,
                TallyLedgerName = x.TallyLedgerName,
                City = x.City,
                State = x.State,
                ContactPerson = x.ContactPerson,
                Mobile = x.Mobile,
                MaterialOutGodownName = x.DefaultMaterialOutDestinationGodown == null ? string.Empty : x.DefaultMaterialOutDestinationGodown.Name,
                MaterialInGodownName = x.DefaultMaterialInConsumptionGodown == null ? string.Empty : x.DefaultMaterialInConsumptionGodown.Name,
                ConcurrencyToken = x.ConcurrencyToken
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<JobWorkerMasterLookupData> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var groups = await db.LedgerGroups.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive)
            .OrderByDescending(x => x.RootClassification == "SundryCreditors")
            .ThenBy(x => x.Name)
            .Select(x => new LedgerGroupChoice
            {
                Id = x.Id,
                Name = x.Name,
                RootClassification = x.RootClassification,
                IsSystem = x.IsSystem
            })
            .ToListAsync(cancellationToken);
        return new JobWorkerMasterLookupData
        {
            LedgerGroups = groups,
            PreferredLedgerGroupId = groups.FirstOrDefault(x => x.RootClassification == "SundryCreditors")?.Id,
            Godowns = await db.Godowns.AsNoTracking()
                .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new JobWorkGodownLookup { Id = x.Id, Name = x.Name })
                .ToListAsync(cancellationToken)
        };
    }

    public async Task<JobWorkerEditModel?> GetForEditAsync(long id, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Ledgers.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.Id == id && x.IsJobWorker)
            .Select(x => new JobWorkerEditModel
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                LedgerGroupId = x.LedgerGroupId,
                TallyLedgerName = x.TallyLedgerName,
                AddressLine1 = x.AddressLine1,
                AddressLine2 = x.AddressLine2,
                City = x.City,
                State = x.State,
                ContactPerson = x.ContactPerson,
                Mobile = x.Mobile,
                Gstin = x.Gstin,
                DefaultMaterialOutDestinationGodownId = x.DefaultMaterialOutDestinationGodownId,
                DefaultMaterialInConsumptionGodownId = x.DefaultMaterialInConsumptionGodownId,
                ConcurrencyToken = x.ConcurrencyToken
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<OperationResult> SaveAsync(JobWorkerEditModel model, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ManageMasters, cancellationToken);
        var validation = Validate(model);
        if (validation is not null) return OperationResult.Fail(validation);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var normalizedName = Normalize(model.Name);
            var tallyName = string.IsNullOrWhiteSpace(model.TallyLedgerName) ? model.Name.Trim() : model.TallyLedgerName.Trim();
            var normalizedGstin = NormalizeCode(model.Gstin);

            var group = await db.LedgerGroups.SingleOrDefaultAsync(
                x => x.CompanyId == companyContext.CompanyId && x.Id == model.LedgerGroupId && x.IsActive,
                cancellationToken);
            if (group is null) return await RollbackFailAsync(transaction, "Select a valid active Ledger Group.", cancellationToken);

            if (await db.Ledgers.AnyAsync(x => x.CompanyId == companyContext.CompanyId &&
                    x.NameNormalized == normalizedName && x.Id != model.Id, cancellationToken))
                return await RollbackFailAsync(transaction, $"A ledger named '{model.Name.Trim()}' already exists.", cancellationToken);

            if (await db.Ledgers.AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.IsJobWorker &&
                    x.TallyLedgerName.ToUpper() == tallyName.ToUpper() && x.Id != model.Id, cancellationToken))
                return await RollbackFailAsync(transaction, $"Another Job Worker already maps to Tally Ledger Name '{tallyName}'.", cancellationToken);

            if (normalizedGstin.Length > 0 && await db.Ledgers.AnyAsync(x => x.CompanyId == companyContext.CompanyId &&
                    x.Gstin == normalizedGstin && x.Id != model.Id, cancellationToken))
                return await RollbackFailAsync(transaction, $"GSTIN '{normalizedGstin}' is already assigned to another ledger.", cancellationToken);

            var godownIds = new[] { model.DefaultMaterialOutDestinationGodownId, model.DefaultMaterialInConsumptionGodownId }
                .Where(x => x is > 0).Select(x => x!.Value).Distinct().ToList();
            var validGodownCount = await db.Godowns.CountAsync(
                x => godownIds.Contains(x.Id) && x.CompanyId == companyContext.CompanyId && x.IsActive,
                cancellationToken);
            if (validGodownCount != godownIds.Count)
                return await RollbackFailAsync(transaction, "Select valid active Godowns for the Job Worker defaults.", cancellationToken);

            var now = DateTimeOffset.UtcNow;
            Ledger entity;
            var action = "Create";
            if (model.Id == 0)
            {
                entity = new Ledger
                {
                    CompanyId = companyContext.CompanyId,
                    LedgerGroupId = group.Id,
                    Country = "India",
                    GstRegistrationType = normalizedGstin.Length == 0 ? "Unregistered" : "Regular",
                    OpeningBalanceType = "Dr",
                    IsJobWorker = true,
                    IsActive = true,
                    IsSystem = false,
                    CreatedAtUtc = now,
                    CreatedBy = companyContext.Actor
                };
                db.Ledgers.Add(entity);
            }
            else
            {
                entity = await db.Ledgers.SingleOrDefaultAsync(
                    x => x.CompanyId == companyContext.CompanyId && x.Id == model.Id && x.IsJobWorker,
                    cancellationToken) ?? throw new InvalidOperationException("The selected Job Worker no longer exists.");
                if (!string.Equals(entity.ConcurrencyToken, model.ConcurrencyToken, StringComparison.Ordinal))
                    return await RollbackFailAsync(transaction, "This Job Worker was changed by another user. Reload it and try again.", cancellationToken);
                if (entity.LedgerGroupId != group.Id &&
                    await db.Vouchers.AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.PartyLedgerId == entity.Id, cancellationToken))
                    return await RollbackFailAsync(transaction, "Ledger Group cannot be changed after the Job Worker has been used in a voucher.", cancellationToken);
                action = "Update";
            }

            var oldName = entity.Name;
            entity.LedgerGroupId = group.Id;
            entity.Name = model.Name.Trim();
            entity.NameNormalized = normalizedName;
            entity.Alias = Clean(model.Alias);
            if (string.IsNullOrWhiteSpace(entity.MailingName) || string.Equals(entity.MailingName, oldName, StringComparison.Ordinal))
                entity.MailingName = entity.Name;
            entity.TallyLedgerName = tallyName;
            entity.AddressLine1 = Clean(model.AddressLine1);
            entity.AddressLine2 = Clean(model.AddressLine2);
            entity.City = Clean(model.City);
            entity.State = Clean(model.State);
            entity.ContactPerson = Clean(model.ContactPerson);
            entity.Mobile = Clean(model.Mobile);
            entity.Gstin = normalizedGstin;
            entity.GstRegistrationType = normalizedGstin.Length == 0 ? "Unregistered" : entity.GstRegistrationType == "Unregistered" ? "Regular" : entity.GstRegistrationType;
            entity.DefaultMaterialOutDestinationGodownId = model.DefaultMaterialOutDestinationGodownId;
            entity.DefaultMaterialInConsumptionGodownId = model.DefaultMaterialInConsumptionGodownId;
            entity.IsJobWorker = true;
            entity.ModifiedAtUtc = now;
            entity.ModifiedBy = companyContext.Actor;
            entity.ConcurrencyToken = Guid.NewGuid().ToString("N");

            db.AuditLogs.Add(NewAudit(entity.Id == 0 ? null : entity.Id, action, true,
                $"{action} Job Worker '{entity.Name}' using Ledger identity."));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"{entity.Name} saved successfully.", entity.Id);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail($"The Job Worker could not be saved because it conflicts with existing data. {exception.GetBaseException().Message}");
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail(exception.GetBaseException().Message);
        }
    }

    public async Task<OperationResult> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ManageMasters, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var entity = await db.Ledgers.SingleOrDefaultAsync(
                x => x.CompanyId == companyContext.CompanyId && x.Id == id && x.IsJobWorker,
                cancellationToken);
            if (entity is null) return await RollbackFailAsync(transaction, "The selected Job Worker no longer exists.", cancellationToken);
            if (await db.Vouchers.AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.PartyLedgerId == id, cancellationToken))
                return await RollbackFailAsync(transaction, "This Job Worker is linked to JWO, Material Out or another voucher and cannot be deleted.", cancellationToken);

            db.Ledgers.Remove(entity);
            db.AuditLogs.Add(NewAudit(id, "Delete", true, $"Deleted completely unused Job Worker '{entity.Name}'."));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"{entity.Name} deleted.");
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail("This Job Worker is linked to production, accounting, mapping, history or another record and cannot be deleted.");
        }
    }

    private static string? Validate(JobWorkerEditModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name)) return "Job Worker Name is required.";
        if (model.Name.Trim().Length > 200) return "Job Worker Name cannot exceed 200 characters.";
        if (model.LedgerGroupId is null or <= 0) return "Ledger Group is required.";
        if (model.TallyLedgerName.Trim().Length > 200) return "Tally Ledger Name cannot exceed 200 characters.";
        var gstin = NormalizeCode(model.Gstin);
        if (gstin.Length > 0 && !GstinPattern.IsMatch(gstin)) return "GSTIN must be a valid 15-character Indian GST number format.";
        return null;
    }

    private static async Task<OperationResult> RollbackFailAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        string message,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return OperationResult.Fail(message);
    }

    private AuditLog NewAudit(long? id, string action, bool success, string description) => new()
    {
        CompanyId = companyContext.CompanyId,
        EntityType = "JobWorker",
        EntityId = id,
        Action = action,
        Success = success,
        Description = description,
        PerformedBy = companyContext.Actor,
        PerformedAtUtc = DateTimeOffset.UtcNow
    };

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    private static string NormalizeCode(string? value) => (value ?? string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
    private static string Clean(string? value) => value?.Trim() ?? string.Empty;
}
