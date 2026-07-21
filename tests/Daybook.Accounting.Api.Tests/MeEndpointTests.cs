using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Daybook.Accounting.Api.Tests;

/// <summary>
/// <c>GET /v1/me</c> (spec §8) — the actual deliverable this milestone
/// exists to produce: proves the full chain (JWT → <c>ClaimsPrincipal</c> →
/// <c>ICurrentUser.UserId</c>) resolves correctly over a real HTTP request.
/// A real, permanent endpoint from the moment it ships (CLAUDE.md's
/// additive-only versioning discipline), not disposable test scaffolding.
/// </summary>
public class MeEndpointTests
{
    private const string Email = "household@example.test";
    private const string Password = "Correct-Horse-Battery-Staple-1!";

    [Fact]
    public async Task GET_me_without_a_token_is_rejected()
    {
        await using var factory = new DaybookWebApplicationFactory();
        await factory.MigrateAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_me_with_a_valid_token_returns_the_authenticated_users_id()
    {
        await using var factory = new DaybookWebApplicationFactory();
        await factory.MigrateAsync();
        using var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/v1/auth/register", new { Email, Password });
        var registered = await registerResponse.Content.ReadFromJsonAsync<RegisterResponseDto>();
        var loginResponse = await client.PostAsJsonAsync("/v1/auth/login", new { Email, Password });
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);

        var response = await client.GetAsync("/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MeResponseDto>();
        body!.UserId.Should().Be(registered!.UserId);
    }

    [Fact]
    public async Task GET_me_with_a_tampered_token_is_rejected()
    {
        await using var factory = new DaybookWebApplicationFactory();
        await factory.MigrateAsync();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/v1/auth/register", new { Email, Password });
        var loginResponse = await client.PostAsJsonAsync("/v1/auth/login", new { Email, Password });
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        var segments = login!.Token.Split('.');
        var tamperedSignature = segments[2][..^1] + (segments[2][^1] == 'A' ? 'B' : 'A');
        var tamperedToken = $"{segments[0]}.{segments[1]}.{tamperedSignature}";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tamperedToken);

        var response = await client.GetAsync("/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_me_with_an_expired_token_is_rejected()
    {
        await using var factory = new DaybookWebApplicationFactory();
        await factory.MigrateAsync();
        using var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/v1/auth/register", new { Email, Password });
        var registered = await registerResponse.Content.ReadFromJsonAsync<RegisterResponseDto>();
        var expiredToken = factory.CreateExpiredToken(registered!.UserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.GetAsync("/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed record RegisterResponseDto(Guid UserId);

    private sealed record LoginResponseDto(string Token);

    private sealed record MeResponseDto(Guid UserId);
}