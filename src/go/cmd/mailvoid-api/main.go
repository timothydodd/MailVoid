// Command mailvoid-api is the Go port of MailVoidApi. It reads the same appsettings.json /
// environment configuration and runs against the existing SQLite database unchanged.
package main

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"net"
	"net/http"
	"net/url"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"

	"mailvoid/internal/api"
	"mailvoid/internal/config"
	"mailvoid/internal/logging"
)

func main() {
	if err := run(); err != nil {
		fmt.Fprintln(os.Stderr, "fatal:", err)
		os.Exit(1)
	}
}

func run() error {
	cfg, err := config.Load(config.Options{UserSecretsID: "fa438ca5-9dd0-403b-a6d4-48e1eca84a32", Args: os.Args[1:]})
	if err != nil {
		return err
	}
	log := logging.New(cfg)
	slog.SetDefault(log)

	connStr := cfg.Get("ConnectionStrings:DefaultConnection")
	if connStr == "" {
		return errors.New("connection string not found (ConnectionStrings:DefaultConnection)")
	}
	if p := strings.ToLower(cfg.Get("DatabaseProvider")); p != "" && p != "sqlite" {
		return fmt.Errorf("unsupported DatabaseProvider %q: only Sqlite is supported", cfg.Get("DatabaseProvider"))
	}

	db, err := api.OpenDatabase(connStr, cfg.Get("Database:JournalMode"))
	if err != nil {
		return fmt.Errorf("opening database: %w", err)
	}
	defer db.Close()

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	log.Info("initializing database")
	if err := api.InitSchema(ctx, db); err != nil {
		return err
	}

	srv, err := api.NewServer(cfg, db, log)
	if err != nil {
		return err
	}
	if err := srv.SeedDefaultData(ctx); err != nil {
		return fmt.Errorf("seeding default data: %w", err)
	}
	go srv.RunCleanup(ctx)
	go srv.WarmStaticCache()

	addrs := listenAddresses(cfg, log)
	handler := srv.Handler()
	errCh := make(chan error, len(addrs))
	var servers []*http.Server
	for _, addr := range addrs {
		// Body read deadlines are set per request (SignalR WebSockets are long-lived), and
		// idle keep-alive connections are closed like Kestrel's 130s keep-alive timeout.
		hs := &http.Server{Addr: addr, Handler: handler, ReadHeaderTimeout: 30 * time.Second, IdleTimeout: 130 * time.Second}
		servers = append(servers, hs)
		ln, err := net.Listen("tcp", addr)
		if err != nil {
			return fmt.Errorf("listen %s: %w", addr, err)
		}
		log.Info("now listening", "address", "http://"+displayAddr(addr), "environment", cfg.Environment)
		go func() { errCh <- hs.Serve(ln) }()
	}

	select {
	case <-ctx.Done():
	case err := <-errCh:
		if !errors.Is(err, http.ErrServerClosed) {
			return err
		}
	}
	log.Info("shutting down")
	shutdownCtx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()
	for _, hs := range servers {
		_ = hs.Shutdown(shutdownCtx)
	}
	return nil
}

// listenAddresses resolves bindings the way Kestrel does: "urls" (ASPNETCORE_URLS),
// then Kestrel:Endpoints:*:Url, then "http_ports" (ASPNETCORE_HTTP_PORTS, set to 8080 in
// the container image), defaulting to the dev URL the SPA expects.
func listenAddresses(cfg *config.Config, log *slog.Logger) []string {
	var urls []string
	if v := cfg.Get("urls"); v != "" {
		urls = strings.Split(v, ";")
	}
	if len(urls) == 0 {
		for _, ep := range cfg.Children("Kestrel:Endpoints") {
			if u := cfg.Get("Kestrel:Endpoints:" + ep + ":Url"); u != "" {
				urls = append(urls, u)
			}
		}
	}
	if len(urls) == 0 {
		if ports := cfg.Get("http_ports"); ports != "" {
			for _, p := range strings.Split(ports, ";") {
				if p = strings.TrimSpace(p); p != "" {
					urls = append(urls, "http://*:"+p)
				}
			}
		}
	}
	if len(urls) == 0 {
		urls = []string{"http://localhost:5133"}
	}

	var addrs []string
	for _, raw := range urls {
		raw = strings.TrimSpace(raw)
		if raw == "" {
			continue
		}
		// url.Parse rejects the "+" and "*" wildcard hosts Kestrel accepts.
		normalized := strings.NewReplacer("://+", "://0.0.0.0", "://*", "://0.0.0.0").Replace(raw)
		u, err := url.Parse(normalized)
		if err != nil {
			log.Warn("ignoring invalid listen URL", "url", raw, "error", err)
			continue
		}
		if u.Scheme != "http" {
			log.Warn("only http endpoints are supported; terminate TLS at the proxy", "url", raw)
			continue
		}
		host, port := u.Hostname(), u.Port()
		if port == "" {
			port = "80"
		}
		if host == "0.0.0.0" || host == "::" {
			host = ""
		}
		addrs = append(addrs, net.JoinHostPort(host, port))
	}
	if len(addrs) == 0 {
		addrs = []string{"localhost:5133"}
	}
	return addrs
}

func displayAddr(addr string) string {
	if strings.HasPrefix(addr, ":") {
		return "[::]" + addr
	}
	return addr
}
