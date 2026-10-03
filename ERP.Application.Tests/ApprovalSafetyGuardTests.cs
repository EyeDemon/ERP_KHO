using ERP.Application.Exceptions;
using ERP.Application.Security;
using FluentAssertions;

namespace ERP.Application.Tests;

public sealed class ApprovalSafetyGuardTests
{
    [Fact]
    public void SameMakerAndChecker_IsRejectedEvenForAnOtherwisePrivilegedActor()
    {
        var action = () => ApprovalSafetyGuard.EnsureDifferentChecker(42, 42);

        action.Should().Throw<ForbiddenException>()
            .WithMessage("*không được tự duyệt*");
    }

    [Fact]
    public void DifferentMakerAndChecker_IsAllowed()
    {
        var action = () => ApprovalSafetyGuard.EnsureDifferentChecker(41, 42);

        action.Should().NotThrow();
    }
}
