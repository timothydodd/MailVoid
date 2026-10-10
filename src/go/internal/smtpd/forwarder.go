package smtpd

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"log/slog"
	"net/http"
	"strings"
	"time"

	"mailvoid/internal/ordered"
)

const (
	maxErrorBodyChars  = 2048
	maxTransientTries  = 2
	forwardHTTPTimeout = 30 * time.Second
)

// mailData is the API's webhook contract (MailVoidApi MailData), PascalCase like the
// payload System.Text.Json produced.
type mailData struct {
	From        string           `json:"From"`
	To          string           `json:"To"`
	Headers     ordered.Map      `json:"Headers"`
	Html        *string          `json:"Html"`
	Text        *string          `json:"Text"`
	Attachments []AttachmentData `json:"Attachments"`
	RawEmail    *string          `json:"RawEmail"`
	MessageId   *string          `json:"MessageId"`
}

type Forwarder struct {
	log     *slog.Logger
	targets []APITarget
	client  *http.Client
}

func NewForwarder(log *slog.Logger, targets []APITarget) *Forwarder {
	return &Forwarder{log: log, targets: targets, client: &http.Client{Timeout: forwardHTTPTimeout}}
}

// Forward posts one webhook per envelope recipient to every enabled target, because the
// API keys an inbox off a single recipient (so BCC/multi-recipient mail lands in every
// inbox). A false result retries the whole message, which can duplicate deliveries that
// already succeeded — accepted for a dev tool.
func (f *Forwarder) Forward(ctx context.Context, email EmailWebhookData) bool {
	var targets []APITarget
	for _, t := range f.targets {
		if t.Enabled && strings.TrimSpace(t.BaseUrl) != "" {
			targets = append(targets, t)
		}
	}
	if len(targets) == 0 {
		f.log.Error("❌ No enabled MailVoidApi targets configured — dropping forward", "from", email.From, "to", strings.Join(email.To, ", "))
		return false
	}

	recipients := email.To
	if len(recipients) == 0 {
		recipients = []string{""}
	}

	ok := true
	for _, rcpt := range recipients {
		payload, err := json.Marshal(mailData{
			From:        email.From,
			To:          rcpt,
			Headers:     email.Headers,
			Html:        email.Html,
			Text:        email.Text,
			Attachments: email.Attachments,
			RawEmail:    &email.RawEmail,
			MessageId:   email.MessageId,
		})
		if err != nil {
			f.log.Error("Failed to serialize webhook payload", "error", err)
			return false
		}
		for _, t := range targets {
			if !f.forwardToTarget(ctx, t, email.From, rcpt, payload) {
				ok = false
			}
		}
	}
	return ok
}

func (f *Forwarder) forwardToTarget(ctx context.Context, t APITarget, from, rcpt string, payload []byte) bool {
	url := strings.TrimRight(t.BaseUrl, "/") + t.WebhookEndpoint

	for attempt := 1; attempt <= maxTransientTries; attempt++ {
		f.log.Info("🚀 Forwarding email", "target", t.Name, "from", from, "to", rcpt, "url", url, "attempt", attempt)

		req, err := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(payload))
		if err != nil {
			f.log.Error("💥 Unexpected error", "target", t.Name, "url", url, "error", err)
			return false
		}
		req.Header.Set("Content-Type", "application/json; charset=utf-8")
		if t.ApiKey != "" {
			req.Header.Set("Authorization", "ApiKey "+t.ApiKey)
		} else {
			f.log.Warn("⚠️ No API key configured", "target", t.Name)
		}

		resp, err := f.client.Do(req)
		if err != nil {
			if ctx.Err() != nil {
				return false
			}
			var timeout interface{ Timeout() bool }
			if errors.As(err, &timeout) && timeout.Timeout() {
				f.log.Error("⏰ Timeout", "target", t.Name, "url", url, "attempt", attempt, "error", err)
			} else {
				f.log.Error("🔌 Network error", "target", t.Name, "url", url, "attempt", attempt, "error", err)
			}
		} else {
			body := readCapped(resp.Body)
			resp.Body.Close()
			if resp.StatusCode >= 200 && resp.StatusCode < 300 {
				f.log.Info("✅ Forwarded", "target", t.Name, "status", resp.StatusCode, "from", from, "to", rcpt)
				return true
			}
			f.log.Error("❌ Failed", "target", t.Name, "status", resp.StatusCode, "url", url, "response", body)
			// Only server-side failures are worth retrying.
			if resp.StatusCode < 500 {
				return false
			}
		}

		if attempt == maxTransientTries {
			return false
		}
		select {
		case <-time.After(time.Duration(250*attempt) * time.Millisecond):
		case <-ctx.Done():
			return false
		}
	}
	return false
}

func readCapped(r io.Reader) string {
	b, err := io.ReadAll(io.LimitReader(r, 64*1024))
	if err != nil {
		return "(unreadable response body)"
	}
	s := string(b)
	if len([]rune(s)) > maxErrorBodyChars {
		return string([]rune(s)[:maxErrorBodyChars]) + "…(truncated)"
	}
	return s
}
