using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Text;
using MailVoidApi.Authentication;
using MailVoidApi.Data;
using MailVoidApi.Services;
using MailVoidWeb;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace MailVoidApi;

public class Program
{
    [Obsolete]
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
        builder.Services.AddControllers();
        builder.Services.AddMemoryCache();
        // Register HttpContextAccessor
        builder.Services.AddHttpContextAccessor();
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

        if (connectionString is null)
        {
            throw new InvalidOperationException("Connection string not found");
        }

        // Add Entity Framework DbContext
        builder.Services.AddDbContext<MailVoidDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
                mysqlOptions => mysqlOptions.MigrationsAssembly("MailVoidApi")));


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
                ClockSkew = TimeSpan.FromMinutes(5) // Allow 5 minute clock skew
            };
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogError("Authentication failed.", context.Exception);
                    return Task.CompletedTask;
                },
                OnChallenge = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogError("OnChallenge error", context.Error, context.ErrorDescription);
                    return Task.CompletedTask;
                },
                OnMessageReceived = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogInformation("Message received: {0}", context.Token);
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogInformation("Token validated");
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
        builder.Services.AddScoped<IClaimedMailboxService, ClaimedMailboxService>();
        builder.Services.AddScoped<UserManagementService>();
        builder.Services.AddScoped<IMailDataExtractionService, MailDataExtractionService>();
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
                                            .AllowCredentials();
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
                    .AllowAnyHeader());
            });
        }


        HealthCheck.AddHealthChecks(builder.Services, connectionString);
        var app = builder.Build();
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        // Initialize database
        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MailVoidDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            try
            {
                logger.LogInformation("Testing database connection...");
                var canConnect = context.Database.CanConnect();
                logger.LogInformation("Database connection test: {CanConnect}", canConnect);

                logger.LogInformation("Checking applied migrations...");
                var appliedMigrations = context.Database.GetAppliedMigrations().ToList();
                logger.LogInformation("Applied migrations: {AppliedMigrations}",
                    appliedMigrations.Any() ? string.Join(", ", appliedMigrations) : "None");

                logger.LogInformation("Checking migrations assembly info...");
                var migrationsAssembly = context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>();
                logger.LogInformation("Migrations assembly: {Assembly}", migrationsAssembly.Assembly.FullName);

                logger.LogInformation("Checking all migrations...");
                var allMigrations = context.Database.GetMigrations().ToList();
                logger.LogInformation("All available migrations: {AllMigrations}",
                    allMigrations.Any() ? string.Join(", ", allMigrations) : "None");

                logger.LogInformation("Checking for pending migrations...");
                var pendingMigrations = context.Database.GetPendingMigrations().ToList();

                if (pendingMigrations.Any())
                {
                    logger.LogInformation("Found {Count} pending migrations: {Migrations}",
                        pendingMigrations.Count, string.Join(", ", pendingMigrations));

                    // Apply any pending migrations
                    logger.LogInformation("Applying migrations...");
                    context.Database.Migrate();
                    logger.LogInformation("Migrations applied successfully.");
                }
                else
                {
                    logger.LogInformation("Database is up to date, no migrations to apply.");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during database migration");
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
        app.UseHealthChecks("/api/health", new HealthCheckOptions { ResponseWriter = HealthCheck.WriteResponse });
        app.MapFallbackToFile("/index.html");


        app.Run();
    }

}
