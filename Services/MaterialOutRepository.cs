using System.Data;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Domain;
using TexTrack.Web.Data;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed class MaterialOutRepository(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext,
    VoucherLifecycleService lifecycle,
    StockPostingService stockPosting,
    VoucherSequenceAllocator? sequenceAllocator = null,
    DeveloperAccessService? developerAccess = null,
    VoucherAuditHistoryService? voucherAuditHistory = null,
    InventoryPeriodControlService? inventoryPeriodControl = null)
{
    private const string JwoTypeCode = "JOB_WORK_OUT_ORDER";
    private const string MaterialOutTypeCode = "MATERIAL_OUT";
    private readonly VoucherAuditHistoryService fullAudit = voucherAuditHistory ?? new(contextFactory);
    private readonly InventoryPeriodControlService periodControl = inventoryPeriodControl ?? new();


    public async Task<IReadOnlyList<MaterialOutListItem>> GetListAsync(
        string searchText = "",
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Vouchers.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId &&
                        x.FinancialYearId == companyContext.FinancialYearId &&
                        (x.VoucherType.SystemTypeCode == MaterialOutTypeCode ||
                         (x.VoucherType.ParentVoucherType != null && x.VoucherType.ParentVoucherType.SystemTypeCode == MaterialOutTypeCode)));

        var term = searchText.Trim();
        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = $"%{term}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.VoucherNumber, pattern) ||
                EF.Functions.ILike(x.ReferenceNumber, pattern) ||
                EF.Functions.ILike(x.Batch, pattern) ||
                (x.PartyLedger != null && EF.Functions.ILike(x.PartyLedger.Name, pattern)) ||
                (x.MaterialOutDetail != null && EF.Functions.ILike(x.MaterialOutDetail.DisplayedOrderNumber, pattern)));
        }

        return await query
            .OrderByDescending(x => x.VoucherDate)
            .ThenByDescending(x => x.SequenceNumber)
            .Select(x => new MaterialOutListItem
            {
                Id = x.Id,
                VoucherNumber = x.VoucherNumber,
                VoucherTypeName = x.VoucherType.Name,
                VoucherDate = x.VoucherDate,
                ReferenceNumber = x.ReferenceNumber,
                Batch = x.Batch,
                JobWorkerName = x.PartyLedger != null ? x.PartyLedger.Name : string.Empty,
                DisplayedOrderNumber = x.MaterialOutDetail != null ? x.MaterialOutDetail.DisplayedOrderNumber : string.Empty,
                DestinationGodownName = x.MaterialOutDetail != null ? x.MaterialOutDetail.DestinationGodown.Name : string.Empty,
                TotalQuantity = x.MaterialOutLines.Sum(y => (decimal?)y.IssuedQuantity) ?? 0,
                TotalAmount = x.MaterialOutLines.Sum(y => (decimal?)y.Amount) ?? 0,
                Status = x.Status
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<MaterialOutVoucherDefaults> GetEntryDefaultsAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var type = await db.VoucherTypes.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive &&
                        (x.SystemTypeCode == MaterialOutTypeCode ||
                         (x.ParentVoucherType != null && x.ParentVoucherType.SystemTypeCode == MaterialOutTypeCode)))
            .OrderByDescending(x => !x.IsSystem)
            .ThenBy(x => x.Name)
            .FirstAsync(cancellationToken);

        var max = await db.Vouchers.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId &&
                        x.FinancialYearId == companyContext.FinancialYearId &&
                        x.VoucherTypeId == type.Id)
            .Select(x => (int?)x.SequenceNumber)
            .MaxAsync(cancellationToken);
        var next = Math.Max(type.StartingNumber, (max ?? type.StartingNumber - 1) + 1);
        var number = type.NumberingMode == "Manual"
            ? string.Empty
            : type.NumberWidth > 0 ? next.ToString($"D{type.NumberWidth}") : next.ToString();
        if (type.NumberingMode == "AutoPrefixSuffix") number = $"{type.Prefix}{number}{type.Suffix}";

        return new MaterialOutVoucherDefaults
        {
            VoucherTypeId = type.Id,
            VoucherTypeName = type.Name,
            VoucherNumber = number,
            ReferenceNumber = number,
            VoucherDate = DateOnly.FromDateTime(DateTime.Today),
            NumberingMode = type.NumberingMode
        };
    }

    public async Task<MaterialOutLookupData> GetLookupDataAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return new MaterialOutLookupData
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
                    DefaultMaterialOutDestinationGodownName = x.DefaultMaterialOutDestinationGodown == null
                        ? string.Empty
                        : x.DefaultMaterialOutDestinationGodown.Name,
                    DefaultMaterialInConsumptionGodownId = x.DefaultMaterialInConsumptionGodownId,
                    DefaultMaterialInConsumptionGodownName = x.DefaultMaterialInConsumptionGodown == null
                        ? string.Empty
                        : x.DefaultMaterialInConsumptionGodown.Name
                })
                .ToListAsync(cancellationToken),
            Godowns = await db.Godowns.AsNoTracking()
                .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new JobWorkGodownLookup { Id = x.Id, Name = x.Name })
                .ToListAsync(cancellationToken)
        };
    }

    public async Task<IReadOnlyList<MaterialOutPendingOrder>> GetPendingOrdersAsync(
        long jobWorkerLedgerId,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var selectedJobWorkerName = await db.Ledgers.AsNoTracking()
            .Where(x => x.Id == jobWorkerLedgerId && x.CompanyId == companyContext.CompanyId && x.IsActive && x.IsJobWorker)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken);
        if (selectedJobWorkerName is null) return Array.Empty<MaterialOutPendingOrder>();

        var orders = await db.Vouchers.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId &&
                        x.FinancialYearId == companyContext.FinancialYearId &&
                        (x.PartyLedgerId == jobWorkerLedgerId ||
                         x.JobWorkFinishedGoods.Any(f => f.BomStages.Any(s => s.AssignedJobWorkerId == jobWorkerLedgerId))) &&
                        x.Status != "Cancelled" &&
                        (x.VoucherType.SystemTypeCode == JwoTypeCode ||
                         (x.VoucherType.ParentVoucherType != null && x.VoucherType.ParentVoucherType.SystemTypeCode == JwoTypeCode)))
            .Include(x => x.PartyLedger)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.StockItem)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.FinishedGoodsGodown)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.DestinationGodown)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.Components).ThenInclude(x => x.StockItem)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.Components).ThenInclude(x => x.Uqc)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.Components).ThenInclude(x => x.ComponentGodown)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.Components).ThenInclude(x => x.BomStage)
            .AsSplitQuery()
            .OrderBy(x => x.VoucherDate).ThenBy(x => x.SequenceNumber)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(x => x.Id).ToList();
        var issuedByComponent = orderIds.Count == 0
            ? new Dictionary<long, decimal>()
            : await db.MaterialOutLines.AsNoTracking()
                .Where(x => orderIds.Contains(x.JwoVoucherId) && x.Voucher.Status != "Cancelled")
                .GroupBy(x => x.JwoComponentId)
                .Select(x => new { ComponentId = x.Key, Quantity = x.Sum(y => y.IssuedQuantity) })
                .ToDictionaryAsync(x => x.ComponentId, x => x.Quantity, cancellationToken);
        var componentIds = orders.SelectMany(x => x.JobWorkFinishedGoods)
            .SelectMany(x => x.Components).Select(x => x.Id).Distinct().ToList();
        var producedValuations = await GetProducedComponentValuationsAsync(
            db, componentIds, null, cancellationToken);

        var result = new List<MaterialOutPendingOrder>();
        foreach (var order in orders)
        {
            var mapped = new MaterialOutPendingOrder
            {
                JwoVoucherId = order.Id,
                VoucherNumber = order.VoucherNumber,
                VoucherDate = order.VoucherDate,
                Batch = order.Batch,
                ReferenceNumber = order.ReferenceNumber,
                JobWorkerLedgerId = jobWorkerLedgerId,
                JobWorkerName = selectedJobWorkerName,
                DueDate = order.DueDate
            };

            foreach (var fg in order.JobWorkFinishedGoods.OrderBy(x => x.LineNumber))
            {
                var mappedFg = new MaterialOutPendingFinishedGood
                {
                    JwoFinishedGoodId = fg.Id,
                    FinishedGoodName = fg.StockItem.Name,
                    OrderedQuantity = fg.OrderedQuantity,
                    FinishedGoodsGodownName = fg.FinishedGoodsGodown?.Name ?? string.Empty,
                    DestinationGodownName = fg.DestinationGodown?.Name ?? string.Empty
                };
                foreach (var component in fg.Components.OrderBy(x => x.LineNumber))
                {
                    var effectiveJobWorkerId = component.BomStage?.AssignedJobWorkerId ?? order.PartyLedgerId;
                    if (effectiveJobWorkerId != jobWorkerLedgerId) continue;
                    issuedByComponent.TryGetValue(component.Id, out var issued);
                    producedValuations.TryGetValue(component.Id, out var producedValuation);
                    var line = new MaterialOutPendingComponent
                    {
                        JwoComponentId = component.Id,
                        StockItemId = component.StockItemId,
                        StockItemVariantId = component.ComponentVariantId,
                        StockItemName = component.StockItem.Name,
                        UqcId = component.UqcId,
                        UqcShortName = component.Uqc.ShortName,
                        DecimalPlaces = component.Uqc.DecimalPlaces,
                        SourceGodownId = component.ComponentGodownId,
                        SourceGodownName = component.ComponentGodown?.Name ?? string.Empty,
                        RequiredQuantity = component.RequiredQuantity,
                        IssuedQuantity = issued,
                        Rate = producedValuation?.Rate ?? component.XmlRate,
                        IsProducedComponent = component.ChildBomStageId is not null
                    };
                    if (line.PendingQuantity > 0) mappedFg.Components.Add(line);
                }
                if (mappedFg.Components.Count > 0) mapped.FinishedGoods.Add(mappedFg);
            }
            if (mapped.FinishedGoods.Count > 0) result.Add(mapped);
        }
        return result;
    }

    public async Task<MaterialOutSaveResult> SaveAsync(
        MaterialOutSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.OperateVouchers, cancellationToken);
        ArgumentNullException.ThrowIfNull(request);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var user = companyContext.Actor;

        try
        {
            await periodControl.EnsurePostingDateIsOpenAsync(
                db, companyContext.CompanyId, request.VoucherDate, "Material Out", cancellationToken);
            var financialYear = await db.FinancialYears.AsNoTracking()
                .SingleAsync(x => x.Id == companyContext.FinancialYearId && x.CompanyId == companyContext.CompanyId, cancellationToken);
            if (request.VoucherDate < financialYear.StartDate || request.VoucherDate > financialYear.EndDate)
                throw new InvalidOperationException($"Voucher Date must be within financial year {financialYear.Name}.");

            var voucherType = await db.VoucherTypes
                .SingleAsync(x => x.Id == request.VoucherTypeId && x.CompanyId == companyContext.CompanyId && x.IsActive, cancellationToken);
            var isMaterialOut = voucherType.SystemTypeCode == MaterialOutTypeCode ||
                await db.VoucherTypes.AsNoTracking().AnyAsync(x => x.Id == voucherType.ParentVoucherTypeId && x.SystemTypeCode == MaterialOutTypeCode, cancellationToken);
            if (!isMaterialOut) throw new InvalidOperationException("Selected Voucher Type is not a Material Out type.");

            var jobWorker = await db.Ledgers.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == request.JobWorkerLedgerId && x.CompanyId == companyContext.CompanyId && x.IsActive && x.IsJobWorker, cancellationToken)
                ?? throw new InvalidOperationException("Select a valid active Party A/c Name.");
            _ = jobWorker;

            var destinationGodown = await db.Godowns.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == request.DestinationGodownId && x.CompanyId == companyContext.CompanyId && x.IsActive, cancellationToken)
                ?? throw new InvalidOperationException("Select a valid active Jobber Destination Godown.");
            _ = destinationGodown;

            var jwo = await db.Vouchers.AsNoTracking()
                .Where(x => x.Id == request.JwoVoucherId && x.CompanyId == companyContext.CompanyId &&
                            x.FinancialYearId == companyContext.FinancialYearId && x.Status != "Cancelled")
                .Select(x => new { x.Id, x.PartyLedgerId, x.VoucherDate })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("The selected JWO is unavailable or cancelled.");
            if (request.VoucherDate < jwo.VoucherDate)
                throw new InvalidOperationException($"Material Out date cannot be earlier than JWO {jwo.Id}'s date {jwo.VoucherDate:dd-MMM-yyyy}.");
            if (request.Lines.Count == 0) throw new InvalidOperationException("Enter at least one Issue Quantity.");
            var requestedLines = request.Lines.Where(x => x.IssuedQuantity > 0).ToList();
            if (requestedLines.Count == 0) throw new InvalidOperationException("Enter at least one Issue Quantity greater than zero.");
            if (requestedLines.GroupBy(x => x.JwoComponentId).Any(x => x.Count() > 1))
                throw new InvalidOperationException("The same JWO component cannot be issued twice in one voucher.");

            var componentIds = requestedLines.Select(x => x.JwoComponentId).ToList();
            var components = await db.JobWorkOrderComponents.AsNoTracking()
                .Where(x => componentIds.Contains(x.Id))
                .Select(x => new
                {
                    x.Id, x.FinishedGoodId, x.StockItemId, x.UqcId, x.RequiredQuantity,
                    x.ComponentGodownId, x.BomStageId, x.ChildBomStageId, x.ComponentVariantId,
                    AssignedJobWorkerId = x.BomStage != null ? x.BomStage.AssignedJobWorkerId : null,
                    StageAssignmentId = x.BomStage == null ? null : x.BomStage.AssignmentHistory
                        .Where(a => a.Status == "Active").Select(a => (long?)a.Id).SingleOrDefault(),
                    VoucherId = x.FinishedGood.VoucherId,
                    JwoJobWorkerId = x.FinishedGood.Voucher.PartyLedgerId
                })
                .ToDictionaryAsync(x => x.Id, cancellationToken);
            if (components.Count != requestedLines.Count)
                throw new InvalidOperationException("One or more selected JWO component rows no longer exist.");
            if (components.Values.Any(x => (x.AssignedJobWorkerId ?? x.JwoJobWorkerId) != request.JobWorkerLedgerId))
                throw new InvalidOperationException("The selected JWO components do not belong to the selected stage Job Worker.");

            var alreadyIssued = await db.MaterialOutLines.AsNoTracking()
                .Where(x => componentIds.Contains(x.JwoComponentId) && x.Voucher.Status != "Cancelled")
                .GroupBy(x => x.JwoComponentId)
                .Select(x => new { x.Key, Quantity = x.Sum(y => y.IssuedQuantity) })
                .ToDictionaryAsync(x => x.Key, x => x.Quantity, cancellationToken);
            var producedValuations = await GetProducedComponentValuationsAsync(
                db, componentIds, null, cancellationToken);

            foreach (var line in requestedLines)
            {
                var component = components[line.JwoComponentId];
                if (component.VoucherId != request.JwoVoucherId || component.FinishedGoodId != line.JwoFinishedGoodId)
                    throw new InvalidOperationException("A selected component does not belong to the selected JWO/Finished Good.");
                if (component.StockItemId != line.StockItemId || component.UqcId != line.UqcId)
                    throw new InvalidOperationException("A selected component master link has changed. Reload the JWO.");
                if (component.ComponentGodownId is null || component.ComponentGodownId.Value != line.SourceGodownId)
                    throw new InvalidOperationException("A selected component Material Source Godown has changed. Reload the JWO.");
                alreadyIssued.TryGetValue(line.JwoComponentId, out var prior);
                if (line.IssuedQuantity <= 0 || prior + line.IssuedQuantity > component.RequiredQuantity)
                    throw new InvalidOperationException("Issue Quantity cannot exceed the current pending JWO component quantity.");
                if (component.ChildBomStageId is not null)
                {
                    if (!producedValuations.TryGetValue(line.JwoComponentId, out var valuation) ||
                        prior + line.IssuedQuantity > valuation.ReceivedQuantity)
                        throw new InvalidOperationException("This produced component is still pending from its earlier job-work stage. Receive that stage before issuing it onward.");
                    if (valuation.RemainingValue < 0)
                        throw new InvalidOperationException("The remaining produced-component value is negative. Review its earlier stage receipts and onward issues before continuing.");
                    line.Rate = valuation.Rate;
                }
                if (line.Rate < 0) throw new InvalidOperationException("Rate cannot be negative.");
            }

            var sequenceNumber = await (sequenceAllocator ?? new VoucherSequenceAllocator(companyContext))
                .ReserveAsync(db, voucherType, user, now, cancellationToken);
            var voucherNumber = voucherType.NumberingMode == "Manual"
                ? request.VoucherNumber.Trim()
                : FormatAutomaticNumber(voucherType, sequenceNumber);
            if (string.IsNullOrWhiteSpace(voucherNumber)) throw new InvalidOperationException("Voucher Number is required.");
            var normalizedNumber = Normalize(voucherNumber);
            if (await db.Vouchers.AsNoTracking().AnyAsync(x => x.CompanyId == companyContext.CompanyId &&
                    x.FinancialYearId == companyContext.FinancialYearId && x.VoucherTypeId == voucherType.Id &&
                    x.VoucherNumberNormalized == normalizedNumber, cancellationToken))
                throw new InvalidOperationException($"Voucher Number '{voucherNumber}' already exists.");

            var voucher = new Voucher
            {
                CompanyId = companyContext.CompanyId,
                FinancialYearId = companyContext.FinancialYearId,
                VoucherTypeId = voucherType.Id,
                SequenceNumber = sequenceNumber,
                VoucherNumber = voucherNumber,
                VoucherNumberNormalized = normalizedNumber,
                VoucherDate = request.VoucherDate,
                ReferenceNumber = request.ReferenceNumber.Trim(),
                Batch = request.Batch.Trim(),
                PartyLedgerId = request.JobWorkerLedgerId,
                Narration = request.Narration.Trim(),
                Status = "Open",
                CreatedAtUtc = now,
                ModifiedAtUtc = now,
                CreatedBy = user,
                ModifiedBy = user,
                ConcurrencyToken = Guid.NewGuid().ToString("N")
            };
            db.Vouchers.Add(voucher);
            await db.SaveChangesAsync(cancellationToken);

            var detail = MapDetails(request, voucher.Id);
            db.MaterialOutDetails.Add(detail);

            var lineNumber = 0;
            foreach (var input in requestedLines)
            {
                lineNumber++;
                var component = components[input.JwoComponentId];
                alreadyIssued.TryGetValue(input.JwoComponentId, out var prior);
                var amount = decimal.Round(input.IssuedQuantity * input.Rate, 4, MidpointRounding.AwayFromZero);
                var line = new MaterialOutLine
                {
                    VoucherId = voucher.Id,
                    JwoVoucherId = request.JwoVoucherId,
                    JwoFinishedGoodId = input.JwoFinishedGoodId,
                    JwoComponentId = input.JwoComponentId,
                    BomStageId = component.BomStageId,
                    StageAssignmentId = component.StageAssignmentId,
                    LineNumber = lineNumber,
                    StockItemId = input.StockItemId,
                    UqcId = input.UqcId,
                    SourceGodownId = input.SourceGodownId,
                    DestinationGodownId = request.DestinationGodownId,
                    RequiredQuantity = component.RequiredQuantity,
                    PreviouslyIssuedQuantity = prior,
                    IssuedQuantity = input.IssuedQuantity,
                    Rate = input.Rate,
                    Amount = amount
                };
                db.MaterialOutLines.Add(line);
                await db.SaveChangesAsync(cancellationToken);

                AddPostedMovements(db, companyContext.CompanyId, companyContext.FinancialYearId,
                    voucher.Id, line, component.ComponentVariantId, request.VoucherDate, user, now);
            }

            db.VoucherLinks.Add(new VoucherLink
            {
                CompanyId = companyContext.CompanyId,
                SourceVoucherId = request.JwoVoucherId,
                TargetVoucherId = voucher.Id,
                LinkType = "JWO_TO_MATERIAL_OUT",
                CreatedAtUtc = now,
                CreatedBy = user
            });
            db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyContext.CompanyId,
                EntityType = "Voucher",
                EntityId = voucher.Id,
                Action = "Create",
                Success = true,
                Description = $"Created Material Out {voucherNumber} linked to JWO {request.DisplayedOrderNumber} with {requestedLines.Count} issued component line(s).",
                PerformedBy = user,
                PerformedAtUtc = now
            });
            await db.SaveChangesAsync(cancellationToken);
            var jwoStatusChanged = await JobWorkOrderStatusUpdater.UpdateAsync(db, request.JwoVoucherId, now, user, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await fullAudit.RecordAsync(
                db, voucher.Id, VoucherAuditActions.Create,
                "Initial Material Out save.", user, now, cancellationToken);
            if (jwoStatusChanged)
                await fullAudit.RecordAsync(
                    db, request.JwoVoucherId, VoucherAuditActions.SystemStatusUpdate,
                    $"Status recalculated after Material Out {voucher.VoucherNumber} was created.",
                    user, now, cancellationToken);
            await stockPosting.EnsureNegativeStockPolicyAsync(
                db, companyContext.CompanyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new MaterialOutSaveResult { VoucherId = voucher.Id, VoucherNumber = voucherNumber };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static string FormatAutomaticNumber(VoucherType type, int sequence)
    {
        var number = type.NumberWidth > 0 ? sequence.ToString($"D{type.NumberWidth}") : sequence.ToString();
        return type.NumberingMode == "AutoPrefixSuffix" ? $"{type.Prefix}{number}{type.Suffix}" : number;
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static MaterialOutDetail MapDetails(MaterialOutSaveRequest request, long voucherId)
    {
        var e = request.EwayDetails ?? new MaterialOutEwayDetailsInput();
        return new MaterialOutDetail
        {
            VoucherId = voucherId,
            ProvideGstEwayDetails = request.ProvideGstEwayDetails,
            DestinationGodownId = request.DestinationGodownId,
            DisplayedOrderNumber = request.DisplayedOrderNumber.Trim(),
            EwayBillNumber = e.EwayBillNumber.Trim(),
            EwayBillDate = e.EwayBillDate,
            ConsolidatedEwayBillNumber = e.ConsolidatedEwayBillNumber.Trim(),
            ConsolidatedEwayBillDate = e.ConsolidatedEwayBillDate,
            EwaySubType = e.EwaySubType.Trim(),
            EwayDocumentType = e.EwayDocumentType.Trim(),
            ConsignorMailingName = e.ConsignorMailingName.Trim(),
            ConsignorGstin = e.ConsignorGstin.Trim(),
            ConsignorState = e.ConsignorState.Trim(),
            ConsignorAddress1 = e.ConsignorAddress1.Trim(),
            ConsignorAddress2 = e.ConsignorAddress2.Trim(),
            ConsignorPincode = e.ConsignorPincode.Trim(),
            ConsignorPlace = e.ConsignorPlace.Trim(),
            ConsignorActualState = e.ConsignorActualState.Trim(),
            ConsigneeMailingName = e.ConsigneeMailingName.Trim(),
            ConsigneeGstin = e.ConsigneeGstin.Trim(),
            ConsigneeState = e.ConsigneeState.Trim(),
            ConsigneeAddress1 = e.ConsigneeAddress1.Trim(),
            ConsigneeAddress2 = e.ConsigneeAddress2.Trim(),
            ConsigneePincode = e.ConsigneePincode.Trim(),
            ConsigneePlace = e.ConsigneePlace.Trim(),
            ConsigneeActualState = e.ConsigneeActualState.Trim(),
            PinToPinDistance = e.PinToPinDistance.Trim(),
            TransporterName = e.TransporterName.Trim(),
            TransporterId = e.TransporterId.Trim(),
            TransportMode = e.TransportMode.Trim(),
            TransportDocumentNumber = e.TransportDocumentNumber.Trim(),
            TransportDocumentDate = e.TransportDocumentDate,
            VehicleNumber = e.VehicleNumber.Trim(),
            VehicleType = e.VehicleType.Trim()
        };
    }

    public async Task<MaterialOutEditData?> GetForEditAsync(long voucherId, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var voucher = await db.Vouchers.AsNoTracking()
            .Include(x => x.VoucherType)
            .Include(x => x.PartyLedger)
            .Include(x => x.MaterialOutDetail!).ThenInclude(x => x.DestinationGodown)
            .Include(x => x.MaterialOutLines).ThenInclude(x => x.StockItem)
            .Include(x => x.MaterialOutLines).ThenInclude(x => x.Uqc)
            .Include(x => x.MaterialOutLines).ThenInclude(x => x.SourceGodown)
            .Include(x => x.MaterialOutLines).ThenInclude(x => x.JwoFinishedGood).ThenInclude(x => x.StockItem)
            .Include(x => x.MaterialOutLines).ThenInclude(x => x.JwoFinishedGood).ThenInclude(x => x.FinishedGoodsGodown)
            .SingleOrDefaultAsync(x => x.Id == voucherId &&
                x.CompanyId == companyContext.CompanyId &&
                x.FinancialYearId == companyContext.FinancialYearId &&
                (x.VoucherType.SystemTypeCode == MaterialOutTypeCode ||
                 (x.VoucherType.ParentVoucherType != null && x.VoucherType.ParentVoucherType.SystemTypeCode == MaterialOutTypeCode)),
                cancellationToken);
        if (voucher is null || voucher.MaterialOutDetail is null || voucher.MaterialOutLines.Count == 0) return null;

        var jwoId = voucher.MaterialOutLines.First().JwoVoucherId;
        var componentIds = voucher.MaterialOutLines.Select(x => x.JwoComponentId).Distinct().ToList();
        var otherIssued = await db.MaterialOutLines.AsNoTracking()
            .Where(x => componentIds.Contains(x.JwoComponentId) && x.VoucherId != voucherId && x.Voucher.Status != "Cancelled")
            .GroupBy(x => x.JwoComponentId)
            .Select(x => new { x.Key, Quantity = x.Sum(y => y.IssuedQuantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity, cancellationToken);
        var componentDetails = await db.JobWorkOrderComponents.AsNoTracking()
            .Where(x => componentIds.Contains(x.Id))
            .Select(x => new { x.Id, x.ChildBomStageId, x.ComponentVariantId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var producedComponentIds = componentDetails.Values
            .Where(x => x.ChildBomStageId != null)
            .Select(x => x.Id)
            .ToHashSet();

        var jwo = await db.Vouchers.AsNoTracking()
            .Include(x => x.PartyLedger)
            .SingleAsync(x => x.Id == jwoId, cancellationToken);

        var order = new MaterialOutPendingOrder
        {
            JwoVoucherId = jwo.Id,
            VoucherNumber = jwo.VoucherNumber,
            VoucherDate = jwo.VoucherDate,
            Batch = jwo.Batch,
            ReferenceNumber = jwo.ReferenceNumber,
            JobWorkerLedgerId = jwo.PartyLedgerId ?? 0,
            JobWorkerName = jwo.PartyLedger?.Name ?? string.Empty,
            DueDate = jwo.DueDate
        };

        foreach (var fgGroup in voucher.MaterialOutLines.OrderBy(x => x.LineNumber).GroupBy(x => x.JwoFinishedGoodId))
        {
            var first = fgGroup.First();
            var fg = new MaterialOutPendingFinishedGood
            {
                JwoFinishedGoodId = first.JwoFinishedGoodId,
                FinishedGoodName = first.JwoFinishedGood.StockItem.Name,
                OrderedQuantity = first.JwoFinishedGood.OrderedQuantity,
                FinishedGoodsGodownName = first.JwoFinishedGood.FinishedGoodsGodown?.Name ?? string.Empty,
                DestinationGodownName = voucher.MaterialOutDetail.DestinationGodown.Name
            };
            foreach (var line in fgGroup)
            {
                otherIssued.TryGetValue(line.JwoComponentId, out var issuedElsewhere);
                fg.Components.Add(new MaterialOutPendingComponent
                {
                    JwoComponentId = line.JwoComponentId,
                    StockItemId = line.StockItemId,
                    StockItemVariantId = componentDetails.TryGetValue(line.JwoComponentId, out var detail) ? detail.ComponentVariantId : null,
                    StockItemName = line.StockItem.Name,
                    UqcId = line.UqcId,
                    UqcShortName = line.Uqc.ShortName,
                    DecimalPlaces = line.Uqc.DecimalPlaces,
                    SourceGodownId = line.SourceGodownId,
                    SourceGodownName = line.SourceGodown.Name,
                    RequiredQuantity = line.RequiredQuantity,
                    IssuedQuantity = issuedElsewhere,
                    IssueNowQuantity = line.IssuedQuantity,
                    Rate = line.Rate,
                    IsProducedComponent = producedComponentIds.Contains(line.JwoComponentId)
                });
            }
            order.FinishedGoods.Add(fg);
        }

        return new MaterialOutEditData
        {
            VoucherId = voucher.Id,
            ConcurrencyToken = voucher.ConcurrencyToken,
            Status = voucher.Status,
            Defaults = new MaterialOutVoucherDefaults
            {
                VoucherTypeId = voucher.VoucherTypeId,
                VoucherTypeName = voucher.VoucherType.Name,
                VoucherNumber = voucher.VoucherNumber,
                ReferenceNumber = voucher.ReferenceNumber,
                VoucherDate = voucher.VoucherDate,
                NumberingMode = voucher.VoucherType.NumberingMode
            },
            JobWorkerLedgerId = voucher.PartyLedgerId ?? 0,
            JobWorkerName = voucher.PartyLedger?.Name ?? string.Empty,
            DestinationGodownId = voucher.MaterialOutDetail.DestinationGodownId,
            DestinationGodownName = voucher.MaterialOutDetail.DestinationGodown.Name,
            Batch = voucher.Batch,
            Narration = voucher.Narration,
            Order = order,
            ProvideGstEwayDetails = voucher.MaterialOutDetail.ProvideGstEwayDetails,
            EwayDetails = MapDetailsToInput(voucher.MaterialOutDetail)
        };
    }

    public async Task<MaterialOutSaveResult> UpdateAsync(MaterialOutSaveRequest request, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.OperateVouchers, cancellationToken);
        if (request.VoucherId <= 0) throw new InvalidOperationException("Material Out voucher identity is missing.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var user = companyContext.Actor;
        try
        {
            var voucher = await db.Vouchers
                .Include(x => x.MaterialOutDetail)
                .Include(x => x.MaterialOutLines)
                .SingleOrDefaultAsync(x => x.Id == request.VoucherId &&
                    x.CompanyId == companyContext.CompanyId &&
                    x.FinancialYearId == companyContext.FinancialYearId, cancellationToken)
                ?? throw new InvalidOperationException("The selected Material Out voucher no longer exists.");
            if (voucher.Status == "Cancelled" && developerAccess?.IsDeveloper != true) throw new InvalidOperationException("A cancelled Material Out voucher cannot be altered.");
            await periodControl.EnsureVoucherMutationIsOpenAsync(
                db, companyContext.CompanyId, voucher.VoucherDate, request.VoucherDate,
                $"Material Out {voucher.VoucherNumber}", cancellationToken);
            if (!string.Equals(voucher.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
                throw new InvalidOperationException("This Material Out voucher was changed by another user. Reload it and try again.");
            var alterationLinks = await lifecycle.GetActiveLinkDescriptionsAsync(
                db, companyContext.CompanyId, voucher.Id, VoucherLinkScope.Downstream, cancellationToken);
            if (alterationLinks.Count > 0)
                throw new InvalidOperationException(VoucherLifecycleService.BuildBlockedMessage("altered", $"Material Out {voucher.VoucherNumber}", alterationLinks));

            var existingJwoId = voucher.MaterialOutLines.Select(x => x.JwoVoucherId).Distinct().Single();
            if (request.JwoVoucherId != existingJwoId)
                throw new InvalidOperationException("The internal JWO link cannot be changed during alteration.");
            await ValidateUpdateRequestAsync(db, request, voucher.Id, cancellationToken);

            stockPosting.RemoveVoucherPostings(db, voucher.Id);
            db.MaterialOutLines.RemoveRange(voucher.MaterialOutLines);
            if (voucher.MaterialOutDetail is not null) db.MaterialOutDetails.Remove(voucher.MaterialOutDetail);
            await db.SaveChangesAsync(cancellationToken);

            voucher.VoucherDate = request.VoucherDate;
            voucher.ReferenceNumber = request.ReferenceNumber.Trim();
            voucher.Batch = request.Batch.Trim();
            voucher.PartyLedgerId = request.JobWorkerLedgerId;
            voucher.Narration = request.Narration.Trim();
            voucher.ModifiedAtUtc = now;
            voucher.ModifiedBy = user;
            voucher.ConcurrencyToken = Guid.NewGuid().ToString("N");
            db.MaterialOutDetails.Add(MapDetails(request, voucher.Id));

            var componentIds = request.Lines.Where(x => x.IssuedQuantity > 0).Select(x => x.JwoComponentId).Distinct().ToList();
            var components = await db.JobWorkOrderComponents.AsNoTracking()
                .Where(x => componentIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
            var prior = await db.MaterialOutLines.AsNoTracking()
                .Where(x => componentIds.Contains(x.JwoComponentId) && x.VoucherId != voucher.Id && x.Voucher.Status != "Cancelled")
                .GroupBy(x => x.JwoComponentId)
                .Select(x => new { x.Key, Qty = x.Sum(y => y.IssuedQuantity) })
                .ToDictionaryAsync(x => x.Key, x => x.Qty, cancellationToken);
            var stageIds = components.Values.Where(x => x.BomStageId != null).Select(x => x.BomStageId!.Value).Distinct().ToList();
            var assignmentByStage = await db.JobWorkOrderStageAssignments.AsNoTracking()
                .Where(x => stageIds.Contains(x.BomStageId) && x.Status == "Active")
                .ToDictionaryAsync(x => x.BomStageId, x => x.Id, cancellationToken);

            var lineNo = 0;
            foreach (var input in request.Lines.Where(x => x.IssuedQuantity > 0))
            {
                lineNo++;
                prior.TryGetValue(input.JwoComponentId, out var previouslyIssued);
                var component = components[input.JwoComponentId];
                var amount = decimal.Round(input.IssuedQuantity * input.Rate, 4, MidpointRounding.AwayFromZero);
                var line = new MaterialOutLine
                {
                    VoucherId = voucher.Id,
                    JwoVoucherId = request.JwoVoucherId,
                    JwoFinishedGoodId = input.JwoFinishedGoodId,
                    JwoComponentId = input.JwoComponentId,
                    BomStageId = component.BomStageId,
                    StageAssignmentId = component.BomStageId is long stageId ? assignmentByStage.GetValueOrDefault(stageId) : null,
                    LineNumber = lineNo,
                    StockItemId = input.StockItemId,
                    UqcId = input.UqcId,
                    SourceGodownId = input.SourceGodownId,
                    DestinationGodownId = request.DestinationGodownId,
                    RequiredQuantity = component.RequiredQuantity,
                    PreviouslyIssuedQuantity = previouslyIssued,
                    IssuedQuantity = input.IssuedQuantity,
                    Rate = input.Rate,
                    Amount = amount
                };
                db.MaterialOutLines.Add(line);
                await db.SaveChangesAsync(cancellationToken);
                AddPostedMovements(db, companyContext.CompanyId, companyContext.FinancialYearId, voucher.Id,
                    line, component.ComponentVariantId, request.VoucherDate, user, now);
            }

            db.AuditLogs.Add(NewAudit(voucher.Id, "Update",
                $"Updated Material Out '{voucher.VoucherNumber}' with {lineNo} issued component line(s).", user, now));
            await db.SaveChangesAsync(cancellationToken);
            var jwoStatusChanged = await JobWorkOrderStatusUpdater.UpdateAsync(db, existingJwoId, now, user, cancellationToken);
            await TallySyncStateTracker.MarkUpdatedNotExportedAsync(db, voucher.Id, user, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await fullAudit.RecordAsync(
                db, voucher.Id, VoucherAuditActions.Update,
                "Material Out altered.", user, now, cancellationToken);
            if (jwoStatusChanged)
                await fullAudit.RecordAsync(
                    db, existingJwoId, VoucherAuditActions.SystemStatusUpdate,
                    $"Status recalculated after Material Out {voucher.VoucherNumber} was altered.",
                    user, now, cancellationToken);
            await stockPosting.EnsureNegativeStockPolicyAsync(
                db, companyContext.CompanyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new MaterialOutSaveResult { VoucherId = voucher.Id, VoucherNumber = voucher.VoucherNumber };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<OperationResult> CancelAsync(long voucherId, string reason, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.CancelOrDeleteVouchers, cancellationToken);
        var reasonValidation = VoucherLifecycleService.ValidateCancellationReason(reason);
        if (reasonValidation is not null) return OperationResult.Fail(reasonValidation);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var user = companyContext.Actor;
        try
        {
            var voucher = await db.Vouchers
                .Include(x => x.MaterialOutLines)
                .SingleOrDefaultAsync(x => x.Id == voucherId &&
                    x.CompanyId == companyContext.CompanyId &&
                    x.FinancialYearId == companyContext.FinancialYearId, cancellationToken);
            if (voucher is null) return OperationResult.Fail("The selected Material Out voucher no longer exists.");
            if (voucher.Status == "Cancelled") return OperationResult.Fail("This Material Out voucher is already cancelled.");
            await periodControl.EnsurePostingDateIsOpenAsync(
                db, companyContext.CompanyId, voucher.VoucherDate,
                $"Material Out {voucher.VoucherNumber}", cancellationToken);
            var consumingMaterialInNumber = await db.MaterialInMaterialOutAllocations.AsNoTracking()
                .Where(x => x.MaterialOutLine.VoucherId == voucher.Id && x.ConsumptionLine.Voucher.Status != "Cancelled")
                .OrderBy(x => x.ConsumptionLine.Voucher.VoucherDate)
                .ThenBy(x => x.ConsumptionLine.Voucher.SequenceNumber)
                .Select(x => x.ConsumptionLine.Voucher.VoucherNumber)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(consumingMaterialInNumber))
                return OperationResult.Fail($"Material Out {voucher.VoucherNumber} cannot be cancelled because Material In {consumingMaterialInNumber} consumes its issued material.");
            var cancellationLinks = await lifecycle.GetActiveLinkDescriptionsAsync(
                db, companyContext.CompanyId, voucher.Id, VoucherLinkScope.Downstream, cancellationToken);
            if (cancellationLinks.Count > 0)
                return OperationResult.Fail(VoucherLifecycleService.BuildBlockedMessage("cancelled", $"Material Out {voucher.VoucherNumber}", cancellationLinks));

            var jwoId = voucher.MaterialOutLines.Select(x => x.JwoVoucherId).Distinct().Single();
            await stockPosting.ReverseVoucherPostingsAsync(
                db,
                voucher.Id,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["MaterialOutSource"] = "MaterialOutCancellationSource",
                    ["MaterialOutDestination"] = "MaterialOutCancellationDestination"
                },
                "MaterialOutCancellation",
                user,
                now,
                cancellationToken);

            lifecycle.MarkCancelled(voucher, reason, user, now);
            db.AuditLogs.Add(VoucherLifecycleService.NewAudit(
                companyContext.CompanyId,
                voucher.Id,
                "Cancel",
                true,
                $"Cancelled Material Out '{voucher.VoucherNumber}'. Reason: {voucher.CancellationReason}",
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
                    $"Status recalculated after Material Out {voucher.VoucherNumber} was cancelled.",
                    user, now, cancellationToken);
            await stockPosting.EnsureNegativeStockPolicyAsync(
                db, companyContext.CompanyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"Material Out {voucher.VoucherNumber} cancelled successfully.", voucher.Id);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail("The database could not complete cancellation. Ask the administrator to investigate before retrying.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail(VoucherErrorMessages.For(ex));
        }
    }

    public async Task<OperationResult> DeleteAsync(long voucherId, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.CancelOrDeleteVouchers, cancellationToken);
        var user = companyContext.Actor;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var voucher = await db.Vouchers
                .Include(x => x.MaterialOutLines)
                .SingleOrDefaultAsync(x => x.Id == voucherId &&
                    x.CompanyId == companyContext.CompanyId &&
                    x.FinancialYearId == companyContext.FinancialYearId, cancellationToken);
            if (voucher is null) return OperationResult.Fail("The selected Material Out voucher no longer exists.");
            await periodControl.EnsurePostingDateIsOpenAsync(
                db, companyContext.CompanyId, voucher.VoucherDate,
                $"Material Out {voucher.VoucherNumber}", cancellationToken);
            if (voucher.Status == "Cancelled" && developerAccess?.IsDeveloper != true) return OperationResult.Fail("A cancelled voucher cannot be deleted. It must remain for audit.");
            var linkedMaterialInNumber = await db.MaterialInMaterialOutAllocations.AsNoTracking()
                .Where(x => x.MaterialOutLine.VoucherId == voucher.Id && x.ConsumptionLine.Voucher.Status != "Cancelled")
                .OrderBy(x => x.ConsumptionLine.Voucher.VoucherDate).ThenBy(x => x.ConsumptionLine.Voucher.SequenceNumber)
                .Select(x => x.ConsumptionLine.Voucher.VoucherNumber).FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(linkedMaterialInNumber))
                return OperationResult.Fail($"Material Out {voucher.VoucherNumber} cannot be deleted because Material In {linkedMaterialInNumber} consumes its issued material.");
            var deletionLinks = await lifecycle.GetActiveLinkDescriptionsAsync(
                db, companyContext.CompanyId, voucher.Id, VoucherLinkScope.Downstream, cancellationToken);
            if (deletionLinks.Count > 0)
                return OperationResult.Fail(VoucherLifecycleService.BuildBlockedMessage("deleted", $"Material Out {voucher.VoucherNumber}", deletionLinks));

            var jwoId = voucher.MaterialOutLines.Select(x => x.JwoVoucherId).Distinct().Single();
            var voucherNumber = voucher.VoucherNumber;
            var now = DateTimeOffset.UtcNow;
            await fullAudit.RecordAsync(
                db, voucher.Id, VoucherAuditActions.Delete,
                "Deleted unlinked Material Out and reversed its stock effect.",
                user, now, cancellationToken);
            stockPosting.RemoveVoucherPostings(db, voucher.Id);
            var links = await db.VoucherLinks.Where(x => x.SourceVoucherId == voucher.Id || x.TargetVoucherId == voucher.Id).ToListAsync(cancellationToken);
            db.VoucherLinks.RemoveRange(links);
            db.Vouchers.Remove(voucher);
            db.AuditLogs.Add(NewAudit(voucher.Id, "Delete",
                $"Deleted unlinked Material Out '{voucherNumber}' and reversed its stock effect.", user, now));
            await db.SaveChangesAsync(cancellationToken);
            var jwoStatusChanged = await JobWorkOrderStatusUpdater.UpdateAsync(db, jwoId, now, user, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (jwoStatusChanged)
                await fullAudit.RecordAsync(
                    db, jwoId, VoucherAuditActions.SystemStatusUpdate,
                    $"Status recalculated after Material Out {voucherNumber} was deleted.",
                    user, now, cancellationToken);
            await stockPosting.EnsureNegativeStockPolicyAsync(
                db, companyContext.CompanyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"Material Out {voucherNumber} deleted successfully.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail(VoucherErrorMessages.For(ex));
        }
    }

    private static async Task ValidateUpdateRequestAsync(TexTrackDbContext db, MaterialOutSaveRequest request, long currentVoucherId, CancellationToken cancellationToken)
    {
        if (request.JobWorkerLedgerId <= 0) throw new InvalidOperationException("Select a valid Party A/c Name.");
        if (request.DestinationGodownId <= 0) throw new InvalidOperationException("Select a valid Jobber Destination Godown.");
        var activeLines = request.Lines.Where(x => x.IssuedQuantity > 0).ToList();
        if (activeLines.Count == 0) throw new InvalidOperationException("Enter at least one Issue Quantity greater than zero.");
        var ids = activeLines.Select(x => x.JwoComponentId).Distinct().ToList();
        if (ids.Count != activeLines.Count) throw new InvalidOperationException("The same JWO component cannot be issued twice.");
        var components = await db.JobWorkOrderComponents.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new
            {
                x.Id, x.RequiredQuantity, x.StockItemId, x.UqcId, x.ComponentGodownId, x.FinishedGoodId, x.ChildBomStageId,
                JwoId = x.FinishedGood.VoucherId,
                AssignedJobWorkerId = x.BomStage != null ? x.BomStage.AssignedJobWorkerId : null,
                JwoJobWorkerId = x.FinishedGood.Voucher.PartyLedgerId
            })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var issued = await db.MaterialOutLines.AsNoTracking()
            .Where(x => ids.Contains(x.JwoComponentId) && x.VoucherId != currentVoucherId && x.Voucher.Status != "Cancelled")
            .GroupBy(x => x.JwoComponentId)
            .Select(x => new { x.Key, Qty = x.Sum(y => y.IssuedQuantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, cancellationToken);
        var producedValuations = await GetProducedComponentValuationsAsync(
            db, ids, currentVoucherId, cancellationToken);
        if (components.Values.Any(x => (x.AssignedJobWorkerId ?? x.JwoJobWorkerId) != request.JobWorkerLedgerId))
            throw new InvalidOperationException("The selected JWO components do not belong to the selected stage Job Worker.");
        foreach (var line in activeLines)
        {
            if (!components.TryGetValue(line.JwoComponentId, out var component))
                throw new InvalidOperationException("A selected JWO component no longer exists.");
            if (component.JwoId != request.JwoVoucherId || component.FinishedGoodId != line.JwoFinishedGoodId ||
                component.StockItemId != line.StockItemId || component.UqcId != line.UqcId ||
                component.ComponentGodownId != line.SourceGodownId)
                throw new InvalidOperationException("A selected JWO component link changed. Reload the voucher.");
            issued.TryGetValue(line.JwoComponentId, out var other);
            if (line.IssuedQuantity <= 0 || other + line.IssuedQuantity > component.RequiredQuantity)
                throw new InvalidOperationException("Issue Quantity cannot exceed the current pending JWO quantity.");
            if (component.ChildBomStageId is not null)
            {
                if (!producedValuations.TryGetValue(line.JwoComponentId, out var valuation) ||
                    other + line.IssuedQuantity > valuation.ReceivedQuantity)
                    throw new InvalidOperationException("This produced component is still pending from its earlier job-work stage. Receive that stage before issuing it onward.");
                if (valuation.RemainingValue < 0)
                    throw new InvalidOperationException("The remaining produced-component value is negative. Review its earlier stage receipts and onward issues before continuing.");
                line.Rate = valuation.Rate;
            }
            if (line.Rate < 0) throw new InvalidOperationException("Rate cannot be negative.");
        }
    }

    private static async Task<Dictionary<long, ProducedComponentValuation>> GetProducedComponentValuationsAsync(
        TexTrackDbContext db,
        IReadOnlyCollection<long> componentIds,
        long? excludedMaterialOutVoucherId,
        CancellationToken cancellationToken)
    {
        if (componentIds.Count == 0) return new Dictionary<long, ProducedComponentValuation>();

        var producedComponents = await db.JobWorkOrderComponents.AsNoTracking()
            .Where(x => componentIds.Contains(x.Id) && x.ChildBomStageId != null)
            .Select(x => new { x.Id, ChildBomStageId = x.ChildBomStageId!.Value })
            .ToListAsync(cancellationToken);
        if (producedComponents.Count == 0) return new Dictionary<long, ProducedComponentValuation>();

        var childStageIds = producedComponents.Select(x => x.ChildBomStageId).Distinct().ToList();
        var receipts = await db.MaterialInFinishedGoods.AsNoTracking()
            .Where(x => x.BomStageId != null && childStageIds.Contains(x.BomStageId.Value) && x.Voucher.Status != "Cancelled")
            .GroupBy(x => x.BomStageId!.Value)
            .Select(x => new
            {
                StageId = x.Key,
                Quantity = x.Sum(y => y.ReceivedQuantity),
                Value = x.Sum(y => y.FinishedGoodsValue)
            })
            .ToDictionaryAsync(x => x.StageId, cancellationToken);

        var producedComponentIds = producedComponents.Select(x => x.Id).ToList();
        var issuesQuery = db.MaterialOutLines.AsNoTracking()
            .Where(x => producedComponentIds.Contains(x.JwoComponentId) && x.Voucher.Status != "Cancelled");
        if (excludedMaterialOutVoucherId is long voucherId)
            issuesQuery = issuesQuery.Where(x => x.VoucherId != voucherId);
        var issues = await issuesQuery
            .GroupBy(x => x.JwoComponentId)
            .Select(x => new
            {
                ComponentId = x.Key,
                Quantity = x.Sum(y => y.IssuedQuantity),
                Value = x.Sum(y => y.Amount)
            })
            .ToDictionaryAsync(x => x.ComponentId, cancellationToken);

        var result = new Dictionary<long, ProducedComponentValuation>();
        foreach (var component in producedComponents)
        {
            receipts.TryGetValue(component.ChildBomStageId, out var receipt);
            issues.TryGetValue(component.Id, out var issue);
            var receivedQuantity = receipt?.Quantity ?? 0;
            var receivedValue = receipt?.Value ?? 0;
            var issuedQuantity = issue?.Quantity ?? 0;
            var issuedValue = issue?.Value ?? 0;
            result[component.Id] = new ProducedComponentValuation(
                receivedQuantity, receivedValue, issuedQuantity, issuedValue);
        }

        return result;
    }

    private sealed record ProducedComponentValuation(
        decimal ReceivedQuantity,
        decimal ReceivedValue,
        decimal IssuedQuantity,
        decimal IssuedValue)
    {
        public decimal RemainingQuantity => ReceivedQuantity - IssuedQuantity;
        public decimal RemainingValue => ReceivedValue - IssuedValue;
        public decimal Rate => RemainingQuantity <= 0
            ? 0
            : decimal.Round(RemainingValue / RemainingQuantity, 4, MidpointRounding.AwayFromZero);
    }

    private void AddPostedMovements(
        TexTrackDbContext db,
        long companyId,
        long financialYearId,
        long voucherId,
        MaterialOutLine line,
        long? stockItemVariantId,
        DateOnly date,
        string user,
        DateTimeOffset now)
    {
        stockPosting.Post(db, new StockMovementDraft(
            companyId, financialYearId, voucherId, date, line.StockItemId, line.UqcId,
            line.SourceGodownId, -line.IssuedQuantity, line.Rate, -line.Amount,
            "MaterialOutSource", MaterialOutLineId: line.Id,
            StockItemVariantId: stockItemVariantId), user, now);
        stockPosting.Post(db, new StockMovementDraft(
            companyId, financialYearId, voucherId, date, line.StockItemId, line.UqcId,
            line.DestinationGodownId, line.IssuedQuantity, line.Rate, line.Amount,
            "MaterialOutDestination", MaterialOutLineId: line.Id,
            StockItemVariantId: stockItemVariantId), user, now);
    }

    private static MaterialOutEwayDetailsInput MapDetailsToInput(MaterialOutDetail d) => new()
    {
        EwayBillNumber = d.EwayBillNumber,
        EwayBillDate = d.EwayBillDate,
        ConsolidatedEwayBillNumber = d.ConsolidatedEwayBillNumber,
        ConsolidatedEwayBillDate = d.ConsolidatedEwayBillDate,
        EwaySubType = d.EwaySubType,
        EwayDocumentType = d.EwayDocumentType,
        ConsignorMailingName = d.ConsignorMailingName,
        ConsignorGstin = d.ConsignorGstin,
        ConsignorState = d.ConsignorState,
        ConsignorAddress1 = d.ConsignorAddress1,
        ConsignorAddress2 = d.ConsignorAddress2,
        ConsignorPincode = d.ConsignorPincode,
        ConsignorPlace = d.ConsignorPlace,
        ConsignorActualState = d.ConsignorActualState,
        ConsigneeMailingName = d.ConsigneeMailingName,
        ConsigneeGstin = d.ConsigneeGstin,
        ConsigneeState = d.ConsigneeState,
        ConsigneeAddress1 = d.ConsigneeAddress1,
        ConsigneeAddress2 = d.ConsigneeAddress2,
        ConsigneePincode = d.ConsigneePincode,
        ConsigneePlace = d.ConsigneePlace,
        ConsigneeActualState = d.ConsigneeActualState,
        PinToPinDistance = d.PinToPinDistance,
        TransporterName = d.TransporterName,
        TransporterId = d.TransporterId,
        TransportMode = d.TransportMode,
        TransportDocumentNumber = d.TransportDocumentNumber,
        TransportDocumentDate = d.TransportDocumentDate,
        VehicleNumber = d.VehicleNumber,
        VehicleType = d.VehicleType
    };

    private AuditLog NewAudit(long entityId, string action, string description, string user, DateTimeOffset now) => new()
    {
        CompanyId = companyContext.CompanyId,
        EntityType = "Voucher",
        EntityId = entityId,
        Action = action,
        Success = true,
        Description = description,
        PerformedBy = user,
        PerformedAtUtc = now
    };

}
