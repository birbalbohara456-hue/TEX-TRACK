using System.Data;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed class MaterialInRepository(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext,
    VoucherLifecycleService lifecycle,
    StockPostingService stockPosting,
    VoucherSequenceAllocator? sequenceAllocator = null,
    DeveloperAccessService? developerAccess = null,
    VoucherAuditHistoryService? voucherAuditHistory = null,
    InventoryPeriodControlService? inventoryPeriodControl = null)
{
    private const string MaterialInTypeCode = "MATERIAL_IN";
    private const string JwoTypeCode = "JOB_WORK_OUT_ORDER";
    private readonly VoucherAuditHistoryService fullAudit = voucherAuditHistory ?? new(contextFactory);
    private readonly InventoryPeriodControlService periodControl = inventoryPeriodControl ?? new();

    public async Task<MaterialInVoucherDefaults> GetEntryDefaultsAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var type = await db.VoucherTypes.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive &&
                (x.SystemTypeCode == MaterialInTypeCode ||
                 (x.ParentVoucherType != null && x.ParentVoucherType.SystemTypeCode == MaterialInTypeCode)))
            .OrderByDescending(x => !x.IsSystem).ThenBy(x => x.Name)
            .FirstAsync(cancellationToken);
        var max = await db.Vouchers.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId && x.VoucherTypeId == type.Id)
            .Select(x => (int?)x.SequenceNumber).MaxAsync(cancellationToken);
        var next = Math.Max(type.StartingNumber, (max ?? type.StartingNumber - 1) + 1);
        return new MaterialInVoucherDefaults
        {
            VoucherTypeId = type.Id,
            VoucherTypeName = type.Name,
            VoucherDate = DateOnly.FromDateTime(DateTime.Today),
            NumberingMode = type.NumberingMode,
            VoucherNumber = type.NumberingMode == "Manual" ? string.Empty : FormatAutomaticNumber(type, next)
        };
    }

    public async Task<MaterialInLookupData> GetLookupDataAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return new MaterialInLookupData
        {
            JobWorkers = await db.Ledgers.AsNoTracking()
                .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive && x.IsJobWorker)
                .OrderBy(x => x.Name)
                .Select(x => new JobWorkerLookupItem
                {
                    Id = x.Id,
                    Name = x.Name,
                    GroupName = x.LedgerGroup.Name,
                    DefaultMaterialOutDestinationGodownId = x.DefaultMaterialOutDestinationGodownId,
                    DefaultMaterialOutDestinationGodownName = x.DefaultMaterialOutDestinationGodown == null ? string.Empty : x.DefaultMaterialOutDestinationGodown.Name,
                    DefaultMaterialInConsumptionGodownId = x.DefaultMaterialInConsumptionGodownId,
                    DefaultMaterialInConsumptionGodownName = x.DefaultMaterialInConsumptionGodown == null ? string.Empty : x.DefaultMaterialInConsumptionGodown.Name
                }).ToListAsync(cancellationToken),
            Godowns = await db.Godowns.AsNoTracking()
                .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new JobWorkGodownLookup { Id = x.Id, Name = x.Name })
                .ToListAsync(cancellationToken)
        };
    }

    public async Task<IReadOnlyList<MaterialInPendingOrder>> GetPendingOrdersAsync(
        long jobWorkerLedgerId,
        long? excludeMaterialInVoucherId = null,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var orders = await db.Vouchers.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId &&
                        (x.JobWorkFinishedGoods.Any(f => f.BomStages.Any(s =>
                             s.AssignedJobWorkerId == jobWorkerLedgerId ||
                             s.AssignmentHistory.Any(a => a.JobWorkerId == jobWorkerLedgerId))) ||
                         (x.PartyLedgerId == jobWorkerLedgerId &&
                          !x.JobWorkFinishedGoods.Any(f => f.BomStages.Any()))) &&
                        x.Status != "Cancelled" &&
                        (x.VoucherType.SystemTypeCode == JwoTypeCode ||
                         (x.VoucherType.ParentVoucherType != null && x.VoucherType.ParentVoucherType.SystemTypeCode == JwoTypeCode)))
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.StockItem).ThenInclude(x => x.Uqc)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.FinishedGoodsGodown)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.SizeAllocations).ThenInclude(x => x.StockItemVariant).ThenInclude(x => x.Colour)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.SizeAllocations).ThenInclude(x => x.StockItemVariant).ThenInclude(x => x.Size)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.Components).ThenInclude(x => x.StockItem)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.Components).ThenInclude(x => x.Uqc)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.BomStages).ThenInclude(x => x.OutputStockItem).ThenInclude(x => x.Uqc)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.BomStages).ThenInclude(x => x.AssignmentHistory)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.BomStages).ThenInclude(x => x.OutputGodown)
            .AsSplitQuery().OrderBy(x => x.VoucherDate).ThenBy(x => x.SequenceNumber).ToListAsync(cancellationToken);

        var orderIds = orders.Select(x => x.Id).ToList();
        var stageIds = orders.SelectMany(x => x.JobWorkFinishedGoods).SelectMany(x => x.BomStages).Select(x => x.Id).ToList();
        var receivedByAssignment = stageIds.Count == 0 ? new Dictionary<(long StageId, long AssignmentId), decimal>() : await db.MaterialInFinishedGoods.AsNoTracking()
            .Where(x => x.BomStageId != null && x.StageAssignmentId != null && stageIds.Contains(x.BomStageId.Value) && x.Voucher.Status != "Cancelled" &&
                        (excludeMaterialInVoucherId == null || x.VoucherId != excludeMaterialInVoucherId.Value))
            .GroupBy(x => new { StageId = x.BomStageId!.Value, AssignmentId = x.StageAssignmentId!.Value })
            .Select(x => new { x.Key.StageId, x.Key.AssignmentId, Qty = x.Sum(y => y.ReceivedQuantity) })
            .ToDictionaryAsync(x => (x.StageId, x.AssignmentId), x => x.Qty, cancellationToken);
        var receivedByVariant = stageIds.Count == 0 ? new Dictionary<(long StageId, long AssignmentId, long VariantId), decimal>() : await db.MaterialInFinishedGoodAllocations.AsNoTracking()
            .Where(x => x.FinishedGoodLine.BomStageId != null && x.FinishedGoodLine.StageAssignmentId != null && stageIds.Contains(x.FinishedGoodLine.BomStageId.Value) && x.FinishedGoodLine.Voucher.Status != "Cancelled" &&
                        (excludeMaterialInVoucherId == null || x.FinishedGoodLine.VoucherId != excludeMaterialInVoucherId.Value))
            .GroupBy(x => new { StageId = x.FinishedGoodLine.BomStageId!.Value, AssignmentId = x.FinishedGoodLine.StageAssignmentId!.Value, VariantId = x.StockItemVariantId })
            .Select(x => new { x.Key.StageId, x.Key.AssignmentId, x.Key.VariantId, Qty = x.Sum(y => y.Quantity) })
            .ToDictionaryAsync(x => (x.StageId, x.AssignmentId, x.VariantId), x => x.Qty, cancellationToken);
        var issued = orderIds.Count == 0 ? new Dictionary<(long ComponentId, long AssignmentId), decimal>() : await db.MaterialOutLines.AsNoTracking()
            .Where(x => orderIds.Contains(x.JwoVoucherId) && x.Voucher.PartyLedgerId == jobWorkerLedgerId && x.Voucher.Status != "Cancelled")
            .GroupBy(x => new { ComponentId = x.JwoComponentId, AssignmentId = x.StageAssignmentId ?? 0 })
            .Select(x => new { x.Key.ComponentId, x.Key.AssignmentId, Qty = x.Sum(y => y.IssuedQuantity) })
            .ToDictionaryAsync(x => (x.ComponentId, x.AssignmentId), x => x.Qty, cancellationToken);
        var issuedDestinationLots = await db.MaterialOutLines.AsNoTracking()
            .Where(x => orderIds.Contains(x.JwoVoucherId) && x.Voucher.PartyLedgerId == jobWorkerLedgerId && x.Voucher.Status != "Cancelled")
            .Select(x => new
            {
                x.JwoVoucherId,
                AssignmentId = x.StageAssignmentId ?? 0,
                x.DestinationGodownId,
                DestinationGodownName = x.DestinationGodown.Name,
                x.IssuedQuantity,
                ConsumedQuantity = db.MaterialInMaterialOutAllocations
                    .Where(a => a.MaterialOutLineId == x.Id && a.ConsumptionLine.Voucher.Status != "Cancelled" &&
                        (excludeMaterialInVoucherId == null || a.ConsumptionLine.VoucherId != excludeMaterialInVoucherId.Value))
                    .Sum(a => (decimal?)a.AllocatedQuantity) ?? 0
            })
            .ToListAsync(cancellationToken);
        var issuedDestinations = issuedDestinationLots.Where(x => x.IssuedQuantity > x.ConsumedQuantity).ToList();
        var consumed = orderIds.Count == 0 ? new Dictionary<(long ComponentId, long AssignmentId), decimal>() : await db.MaterialInConsumptions.AsNoTracking()
            .Where(x => orderIds.Contains(x.JwoComponent.FinishedGood.VoucherId) && x.Voucher.PartyLedgerId == jobWorkerLedgerId && x.Voucher.Status != "Cancelled" &&
                         (excludeMaterialInVoucherId == null || x.VoucherId != excludeMaterialInVoucherId.Value))
            .GroupBy(x => new { ComponentId = x.JwoComponentId, AssignmentId = x.StageAssignmentId ?? 0 })
            .Select(x => new { x.Key.ComponentId, x.Key.AssignmentId, Qty = x.Sum(y => y.ConsumedQuantity) })
            .ToDictionaryAsync(x => (x.ComponentId, x.AssignmentId), x => x.Qty, cancellationToken);

        var result = new List<MaterialInPendingOrder>();
        var workerName = await db.Ledgers.AsNoTracking().Where(x => x.Id == jobWorkerLedgerId).Select(x => x.Name).SingleAsync(cancellationToken);
        foreach (var order in orders)
        {
            var mapped = new MaterialInPendingOrder { JwoVoucherId = order.Id, VoucherNumber = order.VoucherNumber, VoucherDate = order.VoucherDate, Batch = order.Batch, JobWorkerLedgerId = jobWorkerLedgerId, JobWorkerName = workerName };
            foreach (var fg in order.JobWorkFinishedGoods.OrderBy(x => x.LineNumber))
            {
                if (fg.BomStages.Count == 0 && order.PartyLedgerId == jobWorkerLedgerId)
                {
                    var manualComponents = fg.Components.Where(x => x.BomStageId == null).ToList();
                    var supportedOutput = manualComponents.Count == 0 ? 0 : manualComponents
                        .Where(x => x.RequiredQuantity > 0)
                        .Select(x => issued.GetValueOrDefault((x.Id, 0)) / x.RequiredQuantity * fg.OrderedQuantity)
                        .DefaultIfEmpty(0m).Min();
                    supportedOutput = Math.Min(fg.OrderedQuantity, supportedOutput);
                    var prior = await db.MaterialInFinishedGoods.AsNoTracking()
                        .Where(x => x.JwoFinishedGoodId == fg.Id && x.BomStageId == null && x.Voucher.Status != "Cancelled" &&
                                    (excludeMaterialInVoucherId == null || x.VoucherId != excludeMaterialInVoucherId.Value))
                        .SumAsync(x => (decimal?)x.ReceivedQuantity, cancellationToken) ?? 0;
                    if (supportedOutput > prior)
                    {
                        var pending = new MaterialInPendingFinishedGood
                        {
                            JwoFinishedGoodId = fg.Id,
                            BomStageId = 0,
                            StageAssignmentId = 0,
                            AssignmentVersion = 0,
                            StageName = "Manual allocation",
                            IsFinalStage = true,
                            OutputGodownId = fg.FinishedGoodsGodownId ?? 0,
                            OutputGodownName = fg.FinishedGoodsGodown?.Name ?? string.Empty,
                            StockItemId = fg.StockItemId,
                            StockItemName = fg.StockItem.Name,
                            UqcId = fg.StockItem.UqcId,
                            UqcShortName = fg.StockItem.Uqc.ShortName,
                            DecimalPlaces = fg.StockItem.Uqc.DecimalPlaces,
                            OrderedQuantity = supportedOutput,
                            ReceivedQuantity = prior
                        };
                        foreach (var alloc in fg.SizeAllocations.OrderBy(x => x.StockItemVariant.Size == null ? 0 : x.StockItemVariant.Size.DisplayOrder))
                        {
                            var variantPrior = await db.MaterialInFinishedGoodAllocations.AsNoTracking()
                                .Where(x => x.StockItemVariantId == alloc.StockItemVariantId &&
                                            x.FinishedGoodLine.JwoFinishedGoodId == fg.Id &&
                                            x.FinishedGoodLine.BomStageId == null &&
                                            x.FinishedGoodLine.Voucher.Status != "Cancelled" &&
                                            (excludeMaterialInVoucherId == null || x.FinishedGoodLine.VoucherId != excludeMaterialInVoucherId.Value))
                                .SumAsync(x => (decimal?)x.Quantity, cancellationToken) ?? 0;
                            var supportedVariant = fg.OrderedQuantity <= 0 ? 0 : alloc.Quantity * supportedOutput / fg.OrderedQuantity;
                            pending.Variants.Add(new MaterialInPendingVariant
                            {
                                StockItemVariantId = alloc.StockItemVariantId,
                                ColourName = alloc.StockItemVariant.Colour?.Name ?? "N/A",
                                SizeName = alloc.StockItemVariant.Size?.Name ?? "N/A",
                                DisplayOrder = alloc.StockItemVariant.Size?.DisplayOrder ?? 0,
                                OrderedQuantity = supportedVariant,
                                ReceivedQuantity = variantPrior
                            });
                        }
                        mapped.FinishedGoods.Add(pending);
                    }

                    foreach (var component in manualComponents.OrderBy(x => x.LineNumber))
                    {
                        var issuedQty = issued.GetValueOrDefault((component.Id, 0));
                        var consumedQty = consumed.GetValueOrDefault((component.Id, 0));
                        if (issuedQty > consumedQty)
                            mapped.Consumptions.Add(new MaterialInAvailableConsumption
                            {
                                JwoComponentId = component.Id,
                                JwoFinishedGoodId = component.FinishedGoodId,
                                BomStageId = 0,
                                StageAssignmentId = 0,
                                AssignmentVersion = 0,
                                StockItemId = component.StockItemId,
                                StockItemName = component.StockItem.Name,
                                UqcId = component.UqcId,
                                UqcShortName = component.Uqc.ShortName,
                                DecimalPlaces = component.Uqc.DecimalPlaces,
                                RequiredQuantity = component.RequiredQuantity,
                                IssuedQuantity = issuedQty,
                                ConsumedQuantity = consumedQty,
                                SuggestedRate = component.XmlRate
                            });
                    }
                }
                foreach (var stage in fg.BomStages.OrderBy(x => x.StageNumber))
                {
                    var stageComponents = fg.Components.Where(x => x.BomStageId == stage.Id).ToList();
                    foreach (var assignment in stage.AssignmentHistory.Where(x => x.JobWorkerId == jobWorkerLedgerId).OrderBy(x => x.AssignmentVersion))
                    {
                        var supportedOutput = stage.OutputQuantity;
                        if (stageComponents.Count > 0)
                        {
                            supportedOutput = stageComponents.Where(x => x.RequiredQuantity > 0)
                                .Select(x =>
                                {
                                    issued.TryGetValue((x.Id, assignment.Id), out var assignmentIssued);
                                    return assignmentIssued / x.RequiredQuantity * stage.OutputQuantity;
                                })
                                .DefaultIfEmpty(0m).Min();
                            supportedOutput = Math.Min(stage.OutputQuantity, supportedOutput);
                        }

                        receivedByAssignment.TryGetValue((stage.Id, assignment.Id), out var prior);
                        if (supportedOutput - prior <= 0) continue;
                        var pending = new MaterialInPendingFinishedGood
                        {
                            JwoFinishedGoodId = fg.Id, BomStageId = stage.Id, StageAssignmentId = assignment.Id,
                            AssignmentVersion = assignment.AssignmentVersion,
                            StageName = stage.StageName, IsFinalStage = stage.IsFinalStage,
                            OutputGodownId = stage.OutputGodownId ?? 0, OutputGodownName = stage.OutputGodown?.Name ?? string.Empty,
                            StockItemId = stage.OutputStockItemId, StockItemName = stage.OutputStockItem.Name,
                            UqcId = stage.OutputUqcId, UqcShortName = stage.OutputStockItem.Uqc.ShortName,
                            DecimalPlaces = stage.OutputStockItem.Uqc.DecimalPlaces,
                            OrderedQuantity = supportedOutput, ReceivedQuantity = prior
                        };
                        if (stage.IsFinalStage)
                        {
                            foreach (var alloc in fg.SizeAllocations.OrderBy(x => x.StockItemVariant.Size == null ? 0 : x.StockItemVariant.Size.DisplayOrder))
                            {
                                receivedByVariant.TryGetValue((stage.Id, assignment.Id, alloc.StockItemVariantId), out var variantPrior);
                                var supportedVariant = stage.OutputQuantity <= 0 ? 0 : alloc.Quantity * supportedOutput / stage.OutputQuantity;
                                pending.Variants.Add(new MaterialInPendingVariant { StockItemVariantId = alloc.StockItemVariantId, ColourName = alloc.StockItemVariant.Colour == null ? "N/A" : alloc.StockItemVariant.Colour.Name, SizeName = alloc.StockItemVariant.Size == null ? "N/A" : alloc.StockItemVariant.Size.Name, DisplayOrder = alloc.StockItemVariant.Size == null ? 0 : alloc.StockItemVariant.Size.DisplayOrder, OrderedQuantity = supportedVariant, ReceivedQuantity = variantPrior });
                            }
                        }
                        mapped.FinishedGoods.Add(pending);
                    }
                }
            }
            foreach (var c in order.JobWorkFinishedGoods.SelectMany(x => x.Components).OrderBy(x => x.LineNumber))
            {
                if (c.BomStageId is not long stageId || !stageIds.Contains(stageId)) continue;
                var stage = order.JobWorkFinishedGoods.SelectMany(x => x.BomStages).FirstOrDefault(x => x.Id == stageId);
                if (stage is null) continue;
                foreach (var assignment in stage.AssignmentHistory.Where(x => x.JobWorkerId == jobWorkerLedgerId).OrderBy(x => x.AssignmentVersion))
                {
                    issued.TryGetValue((c.Id, assignment.Id), out var issuedQty);
                    consumed.TryGetValue((c.Id, assignment.Id), out var consumedQty);
                    if (issuedQty - consumedQty > 0) mapped.Consumptions.Add(new MaterialInAvailableConsumption { JwoComponentId = c.Id, JwoFinishedGoodId = c.FinishedGoodId, BomStageId = stage.Id, StageAssignmentId = assignment.Id, AssignmentVersion = assignment.AssignmentVersion, StockItemId = c.StockItemId, StockItemName = c.StockItem.Name, UqcId = c.UqcId, UqcShortName = c.Uqc.ShortName, DecimalPlaces = c.Uqc.DecimalPlaces, RequiredQuantity = c.RequiredQuantity, IssuedQuantity = issuedQty, ConsumedQuantity = consumedQty, SuggestedRate = c.XmlRate });
                }
            }
            mapped.FinishedGoods = mapped.FinishedGoods.Where(x => mapped.Consumptions.Any(c => c.BomStageId == x.BomStageId && c.StageAssignmentId == x.StageAssignmentId)).ToList();
            var mappedAssignmentIds = mapped.FinishedGoods.Select(x => x.StageAssignmentId)
                .Concat(mapped.Consumptions.Select(x => x.StageAssignmentId)).ToHashSet();
            var jobberDestinations = issuedDestinations
                .Where(x => x.JwoVoucherId == order.Id && mappedAssignmentIds.Contains(x.AssignmentId))
                .DistinctBy(x => x.DestinationGodownId).ToList();
            if (jobberDestinations.Count == 1)
            {
                mapped.JobberDestinationGodownId = jobberDestinations[0].DestinationGodownId;
                mapped.JobberDestinationGodownName = jobberDestinations[0].DestinationGodownName;
            }
            else if (jobberDestinations.Count > 1) mapped.HasMultipleJobberDestinationGodowns = true;
            if (mapped.FinishedGoods.Count > 0) result.Add(mapped);
        }
        return result;
    }

    public async Task<IReadOnlyList<MaterialInListItem>> GetListAsync(string search = "", CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.MaterialInDetails.AsNoTracking().Where(x =>
            x.Voucher.CompanyId == companyContext.CompanyId &&
            x.Voucher.FinancialYearId == companyContext.FinancialYearId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.Voucher.VoucherNumber, pattern) ||
                                     EF.Functions.ILike(x.DisplayedOrderNumber, pattern) ||
                                     EF.Functions.ILike(x.Voucher.Batch, pattern) ||
                                     (x.Voucher.PartyLedger != null && EF.Functions.ILike(x.Voucher.PartyLedger.Name, pattern)));
        }
        return await query.OrderByDescending(x => x.Voucher.VoucherDate).ThenByDescending(x => x.Voucher.SequenceNumber)
            .Select(x => new MaterialInListItem
            {
                VoucherId = x.VoucherId, VoucherNumber = x.Voucher.VoucherNumber, VoucherDate = x.Voucher.VoucherDate,
                JobWorkerName = x.Voucher.PartyLedger == null ? string.Empty : x.Voucher.PartyLedger.Name,
                JwoVoucherNumber = x.DisplayedOrderNumber, Batch = x.Voucher.Batch,
                ConsumptionGodownName = x.ConsumptionGodown.Name, ReceivingGodownName = x.ReceivingGodown.Name,
                ReceivedQuantity = x.Voucher.MaterialInFinishedGoods.Sum(y => y.ReceivedQuantity),
                ConsumedQuantity = x.Voucher.MaterialInConsumptions.Sum(y => y.ConsumedQuantity),
                ConsumedMaterialValue = x.TotalConsumedMaterialValue,
                ProcessCharge = x.TotalProcessCharge,
                FinishedGoodsValue = x.TotalFinishedGoodsValue,
                Status = x.Voucher.Status
            }).ToListAsync(cancellationToken);
    }

    public async Task<MaterialInViewData?> GetForViewAsync(long voucherId, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var detail = await db.MaterialInDetails.AsNoTracking()
            .Where(x => x.VoucherId == voucherId && x.Voucher.CompanyId == companyContext.CompanyId &&
                        x.Voucher.FinancialYearId == companyContext.FinancialYearId)
            .Include(x => x.Voucher).ThenInclude(x => x.PartyLedger)
            .Include(x => x.ConsumptionGodown).Include(x => x.ReceivingGodown)
            .Include(x => x.Voucher).ThenInclude(x => x.MaterialInFinishedGoods).ThenInclude(x => x.StockItem)
            .Include(x => x.Voucher).ThenInclude(x => x.MaterialInFinishedGoods).ThenInclude(x => x.Uqc)
            .Include(x => x.Voucher).ThenInclude(x => x.MaterialInFinishedGoods).ThenInclude(x => x.Allocations).ThenInclude(x => x.StockItemVariant).ThenInclude(x => x.Colour)
            .Include(x => x.Voucher).ThenInclude(x => x.MaterialInFinishedGoods).ThenInclude(x => x.Allocations).ThenInclude(x => x.StockItemVariant).ThenInclude(x => x.Size)
            .Include(x => x.Voucher).ThenInclude(x => x.MaterialInConsumptions).ThenInclude(x => x.StockItem)
            .Include(x => x.Voucher).ThenInclude(x => x.MaterialInConsumptions).ThenInclude(x => x.Uqc)
            .Include(x => x.Voucher).ThenInclude(x => x.MaterialInConsumptions).ThenInclude(x => x.JwoComponent).ThenInclude(x => x.FinishedGood).ThenInclude(x => x.StockItem)
            .AsSplitQuery().SingleOrDefaultAsync(cancellationToken);
        if (detail is null) return null;
        var result = new MaterialInViewData
        {
            Header = new MaterialInListItem
            {
                VoucherId = detail.VoucherId, VoucherNumber = detail.Voucher.VoucherNumber, VoucherDate = detail.Voucher.VoucherDate,
                JobWorkerName = detail.Voucher.PartyLedger?.Name ?? string.Empty, JwoVoucherNumber = detail.DisplayedOrderNumber,
                Batch = detail.Voucher.Batch, ConsumptionGodownName = detail.ConsumptionGodown.Name,
                ReceivingGodownName = detail.ReceivingGodown.Name, Status = detail.Voucher.Status,
                ReceivedQuantity = detail.Voucher.MaterialInFinishedGoods.Sum(x => x.ReceivedQuantity),
                ConsumedQuantity = detail.Voucher.MaterialInConsumptions.Sum(x => x.ConsumedQuantity),
                ConsumedMaterialValue = detail.TotalConsumedMaterialValue,
                ProcessCharge = detail.TotalProcessCharge,
                FinishedGoodsValue = detail.TotalFinishedGoodsValue
            },
            ReferenceNumber = detail.Voucher.ReferenceNumber, Narration = detail.Voucher.Narration
        };
        result.FinishedGoods = detail.Voucher.MaterialInFinishedGoods.OrderBy(x => x.LineNumber).Select(x => new MaterialInViewFinishedGood
        {
            StockItemName = x.StockItem.Name, UqcName = x.Uqc.ShortName, ReceivedQuantity = x.ReceivedQuantity,
            MaterialValue = x.MaterialValue, ProcessCharge = x.ProcessCharge, FinishedGoodsValue = x.FinishedGoodsValue, Rate = x.Rate,
            Variants = x.Allocations.Select(y => new MaterialInViewVariant
            {
                ColourName = y.StockItemVariant.Colour?.Name ?? "N/A",
                SizeName = y.StockItemVariant.Size?.Name ?? "N/A", Quantity = y.Quantity
            }).ToList()
        }).ToList();
        result.Consumptions = detail.Voucher.MaterialInConsumptions.OrderBy(x => x.LineNumber).Select(x => new MaterialInViewConsumption
        {
            FinishedGoodName = x.JwoComponent.FinishedGood.StockItem.Name, StockItemName = x.StockItem.Name,
            UqcName = x.Uqc.ShortName, ConsumedQuantity = x.ConsumedQuantity, Rate = x.Rate, Value = x.Value
        }).ToList();
        return result;
    }

    public async Task<MaterialInEditData?> GetForEditAsync(long voucherId, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var detail = await db.MaterialInDetails.AsNoTracking()
            .Where(x => x.VoucherId == voucherId && x.Voucher.CompanyId == companyContext.CompanyId &&
                        x.Voucher.FinancialYearId == companyContext.FinancialYearId)
            .Include(x => x.Voucher).ThenInclude(x => x.VoucherType)
            .Include(x => x.Voucher).ThenInclude(x => x.MaterialInFinishedGoods).ThenInclude(x => x.Allocations)
            .Include(x => x.Voucher).ThenInclude(x => x.MaterialInConsumptions)
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);
        if (detail is null) return null;

        return new MaterialInEditData
        {
            VoucherId = detail.VoucherId,
            ConcurrencyToken = detail.Voucher.ConcurrencyToken,
            Status = detail.Voucher.Status,
            Defaults = new MaterialInVoucherDefaults
            {
                VoucherTypeId = detail.Voucher.VoucherTypeId,
                VoucherTypeName = detail.Voucher.VoucherType.Name,
                VoucherNumber = detail.Voucher.VoucherNumber,
                VoucherDate = detail.Voucher.VoucherDate,
                NumberingMode = detail.Voucher.VoucherType.NumberingMode
            },
            JobWorkerLedgerId = detail.Voucher.PartyLedgerId ?? 0,
            JwoVoucherId = detail.JwoVoucherId,
            ConsumptionGodownId = detail.ConsumptionGodownId,
            ReceivingGodownId = detail.ReceivingGodownId,
            ReferenceNumber = detail.Voucher.ReferenceNumber,
            Narration = detail.Voucher.Narration,
            FinishedGoods = detail.Voucher.MaterialInFinishedGoods.OrderBy(x => x.LineNumber).Select(x => new MaterialInFinishedGoodInput
            {
                JwoFinishedGoodId = x.JwoFinishedGoodId,
                BomStageId = x.BomStageId ?? 0,
                StageAssignmentId = x.StageAssignmentId ?? 0,
                ReceivedQuantity = x.ReceivedQuantity,
                TotalCharge = x.ProcessCharge,
                ProcessCharge = x.ActualProcessRate ?? (x.ReceivedQuantity == 0 ? 0 : x.ProcessCharge / x.ReceivedQuantity),
                ChargeMode = Enum.Parse<ProcessChargeMode>(x.ChargeMode),
                Variants = x.Allocations.Select(y => new MaterialInVariantInput
                {
                    StockItemVariantId = y.StockItemVariantId,
                    Quantity = y.Quantity
                }).ToList()
            }).ToList(),
            Consumptions = detail.Voucher.MaterialInConsumptions.OrderBy(x => x.LineNumber).Select(x => new MaterialInConsumptionInput
            {
                JwoComponentId = x.JwoComponentId,
                BomStageId = x.BomStageId ?? 0,
                StageAssignmentId = x.StageAssignmentId ?? 0,
                ConsumedQuantity = x.ConsumedQuantity
            }).ToList()
        };
    }

    public async Task<MaterialInSaveResult> SaveAsync(MaterialInSaveRequest request, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.OperateVouchers, cancellationToken);
        ArgumentNullException.ThrowIfNull(request);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow; var user = companyContext.Actor;
        try
        {
            var isAlteration = request.VoucherId > 0;
            Voucher? existingVoucher = null;
            MaterialInDetail? existingDetail = null;
            if (isAlteration)
            {
                existingVoucher = await db.Vouchers
                    .Include(x => x.VoucherType)
                    .Include(x => x.MaterialInDetail)
                    .Include(x => x.MaterialInFinishedGoods).ThenInclude(x => x.Allocations)
                    .Include(x => x.MaterialInConsumptions).ThenInclude(x => x.MaterialOutAllocations)
                    .SingleOrDefaultAsync(x => x.Id == request.VoucherId &&
                        x.CompanyId == companyContext.CompanyId &&
                        x.FinancialYearId == companyContext.FinancialYearId, cancellationToken)
                    ?? throw new InvalidOperationException("The selected Material In voucher no longer exists.");
                if (existingVoucher.Status == "Cancelled" && developerAccess?.IsDeveloper != true)
                    throw new InvalidOperationException("A cancelled Material In voucher cannot be altered.");
                if (!string.Equals(existingVoucher.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
                    throw new InvalidOperationException("This Material In voucher was changed by another operation. Reopen it and try again.");
                existingDetail = existingVoucher.MaterialInDetail
                    ?? throw new InvalidOperationException("The selected Material In voucher is incomplete.");
                if (existingDetail.JwoVoucherId != request.JwoVoucherId)
                    throw new InvalidOperationException("The original JWO / Order Number cannot be changed during Material In alteration.");
            }

            if (existingVoucher is null)
                await periodControl.EnsurePostingDateIsOpenAsync(
                    db, companyContext.CompanyId, request.VoucherDate, "Material In", cancellationToken);
            else
                await periodControl.EnsureVoucherMutationIsOpenAsync(
                    db, companyContext.CompanyId, existingVoucher.VoucherDate, request.VoucherDate,
                    $"Material In {existingVoucher.VoucherNumber}", cancellationToken);

            var fy = await db.FinancialYears.AsNoTracking().SingleAsync(x => x.Id == companyContext.FinancialYearId && x.CompanyId == companyContext.CompanyId, cancellationToken);
            if (request.VoucherDate < fy.StartDate || request.VoucherDate > fy.EndDate) throw new InvalidOperationException($"Voucher Date must be within financial year {fy.Name}.");
            var type = existingVoucher?.VoucherType ?? await db.VoucherTypes.SingleAsync(x => x.Id == request.VoucherTypeId && x.CompanyId == companyContext.CompanyId && x.IsActive, cancellationToken);
            var validType = type.SystemTypeCode == MaterialInTypeCode || await db.VoucherTypes.AsNoTracking().AnyAsync(x => x.Id == type.ParentVoucherTypeId && x.SystemTypeCode == MaterialInTypeCode, cancellationToken);
            if (!validType) throw new InvalidOperationException("Selected Voucher Type is not a Material In type.");
            var jwo = await db.Vouchers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.JwoVoucherId && x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId && x.Status != "Cancelled", cancellationToken) ?? throw new InvalidOperationException("The selected JWO is unavailable or cancelled.");
            if (request.VoucherDate < jwo.VoucherDate)
                throw new InvalidOperationException($"Material In date cannot be earlier than the selected JWO date {jwo.VoucherDate:dd-MMM-yyyy}.");
            if (!await db.Ledgers.AsNoTracking().AnyAsync(x => x.Id == request.JobWorkerLedgerId && x.CompanyId == companyContext.CompanyId && x.IsActive && x.IsJobWorker, cancellationToken)) throw new InvalidOperationException("Select a valid active Job Worker.");
            if (!await db.Godowns.AsNoTracking().AnyAsync(x => x.Id == request.ConsumptionGodownId && x.CompanyId == companyContext.CompanyId && x.IsActive, cancellationToken)) throw new InvalidOperationException("Select a valid Consumption Godown.");
            if (!await db.Godowns.AsNoTracking().AnyAsync(x => x.Id == request.ReceivingGodownId && x.CompanyId == companyContext.CompanyId && x.IsActive, cancellationToken)) throw new InvalidOperationException("Select a valid Finished Goods Godown.");
            var fgInputs = request.FinishedGoods.Where(x => x.ReceivedQuantity > 0).ToList();
            var originalExpectedRates = existingVoucher?.MaterialInFinishedGoods.ToDictionary(
                x => (x.JwoFinishedGoodId, x.StageAssignmentId ?? 0), x => x.ExpectedProcessRate)
                ?? new Dictionary<(long, long), decimal?>();
            foreach (var input in fgInputs)
                (input.ProcessCharge, input.TotalCharge) = ProcessChargeCalculation.Calculate(
                    input.ReceivedQuantity, input.ProcessCharge, input.TotalCharge, input.ChargeMode);
            var consInputs = request.Consumptions.Where(x => x.ConsumedQuantity > 0).ToList();
            if (fgInputs.Count == 0) throw new InvalidOperationException("Enter at least one finished-goods receipt quantity.");
            if (consInputs.Count == 0) throw new InvalidOperationException("Enter at least one raw-material consumption quantity.");
            if (fgInputs.Any(x => x.TotalCharge <= 0))
                throw new InvalidOperationException("Total process charges are required and must be greater than zero for every received stage output.");

            // Compatibility for forms opened before explicit stage identifiers were added.
            // Resolve only an unambiguous active assignment; never guess between stages.
            foreach (var input in fgInputs.Where(x => x.BomStageId <= 0 || x.StageAssignmentId <= 0))
            {
                var candidates = await db.JobWorkOrderBomStages.AsNoTracking()
                    .Where(x => x.VoucherId == request.JwoVoucherId && x.FinishedGoodId == input.JwoFinishedGoodId &&
                                x.AssignmentHistory.Any(a => a.JobWorkerId == request.JobWorkerLedgerId && a.Status == "Active"))
                    .Select(x => new { StageId = x.Id, AssignmentId = x.AssignmentHistory.Where(a => a.JobWorkerId == request.JobWorkerLedgerId && a.Status == "Active").Select(a => a.Id).Single() })
                    .ToListAsync(cancellationToken);
                if (candidates.Count == 1) { input.BomStageId = candidates[0].StageId; input.StageAssignmentId = candidates[0].AssignmentId; }
            }
            foreach (var input in consInputs.Where(x => x.BomStageId <= 0 || x.StageAssignmentId <= 0))
            {
                var candidate = await db.JobWorkOrderComponents.AsNoTracking()
                    .Where(x => x.Id == input.JwoComponentId && x.BomStageId != null && x.BomStage!.VoucherId == request.JwoVoucherId)
                    .Select(x => new
                    {
                        StageId = x.BomStageId!.Value,
                        AssignmentId = x.BomStage!.AssignmentHistory.Where(a => a.JobWorkerId == request.JobWorkerLedgerId && a.Status == "Active").Select(a => a.Id).SingleOrDefault()
                    }).SingleOrDefaultAsync(cancellationToken);
                if (candidate is not null && candidate.AssignmentId > 0) { input.BomStageId = candidate.StageId; input.StageAssignmentId = candidate.AssignmentId; }
            }
            if (fgInputs.GroupBy(x => new { x.BomStageId, x.StageAssignmentId }).Any(x => x.Count() > 1) ||
                consInputs.GroupBy(x => new { x.JwoComponentId, x.StageAssignmentId }).Any(x => x.Count() > 1))
                throw new InvalidOperationException("Duplicate Material In source rows are not allowed.");

            var fgIds = fgInputs.Select(x => x.JwoFinishedGoodId).ToList();
            var fgs = await db.JobWorkOrderFinishedGoods.AsNoTracking()
                .Where(x => fgIds.Contains(x.Id))
                .Include(x => x.StockItem).ThenInclude(x => x.Uqc)
                .Include(x => x.SizeAllocations)
                .Include(x => x.BomStages)
                .ToDictionaryAsync(x => x.Id, cancellationToken);
            if (fgs.Count != fgIds.Count || fgs.Values.Any(x => x.VoucherId != request.JwoVoucherId))
                throw new InvalidOperationException("One or more finished-goods rows do not belong to the selected JWO.");
            foreach (var input in fgInputs)
            {
                var hasStage = input.BomStageId > 0 || input.StageAssignmentId > 0;
                if ((input.BomStageId > 0) != (input.StageAssignmentId > 0))
                    throw new InvalidOperationException("A production stage and assignment version must be selected together.");
                if (!hasStage && fgs[input.JwoFinishedGoodId].BomStages.Count > 0)
                    throw new InvalidOperationException("A BOM-based receipt must retain its production stage and assignment version.");
                if (!hasStage && jwo.PartyLedgerId != request.JobWorkerLedgerId)
                    throw new InvalidOperationException("This manual JWO is not assigned to the selected Job Worker.");
            }

            var requestedStageIds = fgInputs.Select(x => x.BomStageId)
                .Concat(consInputs.Select(x => x.BomStageId))
                .Where(x => x > 0).Distinct().ToList();
            var stages = await db.JobWorkOrderBomStages.AsNoTracking()
                .Where(x => requestedStageIds.Contains(x.Id))
                .Include(x => x.OutputStockItem).ThenInclude(x => x.Uqc)
                .Include(x => x.AssignmentHistory)
                .Include(x => x.ChildStages)
                .ToDictionaryAsync(x => x.Id, cancellationToken);
            if (stages.Count != requestedStageIds.Count || stages.Values.Any(x => x.VoucherId != request.JwoVoucherId))
                throw new InvalidOperationException("One or more production stages do not belong to the selected JWO.");
            foreach (var stageId in requestedStageIds)
            {
                var stage = stages[stageId];
                var assignmentIds = fgInputs.Where(y => y.BomStageId == stageId).Select(y => y.StageAssignmentId)
                    .Concat(consInputs.Where(y => y.BomStageId == stageId).Select(y => y.StageAssignmentId))
                    .Distinct().ToList();
                if (assignmentIds.Count != 1)
                    throw new InvalidOperationException($"Stage '{stage.StageName}' must use one immutable assignment version per receipt.");
                var assignment = stage.AssignmentHistory.SingleOrDefault(x => x.Id == assignmentIds[0]);
                if (assignment is null || assignment.JobWorkerId != request.JobWorkerLedgerId)
                    throw new InvalidOperationException($"Stage '{stage.StageName}' is not assigned to the selected Job Worker/version. Reload the JWO.");
            }

            var priorFg = await db.MaterialInFinishedGoods.AsNoTracking()
                .Where(x => x.BomStageId != null && x.StageAssignmentId != null && requestedStageIds.Contains(x.BomStageId.Value) && x.Voucher.Status != "Cancelled" &&
                            (!isAlteration || x.VoucherId != request.VoucherId))
                .GroupBy(x => new { StageId = x.BomStageId!.Value, AssignmentId = x.StageAssignmentId!.Value })
                .Select(x => new { x.Key.StageId, x.Key.AssignmentId, Qty = x.Sum(y => y.ReceivedQuantity) })
                .ToDictionaryAsync(x => (x.StageId, x.AssignmentId), x => x.Qty, cancellationToken);
            var priorManualFg = await db.MaterialInFinishedGoods.AsNoTracking()
                .Where(x => fgIds.Contains(x.JwoFinishedGoodId) && x.BomStageId == null && x.Voucher.Status != "Cancelled" &&
                            (!isAlteration || x.VoucherId != request.VoucherId))
                .GroupBy(x => x.JwoFinishedGoodId)
                .Select(x => new { FinishedGoodId = x.Key, Qty = x.Sum(y => y.ReceivedQuantity) })
                .ToDictionaryAsync(x => x.FinishedGoodId, x => x.Qty, cancellationToken);
            var requestedVariantIds = fgInputs.SelectMany(x => x.Variants)
                .Where(x => x.Quantity > 0).Select(x => x.StockItemVariantId).Distinct().ToList();
            var priorVariantReceipts = requestedVariantIds.Count == 0
                ? new Dictionary<(long FinishedGoodId, long VariantId), decimal>()
                : await db.MaterialInFinishedGoodAllocations.AsNoTracking()
                    .Where(x => requestedVariantIds.Contains(x.StockItemVariantId) &&
                                fgIds.Contains(x.FinishedGoodLine.JwoFinishedGoodId) &&
                                x.FinishedGoodLine.Voucher.Status != "Cancelled" &&
                                (!isAlteration || x.FinishedGoodLine.VoucherId != request.VoucherId))
                    .GroupBy(x => new { FinishedGoodId = x.FinishedGoodLine.JwoFinishedGoodId, VariantId = x.StockItemVariantId })
                    .Select(x => new { x.Key.FinishedGoodId, x.Key.VariantId, Qty = x.Sum(y => y.Quantity) })
                    .ToDictionaryAsync(x => (x.FinishedGoodId, x.VariantId), x => x.Qty, cancellationToken);

            var stageRequirements = await db.JobWorkOrderComponents.AsNoTracking()
                .Where(x => x.BomStageId != null && requestedStageIds.Contains(x.BomStageId.Value) && x.RequiredQuantity > 0)
                .Select(x => new { x.Id, StageId = x.BomStageId!.Value, x.RequiredQuantity, DecimalPlaces = x.Uqc.DecimalPlaces })
                .ToListAsync(cancellationToken);
            var manualRequirements = await db.JobWorkOrderComponents.AsNoTracking()
                .Where(x => fgIds.Contains(x.FinishedGoodId) && x.BomStageId == null && x.RequiredQuantity > 0)
                .Select(x => new { x.Id, x.FinishedGoodId, x.RequiredQuantity, DecimalPlaces = x.Uqc.DecimalPlaces })
                .ToListAsync(cancellationToken);
            var requirementIds = stageRequirements.Select(x => x.Id).ToList();
            var issuedForAssignments = await db.MaterialOutLines.AsNoTracking()
                .Where(x => requirementIds.Contains(x.JwoComponentId) && x.StageAssignmentId != null && x.Voucher.Status != "Cancelled")
                .GroupBy(x => new { x.JwoComponentId, AssignmentId = x.StageAssignmentId!.Value })
                .Select(x => new { x.Key.JwoComponentId, x.Key.AssignmentId, Qty = x.Sum(y => y.IssuedQuantity) })
                .ToDictionaryAsync(x => (x.JwoComponentId, x.AssignmentId), x => x.Qty, cancellationToken);
            var manualRequirementIds = manualRequirements.Select(x => x.Id).ToList();
            var issuedForManual = await db.MaterialOutLines.AsNoTracking()
                .Where(x => manualRequirementIds.Contains(x.JwoComponentId) && x.StageAssignmentId == null && x.Voucher.Status != "Cancelled")
                .GroupBy(x => x.JwoComponentId)
                .Select(x => new { ComponentId = x.Key, Qty = x.Sum(y => y.IssuedQuantity) })
                .ToDictionaryAsync(x => x.ComponentId, x => x.Qty, cancellationToken);
            foreach (var input in fgInputs)
            {
                var fg = fgs[input.JwoFinishedGoodId];
                if (input.BomStageId <= 0)
                {
                    if (request.ReceivingGodownId != fg.FinishedGoodsGodownId)
                        throw new InvalidOperationException("Manual JWO output must be received into its Finished Goods Godown.");
                    priorManualFg.TryGetValue(fg.Id, out var manualPrior);
                    var manualOutputRequirements = manualRequirements.Where(x => x.FinishedGoodId == fg.Id).ToList();
                    var supported = manualOutputRequirements.Count == 0 ? 0 : manualOutputRequirements
                        .Select(x => issuedForManual.GetValueOrDefault(x.Id) / x.RequiredQuantity * fg.OrderedQuantity)
                        .DefaultIfEmpty(0m).Min();
                    supported = Math.Min(fg.OrderedQuantity, supported);
                    if (manualPrior + input.ReceivedQuantity > supported)
                        throw new InvalidOperationException($"Manual allocation supports only {supported:0.####}; receive the balance only after its Material Out.");

                    var manualPositiveVariants = input.Variants.Where(x => x.Quantity > 0).ToList();
                    if (manualPositiveVariants.Count == 0 || manualPositiveVariants.Sum(x => x.Quantity) != input.ReceivedQuantity)
                        throw new InvalidOperationException("Final finished-goods colour/size allocation must equal the received quantity.");
                    var allowed = fg.SizeAllocations.ToDictionary(x => x.StockItemVariantId, x => x.Quantity);
                    if (manualPositiveVariants.Any(x => !allowed.ContainsKey(x.StockItemVariantId)) ||
                        manualPositiveVariants.GroupBy(x => x.StockItemVariantId).Any(x => x.Count() > 1))
                        throw new InvalidOperationException("A finished-goods colour/size allocation is invalid for the selected JWO row.");
                    foreach (var variant in manualPositiveVariants)
                    {
                        var cumulative = priorVariantReceipts.GetValueOrDefault((fg.Id, variant.StockItemVariantId)) + variant.Quantity;
                        if (cumulative > allowed[variant.StockItemVariantId])
                            throw new InvalidOperationException("Received colour/size quantity cannot exceed that variant's JWO ordered quantity.");
                    }
                    continue;
                }
                var stage = stages[input.BomStageId];
                if (stage.FinishedGoodId != fg.Id) throw new InvalidOperationException("A production stage/finished-good link changed. Reload the JWO.");
                if (stage.OutputGodownId is not null && stage.OutputGodownId != request.ReceivingGodownId)
                    throw new InvalidOperationException($"Stage '{stage.StageName}' must be received into its assigned output godown.");
                priorFg.TryGetValue((stage.Id, input.StageAssignmentId), out var prior);
                if (input.TotalCharge <= 0) throw new InvalidOperationException($"Total process charges for {fg.StockItem.Name} are required and must be greater than zero.");
                var requirements = stageRequirements.Where(x => x.StageId == stage.Id).ToList();
                var assignmentSupported = requirements.Count == 0 ? stage.OutputQuantity : requirements
                    .Select(x => issuedForAssignments.GetValueOrDefault((x.Id, input.StageAssignmentId)) / x.RequiredQuantity * stage.OutputQuantity)
                    .DefaultIfEmpty(0m).Min();
                assignmentSupported = Math.Min(stage.OutputQuantity, assignmentSupported);
                if (prior + input.ReceivedQuantity > assignmentSupported)
                    throw new InvalidOperationException($"Stage '{stage.StageName}' assignment v{stage.AssignmentHistory.Single(x => x.Id == input.StageAssignmentId).AssignmentVersion} supports only {assignmentSupported:0.####}; receive the balance only after its linked Material Out.");
                var positiveVariants = input.Variants.Where(x => x.Quantity > 0).ToList();
                if (stage.IsFinalStage)
                {
                    if (positiveVariants.Count == 0 || positiveVariants.Sum(x => x.Quantity) != input.ReceivedQuantity) throw new InvalidOperationException("Final finished-goods colour/size allocation must equal the received quantity.");
                    var allowed = fg.SizeAllocations.Select(x => x.StockItemVariantId).ToHashSet();
                    if (positiveVariants.Any(x => !allowed.Contains(x.StockItemVariantId)) || positiveVariants.GroupBy(x => x.StockItemVariantId).Any(x => x.Count() > 1)) throw new InvalidOperationException("A finished-goods colour/size allocation is invalid for the selected JWO row.");
                    var orderedByVariant = fg.SizeAllocations.ToDictionary(x => x.StockItemVariantId, x => x.Quantity);
                    foreach (var variant in positiveVariants)
                    {
                        var cumulative = priorVariantReceipts.GetValueOrDefault((fg.Id, variant.StockItemVariantId)) + variant.Quantity;
                        if (cumulative > orderedByVariant[variant.StockItemVariantId])
                            throw new InvalidOperationException("Received colour/size quantity cannot exceed that variant's JWO ordered quantity.");
                    }
                }
                else if (positiveVariants.Count > 0) throw new InvalidOperationException("Intermediate-stage output does not use final colour/size allocations.");

                if (stage.ChildStages.Count > 0)
                {
                    var childIds = stage.ChildStages.Select(x => x.Id).ToList();
                    var childReceipts = await db.MaterialInFinishedGoods.AsNoTracking()
                        .Where(x => x.BomStageId != null && childIds.Contains(x.BomStageId.Value) && x.Voucher.Status != "Cancelled")
                        .GroupBy(x => x.BomStageId!.Value).Select(x => new { x.Key, Qty = x.Sum(y => y.ReceivedQuantity) })
                        .ToDictionaryAsync(x => x.Key, x => x.Qty, cancellationToken);
                    var supported = stage.ChildStages.Min(child => child.OutputQuantity <= 0 ? 0 :
                        decimal.Round(childReceipts.GetValueOrDefault(child.Id) / child.OutputQuantity * stage.OutputQuantity, 4));
                    if (prior + input.ReceivedQuantity > supported)
                        throw new InvalidOperationException($"Stage '{stage.StageName}' cannot be received yet. Earlier component stages support only {supported:0.####} of {stage.OutputQuantity:0.####}.");
                }
            }

            var componentIds = consInputs.Select(x => x.JwoComponentId).ToList();
            var comps = await db.JobWorkOrderComponents.AsNoTracking().Where(x => componentIds.Contains(x.Id)).Include(x => x.FinishedGood).ToDictionaryAsync(x => x.Id, cancellationToken);
            if (comps.Count != componentIds.Count || comps.Values.Any(x => x.FinishedGood.VoucherId != request.JwoVoucherId)) throw new InvalidOperationException("One or more consumption rows do not belong to the selected JWO.");
            if (consInputs.Any(x => (comps[x.JwoComponentId].BomStageId ?? 0) != x.BomStageId ||
                                    ((x.BomStageId > 0) != (x.StageAssignmentId > 0))))
                throw new InvalidOperationException("A material-consumption stage/assignment link changed. Reload the JWO.");
            var moLines = await db.MaterialOutLines.AsNoTracking()
                .Include(x => x.Voucher)
                .Where(x => componentIds.Contains(x.JwoComponentId) && x.JwoVoucherId == request.JwoVoucherId && x.Voucher.Status != "Cancelled")
                .OrderBy(x => x.Voucher.VoucherDate).ThenBy(x => x.Voucher.SequenceNumber).ThenBy(x => x.LineNumber)
                .ToListAsync(cancellationToken);
            var priorAlloc = await db.MaterialInMaterialOutAllocations.AsNoTracking()
                .Where(x => componentIds.Contains(x.ConsumptionLine.JwoComponentId) &&
                            x.ConsumptionLine.Voucher.Status != "Cancelled" &&
                            (!isAlteration || x.ConsumptionLine.VoucherId != request.VoucherId))
                .GroupBy(x => x.MaterialOutLineId).Select(x => new { x.Key, Qty = x.Sum(y => y.AllocatedQuantity) })
                .ToDictionaryAsync(x => x.Key, x => x.Qty, cancellationToken);
            var selectedAssignmentIds = consInputs.Select(x => x.StageAssignmentId).ToHashSet();
            var actualJobberDestinations = moLines
                .Where(x => selectedAssignmentIds.Contains(x.StageAssignmentId ?? 0) &&
                    x.IssuedQuantity > priorAlloc.GetValueOrDefault(x.Id))
                .Select(x => x.DestinationGodownId).Distinct().ToList();
            if (actualJobberDestinations.Count != 1)
                throw new InvalidOperationException("The selected receipt contains Material Out from multiple Jobber Destination Godowns. Receive each jobber-godown lot separately.");
            if (actualJobberDestinations[0] != request.ConsumptionGodownId)
                throw new InvalidOperationException("Consumption Godown must match the Jobber Destination Godown used by the linked Material Out.");
            var latestMaterialOutDate = moLines
                .Where(x => consInputs.Any(input => input.JwoComponentId == x.JwoComponentId && input.StageAssignmentId == (x.StageAssignmentId ?? 0)))
                .Select(x => (DateOnly?)x.Voucher.VoucherDate).Max();
            if (latestMaterialOutDate is not null && request.VoucherDate < latestMaterialOutDate.Value)
                throw new InvalidOperationException($"Material In date cannot be earlier than its supplying Material Out date {latestMaterialOutDate:dd-MMM-yyyy}.");
            foreach (var input in consInputs)
            {
                var available = moLines.Where(x => x.JwoComponentId == input.JwoComponentId &&
                    (x.StageAssignmentId ?? 0) == input.StageAssignmentId)
                    .Sum(x => x.IssuedQuantity - (priorAlloc.TryGetValue(x.Id, out var used) ? used : 0));
                if (input.ConsumedQuantity <= 0 || input.ConsumedQuantity > available) throw new InvalidOperationException("Raw-material consumption exceeds actual unconsumed Material Out quantity.");
            }

            foreach (var output in fgInputs)
            {
                if (output.BomStageId <= 0)
                {
                    var fg = fgs[output.JwoFinishedGoodId];
                    foreach (var requirement in manualRequirements.Where(x => x.FinishedGoodId == fg.Id))
                    {
                        var consumption = consInputs.SingleOrDefault(x => x.JwoComponentId == requirement.Id && x.StageAssignmentId == 0);
                        var expected = decimal.Round(
                            output.ReceivedQuantity / fg.OrderedQuantity * requirement.RequiredQuantity,
                            requirement.DecimalPlaces,
                            MidpointRounding.AwayFromZero);
                        if (consumption is null || consumption.ConsumedQuantity < expected)
                            throw new InvalidOperationException(
                                $"Manual allocation requires at least {expected:0.####} of every linked component for this receipt. Record the actual consumption first.");
                    }
                    continue;
                }
                var stage = stages[output.BomStageId];
                foreach (var requirement in stageRequirements.Where(x => x.StageId == stage.Id))
                {
                    var consumption = consInputs.SingleOrDefault(x =>
                        x.JwoComponentId == requirement.Id && x.StageAssignmentId == output.StageAssignmentId);
                    var expected = decimal.Round(
                        output.ReceivedQuantity / stage.OutputQuantity * requirement.RequiredQuantity,
                        requirement.DecimalPlaces,
                        MidpointRounding.AwayFromZero);
                    if (consumption is null || consumption.ConsumedQuantity < expected)
                        throw new InvalidOperationException(
                            $"Stage '{stage.StageName}' requires at least {expected:0.####} of every linked component for this receipt. " +
                            "Record the actual consumption before receiving the output.");
                }
            }

            Voucher voucher;
            string number;
            if (isAlteration)
            {
                voucher = existingVoucher!;
                number = voucher.VoucherNumber;

                stockPosting.RemoveVoucherPostings(db, voucher.Id);
                db.MaterialInMaterialOutAllocations.RemoveRange(voucher.MaterialInConsumptions.SelectMany(x => x.MaterialOutAllocations));
                db.MaterialInFinishedGoodAllocations.RemoveRange(voucher.MaterialInFinishedGoods.SelectMany(x => x.Allocations));
                db.MaterialInConsumptions.RemoveRange(voucher.MaterialInConsumptions);
                db.MaterialInFinishedGoods.RemoveRange(voucher.MaterialInFinishedGoods);
                db.MaterialInDetails.Remove(existingDetail!);
                db.VoucherLinks.RemoveRange(db.VoucherLinks.Where(x => x.TargetVoucherId == voucher.Id && x.LinkType == "JWO_TO_MI"));
                await db.SaveChangesAsync(cancellationToken);

                voucher.VoucherDate = request.VoucherDate;
                voucher.ReferenceNumber = request.ReferenceNumber.Trim();
                voucher.Batch = request.Batch.Trim();
                voucher.PartyLedgerId = request.JobWorkerLedgerId;
                voucher.Narration = request.Narration.Trim();
                voucher.ModifiedAtUtc = now;
                voucher.ModifiedBy = user;
                voucher.ConcurrencyToken = Guid.NewGuid().ToString("N");
            }
            else
            {
                var sequence = await (sequenceAllocator ?? new VoucherSequenceAllocator(companyContext))
                    .ReserveAsync(db, type, user, now, cancellationToken);
                number = type.NumberingMode == "Manual" ? request.VoucherNumber.Trim() : FormatAutomaticNumber(type, sequence);
                if (string.IsNullOrWhiteSpace(number)) throw new InvalidOperationException("Voucher Number is required.");
                var normalized = Normalize(number);
                if (await db.Vouchers.AsNoTracking().AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId && x.VoucherTypeId == type.Id && x.VoucherNumberNormalized == normalized, cancellationToken))
                    throw new InvalidOperationException($"Voucher Number '{number}' already exists.");
                voucher = new Voucher { CompanyId = companyContext.CompanyId, FinancialYearId = companyContext.FinancialYearId, VoucherTypeId = type.Id, SequenceNumber = sequence, VoucherNumber = number, VoucherNumberNormalized = normalized, VoucherDate = request.VoucherDate, ReferenceNumber = request.ReferenceNumber.Trim(), Batch = request.Batch.Trim(), PartyLedgerId = request.JobWorkerLedgerId, Narration = request.Narration.Trim(), Status = "Open", CreatedAtUtc = now, ModifiedAtUtc = now, CreatedBy = user, ModifiedBy = user, ConcurrencyToken = Guid.NewGuid().ToString("N") };
                db.Vouchers.Add(voucher);
                await db.SaveChangesAsync(cancellationToken);
            }

            decimal totalConsumedValue = 0;
            var consumedValueByStage = new Dictionary<(long StageId, long AssignmentId), decimal>();
            var consumptionNo = 0;
            foreach (var input in consInputs)
            {
                consumptionNo++;
                var candidates = moLines.Where(x => x.JwoComponentId == input.JwoComponentId &&
                    (x.StageAssignmentId ?? 0) == input.StageAssignmentId).ToList();
                var available = candidates.Sum(x => x.IssuedQuantity - (priorAlloc.TryGetValue(x.Id, out var used) ? used : 0));
                var remaining = input.ConsumedQuantity;
                var line = new MaterialInConsumption { VoucherId = voucher.Id, JwoComponentId = input.JwoComponentId, BomStageId = input.BomStageId > 0 ? input.BomStageId : null, StageAssignmentId = input.StageAssignmentId > 0 ? input.StageAssignmentId : null, LineNumber = consumptionNo, StockItemId = comps[input.JwoComponentId].StockItemId, UqcId = comps[input.JwoComponentId].UqcId, ConsumptionGodownId = request.ConsumptionGodownId, AvailableQuantity = available, ConsumedQuantity = input.ConsumedQuantity };
                db.MaterialInConsumptions.Add(line); await db.SaveChangesAsync(cancellationToken);
                decimal value = 0;
                foreach (var mo in candidates)
                {
                    priorAlloc.TryGetValue(mo.Id, out var priorUsed); var free = mo.IssuedQuantity - priorUsed; if (free <= 0) continue;
                    var qty = Math.Min(free, remaining); var allocValue = decimal.Round(qty * mo.Rate, 4, MidpointRounding.AwayFromZero);
                    db.MaterialInMaterialOutAllocations.Add(new MaterialInMaterialOutAllocation { ConsumptionLineId = line.Id, MaterialOutLineId = mo.Id, AllocatedQuantity = qty, RateSnapshot = mo.Rate, ValueSnapshot = allocValue });
                    value += allocValue; remaining -= qty; if (remaining == 0) break;
                }
                line.Value = value; line.Rate = input.ConsumedQuantity == 0 ? 0 : decimal.Round(value / input.ConsumedQuantity, 4, MidpointRounding.AwayFromZero); totalConsumedValue += value;
                var stageAssignmentKey = input.BomStageId > 0
                    ? (input.BomStageId, input.StageAssignmentId)
                    : (-comps[input.JwoComponentId].FinishedGoodId, 0L);
                consumedValueByStage[stageAssignmentKey] = consumedValueByStage.GetValueOrDefault(stageAssignmentKey) + value;
                stockPosting.Post(db, new StockMovementDraft(
                    companyContext.CompanyId, companyContext.FinancialYearId, voucher.Id,
                    request.VoucherDate, line.StockItemId, line.UqcId, request.ConsumptionGodownId,
                    -line.ConsumedQuantity, line.Rate, -line.Value, "MaterialInConsumption",
                    MaterialInConsumptionId: line.Id,
                    StockItemVariantId: comps[input.JwoComponentId].ComponentVariantId), user, now);
            }

            decimal totalProcess = fgInputs.Sum(x => x.TotalCharge); decimal totalFgValue = 0; var fgNo = 0;
            foreach (var input in fgInputs)
            {
                fgNo++; var fg = fgs[input.JwoFinishedGoodId];
                JobWorkOrderBomStage? stage = input.BomStageId > 0 ? stages[input.BomStageId] : null;
                var prior = stage is null
                    ? priorManualFg.GetValueOrDefault(fg.Id)
                    : priorFg.GetValueOrDefault((stage.Id, input.StageAssignmentId));
                var valueKey = stage is null ? (-fg.Id, 0L) : (stage.Id, input.StageAssignmentId);
                var materialValue = decimal.Round(consumedValueByStage.GetValueOrDefault(valueKey), 4, MidpointRounding.AwayFromZero);
                var finalValue = materialValue + input.TotalCharge; var rate = decimal.Round(finalValue / input.ReceivedQuantity, 4, MidpointRounding.AwayFromZero); totalFgValue += finalValue;
                var line = new MaterialInFinishedGood { VoucherId = voucher.Id, JwoFinishedGoodId = fg.Id, BomStageId = stage?.Id, StageAssignmentId = stage is null ? null : input.StageAssignmentId, LineNumber = fgNo, StockItemId = stage?.OutputStockItemId ?? fg.StockItemId, UqcId = stage?.OutputUqcId ?? fg.StockItem.UqcId, ReceivingGodownId = request.ReceivingGodownId, OrderedQuantity = input.ReceivedQuantity + prior, PreviouslyReceivedQuantity = prior, ReceivedQuantity = input.ReceivedQuantity, MaterialValue = materialValue, ProcessCharge = input.TotalCharge, FinishedGoodsValue = finalValue, Rate = rate };
                line.ActualProcessRate = input.ProcessCharge;
                line.ChargeMode = input.ChargeMode.ToString();
                line.ExpectedProcessRate = originalExpectedRates.TryGetValue((fg.Id, input.StageAssignmentId), out var historicalRate)
                    ? historicalRate
                    : stage?.AssignmentHistory.Single(x => x.Id == input.StageAssignmentId).ExpectedProcessRate;
                db.MaterialInFinishedGoods.Add(line); await db.SaveChangesAsync(cancellationToken);
                db.AuditLogs.Add(new AuditLog
                {
                    CompanyId = companyContext.CompanyId, EntityType = "Voucher", EntityId = voucher.Id,
                    Action = "ProcessChargeSnapshot", Success = true, PerformedBy = user, PerformedAtUtc = now,
                    Description = $"MI {number}, stage {stage?.StagePath ?? "Manual"}, assignment {input.StageAssignmentId}: mode {line.ChargeMode}, quantity {line.ReceivedQuantity}, expected rate {line.ExpectedProcessRate?.ToString() ?? "Not recorded"}, actual rate {line.ActualProcessRate}, total process charge {line.ProcessCharge}."
                });
                foreach (var v in input.Variants.Where(x => x.Quantity > 0))
                {
                    db.MaterialInFinishedGoodAllocations.Add(new MaterialInFinishedGoodAllocation { FinishedGoodLineId = line.Id, StockItemVariantId = v.StockItemVariantId, Quantity = v.Quantity });
                    stockPosting.Post(db, new StockMovementDraft(
                        companyContext.CompanyId, companyContext.FinancialYearId, voucher.Id,
                        request.VoucherDate, line.StockItemId, line.UqcId, request.ReceivingGodownId,
                        v.Quantity, rate, decimal.Round(v.Quantity * rate, 4, MidpointRounding.AwayFromZero),
                        "MaterialInFinishedGoods", MaterialInFinishedGoodId: line.Id,
                        StockItemVariantId: v.StockItemVariantId), user, now);
                }
                if (input.Variants.All(x => x.Quantity <= 0))
                {
                    stockPosting.Post(db, new StockMovementDraft(
                        companyContext.CompanyId, companyContext.FinancialYearId, voucher.Id,
                        request.VoucherDate, line.StockItemId, line.UqcId, request.ReceivingGodownId,
                        line.ReceivedQuantity, rate, line.FinishedGoodsValue,
                        "MaterialInFinishedGoods", MaterialInFinishedGoodId: line.Id), user, now);
                }
            }
            db.MaterialInDetails.Add(new MaterialInDetail { VoucherId = voucher.Id, JwoVoucherId = request.JwoVoucherId, ConsumptionGodownId = request.ConsumptionGodownId, ReceivingGodownId = request.ReceivingGodownId, DisplayedOrderNumber = jwo.VoucherNumber, TotalProcessCharge = totalProcess, TotalConsumedMaterialValue = totalConsumedValue, TotalFinishedGoodsValue = totalFgValue });
            db.VoucherLinks.Add(new VoucherLink { CompanyId = companyContext.CompanyId, SourceVoucherId = request.JwoVoucherId, TargetVoucherId = voucher.Id, LinkType = "JWO_TO_MI", CreatedAtUtc = now, CreatedBy = user });
            db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyContext.CompanyId,
                EntityType = "Voucher",
                EntityId = voucher.Id,
                Action = isAlteration ? "Alter" : "Create",
                Success = true,
                Description = $"{(isAlteration ? "Altered" : "Created")} Material In '{number}' with {fgInputs.Count} finished-good and {consInputs.Count} consumption line(s).",
                PerformedBy = user,
                PerformedAtUtc = now
            });
            await db.SaveChangesAsync(cancellationToken);
            var jwoStatusChanged = await JobWorkOrderStatusUpdater.UpdateAsync(db, request.JwoVoucherId, now, user, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (isAlteration)
            {
                await TallySyncStateTracker.MarkUpdatedNotExportedAsync(db, voucher.Id, user, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }
            await fullAudit.RecordAsync(
                db, voucher.Id,
                isAlteration ? VoucherAuditActions.Update : VoucherAuditActions.Create,
                isAlteration ? "Material In altered." : "Initial Material In save.",
                user, now, cancellationToken);
            if (jwoStatusChanged)
                await fullAudit.RecordAsync(
                    db, request.JwoVoucherId, VoucherAuditActions.SystemStatusUpdate,
                    $"Status recalculated after Material In {number} was {(isAlteration ? "altered" : "created")}.",
                    user, now, cancellationToken);
            await stockPosting.EnsureNegativeStockPolicyAsync(
                db, companyContext.CompanyId, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new MaterialInSaveResult { VoucherId = voucher.Id, VoucherNumber = number };
        }
        catch { await tx.RollbackAsync(cancellationToken); throw; }
    }

    public async Task<OperationResult> CancelAsync(long voucherId, string reason, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.CancelOrDeleteVouchers, cancellationToken);
        var reasonValidation = VoucherLifecycleService.ValidateCancellationReason(reason);
        if (reasonValidation is not null) return OperationResult.Fail(reasonValidation);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var user = companyContext.Actor;
        try
        {
            var voucher = await db.Vouchers.SingleOrDefaultAsync(x => x.Id == voucherId &&
                x.CompanyId == companyContext.CompanyId &&
                x.FinancialYearId == companyContext.FinancialYearId, cancellationToken);
            if (voucher is null) return OperationResult.Fail("The selected Material In voucher no longer exists.");
            if (voucher.Status == "Cancelled") return OperationResult.Fail("This Material In voucher is already cancelled.");
            await periodControl.EnsurePostingDateIsOpenAsync(
                db, companyContext.CompanyId, voucher.VoucherDate,
                $"Material In {voucher.VoucherNumber}", cancellationToken);
            var jwoId = await db.MaterialInDetails.AsNoTracking()
                .Where(x => x.VoucherId == voucher.Id)
                .Select(x => x.JwoVoucherId)
                .SingleAsync(cancellationToken);
            var cancellationLinks = await lifecycle.GetActiveLinkDescriptionsAsync(
                db, companyContext.CompanyId, voucher.Id, VoucherLinkScope.Downstream, cancellationToken);
            if (cancellationLinks.Count > 0)
                return OperationResult.Fail(VoucherLifecycleService.BuildBlockedMessage("cancelled", $"Material In {voucher.VoucherNumber}", cancellationLinks));

            await stockPosting.ReverseVoucherPostingsAsync(
                db,
                voucher.Id,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["MaterialInConsumption"] = "MaterialInCancellationConsumption",
                    ["MaterialInFinishedGoods"] = "MaterialInCancellationFinishedGoods"
                },
                "MaterialInCancellation",
                user,
                now,
                cancellationToken);

            lifecycle.MarkCancelled(voucher, reason, user, now);
            db.AuditLogs.Add(VoucherLifecycleService.NewAudit(
                companyContext.CompanyId,
                voucher.Id,
                "Cancel",
                true,
                $"Cancelled Material In '{voucher.VoucherNumber}'. Reason: {voucher.CancellationReason}",
                user,
                now));
            await db.SaveChangesAsync(cancellationToken);
            var jwoStatusChanged = await JobWorkOrderStatusUpdater.UpdateAsync(db, jwoId, now, user, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await fullAudit.RecordAsync(
                db, voucher.Id, VoucherAuditActions.Cancel,
                voucher.CancellationReason, user, now, cancellationToken);
            if (jwoStatusChanged)
                await fullAudit.RecordAsync(
                    db, jwoId, VoucherAuditActions.SystemStatusUpdate,
                    $"Status recalculated after Material In {voucher.VoucherNumber} was cancelled.",
                    user, now, cancellationToken);
            await stockPosting.EnsureNegativeStockPolicyAsync(
                db, companyContext.CompanyId, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return OperationResult.Ok($"Material In {voucher.VoucherNumber} cancelled successfully.", voucher.Id);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return OperationResult.Fail(VoucherErrorMessages.For(ex));
        }
    }

    public async Task<OperationResult> DeleteAsync(long voucherId, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.CancelOrDeleteVouchers, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var user = companyContext.Actor;
        try
        {
            var voucher = await db.Vouchers
                .Include(x => x.VoucherType).ThenInclude(x => x.ParentVoucherType)
                .SingleOrDefaultAsync(x => x.Id == voucherId && x.CompanyId == companyContext.CompanyId &&
                    x.FinancialYearId == companyContext.FinancialYearId, cancellationToken);
            if (voucher is null) return OperationResult.Fail("The selected Material In voucher no longer exists.");
            await periodControl.EnsurePostingDateIsOpenAsync(
                db, companyContext.CompanyId, voucher.VoucherDate,
                $"Material In {voucher.VoucherNumber}", cancellationToken);
            var systemType = string.IsNullOrWhiteSpace(voucher.VoucherType.SystemTypeCode)
                ? voucher.VoucherType.ParentVoucherType?.SystemTypeCode : voucher.VoucherType.SystemTypeCode;
            if (!string.Equals(systemType, "MATERIAL_IN", StringComparison.OrdinalIgnoreCase))
                return OperationResult.Fail("The selected voucher is not a Material In voucher.");

            var deletionLinks = await lifecycle.GetActiveLinkDescriptionsAsync(
                db, companyContext.CompanyId, voucher.Id, VoucherLinkScope.Downstream, cancellationToken);
            if (deletionLinks.Count > 0)
                return OperationResult.Fail(VoucherLifecycleService.BuildBlockedMessage("deleted", $"Material In {voucher.VoucherNumber}", deletionLinks));

            var number = voucher.VoucherNumber;
            var jwoId = await db.MaterialInDetails.AsNoTracking()
                .Where(x => x.VoucherId == voucherId)
                .Select(x => x.JwoVoucherId)
                .SingleAsync(cancellationToken);
            await fullAudit.RecordAsync(
                db, voucher.Id, VoucherAuditActions.Delete,
                "Deleted Material In and reversed its stock effect.",
                user, now, cancellationToken);
            stockPosting.RemoveVoucherPostings(db, voucherId);
            db.MaterialInMaterialOutAllocations.RemoveRange(await db.MaterialInMaterialOutAllocations.Where(x => x.ConsumptionLine.VoucherId == voucherId).ToListAsync(cancellationToken));
            db.MaterialInFinishedGoodAllocations.RemoveRange(await db.MaterialInFinishedGoodAllocations.Where(x => x.FinishedGoodLine.VoucherId == voucherId).ToListAsync(cancellationToken));
            db.MaterialInConsumptions.RemoveRange(await db.MaterialInConsumptions.Where(x => x.VoucherId == voucherId).ToListAsync(cancellationToken));
            db.MaterialInFinishedGoods.RemoveRange(await db.MaterialInFinishedGoods.Where(x => x.VoucherId == voucherId).ToListAsync(cancellationToken));
            var detail = await db.MaterialInDetails.SingleOrDefaultAsync(x => x.VoucherId == voucherId, cancellationToken);
            if (detail is not null) db.MaterialInDetails.Remove(detail);
            db.VoucherLinks.RemoveRange(await db.VoucherLinks.Where(x => x.SourceVoucherId == voucherId || x.TargetVoucherId == voucherId).ToListAsync(cancellationToken));
            db.Vouchers.Remove(voucher);
            db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyContext.CompanyId, EntityType = "Voucher", EntityId = voucherId,
                Action = "Delete", Success = true, Description = $"Deleted Material In '{number}'.",
                PerformedBy = user, PerformedAtUtc = now
            });
            await db.SaveChangesAsync(cancellationToken);
            var jwoStatusChanged = await JobWorkOrderStatusUpdater.UpdateAsync(db, jwoId, now, user, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (jwoStatusChanged)
                await fullAudit.RecordAsync(
                    db, jwoId, VoucherAuditActions.SystemStatusUpdate,
                    $"Status recalculated after Material In {number} was deleted.",
                    user, now, cancellationToken);
            await stockPosting.EnsureNegativeStockPolicyAsync(
                db, companyContext.CompanyId, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return OperationResult.Ok($"Material In {number} deleted successfully.", voucherId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return OperationResult.Fail(VoucherErrorMessages.For(ex));
        }
    }

    private static string FormatAutomaticNumber(VoucherType type, int sequence)
    {
        var numeric = type.NumberWidth > 0 ? sequence.ToString($"D{type.NumberWidth}") : sequence.ToString();
        return type.NumberingMode == "AutoPrefixSuffix" ? $"{type.Prefix}{numeric}{type.Suffix}" : numeric;
    }
    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
