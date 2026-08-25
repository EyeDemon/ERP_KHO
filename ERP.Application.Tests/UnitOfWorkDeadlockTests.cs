using System.Reflection;
using ERP.Domain.Entities;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ERP.Application.Tests;

public sealed class UnitOfWorkDeadlockTests
{
    [Fact]
    public async Task CommitTransaction_TranslatesSqlServer1205ForWholeTransactionRetry()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new DeadlockInterceptor())
            .Options;
        await using var context = new ErpKhoDbContext(options);
        context.Roles.Add(new Role { RoleName = "deadlock-test" });

        var action = () => new UnitOfWork(context).CommitTransactionAsync();

        await action.Should().ThrowAsync<DeadlockException>();
    }

    private sealed class DeadlockInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<int>>(
                new DbUpdateException("deadlock", CreateSqlException(1205)));
    }

    private static SqlException CreateSqlException(int number)
    {
        var constructor = typeof(SqlError).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .OrderByDescending(x => x.GetParameters().Length).First();
        var args = constructor.GetParameters().Select(parameter =>
        {
            if (parameter.Name?.Contains("number", StringComparison.OrdinalIgnoreCase) == true) return (object)number;
            if (parameter.ParameterType == typeof(string)) return "deadlock-test";
            if (parameter.ParameterType == typeof(byte)) return (byte)0;
            if (parameter.ParameterType == typeof(uint)) return (uint)0;
            if (parameter.ParameterType == typeof(int)) return 0;
            if (parameter.ParameterType == typeof(bool)) return false;
            return parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
        }).ToArray();
        var error = (SqlError)constructor.Invoke(args);
        var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(collection, [error]);
        return (SqlException)typeof(SqlException)
            .GetMethod("CreateException", BindingFlags.Static | BindingFlags.NonPublic, null, [typeof(SqlErrorCollection), typeof(string)], null)!
            .Invoke(null, [collection, "16.0.0"])!;
    }
}
