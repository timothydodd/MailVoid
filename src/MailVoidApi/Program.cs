using System.IO.Compression;
using MailVoidCommon;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;

namespace MailVoidApi;

public class Program
{
    [Obsolete]
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllers();
        builder.Services.AddMemoryCache();
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");


        if (connectionString is null)
        {
            throw new InvalidOperationException("Connection string not found");
        }
        builder.Services.AddDbContext<MailDbContext>(options =>
        options.UseMySQL(connectionString));
        builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        builder.Services.AddResponseCaching();
        builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);
        builder.Services.AddResponseCompression(options =>
        {
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.EnableForHttps = true;
        });
        builder.Services.AddSingleton<TimedCache>();
        builder.Services.AddLogging(logging =>
        {
            logging.AddConsole(c =>
            {
                c.Format = Microsoft.Extensions.Logging.Console.ConsoleLoggerFormat.Systemd;
            });

            logging.AddDebug();
        });

        var origins = builder.Configuration.GetValue<string>("CorsOrigins")?.Split(',');
        if (origins is not null)
        {
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowSpecificDomains",
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
        var Authority = builder.Configuration.GetValue<string>("Auth:Authority");
        var Audience = builder.Configuration.GetValue<string>("Auth:Audience");
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(options =>
        {
            options.Authority = Authority;
            options.Audience = Audience;
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
        });

        HealthCheck.AddHealthChecks(builder.Services, connectionString);
        var app = builder.Build();
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        app.UseCors("AllowSpecificDomains");
        app.UseResponseCaching();
        app.UseResponseCompression();
        app.UseAuthentication();
        app.UseAuthorization();
        // Map controllers
        app.MapControllers();
        app.UseHealthChecks("/api/health", new HealthCheckOptions { ResponseWriter = HealthCheck.WriteResponse });


        app.Run();
    }

}
