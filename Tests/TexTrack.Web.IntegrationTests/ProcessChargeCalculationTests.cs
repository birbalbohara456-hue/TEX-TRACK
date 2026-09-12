using TexTrack.Web.Models;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class ProcessChargeCalculationTests
{
    [Fact]
    public void Per_unit_entry_recalculates_total_when_quantity_changes()
    {
        Assert.Equal((12.5m, 625m), ProcessChargeCalculation.Calculate(50, 12.5m, 999, ProcessChargeMode.PerUnit));
        Assert.Equal((12.5m, 1250m), ProcessChargeCalculation.Calculate(100, 12.5m, 625, ProcessChargeMode.PerUnit));
    }
    [Fact]
    public void Total_entry_preserves_total_and_rounds_only_derived_rate()
    {
        Assert.Equal((33.3333m, 100m), ProcessChargeCalculation.Calculate(3, 999, 100, ProcessChargeMode.Total));
        Assert.Equal((25m, 100m), ProcessChargeCalculation.Calculate(4, 33.3333m, 100, ProcessChargeMode.Total));
    }
    [Fact]
    public void Zero_quantity_preserves_the_entered_value_without_dividing()
    {
        Assert.Equal((0m, 100m), ProcessChargeCalculation.Calculate(0, 5, 100, ProcessChargeMode.Total));
        Assert.Equal((5m, 0m), ProcessChargeCalculation.Calculate(0, 5, 100, ProcessChargeMode.PerUnit));
    }
    [Fact]
    public void Invalid_mode_and_negative_entered_values_are_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => ProcessChargeCalculation.Calculate(1, 1, 1, (ProcessChargeMode)99));
        Assert.Throws<InvalidOperationException>(() => ProcessChargeCalculation.Calculate(1, -1, 1, ProcessChargeMode.PerUnit));
        Assert.Throws<InvalidOperationException>(() => ProcessChargeCalculation.Calculate(1, 1, -1, ProcessChargeMode.Total));
    }
}
