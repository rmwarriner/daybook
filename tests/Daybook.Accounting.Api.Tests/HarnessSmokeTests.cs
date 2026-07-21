namespace Daybook.Accounting.Api.Tests;

/// <summary>Proves the real host — Identity + JWT Bearer wiring included — boots and serves a request.</summary>
public class HarnessSmokeTests
{
    [Fact]
    public async Task The_host_boots_and_responds()
    {
        await using var factory = new DaybookWebApplicationFactory();
        await factory.MigrateAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
    }
}