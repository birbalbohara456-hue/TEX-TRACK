using TexTrack.Web.Models;

namespace TexTrack.Web.Components.Vouchers.Shared;

/// <summary>
/// Groups a flat list of persisted/hydrated lines back into one outer summary
/// row per (Stock Item, Godown) - used both for edit-mode hydration and for
/// rebuilding lines after an upstream reference (PO or Purchase) is selected.
/// A plain static helper, not DI, matching Services/PurchaseLookupQueries.cs's
/// convention.
/// </summary>
public static class PurchaseFamilyLineGrouping
{
    public static List<PurchaseFamilyLineRow> GroupLines(IEnumerable<PurchaseFamilyRawLine> rawLines)
    {
        var groups = rawLines
            .GroupBy(x => (x.StockItemId, GodownId: x.GodownId ?? 0))
            .Select(BuildRow)
            .ToList();
        return groups.Count == 0 ? [new PurchaseFamilyLineRow()] : groups;
    }

    private static PurchaseFamilyLineRow BuildRow(IGrouping<(long StockItemId, long GodownId), PurchaseFamilyRawLine> group)
    {
        var lines = group.ToList();
        var first = lines[0];
        var row = new PurchaseFamilyLineRow
        {
            StockItemId = first.StockItemId,
            ItemText = first.StockItemName,
            UqcId = first.UqcId,
            UqcShortName = first.UqcShortName,
            GodownId = first.GodownId,
            GodownText = first.GodownText,
            ExpectedDeliveryDate = first.ExpectedDeliveryDate,
            Rate = first.Rate,
            Allocations = lines.Select(x => new VariantAllocationCell
            {
                StockItemVariantId = x.StockItemVariantId,
                ColourId = x.ColourId,
                ColourName = x.ColourName,
                SizeId = x.SizeId,
                SizeLabel = x.SizeLabel,
                Quantity = x.Quantity,
                PurchaseOrderLineId = x.PurchaseOrderLineId,
                PendingCap = x.PendingCap
            }).ToList()
        };

        // Documented edge case: variant lines within a group can only carry
        // differing Rate values on data saved by the pre-redesign flat UI
        // (never producible by this UI going forward, since Rate/Discount are
        // now set once per outer row). When that happens, take the first
        // line's Rate and back-solve DiscountPercent so the group's total
        // Amount still matches exactly on load - the per-variant rate nuance
        // collapses the next time this row is saved.
        var totalQuantity = lines.Sum(x => x.Quantity);
        var totalAmount = lines.Sum(x => x.Amount ?? decimal.Round(x.Quantity * x.Rate, 4, MidpointRounding.AwayFromZero));
        if (totalQuantity > 0 && row.Rate > 0)
        {
            var grossAtFirstRate = totalQuantity * row.Rate;
            if (grossAtFirstRate > 0)
            {
                var impliedDiscount = (1 - totalAmount / grossAtFirstRate) * 100m;
                row.DiscountPercent = Math.Clamp(decimal.Round(impliedDiscount, 4, MidpointRounding.AwayFromZero), 0, 100);
            }
        }

        return row;
    }

    /// <summary>
    /// Resolves a persisted StockItemVariantId back to its Colour/Size
    /// identity by searching an already-loaded PurchaseItemVariantDetail -
    /// needed by every mode's edit-hydration adapter (a saved line only
    /// stores StockItemVariantId, never denormalized colour/size names), and
    /// by the upstream-reference hydration adapters. Returns null if the
    /// variant isn't found (e.g. deactivated since the line was saved) - the
    /// caller should fall back to a blank colour/size label in that case.
    /// </summary>
    public static (long? ColourId, string ColourName, long? SizeId, string SizeLabel)? ResolveVariant(
        PurchaseItemVariantDetail detail, long stockItemVariantId)
    {
        foreach (var group in detail.ColourGroups)
            foreach (var cell in group.SizeCells)
                if (cell.StockItemVariantId == stockItemVariantId)
                    return (group.ColourId, group.ColourName, cell.SizeId, cell.SizeLabel);
        return null;
    }

    /// <summary>
    /// The reverse of GroupLines - expands each outer row's per-variant
    /// allocations back into one flat line per (Stock Item, Colour, Size) for
    /// saving. A row's Rate/DiscountPercent apply to every one of its cells;
    /// Amount is computed per-cell here (not derived from the row total) so
    /// each saved line's own Amount override correctly encodes the discount -
    /// every *LineInput DTO already supports Amount as a nullable override of
    /// Quantity*Rate.
    /// </summary>
    public static IEnumerable<PurchaseFamilyExpandedLine> ExpandLines(IEnumerable<PurchaseFamilyLineRow> rows) =>
        rows.Where(r => r.HasItem).SelectMany(row => row.Allocations
            .Where(a => a.Quantity > 0)
            .Select(a => new PurchaseFamilyExpandedLine(
                StockItemId: row.StockItemId,
                StockItemVariantId: a.StockItemVariantId,
                UqcId: row.UqcId,
                GodownId: row.GodownId,
                Quantity: a.Quantity,
                Rate: row.Rate,
                Amount: decimal.Round(a.Quantity * row.Rate * (1 - row.DiscountPercent / 100m), 4, MidpointRounding.AwayFromZero),
                PurchaseOrderLineId: a.PurchaseOrderLineId,
                ExpectedDeliveryDate: row.ExpectedDeliveryDate)));
}

/// <summary>Generic flat line shape yielded by ExpandLines - each wrapper page's SaveAsync
/// adapter maps this into its own concrete *LineInput type.</summary>
public sealed record PurchaseFamilyExpandedLine(
    long StockItemId, long StockItemVariantId, long UqcId, long? GodownId,
    decimal Quantity, decimal Rate, decimal Amount,
    long? PurchaseOrderLineId, DateOnly? ExpectedDeliveryDate);
