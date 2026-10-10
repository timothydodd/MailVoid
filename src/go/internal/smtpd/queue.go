package smtpd

import (
	"context"
	"crypto/rand"
	"encoding/json"
	"fmt"
	"log/slog"
	"math"
	mrand "math/rand/v2"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

// Queue is a single durable, disk-backed queue. Every accepted message is written to
// {SpoolDirectory}/{id}.json before the SMTP transaction is acknowledged, removed once
// forwarded, and moved to dead-letter/ after MaxRetryAttempts failures.
type Queue struct {
	log      *slog.Logger
	opts     QueueOptions
	ch       chan *QueuedEmail
	spoolDir string
	deadDir  string
	pending  atomic.Int64

	mu     sync.Mutex
	retry  map[string]*QueuedEmail
	failed map[string]*QueuedEmail

	stop     chan struct{}
	stopOnce sync.Once
}

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
		ch:       make(chan *QueuedEmail, max(1, opts.MaxQueueDepth)),
		spoolDir: spool,
		deadDir:  dead,
		retry:    map[string]*QueuedEmail{},
		failed:   map[string]*QueuedEmail{},
		stop:     make(chan struct{}),
	}
	go q.retryLoop()
	return q, nil
}

// Items yields queued messages for the processor workers.
func (q *Queue) Items() <-chan *QueuedEmail { return q.ch }

func (q *Queue) PendingCount() int {
	q.mu.Lock()
	defer q.mu.Unlock()
	return int(q.pending.Load()) + len(q.retry)
}

func (q *Queue) FailedEmails() []*QueuedEmail {
	q.mu.Lock()
	defer q.mu.Unlock()
	out := make([]*QueuedEmail, 0, len(q.failed))
	for _, f := range q.failed {
		out = append(out, f)
	}
	return out
}

// Enqueue returns only once the message is durable on disk, so the caller can safely
// reply 250 to the SMTP client.
func (q *Queue) Enqueue(ctx context.Context, data EmailWebhookData) (string, error) {
	item := &QueuedEmail{Id: newGUID(), EmailData: data, QueuedAt: NetTime{time.Now().UTC()}}
	path, err := q.persist(item)
	if err != nil {
		return "", err
	}
	item.spoolPath = path

	q.pending.Add(1)
	select {
	case q.ch <- item:
	case <-ctx.Done():
		// Still on disk; it will be recovered by LoadSpool on the next start.
		q.pending.Add(-1)
		return item.Id, nil
	}
	q.log.Debug("Enqueued email", "emailId", item.Id, "from", data.From, "to", strings.Join(data.To, ","))
	return item.Id, nil
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

	if item.Attempts < q.opts.MaxRetryAttempts {
		if _, err := q.persist(item); err != nil {
			q.log.Error("Failed to persist retry state", "emailId", item.Id, "error", err)
		}
		item.nextRetryAt = time.Now().Add(q.retryDelay(item.Attempts))
		q.mu.Lock()
		q.retry[item.Id] = item
		q.mu.Unlock()
		q.log.Warn("Email processing failed, scheduling retry", "emailId", item.Id, "from", item.EmailData.From,
			"attempt", item.Attempts, "max", q.opts.MaxRetryAttempts, "nextRetry", item.nextRetryAt.UTC(), "error", errMsg)
		return
	}

	q.pending.Add(-1)
	q.moveToDeadLetter(item)
	q.mu.Lock()
	q.failed[item.Id] = item
	q.mu.Unlock()
	q.log.Error("Email processing failed permanently", "emailId", item.Id, "from", item.EmailData.From,
		"subject", item.EmailData.Subject, "attempts", item.Attempts, "finalError", errMsg)
}

// LoadSpool re-enqueues spool files left over from a previous run. It blocks while the
// channel is full, so call it after the workers have started.
func (q *Queue) LoadSpool(ctx context.Context) {
	files, _ := filepath.Glob(filepath.Join(q.spoolDir, "*.json"))
	if len(files) == 0 {
		return
	}
	q.log.Info("Reloading spooled email(s) from disk", "count", len(files))
	for _, file := range files {
		b, err := os.ReadFile(file)
		if err != nil {
			q.log.Error("Failed to reload spool file", "file", file, "error", err)
			continue
		}
		var item QueuedEmail
		if err := json.Unmarshal(b, &item); err != nil {
			q.log.Error("Failed to reload spool file", "file", file, "error", err)
			continue
		}
		item.spoolPath = file
		q.pending.Add(1)
		select {
		case q.ch <- &item:
		case <-ctx.Done():
			q.pending.Add(-1)
			return
		}
	}
}

func (q *Queue) Close() {
	q.stopOnce.Do(func() { close(q.stop) })
}

// retryLoop re-queues items whose backoff has elapsed. If the channel is full the item
// waits for the next tick (backpressure).
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
	for id, item := range q.retry {
		if item.nextRetryAt.After(now) {
			continue
		}
		select {
		case q.ch <- item:
			delete(q.retry, id)
			q.log.Info("Retry time reached, re-queued email", "emailId", id, "attempt", item.Attempts)
		default:
			return
		}
	}
}

func (q *Queue) persist(item *QueuedEmail) (string, error) {
	path := item.spoolPath
	if path == "" {
		path = filepath.Join(q.spoolDir, item.Id+".json")
	}
	b, err := json.Marshal(item)
	if err != nil {
		return "", err
	}
	tmp := path + ".tmp"
	f, err := os.OpenFile(tmp, os.O_CREATE|os.O_WRONLY|os.O_TRUNC, 0o644)
	if err != nil {
		return "", err
	}
	if _, err := f.Write(b); err != nil {
		f.Close()
		return "", err
	}
	if err := f.Sync(); err != nil {
		f.Close()
		return "", err
	}
	if err := f.Close(); err != nil {
		return "", err
	}
	if err := os.Rename(tmp, path); err != nil {
		return "", err
	}
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
	item.spoolPath = target
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
