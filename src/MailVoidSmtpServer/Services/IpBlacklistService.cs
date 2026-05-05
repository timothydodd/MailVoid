using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using SmtpServer;
using SmtpServer.Net;

namespace MailVoidSmtpServer.Services;

public interface IIpBlacklistService
{
    bool IsBlacklisted(string ip);
    bool IsBlacklisted(ISessionContext context);
    void Add(string ip, string reason);
    string? GetRemoteIp(ISessionContext context);
}

public class IpBlacklistService : IIpBlacklistService
{
    private readonly ILogger<IpBlacklistService> _logger;
    private readonly ConcurrentDictionary<string, byte> _blacklist = new();
    private readonly string _filePath;
    private readonly object _fileLock = new();

    public IpBlacklistService(ILogger<IpBlacklistService> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(AppContext.BaseDirectory, "ip-blacklist.txt");
        Load();
    }

    public bool IsBlacklisted(string ip) => !string.IsNullOrEmpty(ip) && _blacklist.ContainsKey(ip);

    public bool IsBlacklisted(ISessionContext context)
    {
        var ip = GetRemoteIp(context);
        return ip != null && IsBlacklisted(ip);
    }

    public string? GetRemoteIp(ISessionContext context)
    {
        if (context.Properties.TryGetValue(EndpointListener.RemoteEndPointKey, out var ep) && ep is IPEndPoint ipep)
        {
            return ipep.Address.ToString();
        }
        return null;
    }

    public void Add(string ip, string reason)
    {
        if (string.IsNullOrWhiteSpace(ip) || ip == "unknown") return;

        if (_blacklist.TryAdd(ip, 0))
        {
            _logger.LogWarning("Blacklisted IP {Ip} - Reason: {Reason}", ip, reason);
            Persist(ip, reason);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;

            foreach (var line in File.ReadAllLines(_filePath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                // Support "ip" or "ip\treason" / "ip,reason"
                var ip = trimmed.Split(new[] { '\t', ',', ' ' }, 2)[0].Trim();
                if (!string.IsNullOrEmpty(ip))
                {
                    _blacklist.TryAdd(ip, 0);
                }
            }

            _logger.LogInformation("Loaded {Count} blacklisted IPs from {Path}", _blacklist.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load IP blacklist from {Path}", _filePath);
        }
    }

    private void Persist(string ip, string reason)
    {
        try
        {
            lock (_fileLock)
            {
                var line = $"{ip}\t{DateTime.UtcNow:O}\t{reason.Replace('\t', ' ').Replace('\n', ' ')}{Environment.NewLine}";
                File.AppendAllText(_filePath, line);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist blacklisted IP {Ip}", ip);
        }
    }
}
