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
    public void AllocationAndPickingCommandsUseSerializableIdempotencyTransaction()
    {
        IdempotentCommandFilter.RequiresSerializableIsolation("InventoryAllocation.Create").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("InventoryAllocation.Reallocate").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("Picking.Assign").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("Picking.Pick").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("Picking.ShortPick.Resolve").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("Packing.Pack").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("Packing.Complete").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("HandlingUnit.Nest").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("Shipment.Stage").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("Shipment.LoadHu").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("Shipment.Dispatch").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("Shipment.ConfirmDelivery").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("Shipment.RetryDelivery").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("SalesOrder.Release").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("SalesOrder.Cancel").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("Backorder.Reallocate").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("Backorder.Cancel").Should().BeTrue();
        IdempotentCommandFilter.RequiresSerializableIsolation("Export.Dispatch").Should().BeFalse();
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
    [InlineData(typeof(ERP.Api.Controllers.PickingTasksController), "Assign")]
    [InlineData(typeof(ERP.Api.Controllers.PickingTasksController), "Start")]
    [InlineData(typeof(ERP.Api.Controllers.PickingTasksController), "Pick")]
    [InlineData(typeof(ERP.Api.Controllers.PickingTasksController), "ReportShortPick")]
    [InlineData(typeof(ERP.Api.Controllers.PickingTasksController), "ResolveShortPick")]
    [InlineData(typeof(ERP.Api.Controllers.PickingTasksController), "OverrideShortPick")]
    [InlineData(typeof(ERP.Api.Controllers.PickingTasksController), "Complete")]
    [InlineData(typeof(ERP.Api.Controllers.PackingSessionsController), "Create")]
    [InlineData(typeof(ERP.Api.Controllers.PackingSessionsController), "CreateHandlingUnit")]
    [InlineData(typeof(ERP.Api.Controllers.PackingSessionsController), "Pack")]
    [InlineData(typeof(ERP.Api.Controllers.PackingSessionsController), "CloseHandlingUnit")]
    [InlineData(typeof(ERP.Api.Controllers.PackingSessionsController), "CancelHandlingUnit")]
    [InlineData(typeof(ERP.Api.Controllers.PackingSessionsController), "NestHandlingUnit")]
    [InlineData(typeof(ERP.Api.Controllers.PackingSessionsController), "UnnestHandlingUnit")]
    [InlineData(typeof(ERP.Api.Controllers.PackingSessionsController), "Complete")]
    [InlineData(typeof(ERP.Api.Controllers.PackingSessionsController), "Close")]
    [InlineData(typeof(ERP.Api.Controllers.PackingSessionsController), "Cancel")]
    [InlineData(typeof(ERP.Api.Controllers.ShipmentsController), "Stage")]
    [InlineData(typeof(ERP.Api.Controllers.ShipmentsController), "StartLoading")]
    [InlineData(typeof(ERP.Api.Controllers.ShipmentsController), "LoadHandlingUnit")]
    [InlineData(typeof(ERP.Api.Controllers.ShipmentsController), "CompleteLoading")]
    [InlineData(typeof(ERP.Api.Controllers.ShipmentsController), "Dispatch")]
    [InlineData(typeof(ERP.Api.Controllers.ShipmentsController), "MarkInTransit")]
    [InlineData(typeof(ERP.Api.Controllers.ShipmentsController), "ConfirmDelivery")]
    [InlineData(typeof(ERP.Api.Controllers.ShipmentsController), "DeliveryFailed")]
    [InlineData(typeof(ERP.Api.Controllers.ShipmentsController), "RetryDelivery")]
    [InlineData(typeof(ERP.Api.Controllers.ShipmentsController), "ReturnInitiate")]
    [InlineData(typeof(ERP.Api.Controllers.ShipmentsController), "Complete")]
    [InlineData(typeof(ERP.Api.Controllers.SalesOrdersController), "Create")]
    [InlineData(typeof(ERP.Api.Controllers.SalesOrdersController), "Hold")]
    [InlineData(typeof(ERP.Api.Controllers.SalesOrdersController), "Release")]
    [InlineData(typeof(ERP.Api.Controllers.SalesOrdersController), "Cancel")]
    [InlineData(typeof(ERP.Api.Controllers.BackordersController), "Reallocate")]
    [InlineData(typeof(ERP.Api.Controllers.BackordersController), "Cancel")]
    public void InventoryMutationEndpoints_RequireIdempotency(Type controller, string method)
    {
        controller.GetMethod(method)!.GetCustomAttribute<IdempotentCommandAttribute>().Should().NotBeNull();
    }

}
