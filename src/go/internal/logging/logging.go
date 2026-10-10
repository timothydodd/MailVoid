// Package logging builds a slog logger whose minimum level comes from the same
// "Logging:LogLevel:Default" setting the .NET services used.
package logging

import (
	"log/slog"
	"os"
	"strings"

	"mailvoid/internal/config"
)

func New(cfg *config.Config) *slog.Logger {
	level := ParseLevel(cfg.String("Logging:LogLevel:Default", "Information"))
	h := slog.NewTextHandler(os.Stdout, &slog.HandlerOptions{Level: level})
	return slog.New(h)
}

// ParseLevel maps Microsoft.Extensions.Logging level names to slog levels.
func ParseLevel(name string) slog.Level {
	switch strings.ToLower(strings.TrimSpace(name)) {
	case "trace", "debug":
		return slog.LevelDebug
	case "warning", "warn":
		return slog.LevelWarn
	case "error", "critical":
		return slog.LevelError
	case "none":
		return slog.Level(100)
	default:
		return slog.LevelInfo
	}
}
