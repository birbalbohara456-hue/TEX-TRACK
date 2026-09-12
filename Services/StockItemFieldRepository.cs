using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed class StockItemFieldRepository(IDbContextFactory<TexTrackDbContext> contextFactory, CurrentCompanyContext company)
{
    public async Task<List<StockItemFieldDefinitionEditModel>> GetAllAsync(CancellationToken ct = default)
    {
        await company.RequirePolicyAsync(SecurityPolicies.ViewReports, ct);
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.StockItemFieldDefinitions.AsNoTracking().Where(x => x.CompanyId == company.CompanyId)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new StockItemFieldDefinitionEditModel
            {
                Id=x.Id, Name=x.Name, FieldType=x.FieldType, DisplayOrder=x.DisplayOrder, IsRequired=x.IsRequired,
                AllowMultiple=x.AllowMultiple, OptionsText=x.OptionsText, IsActive=x.IsActive, ConcurrencyToken=x.ConcurrencyToken
            }).ToListAsync(ct);
    }

    public async Task<OperationResult> SaveAsync(StockItemFieldDefinitionEditModel model, CancellationToken ct = default)
    {
        await company.RequirePolicyAsync(SecurityPolicies.ManageMasters, ct);
        model.Name = model.Name.Trim(); model.OptionsText = model.OptionsText.Trim();
        if (string.IsNullOrWhiteSpace(model.Name)) return OperationResult.Fail("Field Name is required.");
        if (model.FieldType is not ("Text" or "Number" or "Date" or "YesNo" or "Choice" or "Photo")) return OperationResult.Fail("Select a valid field type.");
        if (model.DisplayOrder is < 1 or > 9999) return OperationResult.Fail("Display Order must be between 1 and 9999.");
        if (model.FieldType == "Choice" && string.IsNullOrWhiteSpace(model.OptionsText)) return OperationResult.Fail("Enter at least one choice.");
        if (model.FieldType != "Photo") model.AllowMultiple = false;
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var normalized = model.Name.ToUpperInvariant();
        if (await db.StockItemFieldDefinitions.AnyAsync(x => x.CompanyId == company.CompanyId && x.NameNormalized == normalized && x.Id != model.Id, ct))
            return OperationResult.Fail($"A Stock Item Field named '{model.Name}' already exists.");
        StockItemFieldDefinition entity;
        var now = DateTimeOffset.UtcNow;
        if (model.Id == 0)
        {
            entity = new StockItemFieldDefinition { CompanyId=company.CompanyId, CreatedAtUtc=now, CreatedBy=company.Actor };
            db.Add(entity);
        }
        else
        {
            entity = await db.StockItemFieldDefinitions.SingleOrDefaultAsync(x => x.CompanyId == company.CompanyId && x.Id == model.Id, ct)
                ?? throw new InvalidOperationException("The selected field no longer exists.");
            if (entity.ConcurrencyToken != model.ConcurrencyToken) return OperationResult.Fail("This field was changed by another user. Reload and try again.");
            if (entity.FieldType != model.FieldType && await db.StockItemFieldValues.AnyAsync(x => x.FieldDefinitionId == entity.Id, ct))
                return OperationResult.Fail("Field Type cannot be changed after values have been saved. Create a new field instead.");
        }
        entity.Name=model.Name; entity.NameNormalized=normalized; entity.FieldType=model.FieldType; entity.DisplayOrder=model.DisplayOrder;
        entity.IsRequired=model.IsRequired; entity.AllowMultiple=model.AllowMultiple; entity.OptionsText=model.OptionsText; entity.IsActive=model.IsActive;
        entity.ModifiedAtUtc=now; entity.ModifiedBy=company.Actor; entity.ConcurrencyToken=Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);
        return OperationResult.Ok($"{entity.Name} saved.", entity.Id);
    }

    public async Task<OperationResult> DeleteAsync(long id, CancellationToken ct = default)
    {
        await company.RequirePolicyAsync(SecurityPolicies.ManageMasters, ct);
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var entity = await db.StockItemFieldDefinitions.SingleOrDefaultAsync(x => x.CompanyId == company.CompanyId && x.Id == id, ct);
        if (entity is null) return OperationResult.Fail("The selected field no longer exists.");
        if (await db.StockItemFieldValues.AnyAsync(x => x.FieldDefinitionId == id, ct) || await db.StockItemPhotos.AnyAsync(x => x.FieldDefinitionId == id, ct))
            return OperationResult.Fail("This field has saved item data. Mark it inactive instead of deleting it.");
        db.Remove(entity); await db.SaveChangesAsync(ct); return OperationResult.Ok($"{entity.Name} deleted.");
    }
}
