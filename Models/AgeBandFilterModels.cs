namespace TexTrack.Web.Models;

/// <summary>A reusable, inclusive age range expressed in whole days.</summary>
public sealed record AgeBandRange(int MinimumDays, int? MaximumDays)
{
    public string Label => MaximumDays is null
        ? $"{MinimumDays}+ Days"
        : $"{MinimumDays}–{MaximumDays} Days";

    public bool Contains(int ageDays) =>
        ageDays >= MinimumDays && (MaximumDays is null || ageDays <= MaximumDays.Value);
}

/// <summary>
/// Data-source-neutral age filter state. An empty selection means no age filter; otherwise
/// a row matches when its age falls in any selected band. Reports choose which age field to pass.
/// </summary>
public sealed class AgeBandFilterValue
{
    public static IReadOnlyList<AgeBandRange> DefaultBands { get; } =
    [
        new(0, 45),
        new(46, 90),
        new(91, 120),
        new(121, null)
    ];

    public IReadOnlyList<AgeBandRange> Bands { get; private init; } = DefaultBands;
    public IReadOnlySet<int> SelectedBandIndexes { get; private init; } = new HashSet<int>();
    public bool IsActive => SelectedBandIndexes.Count > 0;

    public string Summary => !IsActive
        ? "All Ages"
        : string.Join(", ", SelectedBandIndexes.OrderBy(x => x).Select(x => Bands[x].Label));

    public bool Matches(int? ageDays)
    {
        if (!IsActive) return true;
        return ageDays is not null && SelectedBandIndexes.Any(index => Bands[index].Contains(ageDays.Value));
    }

    public static AgeBandFilterValue Create(
        IEnumerable<AgeBandRange> bands,
        IEnumerable<int>? selectedBandIndexes = null)
    {
        var normalizedBands = bands.ToList();
        if (normalizedBands.Count is < 1 or > 4)
            throw new ArgumentException("Define between one and four age bands.", nameof(bands));

        for (var index = 0; index < normalizedBands.Count; index++)
        {
            var band = normalizedBands[index];
            if (band.MinimumDays < 0)
                throw new ArgumentException("Age-band minimums cannot be negative.", nameof(bands));
            if (band.MaximumDays is not null && band.MaximumDays < band.MinimumDays)
                throw new ArgumentException("Each age-band maximum must be greater than or equal to its minimum.", nameof(bands));
            if (index > 0)
            {
                var previous = normalizedBands[index - 1];
                if (previous.MaximumDays is null || band.MinimumDays <= previous.MaximumDays.Value)
                    throw new ArgumentException("Age bands must be ascending and cannot overlap.", nameof(bands));
            }
        }

        var selected = (selectedBandIndexes ?? []).Distinct().ToHashSet();
        if (selected.Any(x => x < 0 || x >= normalizedBands.Count))
            throw new ArgumentException("A selected age-band index is outside the defined range.", nameof(selectedBandIndexes));

        return new AgeBandFilterValue
        {
            Bands = normalizedBands.AsReadOnly(),
            SelectedBandIndexes = selected
        };
    }

    public static AgeBandFilterValue All(IEnumerable<AgeBandRange>? bands = null) =>
        Create(bands ?? DefaultBands);
}
