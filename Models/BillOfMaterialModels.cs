namespace TexTrack.Web.Models;

public sealed class BillOfMaterialListItem
{
    public long Id { get; set; }
    public long StockItemId { get; set; }
    public string StockItemName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal OutputQuantity { get; set; }
    public string UqcShortName { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public int ComponentCount { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class BillOfMaterialEditModel
{
    public long Id { get; set; }
    public long StockItemId { get; set; }
    public string StockItemText { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal OutputQuantity { get; set; } = 1;
    public int VersionNumber { get; set; } = 1;
    public bool IsDefault { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string ChangeReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = string.Empty;
    public List<BillOfMaterialLineEditModel> Lines { get; set; } = new();
}

public sealed class BillOfMaterialRevisionListItem
{
    public long Id { get; set; }
    public int RevisionNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal OutputQuantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ChangeReason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public int ComponentCount { get; set; }
    public bool IsCurrent { get; set; }
}

public sealed class BillOfMaterialRevisionDetail
{
    public long Id { get; set; }
    public int RevisionNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string StockItemName { get; set; } = string.Empty;
    public decimal OutputQuantity { get; set; }
    public string UqcShortName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ChangeReason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public List<BillOfMaterialRevisionLineDetail> Lines { get; set; } = new();
}

public sealed class BillOfMaterialRevisionLineDetail
{
    public int LineNumber { get; set; }
    public string ComponentStockItemName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public decimal RequiredQuantity { get; set; }
    public string UqcShortName { get; set; } = string.Empty;
    public string ChildBomName { get; set; } = string.Empty;
    public int? ChildBomRevisionNumber { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class BillOfMaterialRevisionComparison
{
    public BillOfMaterialRevisionDetail Older { get; set; } = new();
    public BillOfMaterialRevisionDetail Newer { get; set; } = new();
}

public sealed class BillOfMaterialLineEditModel
{
    public long Id { get; set; }
    public int LineNumber { get; set; }
    public long ComponentStockItemId { get; set; }
    public string ComponentStockItemText { get; set; } = string.Empty;
    public long? ComponentVariantId { get; set; }
    public long UqcId { get; set; }
    public string UqcShortName { get; set; } = string.Empty;
    public decimal RequiredQuantity { get; set; }
    public long? ChildBomId { get; set; }
    public string ChildBomText { get; set; } = string.Empty;
    public long? ProcessId { get; set; }
    public string ProcessText { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class BillOfMaterialLookupData
{
    public List<BomStockItemLookup> StockItems { get; set; } = new();
    public List<BomProcessLookup> Processes { get; set; } = new();
    public List<BillOfMaterialListItem> Boms { get; set; } = new();
}

public sealed class BomStockItemLookup
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long UqcId { get; set; }
    public string UqcShortName { get; set; } = string.Empty;
}

public sealed class BomProcessLookup
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
