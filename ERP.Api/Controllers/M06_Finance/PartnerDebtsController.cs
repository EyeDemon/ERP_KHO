using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/partner-debts")]
[Route("api/v1/partner-debts")]
public sealed class PartnerDebtsController(IPartnerDebtService service) : ControllerBase
{
    [HttpGet("summary")]
    [PermissionAuthorize(AppPermissions.PartnerRead)]
    public async Task<IActionResult> GetSummary(CancellationToken ct = default) =>
        Ok(await service.GetSummaryAsync(ct));

    [HttpGet("invoices")]
    [PermissionAuthorize(AppPermissions.PartnerRead)]
    public async Task<IActionResult> ListInvoices(
        [FromQuery] string? debtType = null,
        [FromQuery] int? partnerId = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default) =>
        Ok(await service.ListInvoicesAsync(debtType, partnerId, status, ct));

    [HttpGet("invoices/{id:int}")]
    [PermissionAuthorize(AppPermissions.PartnerRead)]
    public async Task<IActionResult> GetInvoice(int id, CancellationToken ct = default) =>
        Ok(await service.GetInvoiceAsync(id, ct));

    [HttpPost("invoices/{id:int}/pay")]
    [IdempotentCommand("PartnerDebt.PayInvoice")]
    [PermissionAuthorize(AppPermissions.PartnerUpdate)]
    public async Task<IActionResult> PayInvoice(
        int id,
        [FromBody] PayPartnerDebtDto dto,
        CancellationToken ct = default) =>
        Ok(await service.PayInvoiceAsync(id, dto, ct));

    [HttpGet("ledger")]
    [PermissionAuthorize(AppPermissions.PartnerRead)]
    public async Task<IActionResult> ListLedger(
        [FromQuery] int? partnerId = null,
        [FromQuery] int? invoiceId = null,
        [FromQuery] string? debtType = null,
        CancellationToken ct = default) =>
        Ok(await service.ListLedgerAsync(partnerId, invoiceId, debtType, ct));
}
