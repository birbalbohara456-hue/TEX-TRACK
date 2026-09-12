using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TexTrack.Web.Services;

namespace TexTrack.Web.IntegrationTests;

// Explicit fixture for tests of business rules/claim parsing, NOT session security.
// SessionRevocationTests uses PersistedSessionValidator and real authenticated users.
internal sealed class BusinessRuleTestSessionValidator : ISessionValidator
{
    public Task<bool> IsValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

// Unlike HttpContextAccessor's shared AsyncLocal, each test circuit owns its context.
internal sealed class TestCircuitHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; }
}
