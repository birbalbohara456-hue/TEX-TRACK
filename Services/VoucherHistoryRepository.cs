using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed class VoucherHistoryRepository(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext)
{
    public async Task<IReadOnlyList<VoucherHistoryMonthRow>> GetMonthsAsync(
        string kind, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await BaseQuery(db, kind)
            .GroupBy(x => new { x.VoucherDate.Year, x.VoucherDate.Month })
            .Select(x => new
            {
                x.Key.Year,
                x.Key.Month,
                Count = x.Count(),
                ActiveCount = x.Count(y => y.Status != "Cancelled"),
                CancelledCount = x.Count(y => y.Status == "Cancelled")
            })
            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
            .ToListAsync(cancellationToken);
        return rows.Select(x => new VoucherHistoryMonthRow(
            x.Year, x.Month, x.Count, x.ActiveCount, x.CancelledCount)).ToList();
    }

    public async Task<VoucherHistoryPage> GetPageAsync(
        string kind, DateOnly from, DateOnly to, string searchText,
        int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = BaseQuery(db, kind).Where(x => x.VoucherDate >= from && x.VoucherDate <= to);
        var term = searchText.Trim();
        if (term.Length != 0)
        {
            var pattern = $"%{term}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.VoucherNumber, pattern) ||
                EF.Functions.ILike(x.ReferenceNumber, pattern) ||
                EF.Functions.ILike(x.Batch, pattern) ||
                EF.Functions.ILike(x.Status, pattern) ||
                (x.PartyLedger != null && EF.Functions.ILike(x.PartyLedger.Name, pattern)) ||
                x.JobWorkFinishedGoods.Any(y => EF.Functions.ILike(y.StockItem.Name, pattern)) ||
                x.MasterJobOrderFinishedGoods.Any(y => EF.Functions.ILike(y.StockItem.Name, pattern)) ||
                x.MaterialOutLines.Any(y => EF.Functions.ILike(y.StockItem.Name, pattern)) ||
                x.MaterialInFinishedGoods.Any(y => EF.Functions.ILike(y.StockItem.Name, pattern)) ||
                (x.MaterialOutDetail != null && EF.Functions.ILike(x.MaterialOutDetail.DisplayedOrderNumber, pattern)) ||
                (x.MaterialInDetail != null && EF.Functions.ILike(x.MaterialInDetail.DisplayedOrderNumber, pattern)) ||
                x.PurchaseOrderLines.Any(y => EF.Functions.ILike(y.StockItem.Name, pattern)) ||
                x.InventoryInwardLines.Any(y => EF.Functions.ILike(y.StockItem.Name, pattern)) ||
                x.PurchaseReturnLines.Any(y => EF.Functions.ILike(y.StockItem.Name, pattern)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var lastPage = Math.Max(0, (totalCount - 1) / pageSize);
        pageIndex = Math.Clamp(pageIndex, 0, lastPage);
        var page = await query.OrderByDescending(x => x.VoucherDate).ThenByDescending(x => x.Id)
            .Skip(pageIndex * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.Id, x.VoucherNumber, x.VoucherDate,
                JobWorker = x.PartyLedger != null ? x.PartyLedger.Name : string.Empty,
                x.ReferenceNumber, x.Batch, x.Status,
                OrderNumber = kind == "material-out" && x.MaterialOutDetail != null
                    ? x.MaterialOutDetail.DisplayedOrderNumber
                    : kind == "material-in" && x.MaterialInDetail != null
                        ? x.MaterialInDetail.DisplayedOrderNumber : x.ReferenceNumber,
                Details = kind == "material-out" && x.MaterialOutDetail != null
                    ? x.MaterialOutDetail.DestinationGodown.Name
                    : kind == "material-in" && x.MaterialInDetail != null
                        ? x.MaterialInDetail.ConsumptionGodown.Name + " -> " + x.MaterialInDetail.ReceivingGodown.Name
                        : string.Empty,
                Quantity = kind == "master-job-orders"
                    ? x.MasterJobOrderFinishedGoods.Sum(y => (decimal?)y.OrderedQuantity) ?? 0
                    : kind == "job-out-orders"
                        ? x.JobWorkFinishedGoods.Sum(y => (decimal?)y.OrderedQuantity) ?? 0
                        : kind == "material-out"
                            ? x.MaterialOutLines.Sum(y => (decimal?)y.IssuedQuantity) ?? 0
                            : kind == "material-in"
                                ? x.MaterialInFinishedGoods.Sum(y => (decimal?)y.ReceivedQuantity) ?? 0
                                : kind == "purchase-order"
                                    ? x.PurchaseOrderLines.Sum(y => (decimal?)y.OrderedQuantity) ?? 0
                                    : kind == "purchase"
                                        ? x.InventoryInwardLines.Sum(y => (decimal?)y.Quantity) ?? 0
                                        : kind == "purchase-return"
                                            ? x.PurchaseReturnLines.Sum(y => (decimal?)y.Quantity) ?? 0
                                            : 0,
                Amount = kind == "material-out"
                    ? x.MaterialOutLines.Sum(y => (decimal?)y.Amount) ?? 0
                    : kind == "material-in" && x.MaterialInDetail != null
                        ? x.MaterialInDetail.TotalFinishedGoodsValue
                        : kind == "purchase-order"
                            ? x.PurchaseOrderLines.Sum(y => (decimal?)y.Amount) ?? 0
                            : kind == "purchase"
                                ? x.InventoryInwardLines.Sum(y => (decimal?)y.Amount) ?? 0
                                : kind == "purchase-return"
                                    ? x.PurchaseReturnLines.Sum(y => (decimal?)y.Amount) ?? 0
                                    : 0
            })
            .ToListAsync(cancellationToken);

        var pageIds = page.Select(x => x.Id).ToList();
        var summaries = kind switch
        {
            "master-job-orders" => await db.MasterJobOrderFinishedGoods.AsNoTracking()
                .Where(x => pageIds.Contains(x.VoucherId)).OrderBy(x => x.LineNumber)
                .Select(x => new { x.VoucherId, x.StockItem.Name }).ToListAsync(cancellationToken),
            "job-out-orders" => await db.JobWorkOrderFinishedGoods.AsNoTracking()
                .Where(x => pageIds.Contains(x.VoucherId)).OrderBy(x => x.LineNumber)
                .Select(x => new { x.VoucherId, x.StockItem.Name }).ToListAsync(cancellationToken),
            "purchase-order" => await db.PurchaseOrderLines.AsNoTracking()
                .Where(x => pageIds.Contains(x.VoucherId)).OrderBy(x => x.LineNumber)
                .Select(x => new { x.VoucherId, x.StockItem.Name }).ToListAsync(cancellationToken),
            "purchase" => await db.InventoryInwardLines.AsNoTracking()
                .Where(x => pageIds.Contains(x.VoucherId)).OrderBy(x => x.LineNumber)
                .Select(x => new { x.VoucherId, x.StockItem.Name }).ToListAsync(cancellationToken),
            "purchase-return" => await db.PurchaseReturnLines.AsNoTracking()
                .Where(x => pageIds.Contains(x.VoucherId)).OrderBy(x => x.LineNumber)
                .Select(x => new { x.VoucherId, x.StockItem.Name }).ToListAsync(cancellationToken),
            _ => []
        };
        var detailMap = summaries.GroupBy(x => x.VoucherId).ToDictionary(
            x => x.Key,
            x => string.Join(", ", x.Select(y => y.Name).Distinct(StringComparer.OrdinalIgnoreCase)));

        var rows = page.Select(x => new VoucherHistoryListRow(
            x.Id, x.VoucherNumber, x.VoucherDate, x.JobWorker, x.OrderNumber, x.Batch,
            detailMap.GetValueOrDefault(x.Id, x.Details), x.Quantity, x.Amount, x.Status)).ToList();

        var active = query.Where(x => x.Status != "Cancelled");
        var activeCount = await active.CountAsync(cancellationToken);
        decimal activeAmount = kind switch
        {
            "material-out" => await db.MaterialOutLines.AsNoTracking()
                .Where(x => active.Select(v => v.Id).Contains(x.VoucherId))
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0,
            "material-in" => await db.MaterialInDetails.AsNoTracking()
                .Where(x => active.Select(v => v.Id).Contains(x.VoucherId))
                .SumAsync(x => (decimal?)x.TotalFinishedGoodsValue, cancellationToken) ?? 0,
            "purchase-order" => await db.PurchaseOrderLines.AsNoTracking()
                .Where(x => active.Select(v => v.Id).Contains(x.VoucherId))
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0,
            "purchase" => await db.InventoryInwardLines.AsNoTracking()
                .Where(x => active.Select(v => v.Id).Contains(x.VoucherId))
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0,
            "purchase-return" => await db.PurchaseReturnLines.AsNoTracking()
                .Where(x => active.Select(v => v.Id).Contains(x.VoucherId))
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0,
            _ => 0
        };

        var quantities = kind switch
        {
            "master-job-orders" => await db.MasterJobOrderFinishedGoods.AsNoTracking()
                .Where(x => active.Select(v => v.Id).Contains(x.VoucherId))
                .GroupBy(x => x.StockItem.Uqc.ShortName)
                .Select(x => new { Uqc = x.Key, Qty = x.Sum(y => y.OrderedQuantity) }).ToListAsync(cancellationToken),
            "job-out-orders" => await db.JobWorkOrderFinishedGoods.AsNoTracking()
                .Where(x => active.Select(v => v.Id).Contains(x.VoucherId))
                .GroupBy(x => x.StockItem.Uqc.ShortName)
                .Select(x => new { Uqc = x.Key, Qty = x.Sum(y => y.OrderedQuantity) }).ToListAsync(cancellationToken),
            "material-out" => await db.MaterialOutLines.AsNoTracking()
                .Where(x => active.Select(v => v.Id).Contains(x.VoucherId))
                .GroupBy(x => x.Uqc.ShortName)
                .Select(x => new { Uqc = x.Key, Qty = x.Sum(y => y.IssuedQuantity) }).ToListAsync(cancellationToken),
            "material-in" => await db.MaterialInFinishedGoods.AsNoTracking()
                .Where(x => active.Select(v => v.Id).Contains(x.VoucherId))
                .GroupBy(x => x.StockItem.Uqc.ShortName)
                .Select(x => new { Uqc = x.Key, Qty = x.Sum(y => y.ReceivedQuantity) }).ToListAsync(cancellationToken),
            "purchase-order" => await db.PurchaseOrderLines.AsNoTracking()
                .Where(x => active.Select(v => v.Id).Contains(x.VoucherId))
                .GroupBy(x => x.Uqc.ShortName)
                .Select(x => new { Uqc = x.Key, Qty = x.Sum(y => y.OrderedQuantity) }).ToListAsync(cancellationToken),
            "purchase" => await db.InventoryInwardLines.AsNoTracking()
                .Where(x => active.Select(v => v.Id).Contains(x.VoucherId))
                .GroupBy(x => x.Uqc.ShortName)
                .Select(x => new { Uqc = x.Key, Qty = x.Sum(y => y.Quantity) }).ToListAsync(cancellationToken),
            "purchase-return" => await db.PurchaseReturnLines.AsNoTracking()
                .Where(x => active.Select(v => v.Id).Contains(x.VoucherId))
                .GroupBy(x => x.Uqc.ShortName)
                .Select(x => new { Uqc = x.Key, Qty = x.Sum(y => y.Quantity) }).ToListAsync(cancellationToken),
            _ => []
        };
        var quantityText = quantities.Count == 0 ? "Total -" :
            "Total " + string.Join("  ", quantities.OrderBy(x => x.Uqc).Select(x => $"{x.Uqc.ToUpperInvariant()}={x.Qty:0.####}"));
        return new VoucherHistoryPage(rows, totalCount, activeCount, activeAmount, quantityText);
    }

    private IQueryable<Voucher> BaseQuery(TexTrackDbContext db, string kind)
    {
        var code = kind switch
        {
            "master-job-orders" => "MASTER_JOB_ORDER",
            "job-out-orders" => "JOB_WORK_OUT_ORDER",
            "material-out" => "MATERIAL_OUT",
            "material-in" => "MATERIAL_IN",
            "purchase-order" => "PURCHASE_ORDER",
            "purchase" => "PURCHASE",
            "purchase-return" => "PURCHASE_RETURN",
            _ => "__UNSUPPORTED__"
        };
        return db.Vouchers.AsNoTracking().Where(x =>
            x.CompanyId == companyContext.CompanyId &&
            x.FinancialYearId == companyContext.FinancialYearId &&
            (x.VoucherType.SystemTypeCode == code ||
             x.VoucherType.ParentVoucherType != null && x.VoucherType.ParentVoucherType.SystemTypeCode == code));
    }
}
