using ERP.Application.Common;
using ERP.Application.Options;
using FluentAssertions;

namespace ERP.Application.Tests;

public sealed class ApprovalAgingCalculatorTests
{
    private static readonly ApprovalAgingOptions Options = new();
    private static readonly DateTime Now = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(23, 59, ApprovalAgingCalculator.Normal)]
    [InlineData(24, 0, ApprovalAgingCalculator.Warning)]
    [InlineData(48, 0, ApprovalAgingCalculator.Overdue)]
    [InlineData(49, 0, ApprovalAgingCalculator.Overdue)]
    public void Calculate_UsesConfiguredUtcBoundaries(int hours, int minutes, string expected)
    {
        var result = ApprovalAgingCalculator.Calculate(Now.AddHours(-hours).AddMinutes(-minutes), Now, true, Options);

        result.SlaStatus.Should().Be(expected);
        result.WaitingMinutes.Should().Be((hours * 60L) + minutes);
    }

    [Fact]
    public void Calculate_UsesUtcForUnspecifiedSqlValuesAndClampsFutureRequests()
    {
        var sqlValue = DateTime.SpecifyKind(Now.AddHours(-24), DateTimeKind.Unspecified);
        ApprovalAgingCalculator.Calculate(sqlValue, Now, true, Options).SlaStatus.Should().Be(ApprovalAgingCalculator.Warning);
        ApprovalAgingCalculator.Calculate(Now.AddMinutes(1), Now, true, Options).WaitingMinutes.Should().Be(0);
    }

    [Fact]
    public void Calculate_ReturnsNoAgingForClosedApproval()
    {
        var result = ApprovalAgingCalculator.Calculate(Now.AddDays(-10), Now, false, Options);

        result.WaitingMinutes.Should().BeNull();
        result.SlaStatus.Should().BeNull();
    }

    [Fact]
    public void Options_RejectInvalidThresholdOrder()
    {
        var options = new ApprovalAgingOptions { WarningAfterHours = 48, OverdueAfterHours = 24 };
        FluentActions.Invoking(options.Validate).Should().Throw<InvalidOperationException>();
    }
}
