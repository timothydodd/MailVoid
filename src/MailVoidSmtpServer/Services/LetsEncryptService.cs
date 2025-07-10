using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailVoidSmtpServer.Services;

public interface ILetsEncryptService
{
    Task<bool> ObtainCertificateAsync(string domain, CancellationToken cancellationToken = default);
    Task<bool> RenewCertificateAsync(string domain, CancellationToken cancellationToken = default);
    Task<X509Certificate2?> GetCertificateAsync(string domain, CancellationToken cancellationToken = default);
    Task<bool> IsCertificateValidAsync(string domain, CancellationToken cancellationToken = default);
    Task<DateTime?> GetCertificateExpirationAsync(string domain, CancellationToken cancellationToken = default);
}

public class LetsEncryptService : ILetsEncryptService
{
    private readonly ILogger<LetsEncryptService> _logger;
    private readonly LetsEncryptOptions _options;
    private readonly ICloudflareApiService _cloudflareApiService;

    public LetsEncryptService(
        ILogger<LetsEncryptService> logger,
        IOptions<LetsEncryptOptions> options,
        ICloudflareApiService cloudflareApiService)
    {
        _logger = logger;
        _options = options.Value;
        _cloudflareApiService = cloudflareApiService;
    }

    public async Task<bool> ObtainCertificateAsync(string domain, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Let's Encrypt is disabled in configuration");
            return false;
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            _logger.LogError("Domain is required to obtain certificate");
            return false;
        }

