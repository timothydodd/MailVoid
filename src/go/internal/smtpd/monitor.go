package smtpd

import (
	"context"
	"io"
	"log/slog"
	"net/http"
	"strings"
	"time"
)

// RunQueueMonitor periodically logs queue depth and dead-letter statistics.
func RunQueueMonitor(ctx context.Context, log *slog.Logger, q *Queue, opts MonitoringOptions) {
	interval := time.Duration(max(1, opts.ReportingIntervalSeconds)) * time.Second
	log.Info("Queue monitoring service started", "intervalSeconds", opts.ReportingIntervalSeconds)
	for {
		reportQueue(log, q, opts)
		select {
		case <-ctx.Done():
			log.Info("Queue monitoring service stopped")
			return
		case <-time.After(interval):
		}
	}
}

func reportQueue(log *slog.Logger, q *Queue, opts MonitoringOptions) {
	pending := q.PendingCount()
	failed := q.FailedEmails()

	if pending > 0 || len(failed) > 0 {
		log.Info("📊 Queue Statistics", "pending", pending, "deadLettered", len(failed))
	} else if opts.LogWhenEmpty {
		log.Debug("📊 Queue is empty")
	}

	old := 0
	for _, f := range failed {
		if f.LastAttemptAt != nil && time.Since(f.LastAttemptAt.Time) > time.Hour {
			old++
		}
	}
	if old > 0 {
		log.Warn("⚠️ Emails have been dead-lettered for over 1 hour", "count", old)
	}
	if pending > opts.HighQueueDepthThreshold {
		log.Warn("🚨 High queue depth", "pending", pending)
	}
}

// RunDiagnostics checks API connectivity and key configuration shortly after startup
// and then every 25 minutes.
func RunDiagnostics(ctx context.Context, log *slog.Logger, targets []APITarget) {
	client := &http.Client{Timeout: 15 * time.Second}
	wait := 5 * time.Second
	for {
		select {
		case <-ctx.Done():
			return
		case <-time.After(wait):
		}
		wait = 25 * time.Minute

		log.Info("🔧 Running SMTP Server Diagnostics...")
		if len(targets) == 0 {
			log.Error("⚠️ No MailVoidApi targets configured. Set MailVoidApi:Targets in appsettings.json")
			continue
		}
		for _, t := range targets {
			checkConnectivity(ctx, log, client, t)
			checkAPIKey(log, t)
		}
	}
}

func checkConnectivity(ctx context.Context, log *slog.Logger, client *http.Client, t APITarget) {
	if !t.Enabled {
		log.Info("⏸️ Disabled, skipping connectivity check", "target", t.Name)
		return
	}
	url := strings.TrimRight(t.BaseUrl, "/") + "/api/health"
	log.Info("🏥 Testing MailVoid API connectivity", "target", t.Name, "url", url)

	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		log.Error("💥 Unexpected error checking MailVoid API connectivity", "target", t.Name, "error", err)
		return
	}
	resp, err := client.Do(req)
	if err != nil {
		log.Error("❌ Cannot reach MailVoid API", "target", t.Name, "url", t.BaseUrl, "error", err)
		return
	}
	defer resp.Body.Close()
	if resp.StatusCode >= 200 && resp.StatusCode < 300 {
		body, _ := io.ReadAll(io.LimitReader(resp.Body, 8192))
		log.Info("✅ MailVoid API is reachable", "target", t.Name, "status", resp.StatusCode, "response", string(body))
	} else {
		log.Warn("⚠️ MailVoid API health check failed", "target", t.Name, "status", resp.StatusCode)
	}
}

func checkAPIKey(log *slog.Logger, t APITarget) {
	switch {
	case t.ApiKey == "":
		log.Warn("⚠️ No API key configured. Set MailVoidApi:Targets[*]:ApiKey", "target", t.Name)
	case t.ApiKey == "smtp-server-key-change-this-in-production":
		log.Warn("⚠️ Using default API key. Change in production!", "target", t.Name)
	default:
		log.Info("🔑 API key is configured", "target", t.Name, "length", len(t.ApiKey))
	}
	log.Info("🌐 Configuration", "target", t.Name, "baseUrl", t.BaseUrl, "endpoint", t.WebhookEndpoint, "enabled", t.Enabled)
}
