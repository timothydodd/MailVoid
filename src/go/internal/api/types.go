package api

import (
	"crypto/rand"
	"database/sql/driver"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"strconv"
	"strings"
	"time"
)

// Microsoft.Data.Sqlite stored DateTime as TEXT "yyyy-MM-dd HH:mm:ss.FFFFFFF" (UTC by
// convention). Go's ".9999999" likewise trims trailing zeros and drops the dot.
const dbTimeLayout = "2006-01-02 15:04:05.9999999"

// The .NET UtcDateTimeJsonConverter wrote every DateTime as "yyyy-MM-ddTHH:mm:ss.fffZ".
const jsonTimeLayout = "2006-01-02T15:04:05.000Z"

var dbTimeParseLayouts = []string{
	"2006-01-02 15:04:05.999999999",
	"2006-01-02T15:04:05.999999999Z07:00",
	"2006-01-02T15:04:05.999999999",
	"2006-01-02 15:04:05.999999999Z07:00",
	"2006-01-02",
}

func parseDBTime(s string) (time.Time, error) {
	s = strings.TrimSpace(s)
	for _, layout := range dbTimeParseLayouts {
		if t, err := time.Parse(layout, s); err == nil {
			return t.UTC(), nil
		}
	}
	return time.Time{}, fmt.Errorf("unrecognised datetime %q", s)
}

func formatDBTime(t time.Time) string { return t.UTC().Format(dbTimeLayout) }

// DBTime is a non-null UTC timestamp column.
type DBTime struct{ time.Time }

func Now() DBTime { return DBTime{time.Now().UTC()} }

func (t DBTime) Value() (driver.Value, error) { return formatDBTime(t.Time), nil }

func (t *DBTime) Scan(src any) error {
	switch v := src.(type) {
	case time.Time:
		t.Time = v.UTC()
		return nil
	case string:
		p, err := parseDBTime(v)
		t.Time = p
		return err
	case []byte:
		p, err := parseDBTime(string(v))
		t.Time = p
		return err
	case nil:
		t.Time = time.Time{}
		return nil
	}
	return fmt.Errorf("cannot scan %T into DBTime", src)
}

func (t DBTime) MarshalJSON() ([]byte, error) {
	return []byte(`"` + t.UTC().Format(jsonTimeLayout) + `"`), nil
}

// NullTime is a nullable UTC timestamp column.
type NullTime struct {
	Time  time.Time
	Valid bool
}

func NullTimeOf(t time.Time) NullTime { return NullTime{Time: t.UTC(), Valid: true} }

func (t NullTime) Value() (driver.Value, error) {
	if !t.Valid {
		return nil, nil
	}
	return formatDBTime(t.Time), nil
}

func (t *NullTime) Scan(src any) error {
	if src == nil {
		*t = NullTime{}
		return nil
	}
	var d DBTime
	if err := d.Scan(src); err != nil {
		return err
	}
	*t = NullTime{Time: d.Time, Valid: true}
	return nil
}

func (t NullTime) MarshalJSON() ([]byte, error) {
	if !t.Valid {
		return []byte("null"), nil
	}
	return DBTime{t.Time}.MarshalJSON()
}

// GUID holds a Guid in its database form. Microsoft.Data.Sqlite bound Guid parameters as
// upper-case TEXT, so all stored ids are upper-case and comparisons in SQL are exact;
// System.Text.Json and Guid.ToString() emit lower-case, which is what clients see.
type GUID string

func NewGUID() GUID {
	var b [16]byte
	_, _ = rand.Read(b[:])
	b[6] = (b[6] & 0x0f) | 0x40 // version 4
	b[8] = (b[8] & 0x3f) | 0x80 // RFC 4122 variant
	h := hex.EncodeToString(b[:])
	return GUID(strings.ToUpper(h[0:8] + "-" + h[8:12] + "-" + h[12:16] + "-" + h[16:20] + "-" + h[20:]))
}

