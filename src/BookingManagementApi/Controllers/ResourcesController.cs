using BookingManagementApi.Contracts.Catalog;
using BookingManagementApi.Services.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingManagementApi.Controllers;

[ApiController, Route("api/resources"), Authorize]
public sealed class ResourcesController(ResourceCatalogService catalog) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ResourceResponse>>> List(CancellationToken ct) =>
        Ok(await catalog.ListAsync(true, null, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ResourceResponse>> Get(Guid id, CancellationToken ct)
    {
        var result = await catalog.GetAsync(id, true, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
