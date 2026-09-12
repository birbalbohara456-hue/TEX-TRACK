using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed class MasterRepository(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    CurrentCompanyContext companyContext)
{
    public async Task<IReadOnlyList<MasterListItem>> GetListAsync(
        MasterKind kind,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = companyContext.CompanyId;

        return kind switch
        {
            MasterKind.LedgerGroup => await GetLedgerGroupsAsync(db, companyId, cancellationToken),
            MasterKind.Uqc => await GetUqcsAsync(db, companyId, cancellationToken),
            MasterKind.StockGroup => await GetStockGroupsAsync(db, companyId, cancellationToken),
            MasterKind.StockCategory => await GetSimpleAsync(db.StockCategories, companyId, cancellationToken),
            MasterKind.Godown => await QueryGodownsAsync(db, companyId, string.Empty, cancellationToken),
            MasterKind.Colour => await GetColoursAsync(db, companyId, cancellationToken),
            MasterKind.Size => await GetSizesAsync(db, companyId, cancellationToken),
            MasterKind.Process => await GetProcessesAsync(db, companyId, cancellationToken),
            MasterKind.TaxClassification => await GetTaxClassificationsAsync(db, companyId, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    public async Task<IReadOnlyList<MasterListItem>> GetGodownListAsync(
        string searchText,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await QueryGodownsAsync(
            db,
            companyContext.CompanyId,
            searchText.Trim(),
            cancellationToken);
    }

    public async Task<MasterEditModel?> GetGodownForEditAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Godowns
            .AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId && x.Id == id)
            .Select(x => new MasterEditModel
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                AddressLine1 = x.AddressLine1,
                AddressLine2 = x.AddressLine2,
                City = x.City,
                State = x.State,
                IsActive = x.IsActive,
                ConcurrencyToken = x.ConcurrencyToken
            })
            .SingleOrDefaultAsync(cancellationToken);
    }
    public async Task<MasterEditModel?> GetForEditAsync(
        MasterKind kind,
        long id,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        var rows = await GetListAsync(kind, cancellationToken);
        var row = rows.FirstOrDefault(x => x.Id == id);
        if (row is null)
        {
            return null;
        }

        var identityLocked = kind is MasterKind.Colour or MasterKind.Size &&
            await IsVariantAttributeUsedAsync(kind, id, cancellationToken);

        return new MasterEditModel
        {
            Id = row.Id,
            Name = row.Name,
            Alias = row.Alias,
            ParentId = row.ParentId,
            ShortName = row.ShortName,
            DecimalPlaces = row.DecimalPlaces ?? 0,
            TaxMode = string.IsNullOrWhiteSpace(row.TaxMode) ? "NotApplicable" : row.TaxMode,
            AddressLine1 = row.AddressLine1,
            AddressLine2 = row.AddressLine2,
            City = row.City,
            State = row.State,
            ColourCode = row.ColourCode,
            DisplayOrder = row.DisplayOrder ?? 100,
            IsActive = row.IsActive,
            IsIdentityLocked = identityLocked,
            ConcurrencyToken = row.ConcurrencyToken
        };
    }

    public async Task<OperationResult> SaveAsync(
        MasterKind kind,
        MasterEditModel model,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ManageMasters, cancellationToken);
        model.Name = model.Name.Trim();
        model.Alias = model.Alias.Trim();
        model.ShortName = model.ShortName.Trim();
        model.AddressLine1 = model.AddressLine1.Trim();
        model.AddressLine2 = model.AddressLine2.Trim();
        model.City = model.City.Trim();
        model.State = model.State.Trim();
        model.ColourCode = model.ColourCode.Trim();

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            return OperationResult.Fail("Name is required.");
        }

        return await (kind switch
        {
            MasterKind.LedgerGroup => SaveLedgerGroupAsync(model, cancellationToken),
            MasterKind.Uqc => SaveUqcAsync(model, cancellationToken),
            MasterKind.StockGroup => SaveStockGroupAsync(model, cancellationToken),
            MasterKind.StockCategory => SaveSimpleAsync<StockCategory>(kind, model, cancellationToken),
            MasterKind.Godown => SaveGodownAsync(model, cancellationToken),
            MasterKind.Colour => SaveColourAsync(model, cancellationToken),
            MasterKind.Size => SaveOrderedAsync<SizeMaster>(kind, model, cancellationToken),
            MasterKind.Process => SaveOrderedAsync<ProcessMaster>(kind, model, cancellationToken),
            MasterKind.TaxClassification => SaveTaxClassificationAsync(model, cancellationToken),
            _ => Task.FromResult(OperationResult.Fail("This master is not enabled in this build."))
        });
    }

    public async Task<OperationResult> DeleteAsync(
        MasterKind kind,
        long id,
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ManageMasters, cancellationToken);
        return await (kind switch
        {
            MasterKind.LedgerGroup => DeleteLedgerGroupAsync(id, cancellationToken),
            MasterKind.StockGroup => DeleteStockGroupAsync(id, cancellationToken),
            MasterKind.Godown => DeleteGodownAsync(id, cancellationToken),
            MasterKind.Uqc => DeleteSimpleAsync<Uqc>(kind, id, cancellationToken),
            MasterKind.StockCategory => DeleteSimpleAsync<StockCategory>(kind, id, cancellationToken),
            MasterKind.Colour => DeleteSimpleAsync<Colour>(kind, id, cancellationToken),
            MasterKind.Size => DeleteSimpleAsync<SizeMaster>(kind, id, cancellationToken),
            MasterKind.Process => DeleteSimpleAsync<ProcessMaster>(kind, id, cancellationToken),
            MasterKind.TaxClassification => DeleteSimpleAsync<TaxClassification>(kind, id, cancellationToken),
            _ => Task.FromResult(OperationResult.Fail("This master is not enabled in this build."))
        });
    }

    public async Task<IReadOnlyList<VoucherTypeListItem>> GetVoucherTypesAsync(
        CancellationToken cancellationToken = default)
    {
        await companyContext.RequirePolicyAsync(SecurityPolicies.ViewReports, cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.VoucherTypes
            .AsNoTracking()
            .Where(x => x.CompanyId == companyContext.CompanyId)
            .OrderBy(x => x.Id)
            .Select(x => new VoucherTypeListItem
            {
                Id = x.Id,
                Name = x.Name,
                SystemTypeCode = x.SystemTypeCode,
                Nature = x.Nature,
                PostingMode = x.PostingMode,
                Abbreviation = x.Abbreviation,
                TallyVoucherTypeName = x.TallyVoucherTypeName,
                IsSystem = x.IsSystem
            })
            .ToListAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<MasterListItem>> GetSimpleAsync<T>(
        DbSet<T> set,
        long companyId,
        CancellationToken cancellationToken)
        where T : class, INamedMasterEntity
    {
        var entities = await set
            .AsNoTracking()
            .Where(x => EF.Property<long>(x, nameof(INamedMasterEntity.CompanyId)) == companyId)
            .OrderBy(x => EF.Property<string>(x, nameof(INamedMasterEntity.Name)))
            .ToListAsync(cancellationToken);

        return entities.Select(x => new MasterListItem
        {
            Id = x.Id,
            Name = x.Name,
            Alias = x.Alias,
            IsSystem = x.IsSystem,
            IsActive = x.IsActive,
            ConcurrencyToken = x.ConcurrencyToken
        }).ToList();
    }

    private static Task<List<MasterListItem>> GetLedgerGroupsAsync(
        TexTrackDbContext db,
        long companyId,
        CancellationToken cancellationToken) =>
        db.LedgerGroups
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.ParentId != null)
            .ThenBy(x => x.Name)
            .Select(x => new MasterListItem
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                ParentId = x.ParentId,
                ParentName = x.Parent == null ? string.Empty : x.Parent.Name,
                IsSystem = x.IsSystem,
                IsActive = x.IsActive,
                ConcurrencyToken = x.ConcurrencyToken
            })
            .ToListAsync(cancellationToken);

    private static Task<List<MasterListItem>> GetStockGroupsAsync(
        TexTrackDbContext db,
        long companyId,
        CancellationToken cancellationToken) =>
        db.StockGroups
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.ParentId != null)
            .ThenBy(x => x.Name)
            .Select(x => new MasterListItem
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                ParentId = x.ParentId,
                ParentName = x.Parent == null ? string.Empty : x.Parent.Name,
                IsSystem = x.IsSystem,
                IsActive = x.IsActive,
                ConcurrencyToken = x.ConcurrencyToken
            })
            .ToListAsync(cancellationToken);

    private static Task<List<MasterListItem>> GetUqcsAsync(
        TexTrackDbContext db,
        long companyId,
        CancellationToken cancellationToken) =>
        db.Uqcs
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Name)
            .Select(x => new MasterListItem
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                ShortName = x.ShortName,
                DecimalPlaces = x.DecimalPlaces,
                IsSystem = x.IsSystem,
                IsActive = x.IsActive,
                ConcurrencyToken = x.ConcurrencyToken
            })
            .ToListAsync(cancellationToken);

    private static Task<List<MasterListItem>> QueryGodownsAsync(
        TexTrackDbContext db,
        long companyId,
        string searchText,
        CancellationToken cancellationToken) =>
        db.Godowns
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Where(x => searchText == string.Empty ||
                        x.Name.ToUpper().Contains(searchText.ToUpper()) ||
                        x.Alias.ToUpper().Contains(searchText.ToUpper()) ||
                        x.AddressLine1.ToUpper().Contains(searchText.ToUpper()) ||
                        x.AddressLine2.ToUpper().Contains(searchText.ToUpper()) ||
                        x.City.ToUpper().Contains(searchText.ToUpper()) ||
                        x.State.ToUpper().Contains(searchText.ToUpper()))
            .OrderBy(x => x.Name)
            .Select(x => new MasterListItem
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                AddressLine1 = x.AddressLine1,
                AddressLine2 = x.AddressLine2,
                City = x.City,
                State = x.State,
                IsSystem = false,
                IsActive = x.IsActive,
                ConcurrencyToken = x.ConcurrencyToken
            })
            .ToListAsync(cancellationToken);

    private static Task<List<MasterListItem>> GetColoursAsync(
        TexTrackDbContext db,
        long companyId,
        CancellationToken cancellationToken) =>
        db.Colours
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Name)
            .Select(x => new MasterListItem
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                ColourCode = x.ColourCode,
                IsSystem = x.IsSystem,
                IsActive = x.IsActive,
                ConcurrencyToken = x.ConcurrencyToken
            })
            .ToListAsync(cancellationToken);

    private static Task<List<MasterListItem>> GetSizesAsync(
        TexTrackDbContext db,
        long companyId,
        CancellationToken cancellationToken) =>
        db.Sizes
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .Select(x => new MasterListItem
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                DisplayOrder = x.DisplayOrder,
                IsSystem = x.IsSystem,
                IsActive = x.IsActive,
                ConcurrencyToken = x.ConcurrencyToken
            })
            .ToListAsync(cancellationToken);

    private static Task<List<MasterListItem>> GetProcessesAsync(
        TexTrackDbContext db,
        long companyId,
        CancellationToken cancellationToken) =>
        db.Processes
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .Select(x => new MasterListItem
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                DisplayOrder = x.DisplayOrder,
                IsSystem = x.IsSystem,
                IsActive = x.IsActive,
                ConcurrencyToken = x.ConcurrencyToken
            })
            .ToListAsync(cancellationToken);

    private static Task<List<MasterListItem>> GetTaxClassificationsAsync(
        TexTrackDbContext db,
        long companyId,
        CancellationToken cancellationToken) =>
        db.TaxClassifications
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Name)
            .Select(x => new MasterListItem
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                TaxMode = x.TaxMode,
                IsSystem = x.IsSystem,
                IsActive = x.IsActive,
                ConcurrencyToken = x.ConcurrencyToken
            })
            .ToListAsync(cancellationToken);

    private async Task<OperationResult> SaveSimpleAsync<T>(
        MasterKind kind,
        MasterEditModel model,
        CancellationToken cancellationToken)
        where T : class, INamedMasterEntity, new()
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Normalize(model.Name);
        var duplicate = await db.Set<T>().AnyAsync(
            x => EF.Property<long>(x, nameof(INamedMasterEntity.CompanyId)) == companyContext.CompanyId &&
                 EF.Property<string>(x, nameof(INamedMasterEntity.NameNormalized)) == normalized &&
                 EF.Property<long>(x, nameof(INamedMasterEntity.Id)) != model.Id,
            cancellationToken);

        if (duplicate)
        {
            return await FailWithAuditAsync(db, kind, model.Id, $"A master named '{model.Name}' already exists.", cancellationToken);
        }

        var entity = await CreateOrLoadAsync<T>(db, kind, model, cancellationToken);
        if (entity.Result is not null)
        {
            return entity.Result;
        }

        ApplyNamedFields(entity.Entity!, model);
        AddAudit(db, kind.ToString(), entity.IsNew ? null : entity.Entity!.Id, entity.Action, true, $"{entity.Action} requested for '{model.Name}'.");

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return OperationResult.Ok($"{model.Name} saved successfully.", entity.Entity!.Id);
        }
        catch (DbUpdateException)
        {
            return OperationResult.Fail("The record could not be saved because it conflicts with existing data.");
        }
    }

    private async Task<OperationResult> SaveUqcAsync(
        MasterEditModel model,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.ShortName))
        {
            return OperationResult.Fail("Short Name is required for UQC.");
        }

        if (model.DecimalPlaces is < 0 or > 6)
        {
            return OperationResult.Fail("Decimal Points must be between 0 and 6.");
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Normalize(model.Name);
        var shortNameNormalized = Normalize(model.ShortName);

        if (await db.Uqcs.AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.NameNormalized == normalized && x.Id != model.Id, cancellationToken))
        {
            return await FailWithAuditAsync(db, MasterKind.Uqc, model.Id, $"A unit named '{model.Name}' already exists.", cancellationToken);
        }

        if (await db.Uqcs.AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.ShortName.ToUpper() == shortNameNormalized && x.Id != model.Id, cancellationToken))
        {
            return await FailWithAuditAsync(db, MasterKind.Uqc, model.Id, $"Short Name '{model.ShortName}' is already used by another unit.", cancellationToken);
        }

        var state = await CreateOrLoadAsync<Uqc>(db, MasterKind.Uqc, model, cancellationToken);
        if (state.Result is not null)
        {
            return state.Result;
        }

        ApplyNamedFields(state.Entity!, model);
        state.Entity!.ShortName = model.ShortName.ToUpperInvariant();
        state.Entity.DecimalPlaces = model.DecimalPlaces;
        AddAudit(db, "Uqc", state.IsNew ? null : state.Entity.Id, state.Action, true, $"{state.Action} requested for UQC '{model.Name}'.");
        await db.SaveChangesAsync(cancellationToken);
        return OperationResult.Ok($"{model.Name} saved successfully.", state.Entity.Id);
    }

    private async Task<OperationResult> SaveColourAsync(
        MasterEditModel model,
        CancellationToken cancellationToken)
    {
        if (model.ColourCode.Length > 30)
        {
            return OperationResult.Fail("Colour Code cannot exceed 30 characters.");
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Normalize(model.Name);
        var codeNormalized = Normalize(model.ColourCode);

        if (model.Id != 0)
        {
            var existing = await db.Colours.AsNoTracking().SingleOrDefaultAsync(
                x => x.CompanyId == companyContext.CompanyId && x.Id == model.Id,
                cancellationToken);
            if (existing is not null &&
                (!string.Equals(existing.Name, model.Name, StringComparison.Ordinal) ||
                 !string.Equals(existing.ColourCode, model.ColourCode, StringComparison.OrdinalIgnoreCase)) &&
                await IsVariantAttributeUsedAsync(db, MasterKind.Colour, model.Id, cancellationToken))
            {
                return await FailWithAuditAsync(db, MasterKind.Colour, model.Id,
                    "This Colour has saved transaction history. Its Name and Colour Code are permanent; it may be marked inactive for new vouchers instead.",
                    cancellationToken);
            }
        }

        if (await db.Colours.AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.NameNormalized == normalized && x.Id != model.Id, cancellationToken))
        {
            return await FailWithAuditAsync(db, MasterKind.Colour, model.Id, $"A colour named '{model.Name}' already exists.", cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(codeNormalized) &&
            await db.Colours.AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.ColourCode.ToUpper() == codeNormalized && x.Id != model.Id, cancellationToken))
        {
            return await FailWithAuditAsync(db, MasterKind.Colour, model.Id, $"Colour Code '{model.ColourCode}' is already in use.", cancellationToken);
        }

        var state = await CreateOrLoadAsync<Colour>(db, MasterKind.Colour, model, cancellationToken);
        if (state.Result is not null)
        {
            return state.Result;
        }

        ApplyNamedFields(state.Entity!, model);
        state.Entity!.ColourCode = model.ColourCode.ToUpperInvariant();
        AddAudit(db, "Colour", state.IsNew ? null : state.Entity.Id, state.Action, true, $"{state.Action} requested for colour '{model.Name}'.");
        await db.SaveChangesAsync(cancellationToken);
        return OperationResult.Ok($"{model.Name} saved successfully.", state.Entity.Id);
    }

    private async Task<OperationResult> SaveOrderedAsync<T>(
        MasterKind kind,
        MasterEditModel model,
        CancellationToken cancellationToken)
        where T : class, INamedMasterEntity, new()
    {
        if (model.DisplayOrder is < 1 or > 9999)
        {
            return OperationResult.Fail("Display Order must be between 1 and 9999.");
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Normalize(model.Name);
        if (kind == MasterKind.Size && model.Id != 0)
        {
            var existing = await db.Sizes.AsNoTracking().SingleOrDefaultAsync(
                x => x.CompanyId == companyContext.CompanyId && x.Id == model.Id,
                cancellationToken);
            if (existing is not null &&
                !string.Equals(existing.Name, model.Name, StringComparison.Ordinal) &&
                await IsVariantAttributeUsedAsync(db, MasterKind.Size, model.Id, cancellationToken))
            {
                return await FailWithAuditAsync(db, MasterKind.Size, model.Id,
                    "This Size has saved transaction history. Its Name is permanent; it may be marked inactive for new vouchers instead.",
                    cancellationToken);
            }
        }

        if (await db.Set<T>().AnyAsync(
                x => EF.Property<long>(x, nameof(INamedMasterEntity.CompanyId)) == companyContext.CompanyId &&
                     EF.Property<string>(x, nameof(INamedMasterEntity.NameNormalized)) == normalized &&
                     EF.Property<long>(x, nameof(INamedMasterEntity.Id)) != model.Id,
                cancellationToken))
        {
            return await FailWithAuditAsync(db, kind, model.Id, $"A {kind.ToString().ToLowerInvariant()} named '{model.Name}' already exists.", cancellationToken);
        }

        var state = await CreateOrLoadAsync<T>(db, kind, model, cancellationToken);
        if (state.Result is not null)
        {
            return state.Result;
        }

        ApplyNamedFields(state.Entity!, model);
        if (state.Entity is SizeMaster size)
        {
            size.DisplayOrder = model.DisplayOrder;
        }
        else if (state.Entity is ProcessMaster process)
        {
            process.DisplayOrder = model.DisplayOrder;
        }

        AddAudit(db, kind.ToString(), state.IsNew ? null : state.Entity!.Id, state.Action, true, $"{state.Action} requested for '{model.Name}'.");
        await db.SaveChangesAsync(cancellationToken);
        return OperationResult.Ok($"{model.Name} saved successfully.", state.Entity!.Id);
    }

    private async Task<OperationResult> SaveGodownAsync(
        MasterEditModel model,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Normalize(model.Name);
        if (await db.Godowns.AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.NameNormalized == normalized && x.Id != model.Id, cancellationToken))
        {
            return await FailWithAuditAsync(db, MasterKind.Godown, model.Id, $"A godown named '{model.Name}' already exists.", cancellationToken);
        }

        var state = await CreateOrLoadAsync<Godown>(db, MasterKind.Godown, model, cancellationToken);
        if (state.Result is not null)
        {
            return state.Result;
        }

        ApplyNamedFields(state.Entity!, model);
        state.Entity!.IsSystem = false;
        state.Entity.AddressLine1 = model.AddressLine1;
        state.Entity.AddressLine2 = model.AddressLine2;
        state.Entity.City = model.City;
        state.Entity.State = model.State;
        AddAudit(db, "Godown", state.IsNew ? null : state.Entity.Id, state.Action, true, $"{state.Action} requested for godown '{model.Name}'.");
        await db.SaveChangesAsync(cancellationToken);
        return OperationResult.Ok($"{model.Name} saved successfully.", state.Entity.Id);
    }

    private async Task<OperationResult> SaveTaxClassificationAsync(
        MasterEditModel model,
        CancellationToken cancellationToken)
    {
        var allowedModes = new[] { "NotApplicable", "InventoryTaxGroup", "DirectRates" };
        if (!allowedModes.Contains(model.TaxMode, StringComparer.Ordinal))
        {
            return OperationResult.Fail("Select a valid tax mode.");
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Normalize(model.Name);
        if (await db.TaxClassifications.AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.NameNormalized == normalized && x.Id != model.Id, cancellationToken))
        {
            return await FailWithAuditAsync(db, MasterKind.TaxClassification, model.Id, $"A tax classification named '{model.Name}' already exists.", cancellationToken);
        }

        var state = await CreateOrLoadAsync<TaxClassification>(db, MasterKind.TaxClassification, model, cancellationToken);
        if (state.Result is not null)
        {
            return state.Result;
        }

        ApplyNamedFields(state.Entity!, model);
        state.Entity!.TaxMode = model.TaxMode;
        AddAudit(db, "TaxClassification", state.IsNew ? null : state.Entity.Id, state.Action, true, $"{state.Action} requested for '{model.Name}'.");
        await db.SaveChangesAsync(cancellationToken);
        return OperationResult.Ok($"{model.Name} saved successfully.", state.Entity.Id);
    }

    private Task<OperationResult> SaveLedgerGroupAsync(MasterEditModel model, CancellationToken cancellationToken) =>
        SaveGroupAsync<LedgerGroup>(MasterKind.LedgerGroup, model, cancellationToken);

    private Task<OperationResult> SaveStockGroupAsync(MasterEditModel model, CancellationToken cancellationToken) =>
        SaveGroupAsync<StockGroup>(MasterKind.StockGroup, model, cancellationToken);

    private async Task<OperationResult> SaveGroupAsync<T>(
        MasterKind kind,
        MasterEditModel model,
        CancellationToken cancellationToken)
        where T : class, INamedMasterEntity, new()
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var set = db.Set<T>();
        var normalized = Normalize(model.Name);
        if (await set.AnyAsync(
                x => EF.Property<long>(x, nameof(INamedMasterEntity.CompanyId)) == companyContext.CompanyId &&
                     EF.Property<string>(x, nameof(INamedMasterEntity.NameNormalized)) == normalized &&
                     EF.Property<long>(x, nameof(INamedMasterEntity.Id)) != model.Id,
                cancellationToken))
        {
            return await FailWithAuditAsync(db, kind, model.Id, $"A group named '{model.Name}' already exists.", cancellationToken);
        }

        T? existing = null;
        if (model.Id != 0)
        {
            existing = await set.SingleOrDefaultAsync(
                x => EF.Property<long>(x, nameof(INamedMasterEntity.CompanyId)) == companyContext.CompanyId &&
                     EF.Property<long>(x, nameof(INamedMasterEntity.Id)) == model.Id,
                cancellationToken);
            if (existing is null)
            {
                return OperationResult.Fail("The selected group no longer exists.");
            }

            if (!string.Equals(existing.ConcurrencyToken, model.ConcurrencyToken, StringComparison.Ordinal))
            {
                return await FailWithAuditAsync(db, kind, model.Id, "This group was changed by another user. Reload and try again.", cancellationToken);
            }
        }

        if (kind == MasterKind.StockGroup && existing?.IsSystem == true)
        {
            if (!string.Equals(existing.Name, model.Name, StringComparison.Ordinal) ||
                !string.Equals(existing.Alias, model.Alias, StringComparison.Ordinal) ||
                model.ParentId is not null)
            {
                return await FailWithAuditAsync(db, kind, model.Id, "Raw Material, Work In Progress and Finished Goods are fixed system roots and cannot be renamed, moved or reclassified.", cancellationToken);
            }
        }

        var parentId = existing?.IsSystem == true ? null : model.ParentId;
        if (existing?.IsSystem != true && parentId is null)
        {
            return await FailWithAuditAsync(db, kind, model.Id, "Select a parent group. Only permanent system groups may exist at the primary level.", cancellationToken);
        }

        var inheritedRoot = string.Empty;
        if (parentId is not null)
        {
            if (parentId == model.Id && model.Id != 0)
            {
                return await FailWithAuditAsync(db, kind, model.Id, "A group cannot be its own parent.", cancellationToken);
            }

            inheritedRoot = await GetGroupRootAsync<T>(db, parentId.Value, cancellationToken);
            if (string.IsNullOrWhiteSpace(inheritedRoot))
            {
                return await FailWithAuditAsync(db, kind, model.Id, "The selected parent group does not exist.", cancellationToken);
            }
            if (kind == MasterKind.StockGroup && string.Equals(inheritedRoot, "WorkInProgress", StringComparison.OrdinalIgnoreCase))
            {
                return await FailWithAuditAsync(db, kind, model.Id,
                    "Work In Progress is transaction-derived and cannot be used as a parent Stock Group.", cancellationToken);
            }

            if (model.Id != 0 && await WouldCreateGroupCycleAsync<T>(db, model.Id, parentId.Value, cancellationToken))
            {
                return await FailWithAuditAsync(db, kind, model.Id, "This parent selection would create a circular group hierarchy.", cancellationToken);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var entity = existing ?? new T
        {
            CompanyId = companyContext.CompanyId,
            CreatedAtUtc = now,
            CreatedBy = companyContext.Actor,
            IsActive = true,
            IsSystem = false
        };
        var isNew = existing is null;
        if (isNew)
        {
            set.Add(entity);
        }

        ApplyNamedFields(entity, model);
        if (entity is LedgerGroup ledgerGroup && !ledgerGroup.IsSystem)
        {
            ledgerGroup.ParentId = parentId;
            ledgerGroup.RootClassification = inheritedRoot;
        }
        else if (entity is StockGroup stockGroup && !stockGroup.IsSystem)
        {
            stockGroup.ParentId = parentId;
            stockGroup.RootClassification = inheritedRoot;
        }

        var action = isNew ? "Create" : "Update";
        AddAudit(db, kind.ToString(), isNew ? null : entity.Id, action, true, $"{action} requested for group '{model.Name}'.");
        await db.SaveChangesAsync(cancellationToken);
        return OperationResult.Ok($"{model.Name} saved successfully.", entity.Id);
    }

    private async Task<(T? Entity, bool IsNew, string Action, OperationResult? Result)> CreateOrLoadAsync<T>(
        TexTrackDbContext db,
        MasterKind kind,
        MasterEditModel model,
        CancellationToken cancellationToken)
        where T : class, INamedMasterEntity, new()
    {
        if (model.Id == 0)
        {
            var entity = new T
            {
                CompanyId = companyContext.CompanyId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                CreatedBy = companyContext.Actor,
                IsActive = true,
                IsSystem = false
            };
            db.Set<T>().Add(entity);
            return (entity, true, "Create", null);
        }

        var existing = await db.Set<T>().SingleOrDefaultAsync(
            x => EF.Property<long>(x, nameof(INamedMasterEntity.CompanyId)) == companyContext.CompanyId &&
                 EF.Property<long>(x, nameof(INamedMasterEntity.Id)) == model.Id,
            cancellationToken);
        if (existing is null)
        {
            return (null, false, "Update", OperationResult.Fail("The selected master no longer exists."));
        }

        if (!string.Equals(existing.ConcurrencyToken, model.ConcurrencyToken, StringComparison.Ordinal))
        {
            return (null, false, "Update", await FailWithAuditAsync(db, kind, model.Id, "This master was changed by another user. Reload and try again.", cancellationToken));
        }

        return (existing, false, "Update", null);
    }

    private void ApplyNamedFields<T>(T entity, MasterEditModel model)
        where T : INamedMasterEntity
    {
        entity.Name = model.Name;
        entity.NameNormalized = Normalize(model.Name);
        entity.Alias = model.Alias;
        entity.IsActive = model.IsActive;
        entity.ModifiedAtUtc = DateTimeOffset.UtcNow;
        entity.ModifiedBy = companyContext.Actor;
        entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
    }

    private async Task<bool> IsVariantAttributeUsedAsync(
        MasterKind kind,
        long attributeId,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await IsVariantAttributeUsedAsync(db, kind, attributeId, cancellationToken);
    }

    private async Task<bool> IsVariantAttributeUsedAsync(
        TexTrackDbContext db,
        MasterKind kind,
        long attributeId,
        CancellationToken cancellationToken)
    {
        if (kind == MasterKind.Colour &&
            (await db.JobWorkOrderFinishedGoods.AnyAsync(x => x.ColourId == attributeId, cancellationToken) ||
             await db.MasterJobOrderAllocations.AnyAsync(x => x.ColourId == attributeId, cancellationToken)))
        {
            return true;
        }

        if (kind == MasterKind.Size &&
            (await db.JobWorkOrderSizeAllocations.AnyAsync(x => x.SizeId == attributeId, cancellationToken) ||
             await db.MasterJobOrderAllocations.AnyAsync(x => x.SizeId == attributeId, cancellationToken)))
        {
            return true;
        }

        if (kind is not (MasterKind.Colour or MasterKind.Size))
        {
            return false;
        }

        var variantIds = db.StockItemVariants
            .Where(x => x.CompanyId == companyContext.CompanyId &&
                (kind == MasterKind.Colour ? x.ColourId == attributeId : x.SizeId == attributeId))
            .Select(x => x.Id);

        return await db.JobWorkOrderSizeAllocations.AnyAsync(x => variantIds.Contains(x.StockItemVariantId), cancellationToken) ||
               await db.MasterJobOrderAllocations.AnyAsync(x => variantIds.Contains(x.StockItemVariantId), cancellationToken) ||
               await db.JobWorkOrderComponents.AnyAsync(
                   x => x.ComponentVariantId != null && variantIds.Contains(x.ComponentVariantId.Value), cancellationToken) ||
               await db.JobWorkOrderBomStages.AnyAsync(
                   x => x.OutputVariantId != null && variantIds.Contains(x.OutputVariantId.Value), cancellationToken) ||
               await db.MaterialInFinishedGoodAllocations.AnyAsync(
                   x => variantIds.Contains(x.StockItemVariantId), cancellationToken) ||
               await db.InventoryInwardLines.AnyAsync(
                   x => variantIds.Contains(x.StockItemVariantId), cancellationToken) ||
               await db.StockMovements.AnyAsync(
                   x => x.StockItemVariantId != null && variantIds.Contains(x.StockItemVariantId.Value), cancellationToken) ||
               await db.BillOfMaterialLines.AnyAsync(
                   x => x.ComponentVariantId != null && variantIds.Contains(x.ComponentVariantId.Value), cancellationToken) ||
               await db.BillOfMaterialRevisionLines.AnyAsync(
                   x => x.ComponentVariantId != null && variantIds.Contains(x.ComponentVariantId.Value), cancellationToken);
    }

    private async Task<string> GetGroupRootAsync<T>(
        TexTrackDbContext db,
        long parentId,
        CancellationToken cancellationToken)
        where T : class, INamedMasterEntity
    {
        if (typeof(T) == typeof(LedgerGroup))
        {
            var parent = await db.LedgerGroups.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.Id == parentId, cancellationToken);
            return parent?.RootClassification ?? string.Empty;
        }

        var stockParent = await db.StockGroups.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyContext.CompanyId && x.Id == parentId, cancellationToken);
        return stockParent?.RootClassification ?? string.Empty;
    }

    private async Task<bool> WouldCreateGroupCycleAsync<T>(
        TexTrackDbContext db,
        long groupId,
        long proposedParentId,
        CancellationToken cancellationToken)
        where T : class, INamedMasterEntity
    {
        long? current = proposedParentId;
        var checkedIds = new HashSet<long>();
        while (current is not null && checkedIds.Add(current.Value))
        {
            if (current.Value == groupId)
            {
                return true;
            }

            current = typeof(T) == typeof(LedgerGroup)
                ? await db.LedgerGroups.Where(x => x.Id == current.Value).Select(x => x.ParentId).SingleOrDefaultAsync(cancellationToken)
                : await db.StockGroups.Where(x => x.Id == current.Value).Select(x => x.ParentId).SingleOrDefaultAsync(cancellationToken);
        }

        return false;
    }

    private Task<OperationResult> DeleteLedgerGroupAsync(long id, CancellationToken cancellationToken) =>
        DeleteGroupAsync<LedgerGroup>(MasterKind.LedgerGroup, id, cancellationToken);

    private Task<OperationResult> DeleteStockGroupAsync(long id, CancellationToken cancellationToken) =>
        DeleteGroupAsync<StockGroup>(MasterKind.StockGroup, id, cancellationToken);

    private async Task<OperationResult> DeleteGroupAsync<T>(
        MasterKind kind,
        long id,
        CancellationToken cancellationToken)
        where T : class, INamedMasterEntity
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var set = db.Set<T>();
        var entity = await set.SingleOrDefaultAsync(
            x => EF.Property<long>(x, nameof(INamedMasterEntity.CompanyId)) == companyContext.CompanyId &&
                 EF.Property<long>(x, nameof(INamedMasterEntity.Id)) == id,
            cancellationToken);
        if (entity is null)
        {
            return OperationResult.Fail("The selected group no longer exists.");
        }

        if (entity.IsSystem)
        {
            return await FailWithAuditAsync(db, kind, id, "Permanent system groups cannot be deleted.", cancellationToken);
        }

        var hasChildren = typeof(T) == typeof(LedgerGroup)
            ? await db.LedgerGroups.AnyAsync(x => x.ParentId == id, cancellationToken)
            : await db.StockGroups.AnyAsync(x => x.ParentId == id, cancellationToken);
        if (hasChildren)
        {
            return await FailWithAuditAsync(db, kind, id, "Delete the child groups first. This group is still in use as a parent.", cancellationToken);
        }

        if (typeof(T) == typeof(LedgerGroup) && await db.Ledgers.AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.LedgerGroupId == id, cancellationToken))
        {
            return await FailWithAuditAsync(db, kind, id, "This Ledger Group is assigned to one or more ledgers and cannot be deleted.", cancellationToken);
        }

        if (typeof(T) == typeof(StockGroup) && await db.StockItems.AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.StockGroupId == id, cancellationToken))
        {
            return await FailWithAuditAsync(db, kind, id, "This Stock Group is assigned to one or more Stock Items and cannot be deleted.", cancellationToken);
        }

        set.Remove(entity);
        AddAudit(db, kind.ToString(), id, "Delete", true, $"Deleted group '{entity.Name}'.");
        await db.SaveChangesAsync(cancellationToken);
        return OperationResult.Ok($"{entity.Name} deleted.");
    }

    private async Task<OperationResult> DeleteGodownAsync(long id, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var entity = await db.Godowns.SingleOrDefaultAsync(
            x => x.CompanyId == companyContext.CompanyId && x.Id == id,
            cancellationToken);
        if (entity is null)
        {
            return OperationResult.Fail("The selected godown no longer exists.");
        }

        string? dependencyMessage = null;
        if (await db.JobWorkOrderFinishedGoods.AnyAsync(
                x => x.FinishedGoodsGodownId == id || x.DestinationGodownId == id,
                cancellationToken) ||
            await db.JobWorkOrderComponents.AnyAsync(x => x.ComponentGodownId == id, cancellationToken))
        {
            dependencyMessage = "This godown is used by one or more Job Work Out Orders and cannot be deleted.";
        }
        else if (await db.MaterialOutLines.AnyAsync(
                     x => x.SourceGodownId == id || x.DestinationGodownId == id,
                     cancellationToken) ||
                 await db.MaterialOutDetails.AnyAsync(x => x.DestinationGodownId == id, cancellationToken))
        {
            dependencyMessage = "This godown is used by one or more Material Out vouchers and cannot be deleted.";
        }
        else if (await db.StockMovements.AnyAsync(x => x.GodownId == id, cancellationToken))
        {
            dependencyMessage = "This godown has stock movement history and cannot be deleted, even when its current balance is zero.";
        }

        if (dependencyMessage is not null)
        {
            AddAudit(db, "Godown", id, "ValidationBlocked", false, dependencyMessage);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Fail(dependencyMessage);
        }

        db.Godowns.Remove(entity);
        AddAudit(db, "Godown", id, "Delete", true, $"Deleted unused godown '{entity.Name}'.");
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Ok($"{entity.Name} deleted.");
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Fail("This godown is linked to stock or voucher records and cannot be deleted.");
        }
    }
    private async Task<OperationResult> DeleteSimpleAsync<T>(
        MasterKind kind,
        long id,
        CancellationToken cancellationToken)
        where T : class, INamedMasterEntity
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var set = db.Set<T>();
        var entity = await set.SingleOrDefaultAsync(
            x => EF.Property<long>(x, nameof(INamedMasterEntity.CompanyId)) == companyContext.CompanyId &&
                 EF.Property<long>(x, nameof(INamedMasterEntity.Id)) == id,
            cancellationToken);
        if (entity is null)
        {
            return OperationResult.Fail("The selected master no longer exists.");
        }

        if (entity.IsSystem)
        {
            return await FailWithAuditAsync(db, kind, id, "Permanent system masters cannot be deleted.", cancellationToken);
        }

        var linkedToStockItem = kind switch
        {
            MasterKind.Uqc => await db.StockItems.AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.UqcId == id, cancellationToken),
            MasterKind.StockCategory => await db.StockItems.AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.StockCategoryId == id, cancellationToken),
            MasterKind.Colour => await db.StockItemColours.AnyAsync(x => x.ColourId == id, cancellationToken),
            MasterKind.Size => await db.StockItemSizes.AnyAsync(x => x.SizeId == id, cancellationToken),
            MasterKind.TaxClassification => await db.StockItems.AnyAsync(x => x.CompanyId == companyContext.CompanyId && x.TaxClassificationId == id, cancellationToken),
            _ => false
        };
        if (linkedToStockItem)
        {
            return await FailWithAuditAsync(db, kind, id, "This master is used by one or more Stock Items and cannot be deleted.", cancellationToken);
        }

        set.Remove(entity);
        AddAudit(db, kind.ToString(), id, "Delete", true, $"Deleted '{entity.Name}'.");
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return OperationResult.Ok($"{entity.Name} deleted.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Fail("This master is linked to other records and cannot be deleted.");
        }
    }

    private async Task<OperationResult> FailWithAuditAsync(
        TexTrackDbContext db,
        MasterKind kind,
        long entityId,
        string message,
        CancellationToken cancellationToken)
    {
        AddAudit(db, kind.ToString(), entityId == 0 ? null : entityId, "ValidationBlocked", false, message);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Preserve the original validation message if audit writing also fails.
        }

        return OperationResult.Fail(message);
    }

    private void AddAudit(
        TexTrackDbContext db,
        string entityType,
        long? entityId,
        string action,
        bool success,
        string description)
    {
        db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyContext.CompanyId,
            EntityType = entityType,
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
