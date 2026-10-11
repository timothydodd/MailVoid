package smtpd

import (
	"bufio"
	"context"
	"crypto/rand"
	"encoding/json"
	"fmt"
	"log/slog"
	"math"
	mrand "math/rand/v2"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

// Queue is a single durable, disk-backed queue. Every accepted message is written to
// {SpoolDirectory}/{id}.json before the SMTP transaction is acknowledged, removed once
// forwarded, and moved to dead-letter/ after MaxRetryAttempts failures.
//
// Only metadata (queueEntry) is kept in memory; message content is read back from the
// spool file when a worker picks the entry up, so memory doesn't grow with queue depth,
// retries or dead letters.
type Queue struct {
	log      *slog.Logger
	opts     QueueOptions
	ch       chan *queueEntry
	spoolDir string
	deadDir  string
	pending  atomic.Int64 // entries in the channel or being processed

	mu         sync.Mutex
	retry      map[string]*queueEntry
	recovered  []string
	deadTotal  int
	deadRecent []time.Time // bounded; used for the "dead-lettered over an hour" warning

	stop     chan struct{}
	stopOnce sync.Once
}

type queueEntry struct {
	id          string
	path        string
	attempts    int
	nextRetryAt time.Time
}

const maxDeadRecent = 1000

func NewQueue(log *slog.Logger, opts QueueOptions) (*Queue, error) {
	spool, err := filepath.Abs(opts.SpoolDirectory)
	if err != nil {
		return nil, err
	}
	dead := filepath.Join(spool, "dead-letter")
	if err := os.MkdirAll(dead, 0o755); err != nil {
		return nil, fmt.Errorf("creating spool directory: %w", err)
	}
	q := &Queue{
		log:      log,
		opts:     opts,
		ch:       make(chan *queueEntry, max(1, opts.MaxQueueDepth)),
		spoolDir: spool,
		deadDir:  dead,
		retry:    map[string]*queueEntry{},
		stop:     make(chan struct{}),
	}
	q.removeTempFiles()
	go q.retryLoop()
	return q, nil
}

// removeTempFiles deletes half-written *.tmp files left by a crash mid-persist; the
// previous complete version (if any) is still in place under its real name.
func (q *Queue) removeTempFiles() {
	for _, dir := range []string{q.spoolDir, q.deadDir} {
		files, _ := filepath.Glob(filepath.Join(dir, "*.tmp"))
		for _, f := range files {
			if err := os.Remove(f); err == nil {
				q.log.Info("Removed incomplete spool file", "file", f)
			}
		}
	}
}

// Items yields queued entries for the processor workers.
func (q *Queue) Items() <-chan *queueEntry { return q.ch }

func (q *Queue) PendingCount() int {
	q.mu.Lock()
	defer q.mu.Unlock()
	return int(q.pending.Load()) + len(q.retry)
}

// DeadLetterStats reports how many messages were dead-lettered since startup and how
// many of those happened more than an hour ago.
func (q *Queue) DeadLetterStats() (total, olderThanHour int) {
	q.mu.Lock()
	defer q.mu.Unlock()
	cutoff := time.Now().Add(-time.Hour)
	recent := 0
	for _, t := range q.deadRecent {
		if t.After(cutoff) {
			recent++
		}
	}
	return q.deadTotal, q.deadTotal - recent
}

// Enqueue returns only once the message is durable on disk, so the caller can safely
// reply 250 to the SMTP client.
func (q *Queue) Enqueue(ctx context.Context, data EmailWebhookData) (string, error) {
	item := &QueuedEmail{Id: newGUID(), EmailData: data, QueuedAt: NetTime{time.Now().UTC()}}
	path, err := q.persist(item)
	if err != nil {
		return "", err
	}

	q.pending.Add(1)
	select {
	case q.ch <- &queueEntry{id: item.Id, path: path}:
	case <-ctx.Done():
		// Still on disk; it will be recovered by the spool scan on the next start.
		q.pending.Add(-1)
		return item.Id, nil
	}
	q.log.Debug("Enqueued email", "emailId", item.Id, "from", data.From, "to", strings.Join(data.To, ","))
	return item.Id, nil
}

// Load reads an entry's message from the spool. An unreadable entry is dropped (and
// logged) so a corrupt file can't wedge a worker.
func (q *Queue) Load(e *queueEntry) (*QueuedEmail, error) {
	b, err := os.ReadFile(e.path)
	if err == nil {
		var item QueuedEmail
		if err = json.Unmarshal(b, &item); err == nil {
			item.spoolPath = e.path
			if item.Id == "" {
				item.Id = e.id
			}
			return &item, nil
		}
	}
	q.pending.Add(-1)
	q.log.Error("Failed to load spool file", "file", e.path, "error", err)
	return nil, err
}

// SaveProgress persists delivery progress without counting an attempt, e.g. when
// shutdown interrupts forwarding; the file is recovered on the next start.
func (q *Queue) SaveProgress(item *QueuedEmail) {
	if _, err := q.persist(item); err != nil {
		q.log.Error("Failed to persist delivery progress", "emailId", item.Id, "error", err)
	}
}

func (q *Queue) MarkProcessed(item *QueuedEmail) {
	q.pending.Add(-1)
	q.tryDelete(item.spoolPath)
	q.log.Info("Email processed successfully", "emailId", item.Id, "from", item.EmailData.From, "attempts", item.Attempts+1)
}

func (q *Queue) MarkFailed(item *QueuedEmail, errMsg string) {
	item.Attempts++
	item.LastError = &errMsg
	now := NetTime{time.Now().UTC()}
	item.LastAttemptAt = &now

	if item.Attempts >= q.opts.MaxRetryAttempts {
		q.DeadLetter(item, errMsg)
		return
	}
	if _, err := q.persist(item); err != nil {
		q.log.Error("Failed to persist retry state", "emailId", item.Id, "error", err)
	}
	e := &queueEntry{id: item.Id, path: item.spoolPath, attempts: item.Attempts,
		nextRetryAt: time.Now().Add(q.retryDelay(item.Attempts))}
	q.mu.Lock()
	q.retry[e.id] = e
	q.pending.Add(-1) // now counted via the retry map
	q.mu.Unlock()
	q.log.Warn("Email processing failed, scheduling retry", "emailId", item.Id, "from", item.EmailData.From,
		"attempt", item.Attempts, "max", q.opts.MaxRetryAttempts, "nextRetry", e.nextRetryAt.UTC(), "error", errMsg)
}

// DeadLetter moves the message to dead-letter/ for inspection.
func (q *Queue) DeadLetter(item *QueuedEmail, errMsg string) {
	item.LastError = &errMsg
	if item.LastAttemptAt == nil {
		now := NetTime{time.Now().UTC()}
		item.LastAttemptAt = &now
	}
	q.pending.Add(-1)
	q.moveToDeadLetter(item)
	q.mu.Lock()
	q.deadTotal++
	q.deadRecent = append(q.deadRecent, time.Now())
	if len(q.deadRecent) > maxDeadRecent {
		q.deadRecent = q.deadRecent[len(q.deadRecent)-maxDeadRecent:]
	}
	q.mu.Unlock()
	q.log.Error("Email processing failed permanently", "emailId", item.Id, "from", item.EmailData.From,
		"subject", item.EmailData.Subject, "attempts", item.Attempts, "finalError", errMsg)
}

// ScanSpool records the spool files left over from a previous run. Call it before the
// SMTP listeners start so messages accepted afterwards aren't picked up twice.
func (q *Queue) ScanSpool() {
	files, _ := filepath.Glob(filepath.Join(q.spoolDir, "*.json"))
	q.mu.Lock()
	q.recovered = files
	q.mu.Unlock()
}

// LoadSpool enqueues the files found by ScanSpool. It blocks while the channel is full,
// so call it after the workers have started.
func (q *Queue) LoadSpool(ctx context.Context) {
	q.mu.Lock()
	files := q.recovered
	q.recovered = nil
	q.mu.Unlock()
	if len(files) == 0 {
		return
	}
	q.log.Info("Reloading spooled email(s) from disk", "count", len(files))
	for _, file := range files {
		e := &queueEntry{id: strings.TrimSuffix(filepath.Base(file), ".json"), path: file}
		q.pending.Add(1)
		select {
		case q.ch <- e:
		case <-ctx.Done():
			q.pending.Add(-1)
			return
		}
	}
}

func (q *Queue) Close() {
	q.stopOnce.Do(func() { close(q.stop) })
}

// retryLoop re-queues entries whose backoff has elapsed. If the channel is full the
// entry waits for the next tick (backpressure).
func (q *Queue) retryLoop() {
	t := time.NewTicker(5 * time.Second)
	defer t.Stop()
	for {
		select {
		case <-q.stop:
			return
		case <-t.C:
			q.requeueDue(time.Now())
		}
	}
}

func (q *Queue) requeueDue(now time.Time) {
	q.mu.Lock()
	defer q.mu.Unlock()
	for id, e := range q.retry {
		if e.nextRetryAt.After(now) {
			continue
		}
		select {
		case q.ch <- e:
			delete(q.retry, id)
			q.pending.Add(1)
			q.log.Info("Retry time reached, re-queued email", "emailId", id, "attempt", e.attempts)
		default:
			return
		}
	}
}

// persist writes the item atomically (temp file + fsync + rename + directory fsync), so
// an acknowledged message survives a crash or power loss.
func (q *Queue) persist(item *QueuedEmail) (string, error) {
	path := item.spoolPath
	if path == "" {
		path = filepath.Join(q.spoolDir, item.Id+".json")
	}
	tmp := path + ".tmp"
	f, err := os.OpenFile(tmp, os.O_CREATE|os.O_WRONLY|os.O_TRUNC, 0o644)
	if err != nil {
		return "", err
	}
	w := bufio.NewWriterSize(f, 64*1024)
	enc := json.NewEncoder(w)
	// Mail bodies are full of <, > and &; escaping them would bloat the file 6x per char.
	enc.SetEscapeHTML(false)
	err = enc.Encode(item)
	if err == nil {
		err = w.Flush()
	}
	if err == nil {
		err = f.Sync()
	}
	if cerr := f.Close(); err == nil {
		err = cerr
	}
	if err != nil {
		os.Remove(tmp)
		return "", err
	}
	if err := os.Rename(tmp, path); err != nil {
		return "", err
	}
	syncDir(filepath.Dir(path))
	item.spoolPath = path
	return path, nil
}

func (q *Queue) moveToDeadLetter(item *QueuedEmail) {
	if _, err := q.persist(item); err != nil {
		q.log.Error("Failed to persist dead-letter state", "emailId", item.Id, "error", err)
	}
	if item.spoolPath == "" {
		return
	}
	target := filepath.Join(q.deadDir, item.Id+".json")
	if err := os.Rename(item.spoolPath, target); err != nil {
		q.log.Error("Failed to move email to dead-letter", "emailId", item.Id, "error", err)
		return
	}
	syncDir(q.deadDir)
	syncDir(q.spoolDir)
	item.spoolPath = target
}

// syncDir makes a rename durable. Windows can't fsync directories; there the rename is
// already as durable as the platform allows.
func syncDir(dir string) {
	if runtime.GOOS == "windows" {
		return
	}
	if d, err := os.Open(dir); err == nil {
		_ = d.Sync()
		d.Close()
	}
}

func (q *Queue) tryDelete(path string) {
	if path == "" {
		return
	}
	if err := os.Remove(path); err != nil && !os.IsNotExist(err) {
		q.log.Warn("Failed to delete spool file", "file", path, "error", err)
	}
}

// retryDelay is exponential backoff (base * 2^(attempt-1)) with 0.75–1.25 jitter.
func (q *Queue) retryDelay(attempt int) time.Duration {
	base := time.Duration(q.opts.BaseRetryDelaySeconds) * time.Second
	exp := float64(base) * math.Pow(2, float64(attempt-1))
	jitter := 1.0 + (mrand.Float64()-0.5)*0.5
	return time.Duration(exp * jitter)
}

// newGUID returns a random (v4) GUID in .NET's default lower-case "D" format.
func newGUID() string {
	var b [16]byte
	_, _ = rand.Read(b[:])
	b[6] = (b[6] & 0x0f) | 0x40
	b[8] = (b[8] & 0x3f) | 0x80
	return fmt.Sprintf("%x-%x-%x-%x-%x", b[0:4], b[4:6], b[6:8], b[8:10], b[10:16])
}
