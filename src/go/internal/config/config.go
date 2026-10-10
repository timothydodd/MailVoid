// Package config reproduces the subset of Microsoft.Extensions.Configuration that the
// .NET services relied on, so the Go binaries can run against the exact same settings:
//
//	appsettings.json -> appsettings.{Environment}.json -> user secrets (Development only)
//	-> environment variables ("__" as the section separator) -> command line
//
// Keys are case-insensitive and addressed with ":" separators, arrays are flattened to
// numeric child keys (e.g. "MailVoidApi:Targets:0:BaseUrl"), and later sources win.
package config

import (
	"bytes"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"sort"
	"strconv"
	"strings"
)

type Config struct {
	values      map[string]string // lower-cased key -> value
	Environment string
	ContentRoot string
}

type Options struct {
	// UserSecretsID matches the <UserSecretsId> of the original .csproj so Development
	// machines keep reading the same secrets.json.
	UserSecretsID string
	Args          []string
}

func Load(opts Options) (*Config, error) {
	c := &Config{values: map[string]string{}}

	cmdline := parseArgs(opts.Args)

	c.Environment = firstNonEmpty(
		cmdline["environment"],
		os.Getenv("ASPNETCORE_ENVIRONMENT"),
		os.Getenv("DOTNET_ENVIRONMENT"),
		"Production",
	)
	c.ContentRoot = resolveContentRoot(firstNonEmpty(cmdline["contentroot"], os.Getenv("ASPNETCORE_CONTENTROOT"), os.Getenv("DOTNET_CONTENTROOT")))

	if err := c.loadJSONFile(filepath.Join(c.ContentRoot, "appsettings.json")); err != nil {
		return nil, err
	}
	if err := c.loadJSONFile(filepath.Join(c.ContentRoot, "appsettings."+c.Environment+".json")); err != nil {
		return nil, err
	}
	if strings.EqualFold(c.Environment, "Development") && opts.UserSecretsID != "" {
		if err := c.loadJSONFile(userSecretsPath(opts.UserSecretsID)); err != nil {
			return nil, err
		}
	}

	// Environment variables. Like WebApplication.CreateBuilder, DOTNET_/ASPNETCORE_ prefixed
	// variables are also exposed with the prefix stripped (ASPNETCORE_URLS -> "urls").
	for _, kv := range os.Environ() {
		k, v, ok := strings.Cut(kv, "=")
		if !ok || k == "" {
			continue
		}
		key := strings.ReplaceAll(k, "__", ":")
		c.set(key, v)
		for _, prefix := range []string{"DOTNET_", "ASPNETCORE_"} {
			if len(k) > len(prefix) && strings.EqualFold(k[:len(prefix)], prefix) {
				c.set(strings.ReplaceAll(k[len(prefix):], "__", ":"), v)
			}
		}
	}

	for k, v := range cmdline {
		c.set(k, v)
	}
	return c, nil
}

func (c *Config) set(key, value string) {
	c.values[strings.ToLower(key)] = value
}

// Get returns the value for a ":"-separated key, or "" when unset.
func (c *Config) Get(key string) string {
	return c.values[strings.ToLower(key)]
}

func (c *Config) Has(key string) bool {
	_, ok := c.values[strings.ToLower(key)]
	return ok
}

func (c *Config) String(key, def string) string {
	if v, ok := c.values[strings.ToLower(key)]; ok && v != "" {
		return v
	}
	return def
}

func (c *Config) Int(key string, def int) int {
	v := strings.TrimSpace(c.Get(key))
	if v == "" {
		return def
	}
	n, err := strconv.Atoi(v)
	if err != nil {
		return def
	}
	return n
}

func (c *Config) Bool(key string, def bool) bool {
	v := strings.TrimSpace(c.Get(key))
	if v == "" {
		return def
	}
	b, err := strconv.ParseBool(strings.ToLower(v))
	if err != nil {
		return def
	}
	return b
}

// Children returns the distinct immediate child names under a section, sorted with
// numeric names (array indices) in numeric order.
func (c *Config) Children(section string) []string {
	prefix := strings.ToLower(section) + ":"
	seen := map[string]bool{}
	var out []string
	for k := range c.values {
		if !strings.HasPrefix(k, prefix) {
			continue
		}
		child, _, _ := strings.Cut(k[len(prefix):], ":")
		if child != "" && !seen[child] {
			seen[child] = true
			out = append(out, child)
		}
	}
	sort.Slice(out, func(i, j int) bool {
		a, errA := strconv.Atoi(out[i])
		b, errB := strconv.Atoi(out[j])
		if errA == nil && errB == nil {
			return a < b
		}
		return out[i] < out[j]
	})
	return out
}

