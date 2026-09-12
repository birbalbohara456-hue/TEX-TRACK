using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;
using TexTrack.Web.Domain;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public static class SecurityRoleCodes
{
    public const string Developer = "Developer";
    public const string Administrator = "Administrator";
    public const string Owner = "Owner";
    public const string Manager = "Manager";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";

    public static readonly string[] All = [Developer, Administrator, Owner, Manager, Operator, Viewer];
}

public static class TexTrackClaimTypes
{
    public const string CompanyId = "textrack:company_id";
    public const string CompanyName = "textrack:company_name";
    public const string FinancialYearId = "textrack:financial_year_id";
    public const string FinancialYearName = "textrack:financial_year_name";
    public const string SecurityStamp = "textrack:security_stamp";
    public const string SessionExpiresAt = "textrack:session_expires_at";
}

public static class PasswordCredentialService
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Prefix = "pbkdf2-sha256";

    public static string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string encoded)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(encoded)) return false;
        try
        {
            var parts = encoded.Split('$');
            if (parts.Length == 4 && parts[0] == Prefix && int.TryParse(parts[1], out var iterations) && iterations >= 100_000)
            {
                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }

            // Temporary compatibility for the pre-P0 Developer hash; successful login upgrades it.
            var legacy = encoded.Split(':', 2);
            if (legacy.Length != 2) return false;
            var legacySalt = Convert.FromBase64String(legacy[0]);
            var legacyExpected = Convert.FromBase64String(legacy[1]);
            var legacyActual = Rfc2898DeriveBytes.Pbkdf2(password, legacySalt, Iterations, HashAlgorithmName.SHA256, legacyExpected.Length);
            return CryptographicOperations.FixedTimeEquals(legacyActual, legacyExpected);
        }
        catch (FormatException) { return false; }
    }

    public static bool NeedsUpgrade(string encoded) => !encoded.StartsWith(Prefix + '$', StringComparison.Ordinal);
}

public sealed record CreateApplicationUserRequest(
    string UserName, string DisplayName, string Password, IReadOnlyCollection<string> RoleCodes, IReadOnlyCollection<long> CompanyIds);

public sealed record AuthenticationResult(bool Succeeded, bool IsLockedOut, ClaimsPrincipal? Principal)
{
    public static AuthenticationResult Failed(bool locked = false) => new(false, locked, null);
}

public sealed class UserIdentityService(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    IHttpContextAccessor httpContextAccessor)
{
    private readonly ClaimsPrincipal? initialPrincipal = httpContextAccessor.HttpContext?.User;
    private readonly ISessionValidator sessions = new PersistedSessionValidator(contextFactory, TimeProvider.System);
    public const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly string DummyPasswordHash = PasswordCredentialService.HashPassword("TexTrack timing equalizer");

    public Task<long> CreateUserAsync(CreateApplicationUserRequest request, CancellationToken cancellationToken = default) =>
        RunAdministrationAsync("UserCreated", null, async actor =>
    {
        if (request.Password.Length < 12) throw new InvalidOperationException("Password must contain at least 12 characters.");
        return await CreateUserCoreAsync(request.UserName, request.DisplayName,
            PasswordCredentialService.HashPassword(request.Password), request.RoleCodes, request.CompanyIds, actor, cancellationToken);
    }, cancellationToken);

    internal Task<long> CreateBootstrapUserAsync(string userName, string displayName, string existingPasswordHash,
        IReadOnlyCollection<string> roleCodes, IReadOnlyCollection<long> companyIds, CancellationToken cancellationToken = default) =>
        CreateUserCoreAsync(userName, displayName, existingPasswordHash, roleCodes, companyIds, "System:IdentityBootstrap", cancellationToken);

