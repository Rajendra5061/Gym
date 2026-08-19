# Public imagery

Each slot is a stack of three layers: a real photograph (`.jpg`), a drawn scene (`.svg`), and a
plain gradient. The browser paints the topmost layer that exists, so a missing or failed photo
falls back to the scene and never to a broken-image icon. Replacing a `.jpg` here is the whole
job — no code changes.

| Slot | Where it appears | Photo (`.jpg`) | Drawn fallback (`.svg`) |
|---|---|---|---|
| `gym-floor` | Member login panel, and the home "member portal" section | **present** — cable-row photo, resized to 1200px | gym floor at night |
| `gym-admin` | Admin login panel | **present** — dumbbell-press photo, resized to 1400px | trainer with a tablet |
| `gym-hero`  | Home hero frame | **not supplied yet** — the drawn sunset room shows meanwhile | wide room at sunset |

Guidance for replacements:
- Landscape, at least 1200px wide (portrait works for `gym-floor`, whose panel is tall).
  The panels crop to fill, centred, so keep the subject central.
- Keep each under ~400KB — these load before anyone can sign in, often on a phone.
  The two present were resized from originals with PowerShell/System.Drawing at quality 80.
- Photographs of identifiable people go public with the repository. Use pictures the gym has
  the right to publish.