// Strings binds a string array section (e.g. "CorsOrigins").
func (c *Config) Strings(section string) []string {
	var out []string
	for _, child := range c.Children(section) {
		if v := c.Get(section + ":" + child); v != "" {
			out = append(out, v)
		}
	}
	return out
}

// IsDevelopment mirrors IHostEnvironment.IsDevelopment().
func (c *Config) IsDevelopment() bool {
	return strings.EqualFold(c.Environment, "Development")
}

func (c *Config) loadJSONFile(path string) error {
	data, err := os.ReadFile(path)
	if os.IsNotExist(err) {
		return nil
	}
	if err != nil {
		return fmt.Errorf("reading %s: %w", path, err)
	}
	data = bytes.TrimPrefix(data, []byte("\xef\xbb\xbf"))
	var root any
	dec := json.NewDecoder(bytes.NewReader(stripJSONComments(data)))
	dec.UseNumber()
	if err := dec.Decode(&root); err != nil {
		return fmt.Errorf("parsing %s: %w", path, err)
	}
	c.flatten("", root)
	return nil
}

func (c *Config) flatten(prefix string, v any) {
	join := func(k string) string {
		if prefix == "" {
			return k
		}
		return prefix + ":" + k
	}
	switch t := v.(type) {
	case map[string]any:
		for k, child := range t {
			c.flatten(join(k), child)
		}
	case []any:
		for i, child := range t {
			c.flatten(join(strconv.Itoa(i)), child)
		}
	case nil:
		c.set(prefix, "")
	case string:
		c.set(prefix, t)
	case json.Number:
		c.set(prefix, t.String())
	case bool:
		c.set(prefix, strconv.FormatBool(t))
	}
}

// stripJSONComments removes // and /* */ comments and trailing commas, which the .NET
// JSON configuration provider tolerates.
func stripJSONComments(in []byte) []byte {
	var out bytes.Buffer
	inString, escaped := false, false
	for i := 0; i < len(in); i++ {
		ch := in[i]
		if inString {
			out.WriteByte(ch)
			switch {
			case escaped:
				escaped = false
			case ch == '\\':
				escaped = true
			case ch == '"':
				inString = false
			}
			continue
		}
		switch {
		case ch == '"':
			inString = true
			out.WriteByte(ch)
		case ch == '/' && i+1 < len(in) && in[i+1] == '/':
			for i < len(in) && in[i] != '\n' {
				i++
			}
			out.WriteByte('\n')
		case ch == '/' && i+1 < len(in) && in[i+1] == '*':
			i += 2
			for i+1 < len(in) && !(in[i] == '*' && in[i+1] == '/') {
				i++
			}
			i++
		case ch == ',':
			// Drop the comma if the next significant character closes an object/array.
			j := i + 1
			for j < len(in) && (in[j] == ' ' || in[j] == '\t' || in[j] == '\r' || in[j] == '\n') {
				j++
			}
			if j < len(in) && (in[j] == '}' || in[j] == ']') {
				continue
			}
			out.WriteByte(ch)
		default:
			out.WriteByte(ch)
		}
	}
	return out.Bytes()
}

// parseArgs supports the .NET command-line provider forms: --Key=Value, --Key Value,
// /Key=Value, /Key Value and Key=Value.
func parseArgs(args []string) map[string]string {
	out := map[string]string{}
	for i := 0; i < len(args); i++ {
		a := args[i]
		key := a
		switch {
		case strings.HasPrefix(a, "--"):
			key = a[2:]
		case strings.HasPrefix(a, "/"):
			key = a[1:]
		}
		if k, v, ok := strings.Cut(key, "="); ok {
			out[strings.ToLower(k)] = v
			continue
		}
		if key != a && i+1 < len(args) {
			out[strings.ToLower(key)] = args[i+1]
			i++
		}
	}
	return out
}

func resolveContentRoot(explicit string) string {
	if explicit != "" {
		return explicit
	}
	cwd, _ := os.Getwd()
	if _, err := os.Stat(filepath.Join(cwd, "appsettings.json")); err == nil {
		return cwd
	}
	if exe, err := os.Executable(); err == nil {
		dir := filepath.Dir(exe)
		if _, err := os.Stat(filepath.Join(dir, "appsettings.json")); err == nil {
			return dir
		}
	}
	return cwd
}

func userSecretsPath(id string) string {
	if runtime.GOOS == "windows" {
		return filepath.Join(os.Getenv("APPDATA"), "Microsoft", "UserSecrets", id, "secrets.json")
	}
	home, _ := os.UserHomeDir()
	return filepath.Join(home, ".microsoft", "usersecrets", id, "secrets.json")
}

func firstNonEmpty(values ...string) string {
	for _, v := range values {
		if v != "" {
			return v
		}
	}
	return ""
}
