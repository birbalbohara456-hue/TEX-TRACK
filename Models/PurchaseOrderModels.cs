namespace TexTrack.Web.Models;

public sealed class PurchaseOrderLineInput
{
    public long StockItemId { get; set; }
    public long StockItemVariantId { get; set; }
    public long UqcId { get; set; }
    public long? GodownId { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal Rate { get; set; }
    public decimal? Amount { get; set; }
    public DateOnly? ExpectedDeliveryDate { get; set; }
}

public sealed class PurchaseOrderSaveRequest
{
    public long VoucherId { get; set; }
    public long VoucherTypeId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public long SupplierLedgerId { get; set; }
    public string Narration { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = string.Empty;
    public List<PurchaseOrderLineInput> Lines { get; set; } = [];
}

public sealed record PurchaseOrderSaveResult(long VoucherId, string VoucherNumber);

public sealed class PurchaseOrderEditData
{
    public long VoucherId { get; init; }
    public long VoucherTypeId { get; init; }
    public string VoucherTypeName { get; init; } = string.Empty;
    public string VoucherNumber { get; init; } = string.Empty;
    public DateOnly VoucherDate { get; init; }
    public string ReferenceNumber { get; init; } = string.Empty;
    public long SupplierLedgerId { get; init; }
    public string Narration { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ConcurrencyToken { get; init; } = string.Empty;
    public IReadOnlyList<PurchaseOrderLineInput> Lines { get; init; } = [];
}

public sealed class PurchaseOrderListItem
{
    public long VoucherId { get; init; }
    public string VoucherNumber { get; init; } = string.Empty;
    public DateOnly VoucherDate { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ConcurrencyToken { get; init; } = string.Empty;
}

/// <summary>
/// One still-pending Purchase Order line, for the Purchase-side picker. Pending is
/// always computed live (OrderedQuantity minus the sum of non-cancelled Purchase
/// receipts against this line) - never a stored snapshot.
/// </summary>
public sealed class PurchaseOrderPendingLine
{
    public long PurchaseOrderLineId { get; init; }
    public long PurchaseOrderVoucherId { get; init; }
    public string PurchaseOrderVoucherNumber { get; init; } = string.Empty;
    public DateOnly PurchaseOrderVoucherDate { get; init; }
    public long StockItemId { get; init; }
    public string StockItemName { get; init; } = string.Empty;
    public long StockItemVariantId { get; init; }
    public long UqcId { get; init; }
    public string UqcName { get; init; } = string.Empty;
    public long? GodownId { get; init; }
    public decimal OrderedQuantity { get; init; }
    public decimal ReceivedQuantity { get; init; }
    public decimal PendingQuantity { get; init; }
    public decimal Rate { get; init; }
}

/// <summary>
/// One outstanding Purchase Order for a supplier's PO-reference picker -
/// grouped from the same live pending-line data GetPendingLinesAsync already
/// computes, not a separate query.
/// </summary>
public sealed class PurchaseOrderOutstandingSummary
{
    public long PurchaseOrderVoucherId { get; init; }
    public string PurchaseOrderVoucherNumber { get; init; } = string.Empty;
    public DateOnly VoucherDate { get; init; }
    public int PendingLineCount { get; init; }
}

/// <summary>
/// Full hydration of one Purchase Order's still-pending lines, for the
/// PO-reference "select and prefill everything" flow (mirrors Material In's
/// SelectOrder/PopulateOrderRows teardown-and-rebuild).
/// </summary>
public sealed class PurchaseOrderHydrationResult
{
    public long PurchaseOrderVoucherId { get; init; }
    public string PurchaseOrderVoucherNumber { get; init; } = string.Empty;
    public IReadOnlyList<PurchaseOrderPendingLine> Lines { get; init; } = [];
}
