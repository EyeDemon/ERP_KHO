using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ERP.Api.Authorization;

namespace ERP.Api.Controllers
{
    [Authorize(Roles = AppRoles.AllRoles)]
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
        public async Task<IActionResult> GetAll()
        {
            var units = await _unitService.GetAllUnitsAsync();
            return Ok(units);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var unit = await _unitService.GetUnitByIdAsync(id);
            return Ok(unit);
        }

        [HttpPost]
        [Authorize(Roles = AppRoles.AdminOrManager)]
        public async Task<IActionResult> Create([FromBody] CreateUnitDto dto)
        {
            var username = User.Identity?.Name ?? "system";
            var unit = await _unitService.CreateUnitAsync(dto, username);
            return CreatedAtAction(nameof(GetById), new { id = unit.Id }, unit);
        }

        [HttpPut("{id}")]
        [Authorize(Roles = AppRoles.AdminOrManager)]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateUnitDto dto)
        {
            var username = User.Identity?.Name ?? "system";
            await _unitService.UpdateUnitAsync(id, dto, username);
            return NoContent();
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = AppRoles.AdminOrManager)]
        public async Task<IActionResult> Delete(int id)
        {
            await _unitService.DeleteUnitAsync(id);
            return NoContent();
        }
    }
}
