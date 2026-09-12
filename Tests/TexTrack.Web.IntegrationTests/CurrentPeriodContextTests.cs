using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class CurrentPeriodContextTests
{
    [Fact]
    public void Valid_period_updates_once_and_notifies_reports()
    {
        var context = new CurrentPeriodContext();
        var notifications = 0;
        context.Changed += () => notifications++;

        var success = context.TrySet(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), out var error);

        Assert.True(success);
        Assert.Equal(string.Empty, error);
        Assert.Equal(new DateOnly(2026, 7, 1), context.From);
        Assert.Equal(new DateOnly(2026, 7, 31), context.To);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void Invalid_period_is_rejected_without_changing_active_period()
    {
        var context = new CurrentPeriodContext();
        var originalFrom = context.From;
        var originalTo = context.To;

        var success = context.TrySet(new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 31), out var error);

        Assert.False(success);
        Assert.Equal("From date cannot be later than To date.", error);
        Assert.Equal(originalFrom, context.From);
        Assert.Equal(originalTo, context.To);
    }
}
