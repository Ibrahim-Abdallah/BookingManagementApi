using BookingManagementApi.Common.Security;
using BookingManagementApi.Contracts.Catalog;
using BookingManagementApi.Services.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingManagementApi.Controllers.Admin;

[ApiController, Route("api/admin/services"), Authorize(Roles = AppRoles.Admin)]
public sealed class ServicesController(ServiceCatalogService catalog) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List services for administration")]
    public async Task<ActionResult<List<ServiceResponse>>> List([FromQuery] bool? isActive, CancellationToken ct) =>
        Ok(await catalog.ListAsync(false, isActive, ct));

    [HttpGet("{id:guid}")]
    [EndpointSummary("Get a service for administration")]
    public async Task<ActionResult<ServiceResponse>> Get(Guid id, CancellationToken ct)
    {
        var result = await catalog.GetAsync(id, false, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [EndpointSummary("Create a service")]
    public async Task<ActionResult<ServiceResponse>> Create(CreateServiceRequest request, CancellationToken ct)
    {
        var result = await catalog.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [EndpointSummary("Update a service")]
    public async Task<ActionResult<ServiceResponse>> Update(Guid id, UpdateServiceRequest request, CancellationToken ct)
    {
        var result = await catalog.UpdateAsync(id, request, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPatch("{id:guid}/activation")]
    [EndpointSummary("Activate or deactivate a service")]
    public async Task<IActionResult> SetActivation(Guid id, SetActivationRequest request, CancellationToken ct) =>
        await catalog.SetActivationAsync(id, request.IsActive, ct) ? NoContent() : NotFound();
}
