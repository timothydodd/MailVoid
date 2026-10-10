package api

import (
	"bytes"
	"crypto/rand"
	"encoding/base64"
	"encoding/json"
	"log/slog"
	"net/http"
	"strings"
	"sync"
	"time"

	"github.com/gorilla/websocket"
)

// Hub implements the server side of the ASP.NET Core SignalR protocol that the SPA's
// @microsoft/signalr client speaks: negotiate (v0/v1), the WebSockets transport and the
// JSON hub protocol. The hub is push-only (NewMail / NewWebhook), so client invocations
// are answered with an error completion.
//
// Connections are grouped per user like the .NET hub's "user-{id}" groups.
type Hub struct {
	log      *slog.Logger
	upgrader websocket.Upgrader

	mu      sync.Mutex
	users   map[string]map[*hubConn]struct{}
	pending map[string]pendingConn // connectionToken -> negotiated, not yet connected
}

type pendingConn struct {
	userID  GUID
	created time.Time
}

type hubConn struct {
	userID GUID
	send   chan []byte
}

const (
	recordSeparator   = 0x1e
	keepAliveInterval = 15 * time.Second
	clientTimeout     = 30 * time.Second
	handshakeTimeout  = 15 * time.Second
	pendingTTL        = 2 * time.Minute
)

func newHub(log *slog.Logger) *Hub {
	return &Hub{
		log: log,
		upgrader: websocket.Upgrader{
			// CORS doesn't apply to WebSockets and the .NET hub didn't check Origin; the
			// connection is authenticated by the access token instead.
			CheckOrigin: func(*http.Request) bool { return true },
		},
		users:   map[string]map[*hubConn]struct{}{},
		pending: map[string]pendingConn{},
	}
}

func userKey(id GUID) string { return strings.ToUpper(string(id)) }

func randomID() string {
	b := make([]byte, 16)
	_, _ = rand.Read(b)
	return base64.RawURLEncoding.EncodeToString(b)
}

func (h *Hub) handleNegotiate(w http.ResponseWriter, r *http.Request, p *Principal) {
	connectionID, token := randomID(), randomID()
	version := r.URL.Query().Get("negotiateVersion")

	h.mu.Lock()
	now := time.Now()
	for k, v := range h.pending {
		if now.Sub(v.created) > pendingTTL {
			delete(h.pending, k)
		}
	}
	resp := map[string]any{
		"connectionId": connectionID,
		"availableTransports": []map[string]any{
			{"transport": "WebSockets", "transferFormats": []string{"Text", "Binary"}},
		},
	}
	if version != "" && version != "0" {
		resp["negotiateVersion"] = 1
		resp["connectionToken"] = token
		h.pending[token] = pendingConn{userID: p.UserID, created: now}
	} else {
		// Negotiate v0 clients connect with the connectionId itself.
		h.pending[connectionID] = pendingConn{userID: p.UserID, created: now}
	}
	h.mu.Unlock()

	writeJSON(w, http.StatusOK, resp)
}

func (h *Hub) handleConnect(w http.ResponseWriter, r *http.Request, p *Principal) {
	if !websocket.IsWebSocketUpgrade(r) {
		writeText(w, http.StatusBadRequest, "Only the WebSockets transport is supported.")
		return
	}
	if id := r.URL.Query().Get("id"); id != "" {
		h.mu.Lock()
		pc, ok := h.pending[id]
		delete(h.pending, id)
		h.mu.Unlock()
		if !ok || !pc.userID.Equal(p.UserID) {
			writeText(w, http.StatusNotFound, "No Connection with that ID")
			return
		}
	}

	ws, err := h.upgrader.Upgrade(w, r, nil)
	if err != nil {
		return // Upgrade already wrote the error response
	}
	defer ws.Close()

	if !h.handshake(ws) {
		return
	}

	c := &hubConn{userID: p.UserID, send: make(chan []byte, 64)}
	h.add(c)
	defer h.remove(c)

	done := make(chan struct{})
	go h.writeLoop(ws, c, done)
	h.readLoop(ws, c)
	close(done)
}

