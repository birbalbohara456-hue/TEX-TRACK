using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

/// <summary>Read-only operational reports built exclusively from persisted vouchers and stock movements.</summary>
public sealed class OperationalReportingService(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext)
{
    public async Task<List<JobWorkRateVarianceRow>> GetJobWorkRateVarianceAsync(DateOnly asOnDate, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.MaterialInFinishedGoods.AsNoTracking()
            .Where(x => x.Voucher.CompanyId == companyContext.CompanyId &&
                x.Voucher.FinancialYearId == companyContext.FinancialYearId &&
                x.Voucher.Status != "Cancelled" && x.JwoFinishedGood.Voucher.Status != "Cancelled" &&
                x.Voucher.VoucherDate <= asOnDate && x.ReceivedQuantity > 0)
            .OrderByDescending(x => x.Voucher.VoucherDate).ThenByDescending(x => x.Voucher.SequenceNumber).ThenBy(x => x.LineNumber)
            .Select(x => new JobWorkRateVarianceRow
            {
                MaterialInVoucherId = x.VoucherId, JwoVoucherId = x.JwoFinishedGood.VoucherId,
                ReceiptDate = x.Voucher.VoucherDate, MaterialInNumber = x.Voucher.VoucherNumber,
                JwoNumber = x.JwoFinishedGood.Voucher.VoucherNumber, Batch = x.JwoFinishedGood.Voucher.Batch,
                FinishedGood = x.JwoFinishedGood.StockItem.Name, StageOutput = x.StockItem.Name,
                Process = x.BomStage == null || x.BomStage.Process == null ? "Not recorded" : x.BomStage.Process.Name,
                JobWorker = x.Voucher.PartyLedger == null ? "Not recorded" : x.Voucher.PartyLedger.Name,
                AssignmentVersion = x.StageAssignment == null ? null : x.StageAssignment.AssignmentVersion,
                Uqc = x.Uqc.ShortName, Quantity = x.ReceivedQuantity, ExpectedRate = x.ExpectedProcessRate,
                ActualRate = x.ActualProcessRate ?? x.ProcessCharge / x.ReceivedQuantity,
                ActualTotal = x.ProcessCharge
            }).ToListAsync(cancellationToken);
    }

