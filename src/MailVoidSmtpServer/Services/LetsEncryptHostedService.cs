using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailVoidSmtpServer.Services;

public class LetsEncryptHostedService : BackgroundService
{
    private readonly ILogger<LetsEncryptHostedService> _logger;
    private readonly ILetsEncryptService _letsEncryptService;
    private readonly IServiceProvider _serviceProvider;
    private readonly LetsEncryptOptions _options;

    public LetsEncryptHostedService(
        ILogger<LetsEncryptHostedService> logger,
        ILetsEncryptService letsEncryptService,
        IServiceProvider serviceProvider,
        IOptions<LetsEncryptOptions> options)
    {
        _logger = logger;
        _letsEncryptService = letsEncryptService;
        _serviceProvider = serviceProvider;
        _options = options.Value;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Let's Encrypt service is disabled");
            return;
        }

        if (!_options.Domains.Any())
        {
            _logger.LogWarning("No domains configured for Let's Encrypt");
            return;
        }

        _logger.LogInformation("Starting Let's Encrypt service for domains: {Domains}",
            string.Join(", ", _options.Domains));

        // Initial certificate check and acquisition
        await InitialCertificateSetupAsync(cancellationToken);

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !_options.Domains.Any())
        {
            return;
        }

        _logger.LogInformation("Let's Encrypt renewal service started. Checking every {Hours} hours",
            _options.RenewalCheckIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndRenewCertificatesAsync(stoppingToken);

                // Wait for the next check interval
                await Task.Delay(TimeSpan.FromHours(_options.RenewalCheckIntervalHours), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Let's Encrypt renewal service is stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Let's Encrypt renewal service");

                // Wait a shorter time before retrying on error
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }
    }

    private async Task InitialCertificateSetupAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Performing initial certificate setup");

        foreach (var domain in _options.Domains)
        {
            try
            {
                var isValid = await _letsEncryptService.IsCertificateValidAsync(domain, cancellationToken);

                if (!isValid)
                {
                    _logger.LogInformation("Certificate for domain {Domain} is not valid or missing. Obtaining new certificate", domain);

                    var success = await _letsEncryptService.ObtainCertificateAsync(domain, cancellationToken);

                    if (success)
                    {
                        _logger.LogInformation("Successfully obtained certificate for domain: {Domain}", domain);

                        // Trigger SMTP server restart to load new certificate
                        await RestartSmtpServerAsync();
                    }
                    else
                    {
                        _logger.LogError("Failed to obtain certificate for domain: {Domain}", domain);
                    }
                }
                else
                {
                    var expiration = await _letsEncryptService.GetCertificateExpirationAsync(domain, cancellationToken);
                    _logger.LogInformation("Certificate for domain {Domain} is valid (expires: {Expiry})",
                        domain, expiration);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting up certificate for domain: {Domain}", domain);
            }
        }
    }

    private async Task CheckAndRenewCertificatesAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checking certificates for renewal");

        var renewalOccurred = false;

        foreach (var domain in _options.Domains)
        {
            try
            {
                var expiration = await _letsEncryptService.GetCertificateExpirationAsync(domain, cancellationToken);

                if (expiration.HasValue)
                {
                    var daysUntilExpiry = (expiration.Value - DateTime.UtcNow).TotalDays;

                    _logger.LogDebug("Certificate for domain {Domain} expires in {Days} days",
                        domain, Math.Round(daysUntilExpiry, 1));

                    if (daysUntilExpiry <= _options.RenewalDaysBeforeExpiry)
                    {
                        _logger.LogInformation("Certificate for domain {Domain} expires in {Days} days. Attempting renewal",
                            domain, Math.Round(daysUntilExpiry, 1));

                        var success = await _letsEncryptService.RenewCertificateAsync(domain, cancellationToken);

                        if (success)
                        {
                            _logger.LogInformation("Successfully renewed certificate for domain: {Domain}", domain);
                            renewalOccurred = true;
                        }
                        else
                        {
                            _logger.LogError("Failed to renew certificate for domain: {Domain}", domain);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Could not determine expiration date for certificate of domain: {Domain}", domain);

                    // Try to obtain a new certificate if we can't determine expiration
                    var success = await _letsEncryptService.ObtainCertificateAsync(domain, cancellationToken);

                    if (success)
                    {
                        _logger.LogInformation("Successfully obtained new certificate for domain: {Domain}", domain);
                        renewalOccurred = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking/renewing certificate for domain: {Domain}", domain);
            }
        }

        // If any certificate was renewed, restart the SMTP server to load the new certificates
        if (renewalOccurred)
        {
            _logger.LogInformation("Certificates were renewed. Restarting SMTP server to load new certificates");
            await RestartSmtpServerAsync();
        }
    }

    private async Task RestartSmtpServerAsync()
    {
        try
        {
            _logger.LogInformation("Restarting SMTP server to load updated certificates");

            // Get the SMTP server service and restart it
            var scope = _serviceProvider.CreateScope();
            var smtpService = scope.ServiceProvider.GetRequiredService<SmtpServerService>();

            // Stop the current server
            await smtpService.StopAsync(CancellationToken.None);

            // Wait a moment for cleanup
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Start the server again
            await smtpService.StartAsync(CancellationToken.None);

            _logger.LogInformation("SMTP server restarted successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restarting SMTP server");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Let's Encrypt service");
        await base.StopAsync(cancellationToken);
    }
}
