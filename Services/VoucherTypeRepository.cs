using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed class VoucherTypeRepository(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext)
{
    public async Task<IReadOnlyList<VoucherTypeListItem>> GetListAsync(
        string searchText = "",
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var search = searchText.Trim().ToUpperInvariant();
        return await db.VoucherTypes
            .AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId)
            .Where(x => search == string.Empty ||
                        x.Name.ToUpper().Contains(search) ||
                        x.SystemTypeCode.ToUpper().Contains(search) ||
                        x.TallyVoucherTypeName.ToUpper().Contains(search) ||
                        (x.ParentVoucherType != null && x.ParentVoucherType.Name.ToUpper().Contains(search)))
            .OrderBy(x => x.ParentVoucherTypeId != null)
            .ThenBy(x => x.ParentVoucherTypeId)
            .ThenBy(x => x.Name)
            .Select(x => new VoucherTypeListItem
            {
                Id = x.Id,
                Name = x.Name,
                ParentVoucherTypeId = x.ParentVoucherTypeId,
                ParentName = x.ParentVoucherType == null ? string.Empty : x.ParentVoucherType.Name,
                SystemTypeCode = x.SystemTypeCode,
                Nature = x.Nature,
                PostingMode = x.PostingMode,
                Abbreviation = x.Abbreviation,
                AllowManualNumbering = x.AllowManualNumbering,
                NumberingMode = x.NumberingMode,
                Prefix = x.Prefix,
                Suffix = x.Suffix,
                StartingNumber = x.StartingNumber,
                NumberWidth = x.NumberWidth,
                ResetPeriod = x.ResetPeriod,
                TallyVoucherTypeName = x.TallyVoucherTypeName,
                IsSystem = x.IsSystem,
                ConcurrencyToken = x.ConcurrencyToken
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VoucherTypeParentLookup>> GetParentsAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.VoucherTypes
            .AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.IsSystem && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new VoucherTypeParentLookup
            {
                Id = x.Id,
                Name = x.Name,
                Nature = x.Nature,
                PostingMode = x.PostingMode
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<VoucherTypeEditModel?> GetForEditAsync(long id, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.VoucherTypes
            .AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.Id == id)
            .Select(x => new VoucherTypeEditModel
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                ParentVoucherTypeId = x.ParentVoucherTypeId,
                Abbreviation = x.Abbreviation,
                AllowManualNumbering = x.AllowManualNumbering,
                NumberingMode = x.NumberingMode,
                Prefix = x.Prefix,
                Suffix = x.Suffix,
                StartingNumber = x.StartingNumber,
                NumberWidth = x.NumberWidth,
                ResetPeriod = x.ResetPeriod,
                IsSystem = x.IsSystem,
                TallyVoucherTypeName = x.TallyVoucherTypeName,
                ConcurrencyToken = x.ConcurrencyToken
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<OperationResult> SaveAsync(VoucherTypeEditModel model, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ManageMasters, cancellationToken);
        Trim(model);
        var validation = ValidateCommon(model);
        if (validation is not null) return OperationResult.Fail(validation);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            VoucherType? existing = null;
            if (model.Id != 0)
            {
                existing = await db.VoucherTypes.SingleOrDefaultAsync(
                    x => x.CompanyId == companyContext.CompanyId && x.Id == model.Id,
                    cancellationToken);
                if (existing is null) return OperationResult.Fail("The selected Voucher Type no longer exists.");
                if (!string.Equals(existing.ConcurrencyToken, model.ConcurrencyToken, StringComparison.Ordinal))
                    return OperationResult.Fail("This Voucher Type was changed by another user. Reload and try again.");
            }

            if (existing?.IsSystem == true)
            {
                ApplyNumbering(existing, model);
                existing.TallyVoucherTypeName = string.IsNullOrWhiteSpace(model.TallyVoucherTypeName)
                    ? existing.Name
                    : model.TallyVoucherTypeName;
                existing.ModifiedAtUtc = DateTimeOffset.UtcNow;
                existing.ModifiedBy = companyContext.Actor;
                existing.ConcurrencyToken = Guid.NewGuid().ToString("N");
                db.AuditLogs.Add(NewAudit(existing.Id, "UpdateConfiguration", true,
                    $"Updated numbering and Tally mapping for permanent Voucher Type '{existing.Name}'."));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return OperationResult.Ok($"Configuration for '{existing.Name}' saved successfully.", existing.Id);
            }

            if (model.IsSystem)
                return OperationResult.Fail("Permanent Voucher Types can only be created by the approved system seed.");
            if (model.ParentVoucherTypeId is null or <= 0)
                return OperationResult.Fail("Select a permanent Parent Voucher Type.");

            var parent = await db.VoucherTypes.SingleOrDefaultAsync(
                x => x.CompanyId == companyContext.CompanyId &&
                     x.Id == model.ParentVoucherTypeId.Value &&
                     x.IsSystem && x.IsActive,
                cancellationToken);
            if (parent is null) return OperationResult.Fail("The selected Parent Voucher Type is unavailable.");

            var normalized = Normalize(model.Name);
            if (await db.VoucherTypes.AnyAsync(
                    x => x.CompanyId == companyContext.CompanyId &&
                         x.NameNormalized == normalized && x.Id != model.Id,
                    cancellationToken))
                return OperationResult.Fail($"Voucher Type '{model.Name}' already exists.");

            var isNew = existing is null;
            var entity = existing ?? new VoucherType
            {
                CompanyId = companyContext.CompanyId,
                SystemTypeCode = $"CUSTOM_{Guid.NewGuid():N}",
                IsSystem = false,
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                CreatedBy = companyContext.Actor
            };
            if (isNew) db.VoucherTypes.Add(entity);

            if (!isNew && entity.ParentVoucherTypeId != parent.Id &&
                await HasHistoricalUseAsync(db, entity.Id, cancellationToken))
                return OperationResult.Fail("The Parent Voucher Type cannot be changed after this Voucher Type has been used.");

            entity.Name = model.Name;
            entity.NameNormalized = normalized;
            entity.Alias = model.Alias;
            entity.ParentVoucherTypeId = parent.Id;
            entity.Nature = parent.Nature;
            entity.PostingMode = parent.PostingMode;
            entity.Abbreviation = model.Abbreviation;
            ApplyNumbering(entity, model);
            entity.TallyVoucherTypeName = string.IsNullOrWhiteSpace(model.TallyVoucherTypeName)
                ? model.Name
                : model.TallyVoucherTypeName;
            entity.ModifiedAtUtc = DateTimeOffset.UtcNow;
            entity.ModifiedBy = companyContext.Actor;
            entity.ConcurrencyToken = Guid.NewGuid().ToString("N");

            db.AuditLogs.Add(NewAudit(entity.Id == 0 ? null : entity.Id, isNew ? "Create" : "Update", true,
                $"{(isNew ? "Created" : "Updated")} Voucher Type '{entity.Name}' under '{parent.Name}'."));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"Voucher Type '{entity.Name}' saved successfully.", entity.Id);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail("The Voucher Type could not be saved because it conflicts with existing data.");
        }
    }

    public async Task<OperationResult> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ManageMasters, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var entity = await db.VoucherTypes.SingleOrDefaultAsync(
            x => x.CompanyId == companyContext.CompanyId && x.Id == id,
            cancellationToken);
        if (entity is null) return OperationResult.Fail("The selected Voucher Type no longer exists.");
        if (entity.IsSystem) return OperationResult.Fail("Permanent system Voucher Types cannot be deleted.");

        string? dependencyMessage = null;
        if (await db.Vouchers.AnyAsync(
                x => x.CompanyId == companyContext.CompanyId && x.VoucherTypeId == id,
                cancellationToken))
            dependencyMessage = "This Voucher Type has been used in vouchers and cannot be deleted.";
        else if (await db.VoucherTypes.AnyAsync(
                     x => x.CompanyId == companyContext.CompanyId && x.ParentVoucherTypeId == id,
                     cancellationToken))
            dependencyMessage = "Delete the child Voucher Types first.";
        else if (await GetLastSequenceAsync(db, id, cancellationToken) > 0)
            dependencyMessage = "This Voucher Type has numbering history and cannot be deleted.";

        if (dependencyMessage is not null)
        {
            db.AuditLogs.Add(NewAudit(id, "ValidationBlocked", false, dependencyMessage));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Fail(dependencyMessage);
        }

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM voucher_sequences WHERE company_id = {companyContext.CompanyId} AND voucher_type_id = {id}",
                cancellationToken);
            db.VoucherTypes.Remove(entity);
            db.AuditLogs.Add(NewAudit(id, "Delete", true, $"Deleted unused Voucher Type '{entity.Name}'."));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"Voucher Type '{entity.Name}' deleted.");
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail("This Voucher Type is linked to historical records and cannot be deleted.");
        }
    }

    private async Task<bool> HasHistoricalUseAsync(
        TexTrackDbContext db,
        long voucherTypeId,
        CancellationToken cancellationToken) =>
        await db.Vouchers.AnyAsync(
            x => x.CompanyId == companyContext.CompanyId && x.VoucherTypeId == voucherTypeId,
            cancellationToken) ||
        await GetLastSequenceAsync(db, voucherTypeId, cancellationToken) > 0;

    private async Task<int> GetLastSequenceAsync(
        TexTrackDbContext db,
        long voucherTypeId,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT COALESCE(MAX(last_number), 0) FROM voucher_sequences WHERE company_id = @company AND voucher_type_id = @type";
        var companyParameter = command.CreateParameter();
        companyParameter.ParameterName = "company";
        companyParameter.Value = companyContext.CompanyId;
        command.Parameters.Add(companyParameter);
        var typeParameter = command.CreateParameter();
        typeParameter.ParameterName = "type";
        typeParameter.Value = voucherTypeId;
        command.Parameters.Add(typeParameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private static string? ValidateCommon(VoucherTypeEditModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name)) return "Voucher Type Name is required.";
        if (model.Name.Length > 200) return "Voucher Type Name cannot exceed 200 characters.";
        if (model.Alias.Length > 200) return "Alias cannot exceed 200 characters.";
        if (model.TallyVoucherTypeName.Length > 200) return "Tally Voucher Type Name cannot exceed 200 characters.";
        if (model.NumberingMode is not ("Auto" or "AutoPrefixSuffix" or "Manual")) return "Select a valid Numbering Method.";
        if (model.StartingNumber <= 0) return "Starting Number must be greater than zero.";
        if (model.NumberWidth < 0 || model.NumberWidth > 12) return "Number Width must be between 0 and 12.";
        if (model.ResetPeriod is not ("Never" or "FinancialYear")) return "Select a valid sequence reset rule.";
        if (model.Abbreviation.Length > 20) return "Abbreviation cannot exceed 20 characters.";
        if (model.Prefix.Length > 30) return "Prefix cannot exceed 30 characters.";
        if (model.Suffix.Length > 30) return "Suffix cannot exceed 30 characters.";
        return null;
    }

    private static void Trim(VoucherTypeEditModel model)
    {
        model.Name = model.Name.Trim();
        model.Alias = model.Alias.Trim();
        model.Abbreviation = model.Abbreviation.Trim();
        model.Prefix = model.Prefix.Trim();
        model.Suffix = model.Suffix.Trim();
        model.NumberingMode = model.NumberingMode.Trim();
        model.ResetPeriod = model.ResetPeriod.Trim();
        model.TallyVoucherTypeName = model.TallyVoucherTypeName.Trim();
    }

    private static void ApplyNumbering(VoucherType entity, VoucherTypeEditModel model)
    {
        entity.NumberingMode = model.NumberingMode;
        entity.AllowManualNumbering = model.NumberingMode == "Manual";
        entity.Prefix = model.NumberingMode == "AutoPrefixSuffix" ? model.Prefix : string.Empty;
        entity.Suffix = model.NumberingMode == "AutoPrefixSuffix" ? model.Suffix : string.Empty;
        entity.StartingNumber = model.StartingNumber;
        entity.NumberWidth = model.NumberWidth;
        entity.ResetPeriod = model.ResetPeriod;
    }

    private AuditLog NewAudit(long? entityId, string action, bool success, string description) => new()
    {
        CompanyId = companyContext.CompanyId,
        EntityType = "VoucherType",
        EntityId = entityId,
        Action = action,
        Success = success,
        Description = description,
        PerformedBy = companyContext.Actor,
        PerformedAtUtc = DateTimeOffset.UtcNow
    };

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
