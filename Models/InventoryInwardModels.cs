namespace TexTrack.Web.Models;

public enum InventoryInwardVoucherKind
{
    Purchase,
    OpeningStock
}

public sealed class InventoryInwardLineInput
{
    public long StockItemId { get; set; }
    public long StockItemVariantId { get; set; }
    public long UqcId { get; set; }
    public long GodownId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal? Amount { get; set; }

    /// <summary>
    /// Optional reference to the Purchase Order line this Purchase line receives
    /// against. Only meaningful when Kind == Purchase. Freely re-editable on every
    /// alteration - re-validated fresh against the (possibly new) PO line's live
    /// pending quantity each save, never locked once set.
    /// </summary>
    public long? PurchaseOrderLineId { get; set; }
}

public sealed class InventoryInwardSaveRequest
{
    public long VoucherId { get; set; }
    public InventoryInwardVoucherKind Kind { get; set; }
    public long VoucherTypeId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public long? SupplierLedgerId { get; set; }
    public long? OpeningStockItemId { get; set; }
    public string Narration { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = string.Empty;
    public List<InventoryInwardLineInput> Lines { get; set; } = [];
}

public sealed record InventoryInwardSaveResult(long VoucherId, string VoucherNumber);

public sealed class PurchaseListItem
{
    public long VoucherId { get; init; }
    public string VoucherNumber { get; init; } = string.Empty;
    public DateOnly VoucherDate { get; init; }
    public string ReferenceNumber { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ConcurrencyToken { get; init; } = string.Empty;
}

/// <summary>
/// One existing Purchase for a supplier's optional reference picker on
/// Purchase Return - unlike Purchase's PO-reference picker, this lists every
/// non-cancelled Purchase (no "pending" concept applies, since Purchase
/// Return is never capped against it - the reference is prefill convenience
/// only).
/// </summary>
public sealed class PurchaseOutstandingSummary
{
    public long PurchaseVoucherId { get; init; }
    public string PurchaseVoucherNumber { get; init; } = string.Empty;
    public DateOnly VoucherDate { get; init; }
    public int LineCount { get; init; }
}

public sealed class PurchaseHydrationResult
{
    public long PurchaseVoucherId { get; init; }
    public string PurchaseVoucherNumber { get; init; } = string.Empty;
    public IReadOnlyList<PurchaseHydrationLine> Lines { get; init; } = [];
}

public sealed class PurchaseHydrationLine
{
    public long StockItemId { get; init; }
    public string StockItemName { get; init; } = string.Empty;
    public long StockItemVariantId { get; init; }
    public long UqcId { get; init; }
    public string UqcName { get; init; } = string.Empty;
    public long GodownId { get; init; }
    public string GodownName { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal Rate { get; init; }
}

public sealed class InventoryInwardEditData
{
    public long VoucherId { get; init; }
    public InventoryInwardVoucherKind Kind { get; init; }
    public long VoucherTypeId { get; init; }
    public string VoucherTypeName { get; init; } = string.Empty;
    public string VoucherNumber { get; init; } = string.Empty;
    public DateOnly VoucherDate { get; init; }
    public string ReferenceNumber { get; init; } = string.Empty;
    public long? SupplierLedgerId { get; init; }
    public long? OpeningStockItemId { get; init; }
    public string Narration { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ConcurrencyToken { get; init; } = string.Empty;
    public IReadOnlyList<InventoryInwardLineInput> Lines { get; init; } = [];
}
