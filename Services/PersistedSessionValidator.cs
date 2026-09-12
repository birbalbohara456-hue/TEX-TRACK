using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;

namespace TexTrack.Web.Services;

public interface ISessionValidator
{
    Task<bool> IsValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}

/// <summary>Never caches successful authorization across operations or circuits.</summary>
public sealed class PersistedSessionValidator(
    IDbContextFactory<TexTrackDbContext> contextFactory, TimeProvider clock) : ISessionValidator
{
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(2);

    public async Task<bool> IsValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            !TryId(ClaimTypes.NameIdentifier, out var userId) ||
            !TryId(TexTrackClaimTypes.CompanyId, out var companyId) ||
            !TryId(TexTrackClaimTypes.FinancialYearId, out var yearId) ||
            !long.TryParse(principal.FindFirstValue(TexTrackClaimTypes.SessionExpiresAt),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiresAt) ||
            expiresAt <= clock.GetUtcNow().ToUnixTimeSeconds()) return false;

        var stamp = principal.FindFirstValue(TexTrackClaimTypes.SecurityStamp);
        if (string.IsNullOrWhiteSpace(stamp)) return false;

        // A fresh context observes committed changes rather than tracked login state.
        // Database failures propagate: callers must not proceed on an unverified session.
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.ApplicationUsers.AsNoTracking()
            .Where(x => x.Id == userId && x.IsActive && x.SecurityStamp == stamp &&
                x.Companies.Any(m => m.CompanyId == companyId && m.IsActive && m.Company.IsActive))
            .Select(x => new { Roles = x.Roles.Select(r => r.Role.Code).ToArray() })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null || user.Roles.Length == 0 ||
            !user.Roles.ToHashSet(StringComparer.Ordinal).SetEquals(
                principal.FindAll(ClaimTypes.Role).Select(x => x.Value))) return false;

        return await db.FinancialYears.AsNoTracking().AnyAsync(
            x => x.Id == yearId && x.CompanyId == companyId && x.IsActive, cancellationToken);

        bool TryId(string type, out long id) => long.TryParse(principal.FindFirstValue(type),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0;
    }
}
