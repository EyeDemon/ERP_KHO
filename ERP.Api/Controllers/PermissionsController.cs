using System.Security.Claims;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Domain.Entities;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController, Route("api/permissions"), Authorize]
public sealed class PermissionsController(ErpKhoDbContext db) : ControllerBase
{
    [HttpGet, PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> Catalog(CancellationToken ct) => Ok(await db.Permissions.AsNoTracking()
        .OrderBy(x => x.Code).Select(x => new { x.Code, x.Description }).ToListAsync(ct));

    [HttpGet("roles"), PermissionAuthorize(AppPermissions.RoleRead)]
    public async Task<IActionResult> Roles(CancellationToken ct) => Ok(await db.Roles.AsNoTracking().OrderBy(x => x.RoleName)
        .Select(x => new { x.Id, x.RoleName, RowVersion = Convert.ToBase64String(x.RowVersion),
            Permissions = x.Permissions.OrderBy(p => p.Permission.Code).Select(p => p.Permission.Code).ToArray() }).ToListAsync(ct));

    [HttpPost("roles/{roleId:int}/grants"), IdempotentCommand("Permission.Grant"), PermissionAuthorize(AppPermissions.PermissionAssign)]
    public Task<IActionResult> Grant(int roleId, PermissionGrantDto dto, CancellationToken ct) =>
        Change(roleId, dto.PermissionCode, dto.RowVersion, true, ct);

    [HttpDelete("roles/{roleId:int}/grants/{permissionCode}"), IdempotentCommand("Permission.Revoke"), PermissionAuthorize(AppPermissions.PermissionAssign)]
    public Task<IActionResult> Revoke(int roleId, string permissionCode, [FromBody] PermissionRevokeDto dto, CancellationToken ct) =>
        Change(roleId, permissionCode, dto.RowVersion, false, ct);

    private async Task<IActionResult> Change(int roleId, string input, byte[] version, bool add, CancellationToken ct)
    {
        var code = Permission.Normalize(input);
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            await PermissionAdministrationGuard.LockAsync(db, ct);
            var role = await db.Roles.SingleOrDefaultAsync(x => x.Id == roleId, ct);
            var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == code, ct);
            if (role is null || permission is null) return NotFound(new { message = "Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập." });
            if (version is null || version.Length != 8 || !role.RowVersion.SequenceEqual(version))
                throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
            var existing = await db.RolePermissions.SingleOrDefaultAsync(x => x.RoleId == roleId && x.PermissionId == permission.Id, ct);
            var changed = add ? existing is null : existing is not null;
            if (changed)
            {
                var actor = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                if (add) db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permission.Id, GrantedByUserId = actor });
                else db.RolePermissions.Remove(existing!);
                role.GrantRevision++;
                db.AuditLogs.Add(new AuditLog { UserId = actor, Action = add ? "Permission.Grant" : "Permission.Revoke", EntityName = "Role", EntityId = roleId,
                    OldValues = JsonSerializer.Serialize(new { permissionCode = code, granted = !add }),
                    NewValues = JsonSerializer.Serialize(new { permissionCode = code, granted = add }), Timestamp = DateTime.UtcNow });
                await db.SaveChangesAsync(ct);
            }
            // Validate the projected post-mutation state while the shared transaction lock is still held.
            await PermissionAdministrationGuard.EnsureAdministratorAsync(db, ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return Ok(new { roleId, rowVersion = Convert.ToBase64String(role.RowVersion), changed });
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
public sealed record PermissionGrantDto(string PermissionCode, byte[] RowVersion);
public sealed record PermissionRevokeDto(byte[] RowVersion);