    private async Task<long> CreateUserCoreAsync(string userName, string displayName, string passwordHash,
        IReadOnlyCollection<string> roleCodes, IReadOnlyCollection<long> companyIds, string actor, CancellationToken cancellationToken)
    {
        var normalized = NormalizeUserName(userName);
        if (normalized.Length == 0) throw new InvalidOperationException("User name is required.");
        if (roleCodes.Count == 0) throw new InvalidOperationException("At least one role is required.");
        if (companyIds.Count == 0) throw new InvalidOperationException("At least one company membership is required.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.ApplicationUsers.AnyAsync(x => x.UserNameNormalized == normalized, cancellationToken))
            throw new InvalidOperationException("A user with this name already exists.");

        await EnsureSystemRolesAsync(db, cancellationToken);
        var distinctRoles = roleCodes.Distinct(StringComparer.Ordinal).ToArray();
        var roles = await db.SecurityRoles.Where(x => distinctRoles.Contains(x.Code)).ToListAsync(cancellationToken);
        if (roles.Count != distinctRoles.Length) throw new InvalidOperationException("One or more roles are invalid.");
        var distinctCompanies = companyIds.Distinct().ToArray();
        var activeCompanyIds = await db.Companies.Where(x => distinctCompanies.Contains(x.Id) && x.IsActive)
            .Select(x => x.Id).ToListAsync(cancellationToken);
        if (activeCompanyIds.Count != distinctCompanies.Length) throw new InvalidOperationException("One or more companies are invalid or inactive.");

        var now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser
        {
            UserName = userName.Trim(), UserNameNormalized = normalized, DisplayName = displayName.Trim(),
            PasswordHash = passwordHash, IsActive = true, CreatedAtUtc = now, ModifiedAtUtc = now
        };
        foreach (var role in roles) user.Roles.Add(new ApplicationUserRole { Role = role });
        for (var index = 0; index < activeCompanyIds.Count; index++)
            user.Companies.Add(new ApplicationUserCompany { CompanyId = activeCompanyIds[index], IsDefault = index == 0, IsActive = true });
        db.ApplicationUsers.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        db.AuditLogs.Add(AdministrationEvent("UserCreated", user.Id, actor, true,
            "Account created; roles: " + string.Join(",", distinctRoles.Order()) +
            "; company IDs: " + string.Join(",", distinctCompanies.Order()) + "."));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user.Id;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(string? userName, string? password, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeUserName(userName);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Lock the account before loading tracked state. ReadCommitted waiters then
        // observe the latest failure count, password and lockout instead of stale values.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM application_users WHERE user_name_normalized = {normalized} FOR UPDATE", cancellationToken);
        var user = await db.ApplicationUsers
            .Include(x => x.Roles).ThenInclude(x => x.Role)
            .Include(x => x.Companies).ThenInclude(x => x.Company)
            .SingleOrDefaultAsync(x => x.UserNameNormalized == normalized, cancellationToken);

        if (user is null)
        {
            PasswordCredentialService.VerifyPassword(password ?? string.Empty, DummyPasswordHash);
            return await FinishAsync(AuthenticationResult.Failed(), "LoginFailure", "Unknown account.");
        }

        var now = DateTimeOffset.UtcNow;
        var passwordValid = PasswordCredentialService.VerifyPassword(password ?? string.Empty, user.PasswordHash);
        if (!user.IsActive) return await FinishAsync(AuthenticationResult.Failed(), "LoginFailure", "Inactive account.");
        if (user.LockoutEndUtc > now) return await FinishAsync(AuthenticationResult.Failed(true), "LoginBlocked", "Account is locked.");
        if (user.LockoutEndUtc is not null)
        {
            user.FailedLoginCount = 0;
            user.LockoutEndUtc = null;
        }
        if (!passwordValid)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts) user.LockoutEndUtc = now.Add(LockoutDuration);
            user.ModifiedAtUtc = now;
            var locked = user.LockoutEndUtc > now;
            if (locked) AddEvent("LoginLockout", false, "Failed-attempt threshold reached.");
            return await FinishAsync(AuthenticationResult.Failed(locked), "LoginFailure", "Invalid credentials.");
        }

        var companies = user.Companies.Where(x => x.IsActive && x.Company.IsActive).OrderByDescending(x => x.IsDefault).ToArray();
        if (companies.Length == 0 || user.Roles.Count == 0)
            return await FinishAsync(AuthenticationResult.Failed(), "LoginFailure", "No active company access or role.");
        var activeCompany = companies[0].Company;
        var financialYear = await db.FinancialYears.AsNoTracking()
            .Where(x => x.CompanyId == activeCompany.Id && x.IsActive)
            .OrderByDescending(x => x.StartDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (financialYear is null)
            return await FinishAsync(AuthenticationResult.Failed(), "LoginFailure", "No active financial year.");
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = now;
        user.ModifiedAtUtc = now;
        if (PasswordCredentialService.NeedsUpgrade(user.PasswordHash)) user.PasswordHash = PasswordCredentialService.HashPassword(password!);
        return await FinishAsync(new AuthenticationResult(true, false, CreatePrincipal(user, companies, financialYear)),
            "LoginSuccess", "Authenticated.");

        void AddEvent(string action, bool success, string description)
        {
            // Authentication is account-scoped, before company selection. Never log
            // submitted passwords, hashes, cookies, or arbitrary submitted usernames.
            db.AuditLogs.Add(new AuditLog
            {
                EntityType = "Authentication", EntityId = user?.Id, Action = action,
                Success = success, Description = description,
                PerformedBy = user is null ? "Anonymous" : $"User:{user.Id}",
                PerformedAtUtc = DateTimeOffset.UtcNow
            });
        }

        async Task<AuthenticationResult> FinishAsync(AuthenticationResult result, string action, string description)
        {
            AddEvent(action, result.Succeeded, description);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
    }

    public Task DeactivateUserAsync(long userId, CancellationToken cancellationToken = default) =>
        RunAdministrationAsync("UserDeactivated", userId, async actor =>
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.ApplicationUsers.SingleAsync(x => x.Id == userId, cancellationToken);
        user.IsActive = false;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.ModifiedAtUtc = DateTimeOffset.UtcNow;
        db.AuditLogs.Add(AdministrationEvent("UserDeactivated", userId, actor, true, "Account deactivated; sessions revoked."));
        await db.SaveChangesAsync(cancellationToken);
        return userId;
    }, cancellationToken);

    public Task ResetPasswordAsync(long userId, string newPassword, CancellationToken cancellationToken = default) =>
        RunAdministrationAsync("UserPasswordReset", userId, async actor =>
    {
        if (newPassword.Length < 12) throw new InvalidOperationException("Password must contain at least 12 characters.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.ApplicationUsers.SingleAsync(x => x.Id == userId, cancellationToken);
        user.PasswordHash = PasswordCredentialService.HashPassword(newPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.ModifiedAtUtc = DateTimeOffset.UtcNow;
        db.AuditLogs.Add(AdministrationEvent("UserPasswordReset", userId, actor, true, "Administrative password reset; sessions revoked."));
        await db.SaveChangesAsync(cancellationToken);
        return userId;
    }, cancellationToken);

    // Account administration is global, not attributed to one arbitrarily selected company.
    // Deliberately allowlist metadata: never serialize a user/request or exception here.
    private static AuditLog AdministrationEvent(string action, long? target, string actor, bool success, string description) => new()
    {
        EntityType = "UserAdministration", EntityId = target, Action = action,
        PerformedBy = actor, Success = success, Description = description,
        PerformedAtUtc = DateTimeOffset.UtcNow
    };

    private async Task<long> RunAdministrationAsync(string action, long? target,
        Func<string, Task<long>> operation, CancellationToken cancellationToken)
    {
        var actor = "Anonymous";
        try
        {
            var principal = httpContextAccessor.HttpContext?.User ?? initialPrincipal ?? new ClaimsPrincipal();
            var valid = await IsPrincipalValidAsync(principal, cancellationToken);
            if (valid) actor = "User:" + principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!valid || !SecurityPolicies.PrincipalCanAccess(principal, SecurityPolicies.DeveloperOnly))
                throw new UnauthorizedAccessException("Developer authorization is required for user administration.");
            return await operation(actor);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or InvalidOperationException)
        {
            // Rejections have no committed account mutation. Record separately, without
            // logging supplied names/passwords or treating unvalidated claims as an actor.
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            db.AuditLogs.Add(AdministrationEvent(action, target, actor, false,
                error is UnauthorizedAccessException ? "Authorization denied." : "Request rejected."));
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> VerifyCurrentUserPasswordAsync(ClaimsPrincipal principal, string password, CancellationToken cancellationToken = default)
    {
        if (!await IsPrincipalValidAsync(principal, cancellationToken) ||
            !long.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return false;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var hash = await db.ApplicationUsers.Where(x => x.Id == userId && x.IsActive).Select(x => x.PasswordHash).SingleOrDefaultAsync(cancellationToken);
        return hash is not null && PasswordCredentialService.VerifyPassword(password, hash);
    }

    public Task<bool> IsPrincipalValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
        sessions.IsValidAsync(principal, cancellationToken);

    internal static async Task EnsureSystemRolesAsync(TexTrackDbContext db, CancellationToken cancellationToken)
    {
        var existing = await db.SecurityRoles.Select(x => x.Code).ToListAsync(cancellationToken);
        foreach (var code in SecurityRoleCodes.All.Except(existing, StringComparer.Ordinal))
            db.SecurityRoles.Add(new SecurityRole { Code = code, Name = code, IsSystem = true });
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(cancellationToken);
    }

    private static ClaimsPrincipal CreatePrincipal(
        ApplicationUser user,
        IReadOnlyCollection<ApplicationUserCompany> companies,
        FinancialYear financialYear)
    {
        var activeCompany = companies.First().Company;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, user.UserName),
            new("name", user.DisplayName), new(TexTrackClaimTypes.SecurityStamp, user.SecurityStamp),
            new(TexTrackClaimTypes.SessionExpiresAt, DateTimeOffset.UtcNow.Add(PersistedSessionValidator.SessionLifetime)
                .ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(TexTrackClaimTypes.CompanyId, activeCompany.Id.ToString()),
            new(TexTrackClaimTypes.CompanyName, activeCompany.Name),
            new(TexTrackClaimTypes.FinancialYearId, financialYear.Id.ToString()),
            new(TexTrackClaimTypes.FinancialYearName, financialYear.Name)
        };
        claims.AddRange(user.Roles.Select(x => new Claim(ClaimTypes.Role, x.Role.Code)));
        claims.AddRange(companies.Skip(1).Select(x => new Claim(TexTrackClaimTypes.CompanyId, x.CompanyId.ToString())));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TexTrackCookie"));
    }

    private static string NormalizeUserName(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
}

public sealed class IdentityBootstrapper(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    UserIdentityService identities,
    IConfiguration configuration,
    FirstRunRecoveryService recovery,
    ILogger<IdentityBootstrapper> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await UserIdentityService.EnsureSystemRolesAsync(db, cancellationToken);
        if (await db.ApplicationUsers.AnyAsync(cancellationToken)) return;

        var userName = configuration["DeveloperAccess:UserName"];
        var passwordHash = configuration["DeveloperAccess:PasswordHash"];
        var companyIds = await db.Companies.Where(x => x.IsActive).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(passwordHash) || companyIds.Length == 0)
        {
            var recoveryCode = recovery.Activate();
            logger.LogCritical("No application user exists. Open /login and use one-time recovery code {RecoveryCode} to create the initial Developer account.", recoveryCode);
            return;
        }

        await identities.CreateBootstrapUserAsync(userName, userName, passwordHash,
            [SecurityRoleCodes.Developer, SecurityRoleCodes.Administrator, SecurityRoleCodes.Owner], companyIds, cancellationToken);
        logger.LogWarning("Created the initial persisted Developer identity from legacy bootstrap configuration. Remove legacy credentials after verifying access.");
    }
}

/// <summary>
/// In-memory, one-time recovery gate used only when the database contains no
/// application identities. The code is printed to the local server console and
/// is never persisted in source, configuration, cookies or the database.
/// </summary>
public sealed class FirstRunRecoveryService(
    IDbContextFactory<TexTrackDbContext> contextFactory,
    IServiceScopeFactory scopeFactory)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? code;
    private int failedAttempts;

    public bool IsActive => Volatile.Read(ref code) is not null;

    public string Activate()
    {
        var current = Volatile.Read(ref code);
        if (current is not null) return current;
        var generated = Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
        Interlocked.CompareExchange(ref code, generated, null);
        return code!;
    }

    public async Task<OperationResult> CompleteAsync(
        string? suppliedCode,
        string? userName,
        string? password,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var expected = code;
            if (expected is null) return OperationResult.Fail("Initial setup is no longer available.");
            if (failedAttempts >= 5) return OperationResult.Fail("Initial setup is locked. Restart the TexTrack server to issue a new recovery code.");
            if (!CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes((suppliedCode ?? string.Empty).Trim().ToUpperInvariant()),
                    System.Text.Encoding.UTF8.GetBytes(expected)))
            {
                failedAttempts++;
                return OperationResult.Fail("The recovery code is invalid.");
            }
            if (string.IsNullOrWhiteSpace(userName)) return OperationResult.Fail("User ID is required.");
            if (string.IsNullOrEmpty(password) || password.Length < 12)
                return OperationResult.Fail("Password must contain at least 12 characters.");

            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            if (await db.ApplicationUsers.AnyAsync(cancellationToken))
            {
                code = null;
                return OperationResult.Fail("An application user already exists. Use the normal login form.");
            }
            var companyIds = await db.Companies.Where(x => x.IsActive).OrderBy(x => x.Id)
                .Select(x => x.Id).ToArrayAsync(cancellationToken);
            if (companyIds.Length == 0) return OperationResult.Fail("No active company exists for initial setup.");

            await using var scope = scopeFactory.CreateAsyncScope();
            var identities = scope.ServiceProvider.GetRequiredService<UserIdentityService>();
            await identities.CreateBootstrapUserAsync(userName, userName,
                PasswordCredentialService.HashPassword(password),
                [SecurityRoleCodes.Developer, SecurityRoleCodes.Administrator, SecurityRoleCodes.Owner],
                companyIds, cancellationToken);
            code = null;
            return OperationResult.Ok("Developer account created.");
        }
        finally { gate.Release(); }
    }
}