// handshake reads {"protocol":"json","version":1} and acknowledges with {}.
func (h *Hub) handshake(ws *websocket.Conn) bool {
	_ = ws.SetReadDeadline(time.Now().Add(handshakeTimeout))
	_, data, err := ws.ReadMessage()
	if err != nil {
		return false
	}
	msg, _, _ := bytes.Cut(data, []byte{recordSeparator})
	var req struct {
		Protocol string `json:"protocol"`
		Version  int    `json:"version"`
	}
	if err := json.Unmarshal(msg, &req); err != nil || req.Protocol != "json" {
		reply, _ := json.Marshal(map[string]string{"error": "The protocol '" + req.Protocol + "' is not supported."})
		_ = ws.WriteMessage(websocket.TextMessage, append(reply, recordSeparator))
		return false
	}
	return ws.WriteMessage(websocket.TextMessage, []byte{'{', '}', recordSeparator}) == nil
}

func (h *Hub) readLoop(ws *websocket.Conn, c *hubConn) {
	for {
		_ = ws.SetReadDeadline(time.Now().Add(clientTimeout))
		_, data, err := ws.ReadMessage()
		if err != nil {
			return
		}
		for _, rec := range bytes.Split(data, []byte{recordSeparator}) {
			if len(bytes.TrimSpace(rec)) == 0 {
				continue
			}
			var msg struct {
				Type         int    `json:"type"`
				InvocationID string `json:"invocationId"`
				Target       string `json:"target"`
			}
			if json.Unmarshal(rec, &msg) != nil {
				return
			}
			switch msg.Type {
			case 1, 4: // Invocation / StreamInvocation: this hub exposes no methods
				if msg.InvocationID != "" {
					reply, _ := json.Marshal(map[string]any{
						"type": 3, "invocationId": msg.InvocationID,
						"error": "Failed to invoke '" + msg.Target + "' because it does not exist.",
					})
					h.enqueue(c, append(reply, recordSeparator))
				}
			case 7: // Close
				return
			}
		}
	}
}

func (h *Hub) writeLoop(ws *websocket.Conn, c *hubConn, done <-chan struct{}) {
	ping := []byte{'{', '"', 't', 'y', 'p', 'e', '"', ':', '6', '}', recordSeparator}
	ticker := time.NewTicker(keepAliveInterval)
	defer ticker.Stop()
	for {
		var msg []byte
		select {
		case <-done:
			return
		case msg = <-c.send:
		case <-ticker.C:
			msg = ping
		}
		_ = ws.SetWriteDeadline(time.Now().Add(10 * time.Second))
		if err := ws.WriteMessage(websocket.TextMessage, msg); err != nil {
			ws.Close() // unblocks readLoop
			return
		}
	}
}

func (h *Hub) add(c *hubConn) {
	h.mu.Lock()
	defer h.mu.Unlock()
	k := userKey(c.userID)
	if h.users[k] == nil {
		h.users[k] = map[*hubConn]struct{}{}
	}
	h.users[k][c] = struct{}{}
}

func (h *Hub) remove(c *hubConn) {
	h.mu.Lock()
	defer h.mu.Unlock()
	k := userKey(c.userID)
	delete(h.users[k], c)
	if len(h.users[k]) == 0 {
		delete(h.users, k)
	}
}

// enqueue drops the message rather than blocking when a client can't keep up.
func (h *Hub) enqueue(c *hubConn, msg []byte) {
	select {
	case c.send <- msg:
	default:
		h.log.Warn("dropping SignalR message for slow client", "userId", c.userID.String())
	}
}

// SendToUser invokes a client method on every connection of the given user.
func (h *Hub) SendToUser(userID GUID, target string, arg any) {
	payload, err := json.Marshal(map[string]any{"type": 1, "target": target, "arguments": []any{arg}})
	if err != nil {
		h.log.Error("failed to encode SignalR message", "target", target, "error", err)
		return
	}
	payload = append(payload, recordSeparator)

	h.mu.Lock()
	conns := make([]*hubConn, 0, len(h.users[userKey(userID)]))
	for c := range h.users[userKey(userID)] {
		conns = append(conns, c)
	}
	h.mu.Unlock()
	for _, c := range conns {
		h.enqueue(c, payload)
	}
}
