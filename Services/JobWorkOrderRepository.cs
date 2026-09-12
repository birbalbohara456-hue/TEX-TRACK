using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed class JobWorkOrderRepository(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext,
    VoucherLifecycleService lifecycle,
    VoucherSequenceAllocator sequenceAllocator,
    VoucherAuditHistoryService? voucherAuditHistory = null)
{
    private const string VoucherTypeCode = "JOB_WORK_OUT_ORDER";
    private readonly VoucherAuditHistoryService fullAudit = voucherAuditHistory ?? new(contextFactory);

    public async Task<List<JobWorkBomStageEditModel>> BuildBomStagePreviewAsync(
        long bomId,
        decimal outputQuantity,
        long? defaultJobWorkerId,
        long? defaultGodownId,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var graph = await LoadBomGraphAsync(db, new[] { bomId }, cancellationToken);
        if (!graph.TryGetValue(bomId, out var root)) return new();

        var workers = await db.Ledgers.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive && x.IsJobWorker)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var godowns = await db.Godowns.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var processes = await db.Processes.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var result = new List<JobWorkBomStageEditModel>();
        var sequence = 0;
        AddBomStagePreview(root, graph, outputQuantity, 0, "1", null,
            null, defaultJobWorkerId, defaultGodownId, true,
            workers, godowns, processes, result, ref sequence);
        return result;
    }

    public async Task<IReadOnlyList<JobWorkOrderListItem>> GetListAsync(
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Vouchers
            .AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId &&
                        x.FinancialYearId == companyContext.FinancialYearId &&
                        (x.VoucherType.SystemTypeCode == VoucherTypeCode ||
                         (x.VoucherType.ParentVoucherType != null && x.VoucherType.ParentVoucherType.SystemTypeCode == VoucherTypeCode)));

        var search = Normalize(searchText);
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                x.VoucherNumberNormalized.Contains(search) ||
                x.Batch.ToUpper().Contains(search) ||
                x.ReferenceNumber.ToUpper().Contains(search) ||
                (x.PartyLedger != null && x.PartyLedger.NameNormalized.Contains(search)) ||
                x.JobWorkFinishedGoods.Any(f => f.StockItem.NameNormalized.Contains(search)));
        }

        var entities = await query
            .Include(x => x.PartyLedger)
            .Include(x => x.VoucherType)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.StockItem)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.Colour)
            .AsSplitQuery()
            .OrderByDescending(x => x.VoucherDate)
            .ThenByDescending(x => x.SequenceNumber)
            .ToListAsync(cancellationToken);

        return entities.Select(x => new JobWorkOrderListItem
        {
            Id = x.Id,
            VoucherNumber = x.VoucherNumber,
            VoucherTypeName = x.VoucherType.Name,
            VoucherDate = x.VoucherDate,
            Batch = x.Batch,
            JobWorkerName = x.PartyLedger?.Name ?? string.Empty,
            DueDate = x.DueDate,
            Status = x.Status,
            ConcurrencyToken = x.ConcurrencyToken,
            TotalOrderedQuantity = x.JobWorkFinishedGoods.Sum(f => f.OrderedQuantity),
            FinishedGoodsSummary = string.Join(", ", x.JobWorkFinishedGoods
                .OrderBy(f => f.LineNumber)
                .Select(f => f.Colour is null ? f.StockItem.Name : $"{f.StockItem.Name} · {f.Colour.Name}"))
        }).ToList();
    }

    public async Task<JobWorkOrderLookupData> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var voucherTypes = await db.VoucherTypes
            .AsNoTracking()
            .Include(x => x.ParentVoucherType)
            .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive &&
                        (x.SystemTypeCode == VoucherTypeCode ||
                         (x.ParentVoucherType != null && x.ParentVoucherType.SystemTypeCode == VoucherTypeCode)))
            .OrderByDescending(x => x.IsSystem)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var voucherType = voucherTypes.Single(x => x.IsSystem && x.SystemTypeCode == VoucherTypeCode);

        var stockItems = await db.StockItems
            .AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive)
            .Include(x => x.StockGroup)
            .Include(x => x.Uqc)
            .Include(x => x.Colours).ThenInclude(x => x.Colour)
            .Include(x => x.Sizes).ThenInclude(x => x.Size)
            .Include(x => x.Variants)
            .AsSplitQuery()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var mappedItems = stockItems.Select(MapStockItemLookup).ToList();

        var componentRateHistory = await db.JobWorkOrderComponents
            .AsNoTracking()
            .Where(x => x.XmlRate > 0 &&
                        x.FinishedGood.Voucher.CompanyId == companyContext.CompanyId &&
                        x.FinishedGood.Voucher.FinancialYearId == companyContext.FinancialYearId &&
                        x.FinishedGood.Voucher.Status != "Cancelled")
            .OrderByDescending(x => x.FinishedGood.Voucher.VoucherDate)
            .ThenByDescending(x => x.FinishedGood.Voucher.SequenceNumber)
            .ThenByDescending(x => x.Id)
            .Select(x => new { x.StockItemId, x.XmlRate })
            .ToListAsync(cancellationToken);
        var latestRateByItem = componentRateHistory
            .GroupBy(x => x.StockItemId)
            .ToDictionary(x => x.Key, x => x.First().XmlRate);
        foreach (var item in mappedItems)
        {
            if (latestRateByItem.TryGetValue(item.Id, out var latestRate)) item.LatestComponentRate = latestRate;
        }

        return new JobWorkOrderLookupData
        {
            VoucherTypeId = voucherType.Id,
            AllowManualNumbering = voucherType.NumberingMode == "Manual",
            NumberingMode = voucherType.NumberingMode,
            VoucherPrefix = voucherType.Prefix,
            StartingNumber = voucherType.StartingNumber,
            VoucherTypes = await BuildVoucherTypeLookupsAsync(db, voucherTypes, cancellationToken),
            JobWorkers = await db.Ledgers
                .AsNoTracking()
                .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive && x.IsJobWorker)
                .OrderBy(x => x.Name)
                .Select(x => new JobWorkerLookupItem
                {
                    Id = x.Id,
                    Name = x.Name,
                    GroupName = x.LedgerGroup.Name
                })
                .ToListAsync(cancellationToken),
            FinishedGoods = mappedItems
                .Where(x => x.RootClassification == "FinishedGoods")
                .ToList(),
            // Any active Stock Item can be issued to production. WIP classification is
            // derived from transactions; the permanent Stock Group never limits issue.
            Components = mappedItems,
            Godowns = await db.Godowns
                .AsNoTracking()
                .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new JobWorkGodownLookup { Id = x.Id, Name = x.Name })
                .ToListAsync(cancellationToken),
            Processes = await db.Processes
                .AsNoTracking()
                .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
                .Select(x => new JobWorkProcessLookup { Id = x.Id, Name = x.Name })
                .ToListAsync(cancellationToken),
            BillOfMaterials = await db.BillOfMaterials.AsNoTracking()
                .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive)
                .OrderBy(x => x.StockItem.Name).ThenByDescending(x => x.IsDefault).ThenBy(x => x.Name)
                .Select(x => new JobWorkBomLookup
                {
                    Id = x.Id,
                    StockItemId = x.StockItemId,
                    Name = x.Name,
                    VersionNumber = x.VersionNumber,
                    IsDefault = x.IsDefault
                }).ToListAsync(cancellationToken)
        };
    }

    public async Task<JobWorkOrderEditModel> CreateNewModelAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var voucherType = await db.VoucherTypes.AsNoTracking().SingleAsync(
            x => x.CompanyId == companyContext.CompanyId && x.SystemTypeCode == VoucherTypeCode,
            cancellationToken);
        var sequence = await GetNextSequenceAsync(db, voucherType.Id, cancellationToken);
        var financialYear = await db.FinancialYears.AsNoTracking().SingleAsync(
            x => x.Id == companyContext.FinancialYearId && x.CompanyId == companyContext.CompanyId,
            cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var voucherDate = today < financialYear.StartDate
            ? financialYear.StartDate
            : today > financialYear.EndDate
                ? financialYear.EndDate
                : today;

        return new JobWorkOrderEditModel
        {
            SequenceNumber = sequence,
            VoucherTypeId = voucherType.Id,
            VoucherTypeText = voucherType.Name,
            VoucherNumber = voucherType.NumberingMode == "Manual" ? string.Empty : FormatVoucherNumber(voucherType, sequence),
            ReferenceNumber = voucherType.NumberingMode == "Manual" ? string.Empty : FormatVoucherNumber(voucherType, sequence),
            VoucherDate = voucherDate,
            DueDate = voucherDate,
            Status = "Open",
            FinishedGoods = new List<JobWorkFinishedGoodEditModel> { new() }
        };
    }

    public async Task<JobWorkOrderEditModel?> GetForEditAsync(long id, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Vouchers
            .AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId &&
                        x.FinancialYearId == companyContext.FinancialYearId &&
                        x.Id == id &&
                        (x.VoucherType.SystemTypeCode == VoucherTypeCode ||
                         (x.VoucherType.ParentVoucherType != null && x.VoucherType.ParentVoucherType.SystemTypeCode == VoucherTypeCode)))
            .Include(x => x.VoucherType).ThenInclude(x => x.ParentVoucherType)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.SizeAllocations)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.Components).ThenInclude(x => x.Uqc)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.Processes).ThenInclude(x => x.Process)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.StockItem).ThenInclude(x => x.Uqc)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.BomStages).ThenInclude(x => x.SourceBom)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.BomStages).ThenInclude(x => x.OutputStockItem)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.BomStages).ThenInclude(x => x.OutputUqc)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.BomStages).ThenInclude(x => x.AssignedJobWorker)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.BomStages).ThenInclude(x => x.AssignmentHistory)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.BomStages).ThenInclude(x => x.OutputGodown)
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);

        if (entity is null) return null;

        return new JobWorkOrderEditModel
        {
            Id = entity.Id,
            SequenceNumber = entity.SequenceNumber,
            VoucherTypeId = entity.VoucherTypeId,
            VoucherTypeText = entity.VoucherType.Name,
            VoucherNumber = entity.VoucherNumber,
            VoucherDate = entity.VoucherDate,
            Batch = entity.Batch,
            JobWorkerLedgerId = entity.PartyLedgerId,
            DueDate = entity.DueDate,
            ReferenceNumber = entity.ReferenceNumber,
            Narration = entity.Narration,
            Status = entity.Status,
            ConcurrencyToken = entity.ConcurrencyToken,
            FinishedGoods = entity.JobWorkFinishedGoods
                .OrderBy(x => x.LineNumber)
                .Select(x => new JobWorkFinishedGoodEditModel
                {
                    Id = x.Id,
                    DesignGroupKey = x.DesignGroupKey,
                    StockItemId = x.StockItemId,
                    ColourId = x.ColourId,
                    FinishedGoodsGodownId = x.FinishedGoodsGodownId,
                    DestinationGodownId = x.DestinationGodownId,
                    XmlRate = x.XmlRate,
                    XmlAmount = x.XmlAmount,
                    BillOfMaterialId = x.BomStages.Where(s => s.IsFinalStage).Select(s => s.SourceBomId).FirstOrDefault(),
                    BillOfMaterialText = x.BomStages.Where(s => s.IsFinalStage).Select(s => s.SourceBom == null ? string.Empty : s.SourceBom.Name).FirstOrDefault() ?? string.Empty,
                    SizeQuantities = x.SizeAllocations
                        .OrderBy(a => a.SizeId)
                        .Select(a => new JobWorkSizeQuantityEditModel
                        {
                            StockItemVariantId = a.StockItemVariantId,
                            MasterJobOrderAllocationId = a.MasterJobOrderAllocationId,
                            SizeId = a.SizeId,
                            Quantity = a.Quantity
                        }).ToList(),
                    Components = x.Components
                        .OrderBy(c => c.LineNumber)
                        .Select(c => new JobWorkComponentEditModel
                        {
                            Id = c.Id,
                            StockItemId = c.StockItemId,
                            UqcId = c.UqcId,
                            UqcShortName = c.Uqc.ShortName,
                            ComponentGodownId = c.ComponentGodownId,
                            RequiredQuantity = c.RequiredQuantity,
                            XmlRate = c.XmlRate,
                            XmlAmount = c.XmlAmount,
                            ComponentVariantId = c.ComponentVariantId,
                            BomLevel = c.BomLevel,
                            BomPath = c.BomPath,
                            IsProducedComponent = c.IsProducedComponent
                        }).ToList(),
                    BomStages = BuildStageEditModelsForLoad(x, entity.DueDate)
                }).ToList()
        };
    }

    public async Task<OperationResult> SaveAsync(
        JobWorkOrderEditModel model,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.OperateVouchers, cancellationToken);
        NormalizeModel(model);
        model.DueDate = CalculateOverallExpectedCompletion(model) ?? model.DueDate ?? model.VoucherDate;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var voucherType = await db.VoucherTypes
                .Include(x => x.ParentVoucherType)
                .SingleOrDefaultAsync(
                    x => x.CompanyId == companyContext.CompanyId &&
                         x.Id == model.VoucherTypeId && x.IsActive &&
                         (x.SystemTypeCode == VoucherTypeCode ||
                          (x.ParentVoucherType != null && x.ParentVoucherType.SystemTypeCode == VoucherTypeCode)),
                    cancellationToken);
            if (voucherType is null)
                return await FailAndRollbackAsync(db, transaction, model.Id, "Job Work Out Order voucher type is unavailable.", cancellationToken);

            var financialYear = await db.FinancialYears.SingleOrDefaultAsync(
                x => x.CompanyId == companyContext.CompanyId &&
                     x.Id == companyContext.FinancialYearId &&
                     x.IsActive,
                cancellationToken);
            if (financialYear is null)
                return await FailAndRollbackAsync(db, transaction, model.Id, "The active Financial Year is unavailable.", cancellationToken);
            if (model.VoucherDate < financialYear.StartDate || model.VoucherDate > financialYear.EndDate)
                return await FailAndRollbackAsync(db, transaction, model.Id,
                    $"Voucher Date must be between {financialYear.StartDate:dd-MM-yyyy} and {financialYear.EndDate:dd-MM-yyyy}.", cancellationToken);
            if (model.DueDate is not null && model.DueDate.Value < model.VoucherDate)
                return await FailAndRollbackAsync(db, transaction, model.Id, "Expected Completion cannot be earlier than Voucher Date.", cancellationToken);
            if (string.IsNullOrWhiteSpace(model.Batch))
                return await FailAndRollbackAsync(db, transaction, model.Id, "Batch is required.", cancellationToken);
            var validation = await ValidateFinishedGoodsAsync(db, model, cancellationToken);
            if (validation is not null)
                return await FailAndRollbackAsync(db, transaction, model.Id, validation, cancellationToken);

            // PartyLedgerId is retained only as a compatibility/reporting summary. The
            // authoritative job-worker assignment now belongs to each permanent BOM stage.
            var summaryWorkerId = model.FinishedGoods
                .SelectMany(x => x.BomStages)
                .OrderByDescending(x => x.IsFinalStage)
                .ThenBy(x => x.StageNumber)
                .Select(x => x.AssignedJobWorkerId)
                .FirstOrDefault(x => x.HasValue)
                ?? model.JobWorkerLedgerId; // legacy/manual JWO and import compatibility

            Voucher entity;
            var isNew = model.Id == 0;
            var assignmentOnlyAmendment = false;
            if (isNew)
            {
                var nextSequence = await sequenceAllocator.ReserveAsync(
                    db, voucherType, companyContext.Actor, DateTimeOffset.UtcNow, cancellationToken);
                entity = new Voucher
                {
                    CompanyId = companyContext.CompanyId,
                    FinancialYearId = companyContext.FinancialYearId,
                    VoucherTypeId = voucherType.Id,
                    SequenceNumber = nextSequence,
                    VoucherNumber = voucherType.NumberingMode == "Manual"
                        ? model.VoucherNumber.Trim()
                        : FormatVoucherNumber(voucherType, nextSequence),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    CreatedBy = companyContext.Actor
                };
                if (string.IsNullOrWhiteSpace(entity.VoucherNumber))
                    return await FailAndRollbackAsync(db, transaction, model.Id, "Voucher Number is required.", cancellationToken);
                db.Vouchers.Add(entity);
            }
            else
            {
                entity = await db.Vouchers
                    .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.SizeAllocations)
                    .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.Components)
                    .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.Processes)
                    .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.BomStages).ThenInclude(x => x.AssignmentHistory)
                    .SingleOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId &&
                                               x.FinancialYearId == companyContext.FinancialYearId &&
                                               x.Id == model.Id &&
                                               x.VoucherTypeId == voucherType.Id,
                        cancellationToken)
                    ?? throw new InvalidOperationException("The selected Job Work Out Order no longer exists.");

                if (!string.Equals(entity.ConcurrencyToken, model.ConcurrencyToken, StringComparison.Ordinal))
                    return await FailAndRollbackAsync(db, transaction, model.Id,
                        "This Job Work Out Order was changed by another user. Reload it and try again.", cancellationToken);

                assignmentOnlyAmendment = IsAssignmentOnlyAmendment(entity, model);
                var alterationLinks = await lifecycle.GetActiveLinkDescriptionsAsync(
                    db, companyContext.CompanyId, entity.Id, VoucherLinkScope.Any, cancellationToken);
                if (alterationLinks.Count > 0 && !assignmentOnlyAmendment)
                    return await FailAndRollbackAsync(db, transaction, model.Id,
                        VoucherLifecycleService.BuildBlockedMessage("altered", $"Job Work Out Order {entity.VoucherNumber}", alterationLinks), cancellationToken);

                if (voucherType.NumberingMode == "Manual")
                {
                    entity.VoucherNumber = model.VoucherNumber.Trim();
                    if (string.IsNullOrWhiteSpace(entity.VoucherNumber))
                        return await FailAndRollbackAsync(db, transaction, model.Id, "Voucher Number is required.", cancellationToken);
                }

                if (!assignmentOnlyAmendment)
                    return await FailAndRollbackAsync(db, transaction, model.Id,
                        "This change would replace permanent JWO stages. Save the revised BOM first, then use the guided JWO amendment workflow so existing stage IDs and voucher links remain intact.", cancellationToken);
            }

            entity.VoucherNumberNormalized = Normalize(entity.VoucherNumber);
            entity.VoucherDate = model.VoucherDate;
            entity.ReferenceNumber = model.ReferenceNumber;
            entity.Batch = model.Batch;
            entity.PartyLedgerId = summaryWorkerId;
            entity.DueDate = model.DueDate;
            entity.Narration = model.Narration;
            entity.Status = "Open";
            entity.ModifiedAtUtc = DateTimeOffset.UtcNow;
            entity.ModifiedBy = companyContext.Actor;
            entity.ConcurrencyToken = Guid.NewGuid().ToString("N");

            var duplicate = await db.Vouchers.AnyAsync(
                x => x.CompanyId == companyContext.CompanyId &&
                     x.FinancialYearId == companyContext.FinancialYearId &&
                     x.VoucherTypeId == voucherType.Id &&
                     x.VoucherNumberNormalized == entity.VoucherNumberNormalized &&
                     x.Id != entity.Id,
                cancellationToken);
            if (duplicate)
                return await FailAndRollbackAsync(db, transaction, model.Id,
                    $"Voucher Number '{entity.VoucherNumber}' already exists.", cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            if (isNew)
                await AddFinishedGoodsAsync(db, entity, model, cancellationToken);
            else
                await ApplyStageAssignmentsAsync(db, entity, model, cancellationToken);
            db.AuditLogs.Add(NewAudit(entity.Id, isNew ? "Create" : "Update", true,
                $"{(isNew ? "Created" : "Updated")} Job Work Out Order '{entity.VoucherNumber}' with stage-level job-worker assignments and Batch '{entity.Batch}'."));

            await db.SaveChangesAsync(cancellationToken);
            await RecordJwoRevisionAsync(db, entity.Id, isNew ? "Initial JWO revision" : "Stage assignment amendment", cancellationToken);
            if (!isNew)
                await JobWorkOrderStatusUpdater.UpdateAsync(db, entity.Id, DateTimeOffset.UtcNow, companyContext.Actor, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (!isNew)
            {
                await TallySyncStateTracker.MarkUpdatedNotExportedAsync(db, entity.Id, companyContext.Actor, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }
            await fullAudit.RecordAsync(
                db,
                entity.Id,
                isNew ? VoucherAuditActions.Create : VoucherAuditActions.Update,
                isNew ? "Initial Job Work Out Order save." : "Job Work Out Order stage-assignment amendment.",
                companyContext.Actor,
                entity.ModifiedAtUtc,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"Job Work Out Order {entity.VoucherNumber} saved successfully.", entity.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail("This Job Work Out Order was changed by another user. Reload and try again.");
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.SerializationFailure ||
            exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail("Another user saved a voucher at the same time. Reload the list and save again.");
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail(VoucherErrorMessages.For(exception));
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static bool IsAssignmentOnlyAmendment(Voucher entity, JobWorkOrderEditModel model)
    {
        if (entity.JobWorkFinishedGoods.Count != model.FinishedGoods.Count) return false;
        foreach (var fgModel in model.FinishedGoods)
        {
            var fg = entity.JobWorkFinishedGoods.SingleOrDefault(x => x.Id == fgModel.Id);
            if (fg is null || fg.StockItemId != fgModel.StockItemId || fg.ColourId != fgModel.ColourId ||
                fg.FinishedGoodsGodownId != fgModel.FinishedGoodsGodownId ||
                fg.DestinationGodownId != (fgModel.DestinationGodownId ?? fgModel.FinishedGoodsGodownId) ||
                fg.OrderedQuantity != fgModel.SizeQuantities.Where(x => x.Quantity > 0).Sum(x => x.Quantity)) return false;

            var modelStages = fgModel.BomStages.OrderBy(x => x.StagePath).ToList();
            var stages = fg.BomStages.OrderBy(x => x.StagePath).ToList();
            if (stages.Count != modelStages.Count) return false;
            for (var index = 0; index < stages.Count; index++)
            {
                var current = stages[index];
                var proposed = modelStages[index];
                if (!string.Equals(current.StagePath, proposed.StagePath, StringComparison.Ordinal) ||
                    current.SourceBomId != proposed.SourceBomId || current.SourceBomVersion != proposed.SourceBomVersion ||
                    current.OutputStockItemId != proposed.OutputStockItemId || current.OutputQuantity != proposed.OutputQuantity ||
                    current.ProcessId != proposed.ProcessId || current.OutputGodownId != proposed.OutputGodownId ||
                    current.IsFinalStage != proposed.IsFinalStage) return false;
            }
        }
        return true;
    }

    private async Task ApplyStageAssignmentsAsync(
        TexTrackDbContext db,
        Voucher entity,
        JobWorkOrderEditModel model,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var fgModel in model.FinishedGoods)
        {
            var fg = entity.JobWorkFinishedGoods.Single(x => x.Id == fgModel.Id);
            foreach (var proposed in fgModel.BomStages)
            {
                var stage = fg.BomStages.Single(x => x.StagePath == proposed.StagePath);
                var currentAssignment = stage.AssignmentHistory.SingleOrDefault(x => x.Status == "Active");
                if (stage.AssignedJobWorkerId == proposed.AssignedJobWorkerId &&
                    currentAssignment?.ExpectedCompletionDate == proposed.ExpectedCompletionDate &&
                    currentAssignment?.ExpectedProcessRate == proposed.ExpectedProcessRate) continue;
                foreach (var active in stage.AssignmentHistory.Where(x => x.Status == "Active"))
                {
                    active.Status = "Superseded";
                    active.ValidToUtc = now;
                }
                var nextVersion = stage.AssignmentHistory.Select(x => x.AssignmentVersion).DefaultIfEmpty(0).Max() + 1;
                stage.AssignmentHistory.Add(new JobWorkOrderStageAssignment
                {
                    AssignmentVersion = nextVersion,
                    JobWorkerId = proposed.AssignedJobWorkerId,
                    ExpectedCompletionDate = proposed.ExpectedCompletionDate,
                    ExpectedProcessRate = proposed.ExpectedProcessRate,
                    Status = "Active",
                    Reason = stage.AssignedJobWorkerId == proposed.AssignedJobWorkerId
                        ? "Stage expected completion or process rate amended without changing the permanent JWO stage"
                        : "Job worker assignment amended without changing the permanent JWO stage",
                    ValidFromUtc = now,
                    CreatedBy = companyContext.Actor
                });
                stage.AssignedJobWorkerId = proposed.AssignedJobWorkerId;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordJwoRevisionAsync(
        TexTrackDbContext db,
        long voucherId,
        string reason,
        CancellationToken cancellationToken)
    {
        var snapshot = await db.Vouchers.AsNoTracking()
            .Where(x => x.Id == voucherId)
            .Select(x => new
            {
                x.Id, x.VoucherNumber, x.VoucherDate, x.Batch, x.PartyLedgerId, x.DueDate, x.ReferenceNumber, x.Narration, x.Status,
                FinishedGoods = x.JobWorkFinishedGoods.OrderBy(f => f.LineNumber).Select(f => new
                {
                    f.Id, f.DesignGroupKey, f.StockItemId, f.ColourId, f.OrderedQuantity, f.FinishedGoodsGodownId, f.DestinationGodownId,
                    Stages = f.BomStages.OrderBy(s => s.StageNumber).Select(s => new
                    {
                        s.Id, s.StableKey, s.ParentStageId, s.SourceBomId, s.SourceBomRevisionId, s.SourceBomVersion,
                        s.StageNumber, s.StageLevel, s.StagePath, s.StageName, s.OutputStockItemId, s.OutputQuantity,
                        s.ProcessId, s.AssignedJobWorkerId, s.OutputGodownId, s.IsFinalStage,
                        Assignments = s.AssignmentHistory.OrderBy(a => a.AssignmentVersion).Select(a => new
                        {
                            a.Id, a.AssignmentVersion, a.JobWorkerId, a.ExpectedCompletionDate, a.ExpectedProcessRate,
                            a.Status, a.ValidFromUtc, a.ValidToUtc
                        })
                    })
                })
            }).SingleAsync(cancellationToken);
        var json = JsonSerializer.Serialize(snapshot);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var latest = await db.JobWorkOrderRevisions
            .Where(x => x.VoucherId == voucherId && x.IsCurrent)
            .SingleOrDefaultAsync(cancellationToken);
        if (latest?.ContentHash == hash) return;
        if (latest is not null) latest.IsCurrent = false;
        var next = await db.JobWorkOrderRevisions.Where(x => x.VoucherId == voucherId)
            .Select(x => (int?)x.RevisionNumber).MaxAsync(cancellationToken) ?? 0;
        db.JobWorkOrderRevisions.Add(new JobWorkOrderRevision
        {
            VoucherId = voucherId,
            RevisionNumber = next + 1,
            ContentHash = hash,
            SnapshotJson = json,
            ChangeReason = reason,
            IsCurrent = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = companyContext.Actor
        });
    }

    public async Task<OperationResult> CancelAsync(long id, string reason, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.CancelOrDeleteVouchers, cancellationToken);
        var reasonValidation = VoucherLifecycleService.ValidateCancellationReason(reason);
        if (reasonValidation is not null) return OperationResult.Fail(reasonValidation);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var entity = await db.Vouchers.SingleOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId &&
            x.FinancialYearId == companyContext.FinancialYearId && x.Id == id, cancellationToken);
        if (entity is null) return OperationResult.Fail("The selected voucher no longer exists.");
        if (entity.Status == "Cancelled") return OperationResult.Fail("This voucher is already cancelled.");

        var activeLinks = await lifecycle.GetActiveLinkDescriptionsAsync(
            db, companyContext.CompanyId, id, VoucherLinkScope.Any, cancellationToken);
        if (activeLinks.Count > 0)
            return OperationResult.Fail(VoucherLifecycleService.BuildBlockedMessage("cancelled", "This Job Work Out Order", activeLinks));

        var now = DateTimeOffset.UtcNow;
        var actor = companyContext.Actor;
        lifecycle.MarkCancelled(entity, reason, actor, now);
        db.AuditLogs.Add(VoucherLifecycleService.NewAudit(
            companyContext.CompanyId,
            entity.Id,
            "Cancel",
            true,
            $"Cancelled voucher '{entity.VoucherNumber}'. Reason: {entity.CancellationReason}",
            actor,
            now));
        await db.SaveChangesAsync(cancellationToken);
        await fullAudit.RecordAsync(
            db, entity.Id, VoucherAuditActions.Cancel, entity.CancellationReason,
            actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OperationResult.Ok($"Voucher '{entity.VoucherNumber}' cancelled successfully.", entity.Id);
    }

    public async Task<OperationResult> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.CancelOrDeleteVouchers, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var entity = await db.Vouchers
            .Include(x => x.VoucherType)
            .SingleOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId &&
                                       x.FinancialYearId == companyContext.FinancialYearId &&
                                       x.Id == id &&
                                       (x.VoucherType.SystemTypeCode == VoucherTypeCode ||
                                        (x.VoucherType.ParentVoucherType != null && x.VoucherType.ParentVoucherType.SystemTypeCode == VoucherTypeCode)),
                cancellationToken);
        if (entity is null) return OperationResult.Fail("The selected Job Work Out Order no longer exists.");

        var activeLinks = await lifecycle.GetActiveLinkDescriptionsAsync(
            db, companyContext.CompanyId, id, VoucherLinkScope.Any, cancellationToken);
        if (activeLinks.Count > 0)
            return OperationResult.Fail(VoucherLifecycleService.BuildBlockedMessage("deleted", "This Job Work Out Order", activeLinks));

        // Revision snapshots and stage-assignment history are internal ownership rows,
        // not external voucher dependencies. Remove them atomically with an unlinked JWO.
        var stageIds = await db.JobWorkOrderBomStages.Where(x => x.VoucherId == id)
            .Select(x => x.Id).ToListAsync(cancellationToken);
        db.JobWorkOrderStageAssignments.RemoveRange(
            await db.JobWorkOrderStageAssignments.Where(x => stageIds.Contains(x.BomStageId)).ToListAsync(cancellationToken));
        db.JobWorkOrderRevisions.RemoveRange(
            await db.JobWorkOrderRevisions.Where(x => x.VoucherId == id).ToListAsync(cancellationToken));
        db.VoucherLinks.RemoveRange(await db.VoucherLinks.Where(x => x.CompanyId == companyContext.CompanyId &&
            (x.SourceVoucherId == id || x.TargetVoucherId == id)).ToListAsync(cancellationToken));

        try
        {
            await fullAudit.RecordAsync(
                db, entity.Id, VoucherAuditActions.Delete,
                "Deleted unlinked Job Work Out Order.", companyContext.Actor,
                DateTimeOffset.UtcNow, cancellationToken);
            db.Vouchers.Remove(entity);
            db.AuditLogs.Add(NewAudit(id, "Delete", true, $"Deleted unlinked Job Work Out Order '{entity.VoucherNumber}'."));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"Job Work Out Order {entity.VoucherNumber} deleted.");
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail(VoucherErrorMessages.For(exception));
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail(VoucherErrorMessages.For(exception));
        }
    }

    private async Task<string?> ValidateFinishedGoodsAsync(
        TexTrackDbContext db,
        JobWorkOrderEditModel model,
        CancellationToken cancellationToken)
    {
        if (model.FinishedGoods.Count == 0) return "Add at least one Finished Good.";

        var duplicateKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var fgIndex = 0; fgIndex < model.FinishedGoods.Count; fgIndex++)
        {
            var fg = model.FinishedGoods[fgIndex];
            if (fg.StockItemId <= 0) return $"Select a Finished Good on line {fgIndex + 1}.";

            var item = await db.StockItems
                .AsNoTracking()
                .Include(x => x.StockGroup)
                .Include(x => x.Colours)
                .Include(x => x.Sizes)
                .Include(x => x.Variants)
                .SingleOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId &&
                                           x.Id == fg.StockItemId &&
                                           x.IsActive,
                    cancellationToken);
            if (item is null || item.StockGroup.RootClassification != "FinishedGoods")
                return $"Line {fgIndex + 1} must use an active Finished Goods Stock Item.";

            if (fg.FinishedGoodsGodownId is null)
                return $"Select a Finished Goods Godown for '{item.Name}'.";
            if (fg.DestinationGodownId is null)
                fg.DestinationGodownId = fg.FinishedGoodsGodownId;
            var validFgGodowns = await db.Godowns.AsNoTracking().Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive && (x.Id == fg.FinishedGoodsGodownId || x.Id == fg.DestinationGodownId)).Select(x => x.Id).ToListAsync(cancellationToken);
            if (!validFgGodowns.Contains(fg.FinishedGoodsGodownId.Value) || !validFgGodowns.Contains(fg.DestinationGodownId.Value))
                return $"Select valid active godowns for '{item.Name}'.";

            var colourIds = item.Colours.Select(x => x.ColourId).ToHashSet();
            if (colourIds.Count > 0 && fg.ColourId is null)
                return $"Select a Colour for '{item.Name}'.";
            if (fg.ColourId is not null && !colourIds.Contains(fg.ColourId.Value))
                return $"The selected Colour is not valid for '{item.Name}'.";

            var lineKey = $"{item.Id}:{fg.ColourId?.ToString() ?? "BASE"}";
            if (!duplicateKeys.Add(lineKey))
                return $"'{item.Name}' with the same Colour appears more than once.";

            var positiveAllocations = fg.SizeQuantities.Where(x => x.Quantity > 0).ToList();
            if (positiveAllocations.Count == 0)
                return $"Enter a quantity for '{item.Name}'.";
            if (fg.SizeQuantities.Any(x => x.Quantity < 0))
                return $"Quantities cannot be negative for '{item.Name}'.";

            var validVariants = item.Variants
                .Where(v => v.IsActive && v.ColourId == fg.ColourId)
                .ToDictionary(v => v.Id);
            foreach (var allocation in positiveAllocations)
            {
                if (!validVariants.TryGetValue(allocation.StockItemVariantId, out var variant) || variant.SizeId != allocation.SizeId)
                    return $"A size/colour variant is invalid for '{item.Name}'. Refresh the form and try again.";
            }

            var componentIds = new HashSet<long>();
            for (var componentIndex = 0; componentIndex < fg.Components.Count; componentIndex++)
            {
                var component = fg.Components[componentIndex];
                if (!string.IsNullOrWhiteSpace(component.BomPath)) continue;
                if (component.StockItemId == 0 && component.RequiredQuantity == 0) continue;
                if (component.StockItemId <= 0)
                    return $"Select Component {componentIndex + 1} under '{item.Name}'.";
                if (component.RequiredQuantity <= 0)
                    return $"Component quantity must be greater than zero under '{item.Name}'.";
                if (!componentIds.Add(component.StockItemId))
                    return $"The same Component is repeated under '{item.Name}'.";
                if (component.ComponentGodownId is null)
                    return $"Select a Component Godown for Component {componentIndex + 1} under '{item.Name}'.";
                var validComponentGodown = await db.Godowns.AsNoTracking().AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.Id == component.ComponentGodownId.Value && x.IsActive, cancellationToken);
                if (!validComponentGodown)
                    return $"Select a valid active Component Godown under '{item.Name}'.";

                var componentItem = await db.StockItems
                    .AsNoTracking()
                    .Include(x => x.StockGroup)
                    .SingleOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId &&
                                               x.Id == component.StockItemId &&
                                               x.IsActive,
                        cancellationToken);
                if (componentItem is null)
                    return $"Select an active Stock Item as a Component under '{item.Name}'.";

                component.UqcId = componentItem.UqcId;
            }

            var processIds = new HashSet<long>();
            for (var pIndex = 0; pIndex < fg.Processes.Count; pIndex++)
            {
                var p = fg.Processes[pIndex];
                if (p.ProcessId <= 0) return $"Select a valid Process on line {pIndex + 1} under '{item.Name}'.";
                if (!processIds.Add(p.ProcessId)) return $"Process '{p.ProcessText}' is repeated under '{item.Name}'.";
                if (p.ExpectedRate < 0) return $"Expected rate cannot be negative for process '{p.ProcessText}'.";
                if (string.IsNullOrWhiteSpace(p.RateBasis)) p.RateBasis = "Per Quantity";
            }

            if (fg.BillOfMaterialId is long bomId)
            {
                var bom = await db.BillOfMaterials.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.Id == bomId && x.IsActive, cancellationToken);
                if (bom is null) return $"The selected BOM for '{item.Name}' is unavailable.";
                if (bom.StockItemId != fg.StockItemId) return $"The selected BOM does not produce '{item.Name}'.";

                if (fg.BomStages.Count == 0)
                    return $"Reload the BOM production stages for '{item.Name}'.";

                for (var stageIndex = 0; stageIndex < fg.BomStages.Count; stageIndex++)
                {
                    var stage = fg.BomStages[stageIndex];
                    var stageLabel = string.IsNullOrWhiteSpace(stage.StagePath)
                        ? (stageIndex + 1).ToString()
                        : stage.StagePath;
                    if (stage.AssignedJobWorkerId is null)
                        return $"Select a Job Worker for production stage {stageLabel} under '{item.Name}'.";
                    var validWorker = await db.Ledgers.AsNoTracking().AnyAsync(
                        x => x.CompanyId == companyContext.CompanyId && x.Id == stage.AssignedJobWorkerId.Value && x.IsActive && x.IsJobWorker,
                        cancellationToken);
                    if (!validWorker)
                        return $"Select a valid active Job Worker for production stage {stageLabel} under '{item.Name}'.";
                    if (stage.ExpectedCompletionDate is null)
                        return $"Select an Expected Completion date for production stage {stageLabel} under '{item.Name}'.";
                    if (stage.ExpectedProcessRate is decimal expectedRate && (expectedRate < 0 || expectedRate != decimal.Round(expectedRate, 4)))
                        return $"Expected process rate for stage {stageLabel} must be non-negative with at most four decimal places.";
                    if (stage.ExpectedCompletionDate.Value < model.VoucherDate)
                        return $"Expected Completion for production stage {stageLabel} under '{item.Name}' cannot be earlier than Voucher Date.";
                    if (stage.ProcessId is null)
                        return $"Select a Process for production stage {stageLabel} under '{item.Name}'.";
                    var validProcess = await db.Processes.AsNoTracking().AnyAsync(
                        x => x.CompanyId == companyContext.CompanyId && x.Id == stage.ProcessId.Value && x.IsActive,
                        cancellationToken);
                    if (!validProcess)
                        return $"Select a valid active Process for production stage {stageLabel} under '{item.Name}'.";
                    if (stage.OutputGodownId is null)
                        return $"Select an Output Godown for production stage {stageLabel} under '{item.Name}'.";
                    var validStageGodown = await db.Godowns.AsNoTracking().AnyAsync(
                        x => x.CompanyId == companyContext.CompanyId && x.Id == stage.OutputGodownId.Value && x.IsActive,
                        cancellationToken);
                    if (!validStageGodown)
                        return $"Select a valid active Output Godown for production stage {stageLabel} under '{item.Name}'.";
                }
            }
        }

        return null;
    }

    private async Task AddFinishedGoodsAsync(TexTrackDbContext db, Voucher entity, JobWorkOrderEditModel model, CancellationToken cancellationToken)
    {
        var selectedBomIds = model.FinishedGoods.Where(x => x.BillOfMaterialId.HasValue)
            .Select(x => x.BillOfMaterialId!.Value).Distinct().ToList();
        var rootRevisionIds = selectedBomIds.Count == 0
            ? new Dictionary<long, long>()
            : await db.BillOfMaterials.AsNoTracking()
                .Where(x => x.CompanyId == companyContext.CompanyId && selectedBomIds.Contains(x.Id) && x.CurrentRevisionId.HasValue)
                .ToDictionaryAsync(x => x.Id, x => x.CurrentRevisionId!.Value, cancellationToken);
        var bomRevisionGraph = rootRevisionIds.Count == 0
            ? new Dictionary<long, BillOfMaterialRevision>()
            : await LoadBomRevisionGraphAsync(db, rootRevisionIds.Values, cancellationToken);
        var lineNumber = 1;
        foreach (var fgModel in model.FinishedGoods)
        {
            var positiveAllocations = fgModel.SizeQuantities.Where(x => x.Quantity > 0).ToList();
            var fg = new JobWorkOrderFinishedGood
            {
                VoucherId = entity.Id,
                LineNumber = lineNumber++,
                DesignGroupKey = string.IsNullOrWhiteSpace(fgModel.DesignGroupKey) ? Guid.NewGuid().ToString("N") : fgModel.DesignGroupKey,
                StockItemId = fgModel.StockItemId,
                ColourId = fgModel.ColourId,
                OrderedQuantity = positiveAllocations.Sum(x => x.Quantity),
                FinishedGoodsGodownId = fgModel.FinishedGoodsGodownId,
                DestinationGodownId = fgModel.DestinationGodownId ?? fgModel.FinishedGoodsGodownId,
                XmlRate = fgModel.XmlRate,
                XmlAmount = fgModel.XmlAmount != 0 ? fgModel.XmlAmount : -(positiveAllocations.Sum(x => x.Quantity) * fgModel.XmlRate)
            };
            db.JobWorkOrderFinishedGoods.Add(fg);

            foreach (var allocation in positiveAllocations)
            {
                fg.SizeAllocations.Add(new JobWorkOrderSizeAllocation
                {
                    StockItemVariantId = allocation.StockItemVariantId,
                    MasterJobOrderAllocationId = allocation.MasterJobOrderAllocationId,
                    SizeId = allocation.SizeId,
                    Quantity = allocation.Quantity
                });
            }

            var componentLine = 1;
            foreach (var component in fgModel.Components.Where(x => string.IsNullOrWhiteSpace(x.BomPath) && (x.StockItemId > 0 || x.RequiredQuantity != 0)))
            {
                fg.Components.Add(new JobWorkOrderComponent
                {
                    LineNumber = componentLine++,
                    StockItemId = component.StockItemId,
                    UqcId = component.UqcId,
                    RequiredQuantity = component.RequiredQuantity,
                    ComponentGodownId = component.ComponentGodownId,
                    XmlRate = component.XmlRate,
                    XmlAmount = component.XmlAmount != 0 ? component.XmlAmount : component.RequiredQuantity * component.XmlRate
                });
            }

            foreach (var p in fgModel.Processes.Where(x => x.ProcessId > 0))
            {
                fg.Processes.Add(new JobWorkOrderProcess
                {
                    ProcessId = p.ProcessId,
                    ExpectedRate = p.ExpectedRate,
                    RateBasis = string.IsNullOrWhiteSpace(p.RateBasis) ? "Per Quantity" : p.RateBasis
                });
            }


            if (fgModel.BillOfMaterialId is long rootBomId &&
                rootRevisionIds.TryGetValue(rootBomId, out var rootRevisionId) &&
                bomRevisionGraph.TryGetValue(rootRevisionId, out var rootRevision))
            {
                var stageNumber = 0;
                var componentNumber = 0;
                var stageAssignments = fgModel.BomStages
                    .Where(x => !string.IsNullOrWhiteSpace(x.StagePath))
                    .GroupBy(x => x.StagePath)
                    .ToDictionary(x => x.Key, x => x.First());
                AddBomStageSnapshot(
                    fg,
                    rootRevision,
                    bomRevisionGraph,
                    positiveAllocations.Sum(x => x.Quantity),
                    ++stageNumber,
                    0,
                    "1",
                    null,
                    null,
                    null,
                    null,
                    fgModel.FinishedGoodsGodownId,
                    true,
                    model.DueDate,
                    stageAssignments,
                    companyContext.Actor,
                    ref stageNumber,
                    ref componentNumber);
            }
            else if (fgModel.BillOfMaterialId is null && fgModel.BomStages.Count > 0)
            {
                // Manual allocation: no BOM template to snapshot from, so each stage is built
                // directly from what the user filled in - same entities
                // (JobWorkOrderBomStage + JobWorkOrderStageAssignment) a BOM-driven stage would
                // produce, just with SourceBomId left null. This is what makes manual JWOs show
                // up in Rate Variance / Job Worker Control identically to BOM-driven ones.
                var stageNumber = 0;
                foreach (var stageModel in fgModel.BomStages.OrderBy(x => x.StageNumber))
                {
                    stageNumber++;
                    var stage = new JobWorkOrderBomStage
                    {
                        VoucherId = fg.VoucherId,
                        FinishedGood = fg,
                        SourceBomId = null,
                        SourceBomRevisionId = null,
                        SourceBomVersion = null,
                        StableKey = Guid.NewGuid(),
                        StageNumber = stageNumber,
                        StageLevel = 0,
                        StagePath = stageNumber.ToString(),
                        StageName = fgModel.StockItemText,
                        OutputStockItemId = fgModel.StockItemId,
                        OutputUqcId = stageModel.OutputUqcId,
                        OutputQuantity = positiveAllocations.Sum(x => x.Quantity),
                        ProcessId = stageModel.ProcessId,
                        AssignedJobWorkerId = stageModel.AssignedJobWorkerId,
                        OutputGodownId = stageModel.OutputGodownId ?? fgModel.FinishedGoodsGodownId,
                        IsFinalStage = true
                    };
                    stage.AssignmentHistory.Add(new JobWorkOrderStageAssignment
                    {
                        AssignmentVersion = 1,
                        JobWorkerId = stage.AssignedJobWorkerId,
                        ExpectedCompletionDate = stageModel.ExpectedCompletionDate ?? model.DueDate,
                        ExpectedProcessRate = stageModel.ExpectedProcessRate,
                        Status = "Active",
                        Reason = "Initial stage assignment",
                        ValidFromUtc = DateTimeOffset.UtcNow,
                        CreatedBy = companyContext.Actor
                    });
                    fg.BomStages.Add(stage);
                }
            }
        }
    }

    private static JobWorkOrderBomStage AddBomStageSnapshot(
        JobWorkOrderFinishedGood finishedGood,
        BillOfMaterialRevision revision,
        IReadOnlyDictionary<long, BillOfMaterialRevision> graph,
        decimal outputQuantity,
        int stageNumber,
        int level,
        string path,
        JobWorkOrderBomStage? parentStage,
        JobWorkOrderComponent? parentComponent,
        long? processId,
        long? jobWorkerId,
        long? outputGodownId,
        bool isFinal,
        DateOnly? defaultExpectedCompletionDate,
        IReadOnlyDictionary<string, JobWorkBomStageEditModel> stageAssignments,
        string actor,
        ref int sequence,
        ref int componentSequence)
    {
        stageAssignments.TryGetValue(path, out var assignment);
        var stage = new JobWorkOrderBomStage
        {
            VoucherId = finishedGood.VoucherId,
            FinishedGood = finishedGood,
            ParentStage = parentStage,
            SourceBomId = revision.BomId,
            SourceBomRevisionId = revision.Id,
            SourceBomVersion = revision.RevisionNumber,
            StableKey = Guid.NewGuid(),
            StageNumber = stageNumber,
            StageLevel = level,
            StagePath = path,
            StageName = revision.Name,
            OutputStockItemId = revision.StockItemId,
            OutputUqcId = revision.StockItem.UqcId,
            OutputQuantity = outputQuantity,
            ProcessId = assignment?.ProcessId ?? processId,
            AssignedJobWorkerId = assignment?.AssignedJobWorkerId ?? jobWorkerId,
            OutputGodownId = assignment?.OutputGodownId ?? outputGodownId,
            IsFinalStage = isFinal
        };
        stage.AssignmentHistory.Add(new JobWorkOrderStageAssignment
        {
            AssignmentVersion = 1,
            JobWorkerId = stage.AssignedJobWorkerId,
            ExpectedCompletionDate = assignment?.ExpectedCompletionDate ?? defaultExpectedCompletionDate,
            ExpectedProcessRate = assignment?.ExpectedProcessRate,
            Status = "Active",
            Reason = "Initial stage assignment",
            ValidFromUtc = DateTimeOffset.UtcNow,
            CreatedBy = actor
        });
        finishedGood.BomStages.Add(stage);
        var stageGodownId = assignment?.OutputGodownId ?? outputGodownId;
        var factor = revision.OutputQuantity == 0 ? 0 : outputQuantity / revision.OutputQuantity;
        foreach (var source in revision.Lines.OrderBy(x => x.LineNumber))
        {
            var component = new JobWorkOrderComponent
            {
                FinishedGood = finishedGood,
                BomStage = stage,
                ParentComponent = parentComponent,
                LineNumber = ++componentSequence,
                StockItemId = source.ComponentStockItemId,
                ComponentVariantId = source.ComponentVariantId,
                UqcId = source.UqcId,
                RequiredQuantity = decimal.Round(source.RequiredQuantity * factor, 4, MidpointRounding.AwayFromZero),
                ComponentGodownId = stageGodownId,
                BomLevel = level,
                BomPath = $"{path}.{source.LineNumber}",
                IsProducedComponent = source.ChildBomId.HasValue
            };
            finishedGood.Components.Add(component);
            stage.Components.Add(component);
            if (source.ChildBomRevisionId is long childRevisionId && graph.TryGetValue(childRevisionId, out var childRevision))
            {
                sequence++;
                var childStage = AddBomStageSnapshot(
                    finishedGood,
                    childRevision,
                    graph,
                    component.RequiredQuantity,
                    sequence,
                    level + 1,
                    $"{path}.{source.LineNumber}",
                    stage,
                    component,
                    source.ProcessId,
                    jobWorkerId,
                    stageGodownId,
                    false,
                    defaultExpectedCompletionDate,
                    stageAssignments,
                    actor,
                    ref sequence,
                    ref componentSequence);
                component.ChildBomStage = childStage;
            }
        }
        return stage;
    }

    private async Task<Dictionary<long, BillOfMaterialRevision>> LoadBomRevisionGraphAsync(
        TexTrackDbContext db,
        IEnumerable<long> roots,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, BillOfMaterialRevision>();
        var pending = new Queue<long>(roots.Distinct());
        while (pending.Count > 0)
        {
            var batch = pending.Where(x => !result.ContainsKey(x)).Distinct().ToList();
            pending.Clear();
            if (batch.Count == 0) break;
            var revisions = await db.BillOfMaterialRevisions.AsNoTracking()
                .Where(x => x.Bom.CompanyId == companyContext.CompanyId && batch.Contains(x.Id))
                .Include(x => x.StockItem).ThenInclude(x => x.Uqc)
                .Include(x => x.Lines)
                .ToListAsync(cancellationToken);
            foreach (var revision in revisions)
            {
                result[revision.Id] = revision;
                foreach (var childRevisionId in revision.Lines
                             .Where(x => x.ChildBomRevisionId.HasValue)
                             .Select(x => x.ChildBomRevisionId!.Value))
                    if (!result.ContainsKey(childRevisionId)) pending.Enqueue(childRevisionId);
            }
        }
        return result;
    }

    private async Task<Dictionary<long, BillOfMaterial>> LoadBomGraphAsync(TexTrackDbContext db, IReadOnlyCollection<long> roots, CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, BillOfMaterial>();
        var pending = new Queue<long>(roots);
        while (pending.Count > 0)
        {
            var batch = pending.Where(x => !result.ContainsKey(x)).Distinct().ToList();
            pending.Clear();
            if (batch.Count == 0) break;
            var boms = await db.BillOfMaterials.AsNoTracking()
                .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive && batch.Contains(x.Id))
                .Include(x => x.StockItem).ThenInclude(x => x.Uqc)
                .Include(x => x.Lines)
                .ToListAsync(cancellationToken);
            foreach (var bom in boms)
            {
                result[bom.Id] = bom;
                foreach (var child in bom.Lines.Where(x => x.ChildBomId.HasValue).Select(x => x.ChildBomId!.Value))
                    if (!result.ContainsKey(child)) pending.Enqueue(child);
            }
        }
        return result;
    }

    // Maps a finished good's real BomStage rows for editing, then - for a legacy manually-
    // allocated JWO saved before the manual-process/BOM-stage unification - upgrades its old
    // JobWorkOrderProcess rows into synthetic, editable stages so nothing is silently missing
    // from the new unified UI. This is purely a display-time upgrade: no data is written here,
    // and the legacy JobWorkOrderProcess rows are left untouched in the database until the
    // voucher is saved again, at which point they're naturally superseded (AddFinishedGoodsAsync
    // only recreates JobWorkOrderProcess rows from JobWorkFinishedGoodEditModel.Processes, which
    // the current UI never populates any more).
    private static List<JobWorkBomStageEditModel> BuildStageEditModelsForLoad(JobWorkOrderFinishedGood x, DateOnly? dueDate)
    {
        var stages = x.BomStages.OrderBy(s => s.StageNumber).Select(s => new JobWorkBomStageEditModel
        {
            Id = s.Id,
            SourceBomId = s.SourceBomId,
            SourceBomVersion = s.SourceBomVersion,
            StageNumber = s.StageNumber,
            StageLevel = s.StageLevel,
            StagePath = s.StagePath,
            StageName = s.StageName,
            OutputStockItemId = s.OutputStockItemId,
            OutputStockItemText = s.OutputStockItem.Name,
            OutputVariantId = s.OutputVariantId,
            OutputUqcId = s.OutputUqcId,
            OutputUqcShortName = s.OutputUqc.ShortName,
            OutputQuantity = s.OutputQuantity,
            ProcessId = s.ProcessId,
            AssignedJobWorkerId = s.AssignedJobWorkerId,
            AssignedJobWorkerText = s.AssignedJobWorker == null ? string.Empty : s.AssignedJobWorker.Name,
            ExpectedCompletionDate = s.AssignmentHistory
                .Where(a => a.Status == "Active")
                .Select(a => a.ExpectedCompletionDate)
                .SingleOrDefault() ?? dueDate,
            ExpectedProcessRate = s.AssignmentHistory.Where(a => a.Status == "Active").Select(a => a.ExpectedProcessRate).SingleOrDefault(),
            OutputGodownId = s.OutputGodownId,
            OutputGodownText = s.OutputGodown == null ? string.Empty : s.OutputGodown.Name,
            IsFinalStage = s.IsFinalStage
        }).ToList();

        if (stages.Count == 0 && x.Processes.Count > 0)
        {
            var stageNumber = 0;
            foreach (var p in x.Processes.OrderBy(p => p.Id))
            {
                stageNumber++;
                stages.Add(new JobWorkBomStageEditModel
                {
                    StageNumber = stageNumber,
                    StageLevel = 0,
                    StagePath = stageNumber.ToString(),
                    StageName = x.StockItem.Name,
                    OutputStockItemId = x.StockItemId,
                    OutputStockItemText = x.StockItem.Name,
                    OutputUqcId = x.StockItem.UqcId,
                    OutputUqcShortName = x.StockItem.Uqc.ShortName,
                    OutputQuantity = x.OrderedQuantity,
                    ProcessId = p.ProcessId,
                    ProcessText = p.Process.Name,
                    IsFinalStage = true,
                    ExpectedCompletionDate = dueDate
                });
            }
        }

        return stages;
    }

    private static void AddBomStagePreview(
        BillOfMaterial bom,
        IReadOnlyDictionary<long, BillOfMaterial> graph,
        decimal outputQuantity,
        int level,
        string path,
        string? parentClientKey,
        long? inheritedProcessId,
        long? defaultJobWorkerId,
        long? defaultGodownId,
        bool isFinal,
        IReadOnlyDictionary<long, string> workers,
        IReadOnlyDictionary<long, string> godowns,
        IReadOnlyDictionary<long, string> processes,
        List<JobWorkBomStageEditModel> result,
        ref int sequence)
    {
        var processId = inheritedProcessId ?? bom.Lines.OrderBy(x => x.LineNumber)
            .Select(x => x.ProcessId).FirstOrDefault(x => x.HasValue);
        var stage = new JobWorkBomStageEditModel
        {
            SourceBomId = bom.Id,
            SourceBomVersion = bom.VersionNumber,
            StageNumber = ++sequence,
            StageLevel = level,
            StagePath = path,
            StageName = bom.Name,
            ParentStageClientKey = parentClientKey,
            OutputStockItemId = bom.StockItemId,
            OutputStockItemText = bom.StockItem.Name,
            OutputUqcId = bom.StockItem.UqcId,
            OutputUqcShortName = bom.StockItem.Uqc.ShortName,
            OutputQuantity = outputQuantity,
            ProcessId = processId,
            ProcessText = processId.HasValue && processes.TryGetValue(processId.Value, out var processName) ? processName : string.Empty,
            AssignedJobWorkerId = defaultJobWorkerId,
            AssignedJobWorkerText = defaultJobWorkerId.HasValue && workers.TryGetValue(defaultJobWorkerId.Value, out var workerName) ? workerName : string.Empty,
            OutputGodownId = defaultGodownId,
            OutputGodownText = defaultGodownId.HasValue && godowns.TryGetValue(defaultGodownId.Value, out var godownName) ? godownName : string.Empty,
            IsFinalStage = isFinal
        };
        result.Add(stage);

        var factor = bom.OutputQuantity == 0 ? 0 : outputQuantity / bom.OutputQuantity;
        foreach (var line in bom.Lines.OrderBy(x => x.LineNumber))
        {
            if (line.ChildBomId is not long childId || !graph.TryGetValue(childId, out var child)) continue;
            AddBomStagePreview(child, graph,
                decimal.Round(line.RequiredQuantity * factor, 4, MidpointRounding.AwayFromZero),
                level + 1, $"{path}.{line.LineNumber}", stage.ClientKey, line.ProcessId,
                defaultJobWorkerId, defaultGodownId, false, workers, godowns, processes, result, ref sequence);
        }
    }

    private async Task<List<JobWorkVoucherTypeLookup>> BuildVoucherTypeLookupsAsync(
        TexTrackDbContext db,
        IReadOnlyList<VoucherType> voucherTypes,
        CancellationToken cancellationToken)
    {
        var result = new List<JobWorkVoucherTypeLookup>();
        foreach (var type in voucherTypes)
        {
            var next = await GetNextSequenceAsync(db, type.Id, cancellationToken);
            result.Add(new JobWorkVoucherTypeLookup
            {
                Id = type.Id,
                Name = type.Name,
                TallyVoucherTypeName = type.TallyVoucherTypeName,
                AllowManualNumbering = type.NumberingMode == "Manual",
                NumberingMode = type.NumberingMode,
                Prefix = type.Prefix,
                Suffix = type.Suffix,
                NumberWidth = type.NumberWidth,
                Abbreviation = type.Abbreviation,
                StartingNumber = type.StartingNumber,
                NextSequence = next,
                NextVoucherNumber = FormatVoucherNumber(type, next),
                IsSystem = type.IsSystem
            });
        }
        return result;
    }

    private static JobWorkStockItemLookup MapStockItemLookup(StockItem x)
    {
        var activeColourIds = x.Colours.Where(c => c.Colour.IsActive).Select(c => c.ColourId).ToHashSet();
        var activeSizeIds = x.Sizes.Where(s => s.Size.IsActive).Select(s => s.SizeId).ToHashSet();

        return new JobWorkStockItemLookup
        {
            Id = x.Id,
            Name = x.Name,
            UqcId = x.UqcId,
            UqcShortName = x.Uqc.ShortName,
            RootClassification = x.StockGroup.RootClassification,
            Colours = x.Colours
                .Where(c => c.Colour.IsActive)
                .Select(c => new JobWorkColourLookup { Id = c.ColourId, Name = c.Colour.Name })
                .OrderBy(c => c.Name)
                .ToList(),
            Sizes = x.Sizes
                .Where(s => s.Size.IsActive)
                .Select(s => new JobWorkSizeLookup { Id = s.SizeId, Name = s.Size.Name, DisplayOrder = s.Size.DisplayOrder })
                .OrderBy(s => s.DisplayOrder)
                .ThenBy(s => s.Name)
                .ToList(),
            Variants = x.Variants
                .Where(v => v.IsActive &&
                    (v.ColourId == null || activeColourIds.Contains(v.ColourId.Value)) &&
                    (v.SizeId == null || activeSizeIds.Contains(v.SizeId.Value)))
                .Select(v => new JobWorkVariantLookup { Id = v.Id, ColourId = v.ColourId, SizeId = v.SizeId })
                .ToList()
        };
    }

    private static void NormalizeModel(JobWorkOrderEditModel model)
    {
        model.VoucherNumber = model.VoucherNumber.Trim();
        model.Batch = model.Batch.Trim();
        model.ReferenceNumber = model.ReferenceNumber.Trim();
        model.Narration = model.Narration.Trim();
        model.FinishedGoods = model.FinishedGoods.Where(x => x.StockItemId > 0 || x.SizeQuantities.Any(q => q.Quantity != 0) || x.Components.Count > 0).ToList();
        foreach (var group in model.FinishedGoods.GroupBy(x => string.IsNullOrWhiteSpace(x.DesignGroupKey) ? x.ClientKey : x.DesignGroupKey))
        {
            var owner = group.First();
            owner.DesignGroupKey = string.IsNullOrWhiteSpace(owner.DesignGroupKey) ? owner.ClientKey : owner.DesignGroupKey;
            foreach (var colourLine in group.Skip(1))
            {
                colourLine.DesignGroupKey = owner.DesignGroupKey;
                colourLine.StockItemId = owner.StockItemId;
                colourLine.StockItemText = owner.StockItemText;
                colourLine.UqcShortName = owner.UqcShortName;
                colourLine.FinishedGoodsGodownId = owner.FinishedGoodsGodownId;
                colourLine.FinishedGoodsGodownText = owner.FinishedGoodsGodownText;
                colourLine.DestinationGodownId = owner.DestinationGodownId;
                colourLine.DestinationGodownText = owner.DestinationGodownText;
                colourLine.Components.Clear();
                colourLine.Processes.Clear();
            }
        }
        foreach (var fg in model.FinishedGoods)
        {
            fg.Components = fg.Components
                .Where(x => x.StockItemId > 0 || x.RequiredQuantity != 0)
                .ToList();

            fg.Processes = fg.Processes.Where(x => x.ProcessId > 0).ToList();
        }
    }

    private static DateOnly? CalculateOverallExpectedCompletion(JobWorkOrderEditModel model)
    {
        var finalDates = model.FinishedGoods
            .SelectMany(x => x.BomStages)
            .Where(x => x.IsFinalStage && x.ExpectedCompletionDate.HasValue)
            .Select(x => x.ExpectedCompletionDate!.Value)
            .ToList();
        return finalDates.Count == 0 ? null : finalDates.Max();
    }

    private async Task<int> GetNextSequenceAsync(
        TexTrackDbContext db,
        long voucherTypeId,
        CancellationToken cancellationToken)
    {
        var startingNumber = await db.VoucherTypes
            .Where(x => x.Id == voucherTypeId)
            .Select(x => x.StartingNumber)
            .SingleAsync(cancellationToken);
        var max = await db.Vouchers
            .Where(x => x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId && x.VoucherTypeId == voucherTypeId)
            .Select(x => (int?)x.SequenceNumber)
            .MaxAsync(cancellationToken);
        return Math.Max(startingNumber, (max ?? startingNumber - 1) + 1);
    }

    private static string FormatVoucherNumber(VoucherType type, int sequence)
    {
        if (type.NumberingMode == "Manual") return string.Empty;
        var number = type.NumberWidth > 0
            ? sequence.ToString($"D{type.NumberWidth}")
            : sequence.ToString();
        return type.NumberingMode == "AutoPrefixSuffix"
            ? $"{type.Prefix}{number}{type.Suffix}"
            : number;
    }

    private async Task<OperationResult> FailAndRollbackAsync(
        TexTrackDbContext db,
        IDbContextTransaction transaction,
        long entityId,
        string message,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        await using var auditDb = await contextFactory.CreateDbContextAsync(cancellationToken);
        auditDb.AuditLogs.Add(NewAudit(entityId == 0 ? null : entityId, entityId == 0 ? "CreateBlocked" : "UpdateBlocked", false, message));
        await auditDb.SaveChangesAsync(cancellationToken);
        return OperationResult.Fail(message);
    }

    private AuditLog NewAudit(long? entityId, string action, bool success, string description) => new()
    {
        CompanyId = companyContext.CompanyId,
        EntityType = "JobWorkOutOrder",
        EntityId = entityId,
        Action = action,
        Success = success,
        Description = description,
        PerformedBy = companyContext.Actor,
        PerformedAtUtc = DateTimeOffset.UtcNow
    };

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
}
