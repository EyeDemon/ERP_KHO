using ERP.Application.Exceptions;
using ERP.Application.Security;
using FluentAssertions;

namespace ERP.Application.Tests;

public sealed class ApprovalRejectReasonPolicyTests
{
    [Fact]
    public void Normalize_TrimsSafeReason() => ApprovalRejectReasonPolicy.Normalize("  Sai số lượng  ").Should().Be("Sai số lượng");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("Lỗi\r\nforged")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<b>Không đạt</b>")]
    public void Normalize_RejectsUnsafeReason(string? value) =>
        FluentActions.Invoking(() => ApprovalRejectReasonPolicy.Normalize(value)).Should().Throw<BusinessRuleException>();

    [Fact]
    public void Normalize_RejectsMoreThanFiveHundredCharacters() =>
        FluentActions.Invoking(() => ApprovalRejectReasonPolicy.Normalize(new string('x', 501))).Should().Throw<BusinessRuleException>();
}
