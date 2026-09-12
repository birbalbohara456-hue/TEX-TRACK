namespace TexTrack.Web.Models;

public sealed record VoucherHistoryMonthRow(int Year, int Month, int Count, int ActiveCount, int CancelledCount);

public sealed record VoucherHistoryListRow(
    long Id,
    string VoucherNumber,
    DateOnly VoucherDate,
    string JobWorker,
    string OrderNumber,
    string Batch,
    string Details,
    decimal Quantity,
    decimal Amount,
    string Status);

public sealed record VoucherHistoryPage(
    IReadOnlyList<VoucherHistoryListRow> Rows,
    int TotalCount,
    int ActiveCount,
    decimal ActiveAmount,
    string ActiveQuantityTotals);
