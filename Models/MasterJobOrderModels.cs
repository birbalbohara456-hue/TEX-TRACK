namespace TexTrack.Web.Models;

public sealed class MasterJobOrderListItem
{
    public long Id { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
    public DateOnly? DueDate { get; set; }
    public string FinishedGoodsSummary { get; set; } = string.Empty;
    public decimal TotalQuantity { get; set; }
    public string Status { get; set; } = "Open";
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class MasterJobOrderEditModel
{
    public long Id { get; set; }
    public int SequenceNumber { get; set; }
    public long VoucherTypeId { get; set; }
    public string VoucherTypeText { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public long? PartyLedgerId { get; set; }
    public string PartyText { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
    public DateOnly? DueDate { get; set; }
    public string Narration { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public string ConcurrencyToken { get; set; } = string.Empty;
    public List<long> LinkedJobOrderIds { get; set; } = new();
    public List<MasterJobOrderLinkedJwoModel> LinkedJobOrders { get; set; } = new();
    public List<MasterJobOrderFinishedGoodEditModel> FinishedGoods { get; set; } = new();
}

public sealed class MasterJobOrderFinishedGoodEditModel
{
    public string ClientKey { get; set; } = Guid.NewGuid().ToString("N");
    public long Id { get; set; }
    public long StockItemId { get; set; }
    public string StockItemText { get; set; } = string.Empty;
    public string UqcShortName { get; set; } = string.Empty;
    public List<MasterJobOrderColourEditModel> Colours { get; set; } = new();
    public decimal TotalQuantity => Colours.Sum(x => x.TotalQuantity);
}

public sealed class MasterJobOrderColourEditModel
{
    public string ClientKey { get; set; } = Guid.NewGuid().ToString("N");
    public long? ColourId { get; set; }
    public string ColourText { get; set; } = string.Empty;
    public List<MasterJobOrderSizeEditModel> Sizes { get; set; } = new();
    public decimal TotalQuantity => Sizes.Sum(x => x.Quantity);
}

public sealed class MasterJobOrderSizeEditModel
{
    public long Id { get; set; }
    public long StockItemVariantId { get; set; }
    public long? SizeId { get; set; }
    public string SizeName { get; set; } = "Quantity";
    public int DisplayOrder { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class MasterJobOrderLookupData
{
    public bool AllowManualNumbering { get; set; }
    public List<JobWorkVoucherTypeLookup> VoucherTypes { get; set; } = new();
    public List<LookupItem> Parties { get; set; } = new();
    public List<JobWorkStockItemLookup> FinishedGoods { get; set; } = new();
    public List<MasterJobOrderLinkedJwoModel> JobWorkOrders { get; set; } = new();
}

public sealed class MasterJobOrderLinkedJwoModel
{
    public long Id { get; set; }
    public long? MasterJobOrderId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    public string JobWorkerName { get; set; } = string.Empty;
    public string Batch { get; set; } = string.Empty;
    public string FinishedGoodsSummary { get; set; } = string.Empty;
    public decimal TotalQuantity { get; set; }
    public List<MasterJobOrderFinishedGoodEditModel> FinishedGoods { get; set; } = new();
}

public sealed class MasterJobOrderLinkLookup
{
    public long Id { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public string PartyName { get; set; } = string.Empty;
    public string FinishedGoodsSummary { get; set; } = string.Empty;
}
