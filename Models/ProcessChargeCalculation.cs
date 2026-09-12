namespace TexTrack.Web.Models;

public enum ProcessChargeMode { Total, PerUnit }

public static class ProcessChargeCalculation
{
    public static (decimal Rate, decimal Total) Calculate(decimal quantity, decimal rate, decimal total, ProcessChargeMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new InvalidOperationException("Invalid process charge calculation mode.");
        if (quantity < 0 || (mode == ProcessChargeMode.PerUnit ? rate : total) < 0)
            throw new InvalidOperationException("Quantity and process charges cannot be negative.");
        if (quantity == 0) return mode == ProcessChargeMode.PerUnit ? (rate, 0) : (0, total);
        rate = decimal.Round(rate, 4, MidpointRounding.AwayFromZero);
        total = decimal.Round(total, 4, MidpointRounding.AwayFromZero);
        return mode == ProcessChargeMode.PerUnit
            ? (rate, decimal.Round(quantity * rate, 4, MidpointRounding.AwayFromZero))
            : (decimal.Round(total / quantity, 4, MidpointRounding.AwayFromZero), total);
    }
}
