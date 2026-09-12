namespace TexTrack.Web.Models;

public sealed class MaterialInVoucherDefaults
{
    public long VoucherTypeId { get; set; }
    public string VoucherTypeName { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string NumberingMode { get; set; } = "Auto";
}

public sealed class MaterialInLookupData
{
    public List<JobWorkerLookupItem> JobWorkers { get; set; } = new();
    public List<JobWorkGodownLookup> Godowns { get; set; } = new();
}

public sealed class MaterialInPendingOrder
{
    public long JwoVoucherId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    public string Batch { get; set; } = string.Empty;
    public long JobWorkerLedgerId { get; set; }
    public string JobWorkerName { get; set; } = string.Empty;
    public long JobberDestinationGodownId { get; set; }
    public string JobberDestinationGodownName { get; set; } = string.Empty;
    public bool HasMultipleJobberDestinationGodowns { get; set; }
    public List<MaterialInPendingFinishedGood> FinishedGoods { get; set; } = new();
    public List<MaterialInAvailableConsumption> Consumptions { get; set; } = new();
}

public sealed class MaterialInPendingFinishedGood
{
    public long JwoFinishedGoodId { get; set; }
    public long BomStageId { get; set; }
    public long StageAssignmentId { get; set; }
    public int AssignmentVersion { get; set; }
    public string StageName { get; set; } = string.Empty;
    public bool IsFinalStage { get; set; }
    public long OutputGodownId { get; set; }
    public string OutputGodownName { get; set; } = string.Empty;
    public long StockItemId { get; set; }
    public string StockItemName { get; set; } = string.Empty;
    public long UqcId { get; set; }
    public string UqcShortName { get; set; } = string.Empty;
    public int DecimalPlaces { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal PendingQuantity => OrderedQuantity - ReceivedQuantity;
    public List<MaterialInPendingVariant> Variants { get; set; } = new();
}

public sealed class MaterialInPendingVariant
{
    public long StockItemVariantId { get; set; }
    public string ColourName { get; set; } = "N/A";
    public string SizeName { get; set; } = "N/A";
    public int DisplayOrder { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal PendingQuantity => OrderedQuantity - ReceivedQuantity;
}

public sealed class MaterialInAvailableConsumption
{
    public long JwoComponentId { get; set; }
    public long JwoFinishedGoodId { get; set; }
    public long BomStageId { get; set; }
    public long StageAssignmentId { get; set; }
    public int AssignmentVersion { get; set; }
    public long StockItemId { get; set; }
    public string StockItemName { get; set; } = string.Empty;
    public long UqcId { get; set; }
    public string UqcShortName { get; set; } = string.Empty;
    public int DecimalPlaces { get; set; }
    public decimal RequiredQuantity { get; set; }
    public decimal IssuedQuantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public decimal ConsumedMaterialValue { get; set; }
    public decimal ProcessCharge { get; set; }
    public decimal FinishedGoodsValue { get; set; }
    public decimal AvailableQuantity => IssuedQuantity - ConsumedQuantity;
    public decimal SuggestedRate { get; set; }
}

public sealed class MaterialInSaveRequest
{
    public long VoucherId { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
    public long VoucherTypeId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Batch { get; set; } = string.Empty;
    public long JobWorkerLedgerId { get; set; }
    public long JwoVoucherId { get; set; }
    public long ConsumptionGodownId { get; set; }
    public long ReceivingGodownId { get; set; }
    public string Narration { get; set; } = string.Empty;
    public List<MaterialInFinishedGoodInput> FinishedGoods { get; set; } = new();
    public List<MaterialInConsumptionInput> Consumptions { get; set; } = new();
}

public sealed class MaterialInEditData
{
    public long VoucherId { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public MaterialInVoucherDefaults Defaults { get; set; } = new();
    public long JobWorkerLedgerId { get; set; }
    public string JobWorkerName { get; set; } = string.Empty;
    public long JwoVoucherId { get; set; }
    public long ConsumptionGodownId { get; set; }
    public long ReceivingGodownId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Narration { get; set; } = string.Empty;
    public List<MaterialInFinishedGoodInput> FinishedGoods { get; set; } = new();
    public List<MaterialInConsumptionInput> Consumptions { get; set; } = new();
}

public sealed class MaterialInFinishedGoodInput
{
    public ProcessChargeMode ChargeMode { get; set; }
    public long JwoFinishedGoodId { get; set; }
    public long BomStageId { get; set; }
    public long StageAssignmentId { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal TotalCharge { get; set; }
    public decimal ProcessCharge { get; set; }
    public List<MaterialInVariantInput> Variants { get; set; } = new();
}

public sealed class MaterialInVariantInput
{
    public long StockItemVariantId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class MaterialInConsumptionInput
{
    public long JwoComponentId { get; set; }
    public long BomStageId { get; set; }
    public long StageAssignmentId { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public decimal ConsumedMaterialValue { get; set; }
    public decimal ProcessCharge { get; set; }
    public decimal FinishedGoodsValue { get; set; }
}

public sealed class MaterialInSaveResult
{
    public long VoucherId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
}

public sealed class MaterialInListItem
{
    public long VoucherId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    public string JobWorkerName { get; set; } = string.Empty;
    public string JwoVoucherNumber { get; set; } = string.Empty;
    public string Batch { get; set; } = string.Empty;
    public string ConsumptionGodownName { get; set; } = string.Empty;
    public string ReceivingGodownName { get; set; } = string.Empty;
    public decimal ReceivedQuantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public decimal ConsumedMaterialValue { get; set; }
    public decimal ProcessCharge { get; set; }
    public decimal FinishedGoodsValue { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class MaterialInViewData
{
    public MaterialInListItem Header { get; set; } = new();
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Narration { get; set; } = string.Empty;
    public List<MaterialInViewFinishedGood> FinishedGoods { get; set; } = new();
    public List<MaterialInViewConsumption> Consumptions { get; set; } = new();
}

public sealed class MaterialInViewFinishedGood
{
    public string StockItemName { get; set; } = string.Empty;
    public string UqcName { get; set; } = string.Empty;
    public decimal ReceivedQuantity { get; set; }
    public decimal MaterialValue { get; set; }
    public decimal ProcessCharge { get; set; }
    public decimal FinishedGoodsValue { get; set; }
    public decimal Rate { get; set; }
    public List<MaterialInViewVariant> Variants { get; set; } = new();
}

public sealed class MaterialInViewVariant
{
    public string ColourName { get; set; } = "N/A";
    public string SizeName { get; set; } = "N/A";
    public decimal Quantity { get; set; }
}

public sealed class MaterialInViewConsumption
{
    public string FinishedGoodName { get; set; } = string.Empty;
    public string StockItemName { get; set; } = string.Empty;
    public string UqcName { get; set; } = string.Empty;
    public decimal ConsumedQuantity { get; set; }
    public decimal ConsumedMaterialValue { get; set; }
    public decimal ProcessCharge { get; set; }
    public decimal FinishedGoodsValue { get; set; }
    public decimal Rate { get; set; }
    public decimal Value { get; set; }
}
