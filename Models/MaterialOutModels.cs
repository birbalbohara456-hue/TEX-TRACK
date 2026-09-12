namespace TexTrack.Web.Models;


public sealed class MaterialOutListItem
{
    public long Id { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public string VoucherTypeName { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Batch { get; set; } = string.Empty;
    public string JobWorkerName { get; set; } = string.Empty;
    public string DisplayedOrderNumber { get; set; } = string.Empty;
    public string DestinationGodownName { get; set; } = string.Empty;
    public decimal TotalQuantity { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class MaterialOutVoucherDefaults
{
    public long VoucherTypeId { get; set; }
    public string VoucherTypeName { get; set; } = "Material Out";
    public string VoucherNumber { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string NumberingMode { get; set; } = "Auto";
}

public sealed class MaterialOutPendingOrder
{
    public long JwoVoucherId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    public string Batch { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
    public long JobWorkerLedgerId { get; set; }
    public string JobWorkerName { get; set; } = string.Empty;
    public DateOnly? DueDate { get; set; }
    public List<MaterialOutPendingFinishedGood> FinishedGoods { get; set; } = new();
    public decimal TotalPendingQuantity => FinishedGoods.SelectMany(x => x.Components).Sum(x => x.PendingQuantity);
}

public sealed class MaterialOutPendingFinishedGood
{
    public long JwoFinishedGoodId { get; set; }
    public string FinishedGoodName { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public string FinishedGoodsGodownName { get; set; } = string.Empty;
    public string DestinationGodownName { get; set; } = string.Empty;
    public List<MaterialOutPendingComponent> Components { get; set; } = new();
}

public sealed class MaterialOutPendingComponent
{
    public long JwoComponentId { get; set; }
    public long StockItemId { get; set; }
    public long? StockItemVariantId { get; set; }
    public string StockItemName { get; set; } = string.Empty;
    public long UqcId { get; set; }
    public string UqcShortName { get; set; } = string.Empty;
    public int DecimalPlaces { get; set; }
    public long? SourceGodownId { get; set; }
    public string SourceGodownName { get; set; } = string.Empty;
    public decimal RequiredQuantity { get; set; }
    public decimal IssuedQuantity { get; set; }
    public decimal PendingQuantity => Math.Max(0, RequiredQuantity - IssuedQuantity);
    public decimal IssueNowQuantity { get; set; }
    public decimal Rate { get; set; }
    public bool IsProducedComponent { get; set; }
    public decimal Amount => IssueNowQuantity * Rate;
}

public sealed class MaterialOutLookupData
{
    public List<JobWorkerLookupItem> JobWorkers { get; set; } = new();
    public List<JobWorkGodownLookup> Godowns { get; set; } = new();
}


public sealed class MaterialOutEditData
{
    public long VoucherId { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public MaterialOutVoucherDefaults Defaults { get; set; } = new();
    public long JobWorkerLedgerId { get; set; }
    public string JobWorkerName { get; set; } = string.Empty;
    public long DestinationGodownId { get; set; }
    public string DestinationGodownName { get; set; } = string.Empty;
    public string Batch { get; set; } = string.Empty;
    public string Narration { get; set; } = string.Empty;
    public MaterialOutPendingOrder Order { get; set; } = new();
    public bool ProvideGstEwayDetails { get; set; }
    public MaterialOutEwayDetailsInput EwayDetails { get; set; } = new();
}

public sealed class MaterialOutSaveRequest
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
    public string DisplayedOrderNumber { get; set; } = string.Empty;
    public long DestinationGodownId { get; set; }
    public string Narration { get; set; } = string.Empty;
    public bool ProvideGstEwayDetails { get; set; }
    public MaterialOutEwayDetailsInput EwayDetails { get; set; } = new();
    public List<MaterialOutSaveLine> Lines { get; set; } = new();
}

public sealed class MaterialOutSaveLine
{
    public long JwoFinishedGoodId { get; set; }
    public long JwoComponentId { get; set; }
    public long StockItemId { get; set; }
    public long UqcId { get; set; }
    public long SourceGodownId { get; set; }
    public decimal IssuedQuantity { get; set; }
    public decimal Rate { get; set; }
}

public sealed class MaterialOutEwayDetailsInput
{
    public string EwayBillNumber { get; set; } = string.Empty;
    public DateOnly? EwayBillDate { get; set; }
    public string ConsolidatedEwayBillNumber { get; set; } = string.Empty;
    public DateOnly? ConsolidatedEwayBillDate { get; set; }
    public string EwaySubType { get; set; } = "Others";
    public string EwayDocumentType { get; set; } = "Delivery Challan";
    public string ConsignorMailingName { get; set; } = string.Empty;
    public string ConsignorGstin { get; set; } = string.Empty;
    public string ConsignorState { get; set; } = string.Empty;
    public string ConsignorAddress1 { get; set; } = string.Empty;
    public string ConsignorAddress2 { get; set; } = string.Empty;
    public string ConsignorPincode { get; set; } = string.Empty;
    public string ConsignorPlace { get; set; } = string.Empty;
    public string ConsignorActualState { get; set; } = string.Empty;
    public string ConsigneeMailingName { get; set; } = string.Empty;
    public string ConsigneeGstin { get; set; } = string.Empty;
    public string ConsigneeState { get; set; } = string.Empty;
    public string ConsigneeAddress1 { get; set; } = string.Empty;
    public string ConsigneeAddress2 { get; set; } = string.Empty;
    public string ConsigneePincode { get; set; } = string.Empty;
    public string ConsigneePlace { get; set; } = string.Empty;
    public string ConsigneeActualState { get; set; } = string.Empty;
    public string PinToPinDistance { get; set; } = string.Empty;
    public string TransporterName { get; set; } = string.Empty;
    public string TransporterId { get; set; } = string.Empty;
    public string TransportMode { get; set; } = "Not Applicable";
    public string TransportDocumentNumber { get; set; } = string.Empty;
    public DateOnly? TransportDocumentDate { get; set; }
    public string VehicleNumber { get; set; } = string.Empty;
    public string VehicleType { get; set; } = "Not Applicable";
}

public sealed class MaterialOutSaveResult
{
    public long VoucherId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
}
