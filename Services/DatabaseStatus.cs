using System.Security.Claims;

namespace TexTrack.Web.Services;

public sealed class DatabaseStatus
{
    public bool IsConnected { get; private set; }
    public string Message { get; private set; } = "Database has not been checked.";

    public void MarkConnected(string message)
    {
        IsConnected = true;
        Message = message;
    }

    public void MarkUnavailable(string message)
    {
        IsConnected = false;
        Message = message;
    }
}

public sealed class CurrentCompanyContext(IHttpContextAccessor httpContextAccessor, ISessionValidator sessions)
{
    private readonly ClaimsPrincipal? initialPrincipal = httpContextAccessor.HttpContext?.User;
    private ClaimsPrincipal Principal => httpContextAccessor.HttpContext?.User ?? initialPrincipal ?? new ClaimsPrincipal();

    public long CompanyId => RequiredLongClaim(TexTrackClaimTypes.CompanyId);
    public string CompanyName => RequiredClaim(TexTrackClaimTypes.CompanyName);
    public long FinancialYearId => RequiredLongClaim(TexTrackClaimTypes.FinancialYearId);
    public string FinancialYearName => RequiredClaim(TexTrackClaimTypes.FinancialYearName);
    public string Actor
    {
        get
        {
            if (!long.TryParse(Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
                throw new UnauthorizedAccessException("The authenticated session has no valid user identity for auditing.");
            var name = Principal.FindFirstValue(ClaimTypes.Name)?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new UnauthorizedAccessException("The authenticated session has no valid user identity for auditing.");
            return $"user:{userId}:{name}";
        }
    }

    public string ActorFor(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return $"{Actor} via {source.Trim()}";
    }

    public async Task RequirePolicyAsync(string policy, CancellationToken cancellationToken = default)
    {
        _ = CompanyId;
        _ = FinancialYearId;

        if (!SecurityPolicies.PrincipalCanAccess(Principal, policy))
            throw new UnauthorizedAccessException("The authenticated user is not authorized for this operation.");

        if (!await sessions.IsValidAsync(Principal, cancellationToken))
            throw new UnauthorizedAccessException("Your session has expired or access has changed. Please sign in again.");
    }

    private long RequiredLongClaim(string type) => long.TryParse(Principal.FindFirstValue(type), out var value) && value > 0
        ? value
        : throw new UnauthorizedAccessException("The authenticated session has no valid company context.");

    private string RequiredClaim(string type) => Principal.FindFirstValue(type) is { Length: > 0 } value
        ? value
        : throw new UnauthorizedAccessException("The authenticated session has no valid company context.");
}
