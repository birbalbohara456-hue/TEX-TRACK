namespace TexTrack.Web.Models;

public sealed record VoucherFieldProfile(
    string Key,
    string Label,
    int Order,
    bool IsVisible = true,
    bool IsRequired = false,
    bool IsReadOnly = false);

public sealed record VoucherLayoutProfile(
    string Key,
    IReadOnlyList<VoucherFieldProfile> Fields)
{
    public VoucherFieldProfile? Field(string key) =>
        Fields.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
}

public static class VoucherLayoutProfiles
{
    public static readonly VoucherLayoutProfile MaterialIn = new(
        "material-in",
        [
            new("party", "Party A/c Name", 10, IsRequired: true),
            new("order", "JWO / Order No.", 20, IsRequired: true),
            new("batch", "Batch", 30, IsReadOnly: true),
            new("source-godown", "Consumption Godown", 40, IsRequired: true),
            new("destination-godown", "Finished Goods Godown", 50, IsRequired: true),
            new("reference", "Reference No.", 60),
            new("narration", "Narration", 90)
        ]);

    public static readonly VoucherLayoutProfile MaterialOut = new(
        "material-out",
        [
            new("party", "Party A/c Name", 10, IsRequired: true),
            new("order", "JWO / Order No.", 20, IsRequired: true),
            new("batch", "Batch", 30),
            new("destination-godown", "Jobber Destination Godown", 50),
            new("reference", "Reference", 60),
            new("narration", "Narration", 90)
        ]);

    public static readonly VoucherLayoutProfile JobWorkOutOrder = new(
        "job-work-out-order",
        [
            new("voucher-type", "Voucher Type", 5, IsRequired: true),
            new("party", "Job Worker", 10, IsRequired: true),
            new("batch", "Batch", 30, IsRequired: true),
            new("destination-godown", "Jobber Destination Godown", 50),
            new("reference", "Reference", 60),
            new("due-date", "Expected Completion", 70),
            new("narration", "Narration", 90)
        ]);

    public static readonly VoucherLayoutProfile PurchaseOrder = new(
        "purchase-order",
        [
            new("party", "Supplier A/c Name", 10, IsRequired: true),
            new("reference", "Reference No.", 60),
            new("discount", "Bulk Discount %", 70),
            new("narration", "Narration", 90)
        ]);

    public static readonly VoucherLayoutProfile Purchase = new(
        "purchase",
        [
            new("party", "Supplier A/c Name", 10, IsRequired: true),
            new("order", "Purchase Order (optional)", 20),
            new("reference", "Reference No.", 60),
            new("discount", "Bulk Discount %", 70),
            new("narration", "Narration", 90)
        ]);

    public static readonly VoucherLayoutProfile PurchaseReturn = new(
        "purchase-return",
        [
            new("party", "Supplier A/c Name", 10),
            new("order", "Purchase Bill (optional)", 20),
            new("reference", "Bill / Reference No.", 60),
            new("discount", "Bulk Discount %", 70),
            new("narration", "Narration", 90)
        ]);

    public static VoucherLayoutProfile Placeholder(string key) => new(
        key,
        [
            new("party", "Party / Ledger", 10),
            new("order", "Order / Reference", 20),
            new("batch", "Batch", 30),
            new("source-godown", "Material Source Godown", 40),
            new("destination-godown", "Jobber Destination Godown", 50),
            new("reference", "Reference", 60),
            new("narration", "Narration", 90)
        ]);
}
