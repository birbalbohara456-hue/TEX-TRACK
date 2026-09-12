namespace TexTrack.Web.Models;

public sealed class StockItemListItem
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Alias { get; init; } = string.Empty;
    public string StockGroupName { get; init; } = string.Empty;
    public string UqcShortName { get; init; } = string.Empty;
    public string StockCategoryName { get; init; } = string.Empty;
    public string TaxMode { get; init; } = string.Empty;
    public string HsnCode { get; init; } = string.Empty;
    public int ColourCount { get; init; }
    public int SizeCount { get; init; }
    public int VariantCount { get; init; }
    public string ConcurrencyToken { get; init; } = string.Empty;
}

public sealed class StockItemEditModel
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public long StockGroupId { get; set; }
    public long UqcId { get; set; }
    public long? StockCategoryId { get; set; }
    public string TaxMode { get; set; } = "NotApplicable";
    public long? TaxClassificationId { get; set; }
    public string HsnCode { get; set; } = string.Empty;
    public decimal IgstRate { get; set; }
    public decimal CgstRate { get; set; }
    public decimal SgstRate { get; set; }
    public decimal? CostPrice { get; set; }
    public decimal? SalePrice { get; set; }
    public HashSet<long> ColourIds { get; set; } = new();
    public HashSet<long> SizeIds { get; set; } = new();
    public StockItemDesignPhotoModel? DesignPhoto { get; set; }
    public List<StockItemCustomFieldEditModel> CustomFields { get; set; } = new();
    public long? OpeningStockVoucherId { get; set; }
    public string OpeningStockConcurrencyToken { get; set; } = string.Empty;
    public List<StockItemOpeningAllocationEditModel> OpeningInventory { get; set; } = new();
    public bool IsUqcLocked { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class StockItemOpeningAllocationEditModel
{
    public string VariantKey { get; set; } = "BASE";
    public long GodownId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal Value { get; set; }
    public string CalculationBasis { get; set; } = "Rate";
}

public sealed class StockItemFieldDefinitionModel
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string FieldType { get; init; } = "Text";
    public int DisplayOrder { get; init; }
    public bool IsRequired { get; init; }
    public bool AllowMultiple { get; init; }
    public string OptionsText { get; init; } = string.Empty;
}

public sealed class StockItemCustomFieldEditModel
{
    public long DefinitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FieldType { get; set; } = "Text";
    public bool IsRequired { get; set; }
    public bool AllowMultiple { get; set; }
    public string OptionsText { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public List<StockItemPhotoModel> Photos { get; set; } = new();
}

public sealed class StockItemPhotoModel
{
    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[]? NewContent { get; set; }
}

public sealed class StockItemDesignPhotoModel
{
    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[]? NewContent { get; set; }
    public bool Remove { get; set; }
}

public sealed class StockItemFieldDefinitionEditModel
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FieldType { get; set; } = "Text";
    public int DisplayOrder { get; set; } = 100;
    public bool IsRequired { get; set; }
    public bool AllowMultiple { get; set; }
    public string OptionsText { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class StockItemLookupData
{
    public IReadOnlyList<LookupItem> StockGroups { get; init; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Uqcs { get; init; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> StockCategories { get; init; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> TaxClassifications { get; init; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Colours { get; init; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Sizes { get; init; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Godowns { get; init; } = Array.Empty<LookupItem>();
    public IReadOnlyList<StockItemFieldDefinitionModel> CustomFieldDefinitions { get; init; } = Array.Empty<StockItemFieldDefinitionModel>();
    public DateOnly BooksBeginningDate { get; init; }
}

public sealed record LookupItem(long Id, string Name, string SecondaryText = "");

public sealed class PagedReportResult<T>
{
    public List<T> Rows { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize)));
}
