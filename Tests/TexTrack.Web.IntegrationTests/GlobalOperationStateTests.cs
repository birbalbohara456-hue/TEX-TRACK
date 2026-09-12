using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class GlobalOperationStateTests
{
    [Fact]
    public void Lease_marks_application_busy_until_disposed()
    {
        var state = new GlobalOperationState();
        using (state.Begin("Loading report…"))
        {
            Assert.True(state.IsBusy);
            Assert.Equal("Loading report…", state.Message);
        }
        Assert.False(state.IsBusy);
        Assert.Equal("Working…", state.Message);
    }

    [Fact]
    public void Nested_lease_restores_outer_message_and_keeps_application_busy()
    {
        var state = new GlobalOperationState();
        using var outer = state.Begin("Loading lookups…");
        using (state.Begin("Loading rows…"))
        {
            Assert.True(state.IsBusy);
            Assert.Equal("Loading rows…", state.Message);
        }
        Assert.True(state.IsBusy);
        Assert.Equal("Loading lookups…", state.Message);
    }

    [Fact]
    public void Out_of_order_disposal_preserves_latest_active_operation()
    {
        var state = new GlobalOperationState();
        var first = state.Begin("First…");
        using var second = state.Begin("Second…");
        first.Dispose();
        Assert.True(state.IsBusy);
        Assert.Equal("Second…", state.Message);
    }

    [Fact]
    public void Lease_disposal_is_idempotent()
    {
        var state = new GlobalOperationState();
        var lease = state.Begin("Loading…");
        lease.Dispose();
        lease.Dispose();
        Assert.False(state.IsBusy);
    }

    [Fact]
    public async Task RunAsync_releases_busy_state_when_operation_throws()
    {
        var state = new GlobalOperationState();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            state.RunAsync("Loading…", () => throw new InvalidOperationException("failure")));
        Assert.False(state.IsBusy);
        Assert.Equal("Working…", state.Message);
    }
}
