using System.Data;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

/// <summary>
/// Purchase Return: a standalone outward-posting voucher. It does not link back to
/// any specific original Purchase - no FK, no quantity cap. An optional bill-number
/// reference uses the existing free-text Voucher.ReferenceNumber field for
/// record-keeping only. Structurally a near-copy of InventoryInwardRepository's
/// Purchase path, but single-kind and posting negative quantity/value.
/// </summary>
public sealed class PurchaseReturnRepository(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext,
    VoucherLifecycleService lifecycle,
    StockPostingService stockPosting,
    VoucherSequenceAllocator sequenceAllocator,
    VoucherAuditHistoryService voucherAuditHistory,
    InventoryPeriodControlService periodControl)
{
    private const string VoucherTypeCode = "PURCHASE_RETURN";

    public async Task<IReadOnlyList<PurchaseReturnListItem>> GetListAsync(
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Vouchers.AsNoTracking()
            .Include(x => x.VoucherType).ThenInclude(x => x.ParentVoucherType)
            .Where(x => x.CompanyId == companyContext.CompanyId &&
                x.FinancialYearId == companyContext.FinancialYearId &&
                (x.VoucherType.SystemTypeCode == VoucherTypeCode ||
                 (x.VoucherType.ParentVoucherType != null && x.VoucherType.ParentVoucherType.SystemTypeCode == VoucherTypeCode)));
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var normalized = Normalize(searchText);
            query = query.Where(x => x.VoucherNumberNormalized.Contains(normalized) ||
                x.ReferenceNumber.Contains(searchText));
        }
        return await query
            .OrderByDescending(x => x.VoucherDate).ThenByDescending(x => x.SequenceNumber)
            .Select(x => new PurchaseReturnListItem
            {
                VoucherId = x.Id,
                VoucherNumber = x.VoucherNumber,
                VoucherDate = x.VoucherDate,
                ReferenceNumber = x.ReferenceNumber,
                Status = x.Status,
                ConcurrencyToken = x.ConcurrencyToken
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<PurchaseVoucherEntryDefaults> GetEntryDefaultsAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await PurchaseLookupQueries.BuildEntryDefaultsAsync(
            db, companyContext.CompanyId, companyContext.FinancialYearId, VoucherTypeCode, cancellationToken);
    }

    public async Task<PurchaseLineLookupData> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await PurchaseLookupQueries.LoadAsync(db, companyContext.CompanyId, restrictSuppliersToSundryCreditors: false, cancellationToken);
    }

    public async Task<PurchaseItemVariantDetail?> GetItemVariantDetailAsync(
        long stockItemId,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await PurchaseLookupQueries.GetItemVariantDetailAsync(db, companyContext.CompanyId, stockItemId, cancellationToken);
    }

    public async Task<PurchaseReturnEditData?> GetForEditAsync(
        long voucherId,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var voucher = await db.Vouchers.AsNoTracking()
            .Include(x => x.VoucherType).ThenInclude(x => x.ParentVoucherType)
            .Include(x => x.PurchaseReturnLines)
            .SingleOrDefaultAsync(x => x.Id == voucherId &&
                x.CompanyId == companyContext.CompanyId &&
                x.FinancialYearId == companyContext.FinancialYearId,
                cancellationToken);
        if (voucher is null || !IsPurchaseReturnType(voucher.VoucherType)) return null;

        return new PurchaseReturnEditData
        {
            VoucherId = voucher.Id,
            VoucherTypeId = voucher.VoucherTypeId,
            VoucherTypeName = voucher.VoucherType.Name,
            VoucherNumber = voucher.VoucherNumber,
            VoucherDate = voucher.VoucherDate,
            ReferenceNumber = voucher.ReferenceNumber,
            SupplierLedgerId = voucher.PartyLedgerId,
            Narration = voucher.Narration,
            Status = voucher.Status,
            ConcurrencyToken = voucher.ConcurrencyToken,
            Lines = voucher.PurchaseReturnLines.OrderBy(x => x.LineNumber)
                .Select(x => new PurchaseReturnLineInput
                {
                    StockItemId = x.StockItemId,
                    StockItemVariantId = x.StockItemVariantId,
                    UqcId = x.UqcId,
                    GodownId = x.GodownId,
                    Quantity = x.Quantity,
                    Rate = x.Rate,
                    Amount = x.Amount
                }).ToList()
        };
    }

    public async Task<PurchaseReturnSaveResult> SaveAsync(
        PurchaseReturnSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.OperateVouchers, cancellationToken);
        ArgumentNullException.ThrowIfNull(request);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var actor = companyContext.Actor;
        try
        {
            var existing = request.VoucherId <= 0
                ? null
                : await db.Vouchers
                    .Include(x => x.VoucherType).ThenInclude(x => x.ParentVoucherType)
                    .Include(x => x.PurchaseReturnLines)
                    .SingleOrDefaultAsync(x => x.Id == request.VoucherId &&
                        x.CompanyId == companyContext.CompanyId &&
                        x.FinancialYearId == companyContext.FinancialYearId,
                        cancellationToken)
                    ?? throw new InvalidOperationException("The selected Purchase Return no longer exists.");

            if (existing is not null)
            {
                if (existing.Status == VoucherLifecycleService.CancelledStatus)
                    throw new InvalidOperationException("A cancelled Purchase Return cannot be altered.");
                if (!string.Equals(existing.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
                    throw new InvalidOperationException("This Purchase Return was changed by another operation. Reopen it and try again.");
            }

            var voucherType = existing?.VoucherType ?? await db.VoucherTypes
                .Include(x => x.ParentVoucherType)
                .SingleOrDefaultAsync(x => x.Id == request.VoucherTypeId &&
                    x.CompanyId == companyContext.CompanyId && x.IsActive,
                    cancellationToken)
                ?? throw new InvalidOperationException("Select a valid active Voucher Type.");
            if (!IsPurchaseReturnType(voucherType))
                throw new InvalidOperationException("Selected Voucher Type is not a Purchase Return type.");
            if (existing is not null && existing.VoucherTypeId != request.VoucherTypeId)
                throw new InvalidOperationException("Voucher Type cannot be changed during alteration.");

            if (existing is null)
                await periodControl.EnsurePostingDateIsOpenAsync(
                    db, companyContext.CompanyId, request.VoucherDate, "Purchase Return", cancellationToken);
            else
                await periodControl.EnsureVoucherMutationIsOpenAsync(
                    db, companyContext.CompanyId, existing.VoucherDate, request.VoucherDate,
                    $"Purchase Return {existing.VoucherNumber}", cancellationToken);

            var financialYear = await db.FinancialYears.AsNoTracking()
                .SingleAsync(x => x.Id == companyContext.FinancialYearId &&
                    x.CompanyId == companyContext.CompanyId, cancellationToken);
            if (request.VoucherDate < financialYear.StartDate || request.VoucherDate > financialYear.EndDate)
                throw new InvalidOperationException($"Voucher Date must be within financial year {financialYear.Name}.");

            if (request.SupplierLedgerId is long supplierId && supplierId > 0)
            {
                var valid = await db.Ledgers.AsNoTracking().AnyAsync(x =>
                    x.Id == supplierId && x.CompanyId == companyContext.CompanyId && x.IsActive,
                    cancellationToken);
                if (!valid)
                    throw new InvalidOperationException("The selected supplier ledger is unavailable or inactive.");
            }

            var lines = await ValidateAndNormalizeLinesAsync(db, request, cancellationToken);

            Voucher voucher;
            string voucherNumber;
            if (existing is null)
            {
                var sequence = await sequenceAllocator.ReserveAsync(db, voucherType, actor, now, cancellationToken);
                voucherNumber = voucherType.NumberingMode == "Manual"
                    ? request.VoucherNumber.Trim()
                    : FormatAutomaticNumber(voucherType, sequence);
                if (string.IsNullOrWhiteSpace(voucherNumber))
                    throw new InvalidOperationException("Voucher Number is required for manual numbering.");
                var normalized = Normalize(voucherNumber);
                if (await db.Vouchers.AsNoTracking().AnyAsync(x =>
                        x.CompanyId == companyContext.CompanyId &&
                        x.FinancialYearId == companyContext.FinancialYearId &&
                        x.VoucherTypeId == voucherType.Id &&
                        x.VoucherNumberNormalized == normalized,
                        cancellationToken))
                    throw new InvalidOperationException($"Voucher Number '{voucherNumber}' already exists for this Voucher Type and financial year.");

                voucher = new Voucher
                {
                    CompanyId = companyContext.CompanyId,
                    FinancialYearId = companyContext.FinancialYearId,
                    VoucherTypeId = voucherType.Id,
                    SequenceNumber = sequence,
                    VoucherNumber = voucherNumber,
                    VoucherNumberNormalized = normalized,
                    VoucherDate = request.VoucherDate,
                    ReferenceNumber = request.ReferenceNumber.Trim(),
                    PartyLedgerId = request.SupplierLedgerId,
                    Narration = request.Narration.Trim(),
                    Status = VoucherLifecycleService.OpenStatus,
                    CreatedAtUtc = now,
                    ModifiedAtUtc = now,
                    CreatedBy = actor,
                    ModifiedBy = actor,
                    ConcurrencyToken = Guid.NewGuid().ToString("N")
                };
                db.Vouchers.Add(voucher);
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                voucher = existing;
                voucherNumber = voucher.VoucherNumber;
                stockPosting.RemoveVoucherPostings(db, voucher.Id);
                db.PurchaseReturnLines.RemoveRange(voucher.PurchaseReturnLines);
                await db.SaveChangesAsync(cancellationToken);

                voucher.VoucherDate = request.VoucherDate;
                voucher.ReferenceNumber = request.ReferenceNumber.Trim();
                voucher.PartyLedgerId = request.SupplierLedgerId;
                voucher.Narration = request.Narration.Trim();
                voucher.ModifiedAtUtc = now;
                voucher.ModifiedBy = actor;
                voucher.ConcurrencyToken = Guid.NewGuid().ToString("N");
            }

            var lineNumber = 0;
            foreach (var input in lines)
            {
                var amount = input.Amount ?? decimal.Round(input.Quantity * input.Rate, 4, MidpointRounding.AwayFromZero);
                var line = new PurchaseReturnLine
                {
                    VoucherId = voucher.Id,
                    LineNumber = ++lineNumber,
                    StockItemId = input.StockItemId,
                    StockItemVariantId = input.StockItemVariantId,
                    UqcId = input.UqcId,
                    GodownId = input.GodownId,
                    Quantity = input.Quantity,
                    Rate = input.Rate,
                    Amount = amount
                };
                db.PurchaseReturnLines.Add(line);
                await db.SaveChangesAsync(cancellationToken);

                stockPosting.Post(db, new StockMovementDraft(
                    CompanyId: companyContext.CompanyId,
                    FinancialYearId: companyContext.FinancialYearId,
                    VoucherId: voucher.Id,
                    MovementDate: voucher.VoucherDate,
                    StockItemId: line.StockItemId,
                    UqcId: line.UqcId,
                    GodownId: line.GodownId,
                    QuantityChange: -line.Quantity,
                    Rate: line.Rate,
                    ValueChange: -line.Amount,
                    MovementKind: StockMovementSemantics.PurchaseReturnOutward,
                    StockItemVariantId: line.StockItemVariantId,
                    PurchaseReturnLineId: line.Id), actor, now);
            }

            db.AuditLogs.Add(VoucherLifecycleService.NewAudit(
                companyContext.CompanyId, voucher.Id,
                existing is null ? "Create" : "Update", true,
                $"{(existing is null ? "Created" : "Updated")} Purchase Return '{voucherNumber}' with {lines.Count} line(s).",
                actor, now));
            await db.SaveChangesAsync(cancellationToken);
            await voucherAuditHistory.RecordAsync(
                db, voucher.Id,
                existing is null ? VoucherAuditActions.Create : VoucherAuditActions.Update,
                existing is null ? "Initial Purchase Return save." : "Purchase Return altered.",
                actor, now, cancellationToken);
            await stockPosting.EnsureNegativeStockPolicyAsync(
                db, companyContext.CompanyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PurchaseReturnSaveResult(voucher.Id, voucherNumber);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<OperationResult> CancelAsync(
        long voucherId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.CancelOrDeleteVouchers, cancellationToken);
        var reasonValidation = VoucherLifecycleService.ValidateCancellationReason(reason);
        if (reasonValidation is not null) return OperationResult.Fail(reasonValidation);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var actor = companyContext.Actor;
        try
        {
            var voucher = await db.Vouchers
                .Include(x => x.VoucherType).ThenInclude(x => x.ParentVoucherType)
                .SingleOrDefaultAsync(x => x.Id == voucherId &&
                    x.CompanyId == companyContext.CompanyId &&
                    x.FinancialYearId == companyContext.FinancialYearId,
                    cancellationToken);
            if (voucher is null) return OperationResult.Fail("The selected Purchase Return no longer exists.");
            if (!IsPurchaseReturnType(voucher.VoucherType)) return OperationResult.Fail("The selected voucher is not a Purchase Return.");
            if (voucher.Status == VoucherLifecycleService.CancelledStatus)
                return OperationResult.Fail("This Purchase Return is already cancelled.");

            await periodControl.EnsurePostingDateIsOpenAsync(
                db, companyContext.CompanyId, voucher.VoucherDate,
                $"Purchase Return {voucher.VoucherNumber}", cancellationToken);
            var links = await lifecycle.GetActiveLinkDescriptionsAsync(
                db, companyContext.CompanyId, voucher.Id, VoucherLinkScope.Downstream, cancellationToken);
            if (links.Count > 0)
                return OperationResult.Fail(VoucherLifecycleService.BuildBlockedMessage(
                    "cancelled", $"Purchase Return {voucher.VoucherNumber}", links));

            await stockPosting.ReverseVoucherPostingsAsync(
                db, voucher.Id,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [StockMovementSemantics.PurchaseReturnOutward] = StockMovementSemantics.PurchaseReturnOutwardCancellation
                },
                StockMovementSemantics.PurchaseReturnOutwardCancellation, actor, now, cancellationToken);
            lifecycle.MarkCancelled(voucher, reason, actor, now);
            db.AuditLogs.Add(VoucherLifecycleService.NewAudit(
                companyContext.CompanyId, voucher.Id, "Cancel", true,
                $"Cancelled Purchase Return '{voucher.VoucherNumber}'. Reason: {voucher.CancellationReason}",
                actor, now));
            await db.SaveChangesAsync(cancellationToken);
            await voucherAuditHistory.RecordAsync(
                db, voucher.Id, VoucherAuditActions.Cancel,
                voucher.CancellationReason, actor, now, cancellationToken);
            await stockPosting.EnsureNegativeStockPolicyAsync(
                db, companyContext.CompanyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"Purchase Return {voucher.VoucherNumber} cancelled successfully.", voucher.Id);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail(VoucherErrorMessages.For(ex));
        }
    }

    public async Task<OperationResult> DeleteAsync(
        long voucherId,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.CancelOrDeleteVouchers, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var actor = companyContext.Actor;
        try
        {
            var voucher = await db.Vouchers
                .Include(x => x.VoucherType).ThenInclude(x => x.ParentVoucherType)
                .SingleOrDefaultAsync(x => x.Id == voucherId &&
                    x.CompanyId == companyContext.CompanyId &&
                    x.FinancialYearId == companyContext.FinancialYearId,
                    cancellationToken);
            if (voucher is null) return OperationResult.Fail("The selected Purchase Return no longer exists.");
            if (!IsPurchaseReturnType(voucher.VoucherType)) return OperationResult.Fail("The selected voucher is not a Purchase Return.");

            await periodControl.EnsurePostingDateIsOpenAsync(
                db, companyContext.CompanyId, voucher.VoucherDate,
                $"Purchase Return {voucher.VoucherNumber}", cancellationToken);
            var links = await lifecycle.GetActiveLinkDescriptionsAsync(
                db, companyContext.CompanyId, voucher.Id, VoucherLinkScope.Downstream, cancellationToken);
            if (links.Count > 0)
                return OperationResult.Fail(VoucherLifecycleService.BuildBlockedMessage(
                    "deleted", $"Purchase Return {voucher.VoucherNumber}", links));

            var number = voucher.VoucherNumber;
            await voucherAuditHistory.RecordAsync(
                db, voucher.Id, VoucherAuditActions.Delete,
                "Deleted Purchase Return and reversed its stock effect.",
                actor, now, cancellationToken);
            stockPosting.RemoveVoucherPostings(db, voucher.Id);
            db.PurchaseReturnLines.RemoveRange(db.PurchaseReturnLines.Where(x => x.VoucherId == voucher.Id));
            db.Vouchers.Remove(voucher);
            db.AuditLogs.Add(VoucherLifecycleService.NewAudit(
                companyContext.CompanyId, voucher.Id, "Delete", true,
                $"Deleted Purchase Return '{number}'.", actor, now));
            await db.SaveChangesAsync(cancellationToken);
            await stockPosting.EnsureNegativeStockPolicyAsync(
                db, companyContext.CompanyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"Purchase Return {number} deleted successfully.", voucherId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail(VoucherErrorMessages.For(ex));
        }
    }

    private async Task<IReadOnlyList<PurchaseReturnLineInput>> ValidateAndNormalizeLinesAsync(
        TexTrackDbContext db,
        PurchaseReturnSaveRequest request,
        CancellationToken cancellationToken)
    {
        var lines = request.Lines?
            .Where(x => x.Quantity != 0 || x.Rate != 0 || x.Amount != 0)
            .Select(x =>
            {
                var amount = x.Amount is decimal acceptedAmount
                    ? decimal.Round(acceptedAmount, 4, MidpointRounding.AwayFromZero)
                    : (decimal?)null;
                var rate = amount is not null && x.Quantity > 0
                    ? decimal.Round(amount.Value / x.Quantity, 4, MidpointRounding.AwayFromZero)
                    : decimal.Round(x.Rate, 4, MidpointRounding.AwayFromZero);
                return new PurchaseReturnLineInput
                {
                    StockItemId = x.StockItemId,
                    StockItemVariantId = x.StockItemVariantId,
                    UqcId = x.UqcId,
                    GodownId = x.GodownId,
                    Quantity = decimal.Round(x.Quantity, 4, MidpointRounding.AwayFromZero),
                    Rate = rate,
                    Amount = amount
                };
            }).ToList() ?? [];
        if (lines.Count == 0)
            throw new InvalidOperationException("Enter at least one Purchase Return line with Quantity greater than zero.");
        if (lines.Any(x => x.Quantity <= 0))
            throw new InvalidOperationException("Every return Quantity must be greater than zero.");
        if (lines.Any(x => x.Rate < 0))
            throw new InvalidOperationException("Return Rate cannot be negative.");
        if (lines.Any(x => x.Amount < 0))
            throw new InvalidOperationException("Return Value cannot be negative.");
        if (lines.GroupBy(x => new { x.StockItemId, x.StockItemVariantId, x.UqcId, x.GodownId }).Any(x => x.Count() > 1))
            throw new InvalidOperationException("The same item, variant, UQC and godown position cannot appear twice in one voucher.");

        var itemIds = lines.Select(x => x.StockItemId).Distinct().ToList();
        var variantIds = lines.Select(x => x.StockItemVariantId).Distinct().ToList();
        var uqcIds = lines.Select(x => x.UqcId).Distinct().ToList();
        var godownIds = lines.Select(x => x.GodownId).Distinct().ToList();
        var items = await db.StockItems.AsNoTracking()
            .Where(x => itemIds.Contains(x.Id) && x.CompanyId == companyContext.CompanyId && x.IsActive)
            .Select(x => new { x.Id, x.UqcId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var variants = await db.StockItemVariants.AsNoTracking()
            .Where(x => variantIds.Contains(x.Id) && x.CompanyId == companyContext.CompanyId && x.IsActive)
            .Select(x => new { x.Id, x.StockItemId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var validUqcSet = (await db.Uqcs.AsNoTracking()
            .Where(x => uqcIds.Contains(x.Id) && x.CompanyId == companyContext.CompanyId && x.IsActive)
            .Select(x => x.Id).ToListAsync(cancellationToken)).ToHashSet();
        var validGodownSet = (await db.Godowns.AsNoTracking()
            .Where(x => godownIds.Contains(x.Id) && x.CompanyId == companyContext.CompanyId && x.IsActive)
            .Select(x => x.Id).ToListAsync(cancellationToken)).ToHashSet();

        foreach (var line in lines)
        {
            if (!items.TryGetValue(line.StockItemId, out var item))
                throw new InvalidOperationException("A selected Stock Item is unavailable, inactive or belongs to another company.");
            if (!variants.TryGetValue(line.StockItemVariantId, out var variant) || variant.StockItemId != line.StockItemId)
                throw new InvalidOperationException("A selected Stock Item Variant is unavailable or does not belong to its Stock Item.");
            if (!validUqcSet.Contains(line.UqcId) || item.UqcId != line.UqcId)
                throw new InvalidOperationException("Each return line UQC must match its active Stock Item UQC.");
            if (!validGodownSet.Contains(line.GodownId))
                throw new InvalidOperationException("A selected Godown is unavailable, inactive or belongs to another company.");
        }
        return lines;
    }

    private static bool IsPurchaseReturnType(VoucherType type) =>
        string.Equals(EffectiveSystemType(type), VoucherTypeCode, StringComparison.OrdinalIgnoreCase);

    private static string EffectiveSystemType(VoucherType type) =>
        string.IsNullOrWhiteSpace(type.SystemTypeCode) || type.SystemTypeCode.StartsWith("CUSTOM_", StringComparison.OrdinalIgnoreCase)
            ? type.ParentVoucherType?.SystemTypeCode ?? type.SystemTypeCode
            : type.SystemTypeCode;

    private static string FormatAutomaticNumber(VoucherType type, int sequence)
    {
        var numeric = type.NumberWidth > 0 ? sequence.ToString($"D{type.NumberWidth}") : sequence.ToString();
        return type.NumberingMode == "AutoPrefixSuffix" ? $"{type.Prefix}{numeric}{type.Suffix}" : numeric;
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
