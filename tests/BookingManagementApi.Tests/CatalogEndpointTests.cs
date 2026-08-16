using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookingManagementApi.Common.Security;
using BookingManagementApi.Contracts.Catalog;
using BookingManagementApi.Data;
using BookingManagementApi.Entities;
using BookingManagementApi.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookingManagementApi.Tests;

public sealed class CatalogEndpointTests : IClassFixture<AuthenticationApiFactory>
{
    private readonly AuthenticationApiFactory _factory;
    public CatalogEndpointTests(AuthenticationApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Authorization_enforces_admin_and_authenticated_catalog_access()
    {
        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/services")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/services")).StatusCode);

        using var customer = await ClientForRoleAsync(AppRoles.Customer);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/admin/services")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync("/api/services")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync("/api/resources")).StatusCode);

        using var admin = await ClientForRoleAsync(AppRoles.Admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/services")).StatusCode);
    }

    [Theory]
    [InlineData("", null, 15)]
    [InlineData("   ", null, 15)]
    [InlineData("Valid", null, 0)]
    [InlineData("Valid", null, -15)]
    [InlineData("Valid", null, 10)]
    public async Task Service_validation_rejects_invalid_values(string name, string? description, int duration)
    {
        using var admin = await ClientForRoleAsync(AppRoles.Admin);
        var response = await admin.PostAsJsonAsync("/api/admin/services", new CreateServiceRequest(name, description, duration));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Service_validation_rejects_oversized_fields()
    {
        using var admin = await ClientForRoleAsync(AppRoles.Admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/services",
            new CreateServiceRequest(new string('n', 201), null, 15))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/services",
            new CreateServiceRequest("Valid", new string('d', 1001), 15))).StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_get_update_and_toggle_service_while_customer_visibility_tracks_activation()
    {
        using var admin = await ClientForRoleAsync(AppRoles.Admin);
        using var customer = await ClientForRoleAsync(AppRoles.Customer);
        var create = await admin.PostAsJsonAsync("/api/admin/services", new CreateServiceRequest("  Massage  ", "   ", 30));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = (await create.Content.ReadFromJsonAsync<ServiceResponse>())!;
        Assert.Equal("Massage", created.Name);
        Assert.Null(created.Description);
        Assert.True(created.IsActive);
        Assert.NotEqual(default, created.CreatedAtUtc);

        Assert.Equal(created, await admin.GetFromJsonAsync<ServiceResponse>($"/api/admin/services/{created.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/services/{Guid.NewGuid()}")).StatusCode);
        await Task.Delay(5);
        var update = await admin.PutAsJsonAsync($"/api/admin/services/{created.Id}", new UpdateServiceRequest("Updated", "Desc", 45));
        var updated = (await update.Content.ReadFromJsonAsync<ServiceResponse>())!;
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(created.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.True(updated.UpdatedAtUtc > created.UpdatedAtUtc);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync($"/api/admin/services/{created.Id}/activation", new SetActivationRequest(false))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync($"/api/admin/services/{created.Id}/activation", new SetActivationRequest(false))).StatusCode);
        Assert.NotNull(await admin.GetFromJsonAsync<ServiceResponse>($"/api/admin/services/{created.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync($"/api/services/{created.Id}")).StatusCode);
        Assert.DoesNotContain((await customer.GetFromJsonAsync<List<ServiceResponse>>("/api/services"))!, x => x.Id == created.Id);
        await admin.PatchAsJsonAsync($"/api/admin/services/{created.Id}/activation", new SetActivationRequest(true));
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync($"/api/services/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Resource_validation_rejects_blank_and_oversized_fields()
    {
        using var admin = await ClientForRoleAsync(AppRoles.Admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/resources", new CreateResourceRequest("  ", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/resources", new CreateResourceRequest(new string('n', 201), null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/resources", new CreateResourceRequest("Valid", new string('d', 1001)))).StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_get_update_and_toggle_resource_while_customer_visibility_tracks_activation()
    {
        using var admin = await ClientForRoleAsync(AppRoles.Admin);
        using var customer = await ClientForRoleAsync(AppRoles.Customer);
        var create = await admin.PostAsJsonAsync("/api/admin/resources", new CreateResourceRequest("  Room A ", " "));
        var created = (await create.Content.ReadFromJsonAsync<ResourceResponse>())!;
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.True(created.IsActive);
        Assert.Equal("Room A", created.Name);
        Assert.Null(created.Description);
        Assert.Equal(created, await admin.GetFromJsonAsync<ResourceResponse>($"/api/admin/resources/{created.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/resources/{Guid.NewGuid()}")).StatusCode);
        await Task.Delay(5);
        var update = await admin.PutAsJsonAsync($"/api/admin/resources/{created.Id}", new UpdateResourceRequest("Room B", "Updated"));
        var updated = (await update.Content.ReadFromJsonAsync<ResourceResponse>())!;
        Assert.Equal(created.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.True(updated.UpdatedAtUtc > created.UpdatedAtUtc);
        await admin.PatchAsJsonAsync($"/api/admin/resources/{created.Id}/activation", new SetActivationRequest(false));
        Assert.NotNull(await admin.GetFromJsonAsync<ResourceResponse>($"/api/admin/resources/{created.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync($"/api/resources/{created.Id}")).StatusCode);
        Assert.DoesNotContain((await customer.GetFromJsonAsync<List<ResourceResponse>>("/api/resources"))!, x => x.Id == created.Id);
        await admin.PatchAsJsonAsync($"/api/admin/resources/{created.Id}/activation", new SetActivationRequest(true));
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync($"/api/resources/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Assignments_handle_success_conflicts_missing_entities_listing_removal_and_deactivation_preservation()
    {
        using var admin = await ClientForRoleAsync(AppRoles.Admin);
        var service = await CreateServiceAsync(admin);
        var resource = await CreateResourceAsync(admin);
        var url = $"/api/admin/resources/{resource.Id}/services/{service.Id}";
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync(url, null)).StatusCode);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.ResourceServices.AnyAsync(x => x.ResourceId == resource.Id && x.ServiceId == service.Id));
        }
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync(url, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/resources/{Guid.NewGuid()}/services/{service.Id}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/resources/{resource.Id}/services/{Guid.NewGuid()}", null)).StatusCode);
        Assert.Contains((await admin.GetFromJsonAsync<List<ServiceResponse>>($"/api/admin/resources/{resource.Id}/services"))!, x => x.Id == service.Id);

        await admin.PatchAsJsonAsync($"/api/admin/services/{service.Id}/activation", new SetActivationRequest(false));
        await admin.PatchAsJsonAsync($"/api/admin/resources/{resource.Id}/activation", new SetActivationRequest(false));
        Assert.Contains((await admin.GetFromJsonAsync<List<ServiceResponse>>($"/api/admin/resources/{resource.Id}/services"))!, x => x.Id == service.Id);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.ResourceServices.AnyAsync(x => x.ResourceId == resource.Id && x.ServiceId == service.Id));
        }
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/admin/services/{service.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/admin/resources/{resource.Id}")).StatusCode);
    }

    private async Task<HttpClient> ClientForRoleAsync(string role)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var user = new User { Id = Guid.NewGuid(), FirstName = "Test", LastName = role,
            Email = $"{Guid.NewGuid():N}@example.com", NormalizedEmail = $"{Guid.NewGuid():N}@EXAMPLE.COM",
            PasswordHash = "test-only", Role = role, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var token = scope.ServiceProvider.GetRequiredService<ITokenService>().CreateAccessToken(user).Token;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<ServiceResponse> CreateServiceAsync(HttpClient client) =>
        (await (await client.PostAsJsonAsync("/api/admin/services", new CreateServiceRequest($"Service {Guid.NewGuid():N}", null, 15)))
            .Content.ReadFromJsonAsync<ServiceResponse>())!;
    private static async Task<ResourceResponse> CreateResourceAsync(HttpClient client) =>
        (await (await client.PostAsJsonAsync("/api/admin/resources", new CreateResourceRequest($"Resource {Guid.NewGuid():N}", null)))
            .Content.ReadFromJsonAsync<ResourceResponse>())!;
}
