using bionicpro_reports.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ClickHouseService>();
builder.Services.AddAuthentication("SessionCookie")
    .AddCookie("SessionCookie", opts => { opts.Cookie.Name = "session_id"; opts.Cookie.HttpOnly = true; opts.Cookie.SecurePolicy = CookieSecurePolicy.Always; });


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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
