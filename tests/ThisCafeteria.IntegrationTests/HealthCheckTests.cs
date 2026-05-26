using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ThisCafeteria.IntegrationTests;

public sealed class HealthCheckTests : IClassFixture<ThisCafeteriaWebApplicationFactory>
{
    private readonly ThisCafeteriaWebApplicationFactory factory;

    public HealthCheckTests(ThisCafeteriaWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Health_ShouldReturnSuccess()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.IsSuccessStatusCode.Should().BeTrue();
    }
}
