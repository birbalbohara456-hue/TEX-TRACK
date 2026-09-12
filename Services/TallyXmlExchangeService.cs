using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed class TallyXmlExchangeService(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext,
    TallyXmlParser parser,
    VoucherLifecycleService lifecycle,
    StockPostingService stockPosting,
    VoucherAuditHistoryService? voucherAuditHistory = null)
{
    private const string JwoCode = "JOB_WORK_OUT_ORDER";
    private readonly VoucherAuditHistoryService fullAudit = voucherAuditHistory ?? new(contextFactory);
    private const string MaterialOutCode = "MATERIAL_OUT";
    private const string MaterialInCode = "MATERIAL_IN";

    public async Task<TallyImportPreview> PreviewAsync(string fileName, string xml, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        var document = parser.Parse(xml);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var link = await db.TallyCompanyLinks.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyContext.CompanyId && x.TallyCompanyIdentity == document.CompanyIdentity, cancellationToken);
        var sync = link is null ? new Dictionary<string, TallySyncRecord>(StringComparer.OrdinalIgnoreCase) : await db.TallySyncRecords.AsNoTracking()
            .Where(x => x.TallyCompanyLinkId == link.Id).ToDictionaryAsync(x => x.TallyGuid, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var preview = new TallyImportPreview
        {
            FileName = fileName,
            CompanyName = document.CompanyName,
            CompanyIdentity = document.CompanyIdentity,
            FileHash = document.FileHash,
            Masters = document.Masters.Where(x => !x.IsDeleted).ToList(),
            NewMasterCount = await CountMissingMastersAsync(db, document, cancellationToken)
        };

        foreach (var voucher in document.Vouchers)
        {
            var row = new TallyImportPreviewRow
            {
                Guid = voucher.Guid,
                VoucherTypeName = voucher.VoucherTypeName,
                VoucherNumber = voucher.VoucherNumber,
                OrderNumber = voucher.PrimaryOrderNumber,
                VoucherDate = voucher.VoucherDate,
                PartyLedgerName = voucher.PartyLedgerName,
                Voucher = voucher
            };

            if (voucher.IsDeleted)
            {
                row.State = "Exception";
                row.Message = "Deleted Tally vouchers are not imported. Update TexTrack manually after verification.";
            }
            else if (string.IsNullOrWhiteSpace(voucher.Guid) || string.IsNullOrWhiteSpace(voucher.VoucherNumber))
            {
                row.State = "Exception";
                row.Message = "Tally GUID and Voucher Number are required.";
            }
            else if (sync.TryGetValue(voucher.Guid, out var existing))
            {
                if (voucher.IsCancelled && existing.SyncState == "Cancelled" && existing.SourceHash == voucher.SourceHash)
                { row.State = "Unchanged"; row.Selected = false; row.Message = "This Tally cancellation was already imported."; }
                else if (voucher.IsCancelled) { row.State = "Cancelled"; row.Message = "Tally marks this voucher as cancelled."; }
                else if (existing.SourceHash == voucher.SourceHash) { row.State = "Unchanged"; row.Selected = false; row.Message = "Already imported without changes."; }
                else { row.State = "Updated"; row.Message = "Tally contains a changed version. Approval will replace the imported voucher when it has no downstream conflict."; }
            }
            else if (voucher.IsCancelled)
            {
                row.State = "Exception";
                row.Message = "Cancellation cannot be matched because the original Tally GUID was never imported.";
            }
            else if (voucher.VoucherTypeName is "Material Out" or "Material In" &&
                     !await JwoExistsAsync(db, voucher.PrimaryOrderNumber, cancellationToken))
            {
                row.State = "Exception";
                row.Message = $"Import Job Work Out Order '{voucher.PrimaryOrderNumber}' first.";
            }
            else if (await VoucherNumberExistsAsync(db, voucher, cancellationToken))
            {
                row.State = "Exception";
                row.Message = "The same voucher number already belongs to a different identity.";
            }

            preview.Rows.Add(row);
        }
        return preview;
    }

    public async Task<TallyApplyResult> ApplyAsync(TallyImportPreview preview, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ExportData, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var link = await db.TallyCompanyLinks.SingleOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId &&
                x.TallyCompanyIdentity == preview.CompanyIdentity, cancellationToken);
            if (link is null)
            {
                link = new TallyCompanyLink
                {
                    CompanyId = companyContext.CompanyId, TallyCompanyName = preview.CompanyName,
                    TallyCompanyIdentity = preview.CompanyIdentity, IsConfirmed = true,
                    CreatedAtUtc = now, ModifiedAtUtc = now, CreatedBy = companyContext.Actor, ModifiedBy = companyContext.Actor
                };
                db.TallyCompanyLinks.Add(link);
                await db.SaveChangesAsync(cancellationToken);
            }

            var batch = new TallyExchangeBatch
            {
                CompanyId = companyContext.CompanyId, TallyCompanyLinkId = link.Id, Direction = "Import",
                FileName = preview.FileName, FileHash = preview.FileHash, Status = "Applying",
                NewMasterCount = preview.NewMasterCount, NewVoucherCount = preview.NewCount,
                UpdatedVoucherCount = preview.UpdatedCount, UnchangedVoucherCount = preview.UnchangedCount,
                CancelledVoucherCount = preview.CancelledCount, ExceptionCount = preview.ExceptionCount,
                CreatedAtUtc = now, ModifiedAtUtc = now, CreatedBy = companyContext.Actor, ModifiedBy = companyContext.Actor
            };
            db.TallyExchangeBatches.Add(batch);
            await db.SaveChangesAsync(cancellationToken);

            await ImportExplicitMastersAsync(db, preview.Masters, cancellationToken);

            foreach (var row in preview.Rows.Where(x => x.Selected || x.State == "Exception"))
            {
                if (row.State == "Exception")
                {
                    AddException(db, batch.Id, row, row.Message.Contains("Job Work", StringComparison.OrdinalIgnoreCase) ? "MISSING_JWO" : "VALIDATION");
                    continue;
                }
                if (row.State == "Unchanged") continue;

                var sync = await db.TallySyncRecords.SingleOrDefaultAsync(x => x.TallyCompanyLinkId == link.Id && x.TallyGuid == row.Guid, cancellationToken);
                if (row.State == "Cancelled")
                {
                    var cancelId = sync?.VoucherId;
                    if (cancelId is long existingVoucherId)
                        await CancelImportedVoucherAsync(db, existingVoucherId, cancellationToken);
                    if (sync is not null)
                    {
                        sync.SourceHash = row.Voucher.SourceHash;
                        sync.SyncState = "Cancelled";
                        sync.LastImportedAtUtc = now;
                        sync.ModifiedAtUtc = now;
                        sync.ModifiedBy = companyContext.Actor;
                    }
                    await db.SaveChangesAsync(cancellationToken);
                    if (cancelId is long cancelledVoucherId)
                        await fullAudit.RecordAsync(
                            db, cancelledVoucherId, VoucherAuditActions.ImportCancel,
                            "Cancelled in TallyPrime", companyContext.ActorFor("Tally XML"), now, cancellationToken);
                    continue;
                }
                if (row.State == "Updated" && sync?.VoucherId is long existingId)
                {
                    var hasDownstream = await db.VoucherLinks.AnyAsync(x => x.SourceVoucherId == existingId, cancellationToken);
                    if (hasDownstream)
                    {
                        AddException(db, batch.Id, row, "UPDATE_CONFLICT", "The imported voucher has linked vouchers. Reconcile those links before replacing it.");
                        batch.ExceptionCount++;
                        continue;
                    }
                    var parentLinks = await db.VoucherLinks.Where(x => x.TargetVoucherId == existingId).ToListAsync(cancellationToken);
                    db.VoucherLinks.RemoveRange(parentLinks);
                    await RemoveImportedVoucherAsync(db, existingId, cancellationToken);
                    sync.VoucherId = null;
                }

                long? voucherId = row.VoucherTypeName switch
                {
                    "Job Work Out Order" => await ImportJwoAsync(db, row.Voucher, cancellationToken),
                    "Material Out" => await ImportMaterialOutAsync(db, row.Voucher, cancellationToken),
                    "Material In" => row.UseOthersVariant
                        ? await ImportMaterialInAsync(db, row.Voucher, cancellationToken)
                        : null,
                    _ => null
                };

                if (voucherId is null)
                {
                    db.TallyVariantAllocationTasks.Add(new TallyVariantAllocationTask
                    {
                        CompanyId = companyContext.CompanyId, ExchangeBatchId = batch.Id, TallyGuid = row.Guid,
                        VoucherTypeName = row.VoucherTypeName, VoucherNumber = row.VoucherNumber,
                        StockItemName = string.Join(", ", row.Voucher.InventoryLines.Where(x => x.Direction == "In").Select(x => x.StockItemName).Distinct()),
                        Quantity = row.Voucher.InventoryLines.Where(x => x.Direction == "In").Sum(x => x.Quantity),
                        UqcName = row.Voucher.InventoryLines.FirstOrDefault(x => x.Direction == "In")?.UqcName ?? string.Empty,
                        PayloadXml = row.Voucher.RawXml, Status = "Pending", CreatedAtUtc = now, ModifiedAtUtc = now,
                        CreatedBy = companyContext.Actor, ModifiedBy = companyContext.Actor
                    });
                    continue;
                }

                if (sync is null)
                {
                    sync = new TallySyncRecord
                    {
                        CompanyId = companyContext.CompanyId, TallyCompanyLinkId = link.Id, TallyGuid = row.Guid,
                        CreatedAtUtc = now, CreatedBy = companyContext.Actor
                    };
                    db.TallySyncRecords.Add(sync);
                }
                sync.VoucherId = voucherId;
                sync.TallyRemoteId = row.Voucher.RemoteId;
                sync.VoucherTypeName = row.VoucherTypeName;
                sync.VoucherNumber = row.VoucherNumber;
                sync.SourceHash = row.Voucher.SourceHash;
                sync.SyncState = "Imported";
                sync.LastImportedAtUtc = now;
                sync.ModifiedAtUtc = now;
                sync.ModifiedBy = companyContext.Actor;
                sync.ConcurrencyToken = Guid.NewGuid().ToString("N");
                await db.SaveChangesAsync(cancellationToken);
                await fullAudit.RecordAsync(
                    db, voucherId.Value,
                    row.State == "Updated" ? VoucherAuditActions.ImportUpdate : VoucherAuditActions.ImportCreate,
                    row.State == "Updated"
                        ? $"Voucher replaced from Tally XML GUID {row.Guid}."
                        : $"Voucher created from Tally XML GUID {row.Guid}.",
                    companyContext.ActorFor("Tally XML"), now, cancellationToken);
            }

            batch.Status = batch.ExceptionCount > 0 ? "CompletedWithExceptions" : "Completed";
            batch.Summary = $"New {preview.NewCount}; Updated {preview.UpdatedCount}; Unchanged {preview.UnchangedCount}; Cancelled {preview.CancelledCount}; Exceptions {batch.ExceptionCount}.";
            batch.ModifiedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await stockPosting.EnsureNegativeStockPolicyAsync(
                db, companyContext.CompanyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new TallyApplyResult(true, $"Tally XML import completed. {batch.Summary}", batch.Id);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new TallyApplyResult(false, $"Nothing was imported. {exception.GetBaseException().Message}");
        }
    }

    public async Task<IReadOnlyList<TallyExchangeHistoryRow>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.TallyExchangeBatches.AsNoTracking().Where(x => x.CompanyId == companyContext.CompanyId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(200).Select(x => new TallyExchangeHistoryRow
            {
                Id = x.Id, Date = x.CreatedAtUtc, Direction = x.Direction, FileName = x.FileName,
                CompanyName = x.TallyCompanyLink == null ? "" : x.TallyCompanyLink.TallyCompanyName,
                Status = x.Status, Summary = x.Summary
            }).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TallyExceptionRow>> GetExceptionsAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.TallyImportExceptions.AsNoTracking().Where(x => x.CompanyId == companyContext.CompanyId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(200).Select(x => new TallyExceptionRow
            {
                Id = x.Id, Date = x.CreatedAtUtc, VoucherTypeName = x.VoucherTypeName, VoucherNumber = x.VoucherNumber,
                OrderNumber = x.OrderNumber, ReasonCode = x.ReasonCode, Message = x.Message, Status = x.Status
            }).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TallyVariantTaskRow>> GetVariantTasksAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.TallyVariantAllocationTasks.AsNoTracking().Where(x => x.CompanyId == companyContext.CompanyId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(200).Select(x => new TallyVariantTaskRow
            {
                Id = x.Id, Date = x.CreatedAtUtc, VoucherTypeName = x.VoucherTypeName,
                VoucherNumber = x.VoucherNumber, StockItemName = x.StockItemName,
                Quantity = x.Quantity, UqcName = x.UqcName, Status = x.Status
            }).ToListAsync(cancellationToken);
    }

    private async Task ImportExplicitMastersAsync(TexTrackDbContext db, IReadOnlyList<TallyXmlMaster> masters, CancellationToken ct)
    {
        foreach (var master in masters.Where(x => x.Type.Equals("GROUP", StringComparison.OrdinalIgnoreCase)))
            await EnsureLedgerGroupAsync(db, master.Name, ct);

        foreach (var master in masters.Where(x => x.Type.Equals("UNIT", StringComparison.OrdinalIgnoreCase)))
        {
            var unit = await EnsureUqcAsync(db, master.Name, ct);
            unit.DecimalPlaces = master.DecimalPlaces;
        }

        foreach (var master in masters.Where(x => x.Type.Equals("STOCKGROUP", StringComparison.OrdinalIgnoreCase)))
            await EnsureStockGroupAsync(db, master.Name, master.Parent, ct);

        foreach (var master in masters.Where(x => x.Type.Equals("STOCKCATEGORY", StringComparison.OrdinalIgnoreCase)))
            await EnsureStockCategoryAsync(db, master.Name, ct);

        foreach (var master in masters.Where(x => x.Type.Equals("GODOWN", StringComparison.OrdinalIgnoreCase)))
            await EnsureGodownAsync(db, master.Name, ct);

        foreach (var master in masters.Where(x => x.Type.Equals("LEDGER", StringComparison.OrdinalIgnoreCase)))
        {
            var ledger = await EnsureLedgerAsync(db, master.Name, false, ct);
            if (!string.IsNullOrWhiteSpace(master.Parent))
            {
                var group = await EnsureLedgerGroupAsync(db, master.Parent, ct);
                ledger.LedgerGroupId = group.Id;
            }
        }

        foreach (var master in masters.Where(x => x.Type.Equals("STOCKITEM", StringComparison.OrdinalIgnoreCase)))
            await EnsureExplicitStockItemAsync(db, master, ct);

        foreach (var master in masters.Where(x => x.Type.Equals("VOUCHERTYPE", StringComparison.OrdinalIgnoreCase)))
            await EnsureVoucherTypeMasterAsync(db, master, ct);
    }

    private async Task<StockGroup> EnsureStockGroupAsync(TexTrackDbContext db, string name, string parentName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "Primary";
        var normalized = Normalize(name);
        var existing = await db.StockGroups.FirstOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.NameNormalized == normalized, ct);
        if (existing is not null) return existing;
        StockGroup? parent = null;
        if (!string.IsNullOrWhiteSpace(parentName) && !string.Equals(parentName, name, StringComparison.OrdinalIgnoreCase))
            parent = await EnsureStockGroupAsync(db, parentName, string.Empty, ct);
        var entity = new StockGroup
        {
            CompanyId = companyContext.CompanyId, ParentId = parent?.Id, Name = name.Trim(), NameNormalized = normalized,
            RootClassification = "Inventory", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow,
            ModifiedAtUtc = DateTimeOffset.UtcNow, CreatedBy = companyContext.ActorFor("Tally XML"), ModifiedBy = companyContext.ActorFor("Tally XML")
        };
        db.StockGroups.Add(entity); await db.SaveChangesAsync(ct); return entity;
    }

    private async Task<StockCategory> EnsureStockCategoryAsync(TexTrackDbContext db, string name, CancellationToken ct)
    {
        var normalized = Normalize(name);
        var existing = await db.StockCategories.FirstOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.NameNormalized == normalized, ct);
        if (existing is not null) return existing;
        var entity = new StockCategory
        {
            CompanyId = companyContext.CompanyId, Name = name.Trim(), NameNormalized = normalized, IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = companyContext.ActorFor("Tally XML"), ModifiedBy = companyContext.ActorFor("Tally XML")
        };
        db.StockCategories.Add(entity); await db.SaveChangesAsync(ct); return entity;
    }

    private async Task<StockItem> EnsureExplicitStockItemAsync(TexTrackDbContext db, TallyXmlMaster master, CancellationToken ct)
    {
        var normalized = Normalize(master.Name);
        var existing = await db.StockItems.FirstOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.NameNormalized == normalized, ct);
        if (existing is not null) return existing;
        var uqc = await EnsureUqcAsync(db, master.BaseUnits, ct);
        var group = await EnsureStockGroupAsync(db, string.IsNullOrWhiteSpace(master.Parent) ? "Primary" : master.Parent, string.Empty, ct);
        StockCategory? category = string.IsNullOrWhiteSpace(master.Category) ? null : await EnsureStockCategoryAsync(db, master.Category, ct);
        var entity = new StockItem
        {
            CompanyId = companyContext.CompanyId, Name = master.Name.Trim(), NameNormalized = normalized,
            StockGroupId = group.Id, StockCategoryId = category?.Id, UqcId = uqc.Id, IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = companyContext.ActorFor("Tally XML"), ModifiedBy = companyContext.ActorFor("Tally XML")
        };
        db.StockItems.Add(entity); await db.SaveChangesAsync(ct); await EnsureBaseVariantAsync(db, entity, ct); return entity;
    }

    private async Task<VoucherType> EnsureVoucherTypeMasterAsync(TexTrackDbContext db, TallyXmlMaster master, CancellationToken ct)
    {
        var normalized = Normalize(master.Name);
        var existing = await db.VoucherTypes.FirstOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.NameNormalized == normalized, ct);
        if (existing is not null) { existing.TallyVoucherTypeName = master.Name.Trim(); return existing; }
        var knownCode = master.Name switch
        {
            "Job Work Out Order" => JwoCode,
            "Material Out" => MaterialOutCode,
            "Material In" => MaterialInCode,
            _ => "TALLY_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..12]
        };
        existing = await db.VoucherTypes.FirstOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.SystemTypeCode == knownCode, ct);
        if (existing is not null) { existing.TallyVoucherTypeName = master.Name.Trim(); return existing; }
        var entity = new VoucherType
        {
            CompanyId = companyContext.CompanyId, Name = master.Name.Trim(), NameNormalized = normalized,
            SystemTypeCode = knownCode, Nature = "Tally", PostingMode = "NoPosting",
            Abbreviation = master.Name.Length <= 12 ? master.Name : master.Name[..12],
            AllowManualNumbering = true, NumberingMode = "Manual", TallyVoucherTypeName = master.Name.Trim(), IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = companyContext.ActorFor("Tally XML"), ModifiedBy = companyContext.ActorFor("Tally XML")
        };
        db.VoucherTypes.Add(entity); await db.SaveChangesAsync(ct); return entity;
    }

    private async Task<long> ImportJwoAsync(TexTrackDbContext db, TallyXmlVoucher input, CancellationToken ct)
    {
        var type = await GetVoucherTypeAsync(db, JwoCode, ct);
        var party = await EnsureLedgerAsync(db, input.PartyLedgerName, true, ct);
        var voucher = await NewVoucherAsync(db, type, input, party.Id, input.ReferenceNumber, ct);
        var finishedLines = input.InventoryLines
            .Where(x => x.Direction == "Order" && (x.Amount <= 0 || x.Components.Count > 0)).ToList();
        if (finishedLines.Count == 0) throw new InvalidOperationException($"JWO {input.VoucherNumber} has no finished-good order lines.");
        var legacyComponentLines = input.InventoryLines.Where(x => x.Direction == "Order" && x.Amount >= 0).ToList();
        var lineNumber = 0;
        foreach (var fgLine in finishedLines)
        {
            var item = await EnsureStockItemAsync(db, fgLine.StockItemName, fgLine.UqcName, true, ct);
            var godown = await EnsureGodownAsync(db, fgLine.GodownName, ct);
            var destinationGodown = await EnsureGodownAsync(db,
                string.IsNullOrWhiteSpace(fgLine.DestinationGodownName) ? fgLine.GodownName : fgLine.DestinationGodownName, ct);
            var variant = await EnsureBaseVariantAsync(db, item, ct);
            var fg = new JobWorkOrderFinishedGood
            {
                VoucherId = voucher.Id, LineNumber = ++lineNumber, DesignGroupKey = Guid.NewGuid().ToString("N"),
                StockItemId = item.Id, OrderedQuantity = fgLine.Quantity, FinishedGoodsGodownId = godown.Id,
                DestinationGodownId = destinationGodown.Id, XmlRate = fgLine.Rate, XmlAmount = fgLine.Amount
            };
            db.JobWorkOrderFinishedGoods.Add(fg);
            await db.SaveChangesAsync(ct);
            db.JobWorkOrderSizeAllocations.Add(new JobWorkOrderSizeAllocation { FinishedGoodId = fg.Id, StockItemVariantId = variant.Id, Quantity = fgLine.Quantity });
            var componentNo = 0;
            var componentLines = fgLine.Components.Count > 0
                ? fgLine.Components
                : legacyComponentLines.Where((_, index) => index % finishedLines.Count == lineNumber - 1).ToList();
            foreach (var componentLine in componentLines)
            {
                var component = await EnsureStockItemAsync(db, componentLine.StockItemName, componentLine.UqcName, false, ct);
                var componentVariant = await EnsureBaseVariantAsync(db, component, ct);
                var componentGodown = await EnsureGodownAsync(db, componentLine.GodownName, ct);
                db.JobWorkOrderComponents.Add(new JobWorkOrderComponent
                {
                    FinishedGoodId = fg.Id, LineNumber = ++componentNo, StockItemId = component.Id, UqcId = component.UqcId,
                    ComponentVariantId = componentVariant.Id,
                    RequiredQuantity = componentLine.Quantity, ComponentGodownId = componentGodown.Id,
                    XmlRate = componentLine.Rate, XmlAmount = Math.Abs(componentLine.Amount)
                });
            }
        }
        await db.SaveChangesAsync(ct);
        return voucher.Id;
    }

    private async Task<long> ImportMaterialOutAsync(TexTrackDbContext db, TallyXmlVoucher input, CancellationToken ct)
    {
        var jwo = await FindJwoAsync(db, input.PrimaryOrderNumber, ct) ?? throw new InvalidOperationException($"Import JWO '{input.PrimaryOrderNumber}' first.");
        var type = await GetVoucherTypeAsync(db, MaterialOutCode, ct);
        var party = await EnsureLedgerAsync(db, input.PartyLedgerName, true, ct);
        var voucher = await NewVoucherAsync(db, type, input, party.Id, jwo.Batch, ct);
        var outLines = input.InventoryLines.Where(x => x.Direction == "Out").ToList();
        var inLines = input.InventoryLines.Where(x => x.Direction == "In").ToList();
        var destinationName = input.DestinationGodownName;
        if (string.IsNullOrWhiteSpace(destinationName))
            destinationName = inLines.FirstOrDefault()?.GodownName ?? string.Empty;
        var destination = await EnsureGodownAsync(db, destinationName, ct);
        db.MaterialOutDetails.Add(new MaterialOutDetail { VoucherId = voucher.Id, DestinationGodownId = destination.Id, DisplayedOrderNumber = jwo.VoucherNumber });
        var components = await db.JobWorkOrderComponents.Include(x => x.FinishedGood).Where(x => x.FinishedGood.VoucherId == jwo.Id).ToListAsync(ct);
        var number = 0;
        foreach (var sourceLine in outLines)
        {
            var item = await EnsureStockItemAsync(db, sourceLine.StockItemName, sourceLine.UqcName, false, ct);
            var component = components.FirstOrDefault(x => x.StockItemId == item.Id) ?? throw new InvalidOperationException($"Component '{item.Name}' is not present in JWO '{input.PrimaryOrderNumber}'.");
            var source = await EnsureGodownAsync(db, sourceLine.GodownName, ct);
            var previous = await db.MaterialOutLines.Where(x => x.JwoComponentId == component.Id && x.Voucher.Status != "Cancelled").SumAsync(x => (decimal?)x.IssuedQuantity, ct) ?? 0;
            var line = new MaterialOutLine
            {
                VoucherId = voucher.Id, JwoVoucherId = jwo.Id, JwoFinishedGoodId = component.FinishedGoodId,
                JwoComponentId = component.Id, LineNumber = ++number, StockItemId = item.Id, UqcId = item.UqcId,
                SourceGodownId = source.Id, DestinationGodownId = destination.Id, RequiredQuantity = component.RequiredQuantity,
                PreviouslyIssuedQuantity = previous, IssuedQuantity = sourceLine.Quantity, Rate = sourceLine.Rate,
                Amount = Math.Abs(sourceLine.Amount)
            };
            db.MaterialOutLines.Add(line);
            await db.SaveChangesAsync(ct);
            stockPosting.Post(db, NewMovementDraft(voucher, line.Id, item, source.Id, -sourceLine.Quantity, sourceLine.Rate, -Math.Abs(sourceLine.Amount), "MaterialOutSource", component.ComponentVariantId), companyContext.ActorFor("Tally XML"), DateTimeOffset.UtcNow);
            stockPosting.Post(db, NewMovementDraft(voucher, line.Id, item, destination.Id, sourceLine.Quantity, sourceLine.Rate, Math.Abs(sourceLine.Amount), "MaterialOutDestination", component.ComponentVariantId), companyContext.ActorFor("Tally XML"), DateTimeOffset.UtcNow);
        }
        db.VoucherLinks.Add(new VoucherLink { CompanyId = companyContext.CompanyId, SourceVoucherId = jwo.Id, TargetVoucherId = voucher.Id, LinkType = "JWO_TO_MO", CreatedAtUtc = DateTimeOffset.UtcNow, CreatedBy = companyContext.ActorFor("Tally XML") });
        await db.SaveChangesAsync(ct);
        var statusTime = DateTimeOffset.UtcNow;
        if (await JobWorkOrderStatusUpdater.UpdateAsync(db, jwo.Id, statusTime, companyContext.ActorFor("Tally XML"), ct))
        {
            await db.SaveChangesAsync(ct);
            await fullAudit.RecordAsync(
                db, jwo.Id, VoucherAuditActions.SystemStatusUpdate,
                $"Status recalculated after imported Material Out {voucher.VoucherNumber}.",
                companyContext.ActorFor("Tally XML"), statusTime, ct);
        }
        return voucher.Id;
    }

    private async Task<long> ImportMaterialInAsync(TexTrackDbContext db, TallyXmlVoucher input, CancellationToken ct)
    {
        var jwo = await FindJwoAsync(db, input.PrimaryOrderNumber, ct) ?? throw new InvalidOperationException($"Import JWO '{input.PrimaryOrderNumber}' first.");
        var type = await GetVoucherTypeAsync(db, MaterialInCode, ct);
        var party = await EnsureLedgerAsync(db, input.PartyLedgerName, true, ct);
        var voucher = await NewVoucherAsync(db, type, input, party.Id, jwo.Batch, ct);
        var finishedLines = input.InventoryLines.Where(x => x.Direction == "In").ToList();
        var consumedLines = input.InventoryLines.Where(x => x.Direction == "Out").ToList();
        var receivingName = finishedLines.FirstOrDefault()?.GodownName;
        if (string.IsNullOrWhiteSpace(receivingName)) receivingName = input.DestinationGodownName;
        var consumingName = consumedLines.FirstOrDefault()?.GodownName;
        if (string.IsNullOrWhiteSpace(consumingName)) consumingName = input.SourceGodownName;
        var receiving = await EnsureGodownAsync(db, receivingName ?? string.Empty, ct);
        var consuming = await EnsureGodownAsync(db, consumingName ?? string.Empty, ct);
        var materialValue = consumedLines.Sum(x => Math.Abs(x.Amount));
        var finishedValue = finishedLines.Sum(x => Math.Abs(x.Amount));
        db.MaterialInDetails.Add(new MaterialInDetail
        {
            VoucherId = voucher.Id, JwoVoucherId = jwo.Id, ConsumptionGodownId = consuming.Id, ReceivingGodownId = receiving.Id,
            DisplayedOrderNumber = jwo.VoucherNumber, TotalConsumedMaterialValue = materialValue,
            TotalProcessCharge = Math.Max(0, finishedValue - materialValue), TotalFinishedGoodsValue = finishedValue
        });
        var jwoFgs = await db.JobWorkOrderFinishedGoods.Where(x => x.VoucherId == jwo.Id).ToListAsync(ct);
        var fgNo = 0;
        foreach (var finishedLine in finishedLines)
        {
            var item = await EnsureStockItemAsync(db, finishedLine.StockItemName, finishedLine.UqcName, true, ct);
            var jwoFg = jwoFgs.FirstOrDefault(x => x.StockItemId == item.Id) ?? throw new InvalidOperationException($"Finished Good '{item.Name}' is not present in JWO '{input.PrimaryOrderNumber}'.");
            var variant = await EnsureBaseVariantAsync(db, item, ct);
            var previous = await db.MaterialInFinishedGoods.Where(x => x.JwoFinishedGoodId == jwoFg.Id && x.Voucher.Status != "Cancelled").SumAsync(x => (decimal?)x.ReceivedQuantity, ct) ?? 0;
            var share = finishedValue == 0 ? 0 : Math.Abs(finishedLine.Amount) / finishedValue;
            var fg = new MaterialInFinishedGood
            {
                VoucherId = voucher.Id, JwoFinishedGoodId = jwoFg.Id, LineNumber = ++fgNo, StockItemId = item.Id,
                UqcId = item.UqcId, ReceivingGodownId = receiving.Id, OrderedQuantity = jwoFg.OrderedQuantity,
                PreviouslyReceivedQuantity = previous, ReceivedQuantity = finishedLine.Quantity, MaterialValue = materialValue * share,
                ProcessCharge = Math.Max(0, Math.Abs(finishedLine.Amount) - materialValue * share), FinishedGoodsValue = Math.Abs(finishedLine.Amount), Rate = finishedLine.Rate
            };
            db.MaterialInFinishedGoods.Add(fg);
            await db.SaveChangesAsync(ct);
            db.MaterialInFinishedGoodAllocations.Add(new MaterialInFinishedGoodAllocation { FinishedGoodLineId = fg.Id, StockItemVariantId = variant.Id, Quantity = finishedLine.Quantity });
            stockPosting.Post(db, new StockMovementDraft(
                companyContext.CompanyId, companyContext.FinancialYearId, voucher.Id, voucher.VoucherDate,
                item.Id, item.UqcId, receiving.Id, finishedLine.Quantity, finishedLine.Rate, Math.Abs(finishedLine.Amount),
                "MaterialInFinishedGoods", MaterialInFinishedGoodId: fg.Id, StockItemVariantId: variant.Id),
                companyContext.ActorFor("Tally XML"), DateTimeOffset.UtcNow);
        }
        var components = await db.JobWorkOrderComponents.Include(x => x.FinishedGood).Where(x => x.FinishedGood.VoucherId == jwo.Id).ToListAsync(ct);
        var consumptionNo = 0;
        foreach (var consumedLine in consumedLines)
        {
            var item = await EnsureStockItemAsync(db, consumedLine.StockItemName, consumedLine.UqcName, false, ct);
            var component = components.FirstOrDefault(x => x.StockItemId == item.Id) ?? throw new InvalidOperationException($"Component '{item.Name}' is not present in JWO '{input.PrimaryOrderNumber}'.");
            var consumed = new MaterialInConsumption
            {
                VoucherId = voucher.Id, JwoComponentId = component.Id, LineNumber = ++consumptionNo, StockItemId = item.Id,
                UqcId = item.UqcId, ConsumptionGodownId = consuming.Id, AvailableQuantity = consumedLine.Quantity,
                ConsumedQuantity = consumedLine.Quantity, Rate = consumedLine.Rate, Value = Math.Abs(consumedLine.Amount)
            };
            db.MaterialInConsumptions.Add(consumed);
            await db.SaveChangesAsync(ct);
            stockPosting.Post(db, new StockMovementDraft(
                companyContext.CompanyId, companyContext.FinancialYearId, voucher.Id, voucher.VoucherDate,
                item.Id, item.UqcId, consuming.Id, -consumedLine.Quantity, consumedLine.Rate, -Math.Abs(consumedLine.Amount),
                "MaterialInConsumption", MaterialInConsumptionId: consumed.Id,
                StockItemVariantId: component.ComponentVariantId),
                companyContext.ActorFor("Tally XML"), DateTimeOffset.UtcNow);
        }
        db.VoucherLinks.Add(new VoucherLink { CompanyId = companyContext.CompanyId, SourceVoucherId = jwo.Id, TargetVoucherId = voucher.Id, LinkType = "JWO_TO_MI", CreatedAtUtc = DateTimeOffset.UtcNow, CreatedBy = companyContext.ActorFor("Tally XML") });
        await db.SaveChangesAsync(ct);
        var statusTime = DateTimeOffset.UtcNow;
        if (await JobWorkOrderStatusUpdater.UpdateAsync(db, jwo.Id, statusTime, companyContext.ActorFor("Tally XML"), ct))
        {
            await db.SaveChangesAsync(ct);
            await fullAudit.RecordAsync(
                db, jwo.Id, VoucherAuditActions.SystemStatusUpdate,
                $"Status recalculated after imported Material In {voucher.VoucherNumber}.",
                companyContext.ActorFor("Tally XML"), statusTime, ct);
        }
        return voucher.Id;
    }

    private async Task<Voucher> NewVoucherAsync(TexTrackDbContext db, VoucherType type, TallyXmlVoucher input, long partyId, string batch, CancellationToken ct)
    {
        var normalized = Normalize(input.VoucherNumber);
        if (await db.Vouchers.AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId && x.VoucherTypeId == type.Id && x.VoucherNumberNormalized == normalized, ct))
            throw new InvalidOperationException($"Voucher Number '{input.VoucherNumber}' already exists for {type.Name}.");
        var sequence = await db.Vouchers.Where(x => x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId && x.VoucherTypeId == type.Id).MaxAsync(x => (int?)x.SequenceNumber, ct) ?? 0;
        var voucher = new Voucher
        {
            CompanyId = companyContext.CompanyId, FinancialYearId = companyContext.FinancialYearId, VoucherTypeId = type.Id,
            SequenceNumber = sequence + 1, VoucherNumber = input.VoucherNumber, VoucherNumberNormalized = normalized,
            VoucherDate = input.VoucherDate, ReferenceNumber = input.ReferenceNumber, Batch = string.IsNullOrWhiteSpace(batch) ? input.ReferenceNumber : batch,
            PartyLedgerId = partyId, Narration = input.Narration, Status = "Open", CreatedAtUtc = DateTimeOffset.UtcNow,
            ModifiedAtUtc = DateTimeOffset.UtcNow, CreatedBy = companyContext.ActorFor("Tally XML"), ModifiedBy = companyContext.ActorFor("Tally XML"), ConcurrencyToken = Guid.NewGuid().ToString("N")
        };
        db.Vouchers.Add(voucher);
        await db.SaveChangesAsync(ct);
        return voucher;
    }

    private async Task<VoucherType> GetVoucherTypeAsync(TexTrackDbContext db, string code, CancellationToken ct) =>
        await db.VoucherTypes.FirstOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.SystemTypeCode == code, ct)
        ?? throw new InvalidOperationException($"TexTrack voucher type '{code}' is unavailable.");

    private async Task<Ledger> EnsureLedgerAsync(TexTrackDbContext db, string name, bool jobWorker, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Party Ledger Name is missing.");
        var normalized = Normalize(name);
        var existing = await db.Ledgers.FirstOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.NameNormalized == normalized, ct);
        if (existing is not null) { if (jobWorker) existing.IsJobWorker = true; return existing; }
        var group = await EnsureLedgerGroupAsync(db, "Sundry Creditors", ct);
        var entity = new Ledger
        {
            CompanyId = companyContext.CompanyId, LedgerGroupId = group.Id, Name = name.Trim(), NameNormalized = normalized,
            MailingName = name.Trim(), IsJobWorker = jobWorker, TallyLedgerName = name.Trim(), IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow, CreatedBy = companyContext.ActorFor("Tally XML"), ModifiedBy = companyContext.ActorFor("Tally XML")
        };
        db.Ledgers.Add(entity); await db.SaveChangesAsync(ct); return entity;
    }

    private async Task<LedgerGroup> EnsureLedgerGroupAsync(TexTrackDbContext db, string name, CancellationToken ct)
    {
        var normalized = Normalize(name);
        var existing = await db.LedgerGroups.FirstOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.NameNormalized == normalized, ct);
        if (existing is not null) return existing;
        var entity = new LedgerGroup { CompanyId = companyContext.CompanyId, Name = name, NameNormalized = normalized, RootClassification = "Liabilities", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow, CreatedBy = companyContext.ActorFor("Tally XML"), ModifiedBy = companyContext.ActorFor("Tally XML") };
        db.LedgerGroups.Add(entity); await db.SaveChangesAsync(ct); return entity;
    }

    private async Task<StockItem> EnsureStockItemAsync(TexTrackDbContext db, string name, string uqcName, bool finishedGood, CancellationToken ct)
    {
        var normalized = Normalize(name);
        var existing = await db.StockItems.FirstOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.NameNormalized == normalized, ct);
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(uqcName))
            {
                var requestedUqc = Normalize(uqcName);
                var existingUqc = await db.Uqcs.AsNoTracking().SingleAsync(x => x.Id == existing.UqcId, ct);
                if (existingUqc.NameNormalized != requestedUqc && !existingUqc.ShortName.Equals(uqcName.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Stock Item '{existing.Name}' uses UQC '{existingUqc.ShortName}' in TexTrack but the XML uses '{uqcName.Trim()}'.");
            }
            return existing;
        }
        var uqc = await EnsureUqcAsync(db, uqcName, ct);
        var groupName = finishedGood ? "Finished Goods" : "Raw Material";
        var groupNormalized = Normalize(groupName);
        var group = await db.StockGroups.FirstOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.NameNormalized == groupNormalized, ct);
        if (group is null)
        {
            group = new StockGroup { CompanyId = companyContext.CompanyId, Name = groupName, NameNormalized = groupNormalized, RootClassification = finishedGood ? "FinishedGoods" : "RawMaterial", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow, CreatedBy = companyContext.ActorFor("Tally XML"), ModifiedBy = companyContext.ActorFor("Tally XML") };
            db.StockGroups.Add(group); await db.SaveChangesAsync(ct);
        }
        var entity = new StockItem { CompanyId = companyContext.CompanyId, Name = name.Trim(), NameNormalized = normalized, StockGroupId = group.Id, UqcId = uqc.Id, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow, CreatedBy = companyContext.ActorFor("Tally XML"), ModifiedBy = companyContext.ActorFor("Tally XML") };
        db.StockItems.Add(entity); await db.SaveChangesAsync(ct); await EnsureBaseVariantAsync(db, entity, ct); return entity;
    }

    private async Task<Uqc> EnsureUqcAsync(TexTrackDbContext db, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "NOS";
        var normalized = Normalize(name);
        var existing = await db.Uqcs.FirstOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId &&
            (x.NameNormalized == normalized || x.ShortName.ToUpper() == normalized), ct);
        if (existing is not null) return existing;
        var entity = new Uqc { CompanyId = companyContext.CompanyId, Name = name.Trim(), NameNormalized = normalized, ShortName = name.Trim(), DecimalPlaces = 4, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow, CreatedBy = companyContext.ActorFor("Tally XML"), ModifiedBy = companyContext.ActorFor("Tally XML") };
        db.Uqcs.Add(entity); await db.SaveChangesAsync(ct); return entity;
    }

    private async Task<Godown> EnsureGodownAsync(TexTrackDbContext db, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "Main Location";
        var normalized = Normalize(name);
        var existing = await db.Godowns.FirstOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.NameNormalized == normalized, ct);
        if (existing is not null) return existing;
        var entity = new Godown { CompanyId = companyContext.CompanyId, Name = name.Trim(), NameNormalized = normalized, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow, CreatedBy = companyContext.ActorFor("Tally XML"), ModifiedBy = companyContext.ActorFor("Tally XML") };
        db.Godowns.Add(entity); await db.SaveChangesAsync(ct); return entity;
    }

    private async Task<StockItemVariant> EnsureBaseVariantAsync(TexTrackDbContext db, StockItem item, CancellationToken ct)
    {
        var existing = await db.StockItemVariants.FirstOrDefaultAsync(x => x.StockItemId == item.Id && x.VariantKey == "BASE", ct);
        if (existing is not null) return existing;
        var entity = new StockItemVariant { CompanyId = companyContext.CompanyId, StockItemId = item.Id, VariantKey = "BASE", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow, CreatedBy = companyContext.ActorFor("Tally XML"), ModifiedBy = companyContext.ActorFor("Tally XML") };
        db.StockItemVariants.Add(entity); await db.SaveChangesAsync(ct); return entity;
    }

    private static StockMovementDraft NewMovementDraft(
        Voucher voucher,
        long lineId,
        StockItem item,
        long godownId,
        decimal qty,
        decimal rate,
        decimal value,
        string kind,
        long? stockItemVariantId) => new(
        voucher.CompanyId, voucher.FinancialYearId, voucher.Id, voucher.VoucherDate, item.Id, item.UqcId, godownId,
        qty, rate, value, kind, MaterialOutLineId: lineId, StockItemVariantId: stockItemVariantId);

    private async Task<Voucher?> FindJwoAsync(TexTrackDbContext db, string orderNumber, CancellationToken ct) => await db.Vouchers
        .Include(x => x.VoucherType).FirstOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId &&
            x.VoucherType.SystemTypeCode == JwoCode && x.Status != "Cancelled" &&
            (x.VoucherNumberNormalized == Normalize(orderNumber) || x.ReferenceNumber.ToUpper() == orderNumber.ToUpper()), ct);

    private async Task CancelImportedVoucherAsync(TexTrackDbContext db, long voucherId, CancellationToken ct)
    {
        var voucher = await db.Vouchers.FirstAsync(x => x.Id == voucherId, ct);
        if (voucher.Status == "Cancelled") return;
        var jwoId = await db.MaterialOutLines.AsNoTracking()
            .Where(x => x.VoucherId == voucherId)
            .Select(x => (long?)x.JwoVoucherId)
            .FirstOrDefaultAsync(ct)
            ?? await db.MaterialInDetails.AsNoTracking()
                .Where(x => x.VoucherId == voucherId)
                .Select(x => (long?)x.JwoVoucherId)
                .FirstOrDefaultAsync(ct);
        var hasActiveDownstream = await lifecycle.HasActiveLinksAsync(db, companyContext.CompanyId, voucherId, VoucherLinkScope.Downstream, ct);
        if (hasActiveDownstream) throw new InvalidOperationException($"Cancel downstream vouchers before cancelling '{voucher.VoucherNumber}'.");
        var now = DateTimeOffset.UtcNow;
        await stockPosting.ReverseVoucherPostingsAsync(db, voucherId, null, "TallyCancellation", companyContext.ActorFor("Tally XML"), now, ct);
        lifecycle.MarkCancelled(voucher, "Cancelled in TallyPrime", companyContext.ActorFor("Tally XML"), now);
        db.AuditLogs.Add(VoucherLifecycleService.NewAudit(
            voucher.CompanyId, voucher.Id, "Tally XML cancellation", true,
            $"Voucher {voucher.VoucherNumber} cancelled from Tally XML.", companyContext.ActorFor("Tally XML"), now));
        await db.SaveChangesAsync(ct);
        if (jwoId is long parentId &&
            await JobWorkOrderStatusUpdater.UpdateAsync(db, parentId, now, companyContext.ActorFor("Tally XML"), ct))
        {
            await db.SaveChangesAsync(ct);
            await fullAudit.RecordAsync(
                db, parentId, VoucherAuditActions.SystemStatusUpdate,
                $"Status recalculated after imported voucher {voucher.VoucherNumber} was cancelled.",
                companyContext.ActorFor("Tally XML"), now, ct);
        }
    }

    private async Task RemoveImportedVoucherAsync(TexTrackDbContext db, long voucherId, CancellationToken ct)
    {
        var voucher = await db.Vouchers.FirstAsync(x => x.Id == voucherId, ct);
        await fullAudit.RecordAsync(
            db, voucher.Id, VoucherAuditActions.Delete,
            "Replaced by a changed Tally XML voucher with the same Tally identity.",
            companyContext.ActorFor("Tally XML"), DateTimeOffset.UtcNow, ct);
        stockPosting.RemoveVoucherPostings(db, voucherId);
        db.Vouchers.Remove(voucher); await db.SaveChangesAsync(ct);
    }

    private void AddException(TexTrackDbContext db, long batchId, TallyImportPreviewRow row, string code, string? message = null) => db.TallyImportExceptions.Add(new TallyImportException
    {
        CompanyId = companyContext.CompanyId, ExchangeBatchId = batchId, TallyGuid = row.Guid, VoucherTypeName = row.VoucherTypeName,
        VoucherNumber = row.VoucherNumber, OrderNumber = row.OrderNumber, ReasonCode = code, Message = message ?? row.Message,
        PayloadXml = row.Voucher.RawXml, Status = "Open", CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow,
        CreatedBy = companyContext.ActorFor("Tally XML"), ModifiedBy = companyContext.ActorFor("Tally XML")
    });

    private async Task<int> CountMissingMastersAsync(TexTrackDbContext db, TallyXmlDocument document, CancellationToken ct)
    {
        var ledgers = document.Vouchers.Select(x => Normalize(x.PartyLedgerName))
            .Concat(document.Masters.Where(x => x.Type == "LEDGER").Select(x => Normalize(x.Name))).Where(x => x.Length > 0).Distinct().ToList();
        var items = document.Vouchers.SelectMany(x => x.AllInventoryLines).Select(x => Normalize(x.StockItemName))
            .Concat(document.Masters.Where(x => x.Type == "STOCKITEM").Select(x => Normalize(x.Name))).Where(x => x.Length > 0).Distinct().ToList();
        var godowns = document.Vouchers.SelectMany(x => x.AllInventoryLines)
            .SelectMany(x => new[] { Normalize(x.GodownName), Normalize(x.DestinationGodownName) })
            .Concat(document.Masters.Where(x => x.Type == "GODOWN").Select(x => Normalize(x.Name))).Where(x => x.Length > 0).Distinct().ToList();
        var uqcs = document.Vouchers.SelectMany(x => x.AllInventoryLines).Select(x => Normalize(x.UqcName))
            .Concat(document.Masters.Where(x => x.Type == "UNIT").Select(x => Normalize(x.Name))).Where(x => x.Length > 0).Distinct().ToList();
        var ledgerGroups = document.Masters.Where(x => x.Type == "GROUP").Select(x => Normalize(x.Name)).Where(x => x.Length > 0).Distinct().ToList();
        var stockGroups = document.Masters.Where(x => x.Type == "STOCKGROUP").Select(x => Normalize(x.Name)).Where(x => x.Length > 0).Distinct().ToList();
        var categories = document.Masters.Where(x => x.Type == "STOCKCATEGORY").Select(x => Normalize(x.Name)).Where(x => x.Length > 0).Distinct().ToList();
        var voucherTypes = document.Masters.Where(x => x.Type == "VOUCHERTYPE").Select(x => Normalize(x.Name)).Where(x => x.Length > 0).Distinct().ToList();
        var existingLedgers = await db.Ledgers.CountAsync(x => x.CompanyId == companyContext.CompanyId && ledgers.Contains(x.NameNormalized), ct);
        var existingItems = await db.StockItems.CountAsync(x => x.CompanyId == companyContext.CompanyId && items.Contains(x.NameNormalized), ct);
        var existingGodowns = await db.Godowns.CountAsync(x => x.CompanyId == companyContext.CompanyId && godowns.Contains(x.NameNormalized), ct);
        var existingUqcs = await db.Uqcs.CountAsync(x => x.CompanyId == companyContext.CompanyId &&
            (uqcs.Contains(x.NameNormalized) || uqcs.Contains(x.ShortName.ToUpper())), ct);
        var existingLedgerGroups = await db.LedgerGroups.CountAsync(x => x.CompanyId == companyContext.CompanyId && ledgerGroups.Contains(x.NameNormalized), ct);
        var existingStockGroups = await db.StockGroups.CountAsync(x => x.CompanyId == companyContext.CompanyId && stockGroups.Contains(x.NameNormalized), ct);
        var existingCategories = await db.StockCategories.CountAsync(x => x.CompanyId == companyContext.CompanyId && categories.Contains(x.NameNormalized), ct);
        var existingVoucherTypes = await db.VoucherTypes.CountAsync(x => x.CompanyId == companyContext.CompanyId && voucherTypes.Contains(x.NameNormalized), ct);
        return ledgers.Count + items.Count + godowns.Count + uqcs.Count + ledgerGroups.Count + stockGroups.Count + categories.Count + voucherTypes.Count
            - existingLedgers - existingItems - existingGodowns - existingUqcs - existingLedgerGroups - existingStockGroups - existingCategories - existingVoucherTypes;
    }

    private async Task<bool> JwoExistsAsync(TexTrackDbContext db, string number, CancellationToken ct) => await FindJwoAsync(db, number, ct) is not null;
    private async Task<bool> VoucherNumberExistsAsync(TexTrackDbContext db, TallyXmlVoucher voucher, CancellationToken ct)
    {
        var code = voucher.VoucherTypeName switch { "Job Work Out Order" => JwoCode, "Material Out" => MaterialOutCode, "Material In" => MaterialInCode, _ => "" };
        return await db.Vouchers.AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.FinancialYearId == companyContext.FinancialYearId && x.VoucherType.SystemTypeCode == code && x.VoucherNumberNormalized == Normalize(voucher.VoucherNumber), ct);
    }

    private static string Normalize(string value) => string.Join(' ', (value ?? string.Empty).Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
