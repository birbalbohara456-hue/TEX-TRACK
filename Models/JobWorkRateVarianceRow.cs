namespace TexTrack.Web.Models;

public sealed class JobWorkRateVarianceRow
{
    public long MaterialInVoucherId { get; set; }
    public long JwoVoucherId { get; set; }
    public DateOnly ReceiptDate { get; set; }
    public string MaterialInNumber { get; set; } = "";
    public string JwoNumber { get; set; } = "";
    public string Batch { get; set; } = "";
    public string FinishedGood { get; set; } = "";
    public string StageOutput { get; set; } = "";
    public string Process { get; set; } = "";
    public string JobWorker { get; set; } = "";
    public int? AssignmentVersion { get; set; }
    public string Uqc { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal? ExpectedRate { get; set; }
    public decimal ActualRate { get; set; }
    public decimal ActualTotal { get; set; }
    public decimal? Difference => ExpectedRate is null ? null : ActualRate - ExpectedRate.Value;
}
