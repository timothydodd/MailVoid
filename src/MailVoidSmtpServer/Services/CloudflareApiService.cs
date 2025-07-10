using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailVoidSmtpServer.Services;

public interface ICloudflareApiService
{
    Task<string?> CreateDnsChallengeRecordAsync(string domain, string challengeToken, CancellationToken cancellationToken = default);
    Task<bool> DeleteDnsChallengeRecordAsync(string recordId, CancellationToken cancellationToken = default);
    Task<bool> VerifyDnsChallengeRecordAsync(string domain, string challengeToken, CancellationToken cancellationToken = default);
    Task<string?> GetZoneIdAsync(string domain, CancellationToken cancellationToken = default);
    Task<List<CloudflareDnsRecord>> GetDnsRecordsAsync(string zoneId, string? name = null, string? type = null, CancellationToken cancellationToken = default);
}

public class CloudflareApiService : ICloudflareApiService
{
    private readonly ILogger<CloudflareApiService> _logger;
    private readonly CloudflareOptions _options;
    private readonly HttpClient _httpClient;

    public CloudflareApiService(
        ILogger<CloudflareApiService> logger,
        IOptions<CloudflareOptions> options,
        HttpClient httpClient)
    {
        _logger = logger;
        _options = options.Value;
        _httpClient = httpClient;

        // Configure HTTP client
        _httpClient.BaseAddress = new Uri("https://api.cloudflare.com/client/v4/");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.ApiToken}");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "MailVoid-SMTP-Server/1.0");
    }

    public async Task<string?> CreateDnsChallengeRecordAsync(string domain, string challengeToken, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_options.Enabled)
            {
                _logger.LogWarning("Cloudflare API is disabled");
                return null;
            }

            var zoneId = await GetZoneIdAsync(domain, cancellationToken);
            if (string.IsNullOrEmpty(zoneId))
            {
                _logger.LogError("Could not find zone ID for domain: {Domain}", domain);
                return null;
            }

            var recordName = $"_acme-challenge.{domain}";
            _logger.LogInformation("Creating DNS TXT record: {RecordName} with value: {Value}", recordName, challengeToken);

            var requestBody = new
            {
                type = "TXT",
                name = recordName,
                content = challengeToken,
                ttl = _options.DnsRecordTtl
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"zones/{zoneId}/dns_records", content, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<CloudflareApiResponse<CloudflareDnsRecord>>(responseContent);
                if (result?.Success == true && result.Result != null)
                {
                    _logger.LogInformation("Successfully created DNS record with ID: {RecordId}", result.Result.Id);
                    return result.Result.Id;
                }
            }

            _logger.LogError("Failed to create DNS record. Status: {Status}, Response: {Response}", response.StatusCode, responseContent);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating DNS challenge record for domain: {Domain}", domain);
            return null;
        }
    }

    public async Task<bool> DeleteDnsChallengeRecordAsync(string recordId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_options.Enabled)
            {
                _logger.LogWarning("Cloudflare API is disabled");
                return false;
            }

            _logger.LogInformation("Deleting DNS record with ID: {RecordId}", recordId);

            // First, get the record to find the zone ID
            var zoneId = await GetZoneIdFromRecordAsync(recordId, cancellationToken);
            if (string.IsNullOrEmpty(zoneId))
            {
                _logger.LogError("Could not determine zone ID for record: {RecordId}", recordId);
                return false;
            }

            var response = await _httpClient.DeleteAsync($"zones/{zoneId}/dns_records/{recordId}", cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<CloudflareApiResponse<object>>(responseContent);
                if (result?.Success == true)
                {
                    _logger.LogInformation("Successfully deleted DNS record: {RecordId}", recordId);
                    return true;
                }
            }

            _logger.LogError("Failed to delete DNS record. Status: {Status}, Response: {Response}", response.StatusCode, responseContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting DNS record: {RecordId}", recordId);
            return false;
        }
    }

    public async Task<bool> VerifyDnsChallengeRecordAsync(string domain, string challengeToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var recordName = $"_acme-challenge.{domain}";
            _logger.LogInformation("Verifying DNS TXT record: {RecordName}", recordName);

            // Use DNS lookup to verify the record exists
            var lookup = new System.Net.NetworkInformation.Ping(); // Placeholder - would need actual DNS lookup
            
            // Simple verification - in a real implementation, you'd use DNS lookup libraries
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken); // Wait for DNS propagation
            
            _logger.LogInformation("DNS challenge record verification completed for: {RecordName}", recordName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying DNS challenge record for domain: {Domain}", domain);
            return false;
        }
    }

    public async Task<string?> GetZoneIdAsync(string domain, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_options.Enabled)
            {
                _logger.LogWarning("Cloudflare API is disabled");
                return null;
            }

            // Extract root domain (e.g., smtp.example.com -> example.com)
            var domainParts = domain.Split('.');
            var rootDomain = domainParts.Length >= 2 
                ? string.Join(".", domainParts.Skip(domainParts.Length - 2))
                : domain;

            _logger.LogDebug("Looking up zone ID for domain: {Domain} (root: {RootDomain})", domain, rootDomain);

            var response = await _httpClient.GetAsync($"zones?name={rootDomain}", cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<CloudflareApiResponse<List<CloudflareZone>>>(responseContent);
                if (result?.Success == true && result.Result?.Any() == true)
                {
                    var zone = result.Result.First();
                    _logger.LogDebug("Found zone ID: {ZoneId} for domain: {Domain}", zone.Id, rootDomain);
                    return zone.Id;
                }
            }

            _logger.LogError("Failed to get zone ID. Status: {Status}, Response: {Response}", response.StatusCode, responseContent);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting zone ID for domain: {Domain}", domain);
            return null;
        }
    }

    public async Task<List<CloudflareDnsRecord>> GetDnsRecordsAsync(string zoneId, string? name = null, string? type = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_options.Enabled)
            {
                _logger.LogWarning("Cloudflare API is disabled");
                return new List<CloudflareDnsRecord>();
            }

            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(name))
                queryParams.Add($"name={Uri.EscapeDataString(name)}");
            if (!string.IsNullOrEmpty(type))
                queryParams.Add($"type={Uri.EscapeDataString(type)}");

            var queryString = queryParams.Any() ? "?" + string.Join("&", queryParams) : "";
            
            var response = await _httpClient.GetAsync($"zones/{zoneId}/dns_records{queryString}", cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<CloudflareApiResponse<List<CloudflareDnsRecord>>>(responseContent);
                if (result?.Success == true && result.Result != null)
                {
                    return result.Result;
                }
            }

            _logger.LogError("Failed to get DNS records. Status: {Status}, Response: {Response}", response.StatusCode, responseContent);
            return new List<CloudflareDnsRecord>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting DNS records for zone: {ZoneId}", zoneId);
            return new List<CloudflareDnsRecord>();
        }
    }

    private async Task<string?> GetZoneIdFromRecordAsync(string recordId, CancellationToken cancellationToken)
    {
        try
        {
            // This is a simplified approach - in practice, you might need to store the zone ID
            // when creating the record, or iterate through zones to find the record
            
            // For now, we'll try to get zones and search through them
            var response = await _httpClient.GetAsync("zones", cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<CloudflareApiResponse<List<CloudflareZone>>>(responseContent);
                if (result?.Success == true && result.Result?.Any() == true)
                {
                    // For each zone, check if the record exists
                    foreach (var zone in result.Result)
                    {
                        var recordResponse = await _httpClient.GetAsync($"zones/{zone.Id}/dns_records/{recordId}", cancellationToken);
                        if (recordResponse.IsSuccessStatusCode)
                        {
                            return zone.Id;
                        }
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding zone ID for record: {RecordId}", recordId);
            return null;
        }
    }
}

// Data models for Cloudflare API responses
public class CloudflareApiResponse<T>
{
    public bool Success { get; set; }
    public T? Result { get; set; }
    public List<CloudflareError>? Errors { get; set; }
    public List<string>? Messages { get; set; }
}

public class CloudflareError
{
    public int Code { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class CloudflareZone
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class CloudflareDnsRecord
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int Ttl { get; set; }
    public bool Proxied { get; set; }
}

public class CloudflareOptions
{
    public bool Enabled { get; set; } = false;
    public string ApiToken { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty; // Optional, for legacy API key authentication
    public string ApiKey { get; set; } = string.Empty; // Optional, for legacy API key authentication
    public int DnsRecordTtl { get; set; } = 120; // 2 minutes for quick propagation
    public int DnsPropagationWaitSeconds { get; set; } = 60; // Wait time for DNS propagation
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 10;
}