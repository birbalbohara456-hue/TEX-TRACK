namespace TexTrack.Web.Models;

public sealed class JobWorkerListItem
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Alias { get; init; } = string.Empty;
    public string LedgerGroupName { get; init; } = string.Empty;
    public string TallyLedgerName { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string ContactPerson { get; init; } = string.Empty;
    public string Mobile { get; init; } = string.Empty;
    public string MaterialOutGodownName { get; init; } = string.Empty;
    public string MaterialInGodownName { get; init; } = string.Empty;
    public string ConcurrencyToken { get; init; } = string.Empty;
}

public sealed class JobWorkerEditModel
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public long? LedgerGroupId { get; set; }
    public string TallyLedgerName { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string AddressLine2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Gstin { get; set; } = string.Empty;
    public long? DefaultMaterialOutDestinationGodownId { get; set; }
    public long? DefaultMaterialInConsumptionGodownId { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class JobWorkerMasterLookupData
{
    public List<LedgerGroupChoice> LedgerGroups { get; init; } = new();
    public List<JobWorkGodownLookup> Godowns { get; init; } = new();
    public long? PreferredLedgerGroupId { get; init; }
}