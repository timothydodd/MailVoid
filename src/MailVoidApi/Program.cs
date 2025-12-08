using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Serialization;
using MailVoidApi.Authentication;
using MailVoidApi.Data;
using MailVoidApi.Hubs;
using MailVoidApi.Middleware;
using MailVoidApi.Services;
using MailVoidWeb;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.IdentityModel.Tokens;

namespace MailVoidApi;

public class Program
{

    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddRequestDecompression();
        builder.Services.AddResponseCompression(options =>
        {
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
            {
            "application/json"
        });
        });
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolver = new ApiJsonSerializerContext();

        });

        builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
        builder.Services.AddHostedService<BackgroundWorkerService>();
        builder.Services.AddHostedService<MailCleanupService>();
        builder.Services.AddHostedService<WebhookCleanupService>();
        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
        builder.Services.AddSignalR();
        builder.Services.AddMemoryCache();
        // Register HttpContextAccessor
        builder.Services.AddHttpContextAccessor();
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

        if (connectionString is null)
        {
            throw new InvalidOperationException("Connection string not found");
        }

        // Add OrmLite Database Service
        builder.Services.AddSingleton<IDatabaseService, DatabaseService>();

        builder.Services.AddScoped<DatabaseInitializer>();
        builder.Services.AddSingleton<PasswordService>();
        builder.Services.AddScoped<RefreshTokenService>();
        builder.Services.AddSingleton<AuthService>();

        // Prevent automatic claim type mapping
        JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
        JwtSecurityTokenHandler.DefaultOutboundClaimTypeMap.Clear();

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            var jwtSettings = builder.Configuration.GetSection("JwtSettings");
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings["Issuer"],
                ValidAudience = jwtSettings["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Secret"])),
                ClockSkew = TimeSpan.FromMinutes(5), // Allow 5 minute clock skew
                NameClaimType = "sub"
            };
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogError(context.Exception, "JWT Authentication failed for {Path}", context.Request.Path);
                    return Task.CompletedTask;
                },
                OnChallenge = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    if (!string.IsNullOrEmpty(context.Error))
                    {
                        logger.LogWarning("JWT Challenge - Error: {Error}, Description: {ErrorDescription}, Path: {Path}",
                            context.Error, context.ErrorDescription, context.Request.Path);
                    }
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogDebug("JWT Token validated successfully for user {User} on path {Path}",
                        context.Principal?.Identity?.Name, context.Request.Path);
                    return Task.CompletedTask;
                },
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        })
        .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>("ApiKey", options => { });

        // Add authorization policies
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("ApiKey", policy =>
            {
                policy.AuthenticationSchemes.Add("ApiKey");
                policy.RequireAuthenticatedUser();
            });
        });

        builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        builder.Services.AddResponseCaching();
        builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);
        builder.Services.AddScoped<IMailGroupService, MailGroupService>();
        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<UserManagementService>();
        builder.Services.AddScoped<IMailDataExtractionService, MailDataExtractionService>();
        builder.Services.AddScoped<IWebhookBucketService, WebhookBucketService>();
        builder.Services.AddSingleton<TimedCache>();
        builder.Services.AddLogging(logging =>
        {
            logging.AddSimpleConsole(c =>
            {
                c.SingleLine = true;
                c.IncludeScopes = false;
                c.TimestampFormat = "HH:mm:ss ";
            });

            logging.AddDebug();
            logging.SetMinimumLevel(LogLevel.Information);
        });
        builder.Services.Configure<BackgroundTaskQueueOptions>(options =>
        {
            options.Capacity = 100; // Set a default or load from configuration
        });


        var origins = builder.Configuration.GetValue<string>("CorsOrigins")?.Split(',');
        if (origins is not null)
        {
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("MailVoidOrigins",
                    policy =>
                    {
                        policy.WithOrigins(origins) // Specify the allowed domains
                                            .AllowAnyHeader()
                                            .AllowAnyMethod()
                                            .SetIsOriginAllowed(x => true)
                                            .AllowCredentials()
                                            .WithExposedHeaders("X-Total-Count");
                    });
            });
        }
        else
        {
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("MailVoidOrigins",
                builder => builder
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .WithExposedHeaders("X-Total-Count"));
            });
        }


        HealthCheck.AddHealthChecks(builder.Services, connectionString);
        var app = builder.Build();

        // Add request logging middleware
        app.UseMiddleware<RequestLoggingMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseMiddleware<ExceptionHandlingMiddleware>();
        }

        // Initialize database tables and seed data
        using (var scope = app.Services.CreateScope())
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();

            try
            {
                logger.LogInformation("Initializing database...");
                await dbService.InitializeAsync();
                logger.LogInformation("Database initialized successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during database initialization");
                throw;
            }

            var dbInitializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
            await dbInitializer.SeedDefaultData();
        }
        app.UseCors("MailVoidOrigins");

        app.UseResponseCaching();
        app.UseResponseCompression();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapControllers();
        app.MapHub<MailNotificationHub>("/hubs/mail");
        app.UseHealthChecks("/api/health", new HealthCheckOptions { ResponseWriter = HealthCheck.WriteResponse });
        app.MapFallbackToFile("/index.html");


        app.Run();
    }

}
