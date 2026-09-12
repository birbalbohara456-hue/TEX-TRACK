#pragma warning disable BL0005
using Xunit;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TexTrack.Web.Components.Keyboard;

namespace TexTrack.Web.IntegrationTests;

public sealed class KeyboardContextComponentTests
{
    [Theory]
    [InlineData("23", 2026, 8, 23)]
    [InlineData("23.5", 2026, 5, 23)]
    [InlineData("23/5", 2026, 5, 23)]
    [InlineData("23-5", 2026, 5, 23)]
    [InlineData("23 5", 2026, 5, 23)]
    [InlineData("23.5.24", 2024, 5, 23)]
    [InlineData("2305", 2026, 5, 23)]
    public void Flexible_date_parser_accepts_tally_style_entries(string input, int year, int month, int day)
    {
        Assert.True(FlexibleDateParser.TryParse(input, new DateOnly(2026, 8, 2), out var parsed));
        Assert.Equal(new DateOnly(year, month, day), parsed);
    }

    [Theory]
    [InlineData("31.2")]
    [InlineData("0")]
    [InlineData("not-a-date")]
    public void Flexible_date_parser_rejects_invalid_entries(string input)
    {
        Assert.False(FlexibleDateParser.TryParse(input, new DateOnly(2026, 8, 2), out _));
    }

    [Fact]
    public async Task Render_registers_once_and_parameter_change_updates_same_scope()
    {
        var js = new RecordingJsRuntime();
        var scope = CreateScope(js, KeyboardContextMode.List, canDelete: true);

        await scope.RenderAsync(firstRender: true);
        await scope.RenderAsync(firstRender: false);

        Assert.Single(js.Calls.Where(x => x.Identifier == "texTrackKeyboardContexts.register"));

        scope.CanDelete = false;
        await scope.RenderAsync(firstRender: false);

        var registrations = js.Calls.Where(x => x.Identifier == "texTrackKeyboardContexts.register").ToList();
        Assert.Equal(2, registrations.Count);
        Assert.Equal(registrations[0].Arguments[6], registrations[1].Arguments[6]);
    }

    [Fact]
    public async Task Delete_routes_only_for_list_or_alteration_with_permission()
    {
        var routed = 0;
        var js = new RecordingJsRuntime();
        var scope = CreateScope(js, KeyboardContextMode.Create, canDelete: false);
        scope.DeleteAsync = EventCallback.Factory.Create(new object(), () => routed++);

        await scope.HandleDeleteAsync();
        Assert.Equal(0, routed);

        scope.Mode = KeyboardContextMode.Alteration;
        scope.CanDelete = true;
        await scope.HandleDeleteAsync();
        Assert.Equal(1, routed);
    }

    [Fact]
    public async Task Cancel_routes_only_for_cancellable_voucher_alteration()
    {
        var routed = 0;
        var js = new RecordingJsRuntime();
        var scope = CreateScope(js, KeyboardContextMode.Create, canDelete: false);
        scope.IsVoucherEntry = true;
        scope.CanCancel = true;
        scope.CancelAsync = EventCallback.Factory.Create(new object(), () => routed++);

        await scope.HandleCancelAsync();
        Assert.Equal(0, routed);

        scope.Mode = KeyboardContextMode.Alteration;
        await scope.HandleCancelAsync();
        Assert.Equal(1, routed);

        scope.CanCancel = false;
        await scope.HandleCancelAsync();
        Assert.Equal(1, routed);
    }

    [Fact]
    public async Task Asynchronous_root_render_retries_without_firstRender_dependency()
    {
        var js = new RecordingJsRuntime { RegisterResults = new Queue<bool>([false, true]) };
        var scope = CreateScope(js, KeyboardContextMode.Alteration, canDelete: true);

        await scope.RenderAsync(firstRender: true);
        await scope.RenderAsync(firstRender: false);

        Assert.Equal(2, js.Calls.Count(x => x.Identifier == "texTrackKeyboardContexts.register"));
    }

    [Fact]
    public async Task Conditional_root_replacement_registers_new_root_and_disposal_unregisters_token()
    {
        var js = new RecordingJsRuntime();
        var scope = CreateScope(js, KeyboardContextMode.Alteration, canDelete: true);
        await scope.RenderAsync(firstRender: true);

        scope.RootElementId = "replacement-root";
        await scope.RenderAsync(firstRender: false);
        await scope.DisposeAsync();
        await scope.RenderAsync(firstRender: false);

        Assert.Equal(2, js.Calls.Count(x => x.Identifier == "texTrackKeyboardContexts.register"));
        Assert.Single(js.Calls.Where(x => x.Identifier == "texTrackKeyboardContexts.unregister"));
    }

    private static TestKeyboardContextScope CreateScope(
        RecordingJsRuntime js,
        KeyboardContextMode mode,
        bool canDelete)
    {
        var scope = new TestKeyboardContextScope
        {
            ContextId = "screen-form",
            RootElementId = "screen-form-root",
            ContextType = KeyboardContextType.Form,
            Mode = mode,
            CanDelete = canDelete
        };
        var property = typeof(KeyboardContextScope).GetProperty(
            "JS",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(property);
        property.SetValue(scope, js);
        return scope;
    }

    private sealed class TestKeyboardContextScope : KeyboardContextScope
    {
        public Task RenderAsync(bool firstRender) => base.OnAfterRenderAsync(firstRender);
    }

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<JsCall> Calls { get; } = [];
        public Queue<bool> RegisterResults { get; set; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add(new JsCall(identifier, args ?? []));
            object? result = identifier == "texTrackKeyboardContexts.register"
                ? RegisterResults.TryDequeue(out var connected) ? connected : true
                : default(TValue);
            return ValueTask.FromResult((TValue)result!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed record JsCall(string Identifier, object?[] Arguments);
}
