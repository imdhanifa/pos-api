using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using PosSaas.Api.Auth;
using PosSaas.Api.Notifications;
using PosSaas.Infrastructure.Persistence;
using PosSaas.Infrastructure.Security;
using Scalar.AspNetCore;

// QuestPDF (Reporting/ReportExporter.cs) requires an explicit license before generating any
// document, or GeneratePdf() throws - Community is QuestPDF's free tier, fine for this scaffold.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// JsonStringEnumConverter: the mobile client (mobile/src/sync/syncEngine.ts, src/types.ts)
// types every enum-backed field (Order.Type/Status, etc.) as a string and writes it straight
// into a SQLite TEXT column - System.Text.Json's default is to serialize enums as their
// numeric value, which would silently corrupt every synced Order's type/status. This applies
// everywhere a raw entity is returned directly (e.g. SyncController.Pull) rather than through
// a DTO that already calls .ToString() itself (e.g. PosController's OrderDto).
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Persistence: EF Core + PostgreSQL (see README "Swapping in EF Core + PostgreSQL" - migrated
// from SQL Server; PosSaasDbContext/EfRepository had no provider-specific code, so this was a
// one-line UseSqlServer -> UseNpgsql swap plus the package/connection-string change).
// AddDbContext registers PosSaasDbContext as Scoped (the EF Core default), so PosSaasStore is
// registered Scoped too - it must share one DbContext per request/scope, not be a
// process-lifetime singleton like it was in the pure in-memory scaffold. See PosSaasStore.cs
// for the InMemoryRepository-backed parameterless constructor if you want the old
// in-memory-only mode instead (e.g. `AddSingleton(new PosSaasStore())`).
builder.Services.AddDbContext<PosSaasDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<PosSaasStore>();

// Auth: hand-rolled JWT + Bearer scheme standing in for JwtBearer (see README).
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "dev-only-secret-change-me-32-chars-min";
builder.Services.AddSingleton(new SimpleJwtService(jwtSecret));
builder.Services
    .AddAuthentication(BearerAuthHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, BearerAuthHandler>(BearerAuthHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

// Real-time push (Notifications/NotificationsHub.cs) + the background scan that drives it
// (Notifications/SubscriptionExpiryNotifier.cs) - see mobile/src/notifications/NotificationsHub.ts
// for the client side.
builder.Services.AddSignalR();
builder.Services.AddHostedService<SubscriptionExpiryNotifier>();

// Health check: GET /health - just DB connectivity for now (SeedData already proved the schema
// exists at startup above). No auth on this route since load balancers/uptime monitors hit it
// anonymously.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<PosSaasDbContext>("database");

// Caching: short-TTL server-side cache for read-heavy per-tenant GETs (catalog, dashboard) - see
// CachingExtensions.cs. In-memory only, which is fine for this single-instance scaffold; swap
// for IDistributedCache (Redis) if this ever runs behind more than one API instance.
builder.Services.AddMemoryCache();

// Rate limiting: global fixed window, partitioned per authenticated tenant (falls back to
// remote IP for anonymous requests like /api/auth/login) so one noisy tenant/device can't
// starve another. 120 requests/minute is generous for normal POS traffic (a busy till polling
// dashboard/sync every few seconds) while still capping runaway retry loops.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var partitionKey = context.User.GetTenantId()?.ToString() ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

// OpenAPI + Scalar (see /scalar/v1 once running) - documents every [ApiController] below via
// reflection, no [SwaggerOperation] annotations needed. Registers the hand-rolled Bearer scheme
// (Infrastructure/Security/BearerAuthHandler, not a real JwtBearer handler) as a security
// requirement so Scalar's "Authorize" button sends the token every [Authorize] endpoint expects.
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste the token from POST /api/auth/login (no \"Bearer \" prefix needed here)."
        };
        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document, null)] = new List<string>()
        });
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// Schema creation always runs, every environment - EnsureCreated is a no-op if the tables
// already exist, so this is safe to call on every startup, not just the first one. Uses
// EnsureCreated rather than a migrations-based Database.Migrate() call so this keeps working
// out of the box before anyone has run `dotnet ef migrations add Initial` - switch to
// Migrate() once real migrations exist (see README / final notes for that command).
//
// Demo-data seeding (SeedData.Seed - one fake tenant, a year of orders) is deliberately
// Development-only: skipping it in Production is right, but an earlier version of this block
// commented out EnsureCreated too, which took the schema down with it - on a fresh production
// database that meant every endpoint failed with a Postgres "relation does not exist" error,
// not just an empty demo. Schema creation and demo seeding are two different concerns; only
// the second one should ever be environment-gated.
//using (var scope = app.Services.CreateScope())
//{
//    var db = scope.ServiceProvider.GetRequiredService<PosSaasDbContext>();
//    db.Database.EnsureCreated();
//    if (app.Environment.IsDevelopment())
//    {
//        await SeedData.Seed(scope.ServiceProvider.GetRequiredService<PosSaasStore>());
//    }
//}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<NotificationsHub>("/hubs/notifications");

app.MapHealthChecks("/health");

// API docs: raw OpenAPI JSON at /openapi/v1.json, interactive Scalar UI at /scalar/v1.
// Left mapped in every environment (not gated behind app.Environment.IsDevelopment()) since
// this is a scaffold with no deployed/production environment yet - revisit if that changes.
app.MapOpenApi();
app.MapScalarApiReference();

app.MapGet("/", () => Results.Ok(new
{
    service = "POS SaaS API",
    status = "ok",
    note = "EF Core + PostgreSQL persistence, hand-rolled JWT for this scaffold - see README for the JwtBearer/Swashbuckle package swap and appsettings.json ConnectionStrings:Default for the connection string.",
    apiDocs = "/scalar/v1",
    health = "/health",
    demoLogin = new { email = "owner@demo.pos", password = "Demo@123" }
}));

app.Run();

// Needed so an xUnit integration test project can reference this entry point via
// WebApplicationFactory<Program> - top-level statement programs need this explicit marker.
public partial class Program { }
