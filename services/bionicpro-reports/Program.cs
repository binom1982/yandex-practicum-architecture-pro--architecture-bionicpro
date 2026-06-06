using Amazon.S3;
using bionicpro_reports.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHttpClient();  // 🔹 Для вызовов к auth-сервису

builder.Services.AddSingleton<ClickHouseService>();

// 🔹 НОВОЕ: Настройка CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://127.0.0.1:3000") // ← оба варианта  // ← разрешаем фронтенд
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();  // ← важно для отправки кук
    });
});

builder.Services.AddAuthentication("SessionCookie")
    .AddCookie("SessionCookie", opts =>
    {
        opts.Cookie.Name = "bionicpro_session";
        opts.Cookie.HttpOnly = true;
        opts.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        opts.Cookie.SameSite = SameSiteMode.Lax;

        // 🔹 НОВОЕ: Отключаем редиректы для API
        opts.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        opts.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// 🔹 Конфигурация для доступа к другим сервисам
builder.Configuration.AddInMemoryCollection(new[]
{
    new KeyValuePair<string, string>("AuthServiceUrl", "http://bionicpro-auth:8080"),
    new KeyValuePair<string, string>("SessionCookieName", "bionicpro_session"),
});

var s3Config = new AmazonS3Config
{
    ServiceURL = builder.Configuration["S3__Endpoint"] ?? "http://minio:9000",
    ForcePathStyle = true // 🔹 Критично для MinIO! (иначе будет искать http://reports.minio:9000)
};

builder.Services.AddSingleton<IAmazonS3>(sp => new AmazonS3Client(
    builder.Configuration["S3__AccessKey"] ?? "minioadmin",
    builder.Configuration["S3__SecretKey"] ?? "minioadmin",
    s3Config
));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

//app.UseHttpsRedirection();


// 🔹 НОВОЕ: Порядок важен! CORS до Auth/Authorization
app.UseCors("AllowFrontend");  // ← должно быть ДО UseAuthentication

//app.UseAuthentication();  // 🔹 Порядок важен: Auth до Authorization
//app.UseAuthorization();

app.MapControllers();


// 🔹 [NEW] После UseCors, до MapControllers
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        // 🔹 Логируем ошибку
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Unhandled exception in request");

        // 🔹 Возвращаем 500 с корректными заголовками
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"error\":\"Internal server error\"}");
    }
});

app.Run();