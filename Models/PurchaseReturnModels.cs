namespace TexTrack.Web.Models;

public sealed class PurchaseReturnLineInput
{
    public long StockItemId { get; set; }
    public long StockItemVariantId { get; set; }
    public long UqcId { get; set; }
    public long GodownId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal? Amount { get; set; }
}

public sealed class PurchaseReturnSaveRequest
{
    public long VoucherId { get; set; }
    public long VoucherTypeId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }

    /// <summary>
    /// Optional bill-number label. Purely informational - no validation against
    /// any specific original Purchase, whether populated or not.
    /// </summary>
    public string ReferenceNumber { get; set; } = string.Empty;
    public long? SupplierLedgerId { get; set; }
    public string Narration { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = string.Empty;
    public List<PurchaseReturnLineInput> Lines { get; set; } = [];
}

public sealed record PurchaseReturnSaveResult(long VoucherId, string VoucherNumber);

public sealed class PurchaseReturnEditData
{
    public long VoucherId { get; init; }
    public long VoucherTypeId { get; init; }
    public string VoucherTypeName { get; init; } = string.Empty;
    public string VoucherNumber { get; init; } = string.Empty;
    public DateOnly VoucherDate { get; init; }
    public string ReferenceNumber { get; init; } = string.Empty;
    public long? SupplierLedgerId { get; init; }
    public string Narration { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ConcurrencyToken { get; init; } = string.Empty;
    public IReadOnlyList<PurchaseReturnLineInput> Lines { get; init; } = [];
}

public sealed class PurchaseReturnListItem
{
    public long VoucherId { get; init; }
    public string VoucherNumber { get; init; } = string.Empty;
    public DateOnly VoucherDate { get; init; }
    public string ReferenceNumber { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ConcurrencyToken { get; init; } = string.Empty;
}
