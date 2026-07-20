using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ThisCafeteria.Web;

namespace ThisCafeteria.IntegrationTests;

public class AgentResourceControllerTests : IClassFixture<WebApplicationFactory<ThisCafeteria.Web.WebMarker>>
{
    private readonly WebApplicationFactory<ThisCafeteria.Web.WebMarker> _factory;

    public AgentResourceControllerTests(WebApplicationFactory<ThisCafeteria.Web.WebMarker> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AgentGateway:ServiceSecret"] = "test-secret",
                    ["Database:ConnectionString"] = "",
                    ["ConnectionStrings:DefaultConnection"] = ""
                });
            });
        });
    }

    [Fact]
    public async Task SearchProducts_MissingSecret_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/internal/agent/resources/search-products", new { Query = "test", CorrelationId = "123" });
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine(await response.Content.ReadAsStringAsync());
        }
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SearchProducts_ValidSecret_ReturnsOk()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-agent-gateway-secret", "test-secret");
        var response = await client.PostAsJsonAsync("/internal/agent/resources/search-products", new { Query = "test query", CorrelationId = "123" });
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("kind").GetString().Should().Be("product-search");
        content.GetProperty("query").GetString().Should().Be("test query");
    }

    [Fact]
    public async Task SearchProducts_InvalidQuery_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-agent-gateway-secret", "test-secret");
        
        // Empty query
        var response = await client.PostAsJsonAsync("/internal/agent/resources/search-products", new { Query = "", CorrelationId = "123" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Max length query
        var longQuery = new string('a', 201);
        response = await client.PostAsJsonAsync("/internal/agent/resources/search-products", new { Query = longQuery, CorrelationId = "123" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BrewPlan_InvalidQuantity_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-agent-gateway-secret", "test-secret");
        
        // Zero quantity
        var response = await client.PostAsJsonAsync("/internal/agent/resources/brew-plan", new { ProductId = "test", Quantity = 0, CorrelationId = "123" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Too large quantity
        response = await client.PostAsJsonAsync("/internal/agent/resources/brew-plan", new { ProductId = "test", Quantity = 10001, CorrelationId = "123" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BrewPlan_MissingCorrelationId_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-agent-gateway-secret", "test-secret");
        
        var response = await client.PostAsJsonAsync("/internal/agent/resources/brew-plan", new { ProductId = "test", Quantity = 1 });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
