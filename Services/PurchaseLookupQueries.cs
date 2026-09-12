using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

/// <summary>
/// Shared line-entry lookup query, reused by PurchaseOrderRepository,
/// PurchaseReturnRepository, and InventoryInwardRepository's Purchase path -
/// a plain static helper (not a DI service) since it only reads, taking an
/// already-open DbContext so each caller controls its own connection lifetime.
/// </summary>
internal static class PurchaseLookupQueries
{
    /// <summary>
    /// Previews the next voucher number for a system type, exactly like
    /// MaterialOutRepository.GetEntryDefaultsAsync does for Material Out -
    /// shared here since Purchase Order, Purchase, and Purchase Return all
    /// need the identical preview.
    /// </summary>
    public static async Task<PurchaseVoucherEntryDefaults> BuildEntryDefaultsAsync(
        TexTrackDbContext db,
        long companyId,
        long financialYearId,
        string systemTypeCode,
        CancellationToken cancellationToken)
    {
        var type = await db.VoucherTypes.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive &&
                        (x.SystemTypeCode == systemTypeCode ||
                         (x.ParentVoucherType != null && x.ParentVoucherType.SystemTypeCode == systemTypeCode)))
            .OrderByDescending(x => !x.IsSystem)
            .ThenBy(x => x.Name)
            .FirstAsync(cancellationToken);

