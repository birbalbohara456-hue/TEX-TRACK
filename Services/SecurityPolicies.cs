using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace TexTrack.Web.Services;

public static class SecurityPolicies
{
    public const string ViewReports = nameof(ViewReports);
    public const string OperateVouchers = nameof(OperateVouchers);
    public const string ManageMasters = nameof(ManageMasters);
    public const string CancelOrDeleteVouchers = nameof(CancelOrDeleteVouchers);
    public const string ExportData = nameof(ExportData);
    public const string ViewAuditTrail = nameof(ViewAuditTrail);
    public const string AdminSettings = nameof(AdminSettings);
    public const string DeveloperOnly = nameof(DeveloperOnly);

    private static readonly IReadOnlyDictionary<string, string[]> RoleMatrix =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [ViewReports] =
            [
                SecurityRoleCodes.Developer, SecurityRoleCodes.Administrator, SecurityRoleCodes.Owner,
                SecurityRoleCodes.Manager, SecurityRoleCodes.Operator, SecurityRoleCodes.Viewer
            ],
            [OperateVouchers] =
            [
                SecurityRoleCodes.Developer, SecurityRoleCodes.Administrator, SecurityRoleCodes.Owner,
                SecurityRoleCodes.Manager, SecurityRoleCodes.Operator
            ],
            [ManageMasters] =
            [
                SecurityRoleCodes.Developer, SecurityRoleCodes.Administrator, SecurityRoleCodes.Owner,
                SecurityRoleCodes.Manager
            ],
            [CancelOrDeleteVouchers] =
            [
                SecurityRoleCodes.Developer, SecurityRoleCodes.Administrator, SecurityRoleCodes.Owner,
                SecurityRoleCodes.Manager
            ],
            [ExportData] =
            [
                SecurityRoleCodes.Developer, SecurityRoleCodes.Administrator, SecurityRoleCodes.Owner,
                SecurityRoleCodes.Manager
            ],
            [ViewAuditTrail] =
            [
                SecurityRoleCodes.Developer, SecurityRoleCodes.Administrator,
                SecurityRoleCodes.Owner, SecurityRoleCodes.Manager
            ],
            [AdminSettings] =
            [
                SecurityRoleCodes.Developer, SecurityRoleCodes.Administrator, SecurityRoleCodes.Owner
            ],
            [DeveloperOnly] = [SecurityRoleCodes.Developer]
        };

    public static bool RoleCanAccess(string role, string policy) =>
        RoleMatrix.TryGetValue(policy, out var permittedRoles) &&
        permittedRoles.Contains(role, StringComparer.Ordinal);

    public static bool PrincipalCanAccess(ClaimsPrincipal principal, string policy) =>
        principal.Identity?.IsAuthenticated == true &&
        RoleMatrix.TryGetValue(policy, out var permittedRoles) &&
        permittedRoles.Any(principal.IsInRole);

    public static void Configure(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var (policy, roles) in RoleMatrix)
            options.AddPolicy(policy, builder => builder.RequireAuthenticatedUser().RequireRole(roles));
    }
}
