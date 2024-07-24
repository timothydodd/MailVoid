using System.IO.Compression;
using MailVoidCommon;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace MailVoidApi;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        Log.Logger = new LoggerConfiguration()
.WriteTo.Console()
.CreateLogger();
        builder.Host.UseSerilog();
        // Add controller support
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
        HealthCheck.AddHealthChecks(builder.Services, connectionString);
        var app = builder.Build();

        app.UseStaticFiles();
        app.UseResponseCaching();
        app.UseResponseCompression();
        app.UseAuthorization();
        // Map controllers
        app.MapControllers();
        app.UseHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthCheck.WriteResponse });


        try
        {
            Log.Information("Starting web host");
            app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

}
