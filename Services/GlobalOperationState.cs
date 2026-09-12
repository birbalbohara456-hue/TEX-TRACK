namespace TexTrack.Web.Services;

public sealed class GlobalOperationState
{
    private readonly object sync = new();
    private readonly List<Operation> activeOperations = [];
    private long nextOperationId;

    public bool IsBusy { get; private set; }
    public string Message { get; private set; } = "Working…";
    public event Action? Changed;

    public IDisposable Begin(string message)
    {
        long operationId;
        lock (sync)
        {
            operationId = ++nextOperationId;
            activeOperations.Add(new Operation(operationId, NormalizeMessage(message)));
            IsBusy = true;
            Message = activeOperations[^1].Message;
        }
        Changed?.Invoke();
        return new Lease(this, operationId);
    }

    public async Task RunAsync(string message, Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var lease = Begin(message);
        await operation();
    }

    public async Task<T> RunAsync<T>(string message, Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var lease = Begin(message);
        return await operation();
    }

    private void End(long operationId)
    {
        lock (sync)
        {
            var index = activeOperations.FindIndex(x => x.Id == operationId);
            if (index < 0) return;

            activeOperations.RemoveAt(index);
            IsBusy = activeOperations.Count > 0;
            Message = IsBusy ? activeOperations[^1].Message : "Working…";
        }
        Changed?.Invoke();
    }

    private static string NormalizeMessage(string message) =>
        string.IsNullOrWhiteSpace(message) ? "Working…" : message;

    private sealed record Operation(long Id, string Message);

    private sealed class Lease(GlobalOperationState owner, long operationId) : IDisposable
    {
        private GlobalOperationState? current = owner;
        public void Dispose() => Interlocked.Exchange(ref current, null)?.End(operationId);
    }
}
