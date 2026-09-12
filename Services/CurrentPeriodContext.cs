namespace TexTrack.Web.Services;

/// <summary>
/// Circuit-scoped active reporting period. Alt+F2 updates this single source of truth;
/// report pages subscribe to Changed and immediately apply the selected range.
/// </summary>
public sealed class CurrentPeriodContext
{
    public DateOnly From { get; private set; } = new(2026, 4, 1);
    public DateOnly To { get; private set; } = new(2027, 3, 31);

    public event Action? Changed;

    public bool TrySet(DateOnly from, DateOnly to, out string error)
    {
        if (from > to)
        {
            error = "From date cannot be later than To date.";
            return false;
        }

        From = from;
        To = to;
        error = string.Empty;
        Changed?.Invoke();
        return true;
    }
}
