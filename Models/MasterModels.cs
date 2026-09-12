namespace TexTrack.Web.Models;

public enum MasterKind
{
    LedgerGroup,
    Uqc,
    StockGroup,
    StockCategory,
    Godown,
    Colour,
    Size,
    Process,
    TaxClassification
}

public sealed record MasterDefinition(
    MasterKind Kind,
    string Title,
    string SingularTitle,
    bool SupportsParent = false,
    bool SupportsUqcFields = false,
    bool SupportsTaxMode = false,
    bool SupportsGodownFields = false,
    bool SupportsColourCode = false,
    bool SupportsDisplayOrder = false,
    bool SupportsActivation = false);

public static class MasterDefinitions
{
    public static MasterDefinition Get(MasterKind kind) => kind switch
    {
        MasterKind.LedgerGroup => new(kind, "Ledger Groups", "Ledger Group", SupportsParent: true),
        MasterKind.Uqc => new(kind, "Units (UQC)", "Unit", SupportsUqcFields: true),
        MasterKind.StockGroup => new(kind, "Stock Groups", "Stock Group", SupportsParent: true),
        MasterKind.StockCategory => new(kind, "Stock Categories", "Stock Category"),
        MasterKind.Godown => new(kind, "Godowns", "Godown", SupportsGodownFields: true),
        MasterKind.Colour => new(kind, "Colours", "Colour", SupportsColourCode: true, SupportsActivation: true),
        MasterKind.Size => new(kind, "Sizes", "Size", SupportsDisplayOrder: true, SupportsActivation: true),
        MasterKind.Process => new(kind, "Processes", "Process", SupportsDisplayOrder: true),
        MasterKind.TaxClassification => new(kind, "Tax Classifications", "Tax Classification", SupportsTaxMode: true),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}

public sealed class MasterListItem
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Alias { get; init; } = string.Empty;
    public long? ParentId { get; init; }
    public string ParentName { get; init; } = string.Empty;
    public string ShortName { get; init; } = string.Empty;
    public int? DecimalPlaces { get; init; }
    public string TaxMode { get; init; } = string.Empty;
    public string AddressLine1 { get; init; } = string.Empty;
    public string AddressLine2 { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string ColourCode { get; init; } = string.Empty;
    public int? DisplayOrder { get; init; }
    public bool IsSystem { get; init; }
    public bool IsActive { get; init; }
    public string ConcurrencyToken { get; init; } = string.Empty;
}

public sealed class MasterEditModel
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public long? ParentId { get; set; }
    public string ShortName { get; set; } = string.Empty;
    public int DecimalPlaces { get; set; }
    public string TaxMode { get; set; } = "NotApplicable";
    public string AddressLine1 { get; set; } = string.Empty;
    public string AddressLine2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string ColourCode { get; set; } = string.Empty;
    public int DisplayOrder { get; set; } = 100;
    public bool IsActive { get; set; } = true;
    public bool IsIdentityLocked { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed record OperationResult(bool Success, string Message, long? EntityId = null)
{
    public static OperationResult Ok(string message, long? entityId = null) => new(true, message, entityId);
    public static OperationResult Fail(string message) => new(false, message);
}

public sealed class VoucherTypeListItem
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public long? ParentVoucherTypeId { get; init; }
    public string ParentName { get; init; } = string.Empty;
    public string SystemTypeCode { get; init; } = string.Empty;
    public string Nature { get; init; } = string.Empty;
    public string PostingMode { get; init; } = string.Empty;
    public string Abbreviation { get; init; } = string.Empty;
    public bool AllowManualNumbering { get; init; }
    public string NumberingMode { get; init; } = "Auto";
    public string Prefix { get; init; } = string.Empty;
    public string Suffix { get; init; } = string.Empty;
    public int StartingNumber { get; init; }
    public int NumberWidth { get; init; }
    public string ResetPeriod { get; init; } = "FinancialYear";
    public string TallyVoucherTypeName { get; init; } = string.Empty;
    public bool IsSystem { get; init; }
    public string ConcurrencyToken { get; init; } = string.Empty;
}

public sealed class VoucherTypeEditModel
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public long? ParentVoucherTypeId { get; set; }
    public string Abbreviation { get; set; } = string.Empty;
    public bool AllowManualNumbering { get; set; }
    public string NumberingMode { get; set; } = "Auto";
    public string Prefix { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;
    public int StartingNumber { get; set; } = 1;
    public int NumberWidth { get; set; }
    public string ResetPeriod { get; set; } = "FinancialYear";
    public string TallyVoucherTypeName { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class VoucherTypeParentLookup
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Nature { get; init; } = string.Empty;
    public string PostingMode { get; init; } = string.Empty;
}
