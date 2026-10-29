using System.Net;
using System.Text.Json;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Tests;

public sealed partial class InboundHttpIntegrationTests
{
    private static ImportReceipt QcReceipt(string code, int warehouse, int maker, Product product, int count = 2)
    {
        var receipt = Receipt(code, warehouse, maker, product, ReceiptStatus.QcPending, 0);
        receipt.Details = Enumerable.Range(0, count).Select(_ =>
        {
            var line = Receipt("unused", warehouse, maker, product, ReceiptStatus.QcPending, 0).Details.Single();
            line.RequiresQc = true; line.QcState = ReceiptLineQcState.QcPending;
            line.AcceptedQuantity = 0; line.BaseAcceptedQuantity = 0;
            return line;
        }).ToList();
        return receipt;
    }

    private static string QcBody(params ImportReceiptDetail[] lines) => JsonSerializer.Serialize(new
    {
        lines = lines.Select(l => new { lineId = l.Id, acceptedQuantity = 5, damagedQuantity = 0, rejectedQuantity = 0 })
    });

    private static async Task QcGrant(ERP.Infrastructure.Persistence.ErpKhoDbContext db, int role, string code, bool allowed)
    {
        var permission = await db.Permissions.SingleAsync(p => p.Code == code);
        var grant = await db.RolePermissions.SingleOrDefaultAsync(p => p.RoleId == role && p.PermissionId == permission.Id);
        if (allowed && grant is null) db.RolePermissions.Add(new RolePermission { RoleId = role, PermissionId = permission.Id });
        if (!allowed && grant is not null) db.RolePermissions.Remove(grant);
        await db.SaveChangesAsync();
    }

