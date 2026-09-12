using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed class LedgerRepository(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext)
{
    private static readonly Regex GstinPattern = new(
        "^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PanPattern = new(
        "^[A-Z]{5}[0-9]{4}[A-Z]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<LedgerListItem>> GetListAsync(
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Ledgers
            .AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId);

        var search = NormalizeSearch(searchText);
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                x.NameNormalized.Contains(search) ||
                x.Alias.ToUpper().Contains(search) ||
                x.LedgerGroup.NameNormalized.Contains(search) ||
                x.Gstin.Contains(search));
        }

        return await query
            .OrderBy(x => x.Name)
            .Select(x => new LedgerListItem
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                LedgerGroupId = x.LedgerGroupId,
                LedgerGroupName = x.LedgerGroup.Name,
                RootClassification = x.LedgerGroup.RootClassification,
                GstRegistrationType = x.GstRegistrationType,
                Gstin = x.Gstin,
                OpeningBalance = x.OpeningBalance,
                OpeningBalanceType = x.OpeningBalanceType,
                MaintainBillWise = x.MaintainBillWise,
                IsSystem = x.IsSystem,
                IsActive = x.IsActive,
                ConcurrencyToken = x.ConcurrencyToken
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedReportResult<LedgerListItem>> GetPageAsync(
        string? searchText,
        int page,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Ledgers.AsNoTracking().Where(x => x.CompanyId == companyContext.CompanyId);
        var search = NormalizeSearch(searchText);
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x => x.NameNormalized.Contains(search) ||
                                     x.Alias.ToUpper().Contains(search) ||
                                     x.LedgerGroup.NameNormalized.Contains(search) ||
                                     x.Gstin.Contains(search));
        }

        pageSize = Math.Clamp(pageSize, 25, 200);
        var total = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new LedgerListItem
            {
                Id = x.Id, Name = x.Name, Alias = x.Alias, LedgerGroupId = x.LedgerGroupId,
                LedgerGroupName = x.LedgerGroup.Name, RootClassification = x.LedgerGroup.RootClassification,
                GstRegistrationType = x.GstRegistrationType, Gstin = x.Gstin,
                OpeningBalance = x.OpeningBalance, OpeningBalanceType = x.OpeningBalanceType,
                MaintainBillWise = x.MaintainBillWise, IsSystem = x.IsSystem,
                IsActive = x.IsActive, ConcurrencyToken = x.ConcurrencyToken
            }).ToListAsync(cancellationToken);
        return new PagedReportResult<LedgerListItem>
        {
            Rows = rows, TotalCount = total, Page = page, PageSize = pageSize
        };
    }

    public async Task<IReadOnlyList<LedgerGroupChoice>> GetGroupChoicesAsync(
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.LedgerGroups
            .AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive)
            .OrderByDescending(x => x.IsSystem)
            .ThenBy(x => x.Name)
            .Select(x => new LedgerGroupChoice
            {
                Id = x.Id,
                Name = x.Name,
                RootClassification = x.RootClassification,
                IsSystem = x.IsSystem
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<LedgerEditModel?> GetForEditAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Ledgers
            .AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.Id == id)
            .Select(x => new LedgerEditModel
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                LedgerGroupId = x.LedgerGroupId,
                MailingName = x.MailingName,
                AddressLine1 = x.AddressLine1,
                AddressLine2 = x.AddressLine2,
                City = x.City,
                State = x.State,
                Country = x.Country,
                PinCode = x.PinCode,
                ContactPerson = x.ContactPerson,
                Phone = x.Phone,
                Mobile = x.Mobile,
                Email = x.Email,
                GstRegistrationType = x.GstRegistrationType,
                Gstin = x.Gstin,
                Pan = x.Pan,
                MaintainBillWise = x.MaintainBillWise,
                CreditPeriodDays = x.CreditPeriodDays,
                CreditLimit = x.CreditLimit,
                OpeningBalance = x.OpeningBalance,
                OpeningBalanceType = x.OpeningBalanceType,
                BankAccountNumber = x.BankAccountNumber,
                BankIfsc = x.BankIfsc,
                BankName = x.BankName,
                BankBranch = x.BankBranch,
                IsSystem = x.IsSystem,
                ConcurrencyToken = x.ConcurrencyToken
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<OperationResult> SaveAsync(
        LedgerEditModel model,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ManageMasters, cancellationToken);
        var validation = Validate(model);
        if (validation is not null)
        {
            return await RecordBlockedAsync(model.Id, validation, cancellationToken);
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var ledgerGroupId = model.LedgerGroupId!.Value;
        var normalizedName = NormalizeName(model.Name);
        var normalizedGstin = NormalizeCode(model.Gstin);
        var normalizedPan = NormalizeCode(model.Pan);
        var normalizedIfsc = NormalizeCode(model.BankIfsc);

        var group = await db.LedgerGroups.SingleOrDefaultAsync(
            x => x.CompanyId == companyContext.CompanyId &&
                 x.Id == ledgerGroupId &&
                 x.IsActive,
            cancellationToken);

        if (group is null)
        {
            return await FailWithAuditAsync(db, model.Id, "Select a valid Ledger Group.", cancellationToken);
        }

        var duplicateName = await db.Ledgers.AnyAsync(
            x => x.CompanyId == companyContext.CompanyId &&
                 x.NameNormalized == normalizedName &&
                 x.Id != model.Id,
            cancellationToken);

        if (duplicateName)
        {
            return await FailWithAuditAsync(db, model.Id, $"A ledger named '{model.Name.Trim()}' already exists.", cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(normalizedGstin))
        {
            var duplicateGstin = await db.Ledgers.AnyAsync(
                x => x.CompanyId == companyContext.CompanyId &&
                     x.Gstin == normalizedGstin &&
                     x.Id != model.Id,
                cancellationToken);

            if (duplicateGstin)
            {
                return await FailWithAuditAsync(db, model.Id, $"GSTIN '{normalizedGstin}' is already assigned to another ledger.", cancellationToken);
            }
        }

        var now = DateTimeOffset.UtcNow;
        Ledger entity;
        var action = "Create";

        if (model.Id == 0)
        {
            entity = new Ledger
            {
                CompanyId = companyContext.CompanyId,
                CreatedAtUtc = now,
                CreatedBy = companyContext.Actor,
                IsActive = true,
                IsSystem = false
            };
            db.Ledgers.Add(entity);
        }
        else
        {
            entity = await db.Ledgers.SingleOrDefaultAsync(
                x => x.CompanyId == companyContext.CompanyId && x.Id == model.Id,
                cancellationToken) ?? throw new InvalidOperationException("The selected ledger no longer exists.");

            if (!string.Equals(entity.ConcurrencyToken, model.ConcurrencyToken, StringComparison.Ordinal))
            {
                return await FailWithAuditAsync(db, model.Id, "This ledger was changed by another user. Reload it and try again.", cancellationToken);
            }

            if (entity.IsSystem && entity.LedgerGroupId != ledgerGroupId)
            {
                return await FailWithAuditAsync(db, model.Id, "The group of a permanent system ledger cannot be changed.", cancellationToken);
            }

            action = "Update";
        }

        entity.LedgerGroupId = ledgerGroupId;
        entity.Name = model.Name.Trim();
        entity.NameNormalized = normalizedName;
        entity.Alias = Clean(model.Alias);
        entity.MailingName = string.IsNullOrWhiteSpace(model.MailingName) ? entity.Name : model.MailingName.Trim();
        entity.AddressLine1 = Clean(model.AddressLine1);
        entity.AddressLine2 = Clean(model.AddressLine2);
        entity.City = Clean(model.City);
        entity.State = Clean(model.State);
        entity.Country = string.IsNullOrWhiteSpace(model.Country) ? "India" : model.Country.Trim();
        entity.PinCode = Clean(model.PinCode);
        entity.ContactPerson = Clean(model.ContactPerson);
        entity.Phone = Clean(model.Phone);
        entity.Mobile = Clean(model.Mobile);
        entity.Email = Clean(model.Email);
        entity.GstRegistrationType = model.GstRegistrationType;
        entity.Gstin = normalizedGstin;
        entity.Pan = normalizedPan;
        entity.MaintainBillWise = model.MaintainBillWise;
        entity.CreditPeriodDays = model.CreditPeriodDays;
        entity.CreditLimit = model.CreditLimit;
        entity.OpeningBalance = model.OpeningBalance;
        entity.OpeningBalanceType = model.OpeningBalance == 0 ? "Dr" : model.OpeningBalanceType;
        entity.BankAccountNumber = Clean(model.BankAccountNumber);
        entity.BankIfsc = normalizedIfsc;
        entity.BankName = Clean(model.BankName);
        entity.BankBranch = Clean(model.BankBranch);
        entity.ModifiedAtUtc = now;
        entity.ModifiedBy = companyContext.Actor;
        entity.ConcurrencyToken = Guid.NewGuid().ToString("N");

        db.AuditLogs.Add(NewAudit(
            entity.Id == 0 ? null : entity.Id,
            action,
            true,
            $"{action} ledger '{entity.Name}' under group '{group.Name}'."));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return OperationResult.Ok($"{entity.Name} saved successfully.", entity.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult.Fail("This ledger was changed by another user. Reload it and try again.");
        }
        catch (DbUpdateException exception)
        {
            return OperationResult.Fail($"The ledger could not be saved because it conflicts with existing data. {exception.GetBaseException().Message}");
        }
    }

    public async Task<OperationResult> DeleteAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ManageMasters, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Ledgers.SingleOrDefaultAsync(
            x => x.CompanyId == companyContext.CompanyId && x.Id == id,
            cancellationToken);

        if (entity is null)
        {
            return OperationResult.Fail("The selected ledger no longer exists.");
        }

        if (entity.IsSystem)
        {
            return await FailWithAuditAsync(db, id, "Permanent system ledgers cannot be deleted.", cancellationToken);
        }

        db.Ledgers.Remove(entity);
        db.AuditLogs.Add(NewAudit(id, "Delete", true, $"Deleted unused ledger '{entity.Name}'."));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return OperationResult.Ok($"{entity.Name} deleted.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Fail("This ledger is linked to vouchers, opening records, mappings or other transactions and cannot be deleted.");
        }
    }

    private static string? Validate(LedgerEditModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            return "Ledger Name is required.";
        }

        if (model.Name.Trim().Length > 200)
        {
            return "Ledger Name cannot exceed 200 characters.";
        }

        if (model.LedgerGroupId is null or <= 0)
        {
            return "Ledger Group is required.";
        }

        if (model.OpeningBalance < 0)
        {
            return "Opening Balance cannot be negative. Enter the amount and select Dr or Cr separately.";
        }

        if (model.OpeningBalanceType is not ("Dr" or "Cr"))
        {
            return "Opening Balance Type must be Dr or Cr.";
        }

        if (model.CreditPeriodDays is < 0 or > 3650)
        {
            return "Credit Period must be between 0 and 3650 days.";
        }

        if (model.CreditLimit < 0)
        {
            return "Credit Limit cannot be negative.";
        }

        var registrationRequiresGstin = model.GstRegistrationType is "Regular" or "Composition" or "SEZ";
        var gstin = NormalizeCode(model.Gstin);
        if (registrationRequiresGstin && string.IsNullOrWhiteSpace(gstin))
        {
            return "GSTIN is required for the selected GST Registration Type.";
        }

        if (!string.IsNullOrWhiteSpace(gstin) && !GstinPattern.IsMatch(gstin))
        {
            return "GSTIN must be a valid 15-character Indian GST number format.";
        }

        var pan = NormalizeCode(model.Pan);
        if (!string.IsNullOrWhiteSpace(pan) && !PanPattern.IsMatch(pan))
        {
            return "PAN must use the format ABCDE1234F.";
        }

        if (!string.IsNullOrWhiteSpace(model.Email))
        {
            try
            {
                _ = new MailAddress(model.Email.Trim());
            }
            catch
            {
                return "Enter a valid email address.";
            }
        }

        return null;
    }

    private async Task<OperationResult> RecordBlockedAsync(
        long entityId,
        string message,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await FailWithAuditAsync(db, entityId, message, cancellationToken);
    }

    private async Task<OperationResult> FailWithAuditAsync(
        TexTrackDbContext db,
        long entityId,
        string message,
        CancellationToken cancellationToken)
    {
        db.AuditLogs.Add(NewAudit(entityId == 0 ? null : entityId, "ValidationBlocked", false, message));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Preserve the original validation error if audit writing fails.
        }

        return OperationResult.Fail(message);
    }

    private AuditLog NewAudit(long? entityId, string action, bool success, string description) => new()
    {
        CompanyId = companyContext.CompanyId,
        EntityType = "Ledger",
        EntityId = entityId,
        Action = action,
        Success = success,
        Description = description,
        PerformedBy = companyContext.Actor,
        PerformedAtUtc = DateTimeOffset.UtcNow
    };

    private static string NormalizeName(string value) => value.Trim().ToUpperInvariant();
    private static string NormalizeSearch(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
    private static string NormalizeCode(string? value) => (value ?? string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
    private static string Clean(string? value) => value?.Trim() ?? string.Empty;
}
