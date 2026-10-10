// Package ordered provides a string->string map that keeps insertion order through JSON
// round-trips, matching how the .NET services' Dictionary<string,string> serialised mail
// headers (in message order rather than sorted).
package ordered

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"strings"
)

type Map struct {
	keys []string
	vals map[string]string
}

func (m *Map) Set(key, value string) {
	if m.vals == nil {
		m.vals = map[string]string{}
	}
	if _, exists := m.vals[key]; !exists {
		m.keys = append(m.keys, key)
	}
	m.vals[key] = value
}

// Get is an exact (case-sensitive) lookup, like the default Dictionary comparer.
func (m Map) Get(key string) (string, bool) {
	v, ok := m.vals[key]
	return v, ok
}

// GetFold returns the first value whose key matches case-insensitively.
func (m Map) GetFold(key string) string {
	for _, k := range m.keys {
		if strings.EqualFold(k, key) {
			return m.vals[k]
		}
	}
	return ""
}

func (m Map) Len() int       { return len(m.keys) }
func (m Map) Keys() []string { return append([]string(nil), m.keys...) }

func (m Map) MarshalJSON() ([]byte, error) {
	var buf bytes.Buffer
	buf.WriteByte('{')
	for i, k := range m.keys {
		if i > 0 {
			buf.WriteByte(',')
		}
		kb, _ := json.Marshal(k)
		vb, _ := json.Marshal(m.vals[k])
		buf.Write(kb)
		buf.WriteByte(':')
		buf.Write(vb)
	}
	buf.WriteByte('}')
	return buf.Bytes(), nil
}

func (m *Map) UnmarshalJSON(b []byte) error {
	*m = Map{}
	if string(bytes.TrimSpace(b)) == "null" {
		return nil
	}
	dec := json.NewDecoder(bytes.NewReader(b))
	tok, err := dec.Token()
	if err != nil {
		return err
	}
	if d, ok := tok.(json.Delim); !ok || d != '{' {
		return errors.New("expected a JSON object")
	}
	for dec.More() {
		kt, err := dec.Token()
		if err != nil {
			return err
		}
		key, _ := kt.(string)
		var val string
		if err := dec.Decode(&val); err != nil {
			return fmt.Errorf("key %q: %w", key, err)
		}
		m.Set(key, val)
	}
	_, err = dec.Token()
	return err
}
