using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed class BillOfMaterialRepository(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext)
{
    public async Task<IReadOnlyList<BillOfMaterialListItem>> GetListAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.BillOfMaterials.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId)
            .OrderBy(x => x.StockItem.Name).ThenByDescending(x => x.IsDefault).ThenBy(x => x.Name)
            .Select(x => new BillOfMaterialListItem
            {
                Id = x.Id, StockItemId = x.StockItemId, StockItemName = x.StockItem.Name,
                Name = x.Name, OutputQuantity = x.OutputQuantity, UqcShortName = x.StockItem.Uqc.ShortName,
                VersionNumber = x.CurrentRevision == null ? x.VersionNumber : x.CurrentRevision.RevisionNumber, IsDefault = x.IsDefault, IsActive = x.IsActive,
                ComponentCount = x.Lines.Count, ConcurrencyToken = x.ConcurrencyToken
            }).ToListAsync(cancellationToken);
    }

    public async Task<BillOfMaterialLookupData> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return new BillOfMaterialLookupData
        {
            StockItems = await db.StockItems.AsNoTracking()
                .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new BomStockItemLookup { Id = x.Id, Name = x.Name, UqcId = x.UqcId, UqcShortName = x.Uqc.ShortName })
                .ToListAsync(cancellationToken),
            Processes = await db.Processes.AsNoTracking()
                .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
                .Select(x => new BomProcessLookup { Id = x.Id, Name = x.Name }).ToListAsync(cancellationToken),
            Boms = (await GetListAsync(cancellationToken)).ToList()
        };
    }

    public async Task<BillOfMaterialEditModel?> GetForEditAsync(long id, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.BillOfMaterials.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.Id == id)
            .Select(x => new BillOfMaterialEditModel
            {
                Id = x.Id, StockItemId = x.StockItemId, StockItemText = x.StockItem.Name, Name = x.Name,
                OutputQuantity = x.OutputQuantity, VersionNumber = x.CurrentRevision == null ? x.VersionNumber : x.CurrentRevision.RevisionNumber, IsDefault = x.IsDefault,
                IsActive = x.IsActive, ConcurrencyToken = x.ConcurrencyToken,
                Lines = x.Lines.OrderBy(l => l.LineNumber).Select(l => new BillOfMaterialLineEditModel
                {
                    Id = l.Id, LineNumber = l.LineNumber, ComponentStockItemId = l.ComponentStockItemId,
                    ComponentStockItemText = l.ComponentStockItem.Name, ComponentVariantId = l.ComponentVariantId,
                    UqcId = l.UqcId, UqcShortName = l.Uqc.ShortName, RequiredQuantity = l.RequiredQuantity,
                    ChildBomId = l.ChildBomId, ChildBomText = l.ChildBom == null ? string.Empty : l.ChildBom.Name,
                    ProcessId = l.ProcessId, ProcessText = l.Process == null ? string.Empty : l.Process.Name, Notes = l.Notes
                }).ToList()
            }).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BillOfMaterialRevisionListItem>> GetRevisionHistoryAsync(long bomId, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.BillOfMaterialRevisions.AsNoTracking()
            .Where(x => x.Bom.CompanyId == companyContext.CompanyId && x.BomId == bomId)
            .OrderByDescending(x => x.RevisionNumber)
            .Select(x => new BillOfMaterialRevisionListItem
            {
                Id = x.Id,
                RevisionNumber = x.RevisionNumber,
                Name = x.Name,
                OutputQuantity = x.OutputQuantity,
                Status = x.Status,
                ChangeReason = x.ChangeReason,
                CreatedAtUtc = x.CreatedAtUtc,
                CreatedBy = x.CreatedBy,
                ComponentCount = x.Lines.Count,
                IsCurrent = x.Bom.CurrentRevisionId == x.Id
            }).ToListAsync(cancellationToken);
    }

    public async Task<BillOfMaterialRevisionComparison?> GetRevisionComparisonAsync(
        long bomId, long olderRevisionId, long newerRevisionId, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var revisions = await db.BillOfMaterialRevisions.AsNoTracking()
            .Where(x => x.Bom.CompanyId == companyContext.CompanyId && x.BomId == bomId &&
                        (x.Id == olderRevisionId || x.Id == newerRevisionId))
            .Include(x => x.StockItem).ThenInclude(x => x.Uqc)
            .Include(x => x.Lines).ThenInclude(x => x.ComponentStockItem)
            .Include(x => x.Lines).ThenInclude(x => x.ComponentVariant).ThenInclude(x => x!.Colour)
            .Include(x => x.Lines).ThenInclude(x => x.ComponentVariant).ThenInclude(x => x!.Size)
            .Include(x => x.Lines).ThenInclude(x => x.Uqc)
            .Include(x => x.Lines).ThenInclude(x => x.ChildBom)
            .Include(x => x.Lines).ThenInclude(x => x.ChildBomRevision)
            .Include(x => x.Lines).ThenInclude(x => x.Process)
            .AsSplitQuery().ToListAsync(cancellationToken);
        if (revisions.Count != 2) return null;
        return new BillOfMaterialRevisionComparison
        {
            Older = MapRevision(revisions.Single(x => x.Id == olderRevisionId)),
            Newer = MapRevision(revisions.Single(x => x.Id == newerRevisionId))
        };
    }

    public async Task<OperationResult> SaveAsync(BillOfMaterialEditModel model, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ManageMasters, cancellationToken);
        Normalize(model);
        if (model.StockItemId <= 0) return OperationResult.Fail("Select the BOM output Stock Item.");
        if (string.IsNullOrWhiteSpace(model.Name)) return OperationResult.Fail("BOM Name is required.");
        if (model.OutputQuantity <= 0) return OperationResult.Fail("BOM output quantity must be greater than zero.");
        if (model.Lines.Count == 0) return OperationResult.Fail("Add at least one BOM component.");
        if (model.Lines.Any(x => x.ComponentStockItemId <= 0 || x.RequiredQuantity <= 0))
            return OperationResult.Fail("Every BOM component requires a Stock Item and a quantity greater than zero.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        await AcquireGraphMutationLockAsync(db, cancellationToken);
        try
        {
            var stockIds = model.Lines.Select(x => x.ComponentStockItemId).Append(model.StockItemId).Distinct().ToList();
            var items = await db.StockItems.Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive && stockIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
            if (items.Count != stockIds.Count) return await RollbackFail(transaction, "One or more Stock Items are unavailable.", cancellationToken);

            var childIds = model.Lines.Where(x => x.ChildBomId.HasValue).Select(x => x.ChildBomId!.Value).Distinct().ToList();
            var childBoms = await db.BillOfMaterials.AsNoTracking().Where(x => x.CompanyId == companyContext.CompanyId && childIds.Contains(x.Id) && x.IsActive)
                .ToDictionaryAsync(x => x.Id, cancellationToken);
            if (childBoms.Count != childIds.Count) return await RollbackFail(transaction, "One or more Child BOMs are unavailable.", cancellationToken);
            foreach (var line in model.Lines)
            {
                line.UqcId = items[line.ComponentStockItemId].UqcId;
                if (line.ChildBomId is long childId && childBoms[childId].StockItemId != line.ComponentStockItemId)
                    return await RollbackFail(transaction, $"Child BOM output must match component '{items[line.ComponentStockItemId].Name}'.", cancellationToken);
            }
            var cycle = await DetectCycleAsync(db, model.Id, model.StockItemId, childIds, cancellationToken);
            if (cycle) return await RollbackFail(transaction, "A BOM cannot directly or indirectly contain itself.", cancellationToken);

            var childRevisionIds = childBoms.ToDictionary(x => x.Key, x => x.Value.CurrentRevisionId);
            if (childRevisionIds.Values.Any(x => !x.HasValue))
                return await RollbackFail(transaction, "A selected Child BOM has no immutable revision. Restart TexTrack so database migrations can finish.", cancellationToken);
            var contentHash = ComputeContentHash(model, childRevisionIds!);

            BillOfMaterial entity;
            var now = DateTimeOffset.UtcNow;
            if (model.Id == 0)
            {
                entity = new BillOfMaterial { CompanyId = companyContext.CompanyId, CreatedAtUtc = now, CreatedBy = companyContext.Actor };
                db.BillOfMaterials.Add(entity);
            }
            else
            {
                entity = await db.BillOfMaterials.Include(x => x.Lines).Include(x => x.CurrentRevision).ThenInclude(x => x!.Lines)
                    .SingleOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.Id == model.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The selected BOM no longer exists.");
                if (!string.Equals(entity.ConcurrencyToken, model.ConcurrencyToken, StringComparison.Ordinal))
                    return await RollbackFail(transaction, "This BOM was changed by another user. Reload and try again.", cancellationToken);
                if (entity.CurrentRevision is not null && ComputeContentHash(entity.CurrentRevision) == contentHash)
                {
                    var metadataChanged = entity.IsDefault != model.IsDefault || entity.IsActive != model.IsActive;
                    if (model.IsDefault)
                    {
                        var oldDefaults = await db.BillOfMaterials.Where(x => x.CompanyId == companyContext.CompanyId && x.StockItemId == model.StockItemId && x.Id != model.Id && x.IsDefault).ToListAsync(cancellationToken);
                        oldDefaults.ForEach(x => x.IsDefault = false);
                    }
                    entity.IsDefault = model.IsDefault;
                    entity.IsActive = model.IsActive;
                    if (metadataChanged)
                    {
                        entity.ModifiedAtUtc = now;
                        entity.ModifiedBy = companyContext.Actor;
                        entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    await transaction.CommitAsync(cancellationToken);
                    return OperationResult.Ok(metadataChanged
                        ? $"BOM '{entity.Name}' metadata updated; revision v{entity.VersionNumber} is unchanged."
                        : $"No BOM changes detected. Revision v{entity.VersionNumber} remains current.", entity.Id);
                }

                db.BillOfMaterialLines.RemoveRange(entity.Lines);
            }

            if (model.IsDefault)
            {
                var oldDefaults = await db.BillOfMaterials.Where(x => x.CompanyId == companyContext.CompanyId && x.StockItemId == model.StockItemId && x.Id != model.Id && x.IsDefault).ToListAsync(cancellationToken);
                oldDefaults.ForEach(x => x.IsDefault = false);
            }
            entity.StockItemId = model.StockItemId;
            entity.Name = model.Name;
            entity.NameNormalized = NormalizeName(model.Name);
            entity.OutputQuantity = model.OutputQuantity;
            var nextRevisionNumber = entity.CurrentRevision is null ? 1 : entity.CurrentRevision.RevisionNumber + 1;
            entity.VersionNumber = nextRevisionNumber;
            entity.IsDefault = model.IsDefault;
            entity.IsActive = model.IsActive;
            entity.ModifiedAtUtc = now; entity.ModifiedBy = companyContext.Actor; entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync(cancellationToken);

            foreach (var (line, index) in model.Lines.Select((line, index) => (line, index)))
                entity.Lines.Add(new BillOfMaterialLine
                {
                    LineNumber = index + 1, ComponentStockItemId = line.ComponentStockItemId,
                    ComponentVariantId = line.ComponentVariantId, UqcId = line.UqcId,
                    RequiredQuantity = line.RequiredQuantity, ChildBomId = line.ChildBomId,
                    ProcessId = line.ProcessId, Notes = line.Notes
                });

            if (entity.CurrentRevision is not null)
                entity.CurrentRevision.Status = "Superseded";
            var revision = new BillOfMaterialRevision
            {
                BomId = entity.Id,
                RevisionNumber = nextRevisionNumber,
                StockItemId = model.StockItemId,
                Name = model.Name,
                OutputQuantity = model.OutputQuantity,
                ContentHash = contentHash,
                Status = model.IsActive ? "Active" : "Inactive",
                ChangeReason = string.IsNullOrWhiteSpace(model.ChangeReason)
                    ? (nextRevisionNumber == 1 ? "Initial revision" : "BOM definition changed")
                    : model.ChangeReason.Trim(),
                CreatedAtUtc = now,
                CreatedBy = companyContext.Actor
            };
            foreach (var (line, index) in model.Lines.Select((line, index) => (line, index)))
                revision.Lines.Add(new BillOfMaterialRevisionLine
                {
                    LineNumber = index + 1,
                    ComponentStockItemId = line.ComponentStockItemId,
                    ComponentVariantId = line.ComponentVariantId,
                    UqcId = line.UqcId,
                    RequiredQuantity = line.RequiredQuantity,
                    ChildBomId = line.ChildBomId,
                    ChildBomRevisionId = line.ChildBomId is long childBomId ? childRevisionIds[childBomId] : null,
                    ProcessId = line.ProcessId,
                    Notes = line.Notes
                });
            db.BillOfMaterialRevisions.Add(revision);
            await db.SaveChangesAsync(cancellationToken);
            entity.CurrentRevisionId = revision.Id;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"BOM '{entity.Name}' revision v{nextRevisionNumber} saved successfully.", entity.Id);
        }
        catch (Exception exception) when (
            exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail("Another BOM structure changed concurrently. Reload the BOM graph and try again.");
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail("A BOM with the same name already exists for this Stock Item.");
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    public async Task<OperationResult> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ManageMasters, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        await AcquireGraphMutationLockAsync(db, cancellationToken);
        var entity = await db.BillOfMaterials.Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.Id == id, cancellationToken);
        if (entity is null) return OperationResult.Fail("The selected BOM no longer exists.");
        if (await db.JobWorkOrderBomStages.AnyAsync(x => x.SourceBomId == id, cancellationToken))
            return OperationResult.Fail("This BOM is already snapshotted in a Job Work Out Order and cannot be deleted. Mark it inactive instead.");
        if (await db.BillOfMaterialLines.AnyAsync(x => x.ChildBomId == id, cancellationToken))
            return OperationResult.Fail("This BOM is used as a Child BOM. Remove that link first.");
        entity.IsActive = false;
        entity.ModifiedAtUtc = DateTimeOffset.UtcNow;
        entity.ModifiedBy = companyContext.Actor;
        entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OperationResult.Ok($"BOM '{entity.Name}' marked inactive. Its immutable revision history was retained.");
    }

    private static Task AcquireGraphMutationLockAsync(TexTrackDbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(4774186387591326797)", cancellationToken);

    private async Task<bool> DetectCycleAsync(TexTrackDbContext db, long currentBomId, long outputItemId, IReadOnlyCollection<long> childBomIds, CancellationToken token)
    {
        if (childBomIds.Count == 0) return false;
        var all = await db.BillOfMaterials.AsNoTracking().Where(x => x.CompanyId == companyContext.CompanyId)
            .Select(x => new { x.Id, x.StockItemId, Children = x.Lines.Where(l => l.ChildBomId != null).Select(l => l.ChildBomId!.Value).ToList() })
            .ToListAsync(token);
        var map = all.ToDictionary(x => x.Id, x => x);
        var stack = new Stack<long>(childBomIds);
        var seen = new HashSet<long>();
        while (stack.TryPop(out var id))
        {
            if (id == currentBomId && currentBomId != 0) return true;
            if (!seen.Add(id) || !map.TryGetValue(id, out var bom)) continue;
            if (bom.StockItemId == outputItemId) return true;
            foreach (var child in bom.Children) stack.Push(child);
        }
        return false;
    }

    private static void Normalize(BillOfMaterialEditModel model)
    {
        model.Name = model.Name.Trim();
        model.Lines = model.Lines.Where(x => x.ComponentStockItemId > 0 || x.RequiredQuantity != 0).ToList();
        foreach (var line in model.Lines) line.Notes = line.Notes.Trim();
    }
    private static string NormalizeName(string value) => string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static BillOfMaterialRevisionDetail MapRevision(BillOfMaterialRevision revision) => new()
    {
        Id = revision.Id,
        RevisionNumber = revision.RevisionNumber,
        Name = revision.Name,
        StockItemName = revision.StockItem.Name,
        OutputQuantity = revision.OutputQuantity,
        UqcShortName = revision.StockItem.Uqc.ShortName,
        Status = revision.Status,
        ChangeReason = revision.ChangeReason,
        CreatedAtUtc = revision.CreatedAtUtc,
        CreatedBy = revision.CreatedBy,
        Lines = revision.Lines.OrderBy(x => x.LineNumber).Select(x => new BillOfMaterialRevisionLineDetail
        {
            LineNumber = x.LineNumber,
            ComponentStockItemName = x.ComponentStockItem.Name,
            VariantName = x.ComponentVariant == null ? string.Empty :
                $"{x.ComponentVariant.Colour!.Name} / {x.ComponentVariant.Size!.Name}",
            RequiredQuantity = x.RequiredQuantity,
            UqcShortName = x.Uqc.ShortName,
            ChildBomName = x.ChildBom == null ? string.Empty : x.ChildBom.Name,
            ChildBomRevisionNumber = x.ChildBomRevision == null ? null : x.ChildBomRevision.RevisionNumber,
            ProcessName = x.Process == null ? string.Empty : x.Process.Name,
            Notes = x.Notes
        }).ToList()
    };
    private static string ComputeContentHash(BillOfMaterialEditModel model, IReadOnlyDictionary<long, long?> childRevisionIds)
    {
        var value = new StringBuilder()
            .Append(model.StockItemId).Append('|')
            .Append(NormalizeName(model.Name)).Append('|')
            .Append(model.OutputQuantity.ToString("0.####", CultureInfo.InvariantCulture));
        foreach (var line in model.Lines.Select((line, index) => (line, index)))
        {
            value.Append('|').Append(line.index + 1)
                .Append(':').Append(line.line.ComponentStockItemId)
                .Append(':').Append(line.line.ComponentVariantId ?? 0)
                .Append(':').Append(line.line.UqcId)
                .Append(':').Append(line.line.RequiredQuantity.ToString("0.####", CultureInfo.InvariantCulture))
                .Append(':').Append(line.line.ChildBomId ?? 0)
                .Append(':').Append(line.line.ChildBomId is long childId ? childRevisionIds[childId] ?? 0 : 0)
                .Append(':').Append(line.line.ProcessId ?? 0)
                .Append(':').Append(line.line.Notes.Trim());
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }
    private static string ComputeContentHash(BillOfMaterialRevision revision)
    {
        var value = new StringBuilder()
            .Append(revision.StockItemId).Append('|')
            .Append(NormalizeName(revision.Name)).Append('|')
            .Append(revision.OutputQuantity.ToString("0.####", CultureInfo.InvariantCulture));
        foreach (var line in revision.Lines.OrderBy(x => x.LineNumber))
        {
            value.Append('|').Append(line.LineNumber)
                .Append(':').Append(line.ComponentStockItemId)
                .Append(':').Append(line.ComponentVariantId ?? 0)
                .Append(':').Append(line.UqcId)
                .Append(':').Append(line.RequiredQuantity.ToString("0.####", CultureInfo.InvariantCulture))
                .Append(':').Append(line.ChildBomId ?? 0)
                .Append(':').Append(line.ChildBomRevisionId ?? 0)
                .Append(':').Append(line.ProcessId ?? 0)
                .Append(':').Append(line.Notes.Trim());
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }
    private static async Task<OperationResult> RollbackFail(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, string message, CancellationToken token)
    { await tx.RollbackAsync(token); return OperationResult.Fail(message); }
}
