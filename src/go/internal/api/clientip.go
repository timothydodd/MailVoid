package api

import (
	"net"
	"net/http"
	"net/netip"
	"strings"

	"mailvoid/internal/config"
)

// defaultTrustedNetworks are the proxies whose X-Forwarded-For is believed when
// ForwardedHeaders:KnownNetworks / KnownProxies aren't configured: loopback and private
// ranges, which covers an in-cluster ingress such as Traefik on k3s (10.42.0.0/16).
var defaultTrustedNetworks = []string{
	"127.0.0.0/8", "::1/128",
	"10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16",
	"fc00::/7",
}

// loadTrustedProxies reads the ASP.NET-style ForwardedHeaders settings
// (KnownNetworks as CIDRs, KnownProxies as single addresses).
func loadTrustedProxies(cfg *config.Config) []netip.Prefix {
	entries := cfg.Strings("ForwardedHeaders:KnownNetworks")
	for _, p := range cfg.Strings("ForwardedHeaders:KnownProxies") {
		if addr, err := netip.ParseAddr(strings.TrimSpace(p)); err == nil {
			entries = append(entries, netip.PrefixFrom(addr, addr.BitLen()).String())
		}
	}
	if len(entries) == 0 {
		entries = defaultTrustedNetworks
	}
	var out []netip.Prefix
	for _, e := range entries {
		if p, err := netip.ParsePrefix(strings.TrimSpace(e)); err == nil {
			out = append(out, p.Masked())
		}
	}
	return out
}

func (s *Server) isTrustedProxy(addr netip.Addr) bool {
	addr = addr.Unmap()
	for _, p := range s.trustedProxies {
		if p.Contains(addr) {
			return true
		}
	}
	return false
}

// clientIP is the address used for rate limiting, the captured-webhook SourceIp and
// request logs. X-Forwarded-For is only honoured when the direct peer is a trusted
// proxy, and then the right-most entry that isn't itself a trusted proxy wins, so a
// client can't spoof its address by sending its own header.
func (s *Server) clientIP(r *http.Request) string {
	peer := remoteIP(r)
	peerAddr, err := netip.ParseAddr(peer)
	if err != nil || !s.isTrustedProxy(peerAddr) {
		return peer
	}
	var hops []string
	for _, v := range r.Header.Values("X-Forwarded-For") {
		for _, h := range strings.Split(v, ",") {
			if h = strings.TrimSpace(h); h != "" {
				hops = append(hops, h)
			}
		}
	}
	for i := len(hops) - 1; i >= 0; i-- {
		addr, err := netip.ParseAddr(hops[i])
		if err != nil {
			return peer // malformed chain: fall back to the connection address
		}
		if !s.isTrustedProxy(addr) || i == 0 {
			return addr.Unmap().String()
		}
	}
	return peer
}

// remoteIP is the connection's peer address.
func remoteIP(r *http.Request) string {
	host, _, err := net.SplitHostPort(r.RemoteAddr)
	if err != nil {
		return r.RemoteAddr
	}
	return host
}
