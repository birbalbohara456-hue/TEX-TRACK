namespace TexTrack.Web.Domain;

public sealed class InventoryPostingSequence
{
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long LastPostingOrder { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string ModifiedBy { get; set; } = "Developer";
}

public sealed class InventoryPosting
{
    public long Id { get; set; }
    public Guid OperationId { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long FinancialYearId { get; set; }
    public FinancialYear FinancialYear { get; set; } = null!;
    public long VoucherId { get; set; }
    public Voucher Voucher { get; set; } = null!;
    public DateOnly EffectiveDate { get; set; }
    public TimeOnly? EffectiveTime { get; set; }
    public long PostingOrder { get; set; }
    public string EventKind { get; set; } = "Original";
    public long? ReversalOfPostingId { get; set; }
    public InventoryPosting? ReversalOfPosting { get; set; }
    public int AlgorithmVersion { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
}

public sealed class InventoryCostLayer
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long ReceiptMovementId { get; set; }
    public StockMovement ReceiptMovement { get; set; } = null!;
    public int LayerSequence { get; set; }
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public long? StockItemVariantId { get; set; }
    public StockItemVariant? StockItemVariant { get; set; }
    public long UqcId { get; set; }
    public Uqc Uqc { get; set; } = null!;
    public long GodownId { get; set; }
    public Godown Godown { get; set; } = null!;
    public string OriginKind { get; set; } = string.Empty;
    public long? SourceAllocationId { get; set; }
    public InventoryCostAllocation? SourceAllocation { get; set; }
    public decimal OriginalQuantity { get; set; }
    public decimal OriginalValue { get; set; }
    public decimal UnitCost { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal RemainingValue { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public long PostingOrder { get; set; }
    public int MovementLineOrder { get; set; }
    public int AlgorithmVersion { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
}

public sealed class InventoryCostAllocation
{
    public long Id { get; set; }
    public Guid OperationId { get; set; }
    public InventoryPosting Posting { get; set; } = null!;
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long OutwardMovementId { get; set; }
    public StockMovement OutwardMovement { get; set; } = null!;
    public long SourceLayerId { get; set; }
    public InventoryCostLayer SourceLayer { get; set; } = null!;
    public int AllocationSequence { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public decimal AllocatedValue { get; set; }
    public decimal UnitCostSnapshot { get; set; }
    public long? ReversalOfAllocationId { get; set; }
    public InventoryCostAllocation? ReversalOfAllocation { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public int AlgorithmVersion { get; set; } = 1;
}

public sealed class InventoryValuationPosition
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public long? StockItemVariantId { get; set; }
    public StockItemVariant? StockItemVariant { get; set; }
    public long UqcId { get; set; }
    public Uqc Uqc { get; set; } = null!;
    public long GodownId { get; set; }
    public Godown Godown { get; set; } = null!;
    public string State { get; set; } = "Legacy";
    public DateOnly? EarliestDirtyDate { get; set; }
    public long? EarliestDirtyPostingOrder { get; set; }
    public Guid? CurrentSuccessfulRunId { get; set; }
    public InventoryValuationRun? CurrentSuccessfulRun { get; set; }
    public string LastErrorSummary { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class InventoryValuationSetting
{
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string BookMethod { get; set; } = "LegacyPersistedValue";
    public string LifecycleState { get; set; } = "NotInitialized";
    public DateOnly? CutoverDate { get; set; }
    public Guid? CutoverRunId { get; set; }
    public InventoryValuationRun? CutoverRun { get; set; }
    public int ActiveAlgorithmVersion { get; set; } = 1;
    public DateTimeOffset? ActivatedAtUtc { get; set; }
    public string ActivatedBy { get; set; } = string.Empty;
    public string ActivationReason { get; set; } = string.Empty;
    public Guid? LastSuccessfulReconciliationRunId { get; set; }
    public InventoryValuationRun? LastSuccessfulReconciliationRun { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class InventoryValuationRun
{
    public Guid Id { get; set; }
    public Guid IdempotencyKey { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string RunKind { get; set; } = string.Empty;
    public int AlgorithmVersion { get; set; } = 1;
    public int SchemaVersion { get; set; } = 1;
    public string RequestedScopeJson { get; set; } = "{}";
    public DateOnly? CutoffDate { get; set; }
    public string State { get; set; } = "Pending";
    public DateTimeOffset RequestedAtUtc { get; set; }
    public string RequestedBy { get; set; } = "Developer";
    public DateTimeOffset? StartedAtUtc { get; set; }
    public string StartedBy { get; set; } = string.Empty;
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public string FinishedBy { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public string LeaseOwner { get; set; } = string.Empty;
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset? HeartbeatAtUtc { get; set; }
    public DateOnly? EarliestAffectedDate { get; set; }
    public long? EarliestAffectedPostingOrder { get; set; }
    public long AffectedPositionCount { get; set; }
    public long AffectedMovementCount { get; set; }
    public long AffectedLayerCount { get; set; }
    public decimal QuantityBefore { get; set; }
    public decimal QuantityAfter { get; set; }
    public decimal ValueBefore { get; set; }
    public decimal ValueAfter { get; set; }
    public string FailureSummary { get; set; } = string.Empty;
    public string DiagnosticReference { get; set; } = string.Empty;
    public Guid AuditCorrelationId { get; set; }
}
