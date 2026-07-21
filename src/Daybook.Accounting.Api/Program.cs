using System.Text;

using Daybook.Accounting.Api.Auth;
using Daybook.Accounting.Infrastructure;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Shared by JWT Bearer validation (below) and JwtTokenFactory (issuance) -
// both sides must read the exact same key bytes.
static byte[] ReadSigningKeyBytes(IConfiguration configuration)
{
    var signingKeyPath = configuration["Daybook:JwtSigningKeyFilePath"] ?? "/run/secrets/jwt_signing_key";
    return Encoding.UTF8.GetBytes(PassphraseFile.Read(signingKeyPath));
}

// Real DB-encryption-key bootstrap (read passphrase -> load-or-generate a
// WrappedDataKey -> unwrap -> UseEncryptedSqlite) is still parked
// composition-root work - see EncryptedSqliteDbContextOptionsExtensions.
// Plain UseSqlite here is a deliberate, known gap, not an oversight.
//
// Configuration is read lazily (via injected IConfiguration), not eagerly
// off `builder.Configuration` here, so overrides layered in through
// WebApplicationFactory.ConfigureWebHost (as the Api test harness does)
// always take effect - an eager read at this point in the script would run
// before those overrides are merged in.
builder.Services.AddDbContext<DaybookDbContext>((services, options) =>
{
    var dbPath = services.GetRequiredService<IConfiguration>()["Daybook:DatabasePath"] ?? "daybook.db";
    options.UseSqlite($"Data Source={dbPath}");
});

builder.Services
    .AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
    .AddEntityFrameworkStores<DaybookDbContext>();
// No .AddRoles<...>() - see ApplicationUser's remarks. No
// .AddDefaultTokenProviders() - neither needed until password-reset/
// email-confirm flows exist.

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, configuration) =>
    {
        var signingKeyBytes = ReadSigningKeyBytes(configuration);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = JwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = JwtSettings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton(services =>
    new JwtTokenFactory(ReadSigningKeyBytes(services.GetRequiredService<IConfiguration>())));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Hello World!");
app.MapAuthEndpoints();

app.Run();

public partial class Program;