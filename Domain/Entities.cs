namespace TexTrack.Web.Domain;

public interface IAuditableEntity
{
    long Id { get; set; }
    DateTimeOffset CreatedAtUtc { get; set; }
    DateTimeOffset ModifiedAtUtc { get; set; }
    string CreatedBy { get; set; }
    string ModifiedBy { get; set; }
    string ConcurrencyToken { get; set; }
}

public interface ICompanyOwnedEntity : IAuditableEntity
{
    long CompanyId { get; set; }
    Company Company { get; set; }
}

public interface INamedMasterEntity : ICompanyOwnedEntity
{
    string Name { get; set; }
    string NameNormalized { get; set; }
    string Alias { get; set; }
    bool IsSystem { get; set; }
    bool IsActive { get; set; }
}

public sealed class Company : IAuditableEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameNormalized { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateOnly? StockFrozenThrough { get; set; }
    public bool AllowNegativeStock { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class FinancialYear : ICompanyOwnedEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class ApplicationUser
{
    public long Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserNameNormalized { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEndUtc { get; set; }
    public DateTimeOffset? LastLoginAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public ICollection<ApplicationUserRole> Roles { get; set; } = new List<ApplicationUserRole>();
    public ICollection<ApplicationUserCompany> Companies { get; set; } = new List<ApplicationUserCompany>();
}

public sealed class SecurityRole
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSystem { get; set; } = true;
    public ICollection<ApplicationUserRole> Users { get; set; } = new List<ApplicationUserRole>();
}

public sealed class ApplicationUserRole
{
    public long UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public long RoleId { get; set; }
    public SecurityRole Role { get; set; } = null!;
}

public sealed class ApplicationUserCompany
{
    public long UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class LedgerGroup : INamedMasterEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long? ParentId { get; set; }
    public LedgerGroup? Parent { get; set; }
    public ICollection<LedgerGroup> Children { get; set; } = new List<LedgerGroup>();
    public string Name { get; set; } = string.Empty;
    public string NameNormalized { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string RootClassification { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class StockGroup : INamedMasterEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long? ParentId { get; set; }
    public StockGroup? Parent { get; set; }
    public ICollection<StockGroup> Children { get; set; } = new List<StockGroup>();
    public string Name { get; set; } = string.Empty;
    public string NameNormalized { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string RootClassification { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class Uqc : INamedMasterEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string NameNormalized { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public int DecimalPlaces { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class StockCategory : INamedMasterEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string NameNormalized { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class Godown : INamedMasterEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string NameNormalized { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string AddressLine2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class Colour : INamedMasterEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string NameNormalized { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string ColourCode { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class SizeMaster : INamedMasterEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string NameNormalized { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public int DisplayOrder { get; set; } = 100;
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class ProcessMaster : INamedMasterEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string NameNormalized { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public int DisplayOrder { get; set; } = 100;
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class TaxClassification : INamedMasterEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string NameNormalized { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string TaxMode { get; set; } = "NotApplicable";
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class StockItem : INamedMasterEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string NameNormalized { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public long StockGroupId { get; set; }
    public StockGroup StockGroup { get; set; } = null!;
    public long UqcId { get; set; }
    public Uqc Uqc { get; set; } = null!;
    public long? StockCategoryId { get; set; }
    public StockCategory? StockCategory { get; set; }
    public string TaxMode { get; set; } = "NotApplicable";
    public long? TaxClassificationId { get; set; }
    public TaxClassification? TaxClassification { get; set; }
    public string HsnCode { get; set; } = string.Empty;
    public decimal IgstRate { get; set; }
    public decimal CgstRate { get; set; }
    public decimal SgstRate { get; set; }
    public decimal? CostPrice { get; set; }
    public decimal? SalePrice { get; set; }
    public ICollection<StockItemColour> Colours { get; set; } = new List<StockItemColour>();
    public ICollection<StockItemSize> Sizes { get; set; } = new List<StockItemSize>();
    public ICollection<StockItemVariant> Variants { get; set; } = new List<StockItemVariant>();
    public ICollection<StockItemFieldValue> FieldValues { get; set; } = new List<StockItemFieldValue>();
    public ICollection<StockItemPhoto> Photos { get; set; } = new List<StockItemPhoto>();
    public StockItemDesignPhoto? DesignPhoto { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class StockItemFieldDefinition : ICompanyOwnedEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string NameNormalized { get; set; } = string.Empty;
    public string FieldType { get; set; } = "Text";
    public int DisplayOrder { get; set; } = 100;
    public bool IsRequired { get; set; }
    public bool AllowMultiple { get; set; }
    public string OptionsText { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
    public ICollection<StockItemFieldValue> Values { get; set; } = new List<StockItemFieldValue>();
    public ICollection<StockItemPhoto> Photos { get; set; } = new List<StockItemPhoto>();
}

public sealed class StockItemFieldValue : ICompanyOwnedEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public long FieldDefinitionId { get; set; }
    public StockItemFieldDefinition FieldDefinition { get; set; } = null!;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class StockItemPhoto : ICompanyOwnedEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public long FieldDefinitionId { get; set; }
    public StockItemFieldDefinition FieldDefinition { get; set; } = null!;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public int DisplayOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class StockItemDesignPhoto : ICompanyOwnedEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class StockItemColour
{
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public long ColourId { get; set; }
    public Colour Colour { get; set; } = null!;
}

public sealed class StockItemSize
{
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public long SizeId { get; set; }
    public SizeMaster Size { get; set; } = null!;
}

public sealed class StockItemVariant : ICompanyOwnedEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public long? ColourId { get; set; }
    public Colour? Colour { get; set; }
    public long? SizeId { get; set; }
    public SizeMaster? Size { get; set; }
    public string VariantKey { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class Ledger : INamedMasterEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long LedgerGroupId { get; set; }
    public LedgerGroup LedgerGroup { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string NameNormalized { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
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
    public bool IsJobWorker { get; set; }
    public string TallyLedgerName { get; set; } = string.Empty;
    public long? DefaultMaterialOutDestinationGodownId { get; set; }
    public Godown? DefaultMaterialOutDestinationGodown { get; set; }
    public long? DefaultMaterialInConsumptionGodownId { get; set; }
    public Godown? DefaultMaterialInConsumptionGodown { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class VoucherType : INamedMasterEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string NameNormalized { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string SystemTypeCode { get; set; } = string.Empty;
    public string Nature { get; set; } = string.Empty;
    public string PostingMode { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public bool AllowManualNumbering { get; set; }
    public string NumberingMode { get; set; } = "Auto";
    public string Prefix { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;
    public int StartingNumber { get; set; } = 1;
    public int NumberWidth { get; set; }
    public string ResetPeriod { get; set; } = "FinancialYear";
    public string TallyVoucherTypeName { get; set; } = string.Empty;
    public long? ParentVoucherTypeId { get; set; }
    public VoucherType? ParentVoucherType { get; set; }
    public ICollection<VoucherType> ChildVoucherTypes { get; set; } = new List<VoucherType>();
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class Voucher : ICompanyOwnedEntity
{
    public long Id { get; set; }
    public Guid AuditIdentity { get; set; } = Guid.NewGuid();
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long FinancialYearId { get; set; }
    public FinancialYear FinancialYear { get; set; } = null!;
    public long VoucherTypeId { get; set; }
    public VoucherType VoucherType { get; set; } = null!;
    public int SequenceNumber { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public string VoucherNumberNormalized { get; set; } = string.Empty;
    public DateOnly VoucherDate { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Batch { get; set; } = string.Empty;
    public long? PartyLedgerId { get; set; }
    public Ledger? PartyLedger { get; set; }
    public long? OpeningStockItemId { get; set; }
    public StockItem? OpeningStockItem { get; set; }
    public long? MasterJobOrderId { get; set; }
    public Voucher? MasterJobOrder { get; set; }
    public ICollection<Voucher> LinkedJobWorkOrders { get; set; } = new List<Voucher>();
    public DateOnly? DueDate { get; set; }
    public string Narration { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public string CancellationReason { get; set; } = string.Empty;
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string CancelledBy { get; set; } = string.Empty;
    public ICollection<JobWorkOrderFinishedGood> JobWorkFinishedGoods { get; set; } = new List<JobWorkOrderFinishedGood>();
    public ICollection<MasterJobOrderFinishedGood> MasterJobOrderFinishedGoods { get; set; } = new List<MasterJobOrderFinishedGood>();
    public ICollection<MaterialOutLine> MaterialOutLines { get; set; } = new List<MaterialOutLine>();
    public MaterialOutDetail? MaterialOutDetail { get; set; }
    public MaterialInDetail? MaterialInDetail { get; set; }
    public ICollection<MaterialInFinishedGood> MaterialInFinishedGoods { get; set; } = new List<MaterialInFinishedGood>();
    public ICollection<MaterialInConsumption> MaterialInConsumptions { get; set; } = new List<MaterialInConsumption>();
    public ICollection<InventoryInwardLine> InventoryInwardLines { get; set; } = new List<InventoryInwardLine>();
    public ICollection<PurchaseOrderLine> PurchaseOrderLines { get; set; } = new List<PurchaseOrderLine>();
    public ICollection<PurchaseReturnLine> PurchaseReturnLines { get; set; } = new List<PurchaseReturnLine>();
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class InventoryInwardLine
{
    public long Id { get; set; }
    public long VoucherId { get; set; }
    public Voucher Voucher { get; set; } = null!;
    public int LineNumber { get; set; }
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public long StockItemVariantId { get; set; }
    public StockItemVariant StockItemVariant { get; set; } = null!;
    public long UqcId { get; set; }
    public Uqc Uqc { get; set; } = null!;
    public long GodownId { get; set; }
    public Godown Godown { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }

    /// <summary>
    /// Optional reference to the Purchase Order line this Purchase line is fulfilling.
    /// Deliberately re-editable on every alteration (no lock, unlike Material In/Out's
    /// JwoVoucherId) per an explicit 2026-09 product decision.
    /// </summary>
    public long? PurchaseOrderLineId { get; set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }
}

/// <summary>
/// A pure reference/planning line - no inventory or financial impact. Supports
/// partial fulfillment: OrderedQuantity minus the live sum of non-cancelled
/// Purchase quantities referencing this line (via InventoryInwardLine.PurchaseOrderLineId)
/// is the pending quantity.
/// </summary>
public sealed class PurchaseOrderLine
{
    public long Id { get; set; }
    public long VoucherId { get; set; }
    public Voucher Voucher { get; set; } = null!;
    public int LineNumber { get; set; }
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public long StockItemVariantId { get; set; }
    public StockItemVariant StockItemVariant { get; set; } = null!;
    public long UqcId { get; set; }
    public Uqc Uqc { get; set; } = null!;
    public long? GodownId { get; set; }
    public Godown? Godown { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
    public DateOnly? ExpectedDeliveryDate { get; set; }
}

/// <summary>
/// A standalone outward-posting line - no FK/cap against any specific original
/// Purchase voucher. An optional bill-number reference uses the existing free-text
/// Voucher.ReferenceNumber field instead of a new linkage column.
/// </summary>
public sealed class PurchaseReturnLine
{
    public long Id { get; set; }
    public long VoucherId { get; set; }
    public Voucher Voucher { get; set; } = null!;
    public int LineNumber { get; set; }
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public long StockItemVariantId { get; set; }
    public StockItemVariant StockItemVariant { get; set; } = null!;
    public long UqcId { get; set; }
    public Uqc Uqc { get; set; } = null!;
    public long GodownId { get; set; }
    public Godown Godown { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
}

public sealed class TallyCompanyLink : ICompanyOwnedEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string TallyCompanyName { get; set; } = string.Empty;
    public string TallyCompanyIdentity { get; set; } = string.Empty;
    public bool IsConfirmed { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class TallyExchangeBatch : ICompanyOwnedEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long? TallyCompanyLinkId { get; set; }
    public TallyCompanyLink? TallyCompanyLink { get; set; }
    public string Direction { get; set; } = "Import";
    public string FileName { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public string Status { get; set; } = "Preview";
    public int NewMasterCount { get; set; }
    public int NewVoucherCount { get; set; }
    public int UpdatedVoucherCount { get; set; }
    public int UnchangedVoucherCount { get; set; }
    public int CancelledVoucherCount { get; set; }
    public int ExceptionCount { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class TallySyncRecord : ICompanyOwnedEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long TallyCompanyLinkId { get; set; }
    public TallyCompanyLink TallyCompanyLink { get; set; } = null!;
    public long? VoucherId { get; set; }
    public Voucher? Voucher { get; set; }
    public string TallyGuid { get; set; } = string.Empty;
    public string TallyRemoteId { get; set; } = string.Empty;
    public string VoucherTypeName { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public string SyncState { get; set; } = "Imported";
    public DateTimeOffset? LastImportedAtUtc { get; set; }
    public DateTimeOffset? LastExportedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class TallyImportException : ICompanyOwnedEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long ExchangeBatchId { get; set; }
    public TallyExchangeBatch ExchangeBatch { get; set; } = null!;
    public string TallyGuid { get; set; } = string.Empty;
    public string VoucherTypeName { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string PayloadXml { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class TallyVariantAllocationTask : ICompanyOwnedEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long ExchangeBatchId { get; set; }
    public TallyExchangeBatch ExchangeBatch { get; set; } = null!;
    public string TallyGuid { get; set; } = string.Empty;
    public string VoucherTypeName { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public string StockItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string UqcName { get; set; } = string.Empty;
    public string PayloadXml { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class BillOfMaterial : ICompanyOwnedEntity
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string NameNormalized { get; set; } = string.Empty;
    public decimal OutputQuantity { get; set; } = 1;
    public int VersionNumber { get; set; } = 1;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public long? CurrentRevisionId { get; set; }
    public BillOfMaterialRevision? CurrentRevision { get; set; }
    public ICollection<BillOfMaterialLine> Lines { get; set; } = new List<BillOfMaterialLine>();
    public ICollection<BillOfMaterialRevision> Revisions { get; set; } = new List<BillOfMaterialRevision>();
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public string ModifiedBy { get; set; } = "Developer";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class BillOfMaterialRevision
{
    public long Id { get; set; }
    public long BomId { get; set; }
    public BillOfMaterial Bom { get; set; } = null!;
    public int RevisionNumber { get; set; }
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public decimal OutputQuantity { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string ChangeReason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
    public ICollection<BillOfMaterialRevisionLine> Lines { get; set; } = new List<BillOfMaterialRevisionLine>();
}

public sealed class BillOfMaterialRevisionLine
{
    public long Id { get; set; }
    public long BomRevisionId { get; set; }
    public BillOfMaterialRevision BomRevision { get; set; } = null!;
    public int LineNumber { get; set; }
    public long ComponentStockItemId { get; set; }
    public StockItem ComponentStockItem { get; set; } = null!;
    public long? ComponentVariantId { get; set; }
    public StockItemVariant? ComponentVariant { get; set; }
    public long UqcId { get; set; }
    public Uqc Uqc { get; set; } = null!;
    public decimal RequiredQuantity { get; set; }
    public long? ChildBomId { get; set; }
    public BillOfMaterial? ChildBom { get; set; }
    public long? ChildBomRevisionId { get; set; }
    public BillOfMaterialRevision? ChildBomRevision { get; set; }
    public long? ProcessId { get; set; }
    public ProcessMaster? Process { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class BillOfMaterialLine
{
    public long Id { get; set; }
    public long BomId { get; set; }
    public BillOfMaterial Bom { get; set; } = null!;
    public int LineNumber { get; set; }
    public long ComponentStockItemId { get; set; }
    public StockItem ComponentStockItem { get; set; } = null!;
    public long? ComponentVariantId { get; set; }
    public StockItemVariant? ComponentVariant { get; set; }
    public long UqcId { get; set; }
    public Uqc Uqc { get; set; } = null!;
    public decimal RequiredQuantity { get; set; }
    public long? ChildBomId { get; set; }
    public BillOfMaterial? ChildBom { get; set; }
    public long? ProcessId { get; set; }
    public ProcessMaster? Process { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class JobWorkOrderFinishedGood
{
    public long Id { get; set; }
    public long VoucherId { get; set; }
    public Voucher Voucher { get; set; } = null!;
    public int LineNumber { get; set; }
    public string DesignGroupKey { get; set; } = Guid.NewGuid().ToString("N");
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public long? ColourId { get; set; }
    public Colour? Colour { get; set; }
    public decimal OrderedQuantity { get; set; }
    public long? FinishedGoodsGodownId { get; set; }
    public Godown? FinishedGoodsGodown { get; set; }
    public long? DestinationGodownId { get; set; }
    public Godown? DestinationGodown { get; set; }
    public decimal XmlRate { get; set; }
    public decimal XmlAmount { get; set; }
    public ICollection<JobWorkOrderSizeAllocation> SizeAllocations { get; set; } = new List<JobWorkOrderSizeAllocation>();
    public ICollection<JobWorkOrderComponent> Components { get; set; } = new List<JobWorkOrderComponent>();
    public ICollection<JobWorkOrderBomStage> BomStages { get; set; } = new List<JobWorkOrderBomStage>();

    // BUILD 2.10
    public ICollection<JobWorkOrderProcess> Processes { get; set; } = new List<JobWorkOrderProcess>();
}

public sealed class JobWorkOrderBomStage
{
    public long Id { get; set; }
    public long VoucherId { get; set; }
    public Voucher Voucher { get; set; } = null!;
    public long FinishedGoodId { get; set; }
    public JobWorkOrderFinishedGood FinishedGood { get; set; } = null!;
    public long? ParentStageId { get; set; }
    public JobWorkOrderBomStage? ParentStage { get; set; }
    public ICollection<JobWorkOrderBomStage> ChildStages { get; set; } = new List<JobWorkOrderBomStage>();
    public long? SourceBomId { get; set; }
    public BillOfMaterial? SourceBom { get; set; }
    public long? SourceBomRevisionId { get; set; }
    public BillOfMaterialRevision? SourceBomRevision { get; set; }
    public int? SourceBomVersion { get; set; }
    public Guid StableKey { get; set; } = Guid.NewGuid();
    public int StageNumber { get; set; }
    public int StageLevel { get; set; }
    public string StagePath { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public long OutputStockItemId { get; set; }
    public StockItem OutputStockItem { get; set; } = null!;
    public long? OutputVariantId { get; set; }
    public StockItemVariant? OutputVariant { get; set; }
    public long OutputUqcId { get; set; }
    public Uqc OutputUqc { get; set; } = null!;
    public decimal OutputQuantity { get; set; }
    public long? ProcessId { get; set; }
    public ProcessMaster? Process { get; set; }
    public long? AssignedJobWorkerId { get; set; }
    public Ledger? AssignedJobWorker { get; set; }
    public long? OutputGodownId { get; set; }
    public Godown? OutputGodown { get; set; }
    public bool IsFinalStage { get; set; }
    public ICollection<JobWorkOrderComponent> Components { get; set; } = new List<JobWorkOrderComponent>();
    public ICollection<JobWorkOrderComponent> ProducedComponents { get; set; } = new List<JobWorkOrderComponent>();
    public ICollection<MaterialInFinishedGood> MaterialInFinishedGoods { get; set; } = new List<MaterialInFinishedGood>();
    public ICollection<JobWorkOrderStageAssignment> AssignmentHistory { get; set; } = new List<JobWorkOrderStageAssignment>();
}

public sealed class JobWorkOrderRevision
{
    public long Id { get; set; }
    public long VoucherId { get; set; }
    public Voucher Voucher { get; set; } = null!;
    public int RevisionNumber { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = "{}";
    public string ChangeReason { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
}

public sealed class JobWorkOrderStageAssignment
{
    public decimal? ExpectedProcessRate { get; set; }
    public long Id { get; set; }
    public long BomStageId { get; set; }
    public JobWorkOrderBomStage BomStage { get; set; } = null!;
    public int AssignmentVersion { get; set; }
    public long? JobWorkerId { get; set; }
    public Ledger? JobWorker { get; set; }
    public DateOnly? ExpectedCompletionDate { get; set; }
    public string Status { get; set; } = "Active";
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset ValidFromUtc { get; set; }
    public DateTimeOffset? ValidToUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
}

public sealed class JobWorkOrderSizeAllocation
{
    public long Id { get; set; }
    public long FinishedGoodId { get; set; }
    public JobWorkOrderFinishedGood FinishedGood { get; set; } = null!;
    public long StockItemVariantId { get; set; }
    public StockItemVariant StockItemVariant { get; set; } = null!;
    public long? SizeId { get; set; }
    public SizeMaster? Size { get; set; }
    public decimal Quantity { get; set; }
    public long? MasterJobOrderAllocationId { get; set; }
    public MasterJobOrderAllocation? MasterJobOrderAllocation { get; set; }
}

public sealed class MasterJobOrderFinishedGood
{
    public long Id { get; set; }
    public long VoucherId { get; set; }
    public Voucher Voucher { get; set; } = null!;
    public int LineNumber { get; set; }
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public decimal OrderedQuantity { get; set; }
    public ICollection<MasterJobOrderAllocation> Allocations { get; set; } = new List<MasterJobOrderAllocation>();
}

public sealed class MasterJobOrderAllocation
{
    public long Id { get; set; }
    public long FinishedGoodId { get; set; }
    public MasterJobOrderFinishedGood FinishedGood { get; set; } = null!;
    public long StockItemVariantId { get; set; }
    public StockItemVariant StockItemVariant { get; set; } = null!;
    public long? ColourId { get; set; }
    public Colour? Colour { get; set; }
    public long? SizeId { get; set; }
    public SizeMaster? Size { get; set; }
    public decimal Quantity { get; set; }
    public ICollection<JobWorkOrderSizeAllocation> JobWorkAllocations { get; set; } = new List<JobWorkOrderSizeAllocation>();
}

public sealed class JobWorkOrderComponent
{
    public long Id { get; set; }
    public long FinishedGoodId { get; set; }
    public JobWorkOrderFinishedGood FinishedGood { get; set; } = null!;
    public int LineNumber { get; set; }
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public long UqcId { get; set; }
    public Uqc Uqc { get; set; } = null!;
    public decimal RequiredQuantity { get; set; }
    public long? ComponentGodownId { get; set; }
    public Godown? ComponentGodown { get; set; }
    public decimal XmlRate { get; set; }
    public decimal XmlAmount { get; set; }
    public long? BomStageId { get; set; }
    public JobWorkOrderBomStage? BomStage { get; set; }
    public long? ParentComponentId { get; set; }
    public JobWorkOrderComponent? ParentComponent { get; set; }
    public ICollection<JobWorkOrderComponent> ChildComponents { get; set; } = new List<JobWorkOrderComponent>();
    public long? ChildBomStageId { get; set; }
    public JobWorkOrderBomStage? ChildBomStage { get; set; }
    public long? ComponentVariantId { get; set; }
    public StockItemVariant? ComponentVariant { get; set; }
    public int BomLevel { get; set; }
    public string BomPath { get; set; } = string.Empty;
    public bool IsProducedComponent { get; set; }
}

// BUILD 2.10
public sealed class JobWorkOrderProcess
{
    public long Id { get; set; }
    public long FinishedGoodId { get; set; }
    public JobWorkOrderFinishedGood FinishedGood { get; set; } = null!;
    public long ProcessId { get; set; }
    public ProcessMaster Process { get; set; } = null!;
    public decimal ExpectedRate { get; set; }
    public string RateBasis { get; set; } = "Per Quantity";
}

public sealed class MaterialOutLine
{
    public long Id { get; set; }
    public long VoucherId { get; set; }
    public Voucher Voucher { get; set; } = null!;
    public long JwoVoucherId { get; set; }
    public Voucher JwoVoucher { get; set; } = null!;
    public long JwoFinishedGoodId { get; set; }
    public JobWorkOrderFinishedGood JwoFinishedGood { get; set; } = null!;
    public long JwoComponentId { get; set; }
    public JobWorkOrderComponent JwoComponent { get; set; } = null!;
    public long? BomStageId { get; set; }
    public JobWorkOrderBomStage? BomStage { get; set; }
    public long? StageAssignmentId { get; set; }
    public JobWorkOrderStageAssignment? StageAssignment { get; set; }
    public int LineNumber { get; set; }
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public long UqcId { get; set; }
    public Uqc Uqc { get; set; } = null!;
    public long SourceGodownId { get; set; }
    public Godown SourceGodown { get; set; } = null!;
    public long DestinationGodownId { get; set; }
    public Godown DestinationGodown { get; set; } = null!;
    public decimal RequiredQuantity { get; set; }
    public decimal PreviouslyIssuedQuantity { get; set; }
    public decimal IssuedQuantity { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
}

public sealed class MaterialOutDetail
{
    public long VoucherId { get; set; }
    public Voucher Voucher { get; set; } = null!;
    public bool ProvideGstEwayDetails { get; set; }
    public long DestinationGodownId { get; set; }
    public Godown DestinationGodown { get; set; } = null!;
    public string DisplayedOrderNumber { get; set; } = string.Empty;
    public string EwayBillNumber { get; set; } = string.Empty;
    public DateOnly? EwayBillDate { get; set; }
    public string ConsolidatedEwayBillNumber { get; set; } = string.Empty;
    public DateOnly? ConsolidatedEwayBillDate { get; set; }
    public string EwaySubType { get; set; } = "Others";
    public string EwayDocumentType { get; set; } = "Delivery Challan";
    public string ConsignorMailingName { get; set; } = string.Empty;
    public string ConsignorGstin { get; set; } = string.Empty;
    public string ConsignorState { get; set; } = string.Empty;
    public string ConsignorAddress1 { get; set; } = string.Empty;
    public string ConsignorAddress2 { get; set; } = string.Empty;
    public string ConsignorPincode { get; set; } = string.Empty;
    public string ConsignorPlace { get; set; } = string.Empty;
    public string ConsignorActualState { get; set; } = string.Empty;
    public string ConsigneeMailingName { get; set; } = string.Empty;
    public string ConsigneeGstin { get; set; } = string.Empty;
    public string ConsigneeState { get; set; } = string.Empty;
    public string ConsigneeAddress1 { get; set; } = string.Empty;
    public string ConsigneeAddress2 { get; set; } = string.Empty;
    public string ConsigneePincode { get; set; } = string.Empty;
    public string ConsigneePlace { get; set; } = string.Empty;
    public string ConsigneeActualState { get; set; } = string.Empty;
    public string PinToPinDistance { get; set; } = string.Empty;
    public string TransporterName { get; set; } = string.Empty;
    public string TransporterId { get; set; } = string.Empty;
    public string TransportMode { get; set; } = "Not Applicable";
    public string TransportDocumentNumber { get; set; } = string.Empty;
    public DateOnly? TransportDocumentDate { get; set; }
    public string VehicleNumber { get; set; } = string.Empty;
    public string VehicleType { get; set; } = "Not Applicable";
}

public sealed class MaterialInDetail
{
    public long VoucherId { get; set; }
    public Voucher Voucher { get; set; } = null!;
    public long JwoVoucherId { get; set; }
    public Voucher JwoVoucher { get; set; } = null!;
    public long ConsumptionGodownId { get; set; }
    public Godown ConsumptionGodown { get; set; } = null!;
    public long ReceivingGodownId { get; set; }
    public Godown ReceivingGodown { get; set; } = null!;
    public string DisplayedOrderNumber { get; set; } = string.Empty;
    public decimal TotalProcessCharge { get; set; }
    public decimal TotalConsumedMaterialValue { get; set; }
    public decimal TotalFinishedGoodsValue { get; set; }
}

public sealed class MaterialInFinishedGood
{
    public decimal? ExpectedProcessRate { get; set; }
    public decimal? ActualProcessRate { get; set; }
    public string ChargeMode { get; set; } = "Total";
    public long Id { get; set; }
    public long VoucherId { get; set; }
    public Voucher Voucher { get; set; } = null!;
    public long JwoFinishedGoodId { get; set; }
    public JobWorkOrderFinishedGood JwoFinishedGood { get; set; } = null!;
    public int LineNumber { get; set; }
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public long UqcId { get; set; }
    public Uqc Uqc { get; set; } = null!;
    public long ReceivingGodownId { get; set; }
    public Godown ReceivingGodown { get; set; } = null!;
    public decimal OrderedQuantity { get; set; }
    public decimal PreviouslyReceivedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal MaterialValue { get; set; }
    public decimal ProcessCharge { get; set; }
    public decimal FinishedGoodsValue { get; set; }
    public decimal Rate { get; set; }
    public long? BomStageId { get; set; }
    public JobWorkOrderBomStage? BomStage { get; set; }
    public long? StageAssignmentId { get; set; }
    public JobWorkOrderStageAssignment? StageAssignment { get; set; }
    public ICollection<MaterialInFinishedGoodAllocation> Allocations { get; set; } = new List<MaterialInFinishedGoodAllocation>();
}

public sealed class MaterialInFinishedGoodAllocation
{
    public long Id { get; set; }
    public long FinishedGoodLineId { get; set; }
    public MaterialInFinishedGood FinishedGoodLine { get; set; } = null!;
    public long StockItemVariantId { get; set; }
    public StockItemVariant StockItemVariant { get; set; } = null!;
    public decimal Quantity { get; set; }
}

public sealed class MaterialInConsumption
{
    public long Id { get; set; }
    public long VoucherId { get; set; }
    public Voucher Voucher { get; set; } = null!;
    public long JwoComponentId { get; set; }
    public JobWorkOrderComponent JwoComponent { get; set; } = null!;
    public long? BomStageId { get; set; }
    public JobWorkOrderBomStage? BomStage { get; set; }
    public long? StageAssignmentId { get; set; }
    public JobWorkOrderStageAssignment? StageAssignment { get; set; }
    public int LineNumber { get; set; }
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public long UqcId { get; set; }
    public Uqc Uqc { get; set; } = null!;
    public long ConsumptionGodownId { get; set; }
    public Godown ConsumptionGodown { get; set; } = null!;
    public decimal AvailableQuantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public decimal Rate { get; set; }
    public decimal Value { get; set; }
    public ICollection<MaterialInMaterialOutAllocation> MaterialOutAllocations { get; set; } = new List<MaterialInMaterialOutAllocation>();
}

public sealed class MaterialInMaterialOutAllocation
{
    public long Id { get; set; }
    public long ConsumptionLineId { get; set; }
    public MaterialInConsumption ConsumptionLine { get; set; } = null!;
    public long MaterialOutLineId { get; set; }
    public MaterialOutLine MaterialOutLine { get; set; } = null!;
    public decimal AllocatedQuantity { get; set; }
    public decimal RateSnapshot { get; set; }
    public decimal ValueSnapshot { get; set; }
}

public sealed class StockMovement
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long FinancialYearId { get; set; }
    public FinancialYear FinancialYear { get; set; } = null!;
    public long VoucherId { get; set; }
    public Voucher Voucher { get; set; } = null!;
    public long? MaterialOutLineId { get; set; }
    public MaterialOutLine? MaterialOutLine { get; set; }
    public long? MaterialInFinishedGoodId { get; set; }
    public MaterialInFinishedGood? MaterialInFinishedGood { get; set; }
    public long? MaterialInConsumptionId { get; set; }
    public MaterialInConsumption? MaterialInConsumption { get; set; }
    public long? InventoryInwardLineId { get; set; }
    public InventoryInwardLine? InventoryInwardLine { get; set; }
    public long? PurchaseReturnLineId { get; set; }
    public PurchaseReturnLine? PurchaseReturnLine { get; set; }
    public long? InventoryPostingId { get; set; }
    public InventoryPosting? InventoryPosting { get; set; }
    public int? MovementLineOrder { get; set; }
    public long? StockItemVariantId { get; set; }
    public StockItemVariant? StockItemVariant { get; set; }
    public DateOnly MovementDate { get; set; }
    public long StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public long UqcId { get; set; }
    public Uqc Uqc { get; set; } = null!;
    public long GodownId { get; set; }
    public Godown Godown { get; set; } = null!;
    public decimal QuantityChange { get; set; }
    public decimal Rate { get; set; }
    public decimal ValueChange { get; set; }
    public string MovementKind { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
}

public sealed class VoucherLink
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public long SourceVoucherId { get; set; }
    public Voucher SourceVoucher { get; set; } = null!;
    public long TargetVoucherId { get; set; }
    public Voucher TargetVoucher { get; set; } = null!;
    public string LinkType { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "Developer";
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public long? CompanyId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public long? EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Description { get; set; } = string.Empty;
    public string PerformedBy { get; set; } = "Developer";
    public DateTimeOffset PerformedAtUtc { get; set; }
    public long? InventoryPostingId { get; set; }
    public InventoryPosting? InventoryPosting { get; set; }
    public Guid? CorrelationId { get; set; }
}

/// <summary>
/// An append-only, full-state voucher revision. This record intentionally has no
/// foreign key to the live voucher or mutable master tables so that deletion or
/// later master renaming cannot erase or rewrite historical evidence.
/// </summary>
public sealed class VoucherAuditRevision
{
    public long Id { get; set; }
    public Guid VoucherAuditIdentity { get; set; }
    public long VoucherId { get; set; }
    public long CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public long FinancialYearId { get; set; }
    public string FinancialYearName { get; set; } = string.Empty;
    public long VoucherTypeId { get; set; }
    public string VoucherTypeName { get; set; } = string.Empty;
    public string VoucherTypeCode { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public int SnapshotSchemaVersion { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = "{}";
    public string ChangesJson { get; set; } = "{}";
    public string ContentHash { get; set; } = string.Empty;
    public string PreviousChainHash { get; set; } = string.Empty;
    public string ChainHash { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
}
