using System.Reflection;
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
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(
            snapshotModel.GetRelationalModel(),
            currentModel.GetRelationalModel());

        operations.Should().BeEmpty(string.Join(
            Environment.NewLine,
            operations.Select(Describe)));
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
