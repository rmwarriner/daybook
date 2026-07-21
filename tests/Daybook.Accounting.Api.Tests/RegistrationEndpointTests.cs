using System.Net;
using System.Net.Http.Json;

namespace Daybook.Accounting.Api.Tests;

/// <summary>
/// <c>POST /v1/auth/register</c> (spec §8) — bootstrap-only: succeeds
/// exactly once, for whoever registers while zero users exist, then rejects
/// every later attempt. An open, always-available registration endpoint on
/// a self-hosted financial API would be a real vulnerability surface for no
/// v1 benefit.
/// </summary>
public class RegistrationEndpointTests
{
    [Fact]
    public async Task Register_the_first_user_succeeds()
    {
        await using var factory = new DaybookWebApplicationFactory();
        await factory.MigrateAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/v1/auth/register", new { Email = "household@example.test", Password = "Correct-Horse-Battery-Staple-1!" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Register_a_second_user_is_rejected_once_one_exists()
    {
        await using var factory = new DaybookWebApplicationFactory();
        await factory.MigrateAsync();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync(
            "/v1/auth/register", new { Email = "household@example.test", Password = "Correct-Horse-Battery-Staple-1!" });

        var response = await client.PostAsJsonAsync(
            "/v1/auth/register", new { Email = "someone-else@example.test", Password = "Another-Strong-Password-2!" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}