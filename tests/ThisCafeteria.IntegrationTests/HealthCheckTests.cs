using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;

namespace ThisCafeteria.IntegrationTests;

public sealed class HealthCheckTests : IClassFixture<ThisCafeteriaWebApplicationFactory>
{
    private readonly ThisCafeteriaWebApplicationFactory factory;

    public HealthCheckTests(ThisCafeteriaWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Liveness_ShouldReturnSuccess()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task Readiness_ShouldReturnSuccessAfterDatabaseInitialization()
    {
        var client = factory.CreateClient();

        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            response?.Dispose();
            response = await client.GetAsync("/health/ready");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        using (response)
        {
            response.Should().NotBeNull();
            response!.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
