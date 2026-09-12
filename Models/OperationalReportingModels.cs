namespace TexTrack.Web.Models;

public sealed class OperationalReportLookups
{
    public List<LookupItem> JobWorkers { get; set; } = new();
    public List<LookupItem> StockGroups { get; set; } = new();
    public List<LookupItem> StockCategories { get; set; } = new();
    public List<LookupItem> StockItems { get; set; } = new();
    public List<LookupItem> Godowns { get; set; } = new();
    public List<LookupItem> Processes { get; set; } = new();
}

public sealed class VoucherUqcQuantityRow
{
    public long VoucherId { get; set; }
    public string UqcName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
}

public enum JobWorkerControlStatusFilter { Pending, Completed, All }

public sealed class JobWorkerControlFilter
{
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public long? JobWorkerId { get; set; }
    public string JobWorker { get; set; } = string.Empty;
    public string Item { get; set; } = string.Empty;
    public long? StockGroupId { get; set; }
    public long? StockCategoryId { get; set; }
    public string JwoNumber { get; set; } = string.Empty;
    public string MasterJobOrderNumber { get; set; } = string.Empty;
    public string Batch { get; set; } = string.Empty;
    public string BatchOrOrder { get; set; } = string.Empty;
    public long? ProcessId { get; set; }
    public string Process { get; set; } = string.Empty;
    public JobWorkerControlStatusFilter Status { get; set; } = JobWorkerControlStatusFilter.Pending;
    public bool IncludeCompleted
    {
        get => Status == JobWorkerControlStatusFilter.All;
        set => Status = value ? JobWorkerControlStatusFilter.All : JobWorkerControlStatusFilter.Pending;
    }
}

public sealed class JobWorkerControlReport
{
    public List<JobWorkerControlOrder> Orders { get; set; } = new();
    public List<JobWorkerControlBatch> Batches { get; set; } = new();
}

public sealed class JobWorkerControlBatch
{
    public string RowKey => $"jwc-batch-{Batch.Trim().ToUpperInvariant()}";
    public string Batch { get; set; } = string.Empty;
    public string JobWorkerNames => string.Join(", ", Orders.Select(x => x.JobWorkerName).Distinct(StringComparer.OrdinalIgnoreCase));
    public List<JobWorkerControlOrder> Orders { get; set; } = new();
    public decimal MaterialCost => Orders.Sum(x => x.MaterialCost);
    public decimal ProcessCost => Orders.Sum(x => x.ProcessCost);
    public decimal TotalCost => MaterialCost + ProcessCost;
}

public sealed class JobWorkerControlOrder
{
    public string RowKey => $"jwc-{JwoVoucherId}";
    public long JwoVoucherId { get; set; }
    public string JwoNumber { get; set; } = string.Empty;
    public long? MasterJobOrderId { get; set; }
    public string MasterJobOrderNumber { get; set; } = string.Empty;
    public DateOnly JwoDate { get; set; }
    public string Batch { get; set; } = string.Empty;
    public string JobWorkerName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TotalFinishedGoodsOrdered { get; set; }
    public decimal TotalFinishedGoodsReceived { get; set; }
    public decimal TotalFinishedGoodsPending => TotalFinishedGoodsOrdered - TotalFinishedGoodsReceived;
    public List<JobWorkerMaterialIssueRow> Issues { get; set; } = new();
    public List<JobWorkerReceiptRow> Receipts { get; set; } = new();
    public List<JobWorkerMaterialPositionRow> MaterialPositions { get; set; } = new();
    public List<JobWorkerFinishedGoodPositionRow> FinishedGoodPositions { get; set; } = new();
    public decimal IssuedValue => Issues.Sum(x => x.Value);
    public decimal ReceivedValue => Receipts.Sum(x => x.Value);
    public string ProcessNames { get; set; } = string.Empty;
    // Production cost is recognized only when issued material is actually consumed.
    // MaterialInFinishedGood.MaterialValue is the persisted allocation-snapshot value
    // of that consumption; unconsumed stock still held by the job worker is excluded.
    public decimal MaterialCost => Receipts.Sum(x => x.MaterialValue);
    public decimal ProcessCost => Receipts.Sum(x => x.ProcessCharge);
    public decimal TotalCost => MaterialCost + ProcessCost;
    public decimal CostPerUnit => TotalFinishedGoodsReceived == 0 ? 0 : TotalCost / TotalFinishedGoodsReceived;
}

