package smtpd

import (
	"context"
	"log/slog"
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
				case item := <-q.Items():
					if fwd.Forward(ctx, item.EmailData) {
						q.MarkProcessed(item)
					} else if ctx.Err() == nil {
						q.MarkFailed(item, "API forwarding returned failure")
					}
				}
			}
		}(i)
	}

	q.LoadSpool(ctx)
	wg.Wait()
	log.Info("Email processor service stopped")
}