// ParseGUID accepts the Guid.Parse formats in practical use (D, N, B and P) and returns
// the canonical upper-case database form.
func ParseGUID(s string) (GUID, bool) {
	s = strings.TrimSpace(s)
	if len(s) == 38 && (s[0] == '{' && s[37] == '}' || s[0] == '(' && s[37] == ')') {
		s = s[1:37]
	}
	var h string
	switch len(s) {
	case 36:
		if s[8] != '-' || s[13] != '-' || s[18] != '-' || s[23] != '-' {
			return "", false
		}
		h = s[0:8] + s[9:13] + s[14:18] + s[19:23] + s[24:]
	case 32:
		h = s
	default:
		return "", false
	}
	if _, err := hex.DecodeString(h); err != nil {
		return "", false
	}
	h = strings.ToUpper(h)
	return GUID(h[0:8] + "-" + h[8:12] + "-" + h[12:16] + "-" + h[16:20] + "-" + h[20:]), true
}

func (g GUID) String() string { return strings.ToLower(string(g)) }

func (g GUID) Value() (driver.Value, error) { return strings.ToUpper(string(g)), nil }

func (g *GUID) Scan(src any) error {
	switch v := src.(type) {
	case string:
		*g = GUID(v)
	case []byte:
		*g = GUID(string(v))
	case nil:
		*g = ""
	default:
		return fmt.Errorf("cannot scan %T into GUID", src)
	}
	return nil
}

func (g GUID) MarshalJSON() ([]byte, error) { return json.Marshal(g.String()) }

func (g *GUID) UnmarshalJSON(b []byte) error {
	var s string
	if err := json.Unmarshal(b, &s); err != nil {
		return err
	}
	p, ok := ParseGUID(s)
	if !ok {
		return fmt.Errorf("invalid guid %q", s)
	}
	*g = p
	return nil
}

func (g GUID) Equal(other GUID) bool { return strings.EqualFold(string(g), string(other)) }

// Role mirrors the C# enum. It is stored as its integer value (in a TEXT column) and
// serialised by JsonStringEnumConverter as its name.
type Role int

const (
	RoleUser  Role = 0
	RoleAdmin Role = 1
)

func (r Role) String() string {
	switch r {
	case RoleUser:
		return "User"
	case RoleAdmin:
		return "Admin"
	}
	return strconv.Itoa(int(r))
}

func (r Role) MarshalJSON() ([]byte, error) { return json.Marshal(r.String()) }

// UnmarshalJSON accepts both names (case-insensitive) and integers, like
// JsonStringEnumConverter with its default AllowIntegerValues.
func (r *Role) UnmarshalJSON(b []byte) error {
	var n int
	if err := json.Unmarshal(b, &n); err == nil {
		*r = Role(n)
		return nil
	}
	var s string
	if err := json.Unmarshal(b, &s); err != nil {
		return err
	}
	switch strings.ToLower(strings.TrimSpace(s)) {
	case "user":
		*r = RoleUser
	case "admin":
		*r = RoleAdmin
	default:
		n, err := strconv.Atoi(s)
		if err != nil {
			return fmt.Errorf("invalid role %q", s)
		}
		*r = Role(n)
	}
	return nil
}

func (r Role) Value() (driver.Value, error) { return int64(r), nil }

func (r *Role) Scan(src any) error {
	switch v := src.(type) {
	case int64:
		*r = Role(v)
	case string:
		n, err := strconv.Atoi(strings.TrimSpace(v))
		if err != nil {
			// Tolerate enum names in case a row was written as text.
			return r.UnmarshalJSON([]byte(strconv.Quote(v)))
		}
		*r = Role(n)
	case []byte:
		return r.Scan(string(v))
	case nil:
		*r = RoleUser
	default:
		return fmt.Errorf("cannot scan %T into Role", src)
	}
	return nil
}
