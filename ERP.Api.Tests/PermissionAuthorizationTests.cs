using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Tests;

public sealed class PermissionAuthorizationTests
{
    [Theory]
    [InlineData(AppPermissions.ProductUpdate, AppPermissions.CategoryManage)]
    [InlineData(AppPermissions.ProductUpdate, AppPermissions.BarcodeManage)]
    [InlineData(AppPermissions.BarcodeManage, AppPermissions.ProductUpdate)]
    [InlineData(AppPermissions.LocationManage, AppPermissions.WarehouseManage)]
    [InlineData(AppPermissions.WarehouseManage, AppPermissions.LocationManage)]
    [InlineData(AppPermissions.ReceiptCancel, AppPermissions.ApprovalReject)]
    [InlineData(AppPermissions.ApprovalReject, AppPermissions.ReceiptRead)]
    public async Task GrantDoesNotAliasAnotherCapability(string granted, string required)
    {
        await using var db = CreateDatabase();
        var permission = new Permission(granted) { Id = 1 };
        var role = new Role { Id = 1, RoleName = "Admin" };
        role.Permissions.Add(new RolePermission { Role = role, Permission = permission, RoleId = 1, PermissionId = 1 });
        db.Users.Add(new User { Id = 1, Username = "qa", Role = role, RoleId = 1 });
        await db.SaveChangesAsync();
        var context = Context();
        await new PermissionAuthorizationFilter(db, required).OnAuthorizationAsync(context);
        Assert.Equal(403, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Fact]
    public async Task GrantAndRevokeTakeEffectOnNextRequestWithoutChangingRoleClaim()
    {
        await using var db = CreateDatabase();
        var role = new Role { Id = 1, RoleName = "Admin" };
        var user = new User { Id = 1, Username = "qa", RoleId = 1, Role = role };
        var permission = new Permission(AppPermissions.PutawayRead) { Id = 1 };
        db.AddRange(role, user, permission);
        await db.SaveChangesAsync();

        var denied = Context();
        await new PermissionAuthorizationFilter(db, permission.Code).OnAuthorizationAsync(denied);
        Assert.Equal(403, Assert.IsType<ObjectResult>(denied.Result).StatusCode);

        var grant = new RolePermission { RoleId = role.Id, PermissionId = permission.Id, Permission = permission, Role = role };
        db.RolePermissions.Add(grant);
        await db.SaveChangesAsync();
        var allowed = Context();
        await new PermissionAuthorizationFilter(db, permission.Code).OnAuthorizationAsync(allowed);
        Assert.Null(allowed.Result);

        db.RolePermissions.Remove(grant);
        await db.SaveChangesAsync();
        var revoked = Context();
        await new PermissionAuthorizationFilter(db, permission.Code).OnAuthorizationAsync(revoked);
        Assert.Equal(403, Assert.IsType<ObjectResult>(revoked.Result).StatusCode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task InactiveOrLockedUserCannotUseGrant(bool active, bool locked)
    {
        await using var db = CreateDatabase();
        var permission = new Permission(AppPermissions.PutawayRead) { Id = 1 };
        var role = new Role { Id = 1, RoleName = "Admin" };
        role.Permissions.Add(new RolePermission { RoleId = 1, PermissionId = 1, Permission = permission, Role = role });
        db.Users.Add(new User { Id = 1, Username = "qa", RoleId = 1, Role = role, IsActive = active, LockoutEnd = locked ? DateTime.UtcNow.AddHours(1) : null });
        await db.SaveChangesAsync();
        var context = Context();
        await new PermissionAuthorizationFilter(db, permission.Code).OnAuthorizationAsync(context);
        Assert.IsType<UnauthorizedObjectResult>(context.Result);
    }

    [Fact]
    public async Task UnavailablePermissionStoreFailsClosed()
    {
        using var db = CreateDatabase();
        await db.DisposeAsync();
        var context = Context();
        await new PermissionAuthorizationFilter(db, AppPermissions.PutawayRead).OnAuthorizationAsync(context);
        Assert.Equal(503, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Fact]
    public async Task StaleAdminClaimCannotRestoreDatabaseRolePrivileges()
    {
        await using var db = CreateDatabase();
        var permission = new Permission(AppPermissions.PutawayRead) { Id = 1 };
        var role = new Role { Id = 1, RoleName = "Viewer" };
        role.Permissions.Add(new RolePermission { RoleId = 1, PermissionId = 1, Permission = permission, Role = role });
        db.Users.Add(new User { Id = 1, Username = "qa", RoleId = 1, Role = role });
        await db.SaveChangesAsync();
        var context = Context();
        await new PermissionAuthorizationFilter(db, permission.Code).OnAuthorizationAsync(context);
        Assert.Null(context.Result);
        var currentUser = new HttpCurrentUser(new HttpContextAccessor { HttpContext = context.HttpContext });
        Assert.Equal("Viewer", currentUser.Role);
        Assert.False(currentUser.IsGlobalAdmin);
    }

    [Theory]
    [InlineData(" RECEIPT.READ ", "receipt.read")]
    [InlineData("putaway.execute", "putaway.execute")]
    public void NormalizesCodes(string input, string expected) => Assert.Equal(expected, Permission.Normalize(input));

    [Theory]
    [InlineData("receipt")]
    [InlineData("receipt..read")]
    [InlineData("receipt/read")]
    public void RejectsInvalidCodes(string input) => Assert.Throws<ArgumentException>(() => Permission.Normalize(input));

    private static ErpKhoDbContext CreateDatabase() => new(new DbContextOptionsBuilder<ErpKhoDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AuthorizationFilterContext Context()
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "Admin")], "qa")) };
        return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
    }
}
