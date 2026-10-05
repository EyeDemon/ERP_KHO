using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ERP.Application.Tests;

public sealed class DockYardServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ErpKhoDbContext _db;
    private readonly Mock<IWarehouseAuthorizationService> _warehouseAuthorization = new();
    private readonly TestCurrentUser _currentUser = new();
    private readonly DockYardService _service;
    private readonly Warehouse _warehouse;
    private readonly User _user;

    public DockYardServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new ErpKhoDbContext(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        var role = new Role { RoleName = "Manager" };
        _user = new User
        {
            Username = "dock_test",
            PasswordHash = "TEST",
            FullName = "Dock Test",
            Role = role
        };
        _warehouse = new Warehouse { Code = "WH-DOCK", Name = "Kho Dock" };
        _db.AddRange(role, _user, _warehouse);
        _db.SaveChanges();

        _currentUser.UserId = _user.Id;
        _warehouseAuthorization
            .Setup(x => x.EnsureWarehouseAccessAsync(_warehouse.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _warehouseAuthorization
            .Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_warehouse.Id]);

        var calendar = new WarehouseCalendar
        {
            WarehouseId = _warehouse.Id,
            TimeZoneId = "Asia/Ho_Chi_Minh",
            RowVersion = Version(1)
        };
        for (var day = 0; day < 7; day++)
        {
            calendar.Days.Add(new WarehouseCalendarDay
            {
                DayOfWeek = day,
                IsOpen = true,
                OpensAtLocal = TimeSpan.FromHours(6),
                ClosesAtLocal = TimeSpan.FromHours(22),
                InboundCutoffLocal = TimeSpan.FromHours(20),
                OutboundCutoffLocal = TimeSpan.FromHours(19.5)
            });
        }
        _db.WarehouseCalendars.Add(calendar);
        _db.SaveChanges();

        _service = new DockYardService(_db, _warehouseAuthorization.Object, _currentUser, TimeProvider.System);
    }

    [Fact]
    public async Task Confirm_rejects_appointment_after_operational_cutoff()
    {
        var appointment = Appointment(
            DockAppointmentStatus.Draft,
            new DateTime(2026, 10, 5, 14, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 5, 14, 30, 0, DateTimeKind.Utc),
            2);
        _db.DockAppointments.Add(appointment);
        await _db.SaveChangesAsync();

        var action = () => _service.ConfirmAsync(appointment.Id, new DockAppointmentCommandDto
        {
            RowVersion = Convert.ToBase64String(Version(2))
        });

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*cutoff*");
        (await _db.DockAppointments.AsNoTracking().SingleAsync(x => x.Id == appointment.Id))
            .Status.Should().Be(DockAppointmentStatus.Draft);
    }

    [Fact]
    public async Task Check_in_rejects_vehicle_mismatch_without_mutating_state()
    {
        var appointment = Appointment(
            DockAppointmentStatus.Arrived,
            new DateTime(2026, 10, 5, 2, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 5, 3, 0, 0, DateTimeKind.Utc),
            3);
        appointment.VehiclePlate = "51C-123.45";
        appointment.ArrivedAtUtc = DateTime.UtcNow;
        _db.DockAppointments.Add(appointment);
        await _db.SaveChangesAsync();

        var action = () => _service.CheckInAsync(appointment.Id, new DockAppointmentCheckInDto
        {
            RowVersion = Convert.ToBase64String(Version(3)),
            VehiclePlate = "51C-999.99",
            DriverName = "Tài xế A"
        });

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*không khớp*");
        var stored = await _db.DockAppointments.AsNoTracking().SingleAsync(x => x.Id == appointment.Id);
        stored.Status.Should().Be(DockAppointmentStatus.Arrived);
        stored.CheckedInAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Assign_dock_rejects_overlapping_active_appointment()
    {
        var dock = new Dock
        {
            WarehouseId = _warehouse.Id,
            Code = "D-01",
            Name = "Dock 01",
            SupportsInbound = true,
            SupportsOutbound = true,
            IsActive = true,
            RowVersion = Version(4)
        };
        _db.Docks.Add(dock);
        await _db.SaveChangesAsync();

        var start = new DateTime(2026, 10, 5, 2, 0, 0, DateTimeKind.Utc);
        var occupied = Appointment(DockAppointmentStatus.DockAssigned, start, start.AddHours(1), 5);
        occupied.DockId = dock.Id;
        occupied.DockAssignedAtUtc = DateTime.UtcNow;

        var waiting = Appointment(DockAppointmentStatus.CheckedIn, start.AddMinutes(30), start.AddHours(2), 6);
        waiting.CheckedInAtUtc = DateTime.UtcNow;
        _db.DockAppointments.AddRange(occupied, waiting);
        await _db.SaveChangesAsync();

        var action = () => _service.AssignDockAsync(waiting.Id, new DockAppointmentAssignDockDto
        {
            DockId = dock.Id,
            RowVersion = Convert.ToBase64String(Version(6))
        });

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*appointment khác*");
        (await _db.DockAppointments.AsNoTracking().SingleAsync(x => x.Id == waiting.Id))
            .DockId.Should().BeNull();
    }

    [Fact]
    public async Task Assign_dock_keeps_current_dock_occupied_after_planned_window_until_released()
    {
        var dock = new Dock
        {
            WarehouseId = _warehouse.Id,
            Code = "D-02",
            Name = "Dock 02",
            SupportsInbound = true,
            SupportsOutbound = true,
            IsActive = true,
            RowVersion = Version(8)
        };
        _db.Docks.Add(dock);
        await _db.SaveChangesAsync();

        var occupied = Appointment(
            DockAppointmentStatus.DockAssigned,
            new DateTime(2026, 10, 5, 1, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 5, 2, 0, 0, DateTimeKind.Utc),
            9);
        occupied.DockId = dock.Id;
        occupied.DockAssignedAtUtc = DateTime.UtcNow;

        var waiting = Appointment(
            DockAppointmentStatus.CheckedIn,
            new DateTime(2026, 10, 5, 3, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 5, 4, 0, 0, DateTimeKind.Utc),
            10);
        waiting.CheckedInAtUtc = DateTime.UtcNow;
        _db.DockAppointments.AddRange(occupied, waiting);
        await _db.SaveChangesAsync();

        var action = () => _service.AssignDockAsync(waiting.Id, new DockAppointmentAssignDockDto
        {
            DockId = dock.Id,
            RowVersion = Convert.ToBase64String(Version(10))
        });

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*appointment khác*");
        (await _db.DockAppointments.AsNoTracking().SingleAsync(x => x.Id == waiting.Id))
            .DockId.Should().BeNull();
    }

    [Fact]
    public async Task Exception_does_not_release_physical_yard_or_dock_occupancy()
    {
        var dock = new Dock
        {
            WarehouseId = _warehouse.Id,
            Code = "D-EX",
            Name = "Dock Exception",
            SupportsInbound = true,
            SupportsOutbound = true,
            IsActive = true,
            RowVersion = Version(11)
        };
        var slot = new YardSlot
        {
            WarehouseId = _warehouse.Id,
            Code = "Y-EX",
            Name = "Yard Exception",
            IsActive = true,
            RowVersion = Version(12)
        };
        _db.AddRange(dock, slot);
        await _db.SaveChangesAsync();

        var blocked = Appointment(
            DockAppointmentStatus.Exception,
            new DateTime(2026, 10, 5, 1, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 5, 2, 0, 0, DateTimeKind.Utc),
            13);
        blocked.DockId = dock.Id;
        blocked.YardSlotId = slot.Id;
        blocked.CheckedInAtUtc = DateTime.UtcNow;
        blocked.ExceptionCode = "DOCK_UNAVAILABLE";

        var waiting = Appointment(
            DockAppointmentStatus.CheckedIn,
            new DateTime(2026, 10, 5, 3, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 5, 4, 0, 0, DateTimeKind.Utc),
            14);
        waiting.CheckedInAtUtc = DateTime.UtcNow;
        _db.DockAppointments.AddRange(blocked, waiting);
        await _db.SaveChangesAsync();

        var yard = await _service.GetYardSlotsAsync(_warehouse.Id);
        yard.Single(x => x.Id == slot.Id).Occupied.Should().BeTrue();

        var assign = () => _service.AssignDockAsync(waiting.Id, new DockAppointmentAssignDockDto
        {
            DockId = dock.Id,
            RowVersion = Convert.ToBase64String(Version(14))
        });
        await assign.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*appointment khác*");
    }

    [Fact]
    public async Task No_show_is_rejected_after_vehicle_has_arrived()
    {
        var appointment = Appointment(
            DockAppointmentStatus.Arrived,
            new DateTime(2026, 10, 5, 1, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 5, 2, 0, 0, DateTimeKind.Utc),
            15);
        appointment.ArrivedAtUtc = DateTime.UtcNow;
        _db.DockAppointments.Add(appointment);
        await _db.SaveChangesAsync();

        var action = () => _service.MarkNoShowAsync(appointment.Id, new DockAppointmentCommandDto
        {
            RowVersion = Convert.ToBase64String(Version(15))
        });

        await action.Should().ThrowAsync<ConcurrencyException>()
            .WithMessage("*chưa đến*");
    }

    [Fact]
    public async Task Happy_path_confirm_records_event_without_inventory_effect()
    {
        var beforeStocks = await _db.InventoryStocks.CountAsync();
        var appointment = Appointment(
            DockAppointmentStatus.Draft,
            new DateTime(2026, 10, 5, 1, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 5, 2, 0, 0, DateTimeKind.Utc),
            7);
        _db.DockAppointments.Add(appointment);
        await _db.SaveChangesAsync();

        var result = await _service.ConfirmAsync(appointment.Id, new DockAppointmentCommandDto
        {
            RowVersion = Convert.ToBase64String(Version(7)),
            Note = "Xác nhận lịch xe"
        });

        result.Status.Should().Be(DockAppointmentStatus.Confirmed);
        result.Events.Should().Contain(x => x.EventType == "Confirmed");
        (await _db.InventoryStocks.CountAsync()).Should().Be(beforeStocks);
    }

    private DockAppointment Appointment(DockAppointmentStatus status, DateTime start, DateTime end, byte version) => new()
    {
        WarehouseId = _warehouse.Id,
        Code = $"APT-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
        Direction = DockAppointmentDirection.Inbound,
        Status = status,
        PlannedStartUtc = start,
        PlannedEndUtc = end,
        CarrierName = "Nhà vận chuyển Test",
        VehicleType = "TRUCK",
        CreatedBy = _user.Id,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        RowVersion = Version(version)
    };

    private static byte[] Version(byte value) => Enumerable.Repeat(value, 8).ToArray();

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int UserId { get; set; }
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => false;
        public string Role => "Manager";
    }
}
