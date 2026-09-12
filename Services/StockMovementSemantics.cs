namespace TexTrack.Web.Services;

/// <summary>
/// Canonical stock-ledger vocabulary. Reporting must classify movements here rather
/// than maintaining private string lists that drift when new voucher types arrive.
/// </summary>
public static class StockMovementSemantics
{
    public const string MaterialOutSource = "MaterialOutSource";
    public const string MaterialOutDestination = "MaterialOutDestination";
    public const string MaterialOutCancellationSource = "MaterialOutCancellationSource";
    public const string MaterialOutCancellationDestination = "MaterialOutCancellationDestination";
    public const string MaterialInConsumption = "MaterialInConsumption";
    public const string MaterialInCancellationConsumption = "MaterialInCancellationConsumption";
    public const string MaterialInFinishedGoods = "MaterialInFinishedGoods";
    public const string MaterialInCancellationFinishedGoods = "MaterialInCancellationFinishedGoods";
    public const string PurchaseInward = "PurchaseInward";
    public const string PurchaseInwardCancellation = "PurchaseInwardCancellation";
    public const string OpeningStockInward = "OpeningStockInward";
    public const string OpeningStockInwardCancellation = "OpeningStockInwardCancellation";
    public const string PurchaseReturnOutward = "PurchaseReturnOutward";
    public const string PurchaseReturnOutwardCancellation = "PurchaseReturnOutwardCancellation";

    public static readonly string[] WorkInProgressKinds =
    [
        MaterialOutDestination,
        MaterialOutCancellationDestination,
        MaterialInConsumption,
        MaterialInCancellationConsumption
    ];
}
