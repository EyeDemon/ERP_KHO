using System.Reflection;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ERP.Api.Tests;

public sealed class StockAllocationAuthorizationMetadataTests
{
    [Fact]
    public void AllocationEndpoints_UseCanonicalPermissionCodesWithoutRoleAlternatives()
    {
        var expected = new Dictionary<string, string>
        {
            ["GetPage"] = AppPermissions.AllocationRead,
            ["Get"] = AppPermissions.AllocationRead,
            ["GetReservations"] = AppPermissions.AllocationRead,
            ["GetCandidates"] = AppPermissions.AllocationRead,
            ["Create"] = AppPermissions.AllocationCreate,
            ["AutoAllocate"] = AppPermissions.AllocationCreate,
            ["Release"] = AppPermissions.AllocationRelease,
            ["Reallocate"] = AppPermissions.AllocationReallocate,
        };

        var controller = typeof(StockAllocationsController);
        controller.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        controller.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().BeNull();

        var actions = controller.GetMethods()
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();
        actions.Should().HaveCount(expected.Count);

        foreach (var action in actions)
        {
            expected.Should().ContainKey(action.Name);
            var permission = action.GetCustomAttribute<PermissionAuthorizeAttribute>();
            permission.Should().NotBeNull();
            permission!.Permission.Should().Be(expected[action.Name]);
            action.GetCustomAttributes<AuthorizeAttribute>()
                .Should().OnlyContain(attribute => string.IsNullOrEmpty(attribute.Roles) && string.IsNullOrEmpty(attribute.Policy));
        }
    }

    [Fact]
    public void AllocationPermissionCodes_ArePublishedInCatalog()
    {
        AppPermissions.Catalog.Should().Contain([
            "allocation.read",
            "allocation.create",
            "allocation.release",
            "allocation.reallocate",
        ]);
    }
}
