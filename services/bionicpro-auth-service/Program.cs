using BionicproAuthService.Middleware;
using BionicproAuthService.Models;
using BionicproAuthService.Services;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────────────
// Configuration
// ─────────────────────────────────────────────────────
builder.Services.Configure<KeycloakOptions>(
    builder.Configuration.GetSection("Keycloak"));
builder.Services.Configure<AuthSessionOptions>(
    builder.Configuration.GetSection("AuthSession"));
builder.Services.Configure<SessionSecurityOptions>(
    builder.Configuration.GetSection("SessionSecurity"));

// ─────────────────────────────────────────────────────
// Session (in-memory, no Redis)
// ─────────────────────────────────────────────────────
builder.Services.AddDistributedMemoryCache(); // для хранения PKCE state
builder.Services.AddSession(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.Name = "bionicpro_auth_session";
    o.IdleTimeout = TimeSpan.FromMinutes(30);
});

// ─────────────────────────────────────────────────────
// HTTP Client for Keycloak
// ─────────────────────────────────────────────────────

builder.Services.AddHttpClient<IKeycloakClient, KeycloakClient>(client =>
{
    var baseUrl = builder.Configuration["Keycloak:AuthUrl"] ?? "http://keycloak:8080";
    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
});


// ─────────────────────────────────────────────────────
// Services
// ─────────────────────────────────────────────────────
// Стало (добавьте оба IOptions):
builder.Services.AddSingleton<IAuthSessionService, InMemorySessionService>(sp =>
{
    var securityOpts = sp.GetRequiredService<IOptions<SessionSecurityOptions>>();
    var sessionOpts = sp.GetRequiredService<IOptions<AuthSessionOptions>>();
    return new InMemorySessionService(securityOpts, sessionOpts);
});

// ─────────────────────────────────────────────────────
// API + Swagger
// ─────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "bionicpro-auth", Version = "v1" });

    // 🔹 Security Definition для cookie
    c.AddSecurityDefinition("cookie", new OpenApiSecurityScheme
    {
        Name = "bionicpro_session",           // имя куки
        Type = SecuritySchemeType.ApiKey,     // тип: API ключ
        In = ParameterLocation.Cookie,        // передаётся в куки
        Description = "Session cookie для авторизованных запросов"
    });

    // 🔹 Security Requirement — применяет схему к эндпоинтам
    /*c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                //Reference = new OpenApiReference
                //{
                //    Type = ReferenceType.SecurityScheme,
                //    Id = "cookie"  // ссылка на определение выше
                //}
            },
            new List<string>() // scopes (пусто для ApiKey)
        }
    });*/
});

// ─────────────────────────────────────────────────────
// CORS — разрешаем фронтенду
// ─────────────────────────────────────────────────────
builder.Services.AddCors(o => o.AddPolicy("AllowFrontend", p => p
    .WithOrigins("http://localhost:3000")
    .AllowAnyMethod()
    .AllowAnyHeader()
    .AllowCredentials())); // важно для куки

builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information); // или Debug для детальных логов

// ─────────────────────────────────────────────────────
// Build app
// ─────────────────────────────────────────────────────
var app = builder.Build();

// ─────────────────────────────────────────────────────
// Middleware pipeline
// ─────────────────────────────────────────────────────
// Swagger — всегда включён (для dev), в prod можно обернуть в if
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "bionicpro-auth v1");
    c.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();
app.UseCors("AllowFrontend"); // ← CORS до авторизации
app.UseSession();             // ← сессии до контроллеров
app.UseMiddleware<SessionValidationMiddleware>();

app.MapControllers();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Root redirect to Swagger (удобно для dev)
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();