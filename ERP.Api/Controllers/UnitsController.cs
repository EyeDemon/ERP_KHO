using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ERP.Api.Authorization;

namespace ERP.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class UnitsController : ControllerBase
    {
        private readonly IUnitService _unitService;

        public UnitsController(IUnitService unitService)
        {
            _unitService = unitService;
        }

        [HttpGet]
        [PermissionAuthorize(AppPermissions.UomRead)]
        public async Task<IActionResult> GetAll()
        {
            var units = await _unitService.GetAllUnitsAsync();
            return Ok(units);
        }

        [HttpGet("{id}")]
        [PermissionAuthorize(AppPermissions.UomRead)]
        public async Task<IActionResult> GetById(int id)
        {
            var unit = await _unitService.GetUnitByIdAsync(id);
            return Ok(unit);
        }

        [HttpPost]
        [PermissionAuthorize(AppPermissions.UomManage)]
        public async Task<IActionResult> Create([FromBody] CreateUnitDto dto)
        {
            var username = User.Identity?.Name ?? "system";
            var unit = await _unitService.CreateUnitAsync(dto, username);
            return CreatedAtAction(nameof(GetById), new { id = unit.Id }, unit);
        }

        [HttpPut("{id}")]
        [PermissionAuthorize(AppPermissions.UomManage)]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateUnitDto dto)
        {
            var username = User.Identity?.Name ?? "system";
            await _unitService.UpdateUnitAsync(id, dto, username);
            return NoContent();
        }

        [HttpDelete("{id}")]
        [PermissionAuthorize(AppPermissions.UomManage)]
        public async Task<IActionResult> Delete(int id)
        {
            await _unitService.DeleteUnitAsync(id);
            return NoContent();
        }
    }
}
