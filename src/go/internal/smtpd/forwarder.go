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
	// Redirects are not followed so message content only ever reaches the configured target.
	client := &http.Client{
		Timeout:       forwardHTTPTimeout,
		CheckRedirect: func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse },
	}
	return &Forwarder{log: log, targets: targets, client: client}
}

// ForwardResult reports what one forwarding pass achieved. Pair keys are
// "{webhook url}|{recipient}".
type ForwardResult struct {
	Delivered []string // pairs accepted by the API this pass
	Rejected  []string // pairs permanently refused (4xx other than 408/429)
	Transient bool     // something failed that is worth retrying
}

type outcome int

const (
	outcomeDelivered outcome = iota
	outcomeTransient
	outcomePermanent
)

// Forward posts one webhook per envelope recipient to every enabled target, because the
// API keys an inbox off a single recipient (so BCC/multi-recipient mail lands in every
// inbox). Pairs already in item.Delivered or item.Rejected are skipped, so a retry only
// re-sends what hasn't succeeded and accepted deliveries aren't duplicated.
func (f *Forwarder) Forward(ctx context.Context, item *QueuedEmail) ForwardResult {
	var res ForwardResult
	email := item.EmailData
	var targets []APITarget
	for _, t := range f.targets {
		if t.Enabled && strings.TrimSpace(t.BaseUrl) != "" {
			targets = append(targets, t)
		}
	}
	if len(targets) == 0 {
		f.log.Error("❌ No enabled MailVoidApi targets configured — dropping forward", "from", email.From, "to", strings.Join(email.To, ", "))
		res.Transient = true
		return res
	}

	done := map[string]bool{}
	for _, k := range item.Delivered {
		done[k] = true
	}
	for _, k := range item.Rejected {
		done[k] = true
	}

	recipients := email.To
	if len(recipients) == 0 {
		recipients = []string{""}
	}

	for _, rcpt := range recipients {
		var payload []byte
		for _, t := range targets {
			url := targetURL(t)
			key := url + "|" + strings.ToLower(rcpt)
			if done[key] {
				continue
			}
			if payload == nil {
				var err error
				if payload, err = encodePayload(email, rcpt); err != nil {
					f.log.Error("Failed to serialize webhook payload", "error", err)
					res.Rejected = append(res.Rejected, key)
					continue
				}
			}
			switch f.forwardToTarget(ctx, t, url, email.From, rcpt, payload) {
			case outcomeDelivered:
				res.Delivered = append(res.Delivered, key)
			case outcomePermanent:
				res.Rejected = append(res.Rejected, key)
			default:
				res.Transient = true
			}
			if ctx.Err() != nil {
				return res
			}
		}
	}
	return res
}

func targetURL(t APITarget) string {
	return strings.TrimRight(t.BaseUrl, "/") + t.WebhookEndpoint
}

func encodePayload(email EmailWebhookData, rcpt string) ([]byte, error) {
	var buf bytes.Buffer
	enc := json.NewEncoder(&buf)
	// Mail bodies are full of <, > and &; escaping them would bloat the payload.
	enc.SetEscapeHTML(false)
	err := enc.Encode(mailData{
		From:        email.From,
		To:          rcpt,
		Headers:     email.Headers,
		Html:        email.Html,
		Text:        email.Text,
		Attachments: email.Attachments,
		RawEmail:    &email.RawEmail,
		MessageId:   email.MessageId,
	})
	return buf.Bytes(), err
}

// permanentStatus is a client error that resending won't fix. 408 and 429 are
// explicitly "try again later".
func permanentStatus(code int) bool {
	return code >= 400 && code < 500 && code != http.StatusRequestTimeout && code != http.StatusTooManyRequests
}

func (f *Forwarder) forwardToTarget(ctx context.Context, t APITarget, url, from, rcpt string, payload []byte) outcome {
	for attempt := 1; attempt <= maxTransientTries; attempt++ {
		f.log.Info("🚀 Forwarding email", "target", t.Name, "from", from, "to", rcpt, "url", url, "attempt", attempt)

		req, err := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(payload))
		if err != nil {
			f.log.Error("💥 Unexpected error", "target", t.Name, "url", url, "error", err)
			return outcomePermanent
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
				return outcomeTransient
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
				return outcomeDelivered
			}
			f.log.Error("❌ Failed", "target", t.Name, "status", resp.StatusCode, "url", url, "response", body)
			if permanentStatus(resp.StatusCode) {
				return outcomePermanent
			}
			// Only server-side failures (and 408/429) are retried immediately.
			if resp.StatusCode < 500 {
				return outcomeTransient
			}
		}

		if attempt == maxTransientTries {
			return outcomeTransient
		}
		select {
		case <-time.After(time.Duration(250*attempt) * time.Millisecond):
		case <-ctx.Done():
			return outcomeTransient
		}
	}
	return outcomeTransient
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
