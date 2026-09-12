using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using TexTrack.Web.Components;
using TexTrack.Web.Data;
using TexTrack.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// TexTrack is normally launched by an ordinary Windows user.  The default
// Windows Event Log provider can throw AccessDenied while trying to record a
// warning, which then aborts the HTTP request itself.  Keep diagnostics in the
// console/debug streams owned by the local server window instead.
builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var connectionString = builder.Configuration.GetConnectionString("TexTrackDatabase")
    ?? throw new InvalidOperationException("Connection string 'TexTrackDatabase' is missing.");
if (!builder.Environment.IsDevelopment() &&
    (string.IsNullOrWhiteSpace(builder.Configuration["AllowedHosts"]) ||
     builder.Configuration["AllowedHosts"] == "*"))
{
    throw new InvalidOperationException(
        "Production startup requires an explicit AllowedHosts value. Configure the server DNS name(s); wildcard hosts are not permitted.");
}

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.Cookie.Name = "TexTrack.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = PersistedSessionValidator.SessionLifetime;
        options.SlidingExpiration = false;
        options.Events.OnValidatePrincipal = async context =>
        {
            var identities = context.HttpContext.RequestServices.GetRequiredService<UserIdentityService>();
            if (await identities.IsPrincipalValidAsync(
                    context.Principal ?? new System.Security.Claims.ClaimsPrincipal(),
                    context.HttpContext.RequestAborted))
                return;

            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    SecurityPolicies.Configure(options);
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddDataProtection();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("Authentication", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddPooledDbContextFactory<TexTrackDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddSingleton<DatabaseStatus>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ISessionValidator, PersistedSessionValidator>();
builder.Services.AddScoped<AuthenticationStateProvider, TexTrackRevalidatingAuthenticationStateProvider>();
builder.Services.AddScoped<CurrentCompanyContext>();
builder.Services.AddScoped<CurrentPeriodContext>();
builder.Services.AddScoped<GlobalOperationState>();
builder.Services.AddSingleton<DatabaseBootstrapper>();
builder.Services.AddScoped<MasterRepository>();
builder.Services.AddScoped<LedgerRepository>();
builder.Services.AddScoped<JobWorkerRepository>();
builder.Services.AddScoped<StockItemRepository>();
builder.Services.AddScoped<StockItemFieldRepository>();
builder.Services.AddScoped<BillOfMaterialRepository>();
builder.Services.AddScoped<JobWorkOrderRepository>();
builder.Services.AddScoped<MasterJobOrderRepository>();
builder.Services.AddScoped<MaterialOutRepository>();
builder.Services.AddScoped<MaterialInRepository>();
builder.Services.AddScoped<InventoryInwardRepository>();
builder.Services.AddScoped<PurchaseOrderRepository>();
builder.Services.AddScoped<PurchaseReturnRepository>();
builder.Services.AddScoped<VoucherLifecycleService>();
builder.Services.AddSingleton<VoucherAuditHistoryService>();
builder.Services.AddScoped<VoucherSequenceAllocator>();
builder.Services.AddScoped<StockPostingService>();
builder.Services.AddScoped<InventoryPeriodControlService>();
builder.Services.AddScoped<InventoryPolicySettingsService>();
builder.Services.AddScoped<StockPositionService>();
builder.Services.AddScoped<OperationalReportingService>();
builder.Services.AddScoped<VoucherHistoryRepository>();
builder.Services.AddScoped<VoucherAuditTrailRepository>();
builder.Services.AddScoped<VoucherTypeRepository>();
builder.Services.AddSingleton<TallyXmlParser>();
builder.Services.AddScoped<TallyXmlExchangeService>();
builder.Services.AddScoped<TallyXmlExporter>();
builder.Services.AddScoped<UserIdentityService>();
builder.Services.AddScoped<IdentityBootstrapper>();
builder.Services.AddSingleton<FirstRunRecoveryService>();
builder.Services.AddScoped<DeveloperAccessService>();
builder.Services.AddScoped<DatabaseMaintenanceService>();
builder.Services.AddScoped<AssistantSettingsStore>();
builder.Services.AddHttpClient<IAssistantModelClient, AssistantModelClient>();
builder.Services.AddScoped<IAssistantEvidenceProvider, TexTrackAssistantEvidenceProvider>();
builder.Services.AddScoped<TexTrackAssistant>();

var app = builder.Build();

await app.Services.GetRequiredService<DatabaseBootstrapper>().InitializeAsync();
if (app.Services.GetRequiredService<DatabaseStatus>().IsConnected)
{
    await app.Services.GetRequiredService<VoucherAuditHistoryService>().EnsureLegacyBaselinesAsync();
    await using var identityScope = app.Services.CreateAsyncScope();
    await identityScope.ServiceProvider.GetRequiredService<IdentityBootstrapper>().InitializeAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/login", (HttpContext context, IAntiforgery antiforgery, DatabaseStatus database, FirstRunRecoveryService recovery) =>
{
    return Results.Content(LoginPageRenderer.Render(context, antiforgery, database, recovery.IsActive), "text/html");
}).AllowAnonymous().RequireRateLimiting("Authentication");

app.MapPost("/login", async (HttpContext context, UserIdentityService identities, IAntiforgery antiforgery) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    var form = await context.Request.ReadFormAsync();
    var returnUrl = form["returnUrl"].FirstOrDefault();
    var safeReturnUrl = SecurityRequestGuard.IsLocalReturnUrl(returnUrl) ? returnUrl! : "/";
    var result = await identities.AuthenticateAsync(
        form["userName"].FirstOrDefault(), form["password"].FirstOrDefault(), context.RequestAborted);
    if (!result.Succeeded || result.Principal is null)
        return Results.Redirect($"/login?error=1&returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, result.Principal);
    return Results.Redirect(safeReturnUrl);
}).AllowAnonymous().RequireRateLimiting("Authentication");

app.MapPost("/initial-setup", async (HttpContext context, FirstRunRecoveryService recovery,
    UserIdentityService identities, IAntiforgery antiforgery) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    var form = await context.Request.ReadFormAsync();
    var returnUrl = form["returnUrl"].FirstOrDefault();
    var safeReturnUrl = SecurityRequestGuard.IsLocalReturnUrl(returnUrl) ? returnUrl! : "/";
    var userName = form["userName"].FirstOrDefault();
    var password = form["password"].FirstOrDefault();
    var setup = await recovery.CompleteAsync(form["recoveryCode"].FirstOrDefault(), userName, password, context.RequestAborted);
    if (!setup.Success) return Results.Redirect($"/login?setupError=1&returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
    var authentication = await identities.AuthenticateAsync(userName, password, context.RequestAborted);
    if (!authentication.Succeeded || authentication.Principal is null) return Results.Redirect("/login?error=1");
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, authentication.Principal);
    return Results.Redirect(safeReturnUrl);
}).AllowAnonymous().RequireRateLimiting("Authentication");

app.MapGet("/developer-login", (HttpContext context) =>
{
    var returnUrl = context.Request.Query["returnUrl"].FirstOrDefault();
    return Results.Redirect(SecurityRequestGuard.IsLocalReturnUrl(returnUrl)
        ? $"/login?returnUrl={Uri.EscapeDataString(returnUrl!)}" : "/login");
}).AllowAnonymous();

app.MapPost("/developer-logout", async (HttpContext context, IAntiforgery antiforgery) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
});

