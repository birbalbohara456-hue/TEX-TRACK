using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class ProducedComponentValuationTests
{
    [Fact]
    public async Task Onward_material_out_carries_remaining_child_stage_value_and_ignores_client_rate()
    {
        await using var environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
        var now = DateTimeOffset.UtcNow;
        long jwoId;
        long finishedGoodId;
        long parentStageId;
        long childStageId;
        long componentId;

        await using (var db = environment.CreateDbContext())
        {
            db.Vouchers.Remove(await db.Vouchers.SingleAsync(x => x.Id == 1));
            var jwoType = await db.VoucherTypes.SingleAsync(x => x.Id == 1);
            jwoType.SystemTypeCode = "JOB_WORK_OUT_ORDER";
            db.LedgerGroups.Add(new LedgerGroup
            {
                Id = 10, CompanyId = 1, Name = "Job Workers", NameNormalized = "JOB WORKERS",
                RootClassification = "SundryCreditors", IsActive = true, CreatedAtUtc = now, ModifiedAtUtc = now
            });
            db.Ledgers.AddRange(
                new Ledger { Id = 10, CompanyId = 1, LedgerGroupId = 10, Name = "Child Worker", NameNormalized = "CHILD WORKER", IsJobWorker = true, IsActive = true, CreatedAtUtc = now, ModifiedAtUtc = now },
                new Ledger { Id = 11, CompanyId = 1, LedgerGroupId = 10, Name = "Parent Worker", NameNormalized = "PARENT WORKER", IsJobWorker = true, IsActive = true, CreatedAtUtc = now, ModifiedAtUtc = now });
            db.VoucherTypes.AddRange(
                new VoucherType
                {
                    Id = 2, CompanyId = 1, Name = "Material Out", NameNormalized = "MATERIAL OUT",
                    SystemTypeCode = "MATERIAL_OUT", Nature = "Inventory", PostingMode = "Actual",
                    Abbreviation = "MO", NumberingMode = "Auto", ResetPeriod = "FinancialYear",
                    TallyVoucherTypeName = "Material Out", IsActive = true, CreatedAtUtc = now, ModifiedAtUtc = now
                },
                new VoucherType
                {
                    Id = 3, CompanyId = 1, Name = "Material In", NameNormalized = "MATERIAL IN",
                    SystemTypeCode = "MATERIAL_IN", Nature = "Inventory", PostingMode = "Actual",
                    Abbreviation = "MI", NumberingMode = "Auto", ResetPeriod = "FinancialYear",
                    TallyVoucherTypeName = "Material In", IsActive = true, CreatedAtUtc = now, ModifiedAtUtc = now
                });
            await db.SaveChangesAsync();

            var jwo = new Voucher
            {
                CompanyId = 1, FinancialYearId = 1, VoucherTypeId = 1, SequenceNumber = 1,
                VoucherNumber = "JWO-1", VoucherNumberNormalized = "JWO-1",
                VoucherDate = new DateOnly(2026, 8, 1), Batch = "VALUE-LINEAGE",
                PartyLedgerId = 11, Status = "Open", CreatedAtUtc = now, ModifiedAtUtc = now
            };
            db.Vouchers.Add(jwo);
            await db.SaveChangesAsync();
            jwoId = jwo.Id;

            var finishedGood = new JobWorkOrderFinishedGood
            {
                VoucherId = jwoId, LineNumber = 1, StockItemId = 2, OrderedQuantity = 100,
                FinishedGoodsGodownId = 1, DestinationGodownId = 2
            };
            db.JobWorkOrderFinishedGoods.Add(finishedGood);
            await db.SaveChangesAsync();
            finishedGoodId = finishedGood.Id;

            var parentStage = new JobWorkOrderBomStage
            {
                VoucherId = jwoId, FinishedGoodId = finishedGoodId, StageNumber = 1, StageLevel = 0,
                StagePath = "1", StageName = "Final Assembly", OutputStockItemId = 2, OutputUqcId = 1,
                OutputQuantity = 100, AssignedJobWorkerId = 11, OutputGodownId = 1, IsFinalStage = true
            };
            db.JobWorkOrderBomStages.Add(parentStage);
            await db.SaveChangesAsync();
            parentStageId = parentStage.Id;

            var childStage = new JobWorkOrderBomStage
            {
                VoucherId = jwoId, FinishedGoodId = finishedGoodId, ParentStageId = parentStageId,
                StageNumber = 2, StageLevel = 1, StagePath = "1.1", StageName = "Produced Panel",
                OutputStockItemId = 3, OutputUqcId = 1, OutputQuantity = 100,
                AssignedJobWorkerId = 10, OutputGodownId = 1, IsFinalStage = false
            };
            db.JobWorkOrderBomStages.Add(childStage);
            await db.SaveChangesAsync();
            childStageId = childStage.Id;

            db.JobWorkOrderStageAssignments.AddRange(
                new JobWorkOrderStageAssignment { BomStageId = parentStageId, AssignmentVersion = 1, JobWorkerId = 11, Status = "Active", ValidFromUtc = now },
                new JobWorkOrderStageAssignment { BomStageId = childStageId, AssignmentVersion = 1, JobWorkerId = 10, Status = "Active", ValidFromUtc = now });
            var component = new JobWorkOrderComponent
            {
                FinishedGoodId = finishedGoodId, LineNumber = 1, StockItemId = 3, UqcId = 1,
                RequiredQuantity = 100, ComponentGodownId = 1, BomStageId = parentStageId,
                ChildBomStageId = childStageId, IsProducedComponent = true
            };
            db.JobWorkOrderComponents.Add(component);
            await db.SaveChangesAsync();
            componentId = component.Id;

            await AddStageReceiptAsync(db, jwoId, finishedGoodId, childStageId, 1, 50, 5_000, now);
        }

        var firstPending = Assert.Single(await environment.MaterialOutRepository.GetPendingOrdersAsync(11));
        var firstComponent = Assert.Single(Assert.Single(firstPending.FinishedGoods).Components);
        Assert.True(firstComponent.IsProducedComponent);
        Assert.Equal(100m, firstComponent.Rate);

        var firstIssue = await environment.MaterialOutRepository.SaveAsync(NewRequest(
            jwoId, finishedGoodId, componentId, new DateOnly(2026, 8, 3), 20, 0));

        await using (var db = environment.CreateDbContext())
        {
            var persisted = await db.MaterialOutLines.SingleAsync(x => x.VoucherId == firstIssue.VoucherId);
            Assert.Equal(100m, persisted.Rate);
            Assert.Equal(2_000m, persisted.Amount);
            await AddStageReceiptAsync(db, jwoId, finishedGoodId, childStageId, 2, 50, 7_500, now);
        }

        var secondPending = Assert.Single(await environment.MaterialOutRepository.GetPendingOrdersAsync(11));
        var remainingComponent = Assert.Single(Assert.Single(secondPending.FinishedGoods).Components);
        Assert.Equal(80m, remainingComponent.PendingQuantity);
        Assert.Equal(131.25m, remainingComponent.Rate);

        var secondIssue = await environment.MaterialOutRepository.SaveAsync(NewRequest(
            jwoId, finishedGoodId, componentId, new DateOnly(2026, 8, 5), 80, 1));

        await using var verify = environment.CreateDbContext();
        var persistedSecond = await verify.MaterialOutLines.SingleAsync(x => x.VoucherId == secondIssue.VoucherId);
        Assert.Equal(131.25m, persistedSecond.Rate);
        Assert.Equal(10_500m, persistedSecond.Amount);
        Assert.Equal(12_500m, await verify.MaterialOutLines.Where(x => x.JwoComponentId == componentId).SumAsync(x => x.Amount));
    }

    private static MaterialOutSaveRequest NewRequest(
        long jwoId, long finishedGoodId, long componentId, DateOnly date, decimal quantity, decimal clientRate) => new()
    {
        VoucherTypeId = 2,
        VoucherDate = date,
        JobWorkerLedgerId = 11,
        JwoVoucherId = jwoId,
        DisplayedOrderNumber = "JWO-1",
        DestinationGodownId = 2,
        Lines =
        [
            new MaterialOutSaveLine
            {
                JwoFinishedGoodId = finishedGoodId, JwoComponentId = componentId,
                StockItemId = 3, UqcId = 1, SourceGodownId = 1,
                IssuedQuantity = quantity, Rate = clientRate
            }
        ]
    };

    private static async Task AddStageReceiptAsync(
        Data.TexTrackDbContext db,
        long jwoId,
        long finishedGoodId,
        long stageId,
        int sequence,
        decimal quantity,
        decimal value,
        DateTimeOffset now)
    {
        var receiptVoucher = new Voucher
        {
            CompanyId = 1, FinancialYearId = 1, VoucherTypeId = 3, SequenceNumber = sequence,
            VoucherNumber = $"MI-{sequence}", VoucherNumberNormalized = $"MI-{sequence}",
            VoucherDate = new DateOnly(2026, 8, 1 + sequence), PartyLedgerId = 10,
            Status = "Open", CreatedAtUtc = now, ModifiedAtUtc = now
        };
        db.Vouchers.Add(receiptVoucher);
        await db.SaveChangesAsync();
        var assignmentId = await db.JobWorkOrderStageAssignments
            .Where(x => x.BomStageId == stageId && x.Status == "Active")
            .Select(x => x.Id).SingleAsync();
        db.MaterialInFinishedGoods.Add(new MaterialInFinishedGood
        {
            VoucherId = receiptVoucher.Id, JwoFinishedGoodId = finishedGoodId, LineNumber = 1,
            StockItemId = 3, UqcId = 1, ReceivingGodownId = 1,
            OrderedQuantity = 100, PreviouslyReceivedQuantity = (sequence - 1) * 50,
            ReceivedQuantity = quantity, FinishedGoodsValue = value,
            Rate = value / quantity, BomStageId = stageId, StageAssignmentId = assignmentId
        });
        db.VoucherLinks.Add(new VoucherLink
        {
            CompanyId = 1, SourceVoucherId = jwoId, TargetVoucherId = receiptVoucher.Id,
            LinkType = "JWO_TO_MATERIAL_IN", CreatedAtUtc = now, CreatedBy = "Test"
        });
        await db.SaveChangesAsync();
    }
}
