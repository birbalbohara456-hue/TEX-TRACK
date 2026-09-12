namespace TexTrack.Web.Models;

public sealed class JobWorkOrderListItem
{
    public long Id { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public string VoucherTypeName { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    public string Batch { get; set; } = string.Empty;
    public string JobWorkerName { get; set; } = string.Empty;
    public DateOnly? DueDate { get; set; }
    public string FinishedGoodsSummary { get; set; } = string.Empty;
    public decimal TotalOrderedQuantity { get; set; }
    public string Status { get; set; } = "Open";
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class JobWorkOrderEditModel
{
    public long Id { get; set; }
    public int SequenceNumber { get; set; }
    public long VoucherTypeId { get; set; }
    public string VoucherTypeText { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string Batch { get; set; } = string.Empty;
    public long? JobWorkerLedgerId { get; set; }
    public string JobWorkerText { get; set; } = string.Empty;
    public DateOnly? DueDate { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Narration { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public string ConcurrencyToken { get; set; } = string.Empty;
    public List<JobWorkFinishedGoodEditModel> FinishedGoods { get; set; } = new();
}

public sealed class JobWorkFinishedGoodEditModel
{
    public string ClientKey { get; set; } = Guid.NewGuid().ToString("N");
    public long Id { get; set; }
    public string DesignGroupKey { get; set; } = Guid.NewGuid().ToString("N");
    public long StockItemId { get; set; }
    public string StockItemText { get; set; } = string.Empty;
    public long? ColourId { get; set; }
    public string ColourText { get; set; } = string.Empty;
    public string UqcShortName { get; set; } = string.Empty;
    public long? FinishedGoodsGodownId { get; set; }
    public string FinishedGoodsGodownText { get; set; } = string.Empty;
    public long? DestinationGodownId { get; set; }
    public string DestinationGodownText { get; set; } = string.Empty;
    public decimal XmlRate { get; set; }
    public decimal XmlAmount { get; set; }
    public long? BillOfMaterialId { get; set; }
    public string BillOfMaterialText { get; set; } = string.Empty;
    public List<JobWorkSizeQuantityEditModel> SizeQuantities { get; set; } = new();
    public List<JobWorkComponentEditModel> Components { get; set; } = new();
    public List<JobWorkBomStageEditModel> BomStages { get; set; } = new();
    
    // NEW: Build 2.10 - List of Nature of Processes for this FG
    public List<JobWorkProcessEditModel> Processes { get; set; } = new();
    
    public decimal TotalQuantity => SizeQuantities.Sum(x => x.Quantity);
}

public sealed class JobWorkBomStageEditModel
{
    public string ClientKey { get; set; } = Guid.NewGuid().ToString("N");
    public long Id { get; set; }
    public string? ParentStageClientKey { get; set; }
    public long? SourceBomId { get; set; }
    public int? SourceBomVersion { get; set; }
    public int StageNumber { get; set; }
    public int StageLevel { get; set; }
    public string StagePath { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public long OutputStockItemId { get; set; }
    public string OutputStockItemText { get; set; } = string.Empty;
    public long? OutputVariantId { get; set; }
    public long OutputUqcId { get; set; }
    public string OutputUqcShortName { get; set; } = string.Empty;
    public decimal OutputQuantity { get; set; }
    public long? ProcessId { get; set; }
    public string ProcessText { get; set; } = string.Empty;
    public long? AssignedJobWorkerId { get; set; }
    public string AssignedJobWorkerText { get; set; } = string.Empty;
    public DateOnly? ExpectedCompletionDate { get; set; }
    public decimal? ExpectedProcessRate { get; set; }
    public long? OutputGodownId { get; set; }
    public string OutputGodownText { get; set; } = string.Empty;
    public bool IsFinalStage { get; set; }
}

// NEW: Build 2.10 - Process Edit Model
public sealed class JobWorkProcessEditModel
{
    public string ClientKey { get; set; } = Guid.NewGuid().ToString("N");
    public long Id { get; set; }
    public long ProcessId { get; set; }
    public string ProcessText { get; set; } = string.Empty;
    public decimal ExpectedRate { get; set; }
    public string RateBasis { get; set; } = "Per Quantity";
}

public sealed class JobWorkSizeQuantityEditModel
{
    public long? MasterJobOrderAllocationId { get; set; }
    public long StockItemVariantId { get; set; }
    public long? SizeId { get; set; }
    public string SizeName { get; set; } = "Quantity";
    public int DisplayOrder { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class JobWorkComponentEditModel
{
    public string ClientKey { get; set; } = Guid.NewGuid().ToString("N");
    public long Id { get; set; }
    public long StockItemId { get; set; }
    public string StockItemText { get; set; } = string.Empty;
    public long UqcId { get; set; }
    public string UqcShortName { get; set; } = string.Empty;
    public long? ComponentGodownId { get; set; }
    public string ComponentGodownText { get; set; } = string.Empty;
    public decimal RequiredQuantity { get; set; }
    public decimal XmlRate { get; set; }
    public decimal XmlAmount { get; set; }
    public string? BomStageClientKey { get; set; }
    public string? ParentComponentClientKey { get; set; }
    public string? ChildBomStageClientKey { get; set; }
    public long? ComponentVariantId { get; set; }
    public int BomLevel { get; set; }
    public string BomPath { get; set; } = string.Empty;
    public bool IsProducedComponent { get; set; }
}

public sealed class JobWorkOrderLookupData
{
    public long VoucherTypeId { get; set; }
    public bool AllowManualNumbering { get; set; }
    public string NumberingMode { get; set; } = "Auto";
    public string VoucherPrefix { get; set; } = string.Empty;
    public int StartingNumber { get; set; } = 1;
    public List<JobWorkVoucherTypeLookup> VoucherTypes { get; set; } = new();
    public List<JobWorkerLookupItem> JobWorkers { get; set; } = new();
    public List<JobWorkStockItemLookup> FinishedGoods { get; set; } = new();
    public List<JobWorkStockItemLookup> Components { get; set; } = new();
    public List<JobWorkGodownLookup> Godowns { get; set; } = new();
    
    // NEW: Build 2.10 - Available Processes to pick from
    public List<JobWorkProcessLookup> Processes { get; set; } = new();
    public List<JobWorkBomLookup> BillOfMaterials { get; set; } = new();
}

public sealed class JobWorkBomLookup
{
    public long Id { get; set; }
    public long StockItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class JobWorkGodownLookup
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

// NEW: Build 2.10 - Process Lookup
public sealed class JobWorkProcessLookup
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class JobWorkVoucherTypeLookup
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TallyVoucherTypeName { get; set; } = string.Empty;
    public bool AllowManualNumbering { get; set; }
    public string NumberingMode { get; set; } = "Auto";
    public string Prefix { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;
    public int NumberWidth { get; set; }
    public string Abbreviation { get; set; } = string.Empty;
    public int StartingNumber { get; set; }
    public int NextSequence { get; set; }
    public string NextVoucherNumber { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
}

public sealed class JobWorkerLookupItem
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public long? DefaultMaterialOutDestinationGodownId { get; set; }
    public string DefaultMaterialOutDestinationGodownName { get; set; } = string.Empty;
    public long? DefaultMaterialInConsumptionGodownId { get; set; }
    public string DefaultMaterialInConsumptionGodownName { get; set; } = string.Empty;
}

public sealed class JobWorkStockItemLookup
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long UqcId { get; set; }
    public string UqcShortName { get; set; } = string.Empty;
    public string RootClassification { get; set; } = string.Empty;
    public decimal LatestComponentRate { get; set; }
    public List<JobWorkColourLookup> Colours { get; set; } = new();
    public List<JobWorkSizeLookup> Sizes { get; set; } = new();
    public List<JobWorkVariantLookup> Variants { get; set; } = new();
}

public sealed class JobWorkColourLookup
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class JobWorkSizeLookup
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

public sealed class JobWorkVariantLookup
{
    public long Id { get; set; }
    public long? ColourId { get; set; }
    public long? SizeId { get; set; }
}
