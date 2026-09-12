using System.Data;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

/// <summary>
/// Purchase Order: a pure reference/planning voucher. It creates no stock movement
/// and no financial entry of any kind - StockPostingService and
/// InventoryPeriodControlService are deliberately not injected here, matching
/// JobWorkOrderRepository's shape for the same reason (Nature 'Planning',
/// PostingMode 'No financial posting').
/// </summary>
public sealed class PurchaseOrderRepository(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext,
    VoucherLifecycleService lifecycle,
    VoucherSequenceAllocator sequenceAllocator,
    VoucherAuditHistoryService voucherAuditHistory)
{
    private const string VoucherTypeCode = "PURCHASE_ORDER";

    public async Task<IReadOnlyList<PurchaseOrderListItem>> GetListAsync(
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Vouchers.AsNoTracking()
            .Include(x => x.VoucherType).ThenInclude(x => x.ParentVoucherType)
            .Include(x => x.PartyLedger)
            .Where(x => x.CompanyId == companyContext.CompanyId &&
                x.FinancialYearId == companyContext.FinancialYearId &&
                (x.VoucherType.SystemTypeCode == VoucherTypeCode ||
                 (x.VoucherType.ParentVoucherType != null && x.VoucherType.ParentVoucherType.SystemTypeCode == VoucherTypeCode)));
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var normalized = Normalize(searchText);
            query = query.Where(x => x.VoucherNumberNormalized.Contains(normalized) ||
                (x.PartyLedger != null && x.PartyLedger.NameNormalized.Contains(normalized)));
        }
        return await query
            .OrderByDescending(x => x.VoucherDate).ThenByDescending(x => x.SequenceNumber)
            .Select(x => new PurchaseOrderListItem
            {
                VoucherId = x.Id,
                VoucherNumber = x.VoucherNumber,
                VoucherDate = x.VoucherDate,
                SupplierName = x.PartyLedger != null ? x.PartyLedger.Name : string.Empty,
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
        return await PurchaseLookupQueries.LoadAsync(db, companyContext.CompanyId, restrictSuppliersToSundryCreditors: true, cancellationToken);
    }

    public async Task<PurchaseOrderEditData?> GetForEditAsync(
        long voucherId,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var voucher = await db.Vouchers.AsNoTracking()
            .Include(x => x.VoucherType).ThenInclude(x => x.ParentVoucherType)
            .Include(x => x.PurchaseOrderLines)
            .SingleOrDefaultAsync(x => x.Id == voucherId &&
                x.CompanyId == companyContext.CompanyId &&
                x.FinancialYearId == companyContext.FinancialYearId,
                cancellationToken);
        if (voucher is null || !IsPurchaseOrderType(voucher.VoucherType)) return null;

        return new PurchaseOrderEditData
        {
            VoucherId = voucher.Id,
            VoucherTypeId = voucher.VoucherTypeId,
            VoucherTypeName = voucher.VoucherType.Name,
            VoucherNumber = voucher.VoucherNumber,
            VoucherDate = voucher.VoucherDate,
            ReferenceNumber = voucher.ReferenceNumber,
            SupplierLedgerId = voucher.PartyLedgerId ?? 0,
            Narration = voucher.Narration,
            Status = voucher.Status,
            ConcurrencyToken = voucher.ConcurrencyToken,
            Lines = voucher.PurchaseOrderLines.OrderBy(x => x.LineNumber)
                .Select(x => new PurchaseOrderLineInput
                {
                    StockItemId = x.StockItemId,
                    StockItemVariantId = x.StockItemVariantId,
                    UqcId = x.UqcId,
                    GodownId = x.GodownId,
                    OrderedQuantity = x.OrderedQuantity,
                    Rate = x.Rate,
                    Amount = x.Amount,
                    ExpectedDeliveryDate = x.ExpectedDeliveryDate
                }).ToList()
        };
    }

    public async Task<PurchaseOrderSaveResult> SaveAsync(
        PurchaseOrderSaveRequest request,
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
                    .Include(x => x.PurchaseOrderLines)
                    .SingleOrDefaultAsync(x => x.Id == request.VoucherId &&
                        x.CompanyId == companyContext.CompanyId &&
                        x.FinancialYearId == companyContext.FinancialYearId,
                        cancellationToken)
                    ?? throw new InvalidOperationException("The selected Purchase Order no longer exists.");

            if (existing is not null)
            {
                if (existing.Status == VoucherLifecycleService.CancelledStatus)
                    throw new InvalidOperationException("A cancelled Purchase Order cannot be altered.");
                if (!string.Equals(existing.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
                    throw new InvalidOperationException("This Purchase Order was changed by another operation. Reopen it and try again.");
            }

            var voucherType = existing?.VoucherType ?? await db.VoucherTypes
                .Include(x => x.ParentVoucherType)
                .SingleOrDefaultAsync(x => x.Id == request.VoucherTypeId &&
                    x.CompanyId == companyContext.CompanyId && x.IsActive,
                    cancellationToken)
                ?? throw new InvalidOperationException("Select a valid active Voucher Type.");
            if (!IsPurchaseOrderType(voucherType))
                throw new InvalidOperationException("Selected Voucher Type is not a Purchase Order type.");
            if (existing is not null && existing.VoucherTypeId != request.VoucherTypeId)
                throw new InvalidOperationException("Voucher Type cannot be changed during alteration.");

            var financialYear = await db.FinancialYears.AsNoTracking()
                .SingleAsync(x => x.Id == companyContext.FinancialYearId &&
                    x.CompanyId == companyContext.CompanyId, cancellationToken);
            if (request.VoucherDate < financialYear.StartDate || request.VoucherDate > financialYear.EndDate)
                throw new InvalidOperationException($"Voucher Date must be within financial year {financialYear.Name}.");

            await ValidateSupplierAsync(db, request.SupplierLedgerId, cancellationToken);
            var lines = await ValidateAndNormalizeLinesAsync(db, request, cancellationToken);
            if (existing is not null)
                await ValidateNoReductionBelowReceivedAsync(db, existing.Id, lines, cancellationToken);

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
                db.PurchaseOrderLines.RemoveRange(voucher.PurchaseOrderLines);
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
                var amount = input.Amount ?? decimal.Round(input.OrderedQuantity * input.Rate, 4, MidpointRounding.AwayFromZero);
                db.PurchaseOrderLines.Add(new PurchaseOrderLine
                {
                    VoucherId = voucher.Id,
                    LineNumber = ++lineNumber,
                    StockItemId = input.StockItemId,
                    StockItemVariantId = input.StockItemVariantId,
                    UqcId = input.UqcId,
                    GodownId = input.GodownId,
                    OrderedQuantity = input.OrderedQuantity,
                    Rate = input.Rate,
                    Amount = amount,
                    ExpectedDeliveryDate = input.ExpectedDeliveryDate
                });
            }
            await db.SaveChangesAsync(cancellationToken);

            db.AuditLogs.Add(VoucherLifecycleService.NewAudit(
                companyContext.CompanyId, voucher.Id,
                existing is null ? "Create" : "Update", true,
                $"{(existing is null ? "Created" : "Updated")} Purchase Order '{voucherNumber}' with {lines.Count} line(s).",
                actor, now));
            await db.SaveChangesAsync(cancellationToken);
            await voucherAuditHistory.RecordAsync(
                db, voucher.Id,
                existing is null ? VoucherAuditActions.Create : VoucherAuditActions.Update,
                existing is null ? "Initial Purchase Order save." : "Purchase Order altered.",
                actor, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PurchaseOrderSaveResult(voucher.Id, voucherNumber);
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
            if (voucher is null) return OperationResult.Fail("The selected Purchase Order no longer exists.");
            if (!IsPurchaseOrderType(voucher.VoucherType)) return OperationResult.Fail("The selected voucher is not a Purchase Order.");
            if (voucher.Status == VoucherLifecycleService.CancelledStatus)
                return OperationResult.Fail("This Purchase Order is already cancelled.");

            var links = await lifecycle.GetActiveLinkDescriptionsAsync(
                db, companyContext.CompanyId, voucher.Id, VoucherLinkScope.Downstream, cancellationToken);
            if (links.Count > 0)
                return OperationResult.Fail(VoucherLifecycleService.BuildBlockedMessage(
                    "cancelled", $"Purchase Order {voucher.VoucherNumber}", links));

            lifecycle.MarkCancelled(voucher, reason, actor, now);
            db.AuditLogs.Add(VoucherLifecycleService.NewAudit(
                companyContext.CompanyId, voucher.Id, "Cancel", true,
                $"Cancelled Purchase Order '{voucher.VoucherNumber}'. Reason: {voucher.CancellationReason}",
                actor, now));
            await db.SaveChangesAsync(cancellationToken);
            await voucherAuditHistory.RecordAsync(
                db, voucher.Id, VoucherAuditActions.Cancel,
                voucher.CancellationReason, actor, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"Purchase Order {voucher.VoucherNumber} cancelled successfully.", voucher.Id);
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
            if (voucher is null) return OperationResult.Fail("The selected Purchase Order no longer exists.");
            if (!IsPurchaseOrderType(voucher.VoucherType)) return OperationResult.Fail("The selected voucher is not a Purchase Order.");

            var links = await lifecycle.GetActiveLinkDescriptionsAsync(
                db, companyContext.CompanyId, voucher.Id, VoucherLinkScope.Downstream, cancellationToken);
            if (links.Count > 0)
                return OperationResult.Fail(VoucherLifecycleService.BuildBlockedMessage(
                    "deleted", $"Purchase Order {voucher.VoucherNumber}", links));

            var number = voucher.VoucherNumber;
            await voucherAuditHistory.RecordAsync(
                db, voucher.Id, VoucherAuditActions.Delete,
                "Deleted Purchase Order.", actor, now, cancellationToken);
            db.PurchaseOrderLines.RemoveRange(db.PurchaseOrderLines.Where(x => x.VoucherId == voucher.Id));
            var ownLinks = await db.VoucherLinks
                .Where(x => x.SourceVoucherId == voucher.Id || x.TargetVoucherId == voucher.Id)
                .ToListAsync(cancellationToken);
            db.VoucherLinks.RemoveRange(ownLinks);
            db.Vouchers.Remove(voucher);
            db.AuditLogs.Add(VoucherLifecycleService.NewAudit(
                companyContext.CompanyId, voucher.Id, "Delete", true,
                $"Deleted Purchase Order '{number}'.", actor, now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"Purchase Order {number} deleted successfully.", voucherId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail(VoucherErrorMessages.For(ex));
        }
    }

    /// <summary>
    /// Live pending-quantity picker feed for the Purchase page: every non-cancelled
    /// Purchase Order line (optionally filtered by supplier) with OrderedQuantity
    /// minus the current sum of non-cancelled Purchase receipts against it, where
    /// that pending amount is still greater than zero. Always computed fresh -
    /// never a stored snapshot.
    /// </summary>
    public async Task<IReadOnlyList<PurchaseOrderPendingLine>> GetPendingLinesAsync(
        long? supplierLedgerId = null,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var query =
            from line in db.PurchaseOrderLines.AsNoTracking()
            join voucher in db.Vouchers.AsNoTracking() on line.VoucherId equals voucher.Id
            join item in db.StockItems.AsNoTracking() on line.StockItemId equals item.Id
            join uqc in db.Uqcs.AsNoTracking() on line.UqcId equals uqc.Id
            where voucher.CompanyId == companyContext.CompanyId &&
                  voucher.FinancialYearId == companyContext.FinancialYearId &&
                  voucher.Status != VoucherLifecycleService.CancelledStatus &&
                  (supplierLedgerId == null || voucher.PartyLedgerId == supplierLedgerId)
            select new
            {
                PurchaseOrderLineId = line.Id,
                PurchaseOrderVoucherId = voucher.Id,
                PurchaseOrderVoucherNumber = voucher.VoucherNumber,
                PurchaseOrderVoucherDate = voucher.VoucherDate,
                line.StockItemId,
                StockItemName = item.Name,
                line.StockItemVariantId,
                line.UqcId,
                UqcName = uqc.ShortName,
                line.GodownId,
                line.OrderedQuantity,
                line.Rate
            };

        var candidates = await query.ToListAsync(cancellationToken);
        if (candidates.Count == 0) return [];

        var lineIds = candidates.Select(x => x.PurchaseOrderLineId).ToList();
        var received = await db.InventoryInwardLines.AsNoTracking()
            .Where(x => x.PurchaseOrderLineId != null && lineIds.Contains(x.PurchaseOrderLineId!.Value)
                && x.Voucher.Status != VoucherLifecycleService.CancelledStatus)
            .GroupBy(x => x.PurchaseOrderLineId!.Value)
            .Select(g => new { PurchaseOrderLineId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.PurchaseOrderLineId, x => x.Quantity, cancellationToken);

        var result = new List<PurchaseOrderPendingLine>();
        foreach (var c in candidates)
        {
            var receivedQuantity = received.GetValueOrDefault(c.PurchaseOrderLineId);
            var pending = c.OrderedQuantity - receivedQuantity;
            if (pending <= 0) continue;
            result.Add(new PurchaseOrderPendingLine
            {
                PurchaseOrderLineId = c.PurchaseOrderLineId,
                PurchaseOrderVoucherId = c.PurchaseOrderVoucherId,
                PurchaseOrderVoucherNumber = c.PurchaseOrderVoucherNumber,
                PurchaseOrderVoucherDate = c.PurchaseOrderVoucherDate,
                StockItemId = c.StockItemId,
                StockItemName = c.StockItemName,
                StockItemVariantId = c.StockItemVariantId,
                UqcId = c.UqcId,
                UqcName = c.UqcName,
                GodownId = c.GodownId,
                OrderedQuantity = c.OrderedQuantity,
                ReceivedQuantity = receivedQuantity,
                PendingQuantity = pending,
                Rate = c.Rate
            });
        }
        return result;
    }

    /// <summary>
    /// Outstanding Purchase Orders for one supplier, grouped from the same
    /// live pending-line data GetPendingLinesAsync computes - no separate
    /// query - for the Purchase-side "PO Reference" picker (mirrors
    /// MaterialInRepository.GetPendingOrdersAsync's role for Job Workers).
    /// </summary>
    public async Task<IReadOnlyList<PurchaseOrderOutstandingSummary>> GetOutstandingOrdersForSupplierAsync(
        long supplierLedgerId,
        CancellationToken cancellationToken = default)
    {
        var pendingLines = await GetPendingLinesAsync(supplierLedgerId, cancellationToken);
        return pendingLines
            .GroupBy(x => new { x.PurchaseOrderVoucherId, x.PurchaseOrderVoucherNumber, x.PurchaseOrderVoucherDate })
            .Select(g => new PurchaseOrderOutstandingSummary
            {
                PurchaseOrderVoucherId = g.Key.PurchaseOrderVoucherId,
                PurchaseOrderVoucherNumber = g.Key.PurchaseOrderVoucherNumber,
                VoucherDate = g.Key.PurchaseOrderVoucherDate,
                PendingLineCount = g.Count()
            })
            .OrderBy(x => x.VoucherDate)
            .ThenBy(x => x.PurchaseOrderVoucherNumber)
            .ToList();
    }

    /// <summary>
    /// Full hydration of one Purchase Order's still-pending lines, for
    /// Purchase's "select a PO Reference -> tear down and rebuild every line"
    /// prefill flow (mirrors MaterialIn.razor's SelectOrder/PopulateOrderRows).
    /// Returns null if the voucher has no pending lines left (fully received,
    /// or not found/not visible to this company/financial year/status filter).
    /// </summary>
    public async Task<PurchaseOrderHydrationResult?> GetPendingLinesForOrderAsync(
        long purchaseOrderVoucherId,
        CancellationToken cancellationToken = default)
    {
        var pendingLines = await GetPendingLinesAsync(null, cancellationToken);
        var lines = pendingLines.Where(x => x.PurchaseOrderVoucherId == purchaseOrderVoucherId).ToList();
        if (lines.Count == 0) return null;
        return new PurchaseOrderHydrationResult
        {
            PurchaseOrderVoucherId = purchaseOrderVoucherId,
            PurchaseOrderVoucherNumber = lines[0].PurchaseOrderVoucherNumber,
            Lines = lines
        };
    }

    public async Task<PurchaseItemVariantDetail?> GetItemVariantDetailAsync(
        long stockItemId,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await PurchaseLookupQueries.GetItemVariantDetailAsync(db, companyContext.CompanyId, stockItemId, cancellationToken);
    }

    private async Task ValidateSupplierAsync(TexTrackDbContext db, long supplierLedgerId, CancellationToken cancellationToken)
    {
        if (supplierLedgerId <= 0)
            throw new InvalidOperationException("Select a valid supplier ledger for the Purchase Order.");
        var valid = await db.Ledgers.AsNoTracking().AnyAsync(x =>
            x.Id == supplierLedgerId && x.CompanyId == companyContext.CompanyId && x.IsActive &&
            x.LedgerGroup.RootClassification == "SundryCreditors", cancellationToken);
        if (!valid)
            throw new InvalidOperationException("Purchase Order supplier must be an active ledger under Sundry Creditors.");
    }

    private async Task<IReadOnlyList<PurchaseOrderLineInput>> ValidateAndNormalizeLinesAsync(
        TexTrackDbContext db,
        PurchaseOrderSaveRequest request,
        CancellationToken cancellationToken)
    {
        var lines = request.Lines?
            .Where(x => x.OrderedQuantity != 0 || x.Rate != 0 || x.Amount != 0)
            .Select(x =>
            {
                var amount = x.Amount is decimal acceptedAmount
                    ? decimal.Round(acceptedAmount, 4, MidpointRounding.AwayFromZero)
                    : (decimal?)null;
                var rate = amount is not null && x.OrderedQuantity > 0
                    ? decimal.Round(amount.Value / x.OrderedQuantity, 4, MidpointRounding.AwayFromZero)
                    : decimal.Round(x.Rate, 4, MidpointRounding.AwayFromZero);
                return new PurchaseOrderLineInput
                {
                    StockItemId = x.StockItemId,
                    StockItemVariantId = x.StockItemVariantId,
                    UqcId = x.UqcId,
                    GodownId = x.GodownId,
                    OrderedQuantity = decimal.Round(x.OrderedQuantity, 4, MidpointRounding.AwayFromZero),
                    Rate = rate,
                    Amount = amount,
                    ExpectedDeliveryDate = x.ExpectedDeliveryDate
                };
            }).ToList() ?? [];
        if (lines.Count == 0)
            throw new InvalidOperationException("Enter at least one Purchase Order line with Ordered Quantity greater than zero.");
        if (lines.Any(x => x.OrderedQuantity <= 0))
            throw new InvalidOperationException("Every Ordered Quantity must be greater than zero.");
        if (lines.Any(x => x.Rate < 0))
            throw new InvalidOperationException("Rate cannot be negative.");
        if (lines.GroupBy(x => new { x.StockItemId, x.StockItemVariantId, x.UqcId, x.GodownId }).Any(x => x.Count() > 1))
            throw new InvalidOperationException("The same item, variant, UQC and godown position cannot appear twice in one Purchase Order.");

        var itemIds = lines.Select(x => x.StockItemId).Distinct().ToList();
        var variantIds = lines.Select(x => x.StockItemVariantId).Distinct().ToList();
        var uqcIds = lines.Select(x => x.UqcId).Distinct().ToList();
        var godownIds = lines.Where(x => x.GodownId is not null).Select(x => x.GodownId!.Value).Distinct().ToList();
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
                throw new InvalidOperationException("Each line UQC must match its active Stock Item UQC.");
            if (line.GodownId is not null && !validGodownSet.Contains(line.GodownId.Value))
                throw new InvalidOperationException("A selected expected Godown is unavailable, inactive or belongs to another company.");
        }
        return lines;
    }

    private async Task ValidateNoReductionBelowReceivedAsync(
        TexTrackDbContext db,
        long existingVoucherId,
        IReadOnlyList<PurchaseOrderLineInput> newLines,
        CancellationToken cancellationToken)
    {
        var existingLines = await db.PurchaseOrderLines.AsNoTracking()
            .Where(x => x.VoucherId == existingVoucherId)
            .ToListAsync(cancellationToken);
        if (existingLines.Count == 0) return;

        var existingLineIds = existingLines.Select(x => x.Id).ToList();
        var received = await db.InventoryInwardLines.AsNoTracking()
            .Where(x => x.PurchaseOrderLineId != null && existingLineIds.Contains(x.PurchaseOrderLineId!.Value)
                && x.Voucher.Status != VoucherLifecycleService.CancelledStatus)
            .GroupBy(x => x.PurchaseOrderLineId!.Value)
            .Select(g => new { PurchaseOrderLineId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.PurchaseOrderLineId, x => x.Quantity, cancellationToken);
        if (received.Count == 0) return;

        // Match altered lines back to their prior row by identity (item/variant/uqc/godown),
        // since line numbers are reassigned fresh on every save.
        foreach (var existingLine in existingLines)
        {
            if (!received.TryGetValue(existingLine.Id, out var receivedQuantity)) continue;
            var stillPresent = newLines.FirstOrDefault(x =>
                x.StockItemId == existingLine.StockItemId &&
                x.StockItemVariantId == existingLine.StockItemVariantId &&
                x.UqcId == existingLine.UqcId &&
                x.GodownId == existingLine.GodownId);
            var newOrderedQuantity = stillPresent?.OrderedQuantity ?? 0m;
            if (newOrderedQuantity < receivedQuantity)
                throw new InvalidOperationException(
                    $"Cannot reduce Ordered Quantity below {receivedQuantity}, which has already been received against this line.");
        }
    }

    private static bool IsPurchaseOrderType(VoucherType type) =>
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
