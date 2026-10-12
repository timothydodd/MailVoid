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
	maxErrorBodyChars = 2048
	maxTransientTries = 2

	// Timeouts. There is no overall client timeout because uploading a large message
	// (attachments are base64 in JSON, so a 10 MB mail can be ~40 MB) may legitimately
	// take a while; instead each phase is bounded and the whole request gets a deadline
	// scaled to the payload size.
	forwardTLSTimeout      = 10 * time.Second
	forwardResponseTimeout = 60 * time.Second // after the body is sent
	forwardBaseTimeout     = 60 * time.Second
	forwardPerMBTimeout    = 10 * time.Second // ~100 KB/s minimum upload rate
	forwardMaxTimeout      = 15 * time.Minute
)

// requestTimeout is the overall deadline for posting a payload of n bytes.
func requestTimeout(n int) time.Duration {
	d := forwardBaseTimeout + time.Duration((n+(1<<20)-1)>>20)*forwardPerMBTimeout
	return min(d, forwardMaxTimeout)
}

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
	// The default transport's dialer already bounds connects (30s) and keeps idle
	// connections; add a bound on waiting for the response once the body is sent.
	transport := http.DefaultTransport.(*http.Transport).Clone()
	transport.TLSHandshakeTimeout = forwardTLSTimeout
	transport.ResponseHeaderTimeout = forwardResponseTimeout
	// Redirects are not followed so message content only ever reaches the configured target.
	client := &http.Client{
		Transport:     transport,
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

	// The expensive part of the payload (bodies, attachments, raw source) is the same for
	// every recipient, so it is encoded once per message; only From/To differ.
	var enc *payloadEncoder
	var encErr error
	for _, rcpt := range recipients {
		var payload *webhookPayload
		for _, t := range targets {
			url := targetURL(t)
			key := url + "|" + strings.ToLower(rcpt)
			if done[key] {
				continue
			}
			if enc == nil && encErr == nil {
				enc, encErr = newPayloadEncoder(email)
			}
			if payload == nil && encErr == nil {
				payload, encErr = enc.forRecipient(rcpt)
			}
			if encErr != nil {
				f.log.Error("Failed to serialize webhook payload", "error", encErr)
				res.Rejected = append(res.Rejected, key)
				continue
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

// mailDataTail is mailData without From/To, in the same field order, so that
// `{"From":…,"To":…,` + tail[1:] is byte-for-byte the encoding of the full mailData.
type mailDataTail struct {
	Headers     ordered.Map      `json:"Headers"`
	Html        *string          `json:"Html"`
	Text        *string          `json:"Text"`
	Attachments []AttachmentData `json:"Attachments"`
	RawEmail    *string          `json:"RawEmail"`
	MessageId   *string          `json:"MessageId"`
}

// payloadEncoder holds the recipient-independent part of a message's webhook JSON.
type payloadEncoder struct {
	from string
	tail []byte // `"Headers":…}` + "\n" (the tail's opening brace is dropped)
}

func newPayloadEncoder(email EmailWebhookData) (*payloadEncoder, error) {
	tail, err := encodeJSON(mailDataTail{
		Headers:     email.Headers,
		Html:        email.Html,
		Text:        email.Text,
		Attachments: email.Attachments,
		RawEmail:    &email.RawEmail,
		MessageId:   email.MessageId,
	})
	if err != nil {
		return nil, err
	}
	return &payloadEncoder{from: email.From, tail: tail[1:]}, nil
}

// webhookPayload is one recipient's request body, kept as two slices so the large tail
// is shared rather than copied per recipient.
type webhookPayload struct {
	head, tail []byte
}

func (p *webhookPayload) Len() int { return len(p.head) + len(p.tail) }

func (p *webhookPayload) reader() io.Reader {
	return io.MultiReader(bytes.NewReader(p.head), bytes.NewReader(p.tail))
}

func (p *webhookPayload) Bytes() []byte {
	return append(append(make([]byte, 0, p.Len()), p.head...), p.tail...)
}

func (e *payloadEncoder) forRecipient(rcpt string) (*webhookPayload, error) {
	from, err := encodeJSON(e.from)
	if err != nil {
		return nil, err
	}
	to, err := encodeJSON(rcpt)
	if err != nil {
		return nil, err
	}
	head := make([]byte, 0, len(from)+len(to)+16)
	head = append(head, `{"From":`...)
	head = append(head, bytes.TrimSuffix(from, []byte("\n"))...)
	head = append(head, `,"To":`...)
	head = append(head, bytes.TrimSuffix(to, []byte("\n"))...)
	head = append(head, ',')
	return &webhookPayload{head: head, tail: e.tail}, nil
}

// encodeJSON encodes like the original payload encoder: HTML characters unescaped
// (mail bodies are full of <, > and &; escaping them would bloat the payload) and a
// trailing newline.
func encodeJSON(v any) ([]byte, error) {
	var buf bytes.Buffer
	enc := json.NewEncoder(&buf)
	enc.SetEscapeHTML(false)
	err := enc.Encode(v)
	return buf.Bytes(), err
}

// permanentStatus is a client error that resending won't fix. 408 and 429 are
// explicitly "try again later".
func permanentStatus(code int) bool {
	return code >= 400 && code < 500 && code != http.StatusRequestTimeout && code != http.StatusTooManyRequests
}

func (f *Forwarder) forwardToTarget(ctx context.Context, t APITarget, url, from, rcpt string, payload *webhookPayload) outcome {
	for attempt := 1; attempt <= maxTransientTries; attempt++ {
		f.log.Info("🚀 Forwarding email", "target", t.Name, "from", from, "to", rcpt, "url", url, "attempt", attempt)

		o, retry := f.post(ctx, t, url, from, rcpt, payload, attempt)
		if !retry {
			return o
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

// post sends one request. retry reports a failure worth an immediate second try
// (network error, timeout or 5xx); otherwise o is final.
func (f *Forwarder) post(ctx context.Context, t APITarget, url, from, rcpt string, payload *webhookPayload, attempt int) (o outcome, retry bool) {
	reqCtx, cancel := context.WithTimeout(ctx, requestTimeout(payload.Len()))
	defer cancel()
	req, err := http.NewRequestWithContext(reqCtx, http.MethodPost, url, payload.reader())
	if err != nil {
		f.log.Error("💥 Unexpected error", "target", t.Name, "url", url, "error", err)
		return outcomePermanent, false
	}
	req.ContentLength = int64(payload.Len())
	req.GetBody = func() (io.ReadCloser, error) { return io.NopCloser(payload.reader()), nil }
	req.Header.Set("Content-Type", "application/json; charset=utf-8")
	if t.ApiKey != "" {
		req.Header.Set("Authorization", "ApiKey "+t.ApiKey)
	} else {
		f.log.Warn("⚠️ No API key configured", "target", t.Name)
	}

	resp, err := f.client.Do(req)
	if err != nil {
		if ctx.Err() != nil {
			return outcomeTransient, false
		}
		var timeout interface{ Timeout() bool }
		if reqCtx.Err() != nil || (errors.As(err, &timeout) && timeout.Timeout()) {
			f.log.Error("⏰ Timeout", "target", t.Name, "url", url, "attempt", attempt, "bytes", payload.Len(), "error", err)
		} else {
			f.log.Error("🔌 Network error", "target", t.Name, "url", url, "attempt", attempt, "error", err)
		}
		return outcomeTransient, true
	}
	body := readCapped(resp.Body)
	resp.Body.Close()
	if resp.StatusCode >= 200 && resp.StatusCode < 300 {
		f.log.Info("✅ Forwarded", "target", t.Name, "status", resp.StatusCode, "from", from, "to", rcpt)
		return outcomeDelivered, false
	}
	f.log.Error("❌ Failed", "target", t.Name, "status", resp.StatusCode, "url", url, "response", body)
	if permanentStatus(resp.StatusCode) {
		return outcomePermanent, false
	}
	// Only server-side failures (and 408/429) are retried; 5xx immediately.
	return outcomeTransient, resp.StatusCode >= 500
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
