package smtpd

import (
	"bufio"
	"bytes"
	"encoding/base64"
	"fmt"
	"io"
	"mime"
	"net/mail"
	"strings"
	"unicode/utf8"

	"github.com/jhillyerd/enmime"
	"golang.org/x/text/encoding/htmlindex"

	"mailvoid/internal/ordered"
)

// BuildEmailData converts a raw DATA payload plus the SMTP envelope into the webhook
// model. Broken MIME is still captured (with ParseFailed set) because this is a
// developer tool: the raw source is always preserved.
func BuildEmailData(raw []byte, envelopeFrom string, recipients []string) (EmailWebhookData, error) {
	data := EmailWebhookData{
		To:          recipients,
		Attachments: []AttachmentData{},
		RawEmail:    latin1(raw),
	}

	parser := enmime.NewParser(enmime.DisableTextConversion(true))
	env, err := parser.ReadEnvelope(bytes.NewReader(raw))
	if err != nil {
		data.From = firstNonEmpty(envelopeFrom, "unknown@unknown.com")
		data.Subject = "[parse failed]"
		data.Text = strPtr("This message could not be parsed as MIME. See the raw source.")
		data.ParseFailed = true
		return data, err
	}

	data.From = "unknown@unknown.com"
	if addrs, err := env.AddressList("From"); err == nil && len(addrs) > 0 && addrs[0].Address != "" {
		data.From = addrs[0].Address
	} else if envelopeFrom != "" {
		data.From = envelopeFrom
	}

	data.Subject = firstNonEmpty(env.GetHeader("Subject"), "(no subject)")
	data.Headers = FlattenHeaders(raw)

	html, text := bodies(env.Root)
	if html != "" {
		data.Html = &html
	}
	// MimeKit's TextBody fell back to the HTML body when there was no text/plain part.
	if text == "" {
		text = html
	}
	if text != "" {
		data.Text = &text
	}

	data.Attachments = attachments(env.Root)

	if id := strings.Trim(strings.TrimSpace(env.GetHeader("Message-Id")), "<>"); id != "" {
		data.MessageId = &id
	}
	if d, err := mail.ParseDate(env.GetHeader("Date")); err == nil {
		data.Date = NetTime{d.UTC()}
	}
	return data, nil
}

// bodies returns the first non-attachment text/html and text/plain parts (depth-first),
// matching MimeKit's HtmlBody/TextBody selection.
func bodies(root *enmime.Part) (html, text string) {
	walk(root, func(p *enmime.Part) {
		if strings.EqualFold(p.Disposition, "attachment") {
			return
		}
		switch strings.ToLower(p.ContentType) {
		case "text/html":
			if html == "" {
				html = string(p.Content)
			}
		case "text/plain":
			if text == "" {
				text = string(p.Content)
			}
		}
	})
	return html, text
}

// attachments mirrors MimeMessage.Attachments: parts with Content-Disposition: attachment.
func attachments(root *enmime.Part) []AttachmentData {
	out := []AttachmentData{}
	walk(root, func(p *enmime.Part) {
		if !strings.EqualFold(p.Disposition, "attachment") {
			return
		}
		out = append(out, AttachmentData{
			Filename:    firstNonEmpty(p.FileName, "attachment"),
			ContentType: p.ContentType,
			Content:     base64.StdEncoding.EncodeToString(p.Content),
		})
	})
	return out
}

func walk(p *enmime.Part, fn func(*enmime.Part)) {
	for ; p != nil; p = p.NextSibling {
		fn(p)
		walk(p.FirstChild, fn)
	}
}

var wordDecoder = &mime.WordDecoder{
	CharsetReader: func(charset string, input io.Reader) (io.Reader, error) {
		enc, err := htmlindex.Get(charset)
		if err != nil {
			return nil, fmt.Errorf("unsupported charset %q", charset)
		}
		return enc.NewDecoder().Reader(input), nil
	},
}

// FlattenHeaders reads the top-level header block with original header-name casing.
// Repeated headers (e.g. Received) are joined with "\n" so each stays intact. Values
// are unfolded (CR/LF removed, continuation whitespace kept) and RFC 2047-decoded,
// like MimeKit's Header.Value.
func FlattenHeaders(raw []byte) ordered.Map {
	var out ordered.Map
	r := bufio.NewReader(bytes.NewReader(raw))
	var name, value string
	flush := func() {
		if name == "" {
			return
		}
		v := strings.TrimLeft(value, " \t")
		if decoded, err := wordDecoder.DecodeHeader(v); err == nil {
			v = decoded
		}
		if existing, ok := out.Get(name); ok {
			v = existing + "\n" + v
		}
		out.Set(name, v)
		name, value = "", ""
	}
	for {
		line, err := r.ReadString('\n')
		trimmed := strings.TrimRight(line, "\r\n")
		if trimmed == "" {
			break // blank line ends the header block
		}
		if trimmed[0] == ' ' || trimmed[0] == '\t' {
			if name != "" {
				value += headerText(trimmed)
			}
		} else {
			flush()
			if k, v, ok := strings.Cut(trimmed, ":"); ok {
				name, value = strings.TrimSpace(k), headerText(v)
			}
		}
		if err != nil {
			break
		}
	}
	flush()
	return out
}

// headerText decodes raw header bytes as UTF-8, falling back to Latin-1.
func headerText(s string) string {
	if utf8.ValidString(s) {
		return s
	}
	return latin1([]byte(s))
}

// latin1 maps each byte to the rune of the same value: a lossless byte<->char mapping,
// so binary / non-UTF-8 content survives in the raw-source view.
func latin1(b []byte) string {
	var sb strings.Builder
	sb.Grow(len(b))
	for _, c := range b {
		sb.WriteRune(rune(c))
	}
	return sb.String()
}

func firstNonEmpty(values ...string) string {
	for _, v := range values {
		if v != "" {
			return v
		}
	}
	return ""
}
