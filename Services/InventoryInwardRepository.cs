using System.Data;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

/// <summary>
/// Shared native posting contract for purchased and opening inventory. Purchase
/// currently establishes stock quantity/value only; it deliberately creates no
/// accounting, tax or supplier-payable entries.
/// </summary>
public sealed class InventoryInwardRepository(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext,
    VoucherLifecycleService lifecycle,
    StockPostingService stockPosting,
    VoucherSequenceAllocator sequenceAllocator,
    VoucherAuditHistoryService voucherAuditHistory,
    InventoryPeriodControlService periodControl)
{
    private const string PurchaseTypeCode = "PURCHASE";
    private const string OpeningStockTypeCode = "OPENING_STOCK";

    /// <summary>
    /// Purchase-only list (Opening Stock is entered from the Stock Item master
    /// page, not its own voucher list) - filters to the Purchase system type
    /// specifically rather than every InventoryInwardVoucherKind.
    /// </summary>
    public async Task<IReadOnlyList<PurchaseListItem>> GetListAsync(
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Vouchers.AsNoTracking()
            .Include(x => x.VoucherType).ThenInclude(x => x.ParentVoucherType)
            .Where(x => x.CompanyId == companyContext.CompanyId &&
                x.FinancialYearId == companyContext.FinancialYearId &&
                (x.VoucherType.SystemTypeCode == PurchaseTypeCode ||
                 (x.VoucherType.ParentVoucherType != null && x.VoucherType.ParentVoucherType.SystemTypeCode == PurchaseTypeCode)));
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var normalized = Normalize(searchText);
            query = query.Where(x => x.VoucherNumberNormalized.Contains(normalized) || x.ReferenceNumber.Contains(searchText));
        }
        return await query
            .OrderByDescending(x => x.VoucherDate).ThenByDescending(x => x.SequenceNumber)
            .Select(x => new PurchaseListItem
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

    /// <summary>
    /// Existing (non-cancelled) Purchases for one supplier - Purchase Return's
    /// optional reference picker. No "pending" filtering: every Purchase for
    /// this supplier is listed regardless of what's already been returned
    /// against it, since Purchase Return is never capped.
    /// </summary>
    public async Task<IReadOnlyList<PurchaseOutstandingSummary>> GetOutstandingPurchasesForSupplierAsync(
        long supplierLedgerId,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Vouchers.AsNoTracking()
            .Include(x => x.VoucherType).ThenInclude(x => x.ParentVoucherType)
            .Where(x => x.CompanyId == companyContext.CompanyId &&
                x.FinancialYearId == companyContext.FinancialYearId &&
                x.PartyLedgerId == supplierLedgerId &&
                x.Status != VoucherLifecycleService.CancelledStatus &&
                (x.VoucherType.SystemTypeCode == PurchaseTypeCode ||
                 (x.VoucherType.ParentVoucherType != null && x.VoucherType.ParentVoucherType.SystemTypeCode == PurchaseTypeCode)))
            .OrderByDescending(x => x.VoucherDate).ThenByDescending(x => x.SequenceNumber)
            .Select(x => new PurchaseOutstandingSummary
            {
                PurchaseVoucherId = x.Id,
                PurchaseVoucherNumber = x.VoucherNumber,
                VoucherDate = x.VoucherDate,
                LineCount = x.InventoryInwardLines.Count
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Full hydration of one Purchase's lines, for Purchase Return's optional
    /// reference-and-prefill flow (mirrors Purchase's own PO-reference
    /// prefill mechanically, but the result is never used to cap anything).
    /// </summary>
    public async Task<PurchaseHydrationResult?> GetLinesForPurchaseAsync(
        long purchaseVoucherId,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var voucher = await db.Vouchers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == purchaseVoucherId &&
                x.CompanyId == companyContext.CompanyId &&
                x.FinancialYearId == companyContext.FinancialYearId, cancellationToken);
        if (voucher is null) return null;

        var lines = await db.InventoryInwardLines.AsNoTracking()
            .Where(x => x.VoucherId == purchaseVoucherId)
            .Select(x => new PurchaseHydrationLine
            {
                StockItemId = x.StockItemId,
                StockItemName = x.StockItem.Name,
                StockItemVariantId = x.StockItemVariantId,
                UqcId = x.UqcId,
                UqcName = x.Uqc.ShortName,
                GodownId = x.GodownId,
                GodownName = x.Godown.Name,
                Quantity = x.Quantity,
                Rate = x.Rate
            })
            .ToListAsync(cancellationToken);
        if (lines.Count == 0) return null;

        return new PurchaseHydrationResult
        {
            PurchaseVoucherId = purchaseVoucherId,
            PurchaseVoucherNumber = voucher.VoucherNumber,
            Lines = lines
        };
    }

    public async Task<PurchaseVoucherEntryDefaults> GetEntryDefaultsAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await PurchaseLookupQueries.BuildEntryDefaultsAsync(
            db, companyContext.CompanyId, companyContext.FinancialYearId, PurchaseTypeCode, cancellationToken);
    }

    public async Task<PurchaseLineLookupData> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await PurchaseLookupQueries.LoadAsync(db, companyContext.CompanyId, restrictSuppliersToSundryCreditors: true, cancellationToken);
    }

    public async Task<PurchaseItemVariantDetail?> GetItemVariantDetailAsync(
        long stockItemId,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await PurchaseLookupQueries.GetItemVariantDetailAsync(db, companyContext.CompanyId, stockItemId, cancellationToken);
    }

    public async Task<InventoryInwardEditData?> GetForEditAsync(
        long voucherId,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var voucher = await db.Vouchers.AsNoTracking()
            .Include(x => x.VoucherType).ThenInclude(x => x.ParentVoucherType)
            .Include(x => x.InventoryInwardLines)
            .SingleOrDefaultAsync(x => x.Id == voucherId &&
                x.CompanyId == companyContext.CompanyId &&
                x.FinancialYearId == companyContext.FinancialYearId,
                cancellationToken);
        if (voucher is null) return null;

        var kind = KindFromSystemType(EffectiveSystemType(voucher.VoucherType));
        if (kind is null) return null;

        return new InventoryInwardEditData
        {
            VoucherId = voucher.Id,
            Kind = kind.Value,
            VoucherTypeId = voucher.VoucherTypeId,
            VoucherTypeName = voucher.VoucherType.Name,
            VoucherNumber = voucher.VoucherNumber,
            VoucherDate = voucher.VoucherDate,
            ReferenceNumber = voucher.ReferenceNumber,
            SupplierLedgerId = voucher.PartyLedgerId,
            OpeningStockItemId = voucher.OpeningStockItemId,
            Narration = voucher.Narration,
            Status = voucher.Status,
            ConcurrencyToken = voucher.ConcurrencyToken,
            Lines = voucher.InventoryInwardLines.OrderBy(x => x.LineNumber)
                .Select(x => new InventoryInwardLineInput
                {
                    StockItemId = x.StockItemId,
                    StockItemVariantId = x.StockItemVariantId,
                    UqcId = x.UqcId,
                    GodownId = x.GodownId,
                    Quantity = x.Quantity,
                    Rate = x.Rate,
                    Amount = x.Amount,
                    PurchaseOrderLineId = x.PurchaseOrderLineId
                }).ToList()
        };
    }

    public async Task<InventoryInwardSaveResult> SaveAsync(
        InventoryInwardSaveRequest request,
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
                    .Include(x => x.InventoryInwardLines)
                    .SingleOrDefaultAsync(x => x.Id == request.VoucherId &&
                        x.CompanyId == companyContext.CompanyId &&
                        x.FinancialYearId == companyContext.FinancialYearId,
                        cancellationToken)
                    ?? throw new InvalidOperationException("The selected inventory inward voucher no longer exists.");

            if (existing is not null)
            {
                if (existing.Status == VoucherLifecycleService.CancelledStatus)
                    throw new InvalidOperationException("A cancelled inventory inward voucher cannot be altered.");
                if (!string.Equals(existing.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
                    throw new InvalidOperationException("This inventory inward voucher was changed by another operation. Reopen it and try again.");
            }

            var voucherType = existing?.VoucherType ?? await db.VoucherTypes
                .Include(x => x.ParentVoucherType)
                .SingleOrDefaultAsync(x => x.Id == request.VoucherTypeId &&
                    x.CompanyId == companyContext.CompanyId && x.IsActive,
                    cancellationToken)
                ?? throw new InvalidOperationException("Select a valid active Voucher Type.");
            var expectedType = SystemTypeFor(request.Kind);
            if (!string.Equals(EffectiveSystemType(voucherType), expectedType, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Selected Voucher Type is not a {DisplayName(request.Kind)} type.");
            if (existing is not null && existing.VoucherTypeId != request.VoucherTypeId)
                throw new InvalidOperationException("Voucher Type cannot be changed during alteration.");

            if (existing is null)
                await periodControl.EnsurePostingDateIsOpenAsync(
                    db, companyContext.CompanyId, request.VoucherDate,
                    DisplayName(request.Kind), cancellationToken);
            else
                await periodControl.EnsureVoucherMutationIsOpenAsync(
                    db, companyContext.CompanyId, existing.VoucherDate, request.VoucherDate,
                    $"{DisplayName(request.Kind)} {existing.VoucherNumber}", cancellationToken);

            var financialYear = await db.FinancialYears.AsNoTracking()
                .SingleAsync(x => x.Id == companyContext.FinancialYearId &&
                    x.CompanyId == companyContext.CompanyId, cancellationToken);
            if (request.VoucherDate < financialYear.StartDate || request.VoucherDate > financialYear.EndDate)
                throw new InvalidOperationException($"Voucher Date must be within financial year {financialYear.Name}.");
            if (request.Kind == InventoryInwardVoucherKind.OpeningStock && request.VoucherDate != financialYear.StartDate)
                throw new InvalidOperationException("Opening Stock must be dated on the Books Beginning Date.");

            await ValidatePartyAsync(db, request, cancellationToken);
            var lines = await ValidateAndNormalizeLinesAsync(db, request, cancellationToken);
            if (request.Kind == InventoryInwardVoucherKind.Purchase)
                await ValidatePurchaseOrderLinkageAsync(db, lines, existing?.Id, cancellationToken);

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
                    PartyLedgerId = request.Kind == InventoryInwardVoucherKind.Purchase
                        ? request.SupplierLedgerId : null,
                    OpeningStockItemId = request.Kind == InventoryInwardVoucherKind.OpeningStock
                        ? request.OpeningStockItemId : null,
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
                db.InventoryInwardLines.RemoveRange(voucher.InventoryInwardLines);
                await db.SaveChangesAsync(cancellationToken);

                voucher.VoucherDate = request.VoucherDate;
                voucher.ReferenceNumber = request.ReferenceNumber.Trim();
                voucher.PartyLedgerId = request.Kind == InventoryInwardVoucherKind.Purchase
                    ? request.SupplierLedgerId : null;
                if (voucher.OpeningStockItemId != request.OpeningStockItemId)
                    throw new InvalidOperationException("The Stock Item owning an Opening Stock voucher cannot be changed during alteration.");
                voucher.Narration = request.Narration.Trim();
                voucher.ModifiedAtUtc = now;
                voucher.ModifiedBy = actor;
                voucher.ConcurrencyToken = Guid.NewGuid().ToString("N");
            }

            var lineNumber = 0;
            foreach (var input in lines)
            {
                var amount = input.Amount ?? decimal.Round(input.Quantity * input.Rate, 4, MidpointRounding.AwayFromZero);
                var line = new InventoryInwardLine
                {
                    VoucherId = voucher.Id,
                    LineNumber = ++lineNumber,
                    StockItemId = input.StockItemId,
                    StockItemVariantId = input.StockItemVariantId,
                    UqcId = input.UqcId,
                    GodownId = input.GodownId,
                    Quantity = input.Quantity,
                    Rate = input.Rate,
                    Amount = amount,
                    PurchaseOrderLineId = request.Kind == InventoryInwardVoucherKind.Purchase
                        ? input.PurchaseOrderLineId : null
                };
                db.InventoryInwardLines.Add(line);
                await db.SaveChangesAsync(cancellationToken);

                stockPosting.Post(db, new StockMovementDraft(
                    CompanyId: companyContext.CompanyId,
                    FinancialYearId: companyContext.FinancialYearId,
                    VoucherId: voucher.Id,
                    MovementDate: voucher.VoucherDate,
                    StockItemId: line.StockItemId,
                    UqcId: line.UqcId,
                    GodownId: line.GodownId,
                    QuantityChange: line.Quantity,
                    Rate: line.Rate,
                    ValueChange: line.Amount,
                    MovementKind: request.Kind == InventoryInwardVoucherKind.Purchase
                        ? StockMovementSemantics.PurchaseInward
                        : StockMovementSemantics.OpeningStockInward,
                    StockItemVariantId: line.StockItemVariantId,
                    InventoryInwardLineId: line.Id), actor, now);
            }

            if (request.Kind == InventoryInwardVoucherKind.Purchase)
                await ReconcilePurchaseOrderLinksAsync(db, voucher.Id, lines, actor, now, cancellationToken);

            db.AuditLogs.Add(VoucherLifecycleService.NewAudit(
                companyContext.CompanyId, voucher.Id,
                existing is null ? "Create" : "Update", true,
                $"{(existing is null ? "Created" : "Updated")} {DisplayName(request.Kind)} '{voucherNumber}' with {lines.Count} inventory line(s).",
                actor, now));
            await db.SaveChangesAsync(cancellationToken);
            await voucherAuditHistory.RecordAsync(
                db, voucher.Id,
                existing is null ? VoucherAuditActions.Create : VoucherAuditActions.Update,
                existing is null ? $"Initial {DisplayName(request.Kind)} save." : $"{DisplayName(request.Kind)} altered.",
                actor, now, cancellationToken);
            await stockPosting.EnsureNegativeStockPolicyAsync(
                db, companyContext.CompanyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new InventoryInwardSaveResult(voucher.Id, voucherNumber);
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
            if (voucher is null) return OperationResult.Fail("The selected inventory inward voucher no longer exists.");
            var kind = KindFromSystemType(EffectiveSystemType(voucher.VoucherType));
            if (kind is null) return OperationResult.Fail("The selected voucher is not Purchase or Opening Stock.");
            if (voucher.Status == VoucherLifecycleService.CancelledStatus)
                return OperationResult.Fail("This inventory inward voucher is already cancelled.");

            await periodControl.EnsurePostingDateIsOpenAsync(
                db, companyContext.CompanyId, voucher.VoucherDate,
                $"{DisplayName(kind.Value)} {voucher.VoucherNumber}", cancellationToken);
            var links = await lifecycle.GetActiveLinkDescriptionsAsync(
                db, companyContext.CompanyId, voucher.Id, VoucherLinkScope.Downstream, cancellationToken);
            if (links.Count > 0)
                return OperationResult.Fail(VoucherLifecycleService.BuildBlockedMessage(
                    "cancelled", $"{DisplayName(kind.Value)} {voucher.VoucherNumber}", links));

            var originalKind = kind == InventoryInwardVoucherKind.Purchase
                ? StockMovementSemantics.PurchaseInward
                : StockMovementSemantics.OpeningStockInward;
            var cancellationKind = kind == InventoryInwardVoucherKind.Purchase
                ? StockMovementSemantics.PurchaseInwardCancellation
                : StockMovementSemantics.OpeningStockInwardCancellation;
            await stockPosting.ReverseVoucherPostingsAsync(
                db, voucher.Id,
                new Dictionary<string, string>(StringComparer.Ordinal) { [originalKind] = cancellationKind },
                cancellationKind, actor, now, cancellationToken);
            lifecycle.MarkCancelled(voucher, reason, actor, now);
            db.AuditLogs.Add(VoucherLifecycleService.NewAudit(
                companyContext.CompanyId, voucher.Id, "Cancel", true,
                $"Cancelled {DisplayName(kind.Value)} '{voucher.VoucherNumber}'. Reason: {voucher.CancellationReason}",
                actor, now));
            await db.SaveChangesAsync(cancellationToken);
            await voucherAuditHistory.RecordAsync(
                db, voucher.Id, VoucherAuditActions.Cancel,
                voucher.CancellationReason, actor, now, cancellationToken);
            await stockPosting.EnsureNegativeStockPolicyAsync(
                db, companyContext.CompanyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"{DisplayName(kind.Value)} {voucher.VoucherNumber} cancelled successfully.", voucher.Id);
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
            if (voucher is null) return OperationResult.Fail("The selected inventory inward voucher no longer exists.");
            var kind = KindFromSystemType(EffectiveSystemType(voucher.VoucherType));
            if (kind is null) return OperationResult.Fail("The selected voucher is not Purchase or Opening Stock.");

            await periodControl.EnsurePostingDateIsOpenAsync(
                db, companyContext.CompanyId, voucher.VoucherDate,
                $"{DisplayName(kind.Value)} {voucher.VoucherNumber}", cancellationToken);
            var links = await lifecycle.GetActiveLinkDescriptionsAsync(
                db, companyContext.CompanyId, voucher.Id, VoucherLinkScope.Downstream, cancellationToken);
            if (links.Count > 0)
                return OperationResult.Fail(VoucherLifecycleService.BuildBlockedMessage(
                    "deleted", $"{DisplayName(kind.Value)} {voucher.VoucherNumber}", links));

            var number = voucher.VoucherNumber;
            await voucherAuditHistory.RecordAsync(
                db, voucher.Id, VoucherAuditActions.Delete,
                $"Deleted {DisplayName(kind.Value)} and removed its stock effect.",
                actor, now, cancellationToken);
            stockPosting.RemoveVoucherPostings(db, voucher.Id);
            db.InventoryInwardLines.RemoveRange(db.InventoryInwardLines.Where(x => x.VoucherId == voucher.Id));
            db.Vouchers.Remove(voucher);
            db.AuditLogs.Add(VoucherLifecycleService.NewAudit(
                companyContext.CompanyId, voucher.Id, "Delete", true,
                $"Deleted {DisplayName(kind.Value)} '{number}'.", actor, now));
            await db.SaveChangesAsync(cancellationToken);
            await stockPosting.EnsureNegativeStockPolicyAsync(
                db, companyContext.CompanyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"{DisplayName(kind.Value)} {number} deleted successfully.", voucherId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail(VoucherErrorMessages.For(ex));
        }
    }

    private async Task ValidatePartyAsync(
        TexTrackDbContext db,
        InventoryInwardSaveRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Kind == InventoryInwardVoucherKind.OpeningStock)
        {
            if (request.SupplierLedgerId is not null and > 0)
                throw new InvalidOperationException("Opening Stock cannot be assigned to a supplier ledger.");
            if (request.OpeningStockItemId is not long openingItemId || openingItemId <= 0)
                throw new InvalidOperationException("Opening Stock must be owned by a Stock Item master.");
            if (!await db.StockItems.AsNoTracking().AnyAsync(x =>
                    x.Id == openingItemId && x.CompanyId == companyContext.CompanyId && x.IsActive,
                    cancellationToken))
                throw new InvalidOperationException("The Stock Item owning this Opening Stock is unavailable or inactive.");
            return;
        }

        if (request.OpeningStockItemId is not null)
            throw new InvalidOperationException("Purchase cannot be linked as a Stock Item opening balance.");

        if (request.SupplierLedgerId is not long supplierId || supplierId <= 0)
            throw new InvalidOperationException("Select a valid supplier ledger for Purchase.");
        var valid = await db.Ledgers.AsNoTracking().AnyAsync(x =>
            x.Id == supplierId && x.CompanyId == companyContext.CompanyId && x.IsActive &&
            x.LedgerGroup.RootClassification == "SundryCreditors", cancellationToken);
        if (!valid)
            throw new InvalidOperationException("Purchase supplier must be an active ledger under Sundry Creditors.");
    }

    private async Task<IReadOnlyList<InventoryInwardLineInput>> ValidateAndNormalizeLinesAsync(
        TexTrackDbContext db,
        InventoryInwardSaveRequest request,
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
                return new InventoryInwardLineInput
                {
                    StockItemId = x.StockItemId,
                    StockItemVariantId = x.StockItemVariantId,
                    UqcId = x.UqcId,
                    GodownId = x.GodownId,
                    Quantity = decimal.Round(x.Quantity, 4, MidpointRounding.AwayFromZero),
                    Rate = rate,
                    Amount = amount,
                    PurchaseOrderLineId = x.PurchaseOrderLineId
                };
            }).ToList() ?? [];
        if (lines.Count == 0)
            throw new InvalidOperationException("Enter at least one inventory line with Quantity greater than zero.");
        if (lines.Any(x => x.Quantity <= 0))
            throw new InvalidOperationException("Every inward Quantity must be greater than zero.");
        if (lines.Any(x => x.Rate < 0))
            throw new InvalidOperationException("Inward Rate cannot be negative.");
        if (lines.Any(x => x.Amount < 0))
            throw new InvalidOperationException("Inward Value cannot be negative.");
        if (request.Kind == InventoryInwardVoucherKind.OpeningStock &&
            lines.Any(x => x.StockItemId != request.OpeningStockItemId))
            throw new InvalidOperationException("Every Opening Stock line must belong to its owning Stock Item master.");
        if (lines.GroupBy(x => new
            {
                x.StockItemId, x.StockItemVariantId, x.UqcId, x.GodownId, x.PurchaseOrderLineId
            }).Any(x => x.Count() > 1))
            throw new InvalidOperationException("The same item, variant, UQC and godown position cannot appear twice against the same Purchase Order reference (or twice as open-market) in one voucher.");

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
        var validUqcs = await db.Uqcs.AsNoTracking()
            .Where(x => uqcIds.Contains(x.Id) && x.CompanyId == companyContext.CompanyId && x.IsActive)
            .Select(x => x.Id).ToListAsync(cancellationToken);
        var validGodowns = await db.Godowns.AsNoTracking()
            .Where(x => godownIds.Contains(x.Id) && x.CompanyId == companyContext.CompanyId && x.IsActive)
            .Select(x => x.Id).ToListAsync(cancellationToken);
        var validUqcSet = validUqcs.ToHashSet();
        var validGodownSet = validGodowns.ToHashSet();

        foreach (var line in lines)
        {
            if (!items.TryGetValue(line.StockItemId, out var item))
                throw new InvalidOperationException("A selected Stock Item is unavailable, inactive or belongs to another company.");
            if (!variants.TryGetValue(line.StockItemVariantId, out var variant) || variant.StockItemId != line.StockItemId)
                throw new InvalidOperationException("A selected Stock Item Variant is unavailable or does not belong to its Stock Item.");
            if (!validUqcSet.Contains(line.UqcId) || item.UqcId != line.UqcId)
                throw new InvalidOperationException("Each inward line UQC must match its active Stock Item UQC.");
            if (!validGodownSet.Contains(line.GodownId))
                throw new InvalidOperationException("A selected Godown is unavailable, inactive or belongs to another company.");
        }
        return lines;
    }

    /// <summary>
    /// Validates each Purchase line's optional Purchase Order reference: the PO
    /// line must exist, belong to a non-cancelled PO of this company, match the
    /// Purchase line's item/variant/UQC, and the receipt must not exceed the PO
    /// line's live pending quantity (excluding this voucher's own prior
    /// contribution when altering). Unlike Material In/Out's JwoVoucherId lock,
    /// a Purchase's PurchaseOrderLineId is intentionally re-editable on every
    /// alteration per an explicit 2026-09 product decision - do not add a lock
    /// here; every save simply re-validates against the PO's live state.
    /// </summary>
    private async Task ValidatePurchaseOrderLinkageAsync(
        TexTrackDbContext db,
        IReadOnlyList<InventoryInwardLineInput> lines,
        long? excludeVoucherId,
        CancellationToken cancellationToken)
    {
        var referencedLineIds = lines
            .Where(x => x.PurchaseOrderLineId is not null)
            .Select(x => x.PurchaseOrderLineId!.Value)
            .Distinct()
            .ToList();
        if (referencedLineIds.Count == 0) return;

        var poLines = await db.PurchaseOrderLines.AsNoTracking()
            .Include(x => x.Voucher)
            .Where(x => referencedLineIds.Contains(x.Id) && x.Voucher.CompanyId == companyContext.CompanyId)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var line in lines.Where(x => x.PurchaseOrderLineId is not null))
        {
            if (!poLines.TryGetValue(line.PurchaseOrderLineId!.Value, out var poLine))
                throw new InvalidOperationException("The selected Purchase Order line is unavailable.");
            if (poLine.Voucher.Status == VoucherLifecycleService.CancelledStatus)
                throw new InvalidOperationException("The selected Purchase Order is cancelled and cannot be received against.");
            if (poLine.StockItemId != line.StockItemId
                || poLine.StockItemVariantId != line.StockItemVariantId
                || poLine.UqcId != line.UqcId)
                throw new InvalidOperationException("The Purchase line's item, variant and UQC must match the referenced Purchase Order line.");

            var alreadyReceived = await db.InventoryInwardLines.AsNoTracking()
                .Where(x => x.PurchaseOrderLineId == line.PurchaseOrderLineId
                    && x.Voucher.Status != VoucherLifecycleService.CancelledStatus
                    && (excludeVoucherId == null || x.VoucherId != excludeVoucherId))
                .SumAsync(x => (decimal?)x.Quantity, cancellationToken) ?? 0m;
            var pending = poLine.OrderedQuantity - alreadyReceived;
            if (line.Quantity > pending)
                throw new InvalidOperationException(
                    $"Receiving {line.Quantity} exceeds the pending quantity ({pending}) on the referenced Purchase Order line.");
        }
    }

    /// <summary>
    /// Reconciles PO_TO_PURCHASE VoucherLink rows to match exactly which Purchase
    /// Order voucher(s) this save's lines reference - zero, one, or more. Existing
    /// links for this Purchase voucher are removed and flushed before any new ones
    /// are added, since a re-added link can share the same unique key as one just
    /// removed (same source PO, same target Purchase).
    /// </summary>
    private async Task ReconcilePurchaseOrderLinksAsync(
        TexTrackDbContext db,
        long purchaseVoucherId,
        IReadOnlyList<InventoryInwardLineInput> lines,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existingLinks = await db.VoucherLinks
            .Where(x => x.CompanyId == companyContext.CompanyId
                && x.TargetVoucherId == purchaseVoucherId
                && x.LinkType == VoucherLinkTypes.PurchaseOrderToPurchase)
            .ToListAsync(cancellationToken);
        if (existingLinks.Count > 0)
        {
            db.VoucherLinks.RemoveRange(existingLinks);
            await db.SaveChangesAsync(cancellationToken);
        }

        var referencedLineIds = lines
            .Where(x => x.PurchaseOrderLineId is not null)
            .Select(x => x.PurchaseOrderLineId!.Value)
            .Distinct()
            .ToList();
        if (referencedLineIds.Count == 0) return;

        var poVoucherIds = await db.PurchaseOrderLines.AsNoTracking()
            .Where(x => referencedLineIds.Contains(x.Id))
            .Select(x => x.VoucherId)
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var poVoucherId in poVoucherIds)
        {
            db.VoucherLinks.Add(new VoucherLink
            {
                CompanyId = companyContext.CompanyId,
                SourceVoucherId = poVoucherId,
                TargetVoucherId = purchaseVoucherId,
                LinkType = VoucherLinkTypes.PurchaseOrderToPurchase,
                CreatedAtUtc = now,
                CreatedBy = actor
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string EffectiveSystemType(VoucherType type) =>
        string.IsNullOrWhiteSpace(type.SystemTypeCode) || type.SystemTypeCode.StartsWith("CUSTOM_", StringComparison.OrdinalIgnoreCase)
            ? type.ParentVoucherType?.SystemTypeCode ?? type.SystemTypeCode
            : type.SystemTypeCode;

    private static InventoryInwardVoucherKind? KindFromSystemType(string systemType) =>
        systemType.ToUpperInvariant() switch
        {
            PurchaseTypeCode => InventoryInwardVoucherKind.Purchase,
            OpeningStockTypeCode => InventoryInwardVoucherKind.OpeningStock,
            _ => null
        };

    private static string SystemTypeFor(InventoryInwardVoucherKind kind) => kind switch
    {
        InventoryInwardVoucherKind.Purchase => PurchaseTypeCode,
        InventoryInwardVoucherKind.OpeningStock => OpeningStockTypeCode,
        _ => throw new InvalidOperationException("Unsupported inventory inward voucher kind.")
    };

    private static string DisplayName(InventoryInwardVoucherKind kind) => kind switch
    {
        InventoryInwardVoucherKind.Purchase => "Purchase",
        InventoryInwardVoucherKind.OpeningStock => "Opening Stock",
        _ => "Inventory Inward"
    };

    private static string FormatAutomaticNumber(VoucherType type, int sequence)
    {
        var numeric = type.NumberWidth > 0 ? sequence.ToString($"D{type.NumberWidth}") : sequence.ToString();
        return type.NumberingMode == "AutoPrefixSuffix" ? $"{type.Prefix}{numeric}{type.Suffix}" : numeric;
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
