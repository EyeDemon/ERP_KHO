using System.Reflection;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ERP.Application.Tests;

public sealed class MigrationSnapshotParityTests
{
    [Fact]
    public void Migration_snapshot_matches_current_model()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=True;Encrypt=False")
            .Options;
        using var context = new ErpKhoDbContext(options);

        var migrationsAssembly = context.GetService<IMigrationsAssembly>();
        var snapshotModel = migrationsAssembly.ModelSnapshot?.Model
            ?? throw new InvalidOperationException("Migration snapshot is missing.");
        if (snapshotModel is IMutableModel mutableModel)
            snapshotModel = mutableModel.FinalizeModel();
        snapshotModel = context.GetService<IModelRuntimeInitializer>().Initialize(snapshotModel);

        var currentModel = context.GetService<IDesignTimeModel>().Model;
        var differ = context.GetService<IMigrationsModelDiffer>();
        var differences = differ.GetDifferences(
                snapshotModel.GetRelationalModel(),
                currentModel.GetRelationalModel())
            .ToList();

        Assert.Empty(differences);
        differences.Should().BeEmpty(string.Join(
            Environment.NewLine,
            differences.Select(Describe)));
    }

    [Fact]
    public void Wave45_MarketplaceShop_CalculateChannelPushQty_EnforcesSafetyStockAndLowStockCutOff()
    {
        var primaryShop = new MarketplaceShop
        {
            ChannelCode = "SHOPEE",
            ReserveRatioPct = 90m,
            SafetyBufferQty = 2m,
            LowStockThreshold = 5m,
            IsPrimaryShop = true
        };
        var secondaryShop = new MarketplaceShop
        {
            ChannelCode = "TIKTOK",
            ReserveRatioPct = 80m,
            SafetyBufferQty = 2m,
            LowStockThreshold = 5m,
            IsPrimaryShop = false
        };

        primaryShop.CalculateChannelPushQty(5m).Should().Be(2m); // floor((5 - 2) * 0.9) = 2
        secondaryShop.CalculateChannelPushQty(5m).Should().Be(0m); // cut-off when ATP <= LowStockThreshold and not primary
        secondaryShop.CalculateChannelPushQty(22m, 2m).Should().Be(8m); // floor(((22 - 2) * 0.8) / 2) = 8
    }

    [Fact]
    public void Wave45_TenantSubscription_EvaluateQuotaTier_Enforces3TierQuotaPolicy()
    {
        var sub = new TenantSubscription
        {
            MonthlyOrderQuota = 100,
            CurrentMonthOrderCount = 70
        };
        sub.EvaluateQuotaTier().Should().Be("NORMAL");

        sub.CurrentMonthOrderCount = 85;
        sub.EvaluateQuotaTier().Should().Be("WARNING_80_100");

        sub.CurrentMonthOrderCount = 110;
        sub.EvaluateQuotaTier().Should().Be("SOFT_BURST_100_115");

        sub.CurrentMonthOrderCount = 116;
        sub.EvaluateQuotaTier().Should().Be("READ_ONLY_OVER_115");
    }

    private static string Describe(object operation)
    {
        var values = operation.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Select(property =>
            {
                object? value;
                try { value = property.GetValue(operation); }
                catch { return null; }
                return value switch
                {
                    null => null,
                    string text => $"{property.Name}={text}",
                    int or long or bool or decimal => $"{property.Name}={value}",
                    Type type => $"{property.Name}={type.Name}",
                    _ => null,
                };
            })
            .Where(value => value is not null);
        return $"{operation.GetType().Name}: {string.Join(", ", values!)}";
    }
}
