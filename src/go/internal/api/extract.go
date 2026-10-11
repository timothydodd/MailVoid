package api

import (
	"encoding/json"
	"io"
	"mime"
	"regexp"
	"strings"
	"time"

	"golang.org/x/text/encoding/htmlindex"

	"mailvoid/internal/ordered"
)

// MailData is the webhook payload posted by the SMTP server (PascalCase on the wire;
// encoding/json matches field names case-insensitively, as ASP.NET did).
type MailData struct {
	From        string              `json:"From"`
	To          string              `json:"To"`
	Headers     ordered.Map         `json:"Headers"`
	HTML        *string             `json:"Html"`
	Text        *string             `json:"Text"`
	Attachments []mailAttachmentDto `json:"Attachments"`
	RawEmail    *string             `json:"RawEmail"`
	MessageID   *string             `json:"MessageId"`
}

type mailAttachmentDto struct {
	Filename    string `json:"Filename"`
	ContentType string `json:"ContentType"`
	Content     string `json:"Content"` // base64
}

var (
	emailRegex       = regexp.MustCompile(`[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}`)
	displayNameRegex = regexp.MustCompile(`^([^<>]+)\s*<`)
	charsetRegex     = regexp.MustCompile(`(?i)charset=([^;]+)`)
	mimeWordRegex    = regexp.MustCompile(`=\?([^?]+)\?([BbQq])\?([^?]*)\?=`)
	spfAuthRegex     = regexp.MustCompile(`(?i)spf\s*=\s*([a-zA-Z]+)`)
	dkimAuthRegex    = regexp.MustCompile(`(?i)dkim\s*=\s*([a-zA-Z]+)`)
	receivedSpfRegex = regexp.MustCompile(`^\s*([a-zA-Z]+)`)
)

// extractMail converts a MailData payload into a Mail row (MailDataExtractionService).
func extractMail(d *MailData) *Mail {
	h := d.Headers
	subject := "No Subject"
	if s, ok := subjectHeader(h); ok {
		subject = decodeMimeHeader(s)
	}
	contentType := contentTypeHeader(h)

	html, text := deref(d.HTML), deref(d.Text)
	isHTML := html != "" || (html == "" && text == "" && strings.Contains(strings.ToLower(contentType), "text/html"))
	body := text
	if html != "" {
		body = html
	}

	// CreatedOn is the receipt time. The .NET code used the sender's Date header when it
	// could parse it, which let a sender control sort order and retention (a far-future
	// date never expired; an old one was deleted on the next sweep).
	createdOn := time.Now().UTC()

	m := &Mail{
		From:        extractEmailAddress(d.From),
		To:          extractEmailAddress(d.To),
		Subject:     subject,
		Text:        body,
		IsHTML:      isHTML,
		FromName:    extractDisplayName(d.From),
		ToOthers:    additionalRecipients(h, d.To),
		Charsets:    charsetFromContentType(contentType),
		CreatedOn:   DBTime{createdOn},
		MessageID:   d.MessageID,
		RawSource:   d.RawEmail,
		SpfResult:   spfResult(h),
		DkimResult:  dkimResult(h),
		Attachments: nil,
	}
	if h.Len() > 0 {
		b, _ := json.Marshal(h)
		m.Headers = strPtr(string(b))
	}
	if len(d.Attachments) > 0 {
		type stored struct {
			Filename    string `json:"filename"`
			ContentType string `json:"contentType"`
			SizeBytes   int    `json:"sizeBytes"`
			Content     string `json:"content"`
		}
		list := make([]stored, len(d.Attachments))
		for i, a := range d.Attachments {
			list[i] = stored{a.Filename, a.ContentType, base64DecodedLength(a.Content), a.Content}
		}
		b, _ := json.Marshal(list)
		m.Attachments = strPtr(string(b))
	}
	return m
}

func subjectHeader(h ordered.Map) (string, bool) {
	if v, ok := h.Get("Subject"); ok {
		return v, true
	}
	return h.Get("subject")
}

func contentTypeHeader(h ordered.Map) string {
	if v, ok := h.Get("Content-Type"); ok {
		return v
	}
	v, _ := h.Get("content-type")
	return v
}

func extractEmailAddress(field string) string {
	if field == "" {
		return ""
	}
	if m := emailRegex.FindString(field); m != "" {
		return m
	}
	return field
}

func extractDisplayName(field string) *string {
	if field == "" {
		return nil
	}
	m := displayNameRegex.FindStringSubmatch(field)
	if m == nil {
		return nil
	}
	name := strings.Trim(strings.TrimSpace(m[1]), `"`)
	return &name
}

func additionalRecipients(h ordered.Map, primaryTo string) *string {
	primary := extractEmailAddress(primaryTo)
	var all []string
	if to, ok := h.Get("To"); ok {
		for _, e := range emailRegex.FindAllString(to, -1) {
			if !strings.EqualFold(e, primary) {
				all = append(all, e)
			}
		}
	}
	if cc, ok := h.Get("Cc"); ok {
		all = append(all, emailRegex.FindAllString(cc, -1)...)
	}
	if len(all) == 0 {
		return nil
	}
	seen := map[string]bool{}
	var distinct []string
	for _, e := range all {
		if !seen[e] {
			seen[e] = true
			distinct = append(distinct, e)
		}
	}
	return strPtr(strings.Join(distinct, ", "))
}

func charsetFromContentType(contentType string) *string {
	if contentType == "" {
		return nil
	}
	m := charsetRegex.FindStringSubmatch(contentType)
	if m == nil {
		return nil
	}
	return strPtr(strings.TrimSpace(m[1]))
}

var wordDecoder = &mime.WordDecoder{
	CharsetReader: func(charset string, input io.Reader) (io.Reader, error) {
		enc, err := htmlindex.Get(charset)
		if err != nil {
			return input, nil // unknown charset: treat as UTF-8, as the .NET code did
		}
		return enc.NewDecoder().Reader(input), nil
	},
}

// decodeMimeHeader replaces each RFC 2047 encoded-word in place, keeping any that fail
// to decode.
func decodeMimeHeader(s string) string {
	matches := mimeWordRegex.FindAllString(s, -1)
	result := s
	for _, word := range matches {
		decoded, err := wordDecoder.Decode(word)
		if err != nil {
			continue
		}
		result = strings.ReplaceAll(result, word, decoded)
	}
	return result
}

func base64DecodedLength(b64 string) int {
	clean := strings.TrimSpace(b64)
	if clean == "" {
		return 0
	}
	padding := 0
	if strings.HasSuffix(clean, "==") {
		padding = 2
	} else if strings.HasSuffix(clean, "=") {
		padding = 1
	}
	return (len(clean)/4)*3 - padding
}

func spfResult(h ordered.Map) *string {
	if auth := h.GetFold("Authentication-Results"); auth != "" {
		if m := spfAuthRegex.FindStringSubmatch(auth); m != nil {
			return strPtr(strings.ToLower(m[1]))
		}
	}
	if rs := h.GetFold("Received-SPF"); rs != "" {
		if m := receivedSpfRegex.FindStringSubmatch(rs); m != nil {
			return strPtr(strings.ToLower(m[1]))
		}
	}
	return nil
}

func dkimResult(h ordered.Map) *string {
	if auth := h.GetFold("Authentication-Results"); auth != "" {
		if m := dkimAuthRegex.FindStringSubmatch(auth); m != nil {
			return strPtr(strings.ToLower(m[1]))
		}
	}
	return nil
}
