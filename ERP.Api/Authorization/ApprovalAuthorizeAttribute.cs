using System.Security.Claims;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Authorization;

[AttributeUsage(AttributeTargets.Class)]
public sealed class ApprovalAuthorizeAttribute : TypeFilterAttribute
{
    public ApprovalAuthorizeAttribute() : base(typeof(ApprovalAuthorizationFilter)) { Order = int.MinValue; }
}

// Establish database identity before idempotency replay on the mixed surface.
public sealed class ApprovalAuthorizationFilter(ErpKhoDbContext db) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!int.TryParse(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        { context.Result = new UnauthorizedResult(); return; }
        try
        {
            var user = await db.Users.AsNoTracking().Where(u => u.Id == id)
                .Select(u => new { u.IsActive, u.LockoutEnd, Role = u.Role.RoleName,
                    Permissions = u.Role.Permissions.Select(g => g.Permission.Code).ToArray() })
                .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
            if (user is null || !user.IsActive || user.LockoutEnd > DateTime.UtcNow)
            { context.Result = new UnauthorizedObjectResult(new { message = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại." }); return; }
            context.HttpContext.Items[HttpCurrentUser.EffectiveRoleKey] = user.Role;
            var type = context.RouteData.Values.TryGetValue("documentType", out var value) ? value?.ToString() : null;
            var legacy = user.Role is "Admin" or "Manager";
            var required = HttpMethods.IsPost(context.HttpContext.Request.Method) ? AppPermissions.ApprovalReject : AppPermissions.ReceiptRead;
            var allowed = type == "ImportReceipt" ? user.Permissions.Contains(required)
                : type == "ExportReceipt" ? HttpMethods.IsPost(context.HttpContext.Request.Method)
                    ? user.Permissions.Contains(AppPermissions.ApprovalReject) && user.Permissions.Contains(AppPermissions.ExportCancel)
                    : user.Permissions.Contains(AppPermissions.ExportRead)
                : type is null ? legacy || user.Permissions.Contains(AppPermissions.ReceiptRead) || user.Permissions.Contains(AppPermissions.ExportRead) : legacy;
            if (!allowed) context.Result = new ObjectResult(new { message = "Bạn không có quyền thực hiện thao tác này." }) { StatusCode = 403 };
        }
        catch
        { context.Result = new ObjectResult(new { message = "Không thể xác minh quyền truy cập. Vui lòng thử lại." }) { StatusCode = 503 }; }
    }
}