var tallyExportApi = app.MapGet("/api/tally-xml/export/{package}", async (
    string package, DateOnly from, DateOnly to, TallyXmlExporter exporter, CancellationToken cancellationToken) =>
{
    if (package is not ("jwo" or "material")) return Results.BadRequest("Package must be 'jwo' or 'material'.");
    if (to < from) return Results.BadRequest("The To date cannot be before the From date.");
    var file = await exporter.ExportAsync(package, from, to, cancellationToken);
    return Results.File(file.Content, file.ContentType, file.FileName);
});
tallyExportApi.RequireAuthorization(SecurityPolicies.ExportData);

var stockItemPhotoApi = app.MapGet("/api/stock-item-photos/{id:long}", async (
    long id, IDbContextFactory<TexTrackDbContext> contexts, CurrentCompanyContext company, CancellationToken cancellationToken) =>
{
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var photo = await db.StockItemPhotos.AsNoTracking()
        .Where(x => x.Id == id && x.CompanyId == company.CompanyId)
        .Select(x => new { x.Content, x.ContentType, x.FileName })
        .SingleOrDefaultAsync(cancellationToken);
    return photo is null ? Results.NotFound() : Results.File(photo.Content, photo.ContentType, photo.FileName);
});
stockItemPhotoApi.RequireAuthorization(SecurityPolicies.ViewReports);

var stockItemDesignPhotoApi = app.MapGet("/api/stock-item-design-photos/{stockItemId:long}", async (
    long stockItemId, IDbContextFactory<TexTrackDbContext> contexts, CurrentCompanyContext company, CancellationToken cancellationToken) =>
{
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var photo = await db.StockItemDesignPhotos.AsNoTracking()
        .Where(x => x.StockItemId == stockItemId && x.CompanyId == company.CompanyId)
        .Select(x => new { x.Content, x.ContentType, x.FileName })
        .SingleOrDefaultAsync(cancellationToken);
    return photo is null ? Results.NotFound() : Results.File(photo.Content, photo.ContentType, photo.FileName);
});
stockItemDesignPhotoApi.RequireAuthorization(SecurityPolicies.ViewReports);

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
