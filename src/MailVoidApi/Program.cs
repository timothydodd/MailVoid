using System.IO.Compression;
using MailVoidCommon;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;

namespace MailVoidApi;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);


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
        builder.Services.AddLogging(configure => configure.AddSimpleConsole(options =>
        {
            options.IncludeScopes = false;
            options.SingleLine = true;
            options.TimestampFormat = "hh:mm:ss ";
        }));
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
        });
        // Add controller support
        builder.Services.AddControllers();
        HealthCheck.AddHealthChecks(builder.Services, connectionString);
        var app = builder.Build();
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        app.UseCors("AllowSpecificDomains");
        app.UseResponseCaching();
        app.UseResponseCompression();
        app.UseAuthorization();
        app.UseAuthentication();
        // Map controllers
        app.MapControllers();
        app.UseHealthChecks("/api/health", new HealthCheckOptions { ResponseWriter = HealthCheck.WriteResponse });


        app.Run();
    }

}
