// Package smtpd is the Go port of MailVoidSmtpServer: a receive-only SMTP server that
// spools inbound mail to disk and forwards it to one or more MailVoid API webhooks.
package smtpd

import (
	"strconv"

	"mailvoid/internal/config"
)

// Options mirror the configuration sections (and defaults) of the .NET server so
// existing appsettings files and environment variables keep working unchanged.

type ServerOptions struct {
	Port                int
	TestPort            int
	SslPort             int
	StartTlsPort        int
	Name                string
	MaxMessageSize      int
	CertificatePath     string
	CertificatePassword string
	EnableSsl           bool
	TlsProtocols        string
	LetsEncryptDomain   string
	// Abuse limits for the public port (new in the Go server; <= 0 means unlimited).
	MaxRecipients       int
	MaxConnections      int
	MaxConnectionsPerIp int
}

type APITarget struct {
	Name            string
	BaseUrl         string
	WebhookEndpoint string
	ApiKey          string
	Enabled         bool
}

type QueueOptions struct {
	MaxRetryAttempts        int
	BaseRetryDelaySeconds   int
	MaxConcurrentProcessing int
	MaxQueueDepth           int
	SpoolDirectory          string
}

type MonitoringOptions struct {
	ReportingIntervalSeconds int
	HighQueueDepthThreshold  int
	LogWhenEmpty             bool
}

type FilterOptions struct {
	AllowedDomains []string
	BlockedDomains []string
}

type LetsEncryptOptions struct {
	Enabled                   bool
	Email                     string
	CertbotPath               string
	CertificateDirectory      string
	CertificatePassword       string
	ChallengeMethod           string
	WebrootPath               string
	HttpPort                  *int
	RenewalDaysBeforeExpiry   int
	RenewalCheckIntervalHours int
	Domains                   []string
	CloudflareApiToken        string
}

type Options struct {
	Server      ServerOptions
	Targets     []APITarget
	Queue       QueueOptions
	Monitoring  MonitoringOptions
	Filter      FilterOptions
	LetsEncrypt LetsEncryptOptions
}

func LoadOptions(c *config.Config) Options {
	o := Options{
		Server: ServerOptions{
			Port:                c.Int("SmtpServer:Port", 25),
			TestPort:            c.Int("SmtpServer:TestPort", 2580),
			SslPort:             c.Int("SmtpServer:SslPort", 465),
			StartTlsPort:        c.Int("SmtpServer:StartTlsPort", 587),
			Name:                c.String("SmtpServer:Name", "MailVoid SMTP Server"),
			MaxMessageSize:      c.Int("SmtpServer:MaxMessageSize", 10*1024*1024),
			CertificatePath:     c.Get("SmtpServer:CertificatePath"),
			CertificatePassword: c.Get("SmtpServer:CertificatePassword"),
			EnableSsl:           c.Bool("SmtpServer:EnableSsl", false),
			TlsProtocols:        c.String("SmtpServer:TlsProtocols", "Tls12,Tls13"),
			LetsEncryptDomain:   c.Get("SmtpServer:LetsEncryptDomain"),
			MaxRecipients:       c.Int("SmtpServer:MaxRecipients", 50),
			MaxConnections:      c.Int("SmtpServer:MaxConnections", 100),
			MaxConnectionsPerIp: c.Int("SmtpServer:MaxConnectionsPerIp", 10),
		},
		Queue: QueueOptions{
			MaxRetryAttempts:        c.Int("EmailQueue:MaxRetryAttempts", 3),
			BaseRetryDelaySeconds:   c.Int("EmailQueue:BaseRetryDelaySeconds", 5),
			MaxConcurrentProcessing: c.Int("EmailQueue:MaxConcurrentProcessing", 5),
			MaxQueueDepth:           c.Int("EmailQueue:MaxQueueDepth", 1000),
			SpoolDirectory:          c.String("EmailQueue:SpoolDirectory", "spool"),
		},
		Monitoring: MonitoringOptions{
			ReportingIntervalSeconds: c.Int("QueueMonitoring:ReportingIntervalSeconds", 60),
			HighQueueDepthThreshold:  c.Int("QueueMonitoring:HighQueueDepthThreshold", 50),
			LogWhenEmpty:             c.Bool("QueueMonitoring:LogWhenEmpty", false),
		},
		Filter: FilterOptions{
			AllowedDomains: c.Strings("MailboxFilter:AllowedDomains"),
			BlockedDomains: c.Strings("MailboxFilter:BlockedDomains"),
		},
		LetsEncrypt: LetsEncryptOptions{
			Enabled:                   c.Bool("LetsEncrypt:Enabled", false),
			Email:                     c.Get("LetsEncrypt:Email"),
			CertbotPath:               c.String("LetsEncrypt:CertbotPath", "certbot"),
			CertificateDirectory:      c.String("LetsEncrypt:CertificateDirectory", "/etc/letsencrypt"),
			CertificatePassword:       c.String("LetsEncrypt:CertificatePassword", "mailvoid-ssl"),
			ChallengeMethod:           c.String("LetsEncrypt:ChallengeMethod", "dns-cloudflare"),
			WebrootPath:               c.String("LetsEncrypt:WebrootPath", "/var/www/html"),
			RenewalDaysBeforeExpiry:   c.Int("LetsEncrypt:RenewalDaysBeforeExpiry", 30),
			RenewalCheckIntervalHours: c.Int("LetsEncrypt:RenewalCheckIntervalHours", 24),
			Domains:                   c.Strings("LetsEncrypt:Domains"),
			CloudflareApiToken:        c.Get("LetsEncrypt:CloudflareApiToken"),
		},
	}

	// int? HttpPort = 80: an explicit empty/null value disables the --http-01-port flag.
	if !c.Has("LetsEncrypt:HttpPort") {
		p := 80
		o.LetsEncrypt.HttpPort = &p
	} else if p, err := strconv.Atoi(c.Get("LetsEncrypt:HttpPort")); err == nil {
		o.LetsEncrypt.HttpPort = &p
	}

	for _, idx := range c.Children("MailVoidApi:Targets") {
		k := "MailVoidApi:Targets:" + idx + ":"
		o.Targets = append(o.Targets, APITarget{
			Name:            c.Get(k + "Name"),
			BaseUrl:         c.Get(k + "BaseUrl"),
			WebhookEndpoint: c.String(k+"WebhookEndpoint", "/api/webhook/mail"),
			ApiKey:          c.Get(k + "ApiKey"),
			Enabled:         c.Bool(k+"Enabled", true),
		})
	}
	return o
}
