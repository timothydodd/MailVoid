package api

import (
	"bytes"
	"compress/gzip"
	"crypto/sha256"
	"encoding/hex"
	"mime"
	"net/http"
	"os"
	"path"
	"path/filepath"
	"regexp"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/andybalholm/brotli"
)

// Static SPA files are read once, precompressed once (Brotli at maximum quality plus
// gzip) and served from memory with ETags. Entries are re-read when the file's size or
// modification time changes.

const maxCachedStaticFile = 16 << 20

// Angular's production build names bundles like main-SCJFFDYI.js; their contents never
// change for a given name, so browsers may cache them indefinitely.
var hashedAsset = regexp.MustCompile(`-[A-Z0-9]{8}\.[a-z0-9]+$`)

type staticEntry struct {
	modTime     time.Time
	size        int64
	contentType string
	etag        string
	raw, gz, br []byte
}

type staticCache struct {
	mu      sync.Mutex
	entries map[string]*staticEntry
}

func newStaticCache() *staticCache { return &staticCache{entries: map[string]*staticEntry{}} }

func (c *staticCache) get(full string, fi os.FileInfo) (*staticEntry, error) {
	c.mu.Lock()
	e := c.entries[full]
	c.mu.Unlock()
	if e != nil && e.size == fi.Size() && e.modTime.Equal(fi.ModTime()) {
		return e, nil
	}

	raw, err := os.ReadFile(full)
	if err != nil {
		return nil, err
	}
	sum := sha256.Sum256(raw)
	e = &staticEntry{modTime: fi.ModTime(), size: fi.Size(), raw: raw, etag: hex.EncodeToString(sum[:8])}
	e.contentType = mime.TypeByExtension(filepath.Ext(full))
	if e.contentType == "" {
		e.contentType = http.DetectContentType(raw)
	}
	if len(raw) > 1024 && isCompressible(e.contentType) {
		var gz bytes.Buffer
		gw, _ := gzip.NewWriterLevel(&gz, gzip.BestCompression)
		_, _ = gw.Write(raw)
		_ = gw.Close()
		var br bytes.Buffer
		bw := brotli.NewWriterLevel(&br, brotli.BestCompression)
		_, _ = bw.Write(raw)
		_ = bw.Close()
		e.gz, e.br = gz.Bytes(), br.Bytes()
	}

	c.mu.Lock()
	c.entries[full] = e
	c.mu.Unlock()
	return e, nil
}

// WarmStaticCache precompresses every file under wwwroot so the first visitor after a
// deploy doesn't wait for maximum-quality Brotli on the large bundles.
func (s *Server) WarmStaticCache() {
	start := time.Now()
	count := 0
	_ = filepath.WalkDir(s.webRoot, func(p string, d os.DirEntry, err error) error {
		if err != nil || d.IsDir() {
			return nil
		}
		if fi, err := d.Info(); err == nil && fi.Size() <= maxCachedStaticFile {
			if _, err := s.static.get(p, fi); err == nil {
				count++
			}
		}
		return nil
	})
	if count > 0 {
		s.log.Info("static files precompressed", "files", count, "durationMs", time.Since(start).Milliseconds())
	}
}

func isCompressible(contentType string) bool {
	for _, t := range compressibleTypes {
		if strings.HasPrefix(contentType, t) {
			return true
		}
	}
	return false
}

// handleStatic serves wwwroot (UseDefaultFiles + UseStaticFiles) and falls back to
// index.html for extension-less paths (MapFallbackToFile).
func (s *Server) handleStatic(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodGet && r.Method != http.MethodHead {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	if strings.HasPrefix(r.URL.Path, "/api/") || strings.HasPrefix(r.URL.Path, "/hubs/") {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	clean := path.Clean("/" + r.URL.Path)
	full := filepath.Join(s.webRoot, filepath.FromSlash(clean))
	fi, err := os.Stat(full)
	if err == nil && fi.IsDir() {
		full = filepath.Join(full, "index.html")
		fi, err = os.Stat(full)
	}
	if err != nil {
		if path.Ext(clean) != "" {
			w.WriteHeader(http.StatusNotFound)
			return
		}
		full = filepath.Join(s.webRoot, "index.html")
		if fi, err = os.Stat(full); err != nil {
			w.WriteHeader(http.StatusNotFound)
			return
		}
	}
	if fi.Size() > maxCachedStaticFile {
		http.ServeFile(w, r, full)
		return
	}
	e, err := s.static.get(full, fi)
	if err != nil {
		w.WriteHeader(http.StatusNotFound)
		return
	}

	body, enc := e.raw, ""
	switch acceptedEncoding(r.Header.Get("Accept-Encoding")) {
	case "br":
		if e.br != nil {
			body, enc = e.br, "br"
		}
	case "gzip":
		if e.gz != nil {
			body, enc = e.gz, "gzip"
		}
	}
	etag := `"` + e.etag + enc + `"`

	h := w.Header()
	h.Set("ETag", etag)
	h.Set("Last-Modified", e.modTime.UTC().Format(http.TimeFormat))
	if e.gz != nil {
		h.Add("Vary", "Accept-Encoding")
	}
	if hashedAsset.MatchString(full) {
		h.Set("Cache-Control", "public, max-age=31536000, immutable")
	} else {
		h.Set("Cache-Control", "no-cache") // revalidate (cheaply, via ETag) so deploys show up
	}
	if match := r.Header.Get("If-None-Match"); match != "" && strings.Contains(match, etag) {
		w.WriteHeader(http.StatusNotModified)
		return
	}
	h.Set("Content-Type", e.contentType)
	if enc != "" {
		h.Set("Content-Encoding", enc)
	}
	h.Set("Content-Length", strconv.Itoa(len(body)))
	w.WriteHeader(http.StatusOK)
	if r.Method != http.MethodHead {
		_, _ = w.Write(body)
	}
}
