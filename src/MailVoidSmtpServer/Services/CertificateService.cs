using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailVoidSmtpServer.Services;

public interface ICertificateService
{
    X509Certificate2? GetCertificate();
    bool IsCertificateAvailable();
}

public class CertificateService : ICertificateService
{
    private readonly ILogger<CertificateService> _logger;
    private readonly SmtpServerOptions _options;
    private readonly ILetsEncryptService _letsEncryptService;
    private X509Certificate2? _certificate;
    private bool _certificateLoadAttempted = false;

    public CertificateService(
        ILogger<CertificateService> logger,
        IOptions<SmtpServerOptions> options,
        ILetsEncryptService letsEncryptService)
    {
        _logger = logger;
        _options = options.Value;
        _letsEncryptService = letsEncryptService;
    }

    public X509Certificate2? GetCertificate()
    {
        if (_certificateLoadAttempted)
        {
            return _certificate;
        }

        _certificateLoadAttempted = true;

        if (!_options.EnableSsl)
        {
            _logger.LogInformation("SSL is disabled in configuration");
            return null;
        }

        // Try to get certificate from Let's Encrypt service first
        _certificate = TryGetLetsEncryptCertificate();
        if (_certificate != null)
        {
            return _certificate;
        }

        if (string.IsNullOrWhiteSpace(_options.CertificatePath))
        {
            _logger.LogWarning("SSL is enabled but no certificate path is configured and Let's Encrypt certificate is not available");
            return null;
        }

        try
        {
            if (!File.Exists(_options.CertificatePath))
            {
                _logger.LogError("Certificate file not found at path: {Path}", _options.CertificatePath);
                return null;
            }

            _logger.LogInformation("Loading certificate from: {Path}", _options.CertificatePath);

            // Determine certificate type and load accordingly
            var extension = Path.GetExtension(_options.CertificatePath).ToLowerInvariant();
            
            switch (extension)
            {
                case ".pfx":
                case ".p12":
                    _certificate = LoadPfxCertificate(_options.CertificatePath, _options.CertificatePassword);
                    break;
                case ".pem":
                case ".crt":
                    _certificate = LoadPemCertificate(_options.CertificatePath, _options.CertificatePassword);
                    break;
                default:
                    _logger.LogError("Unsupported certificate format: {Extension}. Supported formats: .pfx, .p12, .pem, .crt", extension);
                    return null;
            }

            if (_certificate != null)
            {
                _logger.LogInformation("Certificate loaded successfully. Subject: {Subject}, Expires: {Expiry}",
                    _certificate.Subject, _certificate.NotAfter);
                
                // Validate certificate
                if (_certificate.NotAfter < DateTime.Now)
                {
                    _logger.LogWarning("Certificate has expired on {ExpiryDate}", _certificate.NotAfter);
                }
                else if (_certificate.NotAfter < DateTime.Now.AddDays(30))
                {
                    _logger.LogWarning("Certificate will expire soon on {ExpiryDate}", _certificate.NotAfter);
                }
            }

            return _certificate;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load certificate from path: {Path}", _options.CertificatePath);
            return null;
        }
    }

    private X509Certificate2? LoadPfxCertificate(string path, string? password)
    {
        try
        {
            return new X509Certificate2(
                path,
                password,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load PFX certificate");
            return null;
        }
    }

    private X509Certificate2? LoadPemCertificate(string certPath, string? keyPassword)
    {
        try
        {
            // For PEM certificates, we need to check if there's a separate key file
            var keyPath = Path.ChangeExtension(certPath, ".key");
            if (!File.Exists(keyPath))
            {
                // Try to find key file with same name but .key extension
                keyPath = certPath.Replace(".crt", ".key").Replace(".pem", ".key");
            }

            if (File.Exists(keyPath))
            {
                _logger.LogInformation("Loading PEM certificate with separate key file: {KeyPath}", keyPath);
                var certPem = File.ReadAllText(certPath);
                var keyPem = File.ReadAllText(keyPath);

                // Create certificate from PEM format
                var cert = X509Certificate2.CreateFromPem(certPem, keyPem);
                
                // Convert to exportable certificate for Windows compatibility
                return new X509Certificate2(cert.Export(X509ContentType.Pfx),
                    (string?)null,
                    X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
            }
            else
            {
                _logger.LogInformation("Loading PEM certificate (expecting combined cert and key)");
                var pemContent = File.ReadAllText(certPath);
                
                // Try to load as combined PEM (certificate and key in same file)
                var cert = X509Certificate2.CreateFromPem(pemContent, pemContent);
                
                return new X509Certificate2(cert.Export(X509ContentType.Pfx),
                    (string?)null,
                    X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load PEM certificate");
            return null;
        }
    }

    public bool IsCertificateAvailable()
    {
        return GetCertificate() != null;
    }

    private X509Certificate2? TryGetLetsEncryptCertificate()
    {
        try
        {
            // Check if Let's Encrypt is configured with domains
            if (!string.IsNullOrWhiteSpace(_options.LetsEncryptDomain))
            {
                _logger.LogInformation("Attempting to get Let's Encrypt certificate for domain: {Domain}", _options.LetsEncryptDomain);
                
                // Use Task.Run to avoid blocking the main thread
                var certificateTask = Task.Run(async () => await _letsEncryptService.GetCertificateAsync(_options.LetsEncryptDomain));
                var certificate = certificateTask.GetAwaiter().GetResult();
                
                if (certificate != null)
                {
                    _logger.LogInformation("Successfully loaded Let's Encrypt certificate for domain: {Domain}", _options.LetsEncryptDomain);
                    return certificate;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Let's Encrypt certificate");
        }

        return null;
    }
}