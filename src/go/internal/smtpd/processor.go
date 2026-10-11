package smtpd

import (
	"context"
	"log/slog"
	"strings"
	"sync"
)

// RunProcessor drains the queue with MaxConcurrentProcessing workers until ctx is done.
// Workers start before the spool is reloaded: loading first could block forever if the
// spool exceeds the channel capacity.
func RunProcessor(ctx context.Context, log *slog.Logger, q *Queue, fwd *Forwarder, workers int) {
	log.Info("Email processor service started")

	var wg sync.WaitGroup
	for i := 0; i < max(1, workers); i++ {
		wg.Add(1)
		go func(id int) {
			defer wg.Done()
			log.Debug("Email processor worker started", "workerId", id)
			for {
				select {
				case <-ctx.Done():
					log.Debug("Email processor worker stopped", "workerId", id)
					return
				case e := <-q.Items():
					if item, err := q.Load(e); err == nil {
						process(ctx, q, fwd, item)
					}
				}
			}
		}(i)
	}

	q.LoadSpool(ctx)
	wg.Wait()
	log.Info("Email processor service stopped")
}

func process(ctx context.Context, q *Queue, fwd *Forwarder, item *QueuedEmail) {
	res := fwd.Forward(ctx, item)
	item.Delivered = append(item.Delivered, res.Delivered...)
	item.Rejected = append(item.Rejected, res.Rejected...)

	switch {
	case ctx.Err() != nil:
		// Shutting down: keep progress so the next start only sends what's left.
		q.SaveProgress(item)
	case res.Transient:
		q.MarkFailed(item, "API forwarding returned failure")
	case len(item.Rejected) > 0:
		// Everything retryable is done, but some deliveries were refused for good; keep
		// the message in dead-letter/ so it can be inspected.
		q.DeadLetter(item, "API permanently rejected delivery: "+strings.Join(item.Rejected, ", "))
	default:
		q.MarkProcessed(item)
	}
}
