# LuxMap icons

Hand-written, flat vector artwork based on `../luxmap-logo.png`. No tracing,
embedded images, fonts, gradients or effects.

| Colour | Exact sampled hex | Source pixel (x, y; zero-based) |
| --- | --- | --- |
| Navy | `#00294E` | (180, 280) |
| Teal | `#016975` | (300, 450) |
| Amber | `#FCA81D` | (400, 240) |
| White | `#FFFFFF` | Specified background / lamp |

The PNG has slight tonal variation; these three interior RGB samples define the
flat palette. All coloured artwork uses these exact values.

## Files

- `luxmap-mark.svg`: transparent mark, tight viewBox.
- `luxmap-icon.svg`: 1024-square white app icon, optically positioned to balance
  the pin against its route extending to the right.
- `luxmap-icon-maskable.svg`: white square with smaller, mask-safe artwork.
- `luxmap-icon-mono.svg`: navy silhouette with a transparent lamp cutout;
  solid route nodes, for themed / notification use. Platform tint supplies colour.
- `favicon.svg`: thicker lamp, larger bulb, simplified road, no route nodes.
- `png/`: `icon-{1024,512,192}.png`, `icon-maskable-{512,192}.png`,
  `apple-touch-icon-180.png`, `favicon-{48,32,16}.png`, `mark-512.png`.
- `favicon.ico`: embedded 16, 32 and 48 px PNG images.
- `android/res/`: foreground and monochrome VectorDrawables, white background
  colour, adaptive launcher XML, and square legacy launcher PNGs at
  mdpi/hdpi/xhdpi/xxhdpi/xxxhdpi (48/72/96/144/192 px).
- `android/playstore-icon-512.png`: opaque store artwork.

## Installation

Web: copy the SVGs, ICO and required PNGs into your app's `public/` directory
(flatten `png/` for these example URLs), then add to `index.html`:

```html
<link rel="icon" href="/favicon.ico" sizes="16x16 32x32 48x48">
<link rel="icon" href="/favicon.svg" type="image/svg+xml" sizes="any">
<link rel="apple-touch-icon" href="/apple-touch-icon-180.png" sizes="180x180">
```

In the existing web manifest, use `icon-192.png` and `icon-512.png` with
`purpose: "any"`, and `icon-maskable-192.png` and `icon-maskable-512.png` with
`purpose: "maskable"` (each `type: "image/png"`, with matching `sizes`).

Android: merge `android/res/` into `app/src/main/res/`, replacing the default
launcher resources. Manifest references are `@mipmap/ic_launcher` and
`@mipmap/ic_launcher_round`. Both adaptive XML files include the Android 13
`monochrome` layer; older versions use background and foreground. Compile with
SDK 33 or newer. Upload the separate Play Store PNG in the store console.
The monochrome drawable can also be referenced as a notification small icon.

## Safe zones and regeneration

All app backgrounds fill the square; no corners or circular masks are baked in.
PWA maskable coloured content fits the central circle centred at (512, 512),
radius 409.6 (80% diameter). Android vectors use the standard 108dp viewport;
content fits the central 66dp-diameter circle centred at (54, 54), radius 33.
The route endpoints are included in both safety checks. Legacy round-named PNGs
remain opaque squares so the launcher controls masking. The standalone mark and
monochrome SVGs have transparent backgrounds; favicons are transparent too.

From the repository root:

```sh
scripts/branding/render-icons.sh
```

Requires Python 3 (stdlib only), macOS `sips`, and a locally installed Cairo
library (this machine: `/opt/homebrew/lib/libcairo.2.dylib`). The companion Python
script renders the SVG geometry with Cairo at 4× resolution, downsamples with
`sips`, and writes PNG-in-ICO entries. It supports only the explicit SVG subset
used here and fails on unsupported geometry. No downloads are needed.
Android XML is a hand-written equivalent of the SVG geometry; update it alongside
any SVG design changes. The command regenerates all PNG/ICO exports, not XML.
