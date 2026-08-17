using BookingManagementApi.Contracts.Catalog;
using BookingManagementApi.Services.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingManagementApi.Controllers;

[ApiController, Route("api/services"), Authorize]
public sealed class ServicesController(ServiceCatalogService catalog) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List active services")]
    public async Task<ActionResult<List<ServiceResponse>>> List(CancellationToken ct) =>
        Ok(await catalog.ListAsync(true, null, ct));

    [HttpGet("{id:guid}")]
    [EndpointSummary("Get an active service")]
    public async Task<ActionResult<ServiceResponse>> Get(Guid id, CancellationToken ct)
    {
        var result = await catalog.GetAsync(id, true, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