        try
        {
            _logger.LogInformation("Obtaining Let's Encrypt certificate for domain: {Domain}", domain);

            // Check if certificate already exists and is valid
            if (await IsCertificateValidAsync(domain, cancellationToken))
            {
                _logger.LogInformation("Valid certificate already exists for domain: {Domain}", domain);
                return true;
            }

            // Create certificate directory if it doesn't exist
            var certDir = Path.Combine(_options.CertificateDirectory, domain);
            Directory.CreateDirectory(certDir);

            // Handle DNS challenge with Cloudflare if configured
            if (_options.ChallengeMethod.ToLowerInvariant() == "dns-cloudflare")
            {
                var result = await ObtainCertificateWithCloudflareAsync(domain, cancellationToken);
                if (result)
                {
                    await ConvertToPfxAsync(domain, cancellationToken);
                    _logger.LogInformation("Successfully obtained certificate for domain: {Domain}", domain);
                }
                return result;
            }

            // Build certbot command for other challenge methods
            var certbotArgs = BuildCertbotCommand(domain, isRenewal: false);

            // Run certbot
            var resultRunBot = await RunCertbotAsync(certbotArgs, cancellationToken);

            if (resultRunBot)
            {
                // Convert to PFX format if needed
                await ConvertToPfxAsync(domain, cancellationToken);
                _logger.LogInformation("Successfully obtained certificate for domain: {Domain}", domain);
            }
            else
            {
                _logger.LogError("Failed to obtain certificate for domain: {Domain}", domain);
            }

            return resultRunBot;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obtaining certificate for domain: {Domain}", domain);
            return false;
        }
    }

    public async Task<bool> RenewCertificateAsync(string domain, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Let's Encrypt is disabled in configuration");
            return false;
        }

        try
        {
            _logger.LogInformation("Renewing Let's Encrypt certificate for domain: {Domain}", domain);

            // Handle DNS challenge with Cloudflare if configured
            if (_options.ChallengeMethod.ToLowerInvariant() == "dns-cloudflare")
            {
                var result = await RenewCertificateWithCloudflareAsync(domain, cancellationToken);
                if (result)
                {
                    await ConvertToPfxAsync(domain, cancellationToken);
                    _logger.LogInformation("Successfully renewed certificate for domain: {Domain}", domain);
                }
                return result;
            }

            // Build certbot renew command for other challenge methods
            var certbotArgs = BuildCertbotCommand(domain, isRenewal: true);

            // Run certbot renewal
            var runCertResult = await RunCertbotAsync(certbotArgs, cancellationToken);

            if (runCertResult)
            {
                // Convert to PFX format if needed
                await ConvertToPfxAsync(domain, cancellationToken);
                _logger.LogInformation("Successfully renewed certificate for domain: {Domain}", domain);
            }
            else
            {
                _logger.LogWarning("Certificate renewal for domain {Domain} was not needed or failed", domain);
            }

            return runCertResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error renewing certificate for domain: {Domain}", domain);
            return false;
        }
    }

    public async Task<X509Certificate2?> GetCertificateAsync(string domain, CancellationToken cancellationToken = default)
    {
        try
        {
            var pfxPath = GetPfxPath(domain);

            if (!File.Exists(pfxPath))
            {
                _logger.LogWarning("Certificate file not found for domain: {Domain} at path: {Path}", domain, pfxPath);
                return null;
            }

            return await Task.Run(() =>
            {
                try
                {
                    return new X509Certificate2(
                        pfxPath,
                        _options.CertificatePassword,
                        X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load certificate for domain: {Domain}", domain);
                    return null;
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting certificate for domain: {Domain}", domain);
            return null;
        }
    }

    public async Task<bool> IsCertificateValidAsync(string domain, CancellationToken cancellationToken = default)
    {
        try
        {
            var certificate = await GetCertificateAsync(domain, cancellationToken);
            if (certificate == null)
                return false;

            var now = DateTime.UtcNow;
            var isValid = certificate.NotBefore <= now && certificate.NotAfter > now.AddDays(_options.RenewalDaysBeforeExpiry);

            _logger.LogDebug("Certificate for domain {Domain} is valid: {IsValid} (expires: {Expiry})",
                domain, isValid, certificate.NotAfter);

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking certificate validity for domain: {Domain}", domain);
            return false;
        }
    }

    public async Task<DateTime?> GetCertificateExpirationAsync(string domain, CancellationToken cancellationToken = default)
    {
        try
        {
            var certificate = await GetCertificateAsync(domain, cancellationToken);
            return certificate?.NotAfter;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting certificate expiration for domain: {Domain}", domain);
            return null;
        }
    }

    private string BuildCertbotCommand(string domain, bool isRenewal)
    {
        var args = new List<string>();

        if (isRenewal)
        {
            args.Add("renew");
            args.Add("--quiet");
            args.Add($"--cert-name {domain}");
        }
        else
        {
            args.Add("certonly");
            args.Add("--non-interactive");
            args.Add("--agree-tos");
            args.Add($"--email {_options.Email}");

            // Choose challenge method
            switch (_options.ChallengeMethod.ToLowerInvariant())
            {
                case "http":
                    args.Add("--standalone");
                    if (_options.HttpPort.HasValue)
                        args.Add($"--http-01-port {_options.HttpPort}");
                    break;
                case "dns":
                    args.Add("--manual");
                    args.Add("--preferred-challenges dns");
                    break;
                case "dns-cloudflare":
                    args.Add("--dns-cloudflare");
                    args.Add("--dns-cloudflare-credentials");
                    args.Add(GetCloudflareCredentialsPath());
                    break;
                case "webroot":
                    args.Add("--webroot");
                    args.Add($"--webroot-path {_options.WebrootPath}");
                    break;
                default:
                    args.Add("--standalone");
                    break;
            }

            args.Add($"--domains {domain}");

            if (!string.IsNullOrWhiteSpace(_options.CertificateDirectory))
            {
                args.Add($"--work-dir {_options.CertificateDirectory}/work");
                args.Add($"--config-dir {_options.CertificateDirectory}/config");
                args.Add($"--logs-dir {_options.CertificateDirectory}/logs");
            }
        }

        return string.Join(" ", args);
    }

    private async Task<bool> RunCertbotAsync(string arguments, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _options.CertbotPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _logger.LogDebug("Running certbot with arguments: {Arguments}", arguments);

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogError("Failed to start certbot process");
                return false;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("Certbot completed successfully");
                if (!string.IsNullOrWhiteSpace(output))
                    _logger.LogDebug("Certbot output: {Output}", output);
                return true;
            }
            else
            {
                _logger.LogError("Certbot failed with exit code: {ExitCode}", process.ExitCode);
                if (!string.IsNullOrWhiteSpace(error))
                    _logger.LogError("Certbot error: {Error}", error);
                if (!string.IsNullOrWhiteSpace(output))
                    _logger.LogDebug("Certbot output: {Output}", output);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running certbot");
            return false;
        }
    }

    private async Task<bool> ConvertToPfxAsync(string domain, CancellationToken cancellationToken)
    {
        try
        {
            var letsEncryptDir = Path.Combine(_options.CertificateDirectory, "config/live", domain);
            var certPath = Path.Combine(letsEncryptDir, "fullchain.pem");
            var keyPath = Path.Combine(letsEncryptDir, "privkey.pem");
            var pfxPath = GetPfxPath(domain);

            if (!File.Exists(certPath) || !File.Exists(keyPath))
            {
                _logger.LogWarning("Certificate or key file not found for domain: {Domain}", domain);
                return false;
            }

            // Create PFX directory if it doesn't exist
            Directory.CreateDirectory(Path.GetDirectoryName(pfxPath)!);

            // Convert PEM to PFX using OpenSSL
            var opensslArgs = $"pkcs12 -export -out \"{pfxPath}\" -inkey \"{keyPath}\" -in \"{certPath}\" -password pass:{_options.CertificatePassword}";

            var startInfo = new ProcessStartInfo
            {
                FileName = "openssl",
                Arguments = opensslArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogError("Failed to start OpenSSL process");
                return false;
            }

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("Successfully converted certificate to PFX format for domain: {Domain}", domain);
                return true;
            }
            else
            {
                var error = await process.StandardError.ReadToEndAsync();
                _logger.LogError("OpenSSL conversion failed for domain {Domain}: {Error}", domain, error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting certificate to PFX for domain: {Domain}", domain);
            return false;
        }
    }

    private string GetPfxPath(string domain)
    {
        return Path.Combine(_options.CertificateDirectory, "pfx", $"{domain}.pfx");
    }

    private async Task<bool> ObtainCertificateWithCloudflareAsync(string domain, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting Cloudflare DNS challenge for domain: {Domain}", domain);

            // Create Cloudflare credentials file
            var credentialsPath = await CreateCloudflareCredentialsFileAsync();
            if (string.IsNullOrEmpty(credentialsPath))
            {
                _logger.LogError("Failed to create Cloudflare credentials file");
                return false;
            }

            // Build certbot command with Cloudflare DNS plugin
            var args = new List<string>
            {
                "certonly",
                "--non-interactive",
                "--agree-tos",
                $"--email {_options.Email}",
                "--dns-cloudflare",
                $"--dns-cloudflare-credentials {credentialsPath}",
                $"--domains {domain}"
            };

            if (!string.IsNullOrWhiteSpace(_options.CertificateDirectory))
            {
                args.Add($"--work-dir {_options.CertificateDirectory}/work");
                args.Add($"--config-dir {_options.CertificateDirectory}/config");
                args.Add($"--logs-dir {_options.CertificateDirectory}/logs");
            }

            var certbotArgs = string.Join(" ", args);
            var result = await RunCertbotAsync(certbotArgs, cancellationToken);

            // Clean up credentials file
            if (File.Exists(credentialsPath))
            {
                File.Delete(credentialsPath);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obtaining certificate with Cloudflare DNS challenge for domain: {Domain}", domain);
            return false;
        }
    }

    private async Task<bool> RenewCertificateWithCloudflareAsync(string domain, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Renewing certificate with Cloudflare DNS challenge for domain: {Domain}", domain);

            // Create Cloudflare credentials file
            var credentialsPath = await CreateCloudflareCredentialsFileAsync();
            if (string.IsNullOrEmpty(credentialsPath))
            {
                _logger.LogError("Failed to create Cloudflare credentials file");
                return false;
            }

            // Build certbot renewal command
            var args = new List<string>
            {
                "renew",
                "--quiet",
                $"--cert-name {domain}",
                "--dns-cloudflare",
                $"--dns-cloudflare-credentials {credentialsPath}"
            };

            var certbotArgs = string.Join(" ", args);
            var result = await RunCertbotAsync(certbotArgs, cancellationToken);

            // Clean up credentials file
            if (File.Exists(credentialsPath))
            {
                File.Delete(credentialsPath);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error renewing certificate with Cloudflare DNS challenge for domain: {Domain}", domain);
            return false;
        }
    }

    private async Task<string?> CreateCloudflareCredentialsFileAsync()
    {
        try
        {
            var tempDir = Path.Combine(_options.CertificateDirectory, "temp");
            Directory.CreateDirectory(tempDir);

            var credentialsPath = Path.Combine(tempDir, $"cloudflare-{Guid.NewGuid()}.ini");

            var credentialsContent = $@"# Cloudflare API credentials for certbot-dns-cloudflare
dns_cloudflare_api_token = {_options.CloudflareApiToken}
";

            await File.WriteAllTextAsync(credentialsPath, credentialsContent);

            // Set secure permissions (readable only by owner)
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                var chmod = new ProcessStartInfo("chmod", $"600 {credentialsPath}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(chmod);
                await process?.WaitForExitAsync()!;
            }

            _logger.LogDebug("Created Cloudflare credentials file: {Path}", credentialsPath);
            return credentialsPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Cloudflare credentials file");
            return null;
        }
    }

    private string GetCloudflareCredentialsPath()
    {
        return Path.Combine(_options.CertificateDirectory, "cloudflare.ini");
    }
}

public class LetsEncryptOptions
{
    public bool Enabled { get; set; } = false;
    public string Email { get; set; } = string.Empty;
    public string CertbotPath { get; set; } = "certbot";
    public string CertificateDirectory { get; set; } = "/etc/letsencrypt";
    public string CertificatePassword { get; set; } = "mailvoid-ssl";
    public string ChallengeMethod { get; set; } = "http"; // http, dns, webroot, dns-cloudflare
    public string WebrootPath { get; set; } = "/var/www/html";
    public int? HttpPort { get; set; } = 80;
    public int RenewalDaysBeforeExpiry { get; set; } = 30;
    public int RenewalCheckIntervalHours { get; set; } = 24;
    public List<string> Domains { get; set; } = new();
    public string CloudflareApiToken { get; set; } = string.Empty; // Required for dns-cloudflare challenge
}
