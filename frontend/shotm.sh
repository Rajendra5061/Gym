#!/usr/bin/env bash
# Capture an authenticated SPA route at TRUE phone width (390x844 CSS).
#   usage: ./shotm.sh <route> <outfile> [user] [pass] [extra-localStorage]
#
# Headless Chrome clamps its window width to ~500px, so a --window-size=390 capture silently
# renders a ~520px layout — phone bugs hide and phantom ones appear. The dodge: keep the
# window at 520px, load the route in a same-origin 390x844 iframe (media queries inside an
# iframe track the iframe, not the window), then crop the screenshot down to the iframe.
# SCALE (device pixels per CSS px, default 2 for retina) multiplies the crop.
#
# FULL=1 captures the ENTIRE page, not one screenful: the app shell pins itself to the
# viewport and scrolls inside .app-body, so the bootstrap unlocks those heights, grows the
# iframe to the page's real height (clamped by MAXH, default 3400 CSS px), and reports the
# final height through the DOM, which this script reads back to crop exactly.
set -euo pipefail

CHROME="/c/Program Files/Google/Chrome/Application/chrome.exe"
FFMPEG="${FFMPEG:-ffmpeg}"
ROUTE="${1:?route required}"; OUT="${2:?output path required}"
U="${3:-admin}"; P="${4:-123@}"; EXTRA="${5:-}"; CLICK="${6:-}"
DSF="${SCALE:-2}"; FULL="${FULL:-0}"; MAXH="${MAXH:-3400}"; WIDE="${WIDE:-0}"
if [ "$FULL" = "1" ]; then WINH=$((MAXH + 100)); BUDGET=25000; else WINH=900; BUDGET=15000; fi
if [ "$WIDE" = "1" ]; then WINW=2500; else WINW=520; fi

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BOOT="$HERE/public/_shotm.html"
mkdir -p "$HERE/public"

cat > "$BOOT" <<'HTML'
<!doctype html>
<meta charset="utf-8">
<title>phone capture bootstrap</title>
<body style="margin:0;background:#f4f6fb">
<script>
(async () => {
  const p = new URLSearchParams(location.hash.slice(1));
  const to = p.get('to'), user = p.get('u'), pass = p.get('p');
  for (const pair of (p.get('set') || '').split(';').filter(Boolean)) {
    const i = pair.indexOf('='); if (i > 0) localStorage.setItem(pair.slice(0, i), pair.slice(i + 1));
  }
  if (!to || !user || !pass) { document.body.textContent = 'MISSING PARAMS'; return; }
  // user "none": photograph the logged-out experience (login pages redirect the signed-in).
  if (user === 'none') {
    localStorage.removeItem('gym.accessToken');
    localStorage.removeItem('gym.refreshToken');
  } else try {
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
  const fr = document.createElement('iframe');
  fr.src = to;
  fr.style.cssText = 'width:390px;height:844px;border:0;display:block';
  document.body.appendChild(fr);
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));
  // Optional: click an element once it exists, to photograph an opened drawer or menu.
  const click = p.get('click');
  if (click) {
    for (let i = 0; i < 60; i++) {
      await sleep(250);
      const el = fr.contentDocument && fr.contentDocument.querySelector(click);
      if (el) {
        // Compositor-driven transitions don't finish under Chrome's virtual clock, so a
        // sliding drawer would be photographed mid-flight. Freeze motion, then click.
        const st = fr.contentDocument.createElement('style');
        st.textContent = '* { transition: none !important; animation: none !important; }';
        fr.contentDocument.head.appendChild(st);
        el.click();
        break;
      }
    }
  }
  // Full-page mode: unpin the shell so the document grows to its content, then match the
  // iframe to it and leave the measured height where --dump-dom can read it.
  if (p.get('full') === '1') {
    let tries = 0;
    while (tries++ < 60 && !(fr.contentDocument && fr.contentDocument.querySelector('.page, .pub-page, main, form'))) await sleep(250);
    await sleep(2000);
    const d = fr.contentDocument;
    const st = d.createElement('style');
    st.textContent = 'html, body, #root, .app, .app-main, .pub-shell { height: auto !important; min-height: 0 !important; } ' +
      '.app-body, .pub-shell { overflow: visible !important; }';
    d.head.appendChild(st);
    await sleep(700);
    const maxH = Number(p.get('maxh')) || 3400;
    const h = Math.min(Math.max(d.documentElement.scrollHeight, 844), maxH);
    fr.style.height = h + 'px';
    await sleep(700);
    // sx=1: photograph the same page with every table pushed to its right edge, so a
    // film can dissolve between the two and show the columns a phone hides.
    if (p.get('sx') === '1') {
      for (const w of d.querySelectorAll('.table-wrap')) w.scrollLeft = w.scrollWidth;
      await sleep(400);
    }
    // wide=1: photograph the page with the first table UNCLIPPED, for a film to pan over.
    // Phone cells are nowrap, so the table's column widths are identical clipped or not —
    // the wide render is pixel-compatible with the base one. The phone styling must
    // survive the wider viewport, so every media rule that applies at 390px is re-injected
    // without its condition before the iframe grows past the breakpoints.
    if (p.get('wide') === '1') {
      let frozen = '';
      for (const sh of d.styleSheets) {
        let rules; try { rules = sh.cssRules; } catch (e) { continue; }
        for (const r of rules) {
          if (r.type === CSSRule.MEDIA_RULE && fr.contentWindow.matchMedia(r.media.mediaText).matches) {
            for (const rr of r.cssRules) frozen += rr.cssText + '\n';
          }
        }
      }
      const fz = d.createElement('style'); fz.textContent = frozen; d.head.appendChild(fz);
      const wrap = d.querySelector('.table-wrap');
      const table = wrap && (wrap.querySelector('.table') || wrap.firstElementChild);
      if (wrap && table) {
        // The wrap rect anchors the film's overlay, so it must match the settled page —
        // wait out any straggling data fetch that is still adding rows.
        let lastH = 0;
        for (let i = 0; i < 10; i++) {
          const hNow = wrap.getBoundingClientRect().height;
          if (hNow === lastH && hNow > 0) break;
          lastH = hNow; await sleep(600);
        }
        const r1 = wrap.getBoundingClientRect();
        const un = d.createElement('style');
        un.textContent = '.table-wrap { overflow: visible !important; width: max-content !important; max-width: none !important; }';
        d.head.appendChild(un);
        const targetW = Math.min(Math.ceil(table.scrollWidth + r1.left + 60), 2400);
        fr.style.width = targetW + 'px';
        await sleep(700);
        const r2 = table.getBoundingClientRect();
        const m = document.createElement('div'); m.id = 'rects';
        m.textContent = [r1.left, r1.top, r1.width, r1.height,
          r2.left, r2.top, r2.width, r2.height, targetW].map(Math.round).join(',');
        document.body.appendChild(m);
      }
    }
    const marker = document.createElement('div');
    marker.id = 'fullh';
    marker.textContent = String(h);
    document.body.appendChild(marker);
  }
})();
</script>
HTML

