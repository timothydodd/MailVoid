// Command mailvoid-smtp is the Go replacement for MailVoidSmtpServer. It reads the same
// appsettings.json / environment variables and uses the same on-disk spool format.
package main

import (
	"context"
	"fmt"
	"os"
	"os/signal"
	"sync"
	"syscall"
	"time"

	"mailvoid/internal/config"
	"mailvoid/internal/logging"
	"mailvoid/internal/smtpd"
)

func main() {
	if err := run(); err != nil {
		fmt.Fprintln(os.Stderr, "fatal:", err)
		os.Exit(1)
	}
}

func run() error {
	cfg, err := config.Load(config.Options{UserSecretsID: "d2ee9be3-64bf-42ea-b392-36fc8ec6bf45", Args: os.Args[1:]})
	if err != nil {
		return err
	}
	log := logging.New(cfg)
	opts := smtpd.LoadOptions(cfg)
	log.Info("MailVoid SMTP server starting", "environment", cfg.Environment, "contentRoot", cfg.ContentRoot)

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	queue, err := smtpd.NewQueue(log, opts.Queue)
	if err != nil {
		return err
	}
	defer queue.Close()

	certs := smtpd.NewCertStore(log, opts.Server, opts.LetsEncrypt)
	certs.Reload()

	filter := smtpd.NewMailboxFilter(log, opts.Filter, opts.Server.MaxMessageSize)
	server := smtpd.NewServer(ctx, log, opts.Server, filter, queue, certs)
	forwarder := smtpd.NewForwarder(log, opts.Targets)

	var wg sync.WaitGroup
	goRun := func(fn func()) {
		wg.Add(1)
		go func() { defer wg.Done(); fn() }()
	}

	// Let's Encrypt starts first so certificate acquisition kicks off early; it runs in the
	// background and brings TLS endpoints up once a certificate exists.
	goRun(func() { smtpd.NewLetsEncrypt(log, opts.LetsEncrypt, server.CertificateChanged).Run(ctx) })
	// Snapshot leftover spool files before accepting mail, so a message accepted during
	// startup isn't also picked up by the spool reload (and forwarded twice).
	queue.ScanSpool()
	server.Start()
	goRun(func() { smtpd.RunProcessor(ctx, log, queue, forwarder, opts.Queue.MaxConcurrentProcessing) })
	goRun(func() { smtpd.RunQueueMonitor(ctx, log, queue, opts.Monitoring) })
	goRun(func() { smtpd.RunDiagnostics(ctx, log, opts.Targets) })

	<-ctx.Done()
	log.Info("Shutdown requested")

	shutdownCtx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()
	server.Stop(shutdownCtx)
	wg.Wait()
	return nil
}
