namespace TexTrack.Web.Models;

/// <summary>
/// Shared line-entry lookup shape reused by Purchase Order, Purchase, and
/// Purchase Return. Each repository owns and populates its own instance (no
/// shared lookup service exists in this codebase - matches the established
/// per-repository GetLookupsAsync convention), scoping Suppliers per its own
/// validation rule (Purchase/Purchase Order require Sundry Creditors; Purchase
/// Return accepts any active ledger).
/// </summary>
public sealed class PurchaseLineLookupData
{
    public IReadOnlyList<PurchaseSupplierLookup> Suppliers { get; init; } = [];
    public IReadOnlyList<PurchaseItemVariantLookup> ItemVariants { get; init; } = [];
    public IReadOnlyList<PurchaseItemLookup> Items { get; init; } = [];
    public IReadOnlyList<PurchaseGodownLookup> Godowns { get; init; } = [];
}

/// <summary>
/// One searchable Stock Item (never a variant) - the item-only search box for
/// the redesigned line entry, distinct-by-item over the same data
/// PurchaseItemVariantLookup already indexes.
/// </summary>
public sealed class PurchaseItemLookup
{
    public long StockItemId { get; init; }
    public string StockItemName { get; init; } = string.Empty;
    public long UqcId { get; init; }
    public string UqcShortName { get; init; } = string.Empty;
}

/// <summary>
/// Full colour/size variant structure for one Stock Item, already paired
/// server-side (unlike JobWorkOrderRepository's flat Colours/Sizes/Variants
/// lists, which leave pairing to client-side code) - built once by
/// PurchaseLookupQueries.GetItemVariantDetailAsync and consumed directly by
/// the variant-allocation matrix. HasColourAxis distinguishes a genuine
/// colour-only/BASE item (ColourGroups has exactly one entry with
/// ColourId = null) from a real multi-colour item.
/// </summary>
public sealed class PurchaseItemVariantDetail
{
    public long StockItemId { get; init; }
    public string StockItemName { get; init; } = string.Empty;
    public long UqcId { get; init; }
    public string UqcShortName { get; init; } = string.Empty;
    public bool HasColourAxis { get; init; }
    public IReadOnlyList<PurchaseItemColourGroup> ColourGroups { get; init; } = [];
}

public sealed class PurchaseItemColourGroup
{
    public long? ColourId { get; init; }
    public string ColourName { get; init; } = string.Empty;
    public IReadOnlyList<PurchaseItemSizeCell> SizeCells { get; init; } = [];
}

/// <summary>
/// SizeId is null (and SizeLabel is literally "Quantity") when the item has
/// no size axis for this colour - matches JobWorkOutOrder.razor's exact
/// "single Quantity cell" convention rather than printing a placeholder.
/// </summary>
public sealed class PurchaseItemSizeCell
{
    public long? SizeId { get; init; }
    public string SizeLabel { get; init; } = string.Empty;
    public int DisplayOrder { get; init; }
    public long StockItemVariantId { get; init; }
}

/// <summary>
/// Shared "new voucher" preview shape (next number, date, numbering mode) -
/// reused by Purchase Order, Purchase, and Purchase Return, all of which
/// preview the same way MaterialOutVoucherDefaults already does for
/// Material Out.
/// </summary>
public sealed class PurchaseVoucherEntryDefaults
{
    public long VoucherTypeId { get; set; }
    public string VoucherTypeName { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    public string NumberingMode { get; set; } = "Auto";
}

public sealed class PurchaseSupplierLookup
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class PurchaseGodownLookup
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// One purchasable Stock Item Variant row (including the "BASE" variant for
/// non-variant items). VariantDisplay is blank for BASE, otherwise "Colour /
/// Size" (either half omitted if the item has only one axis).
/// </summary>
public sealed class PurchaseItemVariantLookup
{
    public long StockItemId { get; init; }
    public string StockItemName { get; init; } = string.Empty;
    public long StockItemVariantId { get; init; }
    public string VariantDisplay { get; init; } = string.Empty;
    public long UqcId { get; init; }
    public string UqcShortName { get; init; } = string.Empty;

    public string DisplayText => string.IsNullOrWhiteSpace(VariantDisplay)
        ? StockItemName
        : $"{StockItemName} ({VariantDisplay})";
}
