using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using NginxSslManager.Api.Background;
using NginxSslManager.Api.Common;
using NginxSslManager.Api.Data;
using NginxSslManager.Api.Services;
using NginxSslManager.Api.Services.Nginx;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var paths = AppPaths.Create(builder.Environment.ContentRootPath, builder.Configuration);
var jwt = new JwtService(paths, builder.Configuration);

builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<CertificateInspector>();
builder.Services.AddSingleton<NginxConfigLoader>();
builder.Services.AddSingleton<DnsChallengeStore>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={paths.DatabasePath}"));

builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<AdminAccountService>();
builder.Services.AddScoped<ActivityLogService>();
builder.Services.AddScoped<NginxConfigService>();
builder.Services.AddScoped<AcmeService>();
builder.Services.AddScoped<CertificateManager>();
builder.Services.AddHostedService<RenewalWorker>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "Nginx SSL Manager API",
            Version = "v1",
            Description = "基于 .NET 10 的 Nginx Let's Encrypt 证书管理系统 API。\n\n" +
                          "除登录与健康检查外，所有接口都需要先调用 `/api/auth/login` 获取 JWT，并点击右上角 Authorize 填入 Token。"
        };

        var components = document.Components ??= new OpenApiComponents();
        var securitySchemes = components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        securitySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Name = "Authorization",
            Description = "输入登录接口返回的 JWT，不需要手动添加 Bearer 前缀。"
        };

        var servers = document.Servers ??= [];
        servers.Clear();
        servers.Add(new OpenApiServer { Url = "/", Description = "当前服务" });
        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, context, _) =>
    {
        var requiresAuth = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Any();

        if (!requiresAuth)
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = new List<string>()
        });

        return Task.CompletedTask;
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = jwt.SecurityKey,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("dev", policy => policy
        .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();

    var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
    await settings.EnsureSeededAsync();

    var accounts = scope.ServiceProvider.GetRequiredService<AdminAccountService>();
    await accounts.EnsureSeededAsync();
}

app.UseMiddleware<ErrorHandlingMiddleware>();

app.MapOpenApi();
app.MapScalarApiReference("/scalar", options =>
{
    options
        .WithTitle("Nginx SSL Manager API")
        .WithTheme(ScalarTheme.Moon)
        .AddPreferredSecuritySchemes("Bearer");
});

if (app.Environment.IsDevelopment())
{
    app.UseCors("dev");
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

var indexPath = Path.Combine(
    app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
    "index.html");

// SPA 回退：所有非 API 路由都交给前端路由处理。
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    if (!File.Exists(indexPath))
    {
        if (app.Environment.IsDevelopment())
        {
            context.Response.Redirect("/scalar");
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync("前端尚未构建，请先运行 npm run build 并将产物复制到 wwwroot。");
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(indexPath);
});

app.Logger.LogInformation("Nginx SSL Manager started. Data directory: {DataDirectory}", paths.DataDirectory);
app.Run();

public partial class Program;

public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "API error: {Message}", ex.Message);
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = ex.StatusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new
            {
                message = ex.Message,
                output = (ex as NginxException)?.Output
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error for {Method} {Path}", context.Request.Method, context.Request.Path);
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new
            {
                message = "服务器内部错误: " + ex.Message
            });
        }
    }
}