    public async Task<OperationalReportLookups> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = companyContext.CompanyId;
        return new OperationalReportLookups
        {
            JobWorkers = await db.Ledgers.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive && x.IsJobWorker)
                .OrderBy(x => x.Name).Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(cancellationToken),
            StockGroups = await db.StockGroups.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.Name).Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(cancellationToken),
            StockCategories = await db.StockCategories.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.Name).Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(cancellationToken),
            StockItems = await db.StockItems.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.Name).Select(x => new LookupItem(x.Id, x.Name, x.Uqc.ShortName)).ToListAsync(cancellationToken),
            Godowns = await db.Godowns.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.Name).Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(cancellationToken),
            Processes = await db.Processes.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.Name).Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(cancellationToken)
        };
    }

    public async Task<IReadOnlyList<VoucherUqcQuantityRow>> GetVoucherQuantitiesAsync(
        string kind, IReadOnlyCollection<long> voucherIds, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        if (voucherIds.Count == 0) return Array.Empty<VoucherUqcQuantityRow>();

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var ids = voucherIds.Distinct().ToList();
        var companyId = companyContext.CompanyId;

        IQueryable<VoucherUqcQuantityRow>? query = kind switch
        {
            "master-job-orders" => db.MasterJobOrderFinishedGoods.AsNoTracking()
                .Where(x => ids.Contains(x.VoucherId) && x.Voucher.CompanyId == companyId && x.Voucher.Status != "Cancelled")
                .Select(x => new VoucherUqcQuantityRow { VoucherId = x.VoucherId, UqcName = x.StockItem.Uqc.ShortName, Quantity = x.OrderedQuantity }),
            "job-out-orders" => db.JobWorkOrderFinishedGoods.AsNoTracking()
                .Where(x => ids.Contains(x.VoucherId) && x.Voucher.CompanyId == companyId && x.Voucher.Status != "Cancelled")
                .Select(x => new VoucherUqcQuantityRow { VoucherId = x.VoucherId, UqcName = x.StockItem.Uqc.ShortName, Quantity = x.OrderedQuantity }),
            "material-out" => db.MaterialOutLines.AsNoTracking()
                .Where(x => ids.Contains(x.VoucherId) && x.Voucher.CompanyId == companyId && x.Voucher.Status != "Cancelled")
                .Select(x => new VoucherUqcQuantityRow { VoucherId = x.VoucherId, UqcName = x.Uqc.ShortName, Quantity = x.IssuedQuantity }),
            "material-in" => db.MaterialInFinishedGoods.AsNoTracking()
                .Where(x => ids.Contains(x.VoucherId) && x.Voucher.CompanyId == companyId && x.Voucher.Status != "Cancelled")
                .Select(x => new VoucherUqcQuantityRow { VoucherId = x.VoucherId, UqcName = x.StockItem.Uqc.ShortName, Quantity = x.ReceivedQuantity }),
            _ => null
        };

        return query is null
            ? Array.Empty<VoucherUqcQuantityRow>()
            : await query.ToListAsync(cancellationToken);
    }

    public async Task<JobWorkerControlReport> GetJobWorkerControlAsync(
        JobWorkerControlFilter filter, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var asOfDate = filter.DateTo;
        var query = db.Vouchers.AsNoTracking().Where(x =>
            x.CompanyId == companyContext.CompanyId &&
            x.Status != "Cancelled" && x.JobWorkFinishedGoods.Any());
        if (filter.DateFrom is not null) query = query.Where(x => x.VoucherDate >= filter.DateFrom.Value);
        if (filter.DateTo is not null) query = query.Where(x => x.VoucherDate <= filter.DateTo.Value);
        if (filter.JobWorkerId is not null) query = query.Where(x => x.PartyLedgerId == filter.JobWorkerId);
        if (!string.IsNullOrWhiteSpace(filter.JobWorker)) query = query.Where(x => x.PartyLedger != null && EF.Functions.ILike(x.PartyLedger.Name, Pattern(filter.JobWorker)));
        if (!string.IsNullOrWhiteSpace(filter.Item)) query = query.Where(x =>
            x.JobWorkFinishedGoods.Any(f => EF.Functions.ILike(f.StockItem.Name, Pattern(filter.Item)) ||
                f.Components.Any(c => EF.Functions.ILike(c.StockItem.Name, Pattern(filter.Item)))));
        if (filter.StockGroupId is not null) query = query.Where(x =>
            x.JobWorkFinishedGoods.Any(f => f.StockItem.StockGroupId == filter.StockGroupId.Value ||
                f.Components.Any(c => c.StockItem.StockGroupId == filter.StockGroupId.Value)));
        if (filter.StockCategoryId is not null) query = query.Where(x =>
            x.JobWorkFinishedGoods.Any(f => f.StockItem.StockCategoryId == filter.StockCategoryId.Value ||
                f.Components.Any(c => c.StockItem.StockCategoryId == filter.StockCategoryId.Value)));
        if (!string.IsNullOrWhiteSpace(filter.JwoNumber)) query = query.Where(x => EF.Functions.ILike(x.VoucherNumber, Pattern(filter.JwoNumber)));
        if (!string.IsNullOrWhiteSpace(filter.MasterJobOrderNumber)) query = query.Where(x =>
            x.MasterJobOrder != null && EF.Functions.ILike(x.MasterJobOrder.VoucherNumber, Pattern(filter.MasterJobOrderNumber)));
        if (!string.IsNullOrWhiteSpace(filter.Batch)) query = query.Where(x => EF.Functions.ILike(x.Batch, Pattern(filter.Batch)));
        if (!string.IsNullOrWhiteSpace(filter.BatchOrOrder))
            query = query.Where(x => EF.Functions.ILike(x.Batch, Pattern(filter.BatchOrOrder)) ||
                                     EF.Functions.ILike(x.VoucherNumber, Pattern(filter.BatchOrOrder)));
        if (filter.ProcessId is not null)
            query = query.Where(x => x.JobWorkFinishedGoods.Any(f => f.Processes.Any(p => p.ProcessId == filter.ProcessId.Value)));
        else if (!string.IsNullOrWhiteSpace(filter.Process))
            query = query.Where(x => x.JobWorkFinishedGoods.Any(f => f.Processes.Any(p => EF.Functions.ILike(p.Process.Name, Pattern(filter.Process)))));

        if (filter.Status != JobWorkerControlStatusFilter.All)
        {
            // Aggregate each transaction stream once. The previous expression repeated correlated
            // SUM subqueries for every JWO component, which becomes prohibitively expensive once
            // the voucher tables contain hundreds of thousands of rows.
            var validReceipts = db.MaterialInFinishedGoods.AsNoTracking()
                .Where(x => x.Voucher.Status != "Cancelled" &&
                            (x.BomStageId == null || x.BomStage!.IsFinalStage));
            var validIssues = db.MaterialOutLines.AsNoTracking()
                .Where(x => x.Voucher.Status != "Cancelled");
            var validAllocations = db.MaterialInMaterialOutAllocations.AsNoTracking()
                .Where(x => x.MaterialOutLine.Voucher.Status != "Cancelled" &&
                            x.ConsumptionLine.Voucher.Status != "Cancelled");
            if (asOfDate is not null)
            {
                var cutoff = asOfDate.Value;
                validReceipts = validReceipts.Where(x => x.Voucher.VoucherDate <= cutoff);
                validIssues = validIssues.Where(x => x.Voucher.VoucherDate <= cutoff);
                validAllocations = validAllocations.Where(x => x.ConsumptionLine.Voucher.VoucherDate <= cutoff);
            }

            var receiptTotals = validReceipts
                .GroupBy(x => x.JwoFinishedGoodId)
                .Select(x => new { JwoFinishedGoodId = x.Key, Quantity = (decimal?)x.Sum(y => y.ReceivedQuantity) });
            var issueTotals = validIssues
                .GroupBy(x => x.JwoComponentId)
                .Select(x => new { JwoComponentId = x.Key, Quantity = (decimal?)x.Sum(y => y.IssuedQuantity) });
            var consumptionTotals = validAllocations
                .GroupBy(x => x.MaterialOutLine.JwoComponentId)
                .Select(x => new { JwoComponentId = x.Key, Quantity = (decimal?)x.Sum(y => y.AllocatedQuantity) });

            var unfinishedFinishedGoodVoucherIds =
                from finishedGood in db.JobWorkOrderFinishedGoods.AsNoTracking()
                join receipt in receiptTotals on finishedGood.Id equals receipt.JwoFinishedGoodId into receiptMatches
                from receipt in receiptMatches.DefaultIfEmpty()
                where finishedGood.OrderedQuantity > (receipt.Quantity ?? 0)
                select finishedGood.VoucherId;

            var unfinishedComponentVoucherIds =
                from component in db.JobWorkOrderComponents.AsNoTracking()
                join issue in issueTotals on component.Id equals issue.JwoComponentId into issueMatches
                from issue in issueMatches.DefaultIfEmpty()
                join consumption in consumptionTotals on component.Id equals consumption.JwoComponentId into consumptions
                from consumption in consumptions.DefaultIfEmpty()
                let issuedQuantity = issue.Quantity ?? 0
                let consumedQuantity = consumption.Quantity ?? 0
                where component.RequiredQuantity != issuedQuantity || issuedQuantity != consumedQuantity
                select component.FinishedGood.VoucherId;

            if (filter.Status == JobWorkerControlStatusFilter.Pending)
                query = query.Where(x => unfinishedFinishedGoodVoucherIds.Contains(x.Id) ||
                                         unfinishedComponentVoucherIds.Contains(x.Id));
            else
                query = query.Where(x => !unfinishedFinishedGoodVoucherIds.Contains(x.Id) &&
                                         !unfinishedComponentVoucherIds.Contains(x.Id));
        }

        var headers = await query.OrderByDescending(x => x.VoucherDate).ThenByDescending(x => x.SequenceNumber)
            .Take(200).Select(x => new JobWorkerControlOrder
            {
                JwoVoucherId = x.Id, JwoNumber = x.VoucherNumber, JwoDate = x.VoucherDate,
                MasterJobOrderId = x.MasterJobOrderId,
                MasterJobOrderNumber = x.MasterJobOrder == null ? string.Empty : x.MasterJobOrder.VoucherNumber,
                Batch = x.Batch, JobWorkerName = x.PartyLedger == null ? string.Empty : x.PartyLedger.Name,
                Status = x.Status
            }).ToListAsync(cancellationToken);
        var ids = headers.Select(x => x.JwoVoucherId).ToList();
        if (ids.Count == 0) return new JobWorkerControlReport();

        var processNames = await db.JobWorkOrderProcesses.AsNoTracking()
            .Where(x => ids.Contains(x.FinishedGood.VoucherId))
            .Select(x => new { JwoVoucherId = x.FinishedGood.VoucherId, x.Process.Name })
            .ToListAsync(cancellationToken);

        var issues = await db.MaterialOutLines.AsNoTracking()
            .Where(x => ids.Contains(x.JwoVoucherId) && x.Voucher.Status != "Cancelled" &&
                (asOfDate == null || x.Voucher.VoucherDate <= asOfDate.Value))
            .OrderBy(x => x.Voucher.VoucherDate).ThenBy(x => x.Voucher.SequenceNumber).ThenBy(x => x.LineNumber)
            .Select(x => new
            {
                x.JwoVoucherId,
                Row = new JobWorkerMaterialIssueRow
                {
                    VoucherId = x.VoucherId, JwoComponentId = x.JwoComponentId,
                    Date = x.Voucher.VoucherDate, VoucherNumber = x.Voucher.VoucherNumber,
                    ComponentName = x.StockItem.Name, UqcName = x.Uqc.ShortName,
                    Quantity = x.IssuedQuantity, Rate = x.Rate, Value = x.Amount
                }
            }).ToListAsync(cancellationToken);

        var receipts = await db.MaterialInFinishedGoods.AsNoTracking()
            .Where(x => ids.Contains(x.JwoFinishedGood.VoucherId) && x.Voucher.Status != "Cancelled" &&
                (x.BomStageId == null || x.BomStage!.IsFinalStage) &&
                (asOfDate == null || x.Voucher.VoucherDate <= asOfDate.Value))
            .OrderBy(x => x.Voucher.VoucherDate).ThenBy(x => x.Voucher.SequenceNumber).ThenBy(x => x.LineNumber)
            .Select(x => new
            {
                JwoVoucherId = x.JwoFinishedGood.VoucherId,
                Row = new JobWorkerReceiptRow
                {
                    VoucherId = x.VoucherId, JwoFinishedGoodId = x.JwoFinishedGoodId,
                    Date = x.Voucher.VoucherDate, VoucherNumber = x.Voucher.VoucherNumber,
                    FinishedGoodName = x.StockItem.Name, UqcName = x.StockItem.Uqc.ShortName,
                    Quantity = x.ReceivedQuantity, Rate = x.Rate, Value = x.FinishedGoodsValue,
                    MaterialValue = x.MaterialValue, ProcessCharge = x.ProcessCharge
                }
            }).ToListAsync(cancellationToken);
        var receiptLineIds = await db.MaterialInFinishedGoods.AsNoTracking()
            .Where(x => ids.Contains(x.JwoFinishedGood.VoucherId) && x.Voucher.Status != "Cancelled" &&
                (x.BomStageId == null || x.BomStage!.IsFinalStage) &&
                (asOfDate == null || x.Voucher.VoucherDate <= asOfDate.Value))
            .Select(x => new { x.Id, x.VoucherId, x.JwoFinishedGoodId }).ToListAsync(cancellationToken);
        var lineIds = receiptLineIds.Select(x => x.Id).ToList();
        var variants = await db.MaterialInFinishedGoodAllocations.AsNoTracking()
            .Where(x => lineIds.Contains(x.FinishedGoodLineId))
            .Select(x => new
            {
                x.FinishedGoodLineId,
                Row = new JobWorkerReceiptVariantRow
                {
                    ColourName = x.StockItemVariant.Colour == null ? "N/A" : x.StockItemVariant.Colour.Name,
                    SizeName = x.StockItemVariant.Size == null ? "N/A" : x.StockItemVariant.Size.Name,
                    Quantity = x.Quantity
                }
            }).ToListAsync(cancellationToken);

        var variantsByReceipt = variants.GroupBy(x => x.FinishedGoodLineId).ToDictionary(x => x.Key, x => x.Select(y => y.Row).ToList());
        var receiptIdentity = receiptLineIds.ToDictionary(x => (x.VoucherId, x.JwoFinishedGoodId), x => x.Id);
        var materialPositions = await db.JobWorkOrderComponents.AsNoTracking()
            .Where(x => ids.Contains(x.FinishedGood.VoucherId))
            .OrderBy(x => x.FinishedGood.LineNumber).ThenBy(x => x.LineNumber)
            .Select(x => new
            {
                JwoVoucherId = x.FinishedGood.VoucherId,
                Row = new JobWorkerMaterialPositionRow
                {
                    JwoComponentId = x.Id,
                    FinishedGoodName = x.FinishedGood.StockItem.Name,
                    ComponentName = x.StockItem.Name,
                    UqcName = x.Uqc.ShortName,
                    RequiredQuantity = x.RequiredQuantity,
                    IssuedQuantity = db.MaterialOutLines
                        .Where(y => y.JwoComponentId == x.Id && y.Voucher.Status != "Cancelled" &&
                            (asOfDate == null || y.Voucher.VoucherDate <= asOfDate.Value))
                        .Sum(y => (decimal?)y.IssuedQuantity) ?? 0,
                    ConsumedQuantity = db.MaterialInMaterialOutAllocations
                        .Where(y => y.MaterialOutLine.JwoComponentId == x.Id &&
                                    y.MaterialOutLine.Voucher.Status != "Cancelled" &&
                                    y.ConsumptionLine.Voucher.Status != "Cancelled" &&
                                    (asOfDate == null || y.ConsumptionLine.Voucher.VoucherDate <= asOfDate.Value))
                        .Sum(y => (decimal?)y.AllocatedQuantity) ?? 0
                }
            }).ToListAsync(cancellationToken);
        var finishedGoodPositions = await db.JobWorkOrderFinishedGoods.AsNoTracking()
            .Where(x => ids.Contains(x.VoucherId))
            .Select(x => new
            {
                JwoVoucherId = x.VoucherId,
                Row = new JobWorkerFinishedGoodPositionRow
                {
                    JwoFinishedGoodId = x.Id,
                    FinishedGoodName = x.StockItem.Name,
                    UqcName = x.StockItem.Uqc.ShortName,
                    OrderedQuantity = x.OrderedQuantity,
                    ReceivedQuantity = db.MaterialInFinishedGoods
                        .Where(y => y.JwoFinishedGoodId == x.Id && y.Voucher.Status != "Cancelled" &&
                            (y.BomStageId == null || y.BomStage!.IsFinalStage) &&
                            (asOfDate == null || y.Voucher.VoucherDate <= asOfDate.Value))
                        .Sum(y => (decimal?)y.ReceivedQuantity) ?? 0
                }
            }).ToListAsync(cancellationToken);
        foreach (var header in headers)
        {
            header.ProcessNames = string.Join(", ", processNames.Where(x => x.JwoVoucherId == header.JwoVoucherId)
                .Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase));
            header.Issues = issues.Where(x => x.JwoVoucherId == header.JwoVoucherId).Select(x => x.Row).ToList();
            header.Receipts = receipts.Where(x => x.JwoVoucherId == header.JwoVoucherId).Select(x => x.Row).ToList();
            header.MaterialPositions = materialPositions.Where(x => x.JwoVoucherId == header.JwoVoucherId).Select(x => x.Row).ToList();
            header.FinishedGoodPositions = finishedGoodPositions.Where(x => x.JwoVoucherId == header.JwoVoucherId).Select(x => x.Row).ToList();
            header.TotalFinishedGoodsOrdered = header.FinishedGoodPositions.Sum(x => x.OrderedQuantity);
            header.TotalFinishedGoodsReceived = header.FinishedGoodPositions.Sum(x => x.ReceivedQuantity);
            foreach (var componentIssues in header.Issues.GroupBy(x => x.JwoComponentId))
            {
                var required = header.MaterialPositions.FirstOrDefault(x => x.JwoComponentId == componentIssues.Key)?.RequiredQuantity ?? 0;
                var currentPending = Math.Max(0, required - componentIssues.Sum(x => x.Quantity));
                foreach (var issue in componentIssues) issue.PendingQuantity = currentPending;
            }
            foreach (var finishedGoodReceipts in header.Receipts.GroupBy(x => x.JwoFinishedGoodId))
            {
                var orderedQuantity = header.FinishedGoodPositions.FirstOrDefault(x => x.JwoFinishedGoodId == finishedGoodReceipts.Key)?.OrderedQuantity ?? 0;
                var currentPending = Math.Max(0, orderedQuantity - finishedGoodReceipts.Sum(x => x.Quantity));
                foreach (var receipt in finishedGoodReceipts) receipt.PendingQuantity = currentPending;
            }
            foreach (var receipt in header.Receipts)
                if (receiptIdentity.TryGetValue((receipt.VoucherId, receipt.JwoFinishedGoodId), out var lineId) && variantsByReceipt.TryGetValue(lineId, out var rows))
                    receipt.Variants = rows;
        }
        var batches = headers
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Batch) ? "(NO BATCH)" : x.Batch.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(x => new JobWorkerControlBatch { Batch = x.Key, Orders = x.ToList() })
            .ToList();
        return new JobWorkerControlReport { Orders = headers, Batches = batches };
    }

    public async Task<PendingMaterialIssueReport> GetPendingMaterialIssuesAsync(
        PendingMaterialIssueFilter filter, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = companyContext.CompanyId;
        var issueTotals = db.MaterialOutLines.AsNoTracking()
            .Where(x => x.Voucher.Status != "Cancelled")
            .GroupBy(x => x.JwoComponentId)
            .Select(x => new { JwoComponentId = x.Key, Quantity = (decimal?)x.Sum(y => y.IssuedQuantity) });
        var query =
            from component in db.JobWorkOrderComponents.AsNoTracking()
            join issue in issueTotals on component.Id equals issue.JwoComponentId into issueMatches
            from issue in issueMatches.DefaultIfEmpty()
            where component.FinishedGood.Voucher.CompanyId == companyId &&
                  component.FinishedGood.Voucher.Status != "Cancelled"
            select new { Component = component, IssuedQuantity = issue.Quantity ?? 0 };

        if (filter.JobWorkerId is not null)
            query = query.Where(x => x.Component.FinishedGood.Voucher.PartyLedgerId == filter.JobWorkerId.Value);
        else if (!string.IsNullOrWhiteSpace(filter.JobWorker))
            query = query.Where(x => x.Component.FinishedGood.Voucher.PartyLedger != null &&
                EF.Functions.ILike(x.Component.FinishedGood.Voucher.PartyLedger.Name, Pattern(filter.JobWorker)));

        // A deleted voucher has no row to query. Cancelled Material Out vouchers are
        // deliberately excluded, so only live issues reduce the outstanding requirement.
        query = query.Where(x => x.Component.RequiredQuantity > x.IssuedQuantity);

        if (filter.Status == PendingMaterialIssueStatusFilter.PartiallyIssued)
            query = query.Where(x => x.IssuedQuantity > 0);
        else if (filter.Status == PendingMaterialIssueStatusFilter.FullyPending)
            query = query.Where(x => x.IssuedQuantity == 0);

        var components = await query
            .OrderByDescending(x => x.Component.FinishedGood.Voucher.VoucherDate)
            .ThenByDescending(x => x.Component.FinishedGood.Voucher.SequenceNumber)
            .ThenBy(x => x.Component.FinishedGood.LineNumber)
            .ThenBy(x => x.Component.LineNumber)
            .Select(x => new
            {
                JwoVoucherId = x.Component.FinishedGood.VoucherId,
                JwoNumber = x.Component.FinishedGood.Voucher.VoucherNumber,
                JwoDate = x.Component.FinishedGood.Voucher.VoucherDate,
                x.Component.FinishedGood.Voucher.Batch,
                JobWorkerName = x.Component.FinishedGood.Voucher.PartyLedger == null
                    ? string.Empty
                    : x.Component.FinishedGood.Voucher.PartyLedger.Name,
                Component = new PendingMaterialIssueComponentRow
                {
                    JwoComponentId = x.Component.Id,
                    FinishedGoodName = x.Component.FinishedGood.StockItem.Name,
                    ComponentName = x.Component.StockItem.Name,
                    UqcName = x.Component.Uqc.ShortName,
                    RequiredQuantity = x.Component.RequiredQuantity,
                    IssuedQuantity = x.IssuedQuantity
                }
            })
            .Take(2000)
            .ToListAsync(cancellationToken);

        var orders = components.GroupBy(x => new
            { x.JwoVoucherId, x.JwoNumber, x.JwoDate, x.Batch, x.JobWorkerName })
            .Select(x => new PendingMaterialIssueOrderRow
            {
                JwoVoucherId = x.Key.JwoVoucherId,
                JwoNumber = x.Key.JwoNumber,
                JwoDate = x.Key.JwoDate,
                Batch = x.Key.Batch,
                JobWorkerName = x.Key.JobWorkerName,
                Components = x.Select(y => y.Component).ToList()
            })
            .OrderByDescending(x => x.JwoDate)
            .ThenByDescending(x => x.JwoVoucherId)
            .ToList();

        return new PendingMaterialIssueReport { Orders = orders };
    }

    public async Task<IReadOnlyList<ClosingStockGroupRow>> GetClosingStockGroupsAsync(
        InventoryReportFilter filter, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var wip = await db.StockGroups.AsNoTracking().Where(x => x.CompanyId == companyContext.CompanyId && x.RootClassification == "WorkInProgress")
            .Select(x => new { x.Id, x.Name }).SingleOrDefaultAsync(cancellationToken);
        var wipKinds = StockMovementSemantics.WorkInProgressKinds;
        var movementQuery = db.StockMovements.AsNoTracking().Where(x => x.Voucher.Status != "Cancelled" && x.CompanyId == companyContext.CompanyId);
        if (filter.StockItemId is not null) movementQuery = movementQuery.Where(x => x.StockItemId == filter.StockItemId);
        if (!string.IsNullOrWhiteSpace(filter.StockItem)) movementQuery = movementQuery.Where(x => EF.Functions.ILike(x.StockItem.Name, Pattern(filter.StockItem)));
        if (filter.StockCategoryId is not null) movementQuery = movementQuery.Where(x => x.StockItem.StockCategoryId == filter.StockCategoryId);
        if (filter.StockGroupId is long selectedGroupId)
            movementQuery = wip is not null && selectedGroupId == wip.Id
                ? movementQuery.Where(x => wipKinds.Contains(x.MovementKind))
                : movementQuery.Where(x => !wipKinds.Contains(x.MovementKind) && x.StockItem.StockGroupId == selectedGroupId);
        if (filter.DateTo is not null) movementQuery = movementQuery.Where(x => x.MovementDate <= filter.DateTo.Value);
        var rows = await movementQuery.GroupBy(x => new
            {
                StockGroupId = wip != null && wipKinds.Contains(x.MovementKind) ? wip.Id : x.StockItem.StockGroupId,
                GroupName = wip != null && wipKinds.Contains(x.MovementKind) ? wip.Name : x.StockItem.StockGroup.Name,
                x.UqcId,
                UqcName = x.Uqc.ShortName
            }).Select(group => new ClosingStockGroupRow
            {
                StockGroupId = group.Key.StockGroupId, StockGroupName = group.Key.GroupName,
                UqcId = group.Key.UqcId, UqcName = group.Key.UqcName,
                ItemCount = group.Select(x => x.StockItemId).Distinct().Count(),
                Quantity = group.Sum(x => x.QuantityChange), Value = group.Sum(x => x.ValueChange)
            }).ToListAsync(cancellationToken);
        rows = rows.Where(x => MatchQuantity(x.Quantity, filter.QuantityFilter))
            .OrderBy(x => x.StockGroupName).ThenBy(x => x.UqcName).ToList();
        return rows;
    }

    public async Task<ClosingStockItemDetail> GetClosingStockItemsAsync(
        long stockGroupId, long uqcId, InventoryReportFilter filter, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var wipId = await db.StockGroups.AsNoTracking().Where(x => x.CompanyId == companyContext.CompanyId && x.RootClassification == "WorkInProgress")
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(cancellationToken);
        var wipKinds = StockMovementSemantics.WorkInProgressKinds;
        var movementQuery = db.StockMovements.AsNoTracking().Where(x => x.CompanyId == companyContext.CompanyId && x.Voucher.Status != "Cancelled" && x.UqcId == uqcId);
        movementQuery = wipId is not null && stockGroupId == wipId.Value
            ? movementQuery.Where(x => wipKinds.Contains(x.MovementKind))
            : movementQuery.Where(x => !wipKinds.Contains(x.MovementKind) && x.StockItem.StockGroupId == stockGroupId);
        if (filter.StockItemId is not null) movementQuery = movementQuery.Where(x => x.StockItemId == filter.StockItemId);
        if (!string.IsNullOrWhiteSpace(filter.StockItem)) movementQuery = movementQuery.Where(x => EF.Functions.ILike(x.StockItem.Name, Pattern(filter.StockItem)));
        if (filter.StockCategoryId is not null) movementQuery = movementQuery.Where(x => x.StockItem.StockCategoryId == filter.StockCategoryId);
        if (filter.DateTo is not null) movementQuery = movementQuery.Where(x => x.MovementDate <= filter.DateTo.Value);
        var itemIds = await movementQuery.Select(x => x.StockItemId).Distinct().ToListAsync(cancellationToken);
        var items = await db.StockItems.AsNoTracking().Where(x => itemIds.Contains(x.Id)).OrderBy(x => x.Name).Select(x => new ClosingStockItemDetailRow
        {
            StockItemId = x.Id, StockItemName = x.Name,
            StockCategoryName = x.StockCategory == null ? string.Empty : x.StockCategory.Name
        }).ToListAsync(cancellationToken);
        var variants = await movementQuery.GroupBy(x => new
            {
                x.StockItemId, x.StockItemVariantId,
                ColourName = x.StockItemVariant == null || x.StockItemVariant.Colour == null ? "N/A" : x.StockItemVariant.Colour.Name,
                SizeName = x.StockItemVariant == null || x.StockItemVariant.Size == null ? "N/A" : x.StockItemVariant.Size.Name
            }).Select(x => new
            {
                x.Key.StockItemId,
                Row = new ClosingStockVariantDetailRow
                {
                    StockItemVariantId = x.Key.StockItemVariantId, ColourName = x.Key.ColourName, SizeName = x.Key.SizeName,
                    Quantity = x.Sum(y => y.QuantityChange), Value = x.Sum(y => y.ValueChange)
                }
            }).ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            item.Variants = variants.Where(x => x.StockItemId == item.StockItemId).Select(x => x.Row).ToList();
            item.Quantity = item.Variants.Sum(x => x.Quantity);
            item.Value = item.Variants.Sum(x => x.Value);
        }
        items = items
            .Where(x => MatchQuantity(x.Quantity, filter.QuantityFilter))
            .OrderBy(x => x.Quantity == 0 ? 1 : 0)
            .ThenBy(x => x.StockItemName)
            .ToList();
        var group = await db.StockGroups.AsNoTracking().Where(x => x.Id == stockGroupId && x.CompanyId == companyContext.CompanyId).Select(x => x.Name).SingleAsync(cancellationToken);
        var uqc = await db.Uqcs.AsNoTracking().Where(x => x.Id == uqcId && x.CompanyId == companyContext.CompanyId).Select(x => x.ShortName).SingleAsync(cancellationToken);
        return new ClosingStockItemDetail { GroupName = group, UqcName = uqc, Items = items };
    }

    public async Task<IReadOnlyList<StockRegisterSummaryRow>> GetStockRegisterSummaryAsync(
        InventoryReportFilter filter, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        if (filter.GodownId is null) return Array.Empty<StockRegisterSummaryRow>();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var wipId = await db.StockGroups.AsNoTracking().Where(x => x.CompanyId == companyContext.CompanyId && x.RootClassification == "WorkInProgress")
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(cancellationToken);
        var wipKinds = StockMovementSemantics.WorkInProgressKinds;
        var movementQuery = db.StockMovements.AsNoTracking().Where(x => x.GodownId == filter.GodownId && x.Voucher.Status != "Cancelled" && x.CompanyId == companyContext.CompanyId);
        if (filter.DateTo is not null) movementQuery = movementQuery.Where(x => x.MovementDate <= filter.DateTo.Value);
        if (wipId is not null && filter.StockGroupId == wipId.Value) movementQuery = movementQuery.Where(x => wipKinds.Contains(x.MovementKind));
        else movementQuery = movementQuery.Where(x => !wipKinds.Contains(x.MovementKind));
        var movementItemIds = await movementQuery.Select(x => x.StockItemId).Distinct().ToListAsync(cancellationToken);
        var itemQuery = db.StockItems.AsNoTracking().Where(x => movementItemIds.Contains(x.Id));
        if (wipId is null || filter.StockGroupId != wipId.Value) itemQuery = ApplyItemFilters(itemQuery, filter);
        else
        {
            if (filter.StockItemId is not null) itemQuery = itemQuery.Where(x => x.Id == filter.StockItemId);
            if (!string.IsNullOrWhiteSpace(filter.StockItem)) itemQuery = itemQuery.Where(x => EF.Functions.ILike(x.Name, Pattern(filter.StockItem)));
            if (filter.StockCategoryId is not null) itemQuery = itemQuery.Where(x => x.StockCategoryId == filter.StockCategoryId);
        }
        var items = await itemQuery.OrderBy(x => x.Name)
            .Select(x => new StockRegisterSummaryRow { StockItemId = x.Id, StockItemName = x.Name, StockCategoryName = x.StockCategory == null ? string.Empty : x.StockCategory.Name, UqcName = x.Uqc.ShortName })
            .ToListAsync(cancellationToken);
        var ids = items.Select(x => x.StockItemId).ToList();
        var godownName = await db.Godowns.AsNoTracking().Where(x => x.Id == filter.GodownId && x.CompanyId == companyContext.CompanyId).Select(x => x.Name).SingleAsync(cancellationToken);
        foreach (var item in items) item.GodownName = godownName;
        var movements = movementQuery.Where(x => ids.Contains(x.StockItemId));
        var values = await movements.GroupBy(x => x.StockItemId).Select(x => new
        {
            StockItemId = x.Key,
            Opening = x.Sum(y => filter.DateFrom != null && y.MovementDate < filter.DateFrom.Value ? y.QuantityChange : 0),
            Inward = x.Sum(y => (filter.DateFrom == null || y.MovementDate >= filter.DateFrom.Value) && y.QuantityChange > 0 ? y.QuantityChange : 0),
            Outward = x.Sum(y => (filter.DateFrom == null || y.MovementDate >= filter.DateFrom.Value) && y.QuantityChange < 0 ? -y.QuantityChange : 0),
            Value = x.Sum(y => y.ValueChange)
        }).ToDictionaryAsync(x => x.StockItemId, cancellationToken);
        foreach (var row in items)
        {
            if (!values.TryGetValue(row.StockItemId, out var value)) continue;
            row.OpeningQuantity = value.Opening; row.InwardQuantity = value.Inward;
            row.OutwardQuantity = value.Outward; row.Value = value.Value;
        }
        return items.Where(x => x.OpeningQuantity != 0 || x.InwardQuantity != 0 || x.OutwardQuantity != 0).ToList();
    }

    public async Task<ClosingStockGodownDetail?> GetClosingStockGodownsAsync(
        long stockItemId, InventoryReportFilter filter, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var header = await db.StockItems.AsNoTracking()
            .Where(x => x.Id == stockItemId && x.CompanyId == companyContext.CompanyId && x.IsActive)
            .Select(x => new ClosingStockGodownDetail
            {
                StockItemId = x.Id,
                StockItemName = x.Name,
                UqcName = x.Uqc.ShortName
            }).SingleOrDefaultAsync(cancellationToken);
        if (header is null) return null;

        var movements = db.StockMovements.AsNoTracking().Where(x =>
            x.StockItemId == stockItemId && x.CompanyId == companyContext.CompanyId &&
            x.Voucher.Status != "Cancelled");
        if (filter.StockGroupId is not null)
        {
            var wipId = await db.StockGroups.AsNoTracking()
                .Where(x => x.CompanyId == companyContext.CompanyId && x.RootClassification == "WorkInProgress")
                .Select(x => (long?)x.Id).SingleOrDefaultAsync(cancellationToken);
            var wipKinds = StockMovementSemantics.WorkInProgressKinds;
            movements = filter.StockGroupId == wipId
                ? movements.Where(x => wipKinds.Contains(x.MovementKind))
                : movements.Where(x => !wipKinds.Contains(x.MovementKind));
        }
        if (filter.DateTo is not null) movements = movements.Where(x => x.MovementDate <= filter.DateTo.Value);
        header.Godowns = await movements.GroupBy(x => new { x.GodownId, x.Godown.Name })
            .Select(x => new ClosingStockGodownRow
            {
                GodownId = x.Key.GodownId,
                GodownName = x.Key.Name,
                Quantity = x.Sum(y => y.QuantityChange),
                Value = x.Sum(y => y.ValueChange)
            }).OrderBy(x => x.GodownName).ToListAsync(cancellationToken);
        header.Godowns = header.Godowns.Where(x => MatchQuantity(x.Quantity, filter.QuantityFilter)).ToList();
        return header;
    }

    public async Task<StockRegisterDetail?> GetStockRegisterDetailAsync(
        long stockItemId, InventoryReportFilter filter, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        if (filter.GodownId is null) return null;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var header = await db.StockItems.AsNoTracking().Where(x => x.Id == stockItemId && x.CompanyId == companyContext.CompanyId)
            .Select(x => new StockRegisterDetail { StockItemId = x.Id, StockItemName = x.Name, UqcName = x.Uqc.ShortName }).SingleOrDefaultAsync(cancellationToken);
        if (header is null) return null;
        header.GodownName = await db.Godowns.AsNoTracking().Where(x => x.Id == filter.GodownId && x.CompanyId == companyContext.CompanyId).Select(x => x.Name).SingleAsync(cancellationToken);
        var all = db.StockMovements.AsNoTracking().Where(x => x.StockItemId == stockItemId && x.GodownId == filter.GodownId &&
            x.Voucher.Status != "Cancelled" && x.CompanyId == companyContext.CompanyId);
        var wipId = await db.StockGroups.AsNoTracking().Where(x => x.CompanyId == companyContext.CompanyId && x.RootClassification == "WorkInProgress")
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(cancellationToken);
        var wipKinds = StockMovementSemantics.WorkInProgressKinds;
        all = wipId is not null && filter.StockGroupId == wipId.Value
            ? all.Where(x => wipKinds.Contains(x.MovementKind))
            : all.Where(x => !wipKinds.Contains(x.MovementKind));
        if (filter.DateTo is not null) all = all.Where(x => x.MovementDate <= filter.DateTo.Value);
        header.OpeningQuantity = filter.DateFrom is null ? 0 : await all.Where(x => x.MovementDate < filter.DateFrom.Value).SumAsync(x => (decimal?)x.QuantityChange, cancellationToken) ?? 0;
        if (filter.DateFrom is not null) all = all.Where(x => x.MovementDate >= filter.DateFrom.Value);
        var movementRows = await all.OrderBy(x => x.MovementDate).ThenBy(x => x.Voucher.SequenceNumber).ThenBy(x => x.Id)
            .Select(x => new StockRegisterMovementRow
            {
                MovementId = x.Id, VoucherId = x.VoucherId,
                VoucherSystemTypeCode = x.Voucher.VoucherType.SystemTypeCode != string.Empty
                    ? x.Voucher.VoucherType.SystemTypeCode
                    : (x.Voucher.VoucherType.ParentVoucherType == null ? string.Empty : x.Voucher.VoucherType.ParentVoucherType.SystemTypeCode),
                Date = x.MovementDate, VoucherType = x.Voucher.VoucherType.Name,
                VoucherNumber = x.Voucher.VoucherNumber,
                PartyOrJobWorker = x.Voucher.PartyLedger == null ? string.Empty : x.Voucher.PartyLedger.Name,
                Godown = x.Godown.Name, Batch = x.Voucher.Batch, Rate = x.Rate,
                Value = x.ValueChange, Inward = x.QuantityChange > 0 ? x.QuantityChange : 0,
                Outward = x.QuantityChange < 0 ? -x.QuantityChange : 0
            }).ToListAsync(cancellationToken);
        var rows = movementRows.GroupBy(x => x.VoucherId).Select(group =>
        {
            var first = group.First();
            var inward = group.Sum(x => x.Inward);
            var outward = group.Sum(x => x.Outward);
            var value = group.Sum(x => x.Value);
            var netQuantity = inward - outward;
            var absoluteQuantity = inward + outward;
            return new StockRegisterMovementRow
            {
                MovementId = group.Min(x => x.MovementId),
                VoucherId = first.VoucherId,
                VoucherSystemTypeCode = first.VoucherSystemTypeCode,
                Date = first.Date,
                VoucherType = first.VoucherType,
                VoucherNumber = first.VoucherNumber,
                PartyOrJobWorker = first.PartyOrJobWorker,
                Godown = first.Godown,
                Batch = first.Batch,
                Rate = netQuantity != 0
                    ? Math.Abs(value / netQuantity)
                    : absoluteQuantity == 0 ? 0 : group.Sum(x => Math.Abs(x.Value)) / absoluteQuantity,
                Value = value,
                Inward = inward,
                Outward = outward
            };
        }).ToList();
        var running = header.OpeningQuantity;
        foreach (var row in rows) { running += row.Inward - row.Outward; row.RunningQuantity = running; }
        header.Movements = rows; header.ClosingQuantity = running;
        return header;
    }

    public async Task<JobWorkerAgingReport> GetJobWorkerAgingAsync(
        JobWorkerAgingFilter filter, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await BuildJobWorkerAgingRowsAsync(db, filter, null, cancellationToken);

        rows = ApplyAgingFilters(rows, filter).ToList();
        rows = rows.OrderByDescending(x => x.Status == "OVERDUE")
            .ThenByDescending(x => x.DaysOverdue ?? -1)
            .ThenByDescending(x => x.DaysOpen)
            .ThenBy(x => x.JobWorkerName)
            .ThenBy(x => x.JwoNumber)
            .ToList();

        // Registers now scroll rather than page - the ceiling only exists to keep a
        // pathological filter.PageSize from requesting an unbounded result set.
        var pageSize = Math.Clamp(filter.PageSize, 10, 5000);
        var totalRows = rows.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalRows / (double)pageSize));
        var page = Math.Clamp(filter.Page, 1, totalPages);
        var visible = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var active = rows.Where(x => x.Status is not ("COMPLETED" or "COMPLETED LATE")).ToList();

        return new JobWorkerAgingReport
        {
            Rows = visible,
            TotalRows = totalRows,
            Page = page,
            PageSize = pageSize,
            ActiveJwoCount = active.Count,
            OverdueJwoCount = rows.Count(x => x.Status == "OVERDUE"),
            MaterialBalanceCount = rows.Count(x => x.HasMaterialBalance),
            OldestOpenDays = active.Count == 0 ? null : active.Max(x => x.DaysOpen),
            OldestMaterialDays = rows.Where(x => x.OldestOutstandingMaterialAge is not null)
                .Select(x => x.OldestOutstandingMaterialAge).DefaultIfEmpty().Max(),
            FinishedGoodPending = rows.SelectMany(x => x.FinishedGoods)
                .GroupBy(x => x.UqcName, StringComparer.OrdinalIgnoreCase)
                .Select(x => new JobWorkerAgingUqcQuantity
                {
                    UqcName = x.Key,
                    PendingOrBalance = x.Sum(y => y.PendingOrBalance)
                }).OrderBy(x => x.UqcName).ToList()
        };
    }

    public async Task<JobWorkerAgingSlabReport> GetJobWorkerAgingSlabsAsync(
        JobWorkerAgingFilter filter, IReadOnlyList<int> cutoffs, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        var normalized = NormalizeAgeCutoffs(cutoffs);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = ApplyAgingFilters(
                await BuildJobWorkerAgingRowsAsync(db, filter, null, cancellationToken), filter)
            .Where(x => x.OldestOutstandingMaterialAge is not null)
            .ToList();

        var bounds = normalized.Select((value, index) => new
        {
            Index = index,
            Minimum = index == 0 ? 0 : normalized[index - 1] + 1,
            Maximum = (int?)value
        }).ToList();
        bounds.Add(new { Index = bounds.Count, Minimum = normalized.LastOrDefault() + 1, Maximum = (int?)null });

        return new JobWorkerAgingSlabReport
        {
            Slabs = bounds.Select(bound =>
            {
                var matching = rows.Where(x => x.OldestOutstandingMaterialAge >= bound.Minimum &&
                    (bound.Maximum is null || x.OldestOutstandingMaterialAge <= bound.Maximum)).ToList();
                return new JobWorkerAgingSlabRow
                {
                    Index = bound.Index,
                    MinimumDays = bound.Minimum,
                    MaximumDays = bound.Maximum,
                    JwoCount = matching.Count,
                    FinishedGoodPending = matching.SelectMany(x => x.FinishedGoods)
                        .GroupBy(x => x.UqcName, StringComparer.OrdinalIgnoreCase)
                        .Select(x => new JobWorkerAgingUqcQuantity
                        {
                            UqcName = x.Key,
                            PendingOrBalance = x.Sum(y => y.PendingOrBalance)
                        }).OrderBy(x => x.UqcName).ToList()
                };
            }).ToList()
        };
    }

    public async Task<JobWorkerPortfolioReport> GetJobWorkerPortfolioAsync(
        JobWorkerExceptionFilter filter, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var agingFilter = new JobWorkerAgingFilter
        {
            AsOnDate = filter.AsOnDate,
            JobWorkerId = filter.JobWorkerId,
            Batch = filter.Batch,
            JwoNumber = filter.JwoNumber,
            FinishedGood = filter.FinishedGood,
            StockGroupId = filter.StockGroupId,
            StockCategoryId = filter.StockCategoryId,
            Status = filter.Status,
            QuickFilter = JobWorkerAgingQuickFilter.All,
            PageSize = 200
        };
        var exceptions = (await BuildJobWorkerAgingRowsAsync(db, agingFilter, null, cancellationToken))
            .Select(ClassifyJobWorkerException)
            .Where(x => !IsCompleted(x.Aging))
            .ToList();

        var rows = exceptions.SelectMany(exceptionRow => exceptionRow.Aging.ResponsibleJobWorkerRows
                .Select(jobWorker => new { JobWorker = jobWorker, Exception = exceptionRow }))
            .GroupBy(x => new { x.JobWorker.JobWorkerId, x.JobWorker.JobWorkerName })
            .Select(group =>
            {
                var values = group.Select(x => x.Exception).ToList();
                return new JobWorkerPortfolioRow
                {
                    JobWorkerId = group.Key.JobWorkerId,
                    JobWorkerName = group.Key.JobWorkerName,
                    ActiveJwoCount = values.Select(x => x.Aging.JwoVoucherId).Distinct().Count(),
                    CriticalCount = values.Count(x => x.Priority == JobWorkerExceptionPriority.Critical),
                    HighCount = values.Count(x => x.Priority == JobWorkerExceptionPriority.High),
                    AttentionCount = values.Count(x => x.Priority == JobWorkerExceptionPriority.Attention),
                    OverdueCount = values.Count(x => x.Aging.DaysOverdue >= 1),
                    MaterialBalanceCount = values.Count(x => x.Aging.HasMaterialBalance),
                    OldMaterialCount = values.Count(x => x.Aging.OldestOutstandingMaterialAge > JobWorkerExceptionThresholds.MaterialAgeHighDays),
                    NoMovementCount = values.Count(x => x.Aging.LastMovementDate is null ||
                        x.Aging.DaysSinceLastMovement >= JobWorkerExceptionThresholds.NoMovementAttentionDays),
                    OldestMaterialAge = values.Where(x => x.Aging.OldestOutstandingMaterialAge is not null)
                        .Select(x => x.Aging.OldestOutstandingMaterialAge).DefaultIfEmpty().Max(),
                    OldestActiveAge = values.Select(x => x.Aging.DaysOpen).DefaultIfEmpty().Max(),
                    FinishedGoodPending = values.SelectMany(x => x.Aging.FinishedGoods)
                        .GroupBy(x => x.UqcName, StringComparer.OrdinalIgnoreCase)
                        .Select(x => new JobWorkerAgingUqcQuantity
                        {
                            UqcName = x.Key,
                            PendingOrBalance = x.Sum(y => y.PendingOrBalance)
                        }).OrderBy(x => x.UqcName).ToList()
                };
            })
            .OrderByDescending(x => x.CriticalCount)
            .ThenByDescending(x => x.HighCount)
            .ThenByDescending(x => x.OverdueCount)
            .ThenByDescending(x => x.NoMovementCount)
            .ThenByDescending(x => x.OldestMaterialAge ?? -1)
            .ThenBy(x => x.JobWorkerName)
            .ToList();
        return new JobWorkerPortfolioReport { Rows = rows };
    }

    public async Task<JobWorkerExceptionReport> GetJobWorkerExceptionBoardAsync(
        JobWorkerExceptionFilter filter, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var agingFilter = new JobWorkerAgingFilter
        {
            AsOnDate = filter.AsOnDate,
            JobWorkerId = filter.JobWorkerId,
            Batch = filter.Batch,
            JwoNumber = filter.JwoNumber,
            FinishedGood = filter.FinishedGood,
            StockGroupId = filter.StockGroupId,
            StockCategoryId = filter.StockCategoryId,
            Status = JobWorkerAgingStatusFilter.All,
            QuickFilter = JobWorkerAgingQuickFilter.All,
            PageSize = 200
        };

        // The exception board deliberately classifies the canonical Phase 1 aging rows.
        // It does not own a second production/material query or calculation engine.
        var rows = (await BuildJobWorkerAgingRowsAsync(db, agingFilter, null, cancellationToken))
            .Select(ClassifyJobWorkerException)
            .AsEnumerable();

        if (filter.Status != JobWorkerAgingStatusFilter.All)
        {
            var status = AgingStatusName(filter.Status);
            rows = rows.Where(x => x.Aging.Status == status);
        }
        if (filter.Priority is not null) rows = rows.Where(x => x.Priority == filter.Priority);

        rows = ApplyExceptionQuickFilter(rows, filter.QuickFilter);
        var filtered = rows.OrderBy(PriorityRank)
            .ThenByDescending(x => x.Aging.DaysOverdue ?? -1)
            .ThenByDescending(x => x.Aging.DaysSinceLastMovement ?? -1)
            .ThenByDescending(x => x.Aging.OldestOutstandingMaterialAge ?? -1)
            .ThenBy(x => x.Aging.JwoDate)
            .ThenBy(x => x.Aging.JobWorkerName)
            .ThenBy(x => x.Aging.JwoNumber)
            .ToList();

        // Registers now scroll rather than page - the ceiling only exists to keep a
        // pathological filter.PageSize from requesting an unbounded result set.
        var pageSize = Math.Clamp(filter.PageSize, 10, 5000);
        var totalRows = filtered.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalRows / (double)pageSize));
        var page = Math.Clamp(filter.Page, 1, totalPages);
        var open = filtered.Where(x => !IsCompleted(x.Aging)).ToList();

        return new JobWorkerExceptionReport
        {
            Rows = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            TotalRows = totalRows,
            Page = page,
            PageSize = pageSize,
            CriticalCount = filtered.Count(x => x.Priority == JobWorkerExceptionPriority.Critical),
            HighCount = filtered.Count(x => x.Priority == JobWorkerExceptionPriority.High),
            AttentionCount = filtered.Count(x => x.Priority == JobWorkerExceptionPriority.Attention),
            OverdueCount = open.Count(x => x.Aging.DaysOverdue >= 1),
            NoMovementCount = open.Count(x => x.Aging.LastMovementDate is null ||
                x.Aging.DaysSinceLastMovement >= JobWorkerExceptionThresholds.NoMovementAttentionDays),
            OldMaterialCount = open.Count(x => x.Aging.OldestOutstandingMaterialAge > JobWorkerExceptionThresholds.MaterialAgeHighDays),
            MaterialBalanceCount = open.Count(x => x.Aging.HasMaterialBalance),
            OpenCount = open.Count
        };
    }

    public async Task<JobWorkerAgingDetail?> GetJobWorkerAgingDetailAsync(
        long jwoVoucherId, DateOnly asOnDate, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var filter = new JobWorkerAgingFilter
        {
            AsOnDate = asOnDate,
            QuickFilter = JobWorkerAgingQuickFilter.All,
            PageSize = 200
        };
        var summary = (await BuildJobWorkerAgingRowsAsync(db, filter, jwoVoucherId, cancellationToken)).SingleOrDefault();
        if (summary is null) return null;

        var finishedGoods = await db.JobWorkOrderFinishedGoods.AsNoTracking()
            .Where(x => x.VoucherId == jwoVoucherId)
            .Select(x => new
            {
                x.Id,
                Name = x.StockItem.Name,
                Uqc = x.StockItem.Uqc.ShortName,
                x.OrderedQuantity
            }).ToListAsync(cancellationToken);
        var fgIds = finishedGoods.Select(x => x.Id).ToList();
        var orderedVariants = await db.JobWorkOrderSizeAllocations.AsNoTracking()
            .Where(x => fgIds.Contains(x.FinishedGoodId))
            .Select(x => new
            {
                x.FinishedGoodId,
                VariantId = x.StockItemVariantId,
                Colour = x.StockItemVariant.Colour == null ? "N/A" : x.StockItemVariant.Colour.Name,
                Size = x.StockItemVariant.Size == null ? "N/A" : x.StockItemVariant.Size.Name,
                x.Quantity
            }).ToListAsync(cancellationToken);
        var receivedVariants = await db.MaterialInFinishedGoodAllocations.AsNoTracking()
            .Where(x => fgIds.Contains(x.FinishedGoodLine.JwoFinishedGoodId) &&
                        x.FinishedGoodLine.Voucher.Status != "Cancelled" &&
                        x.FinishedGoodLine.Voucher.VoucherDate <= asOnDate &&
                        (x.FinishedGoodLine.BomStageId == null || x.FinishedGoodLine.BomStage!.IsFinalStage))
            .GroupBy(x => new { x.FinishedGoodLine.JwoFinishedGoodId, x.StockItemVariantId })
            .Select(x => new { x.Key.JwoFinishedGoodId, VariantId = x.Key.StockItemVariantId, Quantity = x.Sum(y => y.Quantity) })
            .ToListAsync(cancellationToken);
        var unallocatedReceipts = await db.MaterialInFinishedGoods.AsNoTracking()
            .Where(x => fgIds.Contains(x.JwoFinishedGoodId) && x.Voucher.Status != "Cancelled" &&
                        x.Voucher.VoucherDate <= asOnDate && (x.BomStageId == null || x.BomStage!.IsFinalStage))
            .GroupBy(x => x.JwoFinishedGoodId)
            .Select(x => new { Id = x.Key, Quantity = x.Sum(y => y.ReceivedQuantity) })
            .ToListAsync(cancellationToken);

        var fgRows = new List<JobWorkerAgingFinishedGoodRow>();
        foreach (var fg in finishedGoods)
        {
            var variants = orderedVariants.Where(x => x.FinishedGoodId == fg.Id).ToList();
            if (variants.Count == 0)
            {
                fgRows.Add(new JobWorkerAgingFinishedGoodRow
                {
                    FinishedGoodName = fg.Name, UqcName = fg.Uqc, OrderedQuantity = fg.OrderedQuantity,
                    ReceivedQuantity = unallocatedReceipts.FirstOrDefault(x => x.Id == fg.Id)?.Quantity ?? 0
                });
                continue;
            }
            foreach (var variant in variants)
                fgRows.Add(new JobWorkerAgingFinishedGoodRow
                {
                    FinishedGoodName = fg.Name, UqcName = fg.Uqc, ColourName = variant.Colour, SizeName = variant.Size,
                    OrderedQuantity = variant.Quantity,
                    ReceivedQuantity = receivedVariants.FirstOrDefault(x => x.JwoFinishedGoodId == fg.Id && x.VariantId == variant.VariantId)?.Quantity ?? 0
                });
        }

        var components = await db.JobWorkOrderComponents.AsNoTracking()
            .Where(x => x.FinishedGood.VoucherId == jwoVoucherId)
            .Select(x => new { x.Id, Name = x.StockItem.Name, Uqc = x.Uqc.ShortName, x.RequiredQuantity })
            .ToListAsync(cancellationToken);
        var componentIds = components.Select(x => x.Id).ToList();
        var lots = await GetAgingLotsAsync(db, jwoVoucherId, asOnDate, cancellationToken);
        var materialRows = components.Select(x =>
        {
            var componentLots = lots.Where(y => y.JwoComponentId == x.Id).ToList();
            var oldest = componentLots.Where(y => y.Row.OutstandingQuantity > 0).Select(y => y.Row.AgeDays).DefaultIfEmpty().Max();
            return new JobWorkerAgingMaterialRow
            {
                JwoComponentId = x.Id, MaterialName = x.Name, UqcName = x.Uqc,
                RequiredQuantity = x.RequiredQuantity,
                IssuedQuantity = componentLots.Sum(y => y.Row.IssuedQuantity),
                ConsumedQuantity = componentLots.Sum(y => y.Row.ConsumedQuantity),
                OldestOutstandingAge = oldest
            };
        }).ToList();

        var stageAssignments = await db.JobWorkOrderStageAssignments.AsNoTracking()
            .Where(x => x.BomStage.VoucherId == jwoVoucherId)
            .Select(x => new
            {
                StageAssignmentId = x.Id,
                BomStageId = x.BomStageId,
                x.AssignmentVersion,
                x.ExpectedCompletionDate,
                x.BomStage.StageNumber,
                x.BomStage.StagePath,
                x.BomStage.StageName,
                x.BomStage.IsFinalStage,
                x.BomStage.OutputQuantity,
                OutputItemName = x.BomStage.OutputStockItem.Name,
                UqcName = x.BomStage.OutputUqc.ShortName,
                ProcessName = x.BomStage.Process == null ? string.Empty : x.BomStage.Process.Name,
                JobWorkerName = x.JobWorker == null ? string.Empty : x.JobWorker.Name
            }).OrderBy(x => x.StageNumber).ThenBy(x => x.StagePath).ThenBy(x => x.AssignmentVersion)
            .ToListAsync(cancellationToken);
        var assignmentIds = stageAssignments.Select(x => x.StageAssignmentId).ToList();
        var receiptsByAssignment = await db.MaterialInFinishedGoods.AsNoTracking()
            .Where(x => x.StageAssignmentId != null && assignmentIds.Contains(x.StageAssignmentId.Value) &&
                        x.Voucher.Status != "Cancelled" && x.Voucher.VoucherDate <= asOnDate)
            .GroupBy(x => x.StageAssignmentId!.Value)
            .Select(x => new { AssignmentId = x.Key, Quantity = x.Sum(y => y.ReceivedQuantity) })
            .ToDictionaryAsync(x => x.AssignmentId, x => x.Quantity, cancellationToken);
        var stageRows = stageAssignments.Select(x =>
        {
            var stageLots = lots.Where(y => y.Row.StageAssignmentId == x.StageAssignmentId).Select(y => y.Row).ToList();
            var received = receiptsByAssignment.GetValueOrDefault(x.StageAssignmentId);
            return new JobWorkerAgingStageRow
            {
                BomStageId = x.BomStageId, StageAssignmentId = x.StageAssignmentId,
                StageNumber = x.StageNumber, StagePath = x.StagePath, StageName = x.StageName,
                ProcessName = x.ProcessName, JobWorkerName = x.JobWorkerName,
                AssignmentVersion = x.AssignmentVersion, ExpectedCompletionDate = x.ExpectedCompletionDate,
                IsFinalStage = x.IsFinalStage,
                OutputItemName = x.OutputItemName, UqcName = x.UqcName,
                OrderedQuantity = x.OutputQuantity, ReceivedQuantity = received,
                FirstMaterialOutDate = stageLots.Count == 0 ? null : stageLots.Min(y => y.MaterialOutDate),
                LastMaterialOutDate = stageLots.Count == 0 ? null : stageLots.Max(y => y.MaterialOutDate),
                OldestOutstandingMaterialAge = stageLots.Where(y => y.OutstandingQuantity > 0)
                    .Select(y => y.AgeDays).DefaultIfEmpty().Max(),
                Status = received >= x.OutputQuantity ? "COMPLETED"
                    : received > 0 ? "PARTIALLY RECEIVED"
                    : stageLots.Count > 0 ? "MATERIAL SENT"
                    : "NOT SENT"
            };
        }).ToList();

        return new JobWorkerAgingDetail
        {
            Summary = summary,
            FinishedGoods = fgRows,
            Materials = materialRows,
            MaterialLots = lots.Select(x => x.Row).ToList(),
            Stages = stageRows
        };
    }

    private async Task<List<JobWorkerAgingReportRow>> BuildJobWorkerAgingRowsAsync(
        TexTrackDbContext db, JobWorkerAgingFilter filter, long? onlyJwoId, CancellationToken cancellationToken)
    {
        var companyId = companyContext.CompanyId;
        var financialYearId = companyContext.FinancialYearId;
        var asOnDate = filter.AsOnDate;
        var query = db.Vouchers.AsNoTracking().Where(x =>
            x.CompanyId == companyId && x.FinancialYearId == financialYearId &&
            x.Status != "Cancelled" && x.VoucherDate <= asOnDate && x.JobWorkFinishedGoods.Any());
        if (onlyJwoId is not null) query = query.Where(x => x.Id == onlyJwoId.Value);
        if (filter.JobWorkerId is not null) query = query.Where(x =>
            x.JobWorkFinishedGoods.Any(f => f.BomStages.Any(s =>
                s.AssignedJobWorkerId == filter.JobWorkerId ||
                s.AssignmentHistory.Any(a => a.JobWorkerId == filter.JobWorkerId))));
        if (!string.IsNullOrWhiteSpace(filter.Batch)) query = query.Where(x => EF.Functions.ILike(x.Batch, Pattern(filter.Batch)));
        if (!string.IsNullOrWhiteSpace(filter.JwoNumber)) query = query.Where(x => EF.Functions.ILike(x.VoucherNumber, Pattern(filter.JwoNumber)));
        if (!string.IsNullOrWhiteSpace(filter.FinishedGood)) query = query.Where(x => x.JobWorkFinishedGoods.Any(f => EF.Functions.ILike(f.StockItem.Name, Pattern(filter.FinishedGood))));
        if (filter.StockGroupId is not null) query = query.Where(x => x.JobWorkFinishedGoods.Any(f => f.StockItem.StockGroupId == filter.StockGroupId));
        if (filter.StockCategoryId is not null) query = query.Where(x => x.JobWorkFinishedGoods.Any(f => f.StockItem.StockCategoryId == filter.StockCategoryId));

        var headers = await query.Select(x => new
        {
            x.Id, x.VoucherNumber, x.VoucherDate, x.DueDate, x.Batch
        }).ToListAsync(cancellationToken);
        var ids = headers.Select(x => x.Id).ToList();
        if (ids.Count == 0) return new();

        var fgOrdered = await db.JobWorkOrderFinishedGoods.AsNoTracking()
            .Where(x => ids.Contains(x.VoucherId))
            .Select(x => new { x.Id, x.VoucherId, Name = x.StockItem.Name, Uqc = x.StockItem.Uqc.ShortName, x.OrderedQuantity })
            .ToListAsync(cancellationToken);
        var fgIds = fgOrdered.Select(x => x.Id).ToList();
        var stagePositions = await db.JobWorkOrderBomStages.AsNoTracking()
            .Where(x => ids.Contains(x.VoucherId))
            .Select(x => new
            {
                x.Id, x.VoucherId, x.StageNumber, x.StagePath, x.StageName, x.IsFinalStage, x.OutputQuantity,
                ProcessName = x.Process == null ? string.Empty : x.Process.Name,
                OutputItemName = x.OutputStockItem.Name,
                JobWorkers = x.AssignmentHistory.Where(a => a.JobWorkerId != null)
                    .Select(a => new { Id = a.JobWorkerId!.Value, Name = a.JobWorker!.Name }).ToList(),
                ReceivedQuantity = x.MaterialInFinishedGoods
                    .Where(y => y.Voucher.Status != "Cancelled" && y.Voucher.VoucherDate <= asOnDate)
                    .Sum(y => (decimal?)y.ReceivedQuantity) ?? 0
            }).ToListAsync(cancellationToken);
        var componentRequirements = await db.JobWorkOrderComponents.AsNoTracking()
            .Where(x => ids.Contains(x.FinishedGood.VoucherId) && x.RequiredQuantity > 0)
            .Select(x => new
            {
                x.Id,
                JwoVoucherId = x.FinishedGood.VoucherId,
                x.RequiredQuantity
            })
            .ToListAsync(cancellationToken);
        var fgReceived = await db.MaterialInFinishedGoods.AsNoTracking()
            .Where(x => fgIds.Contains(x.JwoFinishedGoodId) && x.Voucher.Status != "Cancelled" &&
                        x.Voucher.CompanyId == companyId && x.Voucher.FinancialYearId == financialYearId &&
                        x.Voucher.VoucherDate <= asOnDate &&
                        (x.BomStageId == null || x.BomStage!.IsFinalStage))
            .GroupBy(x => x.JwoFinishedGoodId)
            .Select(x => new { FinishedGoodId = x.Key, Quantity = x.Sum(y => y.ReceivedQuantity), LastDate = x.Max(y => y.Voucher.VoucherDate) })
            .ToListAsync(cancellationToken);

        var moLines = await db.MaterialOutLines.AsNoTracking()
            .Where(x => ids.Contains(x.JwoVoucherId) && x.Voucher.Status != "Cancelled" &&
                        x.Voucher.CompanyId == companyId && x.Voucher.FinancialYearId == financialYearId &&
                        x.Voucher.VoucherDate <= asOnDate)
            .Select(x => new { x.Id, x.JwoVoucherId, x.JwoComponentId, Uqc = x.Uqc.ShortName, x.IssuedQuantity, Date = x.Voucher.VoucherDate })
            .ToListAsync(cancellationToken);
        var moLineIds = moLines.Select(x => x.Id).ToList();
        var allocations = await db.MaterialInMaterialOutAllocations.AsNoTracking()
            .Where(x => moLineIds.Contains(x.MaterialOutLineId) &&
                        x.MaterialOutLine.Voucher.Status != "Cancelled" && x.ConsumptionLine.Voucher.Status != "Cancelled" &&
                        x.ConsumptionLine.Voucher.CompanyId == companyId && x.ConsumptionLine.Voucher.FinancialYearId == financialYearId &&
                        x.ConsumptionLine.Voucher.VoucherDate <= asOnDate)
            .GroupBy(x => x.MaterialOutLineId)
            .Select(x => new { MaterialOutLineId = x.Key, Quantity = x.Sum(y => y.AllocatedQuantity) })
            .ToListAsync(cancellationToken);
        var miDates = await db.MaterialInDetails.AsNoTracking()
            .Where(x => ids.Contains(x.JwoVoucherId) && x.Voucher.Status != "Cancelled" &&
                        x.Voucher.CompanyId == companyId && x.Voucher.FinancialYearId == financialYearId &&
                        x.Voucher.VoucherDate <= asOnDate)
            .GroupBy(x => x.JwoVoucherId)
            .Select(x => new { JwoVoucherId = x.Key, Date = x.Max(y => y.Voucher.VoucherDate) })
            .ToListAsync(cancellationToken);

        var receivedByFg = fgReceived.ToDictionary(x => x.FinishedGoodId);
        var consumedByMo = allocations.ToDictionary(x => x.MaterialOutLineId, x => x.Quantity);
        var rows = new List<JobWorkerAgingReportRow>(headers.Count);
        foreach (var header in headers)
        {
            var fgs = fgOrdered.Where(x => x.VoucherId == header.Id).ToList();
            var jwoStages = stagePositions.Where(x => x.VoucherId == header.Id)
                .OrderBy(x => x.StageNumber).ThenBy(x => x.StagePath).ToList();
            var responsibleJobWorkerRows = jwoStages.SelectMany(x => x.JobWorkers)
                .GroupBy(x => x.Id).Select(x => new JobWorkerAgingJobWorker
                {
                    JobWorkerId = x.Key,
                    JobWorkerName = x.First().Name
                }).OrderBy(x => x.JobWorkerName).ToList();
            var responsibleJobWorkers = string.Join(", ", responsibleJobWorkerRows.Select(x => x.JobWorkerName));
            var unfinishedStage = jwoStages.FirstOrDefault(x => x.ReceivedQuantity < x.OutputQuantity);
            var fgPositions = fgs.Select(x => new
            {
                x.Uqc,
                x.OrderedQuantity,
                Received = receivedByFg.TryGetValue(x.Id, out var receipt) ? receipt.Quantity : 0,
                LastDate = receivedByFg.TryGetValue(x.Id, out receipt) ? (DateOnly?)receipt.LastDate : null
            }).ToList();
            var complete = fgPositions.Count > 0 && fgPositions.All(x => x.Received >= x.OrderedQuantity);
            var completionDate = complete ? fgPositions.Where(x => x.LastDate is not null).Max(x => x.LastDate) : null;
            var jwoMos = moLines.Where(x => x.JwoVoucherId == header.Id).ToList();
            var requirements = componentRequirements.Where(x => x.JwoVoucherId == header.Id).ToList();
            var issuedByComponent = jwoMos.GroupBy(x => x.JwoComponentId)
                .ToDictionary(x => x.Key, x => x.Sum(y => y.IssuedQuantity));
            var issuedAnything = issuedByComponent.Values.Any(x => x > 0);
            var allRequirementsIssued = requirements.Count > 0 && requirements.All(x =>
                issuedByComponent.TryGetValue(x.Id, out var issued) && issued >= x.RequiredQuantity);
            var lastMo = jwoMos.Count == 0 ? null : (DateOnly?)jwoMos.Max(x => x.Date);
            var lastMi = miDates.FirstOrDefault(x => x.JwoVoucherId == header.Id)?.Date;
            var lastMovement = MaxDate(lastMo, lastMi);
            var outstanding = jwoMos.Select(x => new
            {
                x.Date,
                Quantity = x.IssuedQuantity - (consumedByMo.TryGetValue(x.Id, out var consumed) ? consumed : 0)
            }).Where(x => x.Quantity > 0).ToList();
            var oldestOutstanding = outstanding.Count == 0 ? null : (DateOnly?)outstanding.Min(x => x.Date);
            var row = new JobWorkerAgingReportRow
            {
                JwoVoucherId = header.Id, JwoNumber = header.VoucherNumber, JwoDate = header.VoucherDate,
                DueDate = header.DueDate, Batch = header.Batch,
                JobWorkerName = responsibleJobWorkers,
                ResponsibleJobWorkers = responsibleJobWorkers,
                ResponsibleJobWorkerRows = responsibleJobWorkerRows,
                FinishedGoodNames = string.Join(", ", fgs.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase)),
                CurrentStage = unfinishedStage is null
                    ? (jwoStages.Count == 0 ? "—" : "Completed")
                    : string.IsNullOrWhiteSpace(unfinishedStage.ProcessName) ? unfinishedStage.StageName : unfinishedStage.ProcessName,
                CompletionDate = completionDate,
                MaterialOutStatus = requirements.Count == 0 ? "NOT REQUIRED"
                    : allRequirementsIssued ? "SENT"
                    : issuedAnything ? "PARTIALLY SENT"
                    : "NOT SENT",
                DaysOpen = Math.Max(0, ((completionDate ?? asOnDate).DayNumber - header.VoucherDate.DayNumber)),
                LastMaterialOutDate = lastMo, LastMaterialInDate = lastMi, LastMovementDate = lastMovement,
                DaysSinceLastMovement = lastMovement is null ? null : Math.Max(0, asOnDate.DayNumber - lastMovement.Value.DayNumber),
                OldestOutstandingMaterialDate = oldestOutstanding,
                OldestOutstandingMaterialAge = oldestOutstanding is null ? null : Math.Max(0, asOnDate.DayNumber - oldestOutstanding.Value.DayNumber),
                HasMaterialBalance = outstanding.Count > 0,
                FinishedGoods = fgPositions.GroupBy(x => x.Uqc, StringComparer.OrdinalIgnoreCase).Select(x => new JobWorkerAgingUqcQuantity
                {
                    UqcName = x.Key,
                    OrderedOrRequired = x.Sum(y => y.OrderedQuantity),
                    ReceivedOrIssued = x.Sum(y => y.Received),
                    PendingOrBalance = x.Sum(y => Math.Max(0, y.OrderedQuantity - y.Received))
                }).OrderBy(x => x.UqcName).ToList(),
                Materials = jwoMos.GroupBy(x => x.Uqc, StringComparer.OrdinalIgnoreCase).Select(x => new JobWorkerAgingUqcQuantity
                {
                    UqcName = x.Key,
                    ReceivedOrIssued = x.Sum(y => y.IssuedQuantity),
                    Consumed = x.Sum(y => consumedByMo.TryGetValue(y.Id, out var consumed) ? consumed : 0),
                    PendingOrBalance = x.Sum(y => y.IssuedQuantity - (consumedByMo.TryGetValue(y.Id, out var consumed) ? consumed : 0))
                }).OrderBy(x => x.UqcName).ToList()
            };
            ApplyAgingStatus(row, asOnDate, complete);
            rows.Add(row);
        }
        return rows;
    }

    private async Task<List<(long JwoComponentId, JobWorkerAgingMaterialLotRow Row)>> GetAgingLotsAsync(
        TexTrackDbContext db, long jwoVoucherId, DateOnly asOnDate, CancellationToken cancellationToken)
    {
        var lines = await db.MaterialOutLines.AsNoTracking()
            .Where(x => x.JwoVoucherId == jwoVoucherId && x.JwoVoucher.CompanyId == companyContext.CompanyId &&
                        x.JwoVoucher.FinancialYearId == companyContext.FinancialYearId &&
                        x.Voucher.Status != "Cancelled" && x.Voucher.VoucherDate <= asOnDate)
            .Select(x => new
            {
                x.Id, x.JwoComponentId, x.VoucherId, x.BomStageId, x.StageAssignmentId,
                Number = x.Voucher.VoucherNumber, Date = x.Voucher.VoucherDate,
                Material = x.StockItem.Name, Uqc = x.Uqc.ShortName, x.IssuedQuantity,
                StageName = x.BomStage == null ? string.Empty : x.BomStage.StageName,
                ProcessName = x.BomStage == null || x.BomStage.Process == null ? string.Empty : x.BomStage.Process.Name,
                JobWorkerName = x.StageAssignment == null || x.StageAssignment.JobWorker == null
                    ? (x.Voucher.PartyLedger == null ? string.Empty : x.Voucher.PartyLedger.Name)
                    : x.StageAssignment.JobWorker.Name
            })
            .ToListAsync(cancellationToken);
        var lineIds = lines.Select(x => x.Id).ToList();
        var allocations = await db.MaterialInMaterialOutAllocations.AsNoTracking()
            .Where(x => lineIds.Contains(x.MaterialOutLineId) && x.MaterialOutLine.Voucher.Status != "Cancelled" &&
                        x.ConsumptionLine.Voucher.Status != "Cancelled" && x.ConsumptionLine.Voucher.VoucherDate <= asOnDate)
            .GroupBy(x => x.MaterialOutLineId)
            .Select(x => new { Id = x.Key, Quantity = x.Sum(y => y.AllocatedQuantity) })
            .ToListAsync(cancellationToken);
        var consumed = allocations.ToDictionary(x => x.Id, x => x.Quantity);
        return lines.Select(x =>
        {
            var used = consumed.TryGetValue(x.Id, out var quantity) ? quantity : 0;
            var outstanding = x.IssuedQuantity - used;
            return (x.JwoComponentId, new JobWorkerAgingMaterialLotRow
            {
                MaterialOutVoucherId = x.VoucherId, MaterialOutNumber = x.Number, MaterialOutDate = x.Date,
                BomStageId = x.BomStageId, StageAssignmentId = x.StageAssignmentId,
                StageName = x.StageName, ProcessName = x.ProcessName, JobWorkerName = x.JobWorkerName,
                MaterialName = x.Material, UqcName = x.Uqc, IssuedQuantity = x.IssuedQuantity,
                ConsumedQuantity = used, AgeDays = outstanding > 0 ? Math.Max(0, asOnDate.DayNumber - x.Date.DayNumber) : null
            });
        }).OrderBy(x => x.Item2.MaterialOutDate).ThenBy(x => x.Item2.MaterialOutNumber).ToList();
    }

    private static IEnumerable<JobWorkerAgingReportRow> ApplyAgingFilters(
        IEnumerable<JobWorkerAgingReportRow> rows, JobWorkerAgingFilter filter)
    {
        rows = filter.QuickFilter switch
        {
            JobWorkerAgingQuickFilter.Open => rows.Where(x => x.Status is not ("COMPLETED" or "COMPLETED LATE")),
            JobWorkerAgingQuickFilter.Overdue => rows.Where(x => x.Status == "OVERDUE"),
            JobWorkerAgingQuickFilter.WithMaterialBalance => rows.Where(x => x.HasMaterialBalance),
            JobWorkerAgingQuickFilter.NoMovement => rows.Where(x => x.LastMovementDate is null),
            _ => rows
        };
        if (filter.Status != JobWorkerAgingStatusFilter.All)
        {
            var status = filter.Status switch
            {
                JobWorkerAgingStatusFilter.NotStarted => "NOT STARTED",
                JobWorkerAgingStatusFilter.InProcess => "IN PROCESS",
                JobWorkerAgingStatusFilter.PartiallyReceived => "PARTIALLY RECEIVED",
                JobWorkerAgingStatusFilter.Overdue => "OVERDUE",
                JobWorkerAgingStatusFilter.Completed => "COMPLETED",
                JobWorkerAgingStatusFilter.CompletedLate => "COMPLETED LATE",
                _ => string.Empty
            };
            rows = rows.Where(x => x.Status == status);
        }
        rows = filter.AgeBucket switch
        {
            JobWorkerAgingAgeBucket.Days0To7 => rows.Where(x => x.DaysOpen <= 7),
            JobWorkerAgingAgeBucket.Days8To15 => rows.Where(x => x.DaysOpen is >= 8 and <= 15),
            JobWorkerAgingAgeBucket.Days16To30 => rows.Where(x => x.DaysOpen is >= 16 and <= 30),
            JobWorkerAgingAgeBucket.Days31To60 => rows.Where(x => x.DaysOpen is >= 31 and <= 60),
            JobWorkerAgingAgeBucket.Days61To90 => rows.Where(x => x.DaysOpen is >= 61 and <= 90),
            JobWorkerAgingAgeBucket.DaysOver90 => rows.Where(x => x.DaysOpen > 90),
            _ => rows
        };
        if (filter.MaterialAgeMinimum is not null)
            rows = rows.Where(x => x.OldestOutstandingMaterialAge is not null &&
                x.OldestOutstandingMaterialAge >= filter.MaterialAgeMinimum.Value);
        if (filter.MaterialAgeMaximum is not null)
            rows = rows.Where(x => x.OldestOutstandingMaterialAge is not null &&
                x.OldestOutstandingMaterialAge <= filter.MaterialAgeMaximum.Value);
        return rows;
    }

    private static List<int> NormalizeAgeCutoffs(IReadOnlyList<int> cutoffs)
    {
        var normalized = cutoffs.Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
        if (normalized.Count == 0 || normalized.Count > 3 || normalized.Count != cutoffs.Count(x => x > 0))
            throw new InvalidOperationException("Enter one to three unique, ascending positive age cut-offs.");
        return normalized;
    }

    private static JobWorkerExceptionRow ClassifyJobWorkerException(JobWorkerAgingReportRow aging)
    {
        var fgPending = aging.FinishedGoods.Any(x => x.PendingOrBalance > 0);
        var overdue = aging.DaysOverdue ?? 0;
        var inactivity = aging.DaysSinceLastMovement;
        var materialAge = aging.OldestOutstandingMaterialAge;
        var completed = IsCompleted(aging);
        var reasons = new List<string>();

        if (fgPending && overdue >= 1) reasons.Add($"{overdue}d overdue");
        if (fgPending && inactivity is not null && inactivity >= JobWorkerExceptionThresholds.NoMovementAttentionDays)
            reasons.Add($"No movement {inactivity}d");
        if (aging.HasMaterialBalance && materialAge > JobWorkerExceptionThresholds.MaterialAgeHighDays)
            reasons.Add($"Material outstanding {materialAge}d");
        if (fgPending && aging.DaysRemaining is >= 0 and <= JobWorkerExceptionThresholds.DueSoonDays)
            reasons.Add(aging.DaysRemaining == 0 ? "Due today" : $"Due in {aging.DaysRemaining}d");
        if (aging.HasMaterialBalance && inactivity is not null && inactivity >= JobWorkerExceptionThresholds.NoMovementAttentionDays)
            reasons.Add($"Material lying — no movement {inactivity}d");
        if (fgPending && aging.LastMovementDate is not null && aging.LastMaterialInDate is null)
            reasons.Add("Production active — no Material In yet");
        if (!completed && aging.LastMovementDate is null) reasons.Add("Production not started");
        if (completed) reasons.Add(aging.Status == "COMPLETED LATE" ? "Completed late" : "Completed on time");

        var critical = !completed &&
            (fgPending && overdue > JobWorkerExceptionThresholds.OverdueCriticalDays ||
             fgPending && inactivity > JobWorkerExceptionThresholds.NoMovementCriticalDays ||
             aging.HasMaterialBalance && materialAge > JobWorkerExceptionThresholds.MaterialAgeCriticalDays);
        var high = !completed && !critical &&
            (fgPending && overdue >= 1 ||
             aging.HasMaterialBalance && materialAge > JobWorkerExceptionThresholds.MaterialAgeHighDays ||
             fgPending && inactivity >= JobWorkerExceptionThresholds.NoMovementHighDays);
        var attention = !completed && !critical && !high &&
            (fgPending && aging.DaysRemaining is >= 0 and <= JobWorkerExceptionThresholds.DueSoonDays ||
             aging.HasMaterialBalance && inactivity >= JobWorkerExceptionThresholds.NoMovementAttentionDays ||
             fgPending && aging.LastMovementDate is not null && aging.LastMaterialInDate is null);

        return new JobWorkerExceptionRow
        {
            Aging = aging,
            Priority = critical ? JobWorkerExceptionPriority.Critical
                : high ? JobWorkerExceptionPriority.High
                : attention ? JobWorkerExceptionPriority.Attention
                : JobWorkerExceptionPriority.Normal,
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static IEnumerable<JobWorkerExceptionRow> ApplyExceptionQuickFilter(
        IEnumerable<JobWorkerExceptionRow> rows, JobWorkerExceptionQuickFilter quickFilter)
    {
        return quickFilter switch
        {
            JobWorkerExceptionQuickFilter.ExceptionsOnly => rows.Where(x => !IsCompleted(x.Aging) && x.Priority != JobWorkerExceptionPriority.Normal),
            JobWorkerExceptionQuickFilter.CriticalOnly => rows.Where(x => !IsCompleted(x.Aging) && x.Priority == JobWorkerExceptionPriority.Critical),
            JobWorkerExceptionQuickFilter.OverdueOnly => rows.Where(x => !IsCompleted(x.Aging) && x.Aging.DaysOverdue >= 1),
            JobWorkerExceptionQuickFilter.NoMovement => rows.Where(x => !IsCompleted(x.Aging) &&
                (x.Aging.LastMovementDate is null || x.Aging.DaysSinceLastMovement >= JobWorkerExceptionThresholds.NoMovementAttentionDays)),
            JobWorkerExceptionQuickFilter.OldMaterial => rows.Where(x => !IsCompleted(x.Aging) &&
                x.Aging.HasMaterialBalance && x.Aging.OldestOutstandingMaterialAge > JobWorkerExceptionThresholds.MaterialAgeHighDays),
            JobWorkerExceptionQuickFilter.MaterialBalance => rows.Where(x => !IsCompleted(x.Aging) && x.Aging.HasMaterialBalance),
            JobWorkerExceptionQuickFilter.DueSoon => rows.Where(x => !IsCompleted(x.Aging) &&
                x.Aging.FinishedGoods.Any(y => y.PendingOrBalance > 0) &&
                x.Aging.DaysRemaining is >= 0 and <= JobWorkerExceptionThresholds.DueSoonDays),
            JobWorkerExceptionQuickFilter.AllOpen => rows.Where(x => !IsCompleted(x.Aging)),
            JobWorkerExceptionQuickFilter.CompletedHistory => rows.Where(x => IsCompleted(x.Aging)),
            _ => rows
        };
    }

    private static int PriorityRank(JobWorkerExceptionRow row) => row.Priority switch
    {
        JobWorkerExceptionPriority.Critical => 0,
        JobWorkerExceptionPriority.High => 1,
        JobWorkerExceptionPriority.Attention => 2,
        _ => 3
    };

    private static bool IsCompleted(JobWorkerAgingReportRow row) => row.Status is "COMPLETED" or "COMPLETED LATE";

    private static string AgingStatusName(JobWorkerAgingStatusFilter status) => status switch
    {
        JobWorkerAgingStatusFilter.NotStarted => "NOT STARTED",
        JobWorkerAgingStatusFilter.InProcess => "IN PROCESS",
        JobWorkerAgingStatusFilter.PartiallyReceived => "PARTIALLY RECEIVED",
        JobWorkerAgingStatusFilter.Overdue => "OVERDUE",
        JobWorkerAgingStatusFilter.Completed => "COMPLETED",
        JobWorkerAgingStatusFilter.CompletedLate => "COMPLETED LATE",
        _ => string.Empty
    };

    private static void ApplyAgingStatus(JobWorkerAgingReportRow row, DateOnly asOnDate, bool complete)
    {
        if (complete)
        {
            var late = row.DueDate is not null && row.CompletionDate is not null && row.CompletionDate > row.DueDate;
            row.Status = late ? "COMPLETED LATE" : "COMPLETED";
            row.DuePosition = late
                ? $"Completed {row.CompletionDate!.Value.DayNumber - row.DueDate!.Value.DayNumber} Days Late"
                : "Completed On Time";
            return;
        }
        if (row.DueDate is not null)
        {
            var difference = asOnDate.DayNumber - row.DueDate.Value.DayNumber;
            if (difference > 0) { row.DaysOverdue = difference; row.DuePosition = $"{difference} Days Overdue"; }
            else if (difference == 0) { row.DaysRemaining = 0; row.DuePosition = "Due Today"; }
            else { row.DaysRemaining = -difference; row.DuePosition = $"{-difference} Days Remaining"; }
        }
        else row.DuePosition = "No Due Date";

        var received = row.FinishedGoods.Sum(x => x.ReceivedOrIssued);
        row.Status = row.DaysOverdue is not null ? "OVERDUE"
            : received > 0 ? "PARTIALLY RECEIVED"
            : row.LastMovementDate is not null ? "IN PROCESS"
            : "NOT STARTED";
    }

    private static DateOnly? MaxDate(DateOnly? left, DateOnly? right) =>
        left is null ? right : right is null ? left : left > right ? left : right;

    private IQueryable<TexTrack.Web.Domain.StockItem> ApplyItemFilters(
        IQueryable<TexTrack.Web.Domain.StockItem> query, InventoryReportFilter filter)
    {
        query = query.Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive);
        if (filter.StockItemId is not null) query = query.Where(x => x.Id == filter.StockItemId);
        if (!string.IsNullOrWhiteSpace(filter.StockItem)) query = query.Where(x => EF.Functions.ILike(x.Name, Pattern(filter.StockItem)));
        if (filter.StockGroupId is not null) query = query.Where(x => x.StockGroupId == filter.StockGroupId);
        if (filter.StockCategoryId is not null) query = query.Where(x => x.StockCategoryId == filter.StockCategoryId);
        return query;
    }

    private static bool MatchQuantity(decimal quantity, ClosingQuantityFilter filter) => filter switch
    {
        ClosingQuantityFilter.Positive => quantity > 0,
        ClosingQuantityFilter.Negative => quantity < 0,
        ClosingQuantityFilter.Zero => quantity == 0,
        _ => true
    };

    private static string Pattern(string value) => $"%{value.Trim()}%";
}
