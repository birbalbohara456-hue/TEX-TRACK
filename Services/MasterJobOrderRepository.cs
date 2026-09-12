using System.Data;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed class MasterJobOrderRepository(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext,
    VoucherLifecycleService lifecycle,
    VoucherSequenceAllocator sequenceAllocator,
    VoucherAuditHistoryService? voucherAuditHistory = null)
{
    private const string VoucherTypeCode = "MASTER_JOB_ORDER";
    private readonly VoucherAuditHistoryService fullAudit = voucherAuditHistory ?? new(contextFactory);

    public async Task<IReadOnlyList<MasterJobOrderListItem>> GetListAsync(string? searchText = null, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Vouchers.AsNoTracking().Where(x =>
            x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId &&
            (x.VoucherType.SystemTypeCode == VoucherTypeCode ||
             x.VoucherType.ParentVoucherType != null && x.VoucherType.ParentVoucherType.SystemTypeCode == VoucherTypeCode));
        var search = (searchText ?? string.Empty).Trim();
        if (search.Length > 0)
            query = query.Where(x => EF.Functions.ILike(x.VoucherNumber, $"%{search}%") ||
                                     EF.Functions.ILike(x.ReferenceNumber, $"%{search}%") ||
                                     x.PartyLedger != null && EF.Functions.ILike(x.PartyLedger.Name, $"%{search}%") ||
                                     x.MasterJobOrderFinishedGoods.Any(f => EF.Functions.ILike(f.StockItem.Name, $"%{search}%")));
        var rows = await query.Include(x => x.PartyLedger)
            .Include(x => x.MasterJobOrderFinishedGoods).ThenInclude(x => x.StockItem)
            .Include(x => x.MasterJobOrderFinishedGoods).ThenInclude(x => x.Allocations).ThenInclude(x => x.Colour)
            .OrderByDescending(x => x.VoucherDate).ThenByDescending(x => x.SequenceNumber)
            .AsSplitQuery().ToListAsync(cancellationToken);
        return rows.Select(x => new MasterJobOrderListItem
        {
            Id = x.Id, VoucherNumber = x.VoucherNumber, VoucherDate = x.VoucherDate,
            PartyName = x.PartyLedger?.Name ?? string.Empty, ReferenceNumber = x.ReferenceNumber,
            DueDate = x.DueDate, TotalQuantity = x.MasterJobOrderFinishedGoods.Sum(f => f.OrderedQuantity),
            Status = x.Status, ConcurrencyToken = x.ConcurrencyToken,
            FinishedGoodsSummary = string.Join(", ", x.MasterJobOrderFinishedGoods.OrderBy(f => f.LineNumber)
                .Select(f => $"{f.StockItem.Name} · {string.Join("/", f.Allocations.Select(a => a.Colour?.Name ?? "N/A").Distinct())}"))
        }).ToList();
    }

    public async Task<MasterJobOrderLookupData> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var types = await db.VoucherTypes.AsNoTracking().Include(x => x.ParentVoucherType)
            .Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive &&
                        (x.SystemTypeCode == VoucherTypeCode || x.ParentVoucherType != null && x.ParentVoucherType.SystemTypeCode == VoucherTypeCode))
            .OrderByDescending(x => x.IsSystem).ThenBy(x => x.Name).ToListAsync(cancellationToken);
        var items = await db.StockItems.AsNoTracking().Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive)
            .Include(x => x.StockGroup).Include(x => x.Uqc).Include(x => x.Colours).ThenInclude(x => x.Colour)
            .Include(x => x.Sizes).ThenInclude(x => x.Size).Include(x => x.Variants).AsSplitQuery().OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var jobOrders = await db.Vouchers.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId &&
                        x.Status != "Cancelled" &&
                        (x.VoucherType.SystemTypeCode == "JOB_WORK_OUT_ORDER" ||
                         x.VoucherType.ParentVoucherType != null && x.VoucherType.ParentVoucherType.SystemTypeCode == "JOB_WORK_OUT_ORDER"))
            .Include(x => x.PartyLedger)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.StockItem).ThenInclude(x => x.Uqc)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.Colour)
            .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.SizeAllocations).ThenInclude(x => x.Size)
            .OrderByDescending(x => x.VoucherDate).ThenByDescending(x => x.SequenceNumber)
            .AsSplitQuery().ToListAsync(cancellationToken);
        var typeLookups = new List<JobWorkVoucherTypeLookup>();
        foreach (var type in types)
        {
            var next = await GetNextSequenceAsync(db, type.Id, cancellationToken);
            typeLookups.Add(new JobWorkVoucherTypeLookup
            {
                Id = type.Id, Name = type.Name, NumberingMode = type.NumberingMode,
                AllowManualNumbering = type.NumberingMode == "Manual", Prefix = type.Prefix, Suffix = type.Suffix,
                NumberWidth = type.NumberWidth, StartingNumber = type.StartingNumber, NextSequence = next,
                NextVoucherNumber = FormatVoucherNumber(type, next), IsSystem = type.IsSystem
            });
        }
        return new MasterJobOrderLookupData
        {
            AllowManualNumbering = types.Any(x => x.IsSystem && x.NumberingMode == "Manual"),
            VoucherTypes = typeLookups,
            Parties = await db.Ledgers.AsNoTracking().Where(x => x.CompanyId == companyContext.CompanyId && x.IsActive)
                .OrderBy(x => x.Name).Select(x => new LookupItem(x.Id, x.Name, x.LedgerGroup.Name)).ToListAsync(cancellationToken),
            FinishedGoods = items.Where(x => x.StockGroup.RootClassification == "FinishedGoods").Select(MapStockItem).ToList(),
            JobWorkOrders = jobOrders.Select(MapLinkedJobOrder).ToList()
        };
    }

    public async Task<MasterJobOrderEditModel> CreateNewModelAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var type = await db.VoucherTypes.AsNoTracking().SingleAsync(x => x.CompanyId == companyContext.CompanyId && x.SystemTypeCode == VoucherTypeCode, cancellationToken);
        var sequence = await GetNextSequenceAsync(db, type.Id, cancellationToken);
        var fy = await db.FinancialYears.AsNoTracking().SingleAsync(x => x.Id == companyContext.FinancialYearId, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var date = today < fy.StartDate ? fy.StartDate : today > fy.EndDate ? fy.EndDate : today;
        return new MasterJobOrderEditModel
        {
            SequenceNumber = sequence, VoucherTypeId = type.Id, VoucherTypeText = type.Name,
            VoucherNumber = FormatVoucherNumber(type, sequence), VoucherDate = date, DueDate = date
        };
    }

    public async Task<MasterJobOrderEditModel?> GetForEditAsync(long id, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Vouchers.AsNoTracking().Where(x => x.Id == id && x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId && x.VoucherType.SystemTypeCode == VoucherTypeCode)
            .Include(x => x.VoucherType).Include(x => x.PartyLedger)
            .Include(x => x.MasterJobOrderFinishedGoods).ThenInclude(x => x.StockItem).ThenInclude(x => x.Uqc)
            .Include(x => x.MasterJobOrderFinishedGoods).ThenInclude(x => x.Allocations).ThenInclude(x => x.Colour)
            .Include(x => x.MasterJobOrderFinishedGoods).ThenInclude(x => x.Allocations).ThenInclude(x => x.Size)
            .Include(x => x.LinkedJobWorkOrders).ThenInclude(x => x.PartyLedger)
            .Include(x => x.LinkedJobWorkOrders).ThenInclude(x => x.JobWorkFinishedGoods).ThenInclude(x => x.StockItem).ThenInclude(x => x.Uqc)
            .Include(x => x.LinkedJobWorkOrders).ThenInclude(x => x.JobWorkFinishedGoods).ThenInclude(x => x.Colour)
            .Include(x => x.LinkedJobWorkOrders).ThenInclude(x => x.JobWorkFinishedGoods).ThenInclude(x => x.SizeAllocations).ThenInclude(x => x.Size)
            .AsSplitQuery().SingleOrDefaultAsync(cancellationToken);
        if (entity is null) return null;
        return new MasterJobOrderEditModel
        {
            Id = entity.Id, SequenceNumber = entity.SequenceNumber, VoucherTypeId = entity.VoucherTypeId,
            VoucherTypeText = entity.VoucherType.Name, VoucherNumber = entity.VoucherNumber, VoucherDate = entity.VoucherDate,
            PartyLedgerId = entity.PartyLedgerId, PartyText = entity.PartyLedger?.Name ?? string.Empty,
            ReferenceNumber = entity.ReferenceNumber, DueDate = entity.DueDate, Narration = entity.Narration,
            Status = entity.Status, ConcurrencyToken = entity.ConcurrencyToken,
            LinkedJobOrderIds = entity.LinkedJobWorkOrders.Select(x => x.Id).ToList(),
            LinkedJobOrders = entity.LinkedJobWorkOrders.OrderBy(x => x.VoucherDate).ThenBy(x => x.SequenceNumber).Select(MapLinkedJobOrder).ToList(),
            FinishedGoods = entity.MasterJobOrderFinishedGoods.OrderBy(x => x.LineNumber).Select(f => new MasterJobOrderFinishedGoodEditModel
            {
                Id = f.Id, StockItemId = f.StockItemId, StockItemText = f.StockItem.Name, UqcShortName = f.StockItem.Uqc.ShortName,
                Colours = f.Allocations.GroupBy(a => new { a.ColourId, Name = a.Colour == null ? string.Empty : a.Colour.Name })
                    .Select(g => new MasterJobOrderColourEditModel
                    {
                        ColourId = g.Key.ColourId, ColourText = g.Key.Name,
                        Sizes = g.OrderBy(a => a.Size == null ? 0 : a.Size.DisplayOrder).Select(a => new MasterJobOrderSizeEditModel
                        { Id = a.Id, StockItemVariantId = a.StockItemVariantId, SizeId = a.SizeId, SizeName = a.Size?.Name ?? "Quantity", DisplayOrder = a.Size?.DisplayOrder ?? 0, Quantity = a.Quantity }).ToList()
                    }).ToList()
            }).ToList()
        };
    }

    public async Task<OperationResult> SaveAsync(MasterJobOrderEditModel model, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.OperateVouchers, cancellationToken);
        Normalize(model);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var type = await db.VoucherTypes.Include(x => x.ParentVoucherType).SingleOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.Id == model.VoucherTypeId && x.IsActive && (x.SystemTypeCode == VoucherTypeCode || x.ParentVoucherType != null && x.ParentVoucherType.SystemTypeCode == VoucherTypeCode), cancellationToken);
            if (type is null) return await Fail(tx, "Master Job Order voucher type is unavailable.", cancellationToken);
            var fy = await db.FinancialYears.AsNoTracking().SingleAsync(x => x.Id == companyContext.FinancialYearId && x.CompanyId == companyContext.CompanyId, cancellationToken);
            if (model.VoucherDate < fy.StartDate || model.VoucherDate > fy.EndDate) return await Fail(tx, "Voucher Date is outside the active Financial Year.", cancellationToken);
            if (model.DueDate is not null && model.DueDate < model.VoucherDate) return await Fail(tx, "Due Date cannot be earlier than Voucher Date.", cancellationToken);
            if (model.PartyLedgerId is null || !await db.Ledgers.AnyAsync(x => x.Id == model.PartyLedgerId && x.CompanyId == companyContext.CompanyId && x.IsActive, cancellationToken)) return await Fail(tx, "Select an active Party A/c Name.", cancellationToken);
            var requestedJobOrderIds = model.LinkedJobOrderIds.Where(x => x > 0).Distinct().ToList();
            if (requestedJobOrderIds.Count == 0) return await Fail(tx, "Link at least one existing Job Work Out Order.", cancellationToken);
            var selectedJobOrders = await db.Vouchers
                .Where(x => requestedJobOrderIds.Contains(x.Id) && x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId &&
                            x.Status != "Cancelled" &&
                            (x.VoucherType.SystemTypeCode == "JOB_WORK_OUT_ORDER" || x.VoucherType.ParentVoucherType != null && x.VoucherType.ParentVoucherType.SystemTypeCode == "JOB_WORK_OUT_ORDER"))
                .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.StockItem).ThenInclude(x => x.Uqc)
                .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.Colour)
                .Include(x => x.JobWorkFinishedGoods).ThenInclude(x => x.SizeAllocations).ThenInclude(x => x.Size)
                .AsSplitQuery().ToListAsync(cancellationToken);
            if (selectedJobOrders.Count != requestedJobOrderIds.Count) return await Fail(tx, "One or more selected Job Work Out Orders are unavailable.", cancellationToken);
            if (selectedJobOrders.Any(x => x.MasterJobOrderId is not null && x.MasterJobOrderId != model.Id))
                return await Fail(tx, "A selected Job Work Out Order is already linked to another Master Job Order.", cancellationToken);
            model.FinishedGoods = BuildPlan(selectedJobOrders);
            var validation = await ValidateLines(db, model, cancellationToken);
            if (validation is not null) return await Fail(tx, validation, cancellationToken);

            Voucher entity;
            if (model.Id == 0)
            {
                var sequence = await sequenceAllocator.ReserveAsync(
                    db, type, companyContext.Actor, DateTimeOffset.UtcNow, cancellationToken);
                var number = type.NumberingMode == "Manual" ? model.VoucherNumber : FormatVoucherNumber(type, sequence);
                if (string.IsNullOrWhiteSpace(number)) return await Fail(tx, "Voucher Number is required.", cancellationToken);
                entity = new Voucher { CompanyId = companyContext.CompanyId, FinancialYearId = companyContext.FinancialYearId, VoucherTypeId = type.Id, SequenceNumber = sequence, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedBy = companyContext.Actor };
                db.Vouchers.Add(entity);
                entity.VoucherNumber = number.Trim(); entity.VoucherNumberNormalized = number.Trim().ToUpperInvariant();
            }
            else
            {
                entity = await db.Vouchers.Include(x => x.MasterJobOrderFinishedGoods).ThenInclude(x => x.Allocations)
                    .Include(x => x.LinkedJobWorkOrders)
                    .SingleOrDefaultAsync(x => x.Id == model.Id && x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId && x.VoucherType.SystemTypeCode == VoucherTypeCode, cancellationToken)
                    ?? throw new InvalidOperationException("The Master Job Order no longer exists.");
                if (!string.Equals(entity.ConcurrencyToken, model.ConcurrencyToken, StringComparison.Ordinal)) return await Fail(tx, "This Master Job Order was changed by another operation. Reopen it and try again.", cancellationToken);
                foreach (var removedLink in entity.LinkedJobWorkOrders.Where(x => !requestedJobOrderIds.Contains(x.Id)).ToList())
                    removedLink.MasterJobOrderId = null;
                db.MasterJobOrderAllocations.RemoveRange(entity.MasterJobOrderFinishedGoods.SelectMany(x => x.Allocations));
                db.MasterJobOrderFinishedGoods.RemoveRange(entity.MasterJobOrderFinishedGoods);
            }
            entity.VoucherDate = model.VoucherDate; entity.PartyLedgerId = model.PartyLedgerId;
            entity.ReferenceNumber = model.ReferenceNumber; entity.DueDate = model.DueDate; entity.Narration = model.Narration;
            entity.ModifiedAtUtc = DateTimeOffset.UtcNow; entity.ModifiedBy = companyContext.Actor; entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
            AddLines(entity, model);
            foreach (var jobOrder in selectedJobOrders) jobOrder.MasterJobOrder = entity;
            await db.SaveChangesAsync(cancellationToken);
            db.AuditLogs.Add(new AuditLog { CompanyId = companyContext.CompanyId, EntityType = "MasterJobOrder", EntityId = entity.Id, Action = model.Id == 0 ? "Create" : "Update", Success = true, Description = $"Saved Master Job Order '{entity.VoucherNumber}'.", PerformedBy = companyContext.Actor, PerformedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(cancellationToken);
            await fullAudit.RecordAsync(
                db, entity.Id,
                model.Id == 0 ? VoucherAuditActions.Create : VoucherAuditActions.Update,
                model.Id == 0 ? "Initial Master Job Order save." : "Master Job Order altered.",
                companyContext.Actor, entity.ModifiedAtUtc, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return OperationResult.Ok($"Master Job Order {entity.VoucherNumber} saved.", entity.Id);
        }
        catch (Exception ex) { await tx.RollbackAsync(cancellationToken); return OperationResult.Fail(VoucherErrorMessages.For(ex)); }
    }

    public async Task<OperationResult> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.CancelOrDeleteVouchers, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var entity = await db.Vouchers.Include(x => x.VoucherType).Include(x => x.LinkedJobWorkOrders)
                .SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId && x.VoucherType.SystemTypeCode == VoucherTypeCode, cancellationToken);
            if (entity is null) return await Fail(tx, "The selected Master Job Order no longer exists.", cancellationToken);
            foreach (var linkedOrder in entity.LinkedJobWorkOrders) linkedOrder.MasterJobOrderId = null;
            await fullAudit.RecordAsync(
                db, entity.Id, VoucherAuditActions.Delete,
                "Deleted Master Job Order after unlinking its grouped Job Work Orders.",
                companyContext.Actor, DateTimeOffset.UtcNow, cancellationToken);
            db.Vouchers.Remove(entity);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return OperationResult.Ok($"Master Job Order {entity.VoucherNumber} deleted.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return OperationResult.Fail(VoucherErrorMessages.For(ex));
        }
    }

    public async Task<OperationResult> CancelAsync(long id, string reason, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.CancelOrDeleteVouchers, cancellationToken);
        var validation = VoucherLifecycleService.ValidateCancellationReason(reason);
        if (validation is not null) return OperationResult.Fail(validation);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var entity = await db.Vouchers.Include(x => x.VoucherType).SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId && x.VoucherType.SystemTypeCode == VoucherTypeCode, cancellationToken);
        if (entity is null) return OperationResult.Fail("The selected Master Job Order no longer exists.");
        try
        {
            var now = DateTimeOffset.UtcNow;
            lifecycle.MarkCancelled(entity, reason, companyContext.Actor, now);
            db.AuditLogs.Add(VoucherLifecycleService.NewAudit(
                companyContext.CompanyId, entity.Id, "Cancel", true,
                $"Cancelled Master Job Order '{entity.VoucherNumber}'. Reason: {reason.Trim()}", companyContext.Actor, now));
            await db.SaveChangesAsync(cancellationToken);
            await fullAudit.RecordAsync(
                db, entity.Id, VoucherAuditActions.Cancel, entity.CancellationReason,
                companyContext.Actor, now, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return OperationResult.Ok($"Master Job Order {entity.VoucherNumber} cancelled.", entity.Id);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return OperationResult.Fail(VoucherErrorMessages.For(ex));
        }
    }

    private async Task<string?> ValidateLines(TexTrackDbContext db, MasterJobOrderEditModel model, CancellationToken ct)
    {
        if (model.FinishedGoods.Count == 0) return "Add at least one Finished Good.";
        var seenItems = new HashSet<long>();
        foreach (var line in model.FinishedGoods)
        {
            if (!seenItems.Add(line.StockItemId)) return "The same Finished Good cannot appear twice in one Master Job Order.";
            var item = await db.StockItems.AsNoTracking().Include(x => x.StockGroup).Include(x => x.Variants).SingleOrDefaultAsync(x => x.Id == line.StockItemId && x.CompanyId == companyContext.CompanyId && x.IsActive, ct);
            if (item is null || item.StockGroup.RootClassification != "FinishedGoods") return "Select an active Finished Goods Stock Item.";
            var positive = line.Colours.SelectMany(x => x.Sizes).Where(x => x.Quantity > 0).ToList();
            if (positive.Count == 0 || line.Colours.SelectMany(x => x.Sizes).Any(x => x.Quantity < 0)) return $"Enter valid positive colour/size quantities for '{line.StockItemText}'.";
            var variants = item.Variants.Where(x => x.IsActive).ToDictionary(x => x.Id);
            if (positive.Any(x => !variants.ContainsKey(x.StockItemVariantId))) return $"A colour/size variant is invalid for '{line.StockItemText}'.";
            if (positive.GroupBy(x => x.StockItemVariantId).Any(x => x.Count() > 1)) return $"A colour/size is repeated for '{line.StockItemText}'.";
        }
        return null;
    }

    private static void AddLines(Voucher entity, MasterJobOrderEditModel model)
    {
        var lineNumber = 1;
        foreach (var input in model.FinishedGoods)
        {
            var allocations = input.Colours.SelectMany(x => x.Sizes.Select(s => new { Colour = x, Size = s })).Where(x => x.Size.Quantity > 0).ToList();
            var line = new MasterJobOrderFinishedGood { LineNumber = lineNumber++, StockItemId = input.StockItemId, OrderedQuantity = allocations.Sum(x => x.Size.Quantity) };
            foreach (var allocation in allocations) line.Allocations.Add(new MasterJobOrderAllocation { StockItemVariantId = allocation.Size.StockItemVariantId, ColourId = allocation.Colour.ColourId, SizeId = allocation.Size.SizeId, Quantity = allocation.Size.Quantity });
            entity.MasterJobOrderFinishedGoods.Add(line);
        }
    }

    private static MasterJobOrderLinkedJwoModel MapLinkedJobOrder(Voucher voucher) => new()
    {
        Id = voucher.Id,
        MasterJobOrderId = voucher.MasterJobOrderId,
        VoucherNumber = voucher.VoucherNumber,
        VoucherDate = voucher.VoucherDate,
        JobWorkerName = voucher.PartyLedger?.Name ?? string.Empty,
        Batch = voucher.Batch,
        FinishedGoodsSummary = string.Join(", ", voucher.JobWorkFinishedGoods
            .OrderBy(x => x.LineNumber).Select(x => x.StockItem.Name).Distinct()),
        TotalQuantity = voucher.JobWorkFinishedGoods.Sum(x => x.OrderedQuantity),
        FinishedGoods = BuildPlan(new[] { voucher })
    };

    private static List<MasterJobOrderFinishedGoodEditModel> BuildPlan(IEnumerable<Voucher> jobOrders)
    {
        return jobOrders.SelectMany(x => x.JobWorkFinishedGoods)
            .GroupBy(x => x.StockItemId)
            .Select(itemGroup =>
            {
                var first = itemGroup.First();
                return new MasterJobOrderFinishedGoodEditModel
                {
                    StockItemId = first.StockItemId,
                    StockItemText = first.StockItem.Name,
                    UqcShortName = first.StockItem.Uqc.ShortName,
                    Colours = itemGroup.GroupBy(x => new { x.ColourId, Name = x.Colour == null ? string.Empty : x.Colour.Name })
                        .Select(colourGroup => new MasterJobOrderColourEditModel
                        {
                            ColourId = colourGroup.Key.ColourId,
                            ColourText = colourGroup.Key.Name,
                            Sizes = colourGroup.SelectMany(x => x.SizeAllocations)
                                .GroupBy(x => new { x.StockItemVariantId, x.SizeId, Name = x.Size == null ? "Quantity" : x.Size.Name, Order = x.Size == null ? 0 : x.Size.DisplayOrder })
                                .Select(sizeGroup => new MasterJobOrderSizeEditModel
                                {
                                    StockItemVariantId = sizeGroup.Key.StockItemVariantId,
                                    SizeId = sizeGroup.Key.SizeId,
                                    SizeName = sizeGroup.Key.Name,
                                    DisplayOrder = sizeGroup.Key.Order,
                                    Quantity = sizeGroup.Sum(x => x.Quantity)
                                }).OrderBy(x => x.DisplayOrder).ThenBy(x => x.SizeName).ToList()
                        }).OrderBy(x => x.ColourText).ToList()
                };
            }).OrderBy(x => x.StockItemText).ToList();
    }

    private static bool SamePlan(Voucher entity, MasterJobOrderEditModel model)
    {
        var persisted = entity.MasterJobOrderFinishedGoods.SelectMany(f => f.Allocations.Select(a => (f.StockItemId, a.StockItemVariantId, a.Quantity))).OrderBy(x => x.StockItemId).ThenBy(x => x.StockItemVariantId).ToList();
        var requested = model.FinishedGoods.SelectMany(f => f.Colours.SelectMany(c => c.Sizes.Where(s => s.Quantity > 0).Select(s => (f.StockItemId, s.StockItemVariantId, s.Quantity)))).OrderBy(x => x.StockItemId).ThenBy(x => x.StockItemVariantId).ToList();
        return persisted.SequenceEqual(requested);
    }

    private static void Normalize(MasterJobOrderEditModel model)
    {
        model.VoucherNumber = model.VoucherNumber.Trim(); model.ReferenceNumber = model.ReferenceNumber.Trim(); model.Narration = model.Narration.Trim();
        model.FinishedGoods = model.FinishedGoods.Where(x => x.StockItemId > 0 || x.Colours.SelectMany(c => c.Sizes).Any(s => s.Quantity != 0)).ToList();
    }

    private async Task<int> GetNextSequenceAsync(TexTrackDbContext db, long voucherTypeId, CancellationToken ct)
    {
        var start = await db.VoucherTypes.Where(x => x.Id == voucherTypeId).Select(x => x.StartingNumber).SingleAsync(ct);
        var max = await db.Vouchers.Where(x => x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId && x.VoucherTypeId == voucherTypeId).Select(x => (int?)x.SequenceNumber).MaxAsync(ct);
        return Math.Max(start, (max ?? start - 1) + 1);
    }
    private static string FormatVoucherNumber(VoucherType type, int sequence)
    {
        if (type.NumberingMode == "Manual") return string.Empty;
        var number = type.NumberWidth > 0 ? sequence.ToString($"D{type.NumberWidth}") : sequence.ToString();
        return type.NumberingMode == "AutoPrefixSuffix" ? $"{type.Prefix}{number}{type.Suffix}" : number;
    }
    private static JobWorkStockItemLookup MapStockItem(StockItem x)
    {
        var activeColourIds = x.Colours.Where(c => c.Colour.IsActive).Select(c => c.ColourId).ToHashSet();
        var activeSizeIds = x.Sizes.Where(s => s.Size.IsActive).Select(s => s.SizeId).ToHashSet();
        return new JobWorkStockItemLookup
        {
            Id = x.Id, Name = x.Name, UqcId = x.UqcId, UqcShortName = x.Uqc.ShortName, RootClassification = x.StockGroup.RootClassification,
            Colours = x.Colours.Where(c => c.Colour.IsActive).Select(c => new JobWorkColourLookup { Id = c.ColourId, Name = c.Colour.Name }).OrderBy(c => c.Name).ToList(),
            Sizes = x.Sizes.Where(s => s.Size.IsActive).Select(s => new JobWorkSizeLookup { Id = s.SizeId, Name = s.Size.Name, DisplayOrder = s.Size.DisplayOrder }).OrderBy(s => s.DisplayOrder).ToList(),
            Variants = x.Variants.Where(v => v.IsActive &&
                    (v.ColourId == null || activeColourIds.Contains(v.ColourId.Value)) &&
                    (v.SizeId == null || activeSizeIds.Contains(v.SizeId.Value)))
                .Select(v => new JobWorkVariantLookup { Id = v.Id, ColourId = v.ColourId, SizeId = v.SizeId }).ToList()
        };
    }
    private static async Task<OperationResult> Fail(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, string message, CancellationToken ct)
    { await tx.RollbackAsync(ct); return OperationResult.Fail(message); }
}
