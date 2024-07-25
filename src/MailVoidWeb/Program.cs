using System.IO.Compression;
using MailVoidCommon;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Serilog;
namespace MailVoidWeb
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateLogger();
            builder.Host.UseSerilog();
            // Add services to the container.
            builder.Services.AddRazorPages();
            // Add controller support

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

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseResponseCaching();
            app.UseResponseCompression();
            app.UseRouting();

            app.UseAuthorization();

            app.MapRazorPages();
            // Map controllers
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
}
