using System.Net;
using System.Text.Json;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Tests;

public sealed partial class InboundHttpIntegrationTests
{
    [ApprovalSqlServerFact]
    public async Task Qc_basis_read_preserves_saved_decision_after_master_changes_without_policy_grant_or_effects()
    {
        await using var database = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await database.MigrateAndSeedAsync();
        await using var db = Context(database.ConnectionString);
        var admin = await db.Users.SingleAsync(u => u.Role.RoleName == "Admin");
        var actor = await db.Users.SingleAsync(u => u.Role.RoleName == "Manager");
        var viewer = await db.Roles.SingleAsync(r => r.RoleName == "Viewer");
        actor.RoleId = viewer.Id;
        var warehouse = await db.Warehouses.SingleAsync();
        db.UserWarehouses.Add(new UserWarehouse { UserId = actor.Id, WarehouseId = warehouse.Id, CreatedBy = admin.Id });
        var product = await db.Products.Include(p => p.Unit).SingleAsync();
        var effective = new DateTime(2026, 9, 20, 16, 0, 0, DateTimeKind.Utc);
        var policy = new QcPolicy { ProductId = product.Id, Version = 3, RequiresQc = true, EffectiveFromUtc = effective, Rule = "PRIVATE_RULE_NOT_FOR_UI" };
        db.QcPolicies.Add(policy); await db.SaveChangesAsync();
        var receipt = QcReceipt("QC-BASIS-READ", warehouse.Id, admin.Id, product, 3);
        var lines = receipt.Details.ToArray();
        lines[0].QcPolicyId = policy.Id; lines[0].QcPolicyVersion = 3;
        lines[0].QcPolicySourceSnapshot = "Product"; lines[0].QcPolicyEffectiveAtUtc = effective;
        lines[0].QcRuleSnapshot = policy.Rule; lines[0].UnitPrice = 123.45m;
        lines[1].RequiresQc = false; lines[1].QcPolicySourceSnapshot = "None"; lines[1].QcState = ReceiptLineQcState.NoQcRequired;
        lines[2].RequiresQc = false; lines[2].QcState = ReceiptLineQcState.NoQcRequired;
        db.ImportReceipts.Add(receipt); await db.SaveChangesAsync();
        Assert.False(await db.RolePermissions.AnyAsync(g => g.RoleId == viewer.Id && g.Permission.Code.StartsWith("quality_policy.")));
        await using var factory = Factory(database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var path = $"/api/importreceipts/{receipt.Id}";
        async Task<string> Read()
        {
            using var response = await Send(client, HttpMethod.Get, path, actor.Id, "Admin");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }
        var before = await Read();
        policy.RequiresQc = false; policy.Version = 4; policy.EffectiveFromUtc = effective.AddDays(2); policy.Rule = "CHANGED_MASTER";
        await db.SaveChangesAsync();
        var after = await Read();
        Assert.Equal(before, after);
        using var json = JsonDocument.Parse(after);
        var details = json.RootElement.GetProperty("details").EnumerateArray().ToArray();
        var saved = details.Single(d => d.GetProperty("id").GetInt32() == lines[0].Id);
        Assert.True(saved.GetProperty("requiresQc").GetBoolean());
        Assert.Equal(3, saved.GetProperty("qcPolicyVersion").GetInt32());
        Assert.Equal("Product", saved.GetProperty("qcPolicySource").GetString());
        Assert.StartsWith("2026-09-20T16:00:00", saved.GetProperty("qcPolicyEffectiveAtUtc").GetString());
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("unitPrice").ValueKind);
        Assert.DoesNotContain("PRIVATE_RULE", after); Assert.DoesNotContain("CHANGED_MASTER", after);
        Assert.DoesNotContain("rowVersion", after); Assert.DoesNotContain("qcRuleSnapshot", after);
        Assert.False(details.Single(d => d.GetProperty("id").GetInt32() == lines[1].Id).GetProperty("requiresQc").GetBoolean());
        Assert.Equal(JsonValueKind.Null, details.Single(d => d.GetProperty("id").GetInt32() == lines[2].Id).GetProperty("qcPolicySource").ValueKind);
        await db.UserWarehouses.Where(w => w.UserId == actor.Id).ExecuteDeleteAsync();
        using (var foreign = await Send(client, HttpMethod.Get, path, actor.Id, "Admin")) Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        await QcGrant(db, viewer.Id, "receipt.read", false);
        using (var denied = await Send(client, HttpMethod.Get, path, actor.Id, "Admin")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Empty(await db.IdempotencyRecords.ToListAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
        Assert.Empty(await db.InventoryTransactions.ToListAsync());
        Assert.Equal(100m, await db.InventoryStocks.SumAsync(s => s.Quantity));
        Assert.Equal(ReceiptStatus.QcPending, await db.ImportReceipts.Where(r => r.Id == receipt.Id).Select(r => r.Status).SingleAsync());
        Assert.Equal(effective, await db.ImportReceiptDetails.Where(d => d.Id == lines[0].Id).Select(d => d.QcPolicyEffectiveAtUtc).SingleAsync());
    }
}
