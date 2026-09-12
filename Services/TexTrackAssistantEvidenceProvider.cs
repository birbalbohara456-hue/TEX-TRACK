using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public interface IAssistantEvidenceProvider
{
    Task<AssistantEvidence> CollectAsync(string question, CancellationToken cancellationToken = default);
}

public sealed partial class TexTrackAssistantEvidenceProvider(
    IDbContextFactory<TexTrackDbContext> dbFactory,
    CurrentCompanyContext company) : IAssistantEvidenceProvider
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "show", "find", "search", "what", "where", "which", "when", "why", "please", "about",
        "with", "from", "this", "that", "item", "stock", "voucher", "order", "report", "status",
        "pending", "cost", "tell", "give", "open", "textrack", "material", "finished", "goods"
    };

    public async Task<AssistantEvidence> CollectAsync(string question, CancellationToken cancellationToken = default)
    {
        await company.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var text = new StringBuilder();
        var sources = new List<AssistantSourceLink>();
        var lower = question.ToLowerInvariant();
        var token = ExtractSearchToken(question);

        text.AppendLine($"Company: {company.CompanyName}; financial year: {company.FinancialYearName}.");
        text.AppendLine("All rows below are read-only, company-scoped TexTrack evidence.");

        if (!string.IsNullOrWhiteSpace(token))
        {
            await AppendVoucherMatchesAsync(db, token, text, sources, cancellationToken);
            await AppendMasterMatchesAsync(db, token, text, sources, cancellationToken);
            await AppendStockPositionAsync(db, token, text, sources, cancellationToken);
        }

        if (lower.Contains("pending") || lower.Contains("job") || lower.Contains("jwo") || lower.Contains("issue"))
            await AppendPendingMaterialAsync(db, token, text, sources, cancellationToken);

        if (lower.Contains("tally") || lower.Contains("xml") || lower.Contains("import") || lower.Contains("export"))
            await AppendTallyExceptionsAsync(db, text, sources, cancellationToken);

        if (text.Length < 180)
            await AppendRecentVoucherSummaryAsync(db, text, sources, cancellationToken);

        return new(text.ToString().Trim(), sources.DistinctBy(x => x.Url).Take(12).ToArray());
    }

    private async Task AppendVoucherMatchesAsync(
        TexTrackDbContext db, string token, StringBuilder text, List<AssistantSourceLink> sources,
        CancellationToken cancellationToken)
    {
        var pattern = $"%{EscapeLike(token)}%";
        var rows = await db.Vouchers.AsNoTracking()
            .Where(x => x.CompanyId == company.CompanyId &&
                        (EF.Functions.ILike(x.VoucherNumber, pattern) ||
                         EF.Functions.ILike(x.ReferenceNumber, pattern) ||
                         EF.Functions.ILike(x.Batch, pattern) ||
                         (x.PartyLedger != null && EF.Functions.ILike(x.PartyLedger.Name, pattern))))
            .OrderByDescending(x => x.VoucherDate).ThenByDescending(x => x.Id)
            .Select(x => new { x.Id, x.VoucherNumber, x.VoucherDate, x.Batch, x.Status, Type = x.VoucherType.SystemTypeCode, TypeName = x.VoucherType.Name, Party = x.PartyLedger == null ? "" : x.PartyLedger.Name })
            .Take(8).ToListAsync(cancellationToken);

        if (rows.Count == 0) return;
        text.AppendLine("Voucher matches:");
        foreach (var row in rows)
        {
            text.AppendLine($"- {row.TypeName} {row.VoucherNumber}; {row.VoucherDate:dd-MMM-yyyy}; party {row.Party}; batch {row.Batch}; status {row.Status}.");
            var url = VoucherUrl(row.Type, row.Id);
            if (url is not null) sources.Add(new($"{row.TypeName} {row.VoucherNumber}", url));
        }
    }

    private async Task AppendMasterMatchesAsync(
        TexTrackDbContext db, string token, StringBuilder text, List<AssistantSourceLink> sources,
        CancellationToken cancellationToken)
    {
        var pattern = $"%{EscapeLike(token)}%";
        var items = await db.StockItems.AsNoTracking()
            .Where(x => x.CompanyId == company.CompanyId && x.IsActive &&
                        (EF.Functions.ILike(x.Name, pattern) || EF.Functions.ILike(x.Alias, pattern)))
            .Select(x => new { x.Id, x.Name, Group = x.StockGroup.Name, Uqc = x.Uqc.ShortName })
            .Take(6).ToListAsync(cancellationToken);
        var ledgers = await db.Ledgers.AsNoTracking()
            .Where(x => x.CompanyId == company.CompanyId && x.IsActive &&
                        (EF.Functions.ILike(x.Name, pattern) || EF.Functions.ILike(x.Alias, pattern)))
            .Select(x => new { x.Name, Group = x.LedgerGroup.Name, x.IsJobWorker })
            .Take(6).ToListAsync(cancellationToken);

        if (items.Count > 0)
        {
            text.AppendLine("Stock Item matches:");
            foreach (var item in items) text.AppendLine($"- {item.Name}; group {item.Group}; UQC {item.Uqc}.");
            sources.Add(new("Open Stock Items", "/masters/stock-items"));
        }
        if (ledgers.Count > 0)
        {
            text.AppendLine("Ledger matches:");
            foreach (var ledger in ledgers) text.AppendLine($"- {ledger.Name}; group {ledger.Group}; job worker {(ledger.IsJobWorker ? "yes" : "no")}.");
            sources.Add(new("Open Ledgers", "/masters/ledgers"));
        }
    }

    private async Task AppendStockPositionAsync(
        TexTrackDbContext db, string token, StringBuilder text, List<AssistantSourceLink> sources,
        CancellationToken cancellationToken)
    {
        var pattern = $"%{EscapeLike(token)}%";
        var item = await db.StockItems.AsNoTracking()
            .Where(x => x.CompanyId == company.CompanyId && x.IsActive && EF.Functions.ILike(x.Name, pattern))
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).FirstOrDefaultAsync(cancellationToken);
        if (item is null) return;

        var balances = await db.StockMovements.AsNoTracking()
            .Where(x => x.CompanyId == company.CompanyId && x.StockItemId == item.Id && x.Voucher.Status != "Cancelled")
            .GroupBy(x => new { Godown = x.Godown.Name, Uqc = x.Uqc.ShortName })
            .Select(x => new { x.Key.Godown, x.Key.Uqc, Quantity = x.Sum(y => y.QuantityChange), Value = x.Sum(y => y.ValueChange) })
            .OrderBy(x => x.Godown).Take(12).ToListAsync(cancellationToken);
        text.AppendLine($"Current stock position for {item.Name}:");
        if (balances.Count == 0) text.AppendLine("- No valid stock movements found.");
        foreach (var balance in balances)
            text.AppendLine($"- {balance.Godown}: {balance.Quantity:0.####} {balance.Uqc}; value {balance.Value:0.00}.");
        sources.Add(new($"Closing Stock: {item.Name}", $"/reports/inventory/closing-stock?itemId={item.Id}"));
    }

    private async Task AppendPendingMaterialAsync(
        TexTrackDbContext db, string? token, StringBuilder text, List<AssistantSourceLink> sources,
        CancellationToken cancellationToken)
    {
        var query = db.JobWorkOrderComponents.AsNoTracking()
            .Where(x => x.FinishedGood.Voucher.CompanyId == company.CompanyId && x.FinishedGood.Voucher.Status != "Cancelled")
            .Select(x => new
            {
                VoucherId = x.FinishedGood.VoucherId,
                VoucherNumber = x.FinishedGood.Voucher.VoucherNumber,
                Batch = x.FinishedGood.Voucher.Batch,
                Party = x.FinishedGood.Voucher.PartyLedger == null ? "" : x.FinishedGood.Voucher.PartyLedger.Name,
                Component = x.StockItem.Name,
                Uqc = x.Uqc.ShortName,
                Required = x.RequiredQuantity,
                Issued = db.MaterialOutLines.Where(y => y.JwoComponentId == x.Id && y.Voucher.Status != "Cancelled").Sum(y => (decimal?)y.IssuedQuantity) ?? 0
            })
            .Where(x => x.Required > x.Issued);
        if (!string.IsNullOrWhiteSpace(token))
            query = query.Where(x => x.VoucherNumber.Contains(token) || x.Batch.Contains(token) || x.Component.Contains(token) || x.Party.Contains(token));
        var rows = await query.OrderBy(x => x.VoucherNumber).Take(8).ToListAsync(cancellationToken);
        if (rows.Count == 0) return;
        text.AppendLine("Pending material issue evidence:");
        foreach (var row in rows)
        {
            text.AppendLine($"- JWO {row.VoucherNumber}; batch {row.Batch}; {row.Component}: required {row.Required:0.####} {row.Uqc}, issued {row.Issued:0.####}, pending {row.Required - row.Issued:0.####}.");
            sources.Add(new($"JWO {row.VoucherNumber}", $"/vouchers/job-work-out-order?voucherId={row.VoucherId}"));
        }
        sources.Add(new("Pending Material Issue Report", "/reports/job-work/pending-material-issue"));
    }

    private async Task AppendTallyExceptionsAsync(
        TexTrackDbContext db, StringBuilder text, List<AssistantSourceLink> sources,
        CancellationToken cancellationToken)
    {
        var rows = await db.TallyImportExceptions.AsNoTracking()
            .Where(x => x.CompanyId == company.CompanyId && x.Status == "Open")
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new { x.VoucherTypeName, x.VoucherNumber, x.ReasonCode, x.Message })
            .Take(8).ToListAsync(cancellationToken);
        text.AppendLine($"Open Tally XML exceptions: {rows.Count}{(rows.Count == 8 ? "+" : string.Empty)}.");
        foreach (var row in rows)
            text.AppendLine($"- {row.VoucherTypeName} {row.VoucherNumber}; {row.ReasonCode}: {row.Message}");
        sources.Add(new("Tally XML Exchange", "/settings/tally-xml"));
    }

    private async Task AppendRecentVoucherSummaryAsync(
        TexTrackDbContext db, StringBuilder text, List<AssistantSourceLink> sources,
        CancellationToken cancellationToken)
    {
        var counts = await db.Vouchers.AsNoTracking().Where(x => x.CompanyId == company.CompanyId)
            .GroupBy(x => new { x.VoucherType.Name, x.Status })
            .Select(x => new { x.Key.Name, x.Key.Status, Count = x.Count() })
            .OrderBy(x => x.Name).ThenBy(x => x.Status).ToListAsync(cancellationToken);
        text.AppendLine("Voucher summary:");
        foreach (var row in counts) text.AppendLine($"- {row.Name}: {row.Count} {row.Status}.");
        sources.Add(new("Job Work Reports", "/reports/job-work"));
    }

    private static string? VoucherUrl(string type, long id) => type switch
    {
        "JOB_WORK_OUT_ORDER" => $"/vouchers/job-work-out-order?voucherId={id}",
        "MATERIAL_OUT" => $"/vouchers/material-out?voucherId={id}",
        "MATERIAL_IN" => $"/vouchers/material-in?voucherId={id}",
        "MASTER_JOB_ORDER" => $"/vouchers/master-job-order?voucherId={id}",
        _ => null
    };

    private static string ExtractSearchToken(string question) => TokenRegex().Matches(question)
        .Select(x => x.Value.Trim())
        .Where(x => x.Length >= 2 && !StopWords.Contains(x))
        .OrderByDescending(x => x.Any(char.IsDigit))
        .ThenByDescending(x => x.Length)
        .FirstOrDefault() ?? string.Empty;

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    [GeneratedRegex("[A-Za-z0-9][A-Za-z0-9_./-]*", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
