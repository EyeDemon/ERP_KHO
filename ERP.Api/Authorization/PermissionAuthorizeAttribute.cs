using System.Security.Claims;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Authorization;

[AttributeUsage(AttributeTargets.Class|AttributeTargets.Method, AllowMultiple=true, Inherited=true)]
public sealed class PermissionAuthorizeAttribute : TypeFilterAttribute
{
    public string Permission { get; }
    public PermissionAuthorizeAttribute(string permission) : base(typeof(PermissionAuthorizationFilter))
    {
        Permission = permission;
        Arguments = [permission];
        Order = int.MinValue;
    }
}

public sealed class PermissionAuthorizationFilter(ErpKhoDbContext db, string permission) : IAsyncAuthorizationFilter, IOrderedFilter
{
    public int Order => int.MinValue;
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true || !int.TryParse(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        { context.Result=new UnauthorizedObjectResult(new { message="Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại." }); return; }
        try
        {
            var user = await db.Users.AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new { u.IsActive, u.LockoutEnd, RoleName = u.Role.RoleName, Allowed = u.Role.Permissions.Any(rp => rp.Permission.Code == permission) })
                .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
            if (user is null || !user.IsActive || user.LockoutEnd > DateTime.UtcNow)
            {
                context.Result = new UnauthorizedObjectResult(new { message = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại." });
                return;
            }
            var allowed = user.Allowed;
            context.HttpContext.Items[HttpCurrentUser.EffectiveRoleKey] = user.RoleName;
            if(!allowed) context.Result=new ObjectResult(new { message="Bạn không có quyền thực hiện thao tác này." }){StatusCode=StatusCodes.Status403Forbidden};
        }
        catch
        {
            context.Result=new ObjectResult(new { message="Không thể xác minh quyền truy cập. Vui lòng thử lại." }){StatusCode=StatusCodes.Status503ServiceUnavailable};
        }
    }
}
