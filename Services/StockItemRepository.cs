using System.Data;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed class StockItemRepository(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext,
    StockPostingService stockPosting,
    VoucherSequenceAllocator sequenceAllocator,
    VoucherAuditHistoryService voucherAuditHistory,
    InventoryPeriodControlService periodControl)
{
    public async Task<PagedReportResult<StockItemListItem>> GetPageAsync(
        string? search = null,
        int page = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedSearch = Normalize(search ?? string.Empty);
        pageSize = Math.Clamp(pageSize, 1, 250);
        page = Math.Max(1, page);

        var query = db.StockItems
            .AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId);

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(x =>
                x.NameNormalized.Contains(normalizedSearch) ||
                x.Alias.ToUpper().Contains(normalizedSearch) ||
                x.HsnCode.ToUpper().Contains(normalizedSearch));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        var rows = await query
            .OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new StockItemListItem
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                StockGroupName = x.StockGroup.Name,
                UqcShortName = x.Uqc.ShortName,
                StockCategoryName = x.StockCategory == null ? string.Empty : x.StockCategory.Name,
                TaxMode = x.TaxMode,
                HsnCode = x.HsnCode,
                ColourCount = x.Colours.Count,
                SizeCount = x.Sizes.Count,
                VariantCount = x.Variants.Count(v => v.IsActive),
                ConcurrencyToken = x.ConcurrencyToken
            })
            .ToListAsync(cancellationToken);

        return new PagedReportResult<StockItemListItem>
        {
            Rows = rows,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<IReadOnlyList<StockItemListItem>> GetListAsync(
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedSearch = Normalize(search ?? string.Empty);

        var query = db.StockItems
            .AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId);

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(x =>
                x.NameNormalized.Contains(normalizedSearch) ||
                x.Alias.ToUpper().Contains(normalizedSearch) ||
                x.HsnCode.ToUpper().Contains(normalizedSearch));
        }

        return await query
            .OrderBy(x => x.Name)
            .Select(x => new StockItemListItem
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                StockGroupName = x.StockGroup.Name,
                UqcShortName = x.Uqc.ShortName,
                StockCategoryName = x.StockCategory == null ? string.Empty : x.StockCategory.Name,
                TaxMode = x.TaxMode,
                HsnCode = x.HsnCode,
                ColourCount = x.Colours.Count,
                SizeCount = x.Sizes.Count,
                VariantCount = x.Variants.Count(v => v.IsActive),
                ConcurrencyToken = x.ConcurrencyToken
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<StockItemLookupData> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = companyContext.CompanyId;

        var groups = await db.StockGroups.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive && x.RootClassification != "WorkInProgress")
            .OrderByDescending(x => x.IsSystem)
            .ThenBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name, x.RootClassification))
            .ToListAsync(cancellationToken);

        var uqcs = await db.Uqcs.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name, x.ShortName))
            .ToListAsync(cancellationToken);

        var categories = await db.StockCategories.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name, string.Empty))
            .ToListAsync(cancellationToken);

        var taxes = await db.TaxClassifications.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name, x.TaxMode))
            .ToListAsync(cancellationToken);

        var colours = await db.Colours.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name, x.ColourCode))
            .ToListAsync(cancellationToken);

        var sizes = await db.Sizes.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name, x.DisplayOrder.ToString()))
            .ToListAsync(cancellationToken);

        var godowns = await db.Godowns.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .OrderByDescending(x => x.IsSystem)
            .ThenBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name, x.Alias))
            .ToListAsync(cancellationToken);

        var booksBeginningDate = await db.FinancialYears.AsNoTracking()
            .Where(x => x.Id == companyContext.FinancialYearId && x.CompanyId == companyId)
            .Select(x => x.StartDate)
            .SingleAsync(cancellationToken);

        var customFields = await db.StockItemFieldDefinitions.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new StockItemFieldDefinitionModel
            {
                Id = x.Id, Name = x.Name, FieldType = x.FieldType, DisplayOrder = x.DisplayOrder,
                IsRequired = x.IsRequired, AllowMultiple = x.AllowMultiple, OptionsText = x.OptionsText
            }).ToListAsync(cancellationToken);

        return new StockItemLookupData
        {
            StockGroups = groups,
            Uqcs = uqcs,
            StockCategories = categories,
            TaxClassifications = taxes,
            Colours = colours,
            Sizes = sizes,
            Godowns = godowns,
            CustomFieldDefinitions = customFields,
            BooksBeginningDate = booksBeginningDate
        };
    }

    public async Task<StockItemEditModel?> GetForEditAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.StockItems
            .AsNoTracking()
            .Include(x => x.Colours)
            .Include(x => x.Sizes)
            .Include(x => x.Variants).ThenInclude(x => x.Colour)
            .Include(x => x.Variants).ThenInclude(x => x.Size)
            .Include(x => x.FieldValues)
            .Include(x => x.Photos)
            .Include(x => x.DesignPhoto)
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyContext.CompanyId && x.Id == id,
                cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var isUqcLocked = await HasTransactionalUsageAsync(db, entity.Id, cancellationToken);
        var openingVoucher = await db.Vouchers.AsNoTracking()
            .Include(x => x.InventoryInwardLines)
            .SingleOrDefaultAsync(x =>
                x.CompanyId == companyContext.CompanyId &&
                x.FinancialYearId == companyContext.FinancialYearId &&
                x.OpeningStockItemId == entity.Id,
                cancellationToken);
        return new StockItemEditModel
        {
            Id = entity.Id,
            Name = entity.Name,
            Alias = entity.Alias,
            StockGroupId = entity.StockGroupId,
            UqcId = entity.UqcId,
            StockCategoryId = entity.StockCategoryId,
            TaxMode = entity.TaxMode,
            TaxClassificationId = entity.TaxClassificationId,
            HsnCode = entity.HsnCode,
            IgstRate = entity.IgstRate,
            CgstRate = entity.CgstRate,
            SgstRate = entity.SgstRate,
            CostPrice = entity.CostPrice,
            SalePrice = entity.SalePrice,
            ColourIds = entity.Colours.Select(x => x.ColourId).ToHashSet(),
            SizeIds = entity.Sizes.Select(x => x.SizeId).ToHashSet(),
            DesignPhoto = entity.DesignPhoto is null ? null : new StockItemDesignPhotoModel
            {
                Id = entity.DesignPhoto.Id,
                FileName = entity.DesignPhoto.FileName,
                ContentType = entity.DesignPhoto.ContentType
            },
            CustomFields = await BuildCustomFieldsAsync(db, entity, cancellationToken),
            OpeningStockVoucherId = openingVoucher?.Id,
            OpeningStockConcurrencyToken = openingVoucher?.ConcurrencyToken ?? string.Empty,
            OpeningInventory = openingVoucher?.InventoryInwardLines
                .OrderBy(x => x.LineNumber)
                .Select(x => new StockItemOpeningAllocationEditModel
                {
                    VariantKey = entity.Variants.Single(v => v.Id == x.StockItemVariantId).VariantKey,
                    GodownId = x.GodownId,
                    Quantity = x.Quantity,
                    Rate = x.Rate,
                    Value = x.Amount,
                    CalculationBasis = "Value"
                }).ToList() ?? [],
            IsUqcLocked = isUqcLocked,
            ConcurrencyToken = entity.ConcurrencyToken
        };
    }

    public async Task<OperationResult> SaveAsync(
        StockItemEditModel model,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ManageMasters, cancellationToken);
        var actor = companyContext.Actor;
        NormalizeModel(model);
        var validation = ValidateModel(model);
        if (validation is not null)
        {
            return OperationResult.Fail(validation);
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var companyId = companyContext.CompanyId;
        var normalizedName = Normalize(model.Name);

        if (await db.StockItems.AnyAsync(
                x => x.CompanyId == companyId && x.NameNormalized == normalizedName && x.Id != model.Id,
                cancellationToken))
        {
            return await FailWithAuditAsync(db, model.Id, $"A Stock Item named '{model.Name}' already exists.", cancellationToken);
        }

        var selectedGroup = await db.StockGroups.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == model.StockGroupId, cancellationToken);
        if (selectedGroup is null)
        {
            return await FailWithAuditAsync(db, model.Id, "Select a valid Stock Group.", cancellationToken);
        }
        if (string.Equals(selectedGroup.RootClassification, "WorkInProgress", StringComparison.OrdinalIgnoreCase))
        {
            return await FailWithAuditAsync(db, model.Id,
                "Work In Progress is transaction-derived. Stock Items cannot be manually created in or moved to the WIP group.",
                cancellationToken);
        }

        if (!await db.Uqcs.AnyAsync(x => x.CompanyId == companyId && x.Id == model.UqcId, cancellationToken))
        {
            return await FailWithAuditAsync(db, model.Id, "Select a valid UQC. Create the UQC first if the list is empty.", cancellationToken);
        }

        if (model.StockCategoryId is not null &&
            !await db.StockCategories.AnyAsync(x => x.CompanyId == companyId && x.Id == model.StockCategoryId, cancellationToken))
        {
            return await FailWithAuditAsync(db, model.Id, "The selected Stock Category does not exist.", cancellationToken);
        }

        if (model.TaxMode == "InventoryTaxGroup")
        {
            var validTaxGroup = model.TaxClassificationId is not null &&
                await db.TaxClassifications.AnyAsync(
                    x => x.CompanyId == companyId &&
                         x.Id == model.TaxClassificationId.Value &&
                         x.TaxMode == "InventoryTaxGroup",
                    cancellationToken);
            if (!validTaxGroup)
            {
                return await FailWithAuditAsync(db, model.Id, "Select a valid user-defined Inventory Tax Group.", cancellationToken);
            }
        }

        if (model.ColourIds.Count != await db.Colours.CountAsync(
                x => x.CompanyId == companyId && model.ColourIds.Contains(x.Id), cancellationToken))
        {
            return await FailWithAuditAsync(db, model.Id, "One or more selected Colours no longer exist.", cancellationToken);
        }

        if (model.SizeIds.Count != await db.Sizes.CountAsync(
                x => x.CompanyId == companyId && model.SizeIds.Contains(x.Id), cancellationToken))
        {
            return await FailWithAuditAsync(db, model.Id, "One or more selected Sizes no longer exist.", cancellationToken);
        }

        var openingRows = ActiveOpeningRows(model);
        var validOpening = await ValidateOpeningInventoryAsync(db, model, openingRows, cancellationToken);
        if (validOpening is not null)
        {
            return await FailWithAuditAsync(db, model.Id, validOpening, cancellationToken);
        }

        StockItem entity;
        var isNew = model.Id == 0;
        if (isNew)
        {
            entity = new StockItem
            {
                CompanyId = companyId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                CreatedBy = actor,
                IsActive = true,
                IsSystem = false
            };
            db.StockItems.Add(entity);
        }
        else
        {
            entity = await db.StockItems
                .Include(x => x.Colours)
                .Include(x => x.Sizes)
                .Include(x => x.Variants)
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == model.Id, cancellationToken)
                ?? throw new InvalidOperationException("The selected Stock Item no longer exists.");

            if (!string.Equals(entity.ConcurrencyToken, model.ConcurrencyToken, StringComparison.Ordinal))
            {
                return await FailWithAuditAsync(db, model.Id, "This Stock Item was changed by another user. Reload and try again.", cancellationToken);
            }

            if (entity.UqcId != model.UqcId &&
                await HasTransactionalUsageAsync(db, entity.Id, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return OperationResult.Fail("Cannot change UQC because this Stock Item has already been used or linked.");
            }

            if (entity.StockGroupId != model.StockGroupId)
            {
                var usedInJobWork = await db.JobWorkOrderFinishedGoods.AnyAsync(
                                        x => x.StockItemId == entity.Id,
                                        cancellationToken) ||
                                    await db.JobWorkOrderComponents.AnyAsync(
                                        x => x.StockItemId == entity.Id,
                                        cancellationToken);
                if (usedInJobWork)
                {
                    return await FailWithAuditAsync(db, model.Id,
                        "Stock Group and UQC cannot be changed because this Stock Item is already used in a Job Work Out Order.",
                        cancellationToken);
                }
            }

            var usedVariantIds = await GetUsedVariantIdsAsync(db, entity.Id, cancellationToken);
            if (usedVariantIds.Count > 0)
            {
                var desiredKeys = BuildVariantKeys(model.ColourIds, model.SizeIds);
                var removesUsedVariant = entity.Variants
                    .Where(x => usedVariantIds.Contains(x.Id))
                    .Any(x => !desiredKeys.ContainsKey(x.VariantKey));
                if (removesUsedVariant)
                {
                    return await FailWithAuditAsync(db, model.Id,
                        "A Colour or Size with voucher or stock history cannot be removed. New Colours and Sizes may still be added.",
                        cancellationToken);
                }
            }
        }

        var desiredVariantCount = BuildVariantKeys(model.ColourIds, model.SizeIds).Count;
        try
        {
            ApplyFields(entity, model, actor);
            await db.SaveChangesAsync(cancellationToken);

            await SynchronizeColoursAsync(db, entity, model.ColourIds, cancellationToken);
            await SynchronizeSizesAsync(db, entity, model.SizeIds, cancellationToken);
            await SynchronizeVariantsAsync(db, entity, model.ColourIds, model.SizeIds, actor, cancellationToken);
            await SynchronizeCustomFieldsAsync(db, entity, model.CustomFields, actor, cancellationToken);
            await SynchronizeDesignPhotoAsync(db, entity, model.DesignPhoto, actor, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            await SynchronizeOpeningInventoryAsync(
                db, entity, model, openingRows, actor, cancellationToken);

            AddAudit(db, entity.Id, isNew ? "Create" : "Update", true,
                $"{(isNew ? "Created" : "Updated")} Stock Item '{entity.Name}' with {desiredVariantCount} hidden variant(s) and {openingRows.Count} opening allocation(s).");

            await db.SaveChangesAsync(cancellationToken);
            await stockPosting.EnsureNegativeStockPolicyAsync(
                db, companyContext.CompanyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"{entity.Name} saved with {desiredVariantCount} hidden variant(s) and {openingRows.Count} opening allocation(s).", entity.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail("This Stock Item was changed by another user. Reload and try again.");
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail($"The Stock Item could not be saved because it conflicts with linked data. {exception.GetBaseException().Message}");
        }
    }

    public async Task<OperationResult> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ManageMasters, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.StockItems.SingleOrDefaultAsync(
            x => x.CompanyId == companyContext.CompanyId && x.Id == id,
            cancellationToken);
        if (entity is null)
        {
            return OperationResult.Fail("The selected Stock Item no longer exists.");
        }

        db.StockItems.Remove(entity);
        AddAudit(db, id, "Delete", true, $"Deleted unused Stock Item '{entity.Name}'.");
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return OperationResult.Ok($"{entity.Name} deleted.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Fail("This Stock Item is linked to stock, vouchers or other records and cannot be deleted.");
        }
    }

    private static void NormalizeModel(StockItemEditModel model)
    {
        model.Name = model.Name.Trim();
        model.Alias = model.Alias.Trim();
        model.HsnCode = model.HsnCode.Trim().ToUpperInvariant();
        model.TaxMode = model.TaxMode.Trim();
        if (model.CostPrice is decimal cost) model.CostPrice = decimal.Round(cost, 4, MidpointRounding.AwayFromZero);
        if (model.SalePrice is decimal sale) model.SalePrice = decimal.Round(sale, 4, MidpointRounding.AwayFromZero);
        if (model.TaxMode != "InventoryTaxGroup")
        {
            model.TaxClassificationId = null;
        }
        if (model.TaxMode != "DirectRates")
        {
            model.IgstRate = 0;
            model.CgstRate = 0;
            model.SgstRate = 0;
        }
        foreach (var row in model.OpeningInventory)
        {
            row.VariantKey = string.IsNullOrWhiteSpace(row.VariantKey) ? "BASE" : row.VariantKey.Trim();
            row.CalculationBasis = string.Equals(row.CalculationBasis, "Value", StringComparison.OrdinalIgnoreCase)
                ? "Value" : "Rate";
            row.Quantity = decimal.Round(row.Quantity, 4, MidpointRounding.AwayFromZero);
            if (row.CalculationBasis == "Value")
            {
                row.Value = decimal.Round(row.Value, 4, MidpointRounding.AwayFromZero);
                row.Rate = row.Quantity > 0
                    ? decimal.Round(row.Value / row.Quantity, 4, MidpointRounding.AwayFromZero)
                    : decimal.Round(row.Rate, 4, MidpointRounding.AwayFromZero);
            }
            else
            {
                row.Rate = decimal.Round(row.Rate, 4, MidpointRounding.AwayFromZero);
                row.Value = decimal.Round(row.Quantity * row.Rate, 4, MidpointRounding.AwayFromZero);
            }
        }
    }

    private static string? ValidateModel(StockItemEditModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name)) return "Stock Item Name is required.";
        if (model.StockGroupId <= 0) return "Stock Group is required.";
        if (model.UqcId <= 0) return "UQC is required.";
        if (model.HsnCode.Length > 20) return "HSN Code cannot exceed 20 characters.";
        if (model.CostPrice is < 0) return "Cost Price cannot be negative.";
        if (model.SalePrice is < 0) return "Sale Price cannot be negative.";
        if (model.TaxMode is not ("NotApplicable" or "InventoryTaxGroup" or "DirectRates")) return "Select a valid Tax Classification mode.";
        if (model.TaxMode == "InventoryTaxGroup" && model.TaxClassificationId is null) return "Inventory Tax Group is required for the selected tax mode.";
        if (model.TaxMode == "DirectRates" &&
            (model.IgstRate is < 0 or > 100 || model.CgstRate is < 0 or > 100 || model.SgstRate is < 0 or > 100))
        {
            return "GST rates must be between 0 and 100.";
        }
        foreach (var field in model.CustomFields)
        {
            if (field.IsRequired && field.FieldType != "Photo" && string.IsNullOrWhiteSpace(field.Value))
                return $"{field.Name} is required.";
            if (field.IsRequired && field.FieldType == "Photo" && field.Photos.Count == 0)
                return $"{field.Name} is required.";
            if (field.Value.Length > 4000) return $"{field.Name} cannot exceed 4000 characters.";
        }
        return null;
    }

    private static List<StockItemOpeningAllocationEditModel> ActiveOpeningRows(StockItemEditModel model) =>
        model.OpeningInventory
            .Where(x => x.Quantity != 0 || x.Rate != 0 || x.Value != 0)
            .ToList();

    private async Task<string?> ValidateOpeningInventoryAsync(
        TexTrackDbContext db,
        StockItemEditModel model,
        IReadOnlyCollection<StockItemOpeningAllocationEditModel> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return null;
        if (rows.Any(x => x.Quantity <= 0))
            return "Every opening allocation with a Rate or Value must have Quantity greater than zero.";
        if (rows.Any(x => x.Rate < 0 || x.Value < 0))
            return "Opening Rate and Value cannot be negative.";
        if (rows.Any(x => x.GodownId <= 0))
            return "Select a Godown for every opening allocation.";

        var validVariantKeys = BuildVariantKeys(model.ColourIds, model.SizeIds).Keys.ToHashSet(StringComparer.Ordinal);
        if (rows.Any(x => !validVariantKeys.Contains(x.VariantKey)))
            return "An opening allocation refers to a Colour/Size variant that is no longer selected.";
        if (rows.GroupBy(x => new { x.VariantKey, x.GodownId }).Any(x => x.Count() > 1))
            return "The same Colour/Size variant and Godown cannot appear twice in opening inventory.";

        var godownIds = rows.Select(x => x.GodownId).Distinct().ToList();
        var validGodownCount = await db.Godowns.AsNoTracking().CountAsync(x =>
            x.CompanyId == companyContext.CompanyId && x.IsActive && godownIds.Contains(x.Id),
            cancellationToken);
        return validGodownCount == godownIds.Count
            ? null
            : "One or more opening-inventory Godowns are unavailable, inactive or belong to another company.";
    }

    private async Task SynchronizeOpeningInventoryAsync(
        TexTrackDbContext db,
        StockItem entity,
        StockItemEditModel model,
        IReadOnlyCollection<StockItemOpeningAllocationEditModel> rows,
        string actor,
        CancellationToken cancellationToken)
    {
        var financialYear = await db.FinancialYears.AsNoTracking().SingleAsync(x =>
            x.Id == companyContext.FinancialYearId && x.CompanyId == companyContext.CompanyId,
            cancellationToken);
        var existing = await db.Vouchers
            .Include(x => x.VoucherType).ThenInclude(x => x.ParentVoucherType)
            .Include(x => x.InventoryInwardLines)
            .SingleOrDefaultAsync(x =>
                x.CompanyId == companyContext.CompanyId &&
                x.FinancialYearId == companyContext.FinancialYearId &&
                x.OpeningStockItemId == entity.Id,
                cancellationToken);

        if (existing?.Id != model.OpeningStockVoucherId ||
            (existing is not null && !string.Equals(
                existing.ConcurrencyToken, model.OpeningStockConcurrencyToken, StringComparison.Ordinal)))
            throw new InvalidOperationException("This Stock Item opening inventory was changed by another operation. Reopen it and try again.");

        var variants = await db.StockItemVariants.AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.StockItemId == entity.Id && x.IsActive)
            .ToDictionaryAsync(x => x.VariantKey, StringComparer.Ordinal, cancellationToken);
        var normalized = rows.Select(x =>
        {
            if (!variants.TryGetValue(x.VariantKey, out var variant))
                throw new InvalidOperationException("An opening allocation refers to a Stock Item variant that is unavailable.");
            return new OpeningPostingLine(
                variant.Id, x.GodownId, x.Quantity, x.Rate, x.Value);
        }).OrderBy(x => x.StockItemVariantId).ThenBy(x => x.GodownId).ToList();

        if (existing is not null && OpeningLinesEqual(existing.InventoryInwardLines, normalized))
            return;
        if (existing is null && normalized.Count == 0)
            return;

        await periodControl.EnsurePostingDateIsOpenAsync(
            db, companyContext.CompanyId, financialYear.StartDate,
            $"Opening inventory for {entity.Name}", cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (normalized.Count == 0)
        {
            await voucherAuditHistory.RecordAsync(
                db, existing!.Id, VoucherAuditActions.Delete,
                $"Opening inventory removed from Stock Item master '{entity.Name}'.",
                actor, now, cancellationToken);
            stockPosting.RemoveVoucherPostings(db, existing.Id);
            db.InventoryInwardLines.RemoveRange(existing.InventoryInwardLines);
            db.Vouchers.Remove(existing);
            db.AuditLogs.Add(VoucherLifecycleService.NewAudit(
                companyContext.CompanyId, existing.Id, "Delete", true,
                $"Removed Opening Stock for Stock Item '{entity.Name}'.", actor, now));
            return;
        }

        if (existing is not null && existing.Status == VoucherLifecycleService.CancelledStatus)
            throw new InvalidOperationException("Cancelled opening inventory cannot be altered from the Stock Item master.");

        Voucher voucher;
        if (existing is null)
        {
            var voucherType = await db.VoucherTypes.SingleAsync(x =>
                x.CompanyId == companyContext.CompanyId && x.IsActive &&
                x.SystemTypeCode == "OPENING_STOCK",
                cancellationToken);
            var sequence = await sequenceAllocator.ReserveAsync(db, voucherType, actor, now, cancellationToken);
            var number = FormatAutomaticNumber(voucherType, sequence);
            voucher = new Voucher
            {
                CompanyId = companyContext.CompanyId,
                FinancialYearId = companyContext.FinancialYearId,
                VoucherTypeId = voucherType.Id,
                SequenceNumber = sequence,
                VoucherNumber = number,
                VoucherNumberNormalized = Normalize(number),
                VoucherDate = financialYear.StartDate,
                ReferenceNumber = $"ITEM-{entity.Id}",
                OpeningStockItemId = entity.Id,
                Narration = "Opening inventory entered from Stock Item master.",
                Status = VoucherLifecycleService.OpenStatus,
                CreatedAtUtc = now,
                ModifiedAtUtc = now,
                CreatedBy = actor,
                ModifiedBy = actor,
                ConcurrencyToken = Guid.NewGuid().ToString("N")
            };
            db.Vouchers.Add(voucher);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            voucher = existing;
            stockPosting.RemoveVoucherPostings(db, voucher.Id);
            db.InventoryInwardLines.RemoveRange(voucher.InventoryInwardLines);
            await db.SaveChangesAsync(cancellationToken);
            voucher.ModifiedAtUtc = now;
            voucher.ModifiedBy = actor;
            voucher.ConcurrencyToken = Guid.NewGuid().ToString("N");
        }

        var lineNumber = 0;
        var lineEntities = normalized.Select(x => new InventoryInwardLine
        {
            VoucherId = voucher.Id,
            LineNumber = ++lineNumber,
            StockItemId = entity.Id,
            StockItemVariantId = x.StockItemVariantId,
            UqcId = entity.UqcId,
            GodownId = x.GodownId,
            Quantity = x.Quantity,
            Rate = x.Rate,
            Amount = x.Value
        }).ToList();
        db.InventoryInwardLines.AddRange(lineEntities);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var line in lineEntities)
        {
            stockPosting.Post(db, new StockMovementDraft(
                companyContext.CompanyId,
                companyContext.FinancialYearId,
                voucher.Id,
                financialYear.StartDate,
                entity.Id,
                entity.UqcId,
                line.GodownId,
                line.Quantity,
                line.Rate,
                line.Amount,
                StockMovementSemantics.OpeningStockInward,
                StockItemVariantId: line.StockItemVariantId,
                InventoryInwardLineId: line.Id), actor, now);
        }

        db.AuditLogs.Add(VoucherLifecycleService.NewAudit(
            companyContext.CompanyId, voucher.Id,
            existing is null ? "Create" : "Update", true,
            $"{(existing is null ? "Created" : "Updated")} opening inventory for Stock Item '{entity.Name}' with {lineEntities.Count} allocation(s).",
            actor, now));
        await db.SaveChangesAsync(cancellationToken);
        await voucherAuditHistory.RecordAsync(
            db, voucher.Id,
            existing is null ? VoucherAuditActions.Create : VoucherAuditActions.Update,
            existing is null
                ? $"Opening inventory created from Stock Item master '{entity.Name}'."
                : $"Opening inventory altered from Stock Item master '{entity.Name}'.",
            actor, now, cancellationToken);
    }

    private static bool OpeningLinesEqual(
        IEnumerable<InventoryInwardLine> existing,
        IReadOnlyList<OpeningPostingLine> requested)
    {
        var current = existing.OrderBy(x => x.StockItemVariantId).ThenBy(x => x.GodownId).ToList();
        return current.Count == requested.Count && current.Zip(requested).All(pair =>
            pair.First.StockItemVariantId == pair.Second.StockItemVariantId &&
            pair.First.GodownId == pair.Second.GodownId &&
            pair.First.Quantity == pair.Second.Quantity &&
            pair.First.Rate == pair.Second.Rate &&
            pair.First.Amount == pair.Second.Value);
    }

    private static string FormatAutomaticNumber(VoucherType type, int sequence)
    {
        var numeric = type.NumberWidth > 0 ? sequence.ToString($"D{type.NumberWidth}") : sequence.ToString();
        return type.NumberingMode == "AutoPrefixSuffix" ? $"{type.Prefix}{numeric}{type.Suffix}" : numeric;
    }

    private sealed record OpeningPostingLine(
        long StockItemVariantId,
        long GodownId,
        decimal Quantity,
        decimal Rate,
        decimal Value);

    private async Task<List<StockItemCustomFieldEditModel>> BuildCustomFieldsAsync(
        TexTrackDbContext db, StockItem entity, CancellationToken cancellationToken)
    {
        var definitions = await db.StockItemFieldDefinitions.AsNoTracking()
            .Where(x => x.CompanyId == entity.CompanyId && x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var values = entity.FieldValues.ToDictionary(x => x.FieldDefinitionId);
        return definitions.Select(definition => new StockItemCustomFieldEditModel
        {
            DefinitionId = definition.Id,
            Name = definition.Name,
            FieldType = definition.FieldType,
            IsRequired = definition.IsRequired,
            AllowMultiple = definition.AllowMultiple,
            OptionsText = definition.OptionsText,
            Value = values.TryGetValue(definition.Id, out var value) ? value.Value : string.Empty,
            Photos = entity.Photos.Where(x => x.FieldDefinitionId == definition.Id)
                .OrderBy(x => x.DisplayOrder)
                .Select(x => new StockItemPhotoModel { Id = x.Id, FileName = x.FileName, ContentType = x.ContentType })
                .ToList()
        }).ToList();
    }

    private static async Task SynchronizeCustomFieldsAsync(
        TexTrackDbContext db, StockItem entity, List<StockItemCustomFieldEditModel> fields,
        string actor, CancellationToken cancellationToken)
    {
        var definitions = await db.StockItemFieldDefinitions
            .Where(x => x.CompanyId == entity.CompanyId && x.IsActive)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var existingValues = await db.StockItemFieldValues
            .Where(x => x.StockItemId == entity.Id).ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var field in fields.Where(x => definitions.ContainsKey(x.DefinitionId)))
        {
            var definition = definitions[field.DefinitionId];
            if (definition.FieldType == "Photo")
            {
                var existingPhotos = await db.StockItemPhotos
                    .Where(x => x.StockItemId == entity.Id && x.FieldDefinitionId == definition.Id)
                    .ToListAsync(cancellationToken);
                var retainedIds = field.Photos.Where(x => x.Id > 0).Select(x => x.Id).ToHashSet();
                db.StockItemPhotos.RemoveRange(existingPhotos.Where(x => !retainedIds.Contains(x.Id)));
                if (!definition.AllowMultiple && field.Photos.Count > 1)
                    throw new InvalidOperationException($"{definition.Name} accepts only one photo.");
                if (field.Photos.Count > 12)
                    throw new InvalidOperationException($"{definition.Name} accepts at most 12 photos.");
                var order = 1;
                foreach (var photo in field.Photos)
                {
                    if (photo.Id > 0) { var saved = existingPhotos.Single(x => x.Id == photo.Id); saved.DisplayOrder = order++; continue; }
                    if (photo.NewContent is null || photo.NewContent.Length == 0 || photo.NewContent.Length > 5 * 1024 * 1024)
                        throw new InvalidOperationException($"Each {definition.Name} image must be between 1 byte and 5 MB.");
                    if (photo.ContentType is not ("image/jpeg" or "image/png" or "image/webp"))
                        throw new InvalidOperationException($"{definition.Name} supports JPG, PNG or WebP images only.");
                    db.StockItemPhotos.Add(new StockItemPhoto
                    {
                        CompanyId = entity.CompanyId, StockItemId = entity.Id, FieldDefinitionId = definition.Id,
                        FileName = Path.GetFileName(photo.FileName), ContentType = photo.ContentType,
                        Content = photo.NewContent, DisplayOrder = order++, CreatedAtUtc = now, ModifiedAtUtc = now,
                        CreatedBy = actor, ModifiedBy = actor, ConcurrencyToken = Guid.NewGuid().ToString("N")
                    });
                }
                continue;
            }

            var normalizedValue = field.Value.Trim();
            var existing = existingValues.SingleOrDefault(x => x.FieldDefinitionId == definition.Id);
            if (string.IsNullOrEmpty(normalizedValue))
            {
                if (existing is not null) db.StockItemFieldValues.Remove(existing);
            }
            else if (existing is null)
            {
                db.StockItemFieldValues.Add(new StockItemFieldValue
                {
                    CompanyId = entity.CompanyId, StockItemId = entity.Id, FieldDefinitionId = definition.Id,
                    Value = normalizedValue, CreatedAtUtc = now, ModifiedAtUtc = now, CreatedBy = actor,
                    ModifiedBy = actor, ConcurrencyToken = Guid.NewGuid().ToString("N")
                });
            }
            else
            {
                existing.Value = normalizedValue; existing.ModifiedAtUtc = now;
                existing.ModifiedBy = actor; existing.ConcurrencyToken = Guid.NewGuid().ToString("N");
            }
        }
    }

    private static async Task SynchronizeDesignPhotoAsync(
        TexTrackDbContext db, StockItem entity, StockItemDesignPhotoModel? photo,
        string actor, CancellationToken cancellationToken)
    {
        var existing = await db.StockItemDesignPhotos
            .SingleOrDefaultAsync(x => x.StockItemId == entity.Id, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (photo is null || photo.Remove)
        {
            if (existing is not null) db.StockItemDesignPhotos.Remove(existing);
            return;
        }

        if (photo.NewContent is null)
        {
            return;
        }

        if (photo.NewContent.Length == 0 || photo.NewContent.Length > 5 * 1024 * 1024)
            throw new InvalidOperationException("The Design Photo must be between 1 byte and 5 MB.");
        if (photo.ContentType is not ("image/jpeg" or "image/png" or "image/webp"))
            throw new InvalidOperationException("The Design Photo supports JPG, PNG or WebP images only.");

        if (existing is null)
        {
            db.StockItemDesignPhotos.Add(new StockItemDesignPhoto
            {
                CompanyId = entity.CompanyId, StockItemId = entity.Id,
                FileName = Path.GetFileName(photo.FileName), ContentType = photo.ContentType, Content = photo.NewContent,
                CreatedAtUtc = now, ModifiedAtUtc = now, CreatedBy = actor, ModifiedBy = actor,
                ConcurrencyToken = Guid.NewGuid().ToString("N")
            });
        }
        else
        {
            existing.FileName = Path.GetFileName(photo.FileName);
            existing.ContentType = photo.ContentType;
            existing.Content = photo.NewContent;
            existing.ModifiedAtUtc = now;
            existing.ModifiedBy = actor;
            existing.ConcurrencyToken = Guid.NewGuid().ToString("N");
        }
    }

    private static void ApplyFields(StockItem entity, StockItemEditModel model, string actor)
    {
        entity.Name = model.Name;
        entity.NameNormalized = Normalize(model.Name);
        entity.Alias = model.Alias;
        entity.StockGroupId = model.StockGroupId;
        entity.UqcId = model.UqcId;
        entity.StockCategoryId = model.StockCategoryId;
        entity.TaxMode = model.TaxMode;
        entity.TaxClassificationId = model.TaxClassificationId;
        entity.HsnCode = model.HsnCode;
        entity.IgstRate = model.IgstRate;
        entity.CgstRate = model.CgstRate;
        entity.SgstRate = model.SgstRate;
        entity.CostPrice = model.CostPrice;
        entity.SalePrice = model.SalePrice;
        entity.ModifiedAtUtc = DateTimeOffset.UtcNow;
        entity.ModifiedBy = actor;
        entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
    }

    private static Task SynchronizeColoursAsync(
        TexTrackDbContext db,
        StockItem entity,
        HashSet<long> desiredIds,
        CancellationToken cancellationToken)
    {
        var existingIds = entity.Colours.Select(x => x.ColourId).ToHashSet();
        db.StockItemColours.RemoveRange(entity.Colours.Where(x => !desiredIds.Contains(x.ColourId)));
        foreach (var id in desiredIds.Except(existingIds))
        {
            db.StockItemColours.Add(new StockItemColour { StockItemId = entity.Id, ColourId = id });
        }
        return Task.CompletedTask;
    }

    private static Task SynchronizeSizesAsync(
        TexTrackDbContext db,
        StockItem entity,
        HashSet<long> desiredIds,
        CancellationToken cancellationToken)
    {
        var existingIds = entity.Sizes.Select(x => x.SizeId).ToHashSet();
        db.StockItemSizes.RemoveRange(entity.Sizes.Where(x => !desiredIds.Contains(x.SizeId)));
        foreach (var id in desiredIds.Except(existingIds))
        {
            db.StockItemSizes.Add(new StockItemSize { StockItemId = entity.Id, SizeId = id });
        }
        return Task.CompletedTask;
    }

    private static Task SynchronizeVariantsAsync(
        TexTrackDbContext db,
        StockItem entity,
        HashSet<long> colourIds,
        HashSet<long> sizeIds,
        string actor,
        CancellationToken cancellationToken)
    {
        var desired = BuildVariantKeys(colourIds, sizeIds);
        var existingByKey = entity.Variants.ToDictionary(x => x.VariantKey, StringComparer.Ordinal);

        db.StockItemVariants.RemoveRange(entity.Variants.Where(x => !desired.ContainsKey(x.VariantKey)));
        foreach (var pair in desired)
        {
            if (existingByKey.TryGetValue(pair.Key, out var existing))
            {
                existing.IsActive = true;
                existing.ModifiedAtUtc = DateTimeOffset.UtcNow;
                existing.ModifiedBy = actor;
                existing.ConcurrencyToken = Guid.NewGuid().ToString("N");
                continue;
            }

            db.StockItemVariants.Add(new StockItemVariant
            {
                CompanyId = entity.CompanyId,
                StockItemId = entity.Id,
                ColourId = pair.Value.ColourId,
                SizeId = pair.Value.SizeId,
                VariantKey = pair.Key,
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ModifiedAtUtc = DateTimeOffset.UtcNow,
                CreatedBy = actor,
                ModifiedBy = actor,
                ConcurrencyToken = Guid.NewGuid().ToString("N")
            });
        }
        return Task.CompletedTask;
    }

    private static Dictionary<string, (long? ColourId, long? SizeId)> BuildVariantKeys(
        HashSet<long> colourIds,
        HashSet<long> sizeIds)
    {
        var result = new Dictionary<string, (long? ColourId, long? SizeId)>(StringComparer.Ordinal);
        var colours = colourIds.Count == 0 ? new long?[] { null } : colourIds.OrderBy(x => x).Select(x => (long?)x).ToArray();
        var sizes = sizeIds.Count == 0 ? new long?[] { null } : sizeIds.OrderBy(x => x).Select(x => (long?)x).ToArray();

        foreach (var colourId in colours)
        {
            foreach (var sizeId in sizes)
            {
                var key = colourId is null && sizeId is null
                    ? "BASE"
                    : $"C:{colourId?.ToString() ?? "-"}|S:{sizeId?.ToString() ?? "-"}";
                result[key] = (colourId, sizeId);
            }
        }
        return result;
    }

    private static async Task<List<long>> GetUsedVariantIdsAsync(
        TexTrackDbContext db,
        long stockItemId,
        CancellationToken cancellationToken)
    {
        var variantIds = db.StockItemVariants
            .Where(x => x.StockItemId == stockItemId)
            .Select(x => x.Id);

        return await db.JobWorkOrderSizeAllocations
            .Where(x => variantIds.Contains(x.StockItemVariantId))
            .Select(x => x.StockItemVariantId)
            .Concat(db.MasterJobOrderAllocations
                .Where(x => variantIds.Contains(x.StockItemVariantId))
                .Select(x => x.StockItemVariantId))
            .Concat(db.JobWorkOrderComponents
                .Where(x => x.ComponentVariantId != null && variantIds.Contains(x.ComponentVariantId.Value))
                .Select(x => x.ComponentVariantId!.Value))
            .Concat(db.JobWorkOrderBomStages
                .Where(x => x.OutputVariantId != null && variantIds.Contains(x.OutputVariantId.Value))
                .Select(x => x.OutputVariantId!.Value))
            .Concat(db.MaterialInFinishedGoodAllocations
                .Where(x => variantIds.Contains(x.StockItemVariantId))
                .Select(x => x.StockItemVariantId))
            .Concat(db.InventoryInwardLines
                .Where(x => variantIds.Contains(x.StockItemVariantId))
                .Select(x => x.StockItemVariantId))
            .Concat(db.StockMovements
                .Where(x => x.StockItemVariantId != null && variantIds.Contains(x.StockItemVariantId.Value))
                .Select(x => x.StockItemVariantId!.Value))
            .Concat(db.BillOfMaterialLines
                .Where(x => x.ComponentVariantId != null && variantIds.Contains(x.ComponentVariantId.Value))
                .Select(x => x.ComponentVariantId!.Value))
            .Concat(db.BillOfMaterialRevisionLines
                .Where(x => x.ComponentVariantId != null && variantIds.Contains(x.ComponentVariantId.Value))
                .Select(x => x.ComponentVariantId!.Value))
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private static async Task<bool> HasTransactionalUsageAsync(
        TexTrackDbContext db,
        long stockItemId,
        CancellationToken cancellationToken)
    {
        var usageIds = db.JobWorkOrderFinishedGoods
            .Where(x => x.StockItemId == stockItemId)
            .Select(x => x.StockItemId)
            .Concat(db.JobWorkOrderComponents
                .Where(x => x.StockItemId == stockItemId)
                .Select(x => x.StockItemId))
            .Concat(db.JobWorkOrderSizeAllocations
                .Where(x => x.StockItemVariant.StockItemId == stockItemId)
                .Select(x => x.StockItemVariant.StockItemId))
            .Concat(db.MaterialOutLines
                .Where(x => x.StockItemId == stockItemId)
                .Select(x => x.StockItemId))
            .Concat(db.StockMovements
                .Where(x => x.StockItemId == stockItemId)
                .Select(x => x.StockItemId));

        return await usageIds.AnyAsync(cancellationToken);
    }

    private async Task<OperationResult> FailWithAuditAsync(
        TexTrackDbContext db,
        long entityId,
        string message,
        CancellationToken cancellationToken)
    {
        AddAudit(db, entityId == 0 ? null : entityId, "ValidationBlocked", false, message);
        try { await db.SaveChangesAsync(cancellationToken); } catch { }
        return OperationResult.Fail(message);
    }

    private void AddAudit(TexTrackDbContext db, long? entityId, string action, bool success, string description)
    {
        db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyContext.CompanyId,
            EntityType = "StockItem",
            EntityId = entityId,
            Action = action,
            Success = success,
            Description = description,
            PerformedBy = companyContext.Actor,
            PerformedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