    [ApprovalSqlServerFact]
    public async Task Qc_execute_partial_final_and_replay_use_original_transition_permissions()
    {
        await using var database = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await database.MigrateAndSeedAsync();
        await using var db = Context(database.ConnectionString);
        var manager = await db.Users.SingleAsync(u => u.Role.RoleName == "Manager");
        var admin = await db.Users.SingleAsync(u => u.Role.RoleName == "Admin");
        var warehouse = await db.Warehouses.SingleAsync();
        var product = await db.Products.Include(p => p.Unit).SingleAsync();
        db.UserWarehouses.Add(new UserWarehouse { UserId = manager.Id, WarehouseId = warehouse.Id, CreatedBy = admin.Id });
        var receipt = QcReceipt("HTTP-QC-CONDITIONAL", warehouse.Id, admin.Id, product);
        db.ImportReceipts.Add(receipt); await db.SaveChangesAsync();
        var lines = receipt.Details.ToArray();
        var path = $"/api/importreceipts/{receipt.Id}/qc-disposition";
        await QcGrant(db, manager.RoleId, "quality_inspection.complete", false);
        await using var factory = Factory(database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using (var partial = await Send(client, HttpMethod.Post, path, manager.Id, "Admin", "qc-partial", QcBody(lines[0]))) Assert.Equal(HttpStatusCode.OK, partial.StatusCode);
        using (var denied = await Send(client, HttpMethod.Post, path, manager.Id, "Admin", "qc-final-denied", QcBody(lines[1])))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var json = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
            Assert.Contains("Bạn không có quyền", json.RootElement.GetProperty("message").GetString());
        }
        db.ChangeTracker.Clear();
        var current = await db.ImportReceipts.Include(r => r.Details).SingleAsync(r => r.Id == receipt.Id);
        Assert.Equal(ReceiptStatus.QcPending, current.Status);
        Assert.Equal(ReceiptLineQcState.QcPending, current.Details.Single(l => l.Id == lines[1].Id).QcState);
        Assert.Equal(0, current.Details.Single(l => l.Id == lines[1].Id).AcceptedQuantity);
        Assert.Equal(1, await db.IdempotencyRecords.CountAsync());
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "ImportReceipt.QcDispositionRecorded" && a.Result == "Success"));
        await QcGrant(db, manager.RoleId, "quality_inspection.complete", true);
        await QcGrant(db, manager.RoleId, "quality_inspection.execute", false);
        using (var completeOnly = await Send(client, HttpMethod.Post, path, manager.Id, "Admin", "qc-complete-only", QcBody(lines[1]))) Assert.Equal(HttpStatusCode.Forbidden, completeOnly.StatusCode);
        await QcGrant(db, manager.RoleId, "quality_inspection.execute", true);
        using (var final = await Send(client, HttpMethod.Post, path, manager.Id, "Admin", "qc-final", QcBody(lines[1]))) Assert.Equal(HttpStatusCode.OK, final.StatusCode);
        using (var replay = await Send(client, HttpMethod.Post, path, manager.Id, "Admin", "qc-final", QcBody(lines[1]))) Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using (var mismatch = await Send(client, HttpMethod.Post, path, manager.Id, "Admin", "qc-final", QcBody(lines[0]))) Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        await QcGrant(db, manager.RoleId, "quality_inspection.complete", false);
        using (var replay = await Send(client, HttpMethod.Post, path, manager.Id, "Admin", "qc-final", QcBody(lines[1]))) Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        using (var partialReplay = await Send(client, HttpMethod.Post, path, manager.Id, "Admin", "qc-partial", QcBody(lines[0]))) Assert.Equal(HttpStatusCode.OK, partialReplay.StatusCode);
        Assert.Equal(2, await db.IdempotencyRecords.CountAsync());
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.Action == "ImportReceipt.QcDispositionRecorded" && a.Result == "Success"));
        Assert.Equal(ReceiptStatus.QcCompleted, await db.ImportReceipts.AsNoTracking().Where(r => r.Id == receipt.Id).Select(r => r.Status).SingleAsync());
        Assert.Equal(100m, await db.InventoryStocks.SumAsync(s => s.Quantity)); Assert.Empty(await db.InventoryTransactions.ToListAsync());
    }

    [ApprovalSqlServerFact]
    public async Task Qc_approval_receipt_and_mixed_center_require_complete_and_disposition_approve_without_execute()
    {
        await using var database = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await database.MigrateAndSeedAsync();
        await using var db = Context(database.ConnectionString);
        var manager = await db.Users.SingleAsync(u => u.Role.RoleName == "Manager");
        var admin = await db.Users.SingleAsync(u => u.Role.RoleName == "Admin");
        var warehouse = await db.Warehouses.SingleAsync();
        var product = await db.Products.Include(p => p.Unit).SingleAsync();
        db.UserWarehouses.Add(new UserWarehouse { UserId = manager.Id, WarehouseId = warehouse.Id, CreatedBy = admin.Id });
        var qc = QcReceipt("HTTP-QC-APPROVE", warehouse.Id, admin.Id, product, 1);
        qc.Status = ReceiptStatus.QcCompleted; qc.Details.Single().QcState = ReceiptLineQcState.QcCompleted;
        var noQc = Receipt("HTTP-NO-QC-APPROVE", warehouse.Id, admin.Id, product, ReceiptStatus.Received, 0);
        var own = QcReceipt("HTTP-QC-MAKER", warehouse.Id, manager.Id, product, 1); own.Status = ReceiptStatus.QcCompleted;
        db.AddRange(qc, noQc, own); await db.SaveChangesAsync();
        await QcGrant(db, manager.RoleId, "quality_inspection.execute", false);
        await QcGrant(db, manager.RoleId, "quality_inspection.complete", false);
        await QcGrant(db, manager.RoleId, "quality_disposition.approve", false);
        await using var factory = Factory(database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var path = $"/api/importreceipts/{qc.Id}/approve";
        using (var approved = await Send(client, HttpMethod.Post, $"/api/importreceipts/{noQc.Id}/approve", manager.Id, "Admin", "no-qc")) Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        using (var denied = await Send(client, HttpMethod.Post, path, manager.Id, "Admin", "qc-approve")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var queue = await Send(client, HttpMethod.Get, "/api/approvals/queue?documentType=ImportReceipt", manager.Id, "Admin"))
        {
            Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
            using var json = JsonDocument.Parse(await queue.Content.ReadAsStringAsync());
            var item = json.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("documentId").GetInt32() == qc.Id);
            Assert.Equal("QcCompleted", item.GetProperty("pendingState").GetString());
            Assert.False(item.GetProperty("canApprove").GetBoolean()); Assert.False(item.GetProperty("canReject").GetBoolean());
        }
        await QcGrant(db, manager.RoleId, "quality_disposition.approve", true);
        await QcGrant(db, manager.RoleId, "receipt.complete", false);
        using (var denied = await Send(client, HttpMethod.Post, path, manager.Id, "Admin", "qc-approve")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await QcGrant(db, manager.RoleId, "receipt.complete", true);
        using (var detail = await Send(client, HttpMethod.Get, $"/api/approvals/ImportReceipt/{qc.Id}", manager.Id, "Admin"))
        {
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            using var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
            Assert.True(json.RootElement.GetProperty("summary").GetProperty("canApprove").GetBoolean());
        }
        using (var maker = await Send(client, HttpMethod.Post, $"/api/importreceipts/{own.Id}/approve", manager.Id, "Admin", "qc-maker")) Assert.Equal(HttpStatusCode.Forbidden, maker.StatusCode);
        var foreign = new Warehouse { Code = "HTTP-QC-FOREIGN", Name = "Kho ngoài phạm vi" }; db.Add(foreign); await db.SaveChangesAsync();
        var other = QcReceipt("HTTP-QC-PRIVATE", foreign.Id, admin.Id, product, 1); other.Status = ReceiptStatus.QcCompleted; db.Add(other); await db.SaveChangesAsync();
        foreach (var route in new[] { $"/api/importreceipts/{other.Id}/approve", $"/api/approvals/ImportReceipt/{other.Id}" })
            using (var denied = await Send(client, route.Contains("approvals") ? HttpMethod.Get : HttpMethod.Post, route, manager.Id, "Admin", "qc-foreign")) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using (var approved = await Send(client, HttpMethod.Post, path, manager.Id, "Admin", "qc-approve")) Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        using (var replay = await Send(client, HttpMethod.Post, path, manager.Id, "Admin", "qc-approve")) Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        await QcGrant(db, manager.RoleId, "quality_disposition.approve", false);
        using (var replay = await Send(client, HttpMethod.Post, path, manager.Id, "Admin", "qc-approve")) Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        Assert.Equal(2, await db.IdempotencyRecords.CountAsync());
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.Action == "ImportReceipt.Approved" && a.Result == "Success"));
        Assert.Equal(100m, await db.InventoryStocks.SumAsync(s => s.Quantity)); Assert.Empty(await db.InventoryTransactions.ToListAsync());
    }

    [ApprovalSqlServerFact]
    public async Task Concurrent_Qc_commands_cannot_complete_with_execute_only_and_final_completion_has_one_winner()
    {
        await using var database = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await database.MigrateAndSeedAsync();
        await using var db = Context(database.ConnectionString);
        var admin = await db.Users.SingleAsync(u => u.Role.RoleName == "Admin");
        var warehouse = await db.Warehouses.SingleAsync(); var product = await db.Products.Include(p => p.Unit).SingleAsync();
        var receipt = QcReceipt("HTTP-QC-RACE", warehouse.Id, admin.Id, product);
        db.Add(receipt); await db.SaveChangesAsync(); var lines = receipt.Details.ToArray();
        await QcGrant(db, admin.RoleId, "quality_inspection.complete", false);
        await using var factory = Factory(database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        async Task<HttpResponseMessage[]> Race(string bodyA, string bodyB, string prefix)
        {
            await using var blocker = new SqlConnection(database.ConnectionString); await blocker.OpenAsync();
            await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
            await using var command = blocker.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT Id FROM ImportReceipts WITH (UPDLOCK,HOLDLOCK) WHERE Id=@id";
            command.Parameters.AddWithValue("@id", receipt.Id); await command.ExecuteScalarAsync();
            var a = Send(client, HttpMethod.Post, $"/api/importreceipts/{receipt.Id}/qc-disposition", admin.Id, "Admin", prefix + "-a", bodyA);
            var b = Send(client, HttpMethod.Post, $"/api/importreceipts/{receipt.Id}/qc-disposition", admin.Id, "Admin", prefix + "-b", bodyB);
            await Task.Delay(300); Assert.False(a.IsCompleted); Assert.False(b.IsCompleted);
            await transaction.CommitAsync(); return await Task.WhenAll(a, b);
        }
        var partialRace = await Race(QcBody(lines[0]), QcBody(lines[1]), "partial-race");
        Assert.Single(partialRace, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(partialRace.Where(r => !r.IsSuccessStatusCode), r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Conflict, HttpStatusCode.Forbidden }));
        foreach (var r in partialRace) r.Dispose();
        db.ChangeTracker.Clear();
        Assert.Equal(ReceiptStatus.QcPending, await db.ImportReceipts.Where(r => r.Id == receipt.Id).Select(r => r.Status).SingleAsync());
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "ImportReceipt.QcDispositionRecorded" && a.Result == "Success"));
        await QcGrant(db, admin.RoleId, "quality_inspection.complete", true);
        var pending = await db.ImportReceiptDetails.SingleAsync(l => l.ImportReceiptId == receipt.Id && l.QcState == ReceiptLineQcState.QcPending);
        var finalRace = await Race(QcBody(pending), QcBody(pending), "final-race");
        Assert.Single(finalRace, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(finalRace, r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var r in finalRace) r.Dispose();
        Assert.Equal(ReceiptStatus.QcCompleted, await db.ImportReceipts.AsNoTracking().Where(r => r.Id == receipt.Id).Select(r => r.Status).SingleAsync());
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.Action == "ImportReceipt.QcDispositionRecorded" && a.Result == "Success"));
        Assert.Equal(2, await db.IdempotencyRecords.CountAsync());
        Assert.Equal(100m, await db.InventoryStocks.SumAsync(s => s.Quantity)); Assert.Empty(await db.InventoryTransactions.ToListAsync());
    }
}
