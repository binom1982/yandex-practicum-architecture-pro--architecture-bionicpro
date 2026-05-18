using BionicproAuthService.Middleware;
using BionicproAuthService.Models;
using BionicproAuthService.Services;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<KeycloakOptions>(
    builder.Configuration.GetSection("Keycloak"));
builder.Services.Configure<AuthSessionOptions>(
    builder.Configuration.GetSection("Session"));

// Redis cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"];
    options.InstanceName = "bionicpro:";
});

// HTTP Client for Keycloak
builder.Services.AddHttpClient<IKeycloakClient, KeycloakClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Keycloak:BaseUrl"]!);
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
});

// Services
builder.Services.AddScoped<IAuthSessionService, AuthSessionService>();

// API

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseMiddleware<SessionValidationMiddleware>();
//app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
