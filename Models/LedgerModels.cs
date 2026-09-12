namespace TexTrack.Web.Models;

public sealed class LedgerGroupChoice
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string RootClassification { get; init; } = string.Empty;
    public bool IsSystem { get; init; }
}

public sealed class LedgerListItem
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Alias { get; init; } = string.Empty;
    public long LedgerGroupId { get; init; }
    public string LedgerGroupName { get; init; } = string.Empty;
    public string RootClassification { get; init; } = string.Empty;
    public string GstRegistrationType { get; init; } = string.Empty;
    public string Gstin { get; init; } = string.Empty;
    public decimal OpeningBalance { get; init; }
    public string OpeningBalanceType { get; init; } = "Dr";
    public bool MaintainBillWise { get; init; }
    public bool IsSystem { get; init; }
    public bool IsActive { get; init; }
    public string ConcurrencyToken { get; init; } = string.Empty;
}

public sealed class LedgerEditModel
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public long? LedgerGroupId { get; set; }
    public string MailingName { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string AddressLine2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = "India";
    public string PinCode { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string GstRegistrationType { get; set; } = "Unregistered";
    public string Gstin { get; set; } = string.Empty;
    public string Pan { get; set; } = string.Empty;
    public bool MaintainBillWise { get; set; }
    public int CreditPeriodDays { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal OpeningBalance { get; set; }
    public string OpeningBalanceType { get; set; } = "Dr";
    public string BankAccountNumber { get; set; } = string.Empty;
    public string BankIfsc { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BankBranch { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
}
