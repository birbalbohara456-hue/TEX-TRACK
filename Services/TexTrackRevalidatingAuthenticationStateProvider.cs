using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace TexTrack.Web.Services;

/// <summary>
/// Refreshes the visible authentication state of an open circuit. Protected services
/// independently validate every operation; this timer is not the write boundary.
/// </summary>
public class TexTrackRevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory, ISessionValidator sessions)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);

    protected override Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken) =>
        sessions.IsValidAsync(authenticationState.User, cancellationToken);
}
