using TexTrack.Web.Models;

namespace TexTrack.Web.Components.Vouchers.Shared;

public enum PurchaseFamilyMode
{
    PurchaseOrder,
    Purchase,
    PurchaseReturn
}

/// <summary>
/// One record per mode, built once by a static factory - keeps the shared
/// entry form's per-mode differences explicit and centralized instead of a
/// pile of individual bool [Parameter]s.
/// </summary>
public sealed record PurchaseFamilyFormOptions(
    PurchaseFamilyMode Mode,
    string VoucherNoun,
    bool RequireSupplier,
    bool GodownRequired,
    bool ShowUpstreamReference,
    string UpstreamReferenceLabel,
    bool UpstreamCapsApply,
    bool ShowExpectedDeliveryDate,
    bool ReversesStockOnCancel)
{
    public static PurchaseFamilyFormOptions For(PurchaseFamilyMode mode) => mode switch
    {
        PurchaseFamilyMode.PurchaseOrder => new(
            mode, "Purchase Order",
            RequireSupplier: true, GodownRequired: false,
            ShowUpstreamReference: false, UpstreamReferenceLabel: "",
            UpstreamCapsApply: false, ShowExpectedDeliveryDate: true,
            ReversesStockOnCancel: false),
        PurchaseFamilyMode.Purchase => new(
            mode, "Purchase",
            RequireSupplier: true, GodownRequired: true,
            ShowUpstreamReference: false, UpstreamReferenceLabel: "Purchase Order (optional)",
            UpstreamCapsApply: true, ShowExpectedDeliveryDate: false,
            ReversesStockOnCancel: true),
        PurchaseFamilyMode.PurchaseReturn => new(
            mode, "Purchase Return",
            RequireSupplier: false, GodownRequired: true,
            ShowUpstreamReference: true, UpstreamReferenceLabel: "Purchase Bill (optional)",
            UpstreamCapsApply: false, ShowExpectedDeliveryDate: false,
            ReversesStockOnCancel: true),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}

public sealed class PurchaseFamilyFormState
{
    public long SupplierLedgerId { get; set; }
    public string SupplierText { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Narration { get; set; } = string.Empty;

    /// <summary>Null = blank/not-uniform. See PurchaseFamilyEntryForm's header-discount algorithm.</summary>
    public decimal? HeaderDiscountPercent { get; set; }

    /// <summary>Upstream documents (POs for Purchase; Purchases for Purchase Return) whose
    /// lines have been pulled into this voucher during the current entry session - a supplier
    /// invoice can legitimately cover several, so each pick via the reference field appends
    /// to this list instead of replacing it. Purely a live-session aid (not reconstructed from
    /// saved data on edit-open); each saved line's own PurchaseOrderLineId is the durable link.</summary>
    public List<UpstreamDocumentSummary> ReferencedUpstreamDocuments { get; set; } = [];
    public string UpstreamReferenceText { get; set; } = string.Empty;

    public List<PurchaseFamilyLineRow> Lines { get; set; } = [new()];
}

public sealed class PurchaseFamilyLineRow
{
    public string ClientKey { get; } = Guid.NewGuid().ToString("N");
    public long StockItemId { get; set; }
    public string ItemText { get; set; } = string.Empty;
    public long UqcId { get; set; }
    public string UqcShortName { get; set; } = string.Empty;
    public long? GodownId { get; set; }
    public string GodownText { get; set; } = string.Empty;
    public List<VariantAllocationCell> Allocations { get; set; } = [];
    public decimal Rate { get; set; }
    public decimal DiscountPercent { get; set; }
    public DateOnly? ExpectedDeliveryDate { get; set; }

    /// <summary>Free-text note only (not persisted) - lets the user jot down which PO an
    /// individual line relates to. Not wired to the real PurchaseOrderLineId capping/hydration
    /// path; that stays driven by the header-level upstream reference.</summary>
    public string PoRefNo { get; set; } = string.Empty;

    public decimal TotalQuantity => Allocations.Sum(a => a.Quantity);

    public decimal Amount => decimal.Round(
        TotalQuantity * Rate * (1 - DiscountPercent / 100m), 4, MidpointRounding.AwayFromZero);

    public bool HasAllocation => Allocations.Any(a => a.Quantity > 0);
    public bool HasItem => StockItemId != 0;
}

public sealed class VariantAllocationCell
{
    public long StockItemVariantId { get; set; }
    public long? ColourId { get; set; }
    public string ColourName { get; set; } = string.Empty;
    public long? SizeId { get; set; }
    public string SizeLabel { get; set; } = string.Empty;
    public decimal Quantity { get; set; }

    /// <summary>Purchase mode only, and only when this cell came from a PO reference.</summary>
    public long? PurchaseOrderLineId { get; set; }

    /// <summary>Purchase mode + PO reference only; null = uncapped.</summary>
    public decimal? PendingCap { get; set; }
}

/// <summary>
/// Generic shape for one outstanding upstream document (a Purchase Order for
/// Purchase mode, or a Purchase for Purchase Return mode) in the reference
/// picker - each wrapper page's adapter maps its own concrete
/// PurchaseOrderOutstandingSummary/PurchaseOutstandingSummary into this.
/// </summary>
public sealed record UpstreamDocumentSummary(long VoucherId, string VoucherNumber, DateOnly VoucherDate, int LineCount);

/// <summary>
/// Generic full-hydration result for a selected upstream document - each
/// wrapper page's adapter maps its own concrete
/// PurchaseOrderHydrationResult/PurchaseHydrationResult into this, resolving
/// each line's Colour/Size identity via PurchaseFamilyLineGrouping.ResolveVariant.
/// </summary>
public sealed record UpstreamHydrationResult(long VoucherId, string VoucherNumber, IReadOnlyList<PurchaseFamilyRawLine> Lines);

/// <summary>Result handed back by VariantAllocationModal when the user confirms.</summary>
public sealed class VariantAllocationResult
{
    public IReadOnlyList<VariantAllocationCell> Allocations { get; init; } = [];
    public decimal Rate { get; init; }
    public decimal DiscountPercent { get; init; }
    public string PoRefNo { get; init; } = string.Empty;
}

/// <summary>
/// Common projection a wrapper page's GetForEdit adapter builds from its own
/// concrete *EditData type, so PurchaseFamilyEntryForm never sees
/// InventoryInwardEditData/PurchaseOrderEditData/PurchaseReturnEditData
/// directly.
/// </summary>
public sealed class PurchaseFamilyEditData
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
    public IReadOnlyList<PurchaseFamilyRawLine> Lines { get; init; } = [];
}

public sealed class PurchaseFamilySaveOutcome
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public long VoucherId { get; init; }
    public string VoucherNumber { get; init; } = string.Empty;
}

/// <summary>
/// Common shape both edit-hydration and upstream-reference-prefill map into
/// before PurchaseFamilyLineGrouping.GroupLines groups them into outer
/// summary rows.
/// </summary>
public sealed record PurchaseFamilyRawLine(
    long StockItemId, string StockItemName, long StockItemVariantId,
    long? ColourId, string ColourName, long? SizeId, string SizeLabel,
    long UqcId, string UqcShortName, long? GodownId, string GodownText,
    decimal Quantity, decimal Rate, decimal? Amount,
    long? PurchaseOrderLineId, decimal? PendingCap, DateOnly? ExpectedDeliveryDate);
