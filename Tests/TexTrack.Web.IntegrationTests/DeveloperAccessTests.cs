using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class DeveloperAccessTests
{
    [Fact]
    public void DeveloperRoleCannotBeSimulatedByAnOrdinaryAuthenticatedUser()
    {
        var ordinary = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "ordinary-user")], "test"));
        var developer = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Name, "DEVELOPER"), new Claim(ClaimTypes.Role, DeveloperAccessService.RoleName)], "test"));
        Assert.False(CreateService(ordinary).IsDeveloper);
        Assert.True(CreateService(developer).IsDeveloper);
    }

    private static DeveloperAccessService CreateService(ClaimsPrincipal principal)
    {
        return new DeveloperAccessService(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal }
        });
    }
}