# The production preview serves dist/, so the bootstrap must exist there too.
if [ -d "$HERE/dist" ]; then cp "$BOOT" "$HERE/dist/_shotm.html"; fi

PROFILE="$(mktemp -d)"
RAW="$(mktemp -u).png"
trap 'rm -f "$BOOT" "$RAW"; rm -rf "$PROFILE"' EXIT

# reduced-motion: charts and transitions draw in their finished state instead of being
# photographed mid-animation (the app honours the preference; virtual time stalls JS tweens).
# --dump-dom rides along to carry the measured page height back out in full mode.
DOM="$("$CHROME" --headless=new --disable-gpu --hide-scrollbars --no-first-run --force-device-scale-factor="$DSF" \
  --force-prefers-reduced-motion \
  --user-data-dir="$PROFILE" --window-size="$WINW,$WINH" --virtual-time-budget="$BUDGET" \
  --screenshot="$(cygpath -w "$RAW")" --dump-dom \
  "${BASE_URL:-http://localhost:5175}/_shotm.html#to=$ROUTE&u=$U&p=$P&set=$EXTRA&click=$CLICK&full=$FULL&maxh=$MAXH&sx=${SCROLLX:-0}&wide=$WIDE" 2>/dev/null)"

[ -s "$RAW" ] || { echo "capture produced no file for $ROUTE" >&2; exit 1; }
CROPH=844; CROPW=390
if [ "$FULL" = "1" ]; then
  CROPH="$(printf '%s' "$DOM" | grep -o 'id="fullh">[0-9]*' | grep -o '[0-9]*$' || true)"
  [ -n "$CROPH" ] || { echo "full-page height missing for $ROUTE" >&2; exit 1; }
fi
if [ "$WIDE" = "1" ]; then
  RECTS="$(printf '%s' "$DOM" | grep -o 'id="rects">[0-9,]*' | grep -o '[0-9,]*$' || true)"
  [ -n "$RECTS" ] || { echo "table rects missing for $ROUTE" >&2; exit 1; }
  CROPW="$(printf '%s' "$RECTS" | cut -d, -f9)"
  printf '%s\n' "$RECTS" > "$OUT.rects"
fi
"$FFMPEG" -y -i "$RAW" -vf "crop=$((CROPW*DSF)):$((CROPH*DSF)):0:0" "$OUT" 2>/dev/null
[ -s "$OUT" ] || { echo "crop failed for $ROUTE" >&2; exit 1; }
echo "$(md5sum "$OUT" | cut -c1-8)  $OUT"
