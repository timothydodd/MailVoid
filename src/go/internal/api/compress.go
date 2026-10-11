package api

import (
	"compress/flate"
	"compress/gzip"
	"errors"
	"io"
	"net/http"
	"strings"
	"sync"
	"time"

	"github.com/andybalholm/brotli"
)

// ---- Response compression ----

// Like the .NET API: Brotli when the client accepts it, otherwise gzip at its fastest
// level. Static files are precompressed separately (static.go) and arrive here with
// Content-Encoding already set, so they pass straight through.
var compressibleTypes = []string{"text/", "application/json", "application/javascript", "application/xml",
	"application/problem+json", "image/svg+xml", "application/wasm"}

// Brotli quality 4 is close to gzip's speed with noticeably smaller output.
const brotliDynamicQuality = 4

var (
	gzipPool   = sync.Pool{New: func() any { w, _ := gzip.NewWriterLevel(io.Discard, gzip.BestSpeed); return w }}
	brotliPool = sync.Pool{New: func() any { return brotli.NewWriterLevel(io.Discard, brotliDynamicQuality) }}
)

type resettableWriter interface {
	io.WriteCloser
	Flush() error
	Reset(io.Writer)
}

type compressWriter struct {
	http.ResponseWriter
	encoding    string // "br" or "gzip"
	cw          resettableWriter
	wroteHeader bool
}

func (c *compressWriter) WriteHeader(code int) {
	if c.wroteHeader {
		return
	}
	c.wroteHeader = true
	h := c.Header()
	if h.Get("Content-Encoding") == "" && code != http.StatusNoContent && code != http.StatusNotModified && code >= 200 {
		ct := h.Get("Content-Type")
		for _, t := range compressibleTypes {
			if strings.HasPrefix(ct, t) {
				if c.encoding == "br" {
					c.cw = brotliPool.Get().(*brotli.Writer)
				} else {
					c.cw = gzipPool.Get().(*gzip.Writer)
				}
				c.cw.Reset(c.ResponseWriter)
				h.Set("Content-Encoding", c.encoding)
				h.Del("Content-Length")
				break
			}
		}
	}
	h.Add("Vary", "Accept-Encoding")
	c.ResponseWriter.WriteHeader(code)
}

func (c *compressWriter) Write(b []byte) (int, error) {
	if !c.wroteHeader {
		if c.Header().Get("Content-Type") == "" {
			c.Header().Set("Content-Type", http.DetectContentType(b))
		}
		c.WriteHeader(http.StatusOK)
	}
	if c.cw != nil {
		return c.cw.Write(b)
	}
	return c.ResponseWriter.Write(b)
}

func (c *compressWriter) Flush() {
	if c.cw != nil {
		_ = c.cw.Flush()
	}
	_ = http.NewResponseController(c.ResponseWriter).Flush()
}

func (c *compressWriter) Unwrap() http.ResponseWriter { return c.ResponseWriter }

func (c *compressWriter) close() {
	if c.cw == nil {
		return
	}
	_ = c.cw.Close()
	c.cw.Reset(io.Discard)
	if c.encoding == "br" {
		brotliPool.Put(c.cw)
	} else {
		gzipPool.Put(c.cw)
	}
	c.cw = nil
}

// acceptedEncoding picks br or gzip from Accept-Encoding, honouring q=0 exclusions.
func acceptedEncoding(header string) string {
	br, gz := false, false
	for _, part := range strings.Split(header, ",") {
		name, params, _ := strings.Cut(strings.TrimSpace(part), ";")
		if q := strings.ReplaceAll(params, " ", ""); q == "q=0" || q == "q=0.0" || q == "q=0.00" || q == "q=0.000" {
			continue
		}
		switch strings.ToLower(strings.TrimSpace(name)) {
		case "br":
			br = true
		case "gzip":
			gz = true
		}
	}
	switch {
	case br:
		return "br"
	case gz:
		return "gzip"
	}
	return ""
}

func compressResponses(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		enc := acceptedEncoding(r.Header.Get("Accept-Encoding"))
		if enc == "" || strings.HasPrefix(r.URL.Path, "/hubs") || r.Method == http.MethodHead {
			next.ServeHTTP(w, r)
			return
		}
		cw := &compressWriter{ResponseWriter: w, encoding: enc}
		defer cw.close()
		next.ServeHTTP(cw, r)
	})
}

// ---- Request decompression and limits ----

// Body limits. Kestrel applied 30 MB to every request (after AddRequestDecompression);
// the anonymous auth endpoints get far less, and the API-key-protected mail webhook more,
// because its JSON carries the raw message plus decoded bodies and base64 attachments.
const (
	defaultBodyLimit     = 30 << 20
	authBodyLimit        = 64 << 10
	mailWebhookBodyLimit = 128 << 20
)

// Per-request body read deadlines (Kestrel enforced a minimum data rate instead).
const (
	defaultReadDeadline     = 2 * time.Minute
	mailWebhookReadDeadline = 10 * time.Minute
)

func bodyLimitFor(path string) int64 {
	switch {
	case strings.HasPrefix(path, "/api/auth/"):
		return authBodyLimit
	case path == "/api/webhook/mail":
		return mailWebhookBodyLimit
	case strings.HasPrefix(path, "/api/hook/"):
		return maxWebhookBody
	}
	return defaultBodyLimit
}

// decompressRequests mirrors AddRequestDecompression for gzip/deflate bodies; the size
// limit is applied to the decompressed stream so a small compressed body can't expand
// without bound.
func decompressRequests(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		switch strings.ToLower(r.Header.Get("Content-Encoding")) {
		case "gzip":
			zr, err := gzip.NewReader(r.Body)
			if err != nil {
				w.WriteHeader(http.StatusBadRequest)
				return
			}
			r.Body = zr
		case "deflate":
			r.Body = flate.NewReader(r.Body)
		case "br":
			r.Body = io.NopCloser(brotli.NewReader(r.Body))
		default:
			// Unknown or "identity" encodings pass through untouched, as in .NET.
			r.Body = http.MaxBytesReader(w, r.Body, bodyLimitFor(r.URL.Path))
			next.ServeHTTP(w, r)
			return
		}
		r.Header.Del("Content-Encoding")
		r.Header.Del("Content-Length")
		r.ContentLength = -1
		r.Body = http.MaxBytesReader(w, r.Body, bodyLimitFor(r.URL.Path))
		next.ServeHTTP(w, r)
	})
}

// readDeadlines bounds how long a request body may take to arrive, so slow uploads
// can't hold connections open. SignalR WebSockets manage their own deadlines.
func readDeadlines(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if !strings.HasPrefix(r.URL.Path, "/hubs") {
			d := defaultReadDeadline
			if r.URL.Path == "/api/webhook/mail" {
				d = mailWebhookReadDeadline
			}
			_ = http.NewResponseController(w).SetReadDeadline(time.Now().Add(d))
		}
		next.ServeHTTP(w, r)
	})
}

// writeBodyError answers a failed body decode: 413 when the size limit was hit,
// otherwise the ProblemDetails 400 that ASP.NET model binding returned.
func writeBodyError(w http.ResponseWriter, err error) {
	var tooLarge *http.MaxBytesError
	if errors.As(err, &tooLarge) {
		w.WriteHeader(http.StatusRequestEntityTooLarge)
		return
	}
	writeProblem(w, http.StatusBadRequest)
}
