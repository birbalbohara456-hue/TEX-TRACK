namespace TexTrack.Web.Models;

public sealed class TallyXmlDocument
{
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyIdentity { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public List<TallyXmlMaster> Masters { get; set; } = new();
    public List<TallyXmlVoucher> Vouchers { get; set; } = new();
}

public sealed class TallyXmlMaster
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;
    public string Parent { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string BaseUnits { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public int DecimalPlaces { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class TallyXmlVoucher
{
    public string Guid { get; set; } = string.Empty;
    public string RemoteId { get; set; } = string.Empty;
    public string Action { get; set; } = "Create";
    public string VoucherTypeName { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
    public string OrderReferenceNumber { get; set; } = string.Empty;
    public string DestinationGodownName { get; set; } = string.Empty;
    public string SourceGodownName { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    public string PartyLedgerName { get; set; } = string.Empty;
    public string Narration { get; set; } = string.Empty;
    public bool IsCancelled { get; set; }
    public bool IsDeleted { get; set; }
    public string SourceHash { get; set; } = string.Empty;
    public string RawXml { get; set; } = string.Empty;
    public List<TallyXmlInventoryLine> InventoryLines { get; set; } = new();
    public IEnumerable<TallyXmlInventoryLine> AllInventoryLines => InventoryLines.SelectMany(Flatten);
    public string PrimaryOrderNumber => InventoryLines.Select(x => x.OrderNumber)
        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? OrderReferenceNumber;

    private static IEnumerable<TallyXmlInventoryLine> Flatten(TallyXmlInventoryLine line)
    {
        yield return line;
        foreach (var component in line.Components.SelectMany(Flatten)) yield return component;
    }
}

public sealed class TallyXmlInventoryLine
{
    public string Direction { get; set; } = string.Empty;
    public string StockItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string UqcName { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
    public string GodownName { get; set; } = string.Empty;
    public string DestinationGodownName { get; set; } = string.Empty;
    public string BatchName { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public List<TallyXmlInventoryLine> Components { get; set; } = new();
}

public sealed class TallyImportPreview
{
    public string FileName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyIdentity { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public int NewMasterCount { get; set; }
    public List<TallyXmlMaster> Masters { get; set; } = new();
    public List<TallyImportPreviewRow> Rows { get; set; } = new();
    public int NewCount => Rows.Count(x => x.State == "New");
    public int UpdatedCount => Rows.Count(x => x.State == "Updated");
    public int UnchangedCount => Rows.Count(x => x.State == "Unchanged");
    public int CancelledCount => Rows.Count(x => x.State == "Cancelled");
    public int ExceptionCount => Rows.Count(x => x.State == "Exception");
}

public sealed class TallyImportPreviewRow
{
    public string Guid { get; set; } = string.Empty;
    public string VoucherTypeName { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    public string PartyLedgerName { get; set; } = string.Empty;
    public string State { get; set; } = "New";
    public string Message { get; set; } = string.Empty;
    public bool Selected { get; set; } = true;
    public bool UseOthersVariant { get; set; } = true;
    public TallyXmlVoucher Voucher { get; set; } = new();
}

public sealed record TallyApplyResult(bool Success, string Message, long? BatchId = null);

public sealed class TallyExchangeHistoryRow
{
    public long Id { get; set; }
    public DateTimeOffset Date { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public sealed class TallyExceptionRow
{
    public long Id { get; set; }
    public DateTimeOffset Date { get; set; }
    public string VoucherTypeName { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class TallyVariantTaskRow
{
    public long Id { get; set; }
    public DateTimeOffset Date { get; set; }
    public string VoucherTypeName { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public string StockItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string UqcName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed record TallyExportFile(string FileName, string ContentType, byte[] Content);
