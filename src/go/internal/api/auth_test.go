package api

import "testing"

// Vectors from ASP.NET Core's PasswordHasherTest (password "my password").
func TestVerifyPasswordIdentityVectors(t *testing.T) {
	cases := map[string]string{
		"v2":        "AAABAgMEBQYHCAkKCwwNDg+ukCEMDf0yyQ29NYubggHIVY0sdEUfdyeM+E1LtH1uJg==",
		"v3-sha256": "AQAAAAEAACcQAAAAEAABAgMEBQYHCAkKCwwNDg+yWU7rLgUwPZb1Itsmra7cbxw2EFpwpVFIEtP+JIuUEw==",
	}
	for name, hash := range cases {
		if !verifyPassword(hash, "my password") {
			t.Errorf("%s: expected match", name)
		}
		if verifyPassword(hash, "wrong password") {
			t.Errorf("%s: unexpected match", name)
		}
	}
}

func TestHashPasswordRoundTrip(t *testing.T) {
	h := hashPassword("s3cret!")
	// V3 / HMACSHA512 / 100000 iterations header, as produced by .NET 8+.
	if h[:16] != "AQAAAAIAAYagAAAA" {
		t.Fatalf("unexpected header: %s", h[:16])
	}
	if !verifyPassword(h, "s3cret!") || verifyPassword(h, "nope") {
		t.Fatal("round trip failed")
	}
}

func TestParseGUID(t *testing.T) {
	want := GUID("01F686C6-7FCF-4AB9-8A91-F57394EC1BC4")
	for _, in := range []string{
		"01f686c6-7fcf-4ab9-8a91-f57394ec1bc4",
		"01F686C6-7FCF-4AB9-8A91-F57394EC1BC4",
		"01f686c67fcf4ab98a91f57394ec1bc4",
		"{01f686c6-7fcf-4ab9-8a91-f57394ec1bc4}",
	} {
		got, ok := ParseGUID(in)
		if !ok || got != want {
			t.Errorf("ParseGUID(%q) = %q, %v", in, got, ok)
		}
	}
	if _, ok := ParseGUID("not-a-guid"); ok {
		t.Error("expected failure")
	}
	if want.String() != "01f686c6-7fcf-4ab9-8a91-f57394ec1bc4" {
		t.Error("JSON form must be lower-case")
	}
}

func TestDBTimeFormats(t *testing.T) {
	for _, s := range []string{"2025-06-25 13:20:38.920137", "2025-06-25 13:20:39", "2026-08-10T14:36:17Z"} {
		var d DBTime
		if err := d.Scan(s); err != nil {
			t.Errorf("scan %q: %v", s, err)
		}
	}
	var d DBTime
	_ = d.Scan("2025-06-25 13:20:39")
	if v, _ := d.Value(); v != "2025-06-25 13:20:39" {
		t.Errorf("whole seconds should have no fraction, got %v", v)
	}
	_ = d.Scan("2025-06-25 13:20:38.920137")
	if v, _ := d.Value(); v != "2025-06-25 13:20:38.920137" {
		t.Errorf("got %v", v)
	}
	b, _ := d.MarshalJSON()
	if string(b) != `"2025-06-25T13:20:38.920Z"` {
		t.Errorf("json %s", b)
	}
}
