using ERP.Api.Infrastructure;
using FluentAssertions;
using System.Reflection;

namespace ERP.Api.Tests;

public sealed class ApprovalRequestSafetyTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("safe-correlation_01", true)]
    [InlineData("line\nbreak", false)]
    public void CorrelationIdValidation_RejectsMissingAndControlCharacters(string? value, bool expected) =>
        CorrelationIdMiddleware.IsValid(value).Should().Be(expected);

    [Fact]
    public void DockYardOccupancyCommandsUseSerializableIdempotencyTransaction()
    {
        IdempotentCommandFilter.RequiresSerializableIsolation("DockYard.Appointment.CheckIn").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("DockYard.Appointment.AssignDock").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("DockYard.Appointment.Confirm").Should().BeFalse();
        IdempotentCommandFilter.RequiresSerializableIsolation("StockTransfer.Approve").Should().BeFalse();
    }

    [Fact]
    public void Fingerprint_IsCanonicalAcrossArgumentInsertionOrder()
    {
        var first = new Dictionary<string, object?> { ["id"] = 7, ["request"] = new { Quantity = 2m } };
        var second = new Dictionary<string, object?> { ["request"] = new { Quantity = 2m }, ["id"] = 7 };

        IdempotentCommandFilter.Fingerprint("Export.Dispatch", first)
            .Should().Be(IdempotentCommandFilter.Fingerprint("Export.Dispatch", second));
    }

    [Fact]
    public void Fingerprint_IgnoresCancellationToken_ButKeepsBusinessArguments()
    {
        using var source = new CancellationTokenSource();
        var first = IdempotentCommandFilter.Fingerprint("StockTransfer.Approve", new Dictionary<string, object?> { ["id"] = 7, ["cancellationToken"] = source.Token });
        var replay = IdempotentCommandFilter.Fingerprint("StockTransfer.Approve", new Dictionary<string, object?> { ["id"] = 7, ["cancellationToken"] = CancellationToken.None });
        var other = IdempotentCommandFilter.Fingerprint("StockTransfer.Approve", new Dictionary<string, object?> { ["id"] = 8, ["cancellationToken"] = CancellationToken.None });

        first.Should().Be(replay);
        first.Should().NotBe(other);
    }

    [Fact]
    public void Hash_DoesNotPersistRawIdempotencyKey()
    {
        const string raw = "raw-private-key";
        var hash = IdempotentCommandFilter.Hash(raw);

        hash.Should().HaveLength(64).And.NotContain(raw);
    }

    [Theory]
    [InlineData(typeof(ERP.Api.Controllers.ImportReceiptsController), "Create")]
    [InlineData(typeof(ERP.Api.Controllers.ImportReceiptsController), "Cancel")]
    [InlineData(typeof(ERP.Api.Controllers.ImportReceiptsController), "Approve")]
    [InlineData(typeof(ERP.Api.Controllers.ExportReceiptsController), "Create")]
    [InlineData(typeof(ERP.Api.Controllers.ExportReceiptsController), "Cancel")]
    [InlineData(typeof(ERP.Api.Controllers.ExportReceiptsController), "ApproveAndReserve")]
    [InlineData(typeof(ERP.Api.Controllers.ExportReceiptsController), "ApproveAndDispatch")]
    [InlineData(typeof(ERP.Api.Controllers.ExportReceiptsController), "Dispatch")]
    [InlineData(typeof(ERP.Api.Controllers.StocktakesController), "Approve")]
    [InlineData(typeof(ERP.Api.Controllers.StocktakesController), "Create")]
    [InlineData(typeof(ERP.Api.Controllers.StocktakesController), "UpdateDetail")]
    [InlineData(typeof(ERP.Api.Controllers.StockTransfersController), "Create")]
    [InlineData(typeof(ERP.Api.Controllers.StockTransfersController), "Update")]
    [InlineData(typeof(ERP.Api.Controllers.StockTransfersController), "Approve")]
    [InlineData(typeof(ERP.Api.Controllers.StockTransfersController), "Dispatch")]
    [InlineData(typeof(ERP.Api.Controllers.StockTransfersController), "Receive")]
    [InlineData(typeof(ERP.Api.Controllers.StockTransfersController), "Complete")]
    [InlineData(typeof(ERP.Api.Controllers.DockYardController), "CreateDock")]
    [InlineData(typeof(ERP.Api.Controllers.DockYardController), "CreateYardSlot")]
    [InlineData(typeof(ERP.Api.Controllers.DockYardController), "CreateAppointment")]
    [InlineData(typeof(ERP.Api.Controllers.DockYardController), "Confirm")]
    [InlineData(typeof(ERP.Api.Controllers.DockYardController), "Arrive")]
    [InlineData(typeof(ERP.Api.Controllers.DockYardController), "CheckIn")]
    [InlineData(typeof(ERP.Api.Controllers.DockYardController), "AssignDock")]
    [InlineData(typeof(ERP.Api.Controllers.DockYardController), "StartService")]
    [InlineData(typeof(ERP.Api.Controllers.DockYardController), "Complete")]
    [InlineData(typeof(ERP.Api.Controllers.DockYardController), "Checkout")]
    [InlineData(typeof(ERP.Api.Controllers.DockYardController), "Cancel")]
    [InlineData(typeof(ERP.Api.Controllers.DockYardController), "MarkNoShow")]
    [InlineData(typeof(ERP.Api.Controllers.DockYardController), "MarkException")]
    public void InventoryMutationEndpoints_RequireIdempotency(Type controller, string method)
    {
        controller.GetMethod(method)!.GetCustomAttribute<IdempotentCommandAttribute>().Should().NotBeNull();
    }

}
