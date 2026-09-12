using System.Security.Claims;

namespace TexTrack.Web.Services;

public sealed class DeveloperAccessService(IHttpContextAccessor httpContextAccessor)
{
    private readonly ClaimsPrincipal? initialPrincipal = httpContextAccessor.HttpContext?.User;
    public const string RoleName = SecurityRoleCodes.Developer;

    private ClaimsPrincipal Principal => httpContextAccessor.HttpContext?.User ?? initialPrincipal ?? new ClaimsPrincipal();

    public string UserName => Principal.Identity?.Name ?? "Anonymous";
    public bool IsDeveloper => Principal.IsInRole(RoleName);
    public string ActorName => Principal.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } userId
        ? $"{UserName} [{userId}]"
        : "Anonymous";
}
