using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed class TallyXmlExporter(IDbContextFactory<TexTrackDbContext> contextFactory, CurrentCompanyContext companyContext)
{
    public async Task<TallyExportFile> ExportAsync(string package, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ExportData, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var codes = package == "jwo" ? new[] { "JOB_WORK_OUT_ORDER" } : new[] { "MATERIAL_OUT", "MATERIAL_IN" };
        var vouchers = await db.Vouchers.AsNoTracking()
            .Include(x => x.Company).Include(x => x.VoucherType).Include(x => x.PartyLedger).ThenInclude(x => x!.LedgerGroup)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.StockItem).ThenInclude(x => x.Uqc)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.StockItem).ThenInclude(x => x.StockGroup)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.FinishedGoodsGodown)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.DestinationGodown)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.SizeAllocations).ThenInclude(x => x.StockItemVariant).ThenInclude(x => x.Colour)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.SizeAllocations).ThenInclude(x => x.StockItemVariant).ThenInclude(x => x.Size)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.Components).ThenInclude(x => x.StockItem).ThenInclude(x => x.Uqc)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.Components).ThenInclude(x => x.StockItem).ThenInclude(x => x.StockGroup)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.Components).ThenInclude(x => x.ComponentGodown)
            .Include(x => x.MaterialOutLines).ThenInclude(x => x.StockItem).ThenInclude(x => x.Uqc)
            .Include(x => x.MaterialOutLines).ThenInclude(x => x.StockItem).ThenInclude(x => x.StockGroup)
            .Include(x => x.MaterialOutLines).ThenInclude(x => x.SourceGodown)
            .Include(x => x.MaterialOutLines).ThenInclude(x => x.DestinationGodown)
            .Include(x => x.MaterialOutLines).ThenInclude(x => x.JwoVoucher)
            .Include(x => x.MaterialOutLines).ThenInclude(x => x.JwoFinishedGood).ThenInclude(x => x.StockItem)
            .Include(x => x.MaterialOutDetail)
            .Include(x => x.MaterialInDetail).ThenInclude(x => x!.JwoVoucher)
            .Include(x => x.MaterialInFinishedGoods).ThenInclude(x => x.StockItem).ThenInclude(x => x.Uqc)
            .Include(x => x.MaterialInFinishedGoods).ThenInclude(x => x.StockItem).ThenInclude(x => x.StockGroup)
            .Include(x => x.MaterialInFinishedGoods).ThenInclude(x => x.ReceivingGodown)
            .Include(x => x.MaterialInFinishedGoods).ThenInclude(x => x.Allocations).ThenInclude(x => x.StockItemVariant).ThenInclude(x => x.Colour)
            .Include(x => x.MaterialInFinishedGoods).ThenInclude(x => x.Allocations).ThenInclude(x => x.StockItemVariant).ThenInclude(x => x.Size)
            .Include(x => x.MaterialInConsumptions).ThenInclude(x => x.StockItem).ThenInclude(x => x.Uqc)
            .Include(x => x.MaterialInConsumptions).ThenInclude(x => x.StockItem).ThenInclude(x => x.StockGroup)
            .Include(x => x.MaterialInConsumptions).ThenInclude(x => x.ConsumptionGodown)
            .Where(x => x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId &&
                codes.Contains(x.VoucherType.SystemTypeCode) && x.VoucherDate >= from && x.VoucherDate <= to)
            .OrderBy(x => x.VoucherDate).ThenBy(x => x.SequenceNumber).ToListAsync(cancellationToken);

        var companyName = vouchers.FirstOrDefault()?.Company.Name ?? companyContext.CompanyName;
        var requestData = new XElement("REQUESTDATA");
        AddRequiredMasters(requestData, vouchers);
        var exportedVouchers = vouchers.Select(x => (Voucher: x, Xml: BuildVoucher(x, companyName))).ToList();
        foreach (var exported in exportedVouchers) requestData.Add(new XElement("TALLYMESSAGE", exported.Xml));

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("ENVELOPE",
                new XElement("HEADER", new XElement("TALLYREQUEST", "Import Data")),
                new XElement("BODY", new XElement("IMPORTDATA",
                    new XElement("REQUESTDESC", new XElement("REPORTNAME", "Vouchers"),
                        new XElement("STATICVARIABLES", new XElement("SVCURRENTCOMPANY", companyName))), requestData))));
        var bytes = Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
        var fileName = package == "jwo"
            ? $"TexTrack_JWO_{from:yyyyMMdd}_{to:yyyyMMdd}.xml"
            : $"TexTrack_Material_Out_In_{from:yyyyMMdd}_{to:yyyyMMdd}.xml";
        var now = DateTimeOffset.UtcNow;
        db.TallyExchangeBatches.Add(new TallyExchangeBatch
        {
            CompanyId = companyContext.CompanyId, Direction = "Export", FileName = fileName,
            FileHash = Convert.ToHexString(SHA256.HashData(bytes)), Status = "Completed",
            NewVoucherCount = vouchers.Count, Summary = $"Exported {vouchers.Count} voucher(s) from {from:dd-MMM-yyyy} to {to:dd-MMM-yyyy}.",
            CreatedAtUtc = now, ModifiedAtUtc = now, CreatedBy = companyContext.Actor, ModifiedBy = companyContext.Actor
        });
        var companyLink = await db.TallyCompanyLinks.FirstOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.IsConfirmed, cancellationToken);
        if (companyLink is not null)
        {
            foreach (var exported in exportedVouchers)
            {
                var guid = StableGuid(exported.Voucher.CompanyId, exported.Voucher.Id);
                var record = await db.TallySyncRecords.FirstOrDefaultAsync(x => x.TallyCompanyLinkId == companyLink.Id &&
                    (x.VoucherId == exported.Voucher.Id || x.TallyGuid == guid), cancellationToken);
                if (record is null)
                {
                    record = new TallySyncRecord
                    {
                        CompanyId = companyContext.CompanyId, TallyCompanyLinkId = companyLink.Id,
                        VoucherId = exported.Voucher.Id, TallyGuid = guid, CreatedAtUtc = now, CreatedBy = companyContext.Actor
                    };
                    db.TallySyncRecords.Add(record);
                }
                record.VoucherId = exported.Voucher.Id;
                record.TallyRemoteId = guid;
                record.VoucherTypeName = exported.Voucher.VoucherType.TallyVoucherTypeName;
                record.VoucherNumber = exported.Voucher.VoucherNumber;
                record.SourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(exported.Xml.ToString(SaveOptions.DisableFormatting))));
                record.SyncState = "Exported";
                record.LastExportedAtUtc = now;
                record.ModifiedAtUtc = now;
                record.ModifiedBy = companyContext.Actor;
                record.ConcurrencyToken = Guid.NewGuid().ToString("N");
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return new TallyExportFile(fileName, "application/xml", bytes);
    }

    private static XElement BuildVoucher(Voucher voucher, string companyName)
    {
        var typeName = voucher.VoucherType.TallyVoucherTypeName.Length > 0 ? voucher.VoucherType.TallyVoucherTypeName : voucher.VoucherType.Name;
        var guid = StableGuid(voucher.CompanyId, voucher.Id);
        var element = new XElement("VOUCHER",
            new XAttribute("REMOTEID", guid), new XAttribute("VCHTYPE", typeName),
            new XAttribute("ACTION", voucher.Status == "Cancelled" ? "Cancel" : "Create"),
            new XAttribute("OBJVIEW", voucher.VoucherType.SystemTypeCode == "JOB_WORK_OUT_ORDER" ? "Invoice Voucher View" : "Multi Consumption Voucher View"),
            new XElement("DATE", voucher.VoucherDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)),
            new XElement("GUID", guid), new XElement("VOUCHERTYPENAME", typeName),
            new XElement("PARTYLEDGERNAME", voucher.PartyLedger?.TallyLedgerName.Length > 0 ? voucher.PartyLedger.TallyLedgerName : voucher.PartyLedger?.Name ?? string.Empty),
            new XElement("VOUCHERNUMBER", voucher.VoucherNumber), new XElement("REFERENCE", voucher.ReferenceNumber),
            new XElement("NARRATION", BuildNarration(voucher)), new XElement("PERSISTEDVIEW", voucher.VoucherType.SystemTypeCode == "JOB_WORK_OUT_ORDER" ? "Invoice Voucher View" : "Multi Consumption Voucher View"),
            new XElement("ISINVOICE", "No"), new XElement("ISDELETED", "No"),
            new XElement("ISCANCELLED", voucher.Status == "Cancelled" ? "Yes" : "No"));

        if (voucher.VoucherType.SystemTypeCode == "JOB_WORK_OUT_ORDER")
        {
            foreach (var fg in voucher.JobWorkFinishedGoods.OrderBy(x => x.LineNumber))
                element.Add(JobWorkOrderLine(fg,
                    voucher.ReferenceNumber.Length > 0 ? voucher.ReferenceNumber : voucher.VoucherNumber));
        }
        else if (voucher.VoucherType.SystemTypeCode == "MATERIAL_OUT")
        {
            var first = voucher.MaterialOutLines.OrderBy(x => x.LineNumber).FirstOrDefault();
            var orderNo = first is null ? voucher.MaterialOutDetail?.DisplayedOrderNumber ?? string.Empty : TallyOrderNumber(first.JwoVoucher);
            AddMovementHeader(element, first?.SourceGodown.Name, first?.DestinationGodown.Name,
                orderNo, first?.JwoVoucher.VoucherDate, "BOM Out Order", first?.JwoFinishedGood.StockItem.Name);
            foreach (var line in voucher.MaterialOutLines.OrderBy(x => x.LineNumber))
            {
                element.Add(MovementLine("INVENTORYENTRIESIN.LIST", line.StockItem.Name, line.IssuedQuantity, line.StockItem.Uqc.ShortName,
                    line.Rate, -Math.Abs(line.Amount), line.DestinationGodown.Name, string.Empty, line.SourceGodown.Name,
                    parentItem: line.JwoFinishedGood.StockItem.Name));
                element.Add(MovementLine("INVENTORYENTRIESOUT.LIST", line.StockItem.Name, line.IssuedQuantity, line.StockItem.Uqc.ShortName,
                    line.Rate, Math.Abs(line.Amount), line.SourceGodown.Name, orderNo, line.SourceGodown.Name,
                    orderType: "SubOrder", parentItem: line.JwoFinishedGood.StockItem.Name));
            }
        }
        else
        {
            var finished = voucher.MaterialInFinishedGoods.OrderBy(x => x.LineNumber).FirstOrDefault();
            var consumed = voucher.MaterialInConsumptions.OrderBy(x => x.LineNumber).FirstOrDefault();
            var orderNo = voucher.MaterialInDetail?.JwoVoucher is { } jwo
                ? TallyOrderNumber(jwo)
                : voucher.MaterialInDetail?.DisplayedOrderNumber ?? string.Empty;
            AddMovementHeader(element, consumed?.ConsumptionGodown.Name, finished?.ReceivingGodown.Name,
                orderNo, voucher.MaterialInDetail?.JwoVoucher.VoucherDate, "BOM In Order", finished?.StockItem.Name);
            foreach (var line in voucher.MaterialInFinishedGoods.OrderBy(x => x.LineNumber))
                element.Add(MovementLine("INVENTORYENTRIESIN.LIST", line.StockItem.Name, line.ReceivedQuantity, line.StockItem.Uqc.ShortName,
                    line.Rate, -Math.Abs(line.FinishedGoodsValue), line.ReceivingGodown.Name, orderNo, consumed?.ConsumptionGodown.Name));
        }
        return element;
    }

    private static XElement JobWorkOrderLine(JobWorkOrderFinishedGood finishedGood, string orderNo)
    {
        var components = finishedGood.Components.OrderBy(x => x.LineNumber).ToList();
        var componentValue = components.Sum(ComponentAmount);
        var finishedAmount = components.Count > 0
            ? -Math.Abs(componentValue)
            : finishedGood.XmlAmount != 0
                ? -Math.Abs(finishedGood.XmlAmount)
                : -Math.Abs(finishedGood.OrderedQuantity * finishedGood.XmlRate);
        var finishedRate = finishedGood.OrderedQuantity > 0
            ? Math.Abs(finishedAmount) / finishedGood.OrderedQuantity
            : Math.Abs(finishedGood.XmlRate);
        var batch = JobWorkBatch(finishedGood.FinishedGoodsGodown?.Name,
            finishedGood.DestinationGodown?.Name ?? finishedGood.FinishedGoodsGodown?.Name,
            orderNo, finishedGood.OrderedQuantity, finishedGood.StockItem.Uqc.ShortName,
            finishedAmount, "JobOrder");
        foreach (var component in components)
            batch.Add(JobWorkComponentLine(component, finishedGood.StockItem.Name, orderNo));

        return new XElement("ALLINVENTORYENTRIES.LIST",
            new XElement("STOCKITEMNAME", finishedGood.StockItem.Name),
            new XElement("COMPONENTLISTTYPE", "Track Components"),
            new XElement("ISDEEMEDPOSITIVE", "Yes"),
            new XElement("ISTRACKCOMPONENT", "Yes"),
            new XElement("RATE", Rate(finishedRate, finishedGood.StockItem.Uqc.ShortName)),
            new XElement("AMOUNT", Money(finishedAmount)),
            new XElement("ACTUALQTY", Quantity(finishedGood.OrderedQuantity, finishedGood.StockItem.Uqc.ShortName)),
            new XElement("BILLEDQTY", Quantity(finishedGood.OrderedQuantity, finishedGood.StockItem.Uqc.ShortName)),
            batch);
    }

    private static XElement JobWorkComponentLine(JobWorkOrderComponent component, string parentItem, string orderNo)
    {
        var amount = ComponentAmount(component);
        var batch = JobWorkBatch(component.ComponentGodown?.Name, null, orderNo,
            component.RequiredQuantity, component.StockItem.Uqc.ShortName, amount, "SubOrder", parentItem);
        return new XElement("VOUCHERCOMPONENTLIST.LIST",
            new XElement("STOCKITEMNAME", component.StockItem.Name),
            new XElement("NATUREOFCOMPONENT", "Pending to Issue"),
            new XElement("ISDEEMEDPOSITIVE", "No"),
            new XElement("ISTRACKCOMPONENT", "No"),
            new XElement("RATE", Rate(component.XmlRate, component.StockItem.Uqc.ShortName)),
            new XElement("AMOUNT", Money(amount)),
            new XElement("ACTUALQTY", Quantity(component.RequiredQuantity, component.StockItem.Uqc.ShortName)),
            new XElement("BILLEDQTY", Quantity(component.RequiredQuantity, component.StockItem.Uqc.ShortName)),
            batch);
    }

    private static decimal ComponentAmount(JobWorkOrderComponent component) =>
        component.XmlAmount != 0 ? Math.Abs(component.XmlAmount) : Math.Abs(component.RequiredQuantity * component.XmlRate);

    private static XElement JobWorkBatch(string? godown, string? destinationGodown, string orderNo,
        decimal qty, string uqc, decimal amount, string orderType, string? parentItem = null)
    {
        var batch = new XElement("BATCHALLOCATIONS.LIST");
        if (!string.IsNullOrWhiteSpace(godown)) batch.Add(new XElement("GODOWNNAME", godown));
        if (!string.IsNullOrWhiteSpace(destinationGodown)) batch.Add(new XElement("DESTINATIONGODOWNNAME", destinationGodown));
        batch.Add(new XElement("BATCHNAME", "Primary Batch"), new XElement("ORDERTYPE", orderType));
        if (!string.IsNullOrWhiteSpace(parentItem)) batch.Add(new XElement("PARENTITEM", parentItem));
        batch.Add(new XElement("ORDERNO", orderNo), new XElement("AMOUNT", Money(amount)),
            new XElement("ACTUALQTY", Quantity(qty, uqc)), new XElement("BILLEDQTY", Quantity(qty, uqc)));
        return batch;
    }

    private static void AddMovementHeader(XElement voucher, string? sourceGodown, string? destinationGodown,
        string orderNo, DateOnly? orderDate, string orderType, string? parentItem)
    {
        if (!string.IsNullOrWhiteSpace(destinationGodown))
        {
            voucher.Add(new XElement("DESTINATIONGODOWN", destinationGodown),
                new XElement("VOUCHERDESTINATIONGODOWN", destinationGodown));
        }
        if (!string.IsNullOrWhiteSpace(sourceGodown)) voucher.Add(new XElement("VOUCHERSOURCEGODOWN", sourceGodown));
        if (string.IsNullOrWhiteSpace(orderNo)) return;
        var order = new XElement("INVOICEORDERLIST.LIST");
        if (orderDate is not null) order.Add(new XElement("BASICORDERDATE", orderDate.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));
        order.Add(new XElement("ORDERTYPE", orderType));
        if (!string.IsNullOrWhiteSpace(parentItem)) order.Add(new XElement("PARENTITEM", parentItem));
        order.Add(new XElement("BASICPURCHASEORDERNO", orderNo));
        voucher.Add(order);
    }

    private static XElement MovementLine(string listName, string item, decimal qty, string uqc, decimal rate, decimal amount,
        string godown, string orderNo, string? destinationGodown, string? orderType = null, string? parentItem = null) =>
        new(listName, new XElement("STOCKITEMNAME", item), new XElement("RATE", Rate(rate, uqc)), new XElement("AMOUNT", Money(amount)),
            new XElement("ACTUALQTY", Quantity(qty, uqc)), new XElement("BILLEDQTY", Quantity(qty, uqc)),
            Batch(godown, destinationGodown, orderNo, qty, uqc, rate, amount, orderType, parentItem));

    private static XElement Batch(string? godown, string? destinationGodown, string orderNo, decimal qty, string uqc, decimal rate,
        decimal amount, string? orderType, string? parentItem)
    {
        var batch = new XElement("BATCHALLOCATIONS.LIST", new XElement("GODOWNNAME", godown ?? string.Empty),
            new XElement("BATCHNAME", "Primary Batch"));
        if (!string.IsNullOrWhiteSpace(destinationGodown)) batch.Add(new XElement("DESTINATIONGODOWNNAME", destinationGodown));
        if (!string.IsNullOrWhiteSpace(orderType)) batch.Add(new XElement("ORDERTYPE", orderType));
        if (!string.IsNullOrWhiteSpace(parentItem)) batch.Add(new XElement("PARENTITEM", parentItem));
        batch.Add(new XElement("ORDERNO", orderNo), new XElement("AMOUNT", Money(amount)),
            new XElement("ACTUALQTY", Quantity(qty, uqc)), new XElement("BILLEDQTY", Quantity(qty, uqc)),
            new XElement("RATE", Rate(rate, uqc)));
        return batch;
    }

    private static void AddRequiredMasters(XElement requestData, IReadOnlyList<Voucher> vouchers)
    {
        var messages = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var voucher in vouchers)
        {
            if (voucher.PartyLedger is not null)
                Add(messages, "LEDGER", voucher.PartyLedger.Name, new XElement("PARENT", voucher.PartyLedger.LedgerGroup.Name), new XElement("ISBILLWISEON", "Yes"));
            foreach (var item in GetItems(voucher))
            {
                Add(messages, "UNIT", item.Uqc.ShortName, new XElement("ISSIMPLEUNIT", "Yes"), new XElement("DECIMALPLACES", item.Uqc.DecimalPlaces));
                Add(messages, "STOCKGROUP", item.StockGroup.Name, new XElement("BASEUNITS", item.Uqc.ShortName));
                Add(messages, "STOCKITEM", item.Name, new XElement("PARENT", item.StockGroup.Name), new XElement("BASEUNITS", item.Uqc.ShortName));
            }
            foreach (var godown in GetGodowns(voucher)) Add(messages, "GODOWN", godown, new XElement("ISINTERNAL", "No"));
        }
        foreach (var message in messages.Values) requestData.Add(new XElement("TALLYMESSAGE", message));
    }

    private static IEnumerable<StockItem> GetItems(Voucher voucher) => voucher.JobWorkFinishedGoods.Select(x => x.StockItem)
        .Concat(voucher.JobWorkFinishedGoods.SelectMany(x => x.Components).Select(x => x.StockItem))
        .Concat(voucher.MaterialOutLines.Select(x => x.StockItem)).Concat(voucher.MaterialInFinishedGoods.Select(x => x.StockItem))
        .Concat(voucher.MaterialInConsumptions.Select(x => x.StockItem)).DistinctBy(x => x.Id);

    private static IEnumerable<string> GetGodowns(Voucher voucher) => voucher.JobWorkFinishedGoods.SelectMany(x => new[] { x.FinishedGoodsGodown?.Name, x.DestinationGodown?.Name })
        .Concat(voucher.JobWorkFinishedGoods.SelectMany(x => x.Components).Select(x => x.ComponentGodown?.Name))
        .Concat(voucher.MaterialOutLines.SelectMany(x => new[] { x.SourceGodown.Name, x.DestinationGodown.Name }))
        .Concat(voucher.MaterialInFinishedGoods.Select(x => x.ReceivingGodown.Name)).Concat(voucher.MaterialInConsumptions.Select(x => x.ConsumptionGodown.Name))
        .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase);

    private static void Add(Dictionary<string, XElement> messages, string type, string name, params object[] content)
    {
        var key = $"{type}|{name}";
        if (!messages.ContainsKey(key)) messages[key] = new XElement(type, new XAttribute("NAME", name), new XElement("NAME", name), content);
    }

    private static string BuildNarration(Voucher voucher)
    {
        var details = voucher.JobWorkFinishedGoods.SelectMany(x => x.SizeAllocations.Select(a => $"{x.StockItem.Name}: {a.StockItemVariant.Colour?.Name ?? "Others"}/{a.StockItemVariant.Size?.Name ?? "Others"} {a.Quantity:0.####}"))
            .Concat(voucher.MaterialInFinishedGoods.SelectMany(x => x.Allocations.Select(a => $"{x.StockItem.Name}: {a.StockItemVariant.Colour?.Name ?? "Others"}/{a.StockItemVariant.Size?.Name ?? "Others"} {a.Quantity:0.####}")))
            .Concat(voucher.MaterialInConsumptions.Select(x => $"Consumed {x.StockItem.Name}: {x.ConsumedQuantity:0.####} {x.StockItem.Uqc.ShortName} @ {x.Rate:0.####}"))
            .ToList();
        return details.Count == 0 ? voucher.Narration : $"{voucher.Narration}{(voucher.Narration.Length > 0 ? " | " : "")}TexTrack variants: {string.Join("; ", details)}";
    }

    private static string TallyOrderNumber(Voucher jwo) =>
        string.IsNullOrWhiteSpace(jwo.ReferenceNumber) ? jwo.VoucherNumber : jwo.ReferenceNumber;

    private static string StableGuid(long companyId, long voucherId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"TexTrack|{companyId}|{voucherId}"));
        return new Guid(hash[..16]).ToString() + "-" + voucherId.ToString("x8", CultureInfo.InvariantCulture);
    }
    private static string Quantity(decimal value, string uqc) => $" {value:0.####} {uqc}";
    private static string Rate(decimal value, string uqc) => $"{value:0.####}/{uqc}";
    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
