package config

import (
	"os"
	"path/filepath"
	"reflect"
	"testing"
)

func TestLoadLayersLikeDotNet(t *testing.T) {
	dir := t.TempDir()
	write := func(name, body string) {
		if err := os.WriteFile(filepath.Join(dir, name), []byte(body), 0o644); err != nil {
			t.Fatal(err)
		}
	}
	write("appsettings.json", `{
		// comments and trailing commas are tolerated, as in .NET
		"JwtSettings": { "Secret": "base", "ExpiryMinutes": 30, },
		"CorsOrigins": ["http://a", "http://b"],
		"MailVoidApi": { "Targets": [ { "Name": "primary", "Enabled": true } ] },
	}`)
	write("appsettings.Staging.json", `{"JwtSettings": {"Secret": "staging"}}`)

	t.Setenv("ASPNETCORE_ENVIRONMENT", "Staging")
	t.Setenv("MailVoidApi__Targets__0__BaseUrl", "http://api:8080")
	t.Setenv("MailVoidApi__Targets__1__Name", "secondary")
	t.Setenv("ASPNETCORE_URLS", "http://+:8080")

	cfg, err := Load(Options{Args: []string{"--contentRoot", dir, "--JwtSettings:Issuer=cli"}})
	if err != nil {
		t.Fatal(err)
	}
	if got := cfg.Get("jwtsettings:SECRET"); got != "staging" {
		t.Errorf("environment file should override base, got %q", got)
	}
	if cfg.Int("JwtSettings:ExpiryMinutes", 0) != 30 {
		t.Error("numbers should bind")
	}
	if cfg.Get("JwtSettings:Issuer") != "cli" {
		t.Error("command line should win")
	}
	if got := cfg.Strings("CorsOrigins"); !reflect.DeepEqual(got, []string{"http://a", "http://b"}) {
		t.Errorf("CorsOrigins = %v", got)
	}
	if got := cfg.Children("MailVoidApi:Targets"); !reflect.DeepEqual(got, []string{"0", "1"}) {
		t.Errorf("env vars should extend arrays, got %v", got)
	}
	if cfg.Get("MailVoidApi:Targets:0:BaseUrl") != "http://api:8080" || cfg.Get("MailVoidApi:Targets:0:Name") != "primary" {
		t.Error("env var should merge into the JSON array element")
	}
	if !cfg.Bool("MailVoidApi:Targets:0:Enabled", false) {
		t.Error("bools should bind")
	}
	if cfg.Get("urls") != "http://+:8080" {
		t.Error("ASPNETCORE_ prefix should be stripped")
	}
}

func TestUserSecretsOnlyInDevelopment(t *testing.T) {
	dir := t.TempDir()
	home := t.TempDir()
	t.Setenv("HOME", home)
	t.Setenv("APPDATA", home)
	secrets := userSecretsPath("test-id")
	if err := os.MkdirAll(filepath.Dir(secrets), 0o755); err != nil {
		t.Fatal(err)
	}
	// dotnet user-secrets writes flat ":"-separated keys.
	if err := os.WriteFile(secrets, []byte(`{"JwtSettings:Secret": "from-secrets"}`), 0o644); err != nil {
		t.Fatal(err)
	}

	t.Setenv("ASPNETCORE_ENVIRONMENT", "Production")
	cfg, _ := Load(Options{UserSecretsID: "test-id", Args: []string{"--contentRoot", dir}})
	if cfg.Get("JwtSettings:Secret") != "" {
		t.Error("user secrets must not load outside Development")
	}

	t.Setenv("ASPNETCORE_ENVIRONMENT", "Development")
	cfg, _ = Load(Options{UserSecretsID: "test-id", Args: []string{"--contentRoot", dir}})
	if cfg.Get("JwtSettings:Secret") != "from-secrets" {
		t.Error("user secrets should load in Development")
	}
}
