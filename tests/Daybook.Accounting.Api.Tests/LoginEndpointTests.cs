using System.Net;
using System.Net.Http.Json;

using Daybook.Accounting.Api.Auth;

using Microsoft.IdentityModel.JsonWebTokens;

namespace Daybook.Accounting.Api.Tests;

/// <summary>
/// <c>POST /v1/auth/login</c> (spec §8) — issues a real, inspectable JWT
/// (not an opaque token format) on correct credentials.
/// </summary>
public class LoginEndpointTests
{
    private const string Email = "household@example.test";
    private const string Password = "Correct-Horse-Battery-Staple-1!";

    [Fact]
    public async Task Login_with_correct_credentials_returns_a_valid_JWT()
    {
        await using var factory = new DaybookWebApplicationFactory();
        await factory.MigrateAsync();
        using var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/v1/auth/register", new { Email, Password });
        var registered = await registerResponse.Content.ReadFromJsonAsync<RegisterResponseDto>();

        var response = await client.PostAsJsonAsync("/v1/auth/login", new { Email, Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        body.Should().NotBeNull();
        body!.Token.Split('.').Should().HaveCount(3);

        var jwt = new JsonWebToken(body.Token);
        jwt.TryGetClaim(DaybookClaimTypes.UserId, out var claim).Should().BeTrue();
        Guid.Parse(claim.Value).Should().Be(registered!.UserId);
    }

    [Fact]
    public async Task Login_with_the_wrong_password_is_rejected()
    {
        await using var factory = new DaybookWebApplicationFactory();
        await factory.MigrateAsync();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/v1/auth/register", new { Email, Password });

        var response = await client.PostAsJsonAsync("/v1/auth/login", new { Email, Password = "wrong-password" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_for_a_nonexistent_user_is_rejected_the_same_way_as_a_wrong_password()
    {
        await using var factory = new DaybookWebApplicationFactory();
        await factory.MigrateAsync();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/v1/auth/register", new { Email, Password });

        var wrongPasswordResponse = await client.PostAsJsonAsync("/v1/auth/login", new { Email, Password = "wrong-password" });
        var noSuchUserResponse = await client.PostAsJsonAsync(
            "/v1/auth/login", new { Email = "nobody@example.test", Password });

        // Same status and same body shape for both - a distinguishable
        // response is a classic user-enumeration side channel.
        noSuchUserResponse.StatusCode.Should().Be(wrongPasswordResponse.StatusCode).And.Be(HttpStatusCode.Unauthorized);
        var wrongPasswordBody = await wrongPasswordResponse.Content.ReadAsStringAsync();
        var noSuchUserBody = await noSuchUserResponse.Content.ReadAsStringAsync();
        noSuchUserBody.Should().Be(wrongPasswordBody);
    }

    private sealed record RegisterResponseDto(Guid UserId);

    private sealed record LoginResponseDto(string Token);
}