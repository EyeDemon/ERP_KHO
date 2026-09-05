using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace ERP.Api.Tests;

public sealed class ApprovalsControllerTests
{
    [Fact]
    public async Task Reject_ForwardsDistinctDocumentCommandAndReason()
    {
        var service = new Mock<IApprovalWorkflowService>();
        service.Setup(x => x.RejectAsync("ImportReceipt", 7, "Sai số lượng", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApprovalActionResult { DocumentType = "ImportReceipt", DocumentId = 7, DocumentCode = "IMP-7", CorrelationId = "corr" });
        var result = await new ApprovalsController(service.Object).Reject("ImportReceipt", 7, new ApprovalRejectRequest { Reason = "Sai số lượng" }, default);
        result.Should().BeOfType<OkObjectResult>();
        service.VerifyAll();
    }

    [Fact]
    public void Controller_RequiresCheckerPolicy_AndRejectUsesPersistentIdempotency()
    {
        typeof(ApprovalsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()
            .Should().Contain(x => x.Policy == ApprovalPolicies.Checker);
        typeof(ApprovalsController).GetMethod(nameof(ApprovalsController.Reject))!.GetCustomAttributes(typeof(IdempotentCommandAttribute), true)
            .Should().ContainSingle();
    }
}
