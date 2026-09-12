namespace TexTrack.Web.Models;

public sealed record StockPositionRequest(
    long StockItemId,
    long? StockItemVariantId,
    long UqcId,
    long GodownId,
    DateOnly AsOnDate);

public sealed record StockPositionSnapshot(
    long StockItemId,
    long? StockItemVariantId,
    long UqcId,
    long GodownId,
    DateOnly AsOnDate,
    decimal Quantity,
    decimal Value)
{
    public decimal AverageRate => Quantity == 0 ? 0 : Value / Quantity;
}