        var max = await db.Vouchers.AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.FinancialYearId == financialYearId &&
                        x.VoucherTypeId == type.Id)
            .Select(x => (int?)x.SequenceNumber)
            .MaxAsync(cancellationToken);
        var next = Math.Max(type.StartingNumber, (max ?? type.StartingNumber - 1) + 1);
        var number = type.NumberingMode == "Manual"
            ? string.Empty
            : type.NumberWidth > 0 ? next.ToString($"D{type.NumberWidth}") : next.ToString();
        if (type.NumberingMode == "AutoPrefixSuffix") number = $"{type.Prefix}{number}{type.Suffix}";

        return new PurchaseVoucherEntryDefaults
        {
            VoucherTypeId = type.Id,
            VoucherTypeName = type.Name,
            VoucherNumber = number,
            VoucherDate = DateOnly.FromDateTime(DateTime.Today),
            NumberingMode = type.NumberingMode
        };
    }

    public static async Task<PurchaseLineLookupData> LoadAsync(
        TexTrackDbContext db,
        long companyId,
        bool restrictSuppliersToSundryCreditors,
        CancellationToken cancellationToken)
    {
        var supplierQuery = db.Ledgers.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive);
        if (restrictSuppliersToSundryCreditors)
            supplierQuery = supplierQuery.Where(x => x.LedgerGroup.RootClassification == "SundryCreditors");

        var suppliers = await supplierQuery
            .OrderBy(x => x.Name)
            .Select(x => new PurchaseSupplierLookup { Id = x.Id, Name = x.Name })
            .ToListAsync(cancellationToken);

        var godowns = await db.Godowns.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new PurchaseGodownLookup { Id = x.Id, Name = x.Name })
            .ToListAsync(cancellationToken);

        var rawVariants = await db.StockItemVariants.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive && x.StockItem.IsActive)
            .Select(x => new
            {
                x.StockItemId,
                ItemName = x.StockItem.Name,
                VariantId = x.Id,
                ColourName = x.Colour != null ? x.Colour.Name : null,
                SizeName = x.Size != null ? x.Size.Name : null,
                x.StockItem.UqcId,
                UqcShortName = x.StockItem.Uqc.ShortName
            })
            .ToListAsync(cancellationToken);

        var itemVariants = rawVariants
            .OrderBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ColourName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SizeName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new PurchaseItemVariantLookup
            {
                StockItemId = x.StockItemId,
                StockItemName = x.ItemName,
                StockItemVariantId = x.VariantId,
                VariantDisplay = string.Join(" / ", new[] { x.ColourName, x.SizeName }
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
                UqcId = x.UqcId,
                UqcShortName = x.UqcShortName
            })
            .ToList();

        var items = rawVariants
            .GroupBy(x => x.StockItemId)
            .Select(g => g.First())
            .OrderBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new PurchaseItemLookup
            {
                StockItemId = x.StockItemId,
                StockItemName = x.ItemName,
                UqcId = x.UqcId,
                UqcShortName = x.UqcShortName
            })
            .ToList();

        return new PurchaseLineLookupData
        {
            Suppliers = suppliers,
            Godowns = godowns,
            ItemVariants = itemVariants,
            Items = items
        };
    }

    /// <summary>
    /// Full colour/size pairing for one Stock Item, done once server-side
    /// (unlike JobWorkOrderRepository.MapStockItemLookup's flat lists, which
    /// leave the colour-x-size pairing to client-side code in
    /// JobWorkOutOrder.razor's RebuildSizeQuantities) - drives the
    /// variant-allocation matrix directly. Replicates
    /// MapStockItemLookup's active-colour/active-size/active-variant
    /// filtering exactly. Returns null if the item doesn't exist, is
    /// inactive, or belongs to another company.
    /// </summary>
    public static async Task<PurchaseItemVariantDetail?> GetItemVariantDetailAsync(
        TexTrackDbContext db,
        long companyId,
        long stockItemId,
        CancellationToken cancellationToken)
    {
        var item = await db.StockItems.AsNoTracking()
            .Include(x => x.Uqc)
            .Include(x => x.Colours).ThenInclude(c => c.Colour)
            .Include(x => x.Sizes).ThenInclude(s => s.Size)
            .Include(x => x.Variants)
            .SingleOrDefaultAsync(x => x.Id == stockItemId && x.CompanyId == companyId && x.IsActive, cancellationToken);
        if (item is null) return null;

        var activeColours = item.Colours.Where(c => c.Colour.IsActive)
            .Select(c => (Id: (long?)c.ColourId, Name: c.Colour.Name))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var activeColourIds = activeColours.Where(c => c.Id is not null).Select(c => c.Id!.Value).ToHashSet();
        var activeSizes = item.Sizes.Where(s => s.Size.IsActive)
            .Select(s => (Id: (long?)s.SizeId, Name: s.Size.Name, s.Size.DisplayOrder))
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var activeSizeIds = activeSizes.Where(s => s.Id is not null).Select(s => s.Id!.Value).ToHashSet();

        var activeVariants = item.Variants.Where(v => v.IsActive &&
            (v.ColourId == null || activeColourIds.Contains(v.ColourId.Value)) &&
            (v.SizeId == null || activeSizeIds.Contains(v.SizeId.Value))).ToList();

        var hasColourAxis = activeColours.Count > 0;
        var colourAxis = hasColourAxis
            ? activeColours
            : new List<(long? Id, string Name)> { (null, string.Empty) };
        var sizeAxis = activeSizes.Count > 0
            ? activeSizes
            : new List<(long? Id, string Name, int DisplayOrder)> { (null, "Quantity", 0) };

        var colourGroups = new List<PurchaseItemColourGroup>();
        foreach (var colour in colourAxis)
        {
            var cells = new List<PurchaseItemSizeCell>();
            foreach (var size in sizeAxis)
            {
                var variant = activeVariants.FirstOrDefault(v => v.ColourId == colour.Id && v.SizeId == size.Id);
                if (variant is null) continue;
                cells.Add(new PurchaseItemSizeCell
                {
                    SizeId = size.Id,
                    SizeLabel = size.Id is null ? "Quantity" : size.Name,
                    DisplayOrder = size.DisplayOrder,
                    StockItemVariantId = variant.Id
                });
            }
            if (cells.Count == 0) continue;
            colourGroups.Add(new PurchaseItemColourGroup
            {
                ColourId = colour.Id,
                ColourName = colour.Name,
                SizeCells = cells
            });
        }

        return new PurchaseItemVariantDetail
        {
            StockItemId = item.Id,
            StockItemName = item.Name,
            UqcId = item.UqcId,
            UqcShortName = item.Uqc.ShortName,
            HasColourAxis = hasColourAxis,
            ColourGroups = colourGroups
        };
    }
}
