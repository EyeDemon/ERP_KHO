using ERP.QaSeed;
using FluentAssertions;

namespace ERP.Application.Tests;

public sealed class QaSeedPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void EnsureDevelopment_RejectsEveryNonDevelopmentEnvironment(string? environment)
    {
        var action = () => QaSeedPolicy.EnsureDevelopment(environment);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureDevelopment_AllowsDevelopment()
    {
        var action = () => QaSeedPolicy.EnsureDevelopment("Development");
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData("short")]
    [InlineData("alllowercase123!")]
    [InlineData("ALLUPPERCASE123!")]
    [InlineData("NoDigitsHere!")]
    [InlineData("NoSpecial12345")]
    [InlineData("qa_admin_local-A1!")]
    public void ValidatePassword_RejectsWeakOrUsernameDerivedPasswords(string password)
    {
        var action = () => QaSeedPolicy.ValidatePassword("qa_admin_local", password);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidatePassword_AcceptsStrongUnrelatedPassword()
    {
        var action = () => QaSeedPolicy.ValidatePassword("qa_admin_local", "Strong-Local-Only-42!");
        action.Should().NotThrow();
    }

    [Fact]
    public void ValidateLocalConnection_AcceptsWindowsAuthenticatedErpKhoLocalDb()
    {
        var action = () => QaSeedPolicy.ValidateLocalConnection(
            @"Server=(localdb)\mssqllocaldb;Database=ERP_KHO;Trusted_Connection=True");

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(@"Server=remote-sql;Database=ERP_KHO;Trusted_Connection=True")]
    [InlineData(@"Server=(localdb)\mssqllocaldb;Database=OtherDb;Trusted_Connection=True")]
    [InlineData(@"Server=(localdb)\mssqllocaldb;Database=ERP_KHO;User ID=qa;Password=not-a-real-secret")]
    public void ValidateLocalConnection_RejectsRemoteWrongDatabaseOrSqlCredentials(string connectionString)
    {
        var action = () => QaSeedPolicy.ValidateLocalConnection(connectionString);

        action.Should().Throw<InvalidOperationException>();
    }
}
