# Motion Studies V1: Delivery Audit

## Scope

This delivery fulfills the artwork, separation/completion, motion batch and offline preview request. It does not install new assets, wire motions to real Codex status, install a touch listener, or verify interactive desktop behavior. The running 0.3.0 companion remains separate.

| Requirement | Current evidence | Result |
| --- | --- | --- |
| Reference existing Live2D/desktop companions | Primary-source references and applicability limits in `assets/claude-motion-v1/README.md` | Covered |
| Use a mature component where suitable; stay lightweight | SkiaSharp 4.156.1 in `tools/MotionLab` only; no companion project dependency; explicit payload file manifest | Covered |
| Separate/completed parts | 16 clean PNG layers including independent rear support; part-order manifest; `parts.png` | Covered |
| Keep original simplified eye style and right-side accessory | Original 148x244 orange eye; no opaque white eye pixels; right-side accessory bounds verified | Covered |
| Hair and other dynamics | 13 bones, four-weight mesh skinning, fixed-step two-stage response; persistent additive wind under quiet-hair states with authored-gesture suppression | Covered |
| Facial expressions and interactions | 14 named six-second motions; six new mouths plus neutral; independent eyebrows and lids | Covered as preview, not event integration |
| Batch animated previews | 14 GIFs and transparent WebPs, plus skeleton demo; 180 source frames each, 30 FPS, decoded duration 6000 ms | Covered |
| Review views | Two animated overviews, per-clip media, part sheet, standalone local frame/speed/theme inspector | Covered; browser interaction is manual |
| Own QA | 108 rig checks and 221 delivery checks, reports in this directory; latest overviews/parts inspected | Covered within documented offline scope |
| No mouse/keyboard/app manipulation | Authoring/export/validation scripts only; no installed-plugin edit or host-control code in MotionLab | Preserved |

## Checks and Corrections

2026-11-17: the user correctly identified excessive darkness under the chin. A missing rear support caused genuine transparent pixels, and the single wig's lower face-hole rim added a second outline in front of the collar. The prior face-interior sampling did not cover this seam. An independent rear curtain now renders behind face/neck, and the front U-rim is feathered away. Every motion now also checks a 36-point under-chin corridor per frame. `chin-before-after.gif` compares saved old frames with the corrected batch.

Ambient wind is a separate globally timed pose channel, not a mutually exclusive state or a replacement for authored `Wind`. `ambient-wind-comparison.gif` uses the same idle/thinking/talk pose sequence with only ambient input changed, keeping persistent spring instances. Dedicated tests cover periodicity, independent local/global time, and suppression under authored hair gestures.

The first color-backfilled draft was rejected: the hair layer picked up the original chest flower, and the face picked up local color-difference patches. The accepted layers use coherent completed sources instead, with original eye/brow/neutral-mouth parts. The final delivery audit verifies opaque RGB provenance for the completed parts to catch this regression.

A row-convex alpha check was replaced with exterior flood-fill because the intended cheek notch is concave, not a face hole. The accepted face has no enclosed low-alpha holes. Dynamic interior samples, pinned-paw samples, triangle orientation, fixed-frame bounds and periodic spring continuity are also checked. These tests do not prove every conceivable future pose or physical desktop behavior.

`delivery-validation.json` records decoded-media durations, temporal difference/seam measurements, atlas-to-source RGBA comparisons, actual asset hashes, ZIP CRC and local gallery JavaScript syntax validation. `rig-validation.json` records the bone/render checks. `motion-assets-v1.zip` contains only the explicitly hashed layers/rig/motion data plus documentation, not native Skia binaries or preview atlases.

Generation prompts/briefs and provenance are in `assets/claude-motion-v1/generation.json`. Character-art rights remain with the respective owners; no redistribution license for the supplied character is implied.
