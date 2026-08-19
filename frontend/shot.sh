#!/usr/bin/env bash
# Capture an authenticated SPA route to a PNG.
#   usage: ./shot.sh <route> <outfile> [width] [height] [user] [pass]
#
# The SPA keeps its tokens in localStorage, so the capture has to sign in on the app's own
# origin. This writes a throwaway bootstrap page into public/, uses it, then deletes it —
# public/ is copied verbatim into a production build, and a page that performs an admin
# login has no business shipping there. Credentials travel in the URL fragment, which the
# browser never sends to the server, so they stay out of the dev-server request log.
set -euo pipefail

CHROME="/c/Program Files/Google/Chrome/Application/chrome.exe"
ROUTE="${1:?route required}"; OUT="${2:?output path required}"
W="${3:-1440}"; H="${4:-900}"; U="${5:-admin}"; P="${6:-123@}"; EXTRA="${7:-}"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BOOT="$HERE/public/_shot.html"
mkdir -p "$HERE/public"
trap 'rm -f "$BOOT"' EXIT

cat > "$BOOT" <<'HTML'
<!doctype html>
<meta charset="utf-8">
<title>capture bootstrap</title>
<body style="background:#f4f6fb">
<script>
(async () => {
  const p = new URLSearchParams(location.hash.slice(1));
  const to = p.get('to'), user = p.get('u'), pass = p.get('p');
  for (const pair of (p.get('set') || '').split(';').filter(Boolean)) {
    const i = pair.indexOf('='); if (i > 0) localStorage.setItem(pair.slice(0, i), pair.slice(i + 1));
  }
  if (!to || !user || !pass) { document.body.textContent = 'MISSING PARAMS'; return; }
  try {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userNameOrEmail: user, password: pass })
    });
    const body = await res.json();
    const data = body.data || {};
    if (!data.accessToken) { document.body.textContent = 'LOGIN FAILED: ' + (body.message || res.status); return; }
    localStorage.setItem('gym.accessToken', data.accessToken);
    localStorage.setItem('gym.refreshToken', data.refreshToken || '');
  } catch (err) { document.body.textContent = 'LOGIN ERROR: ' + err; return; }
  location.replace(to);
})();
</script>
HTML

PROFILE="$(mktemp -d)"
trap 'rm -f "$BOOT"; rm -rf "$PROFILE"' EXIT

"$CHROME" --headless=new --disable-gpu --hide-scrollbars --no-first-run \
  --user-data-dir="$PROFILE" --window-size="$W,$H" --virtual-time-budget=15000 \
  --screenshot="$(cygpath -w "$OUT")" \
  "http://localhost:5173/_shot.html#to=$ROUTE&u=$U&p=$P&set=$EXTRA" 2>/dev/null

# A capture that silently fell back to the login or home page is worse than no capture,
# so make the caller aware the file is at least distinct from a known-unauthenticated one.
[ -s "$OUT" ] || { echo "capture produced no file: $OUT" >&2; exit 1; }
echo "$(md5sum "$OUT" | cut -c1-8)  $OUT"
