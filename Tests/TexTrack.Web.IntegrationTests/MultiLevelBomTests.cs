using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class MultiLevelBomTests
{
    [Fact]
    public async Task Reassigning_stage_jobber_preserves_stage_identity_and_appends_history()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        await environment.UseCanonicalJwoTypeCodeAsync();
        var now = DateTimeOffset.UtcNow;
        await using (var setup = environment.CreateDbContext())
        {
            setup.StockGroups.Single(x => x.Id == 1).RootClassification = "FinishedGoods";
            setup.LedgerGroups.Add(new LedgerGroup
            {
                Id = 10, CompanyId = 1, Name = "Job Workers", NameNormalized = "JOB WORKERS",
                RootClassification = "SundryCreditors", IsActive = true, CreatedAtUtc = now, ModifiedAtUtc = now
            });
            setup.Ledgers.AddRange(
                new Ledger { Id = 10, CompanyId = 1, LedgerGroupId = 10, Name = "Worker A", NameNormalized = "WORKER A", IsJobWorker = true, IsActive = true, CreatedAtUtc = now, ModifiedAtUtc = now },
                new Ledger { Id = 11, CompanyId = 1, LedgerGroupId = 10, Name = "Worker B", NameNormalized = "WORKER B", IsJobWorker = true, IsActive = true, CreatedAtUtc = now, ModifiedAtUtc = now });
            setup.Processes.Add(new ProcessMaster
            {
                Id = 10, CompanyId = 1, Name = "Stitching", NameNormalized = "STITCHING",
                IsActive = true, CreatedAtUtc = now, ModifiedAtUtc = now
            });
            setup.VoucherTypes.Add(new VoucherType
            {
                Id = 2, CompanyId = 1, Name = "Material Out", NameNormalized = "MATERIAL OUT",
                SystemTypeCode = "MATERIAL_OUT", Nature = "Inventory", PostingMode = "Actual",
                Abbreviation = "MO", NumberingMode = "Auto", ResetPeriod = "FinancialYear",
                TallyVoucherTypeName = "Material Out", IsActive = true, CreatedAtUtc = now, ModifiedAtUtc = now
            });
            setup.Vouchers.Remove(await setup.Vouchers.SingleAsync(x => x.Id == 1));
            await setup.SaveChangesAsync();
        }

        var bom = await environment.BillOfMaterialRepository.SaveAsync(new BillOfMaterialEditModel
        {
            StockItemId = 2, Name = "Stable Stage BOM", OutputQuantity = 1,
            Lines = [new() { ComponentStockItemId = 3, RequiredQuantity = 1 }]
        });
        Assert.True(bom.Success, bom.Message);
        var stages = await environment.JobWorkOrderRepository.BuildBomStagePreviewAsync(bom.EntityId!.Value, 10, 10, 1);
        foreach (var stage in stages)
        {
            stage.ProcessId = 10;
            stage.ExpectedCompletionDate = new DateOnly(2026, 8, 20);
            stage.ExpectedProcessRate = 2;
        }
        var created = await environment.JobWorkOrderRepository.SaveAsync(new JobWorkOrderEditModel
        {
            VoucherTypeId = 1,
            VoucherDate = new DateOnly(2026, 8, 15),
            DueDate = new DateOnly(2026, 8, 20),
            Batch = "REVISION-BATCH",
            JobWorkerLedgerId = null,
            FinishedGoods =
            [
                new JobWorkFinishedGoodEditModel
                {
                    StockItemId = 2,
                    FinishedGoodsGodownId = 1,
                    DestinationGodownId = 1,
                    BillOfMaterialId = bom.EntityId,
                    SizeQuantities = [new JobWorkSizeQuantityEditModel { StockItemVariantId = 2, Quantity = 10 }],
                    BomStages = stages
                }
            ]
        });
        Assert.True(created.Success, created.Message);

        long stageId;
        Guid stableKey;
        await using (var before = environment.CreateDbContext())
        {
            var stage = await before.JobWorkOrderBomStages.SingleAsync(x => x.VoucherId == created.EntityId);
            stageId = stage.Id;
            stableKey = stage.StableKey;
            var component = await before.JobWorkOrderComponents.SingleAsync(x => x.BomStageId == stage.Id);
            var assignment = await before.JobWorkOrderStageAssignments.SingleAsync(x => x.BomStageId == stage.Id && x.Status == "Active");
            var issue = new Voucher
            {
                CompanyId = 1, FinancialYearId = 1, VoucherTypeId = 2, SequenceNumber = 1,
                VoucherNumber = "MO-A-1", VoucherNumberNormalized = "MO-A-1",
                VoucherDate = new DateOnly(2026, 8, 16), PartyLedgerId = 10, Status = "Open",
                CreatedAtUtc = now, ModifiedAtUtc = now
            };
            before.Vouchers.Add(issue);
            await before.SaveChangesAsync();
            before.MaterialOutLines.Add(new MaterialOutLine
            {
                VoucherId = issue.Id, JwoVoucherId = created.EntityId!.Value, JwoFinishedGoodId = component.FinishedGoodId,
                JwoComponentId = component.Id, BomStageId = stage.Id, StageAssignmentId = assignment.Id,
                LineNumber = 1, StockItemId = component.StockItemId, UqcId = component.UqcId,
                SourceGodownId = 1, DestinationGodownId = 1, RequiredQuantity = component.RequiredQuantity,
                IssuedQuantity = component.RequiredQuantity
            });
            await before.SaveChangesAsync();
        }

        var edit = await environment.JobWorkOrderRepository.GetForEditAsync(created.EntityId!.Value)
            ?? throw new InvalidOperationException();
        edit.FinishedGoods[0].BomStages[0].AssignedJobWorkerId = 11;
        edit.FinishedGoods[0].BomStages[0].ExpectedCompletionDate = new DateOnly(2026, 8, 22);
        Assert.Equal(2m, edit.FinishedGoods[0].BomStages[0].ExpectedProcessRate);
        edit.FinishedGoods[0].BomStages[0].ExpectedProcessRate = 3;
        var reassigned = await environment.JobWorkOrderRepository.SaveAsync(edit);
        Assert.True(reassigned.Success, reassigned.Message);

        await using var verify = environment.CreateDbContext();
        var persisted = await verify.JobWorkOrderBomStages.SingleAsync(x => x.VoucherId == created.EntityId);
        Assert.Equal(stageId, persisted.Id);
        Assert.Equal(stableKey, persisted.StableKey);
        Assert.Equal(11, persisted.AssignedJobWorkerId);
        var assignments = await verify.JobWorkOrderStageAssignments.Where(x => x.BomStageId == stageId)
            .OrderBy(x => x.AssignmentVersion).ToListAsync();
        Assert.Equal(2, assignments.Count);
        Assert.Equal(2m, assignments[0].ExpectedProcessRate);
        Assert.Equal(3m, assignments[1].ExpectedProcessRate);
        Assert.Equal("Superseded", assignments[0].Status);
        Assert.Equal("Active", assignments[1].Status);
        Assert.Equal(new DateOnly(2026, 8, 20), assignments[0].ExpectedCompletionDate);
        Assert.Equal(new DateOnly(2026, 8, 22), assignments[1].ExpectedCompletionDate);
        Assert.Equal(new DateOnly(2026, 8, 22),
            (await verify.Vouchers.SingleAsync(x => x.Id == created.EntityId)).DueDate);
        Assert.Equal(2, await verify.JobWorkOrderRevisions.CountAsync(x => x.VoucherId == created.EntityId));

        var workerAPending = Assert.Single(await environment.MaterialInRepository.GetPendingOrdersAsync(10));
        var workerAStage = Assert.Single(workerAPending.FinishedGoods);
        Assert.Equal(assignments[0].Id, workerAStage.StageAssignmentId);
        Assert.Equal(1, workerAStage.AssignmentVersion);
        Assert.Empty(await environment.MaterialInRepository.GetPendingOrdersAsync(11));
        Assert.Equal("PartiallyProcessed", (await verify.Vouchers.AsNoTracking().SingleAsync(x => x.Id == created.EntityId)).Status);
        var rateEdit = await environment.JobWorkOrderRepository.GetForEditAsync(created.EntityId.Value);
        Assert.NotNull(rateEdit);
        rateEdit!.FinishedGoods[0].BomStages[0].ExpectedProcessRate = 4;
        var rateAmended = await environment.JobWorkOrderRepository.SaveAsync(rateEdit);
        Assert.True(rateAmended.Success, rateAmended.Message);
        var history = await verify.JobWorkOrderStageAssignments.AsNoTracking().Where(x => x.BomStageId == stageId)
            .OrderBy(x => x.AssignmentVersion).ToListAsync();
        Assert.Equal(new decimal?[] { 2, 3, 4 }, history.Select(x => x.ExpectedProcessRate).ToArray());
        Assert.Equal(stageId, (await verify.JobWorkOrderBomStages.AsNoTracking().SingleAsync(x => x.VoucherId == created.EntityId)).Id);
    }

    [Fact]
    public async Task Bom_save_is_noop_when_unchanged_and_creates_revision_only_for_semantic_change()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();

        var created = await environment.BillOfMaterialRepository.SaveAsync(new BillOfMaterialEditModel
        {
            StockItemId = 3, Name = "Revision Test", OutputQuantity = 1,
            Lines = [new() { ComponentStockItemId = 1, RequiredQuantity = 5 }]
        });
        Assert.True(created.Success, created.Message);

        var unchanged = await environment.BillOfMaterialRepository.GetForEditAsync(created.EntityId!.Value)
            ?? throw new InvalidOperationException();
        var noOp = await environment.BillOfMaterialRepository.SaveAsync(unchanged);
        Assert.True(noOp.Success, noOp.Message);
        Assert.Contains("No BOM changes", noOp.Message, StringComparison.OrdinalIgnoreCase);

        unchanged.Name = "Revision Test Renamed";
        unchanged.ChangeReason = "BOM name corrected";
        var changed = await environment.BillOfMaterialRepository.SaveAsync(unchanged);
        Assert.True(changed.Success, changed.Message);

        var history = await environment.BillOfMaterialRepository.GetRevisionHistoryAsync(created.EntityId.Value);
        Assert.Equal(2, history.Count);
        Assert.True(history[0].IsCurrent);
        Assert.Equal(2, history[0].RevisionNumber);
        Assert.Equal("BOM name corrected", history[0].ChangeReason);
    }

    [Fact]
    public async Task Parent_revision_keeps_exact_child_revision_after_child_changes()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();

        var child = await environment.BillOfMaterialRepository.SaveAsync(new BillOfMaterialEditModel
        {
            StockItemId = 3, Name = "Child Revision", OutputQuantity = 1,
            Lines = [new() { ComponentStockItemId = 1, RequiredQuantity = 5 }]
        });
        var parent = await environment.BillOfMaterialRepository.SaveAsync(new BillOfMaterialEditModel
        {
            StockItemId = 2, Name = "Parent Revision", OutputQuantity = 1,
            Lines = [new() { ComponentStockItemId = 3, RequiredQuantity = 1, ChildBomId = child.EntityId }]
        });
        Assert.True(child.Success && parent.Success);

        await using var beforeDb = environment.CreateDbContext();
        var parentChildRevision = await beforeDb.BillOfMaterialRevisionLines
            .Where(x => x.BomRevision.BomId == parent.EntityId)
            .Select(x => x.ChildBomRevisionId)
            .SingleAsync();

        var childEdit = await environment.BillOfMaterialRepository.GetForEditAsync(child.EntityId!.Value)
            ?? throw new InvalidOperationException();
        childEdit.Lines[0].RequiredQuantity = 7;
        Assert.True((await environment.BillOfMaterialRepository.SaveAsync(childEdit)).Success);

        await using var afterDb = environment.CreateDbContext();
        var stillPinned = await afterDb.BillOfMaterialRevisionLines
            .Where(x => x.BomRevision.BomId == parent.EntityId)
            .Select(x => x.ChildBomRevisionId)
            .SingleAsync();
        var childCurrent = await afterDb.BillOfMaterials.Where(x => x.Id == child.EntityId)
            .Select(x => x.CurrentRevisionId).SingleAsync();
        Assert.Equal(parentChildRevision, stillPinned);
        Assert.NotEqual(childCurrent, stillPinned);
    }

    [Fact]
    public async Task Nested_bom_preview_expands_quantities_and_preserves_stage_hierarchy()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();

        var child = await environment.BillOfMaterialRepository.SaveAsync(new BillOfMaterialEditModel
        {
            StockItemId = 3,
            Name = "Embroidery B001",
            OutputQuantity = 1,
            Lines = [new() { ComponentStockItemId = 1, RequiredQuantity = 5 }]
        });
        Assert.True(child.Success, child.Message);

        var parent = await environment.BillOfMaterialRepository.SaveAsync(new BillOfMaterialEditModel
        {
            StockItemId = 2,
            Name = "B001 Master BOM",
            OutputQuantity = 10,
            Lines = [new() { ComponentStockItemId = 3, RequiredQuantity = 2, ChildBomId = child.EntityId }]
        });
        Assert.True(parent.Success, parent.Message);

        var preview = await environment.JobWorkOrderRepository.BuildBomStagePreviewAsync(
            parent.EntityId!.Value, 20, 1, 2);

        Assert.Equal(2, preview.Count);
        Assert.True(preview[0].IsFinalStage);
        Assert.Equal(20, preview[0].OutputQuantity);
        Assert.False(preview[1].IsFinalStage);
        Assert.Equal(4, preview[1].OutputQuantity);
        Assert.Equal(preview[0].ClientKey, preview[1].ParentStageClientKey);
    }

    [Fact]
    public async Task Repository_rejects_indirect_bom_cycle_without_partial_update()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();

        var child = await environment.BillOfMaterialRepository.SaveAsync(new BillOfMaterialEditModel
        {
            StockItemId = 3, Name = "Child", OutputQuantity = 1,
            Lines = [new() { ComponentStockItemId = 1, RequiredQuantity = 5 }]
        });
        var parent = await environment.BillOfMaterialRepository.SaveAsync(new BillOfMaterialEditModel
        {
            StockItemId = 2, Name = "Parent", OutputQuantity = 1,
            Lines = [new() { ComponentStockItemId = 3, RequiredQuantity = 1, ChildBomId = child.EntityId }]
        });
        Assert.True(child.Success && parent.Success);

        var edit = await environment.BillOfMaterialRepository.GetForEditAsync(child.EntityId!.Value)
            ?? throw new InvalidOperationException();
        edit.Lines.Add(new BillOfMaterialLineEditModel
        {
            ComponentStockItemId = 2,
            RequiredQuantity = 1,
            ChildBomId = parent.EntityId
        });
        var rejected = await environment.BillOfMaterialRepository.SaveAsync(edit);

        Assert.False(rejected.Success);
        Assert.Contains("cannot directly or indirectly contain itself", rejected.Message, StringComparison.OrdinalIgnoreCase);
        await using var db = environment.CreateDbContext();
        Assert.Single(await db.BillOfMaterialLines.Where(x => x.BomId == child.EntityId).ToListAsync());
    }

    [Fact]
    public async Task Simultaneous_opposite_bom_links_cannot_create_a_cycle()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();

        var a = await environment.BillOfMaterialRepository.SaveAsync(new BillOfMaterialEditModel
        {
            StockItemId = 2, Name = "Concurrent A", OutputQuantity = 1,
            Lines = [new() { ComponentStockItemId = 1, RequiredQuantity = 1 }]
        });
        var b = await environment.BillOfMaterialRepository.SaveAsync(new BillOfMaterialEditModel
        {
            StockItemId = 3, Name = "Concurrent B", OutputQuantity = 1,
            Lines = [new() { ComponentStockItemId = 1, RequiredQuantity = 1 }]
        });
        Assert.True(a.Success && b.Success);

        var editA = await environment.BillOfMaterialRepository.GetForEditAsync(a.EntityId!.Value)
            ?? throw new InvalidOperationException();
        editA.Lines = [new() { ComponentStockItemId = 3, RequiredQuantity = 1, ChildBomId = b.EntityId }];
        var editB = await environment.BillOfMaterialRepository.GetForEditAsync(b.EntityId!.Value)
            ?? throw new InvalidOperationException();
        editB.Lines = [new() { ComponentStockItemId = 2, RequiredQuantity = 1, ChildBomId = a.EntityId }];

        var results = await Task.WhenAll(
            environment.BillOfMaterialRepository.SaveAsync(editA),
            environment.BillOfMaterialRepository.SaveAsync(editB));

        Assert.Single(results.Where(x => x.Success));
        var rejected = Assert.Single(results.Where(x => !x.Success));
        Assert.True(
            rejected.Message.Contains("cannot directly or indirectly contain itself", StringComparison.OrdinalIgnoreCase) ||
            rejected.Message.Contains("changed concurrently", StringComparison.OrdinalIgnoreCase),
            rejected.Message);
    }
}
