package smtpd

import (
	"strings"
	"time"

	"mailvoid/internal/ordered"
)

// The JSON shapes below match the .NET QueuedEmail / EmailWebhookData / AttachmentData
// classes field-for-field, so spool files written by either implementation can be
// picked up by the other.

type QueuedEmail struct {
	Id            string           `json:"Id"`
	EmailData     EmailWebhookData `json:"EmailData"`
	QueuedAt      NetTime          `json:"QueuedAt"`
	Attempts      int              `json:"Attempts"`
	LastAttemptAt *NetTime         `json:"LastAttemptAt"`
	LastError     *string          `json:"LastError"`

	// Per-(target, recipient) progress, keyed "{webhook url}|{recipient}", so a retry only
	// re-sends what hasn't been accepted yet. Rejected pairs failed permanently (4xx) and
	// are not retried. The .NET reader ignores these extra fields.
	Delivered []string `json:"Delivered,omitempty"`
	Rejected  []string `json:"Rejected,omitempty"`

	// Not serialized: on-disk location.
	spoolPath string
}

type EmailWebhookData struct {
	From        string           `json:"From"`
	To          []string         `json:"To"`
	Subject     string           `json:"Subject"`
	Html        *string          `json:"Html"`
	Text        *string          `json:"Text"`
	Headers     ordered.Map      `json:"Headers"`
	Attachments []AttachmentData `json:"Attachments"`
	MessageId   *string          `json:"MessageId"`
	Date        NetTime          `json:"Date"`
	RawEmail    string           `json:"RawEmail"`
	ParseFailed bool             `json:"ParseFailed"`
}

type AttachmentData struct {
	Filename    string `json:"Filename"`
	ContentType string `json:"ContentType"`
	Content     string `json:"Content"` // base64
}

// NetTime round-trips System.Text.Json DateTime values, including Kind=Unspecified
// values such as "0001-01-01T00:00:00" that carry no zone (treated as UTC).
type NetTime struct{ time.Time }

var netTimeLayouts = []string{
	time.RFC3339Nano,
	"2006-01-02T15:04:05.9999999",
	"2006-01-02T15:04:05",
	"2006-01-02",
}

func (t NetTime) MarshalJSON() ([]byte, error) {
	return []byte(`"` + t.UTC().Format("2006-01-02T15:04:05.9999999Z") + `"`), nil
}

func (t *NetTime) UnmarshalJSON(b []byte) error {
	s := strings.Trim(string(b), `"`)
	if s == "" || s == "null" {
		t.Time = time.Time{}
		return nil
	}
	for _, layout := range netTimeLayouts {
		if parsed, err := time.Parse(layout, s); err == nil {
			t.Time = parsed.UTC()
			return nil
		}
	}
	// Unparseable timestamps are informational only; never fail a spool reload over one.
	t.Time = time.Time{}
	return nil
}

func strPtr(s string) *string { return &s }
