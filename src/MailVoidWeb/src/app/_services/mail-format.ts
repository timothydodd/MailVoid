// Small display helpers shared by the mail list, mail view and user menu.

/** Two-letter initials from a display name ("Jane Doe" → "JD") or an address ("qa@x.com" → "QA"). */
export function initialsFor(value: string | null | undefined): string {
  const v = (value ?? '').trim();
  if (!v) return '?';
  const local = v.includes('@') && !v.includes(' ') ? v.split('@')[0] : v;
  const words = local.split(/[\s._-]+/).filter(Boolean);
  if (words.length >= 2) return (words[0][0] + words[1][0]).toUpperCase();
  return local.slice(0, 2).toUpperCase();
}

/** Compact received time: "10:42 AM" today, "Nov 2" this year, "Nov 2, 2025" otherwise. */
export function shortMailDate(value: string | Date | null | undefined): string {
  if (!value) return '';
  const d = new Date(value);
  if (isNaN(d.getTime())) return '';
  const now = new Date();
  if (d.toDateString() === now.toDateString()) {
    return d.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' });
  }
  if (d.getFullYear() === now.getFullYear()) {
    return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
  }
  return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
}

/** Splits "local@domain" for display; domain is '' when there is no '@'. */
export function splitAddress(address: string): { local: string; domain: string } {
  const at = address.lastIndexOf('@');
  return at < 0 ? { local: address, domain: '' } : { local: address.slice(0, at), domain: address.slice(at + 1) };
}