public sealed class JobWorkerFinishedGoodPositionRow
{
    public long JwoFinishedGoodId { get; set; }
    public string FinishedGoodName { get; set; } = string.Empty;
    public string UqcName { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal PendingQuantity => Math.Max(0, OrderedQuantity - ReceivedQuantity);
}

public sealed class JobWorkerMaterialPositionRow
{
    public string RowKey => $"jwc-material-{JwoComponentId}";
    public long JwoComponentId { get; set; }
    public string FinishedGoodName { get; set; } = string.Empty;
    public string ComponentName { get; set; } = string.Empty;
    public string UqcName { get; set; } = string.Empty;
    public decimal RequiredQuantity { get; set; }
    public decimal IssuedQuantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public decimal PendingToIssue => Math.Max(0, RequiredQuantity - IssuedQuantity);
    public decimal BalanceWithJobWorker => IssuedQuantity - ConsumedQuantity;
}
public sealed class JobWorkerMaterialIssueRow
{
    public long VoucherId { get; set; }
    public long JwoComponentId { get; set; }
    public DateOnly Date { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public string ComponentName { get; set; } = string.Empty;
    public string UqcName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal PendingQuantity { get; set; }
    public decimal Rate { get; set; }
    public decimal Value { get; set; }
}

public sealed class JobWorkerReceiptRow
{
    public long VoucherId { get; set; }
    public long JwoFinishedGoodId { get; set; }
    public DateOnly Date { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public string FinishedGoodName { get; set; } = string.Empty;
    public string UqcName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal PendingQuantity { get; set; }
    public decimal Rate { get; set; }
    public decimal Value { get; set; }
    public decimal MaterialValue { get; set; }
    public decimal ProcessCharge { get; set; }
    public List<JobWorkerReceiptVariantRow> Variants { get; set; } = new();
    public decimal MaterialCostPerUnit => Quantity == 0 ? 0 : MaterialValue / Quantity;
    public decimal ProcessCostPerUnit => Quantity == 0 ? 0 : ProcessCharge / Quantity;
    public decimal OverallCostPerUnit => Quantity == 0 ? 0 : Value / Quantity;
}

public sealed class JobWorkerReceiptVariantRow
{
    public string ColourName { get; set; } = "N/A";
    public string SizeName { get; set; } = "N/A";
    public decimal Quantity { get; set; }
}

public enum PendingMaterialIssueStatusFilter { All, PartiallyIssued, FullyPending }

public sealed class PendingMaterialIssueFilter
{
    public long? JobWorkerId { get; set; }
    public string JobWorker { get; set; } = string.Empty;
    public PendingMaterialIssueStatusFilter Status { get; set; } = PendingMaterialIssueStatusFilter.All;
}

public sealed class PendingMaterialIssueReport
{
    public List<PendingMaterialIssueOrderRow> Orders { get; set; } = new();
}

public sealed class PendingMaterialIssueOrderRow
{
    public string RowKey => $"pending-material-order-{JwoVoucherId}";
    public long JwoVoucherId { get; set; }
    public string JwoNumber { get; set; } = string.Empty;
    public DateOnly JwoDate { get; set; }
    public string Batch { get; set; } = string.Empty;
    public string JobWorkerName { get; set; } = string.Empty;
    public List<PendingMaterialIssueComponentRow> Components { get; set; } = new();
}

public sealed class PendingMaterialIssueComponentRow
{
    public long JwoComponentId { get; set; }
    public string FinishedGoodName { get; set; } = string.Empty;
    public string ComponentName { get; set; } = string.Empty;
    public string UqcName { get; set; } = string.Empty;
    public decimal RequiredQuantity { get; set; }
    public decimal IssuedQuantity { get; set; }
    public decimal PendingQuantity => Math.Max(0, RequiredQuantity - IssuedQuantity);
    public string IssueStatus => IssuedQuantity <= 0 ? "Fully Pending" : "Partially Issued";
}

public enum ClosingQuantityFilter { All, Positive, Negative, Zero }

public sealed class InventoryReportFilter
{
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public long? StockItemId { get; set; }
    public string StockItem { get; set; } = string.Empty;
    public long? StockGroupId { get; set; }
    public long? StockCategoryId { get; set; }
    public long? GodownId { get; set; }
    public ClosingQuantityFilter QuantityFilter { get; set; }
}

public sealed class ClosingStockGroupRow
{
    public string RowKey => $"closing-group-{StockGroupId}-{UqcId}";
    public long StockGroupId { get; set; }
    public long UqcId { get; set; }
    public string StockGroupName { get; set; } = string.Empty;
    public string UqcName { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public decimal Quantity { get; set; }
    public decimal Value { get; set; }
    public decimal AverageRate => Quantity == 0 ? 0 : Value / Quantity;
}

public sealed class ClosingStockItemDetail
{
    public string GroupName { get; set; } = string.Empty;
    public string UqcName { get; set; } = string.Empty;
    public List<ClosingStockItemDetailRow> Items { get; set; } = new();
}

public sealed class ClosingStockItemDetailRow
{
    public string RowKey => $"closing-item-{StockItemId}";
    public long StockItemId { get; set; }
    public string StockItemName { get; set; } = string.Empty;
    public string StockCategoryName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Value { get; set; }
    public decimal AverageRate => Quantity == 0 ? 0 : Value / Quantity;
    public List<ClosingStockVariantDetailRow> Variants { get; set; } = new();
}

public sealed class ClosingStockVariantDetailRow
{
    public long? StockItemVariantId { get; set; }
    public string ColourName { get; set; } = "N/A";
    public string SizeName { get; set; } = "N/A";
    public decimal Quantity { get; set; }
    public decimal Value { get; set; }
}

public sealed class ClosingStockGodownDetail
{
    public long StockItemId { get; set; }
    public string StockItemName { get; set; } = string.Empty;
    public string UqcName { get; set; } = string.Empty;
    public List<ClosingStockGodownRow> Godowns { get; set; } = new();
}

public sealed class ClosingStockGodownRow
{
    public string RowKey => $"closing-godown-{GodownId}";
    public long GodownId { get; set; }
    public string GodownName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Value { get; set; }
    public decimal AverageRate => Quantity == 0 ? 0 : Value / Quantity;
}

public sealed class StockRegisterSummaryRow
{
    public string RowKey => $"register-{StockItemId}";
    public long StockItemId { get; set; }
    public string StockItemName { get; set; } = string.Empty;
    public string StockCategoryName { get; set; } = string.Empty;
    public string GodownName { get; set; } = string.Empty;
    public string UqcName { get; set; } = string.Empty;
    public decimal OpeningQuantity { get; set; }
    public decimal InwardQuantity { get; set; }
    public decimal OutwardQuantity { get; set; }
    public decimal ClosingQuantity => OpeningQuantity + InwardQuantity - OutwardQuantity;
    public decimal Value { get; set; }
}

public sealed class StockRegisterDetail
{
    public long StockItemId { get; set; }
    public string StockItemName { get; set; } = string.Empty;
    public string GodownName { get; set; } = string.Empty;
    public string UqcName { get; set; } = string.Empty;
    public decimal OpeningQuantity { get; set; }
    public decimal ClosingQuantity { get; set; }
    public List<StockRegisterMovementRow> Movements { get; set; } = new();
}

public sealed class StockRegisterMovementRow
{
    public string RowKey => $"stock-movement-{MovementId}";
    public long MovementId { get; set; }
    public long VoucherId { get; set; }
    public string VoucherSystemTypeCode { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string VoucherType { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public string PartyOrJobWorker { get; set; } = string.Empty;
    public string Godown { get; set; } = string.Empty;
    public string Batch { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public decimal Value { get; set; }
    public decimal Inward { get; set; }
    public decimal Outward { get; set; }
    public decimal RunningQuantity { get; set; }
}

public enum JobWorkerAgingQuickFilter { Open, Overdue, WithMaterialBalance, NoMovement, All }
public enum JobWorkerAgingStatusFilter { All, NotStarted, InProcess, PartiallyReceived, Overdue, Completed, CompletedLate }
public enum JobWorkerAgingAgeBucket { All, Days0To7, Days8To15, Days16To30, Days31To60, Days61To90, DaysOver90 }

public sealed class JobWorkerAgingFilter
{
    public DateOnly AsOnDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public long? JobWorkerId { get; set; }
    public string Batch { get; set; } = string.Empty;
    public string JwoNumber { get; set; } = string.Empty;
    public string FinishedGood { get; set; } = string.Empty;
    public long? StockGroupId { get; set; }
    public long? StockCategoryId { get; set; }
    public JobWorkerAgingQuickFilter QuickFilter { get; set; } = JobWorkerAgingQuickFilter.Open;
    public JobWorkerAgingStatusFilter Status { get; set; } = JobWorkerAgingStatusFilter.All;
    public JobWorkerAgingAgeBucket AgeBucket { get; set; } = JobWorkerAgingAgeBucket.All;
    public int? MaterialAgeMinimum { get; set; }
    public int? MaterialAgeMaximum { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class JobWorkerAgingReport
{
    public List<JobWorkerAgingReportRow> Rows { get; set; } = new();
    public int TotalRows { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalPages => TotalRows == 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);
    public int ActiveJwoCount { get; set; }
    public int OverdueJwoCount { get; set; }
    public int MaterialBalanceCount { get; set; }
    public int? OldestOpenDays { get; set; }
    public int? OldestMaterialDays { get; set; }
    public List<JobWorkerAgingUqcQuantity> FinishedGoodPending { get; set; } = new();
}

public sealed class JobWorkerAgingReportRow
{
    public string RowKey => $"job-worker-aging-{JwoVoucherId}";
    public long JwoVoucherId { get; set; }
    public string JobWorkerName { get; set; } = string.Empty;
    public string ResponsibleJobWorkers { get; set; } = string.Empty;
    public List<JobWorkerAgingJobWorker> ResponsibleJobWorkerRows { get; set; } = new();
    public string FinishedGoodNames { get; set; } = string.Empty;
    public string CurrentStage { get; set; } = string.Empty;
    public string Batch { get; set; } = string.Empty;
    public string JwoNumber { get; set; } = string.Empty;
    public DateOnly JwoDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public int DaysOpen { get; set; }
    public int? DaysOverdue { get; set; }
    public int? DaysRemaining { get; set; }
    public string DuePosition { get; set; } = string.Empty;
    public List<JobWorkerAgingUqcQuantity> FinishedGoods { get; set; } = new();
    public List<JobWorkerAgingUqcQuantity> Materials { get; set; } = new();
    public bool HasMaterialBalance { get; set; }
    public DateOnly? OldestOutstandingMaterialDate { get; set; }
    public int? OldestOutstandingMaterialAge { get; set; }
    public DateOnly? LastMaterialOutDate { get; set; }
    public DateOnly? LastMaterialInDate { get; set; }
    public DateOnly? LastMovementDate { get; set; }
    public int? DaysSinceLastMovement { get; set; }
    public DateOnly? CompletionDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string MaterialOutStatus { get; set; } = "NOT SENT";
}

public sealed class JobWorkerAgingJobWorker
{
    public long JobWorkerId { get; set; }
    public string JobWorkerName { get; set; } = string.Empty;
}

public sealed class JobWorkerAgingSlabReport
{
    public List<JobWorkerAgingSlabRow> Slabs { get; set; } = new();
}

public sealed class JobWorkerAgingSlabRow
{
    public int Index { get; set; }
    public int MinimumDays { get; set; }
    public int? MaximumDays { get; set; }
    public string Label => MaximumDays is null ? $"{MinimumDays}+ Days" : $"{MinimumDays}–{MaximumDays} Days";
    public int JwoCount { get; set; }
    public List<JobWorkerAgingUqcQuantity> FinishedGoodPending { get; set; } = new();
}

public sealed class JobWorkerAgingUqcQuantity
{
    public string UqcName { get; set; } = string.Empty;
    public decimal OrderedOrRequired { get; set; }
    public decimal ReceivedOrIssued { get; set; }
    public decimal Consumed { get; set; }
    public decimal PendingOrBalance { get; set; }
}

public sealed class JobWorkerAgingDetail
{
    public JobWorkerAgingReportRow Summary { get; set; } = new();
    public List<JobWorkerAgingFinishedGoodRow> FinishedGoods { get; set; } = new();
    public List<JobWorkerAgingMaterialRow> Materials { get; set; } = new();
    public List<JobWorkerAgingMaterialLotRow> MaterialLots { get; set; } = new();
    public List<JobWorkerAgingStageRow> Stages { get; set; } = new();
}

public sealed class JobWorkerAgingStageRow
{
    public long BomStageId { get; set; }
    public long StageAssignmentId { get; set; }
    public int StageNumber { get; set; }
    public string StagePath { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string JobWorkerName { get; set; } = string.Empty;
    public int AssignmentVersion { get; set; }
    public DateOnly? ExpectedCompletionDate { get; set; }
    public bool IsFinalStage { get; set; }
    public string OutputItemName { get; set; } = string.Empty;
    public string UqcName { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal PendingQuantity => Math.Max(0, OrderedQuantity - ReceivedQuantity);
    public DateOnly? FirstMaterialOutDate { get; set; }
    public DateOnly? LastMaterialOutDate { get; set; }
    public int? OldestOutstandingMaterialAge { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class JobWorkerAgingFinishedGoodRow
{
    public string FinishedGoodName { get; set; } = string.Empty;
    public string ColourName { get; set; } = "N/A";
    public string SizeName { get; set; } = "N/A";
    public string UqcName { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal PendingQuantity => Math.Max(0, OrderedQuantity - ReceivedQuantity);
}

public sealed class JobWorkerAgingMaterialRow
{
    public long JwoComponentId { get; set; }
    public string MaterialName { get; set; } = string.Empty;
    public string UqcName { get; set; } = string.Empty;
    public decimal RequiredQuantity { get; set; }
    public decimal IssuedQuantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public decimal BalanceQuantity => IssuedQuantity - ConsumedQuantity;
    public int? OldestOutstandingAge { get; set; }
}

public sealed class JobWorkerAgingMaterialLotRow
{
    public long MaterialOutVoucherId { get; set; }
    public string MaterialOutNumber { get; set; } = string.Empty;
    public DateOnly MaterialOutDate { get; set; }
    public long? BomStageId { get; set; }
    public long? StageAssignmentId { get; set; }
    public string StageName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string JobWorkerName { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public string UqcName { get; set; } = string.Empty;
    public decimal IssuedQuantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public decimal OutstandingQuantity => IssuedQuantity - ConsumedQuantity;
    public int? AgeDays { get; set; }
}

public enum JobWorkerExceptionPriority { Critical, High, Attention, Normal }
public enum JobWorkerExceptionQuickFilter
{
    ExceptionsOnly, CriticalOnly, OverdueOnly, NoMovement, OldMaterial,
    MaterialBalance, DueSoon, AllOpen, CompletedHistory
}

public static class JobWorkerExceptionThresholds
{
    public const int DueSoonDays = 3;
    public const int NoMovementAttentionDays = 3;
    public const int NoMovementHighDays = 5;
    public const int NoMovementCriticalDays = 7;
    public const int MaterialAgeHighDays = 30;
    public const int MaterialAgeCriticalDays = 45;
    public const int OverdueCriticalDays = 7;
}

public sealed class JobWorkerExceptionFilter
{
    public DateOnly AsOnDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public long? JobWorkerId { get; set; }
    public string Batch { get; set; } = string.Empty;
    public string JwoNumber { get; set; } = string.Empty;
    public string FinishedGood { get; set; } = string.Empty;
    public long? StockGroupId { get; set; }
    public long? StockCategoryId { get; set; }
    public JobWorkerExceptionPriority? Priority { get; set; }
    public JobWorkerAgingStatusFilter Status { get; set; } = JobWorkerAgingStatusFilter.All;
    public JobWorkerExceptionQuickFilter QuickFilter { get; set; } = JobWorkerExceptionQuickFilter.ExceptionsOnly;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class JobWorkerExceptionReport
{
    public List<JobWorkerExceptionRow> Rows { get; set; } = new();
    public int TotalRows { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalPages => TotalRows == 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);
    public int CriticalCount { get; set; }
    public int HighCount { get; set; }
    public int AttentionCount { get; set; }
    public int OverdueCount { get; set; }
    public int NoMovementCount { get; set; }
    public int OldMaterialCount { get; set; }
    public int MaterialBalanceCount { get; set; }
    public int OpenCount { get; set; }
}

public sealed class JobWorkerPortfolioReport
{
    public List<JobWorkerPortfolioRow> Rows { get; set; } = new();
}

public sealed class JobWorkerPortfolioRow
{
    public string RowKey => $"job-worker-portfolio-{JobWorkerId}";
    public long JobWorkerId { get; set; }
    public string JobWorkerName { get; set; } = string.Empty;
    public int ActiveJwoCount { get; set; }
    public int CriticalCount { get; set; }
    public int HighCount { get; set; }
    public int AttentionCount { get; set; }
    public int OverdueCount { get; set; }
    public int MaterialBalanceCount { get; set; }
    public int OldMaterialCount { get; set; }
    public int NoMovementCount { get; set; }
    public int? OldestMaterialAge { get; set; }
    public int? OldestActiveAge { get; set; }
    public List<JobWorkerAgingUqcQuantity> FinishedGoodPending { get; set; } = new();
}

public sealed class JobWorkerExceptionRow
{
    public string RowKey => $"job-worker-exception-{Aging.JwoVoucherId}";
    public JobWorkerAgingReportRow Aging { get; set; } = new();
    public JobWorkerExceptionPriority Priority { get; set; }
    public List<string> Reasons { get; set; } = new();
    public string ReasonText => Reasons.Count == 0 ? "—" : string.Join(" • ", Reasons);
}
