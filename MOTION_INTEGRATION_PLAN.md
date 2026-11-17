# Motion Runtime Integration

## Implemented in 0.4.0

- Shared rig/pose/renderer code now lives in `plugins/claude-in-codex/src/MotionCore`, used by both WPF and offline MotionLab.
- Cropped CPU textures, cached shaders/UVs and source-alpha triangle culling feed a reusable WPF pixel buffer. Only x64 native Skia is published, without PDBs. The previous renderer remains available on motion-load failure.
- The visual artwork square keeps its previous scale/offsets. Native overscan is added only above/left and clamped to the host origin; bottom/right remain anchored to the original placement. Popup coordinates compensate for this internal overscan.
- Idle/blink, real gaze, additive wind, expansion wink, long-idle sleepy, locked head hold/pat and confirmation nod are wired. Other authored reactions remain reserved rather than being mislabeled as application events.
- Installed Codex CLI 0.169.0-alpha.11.1 exports `UserPromptSubmit`, `Stop`, `Interrupt` and `SessionEnd` in its generated hook schema. These official hooks can bridge aggregate task activity, contrary to the pre-integration limitation below. A small three-dot bubble accompanies thinking. This is not hidden reasoning, nor the selected desktop chat; native delivery/trust still needs user verification.
- Anonymous hook events are atomic, prompt-free and consumed locally. Duplicate/out-of-order stops and concurrent sessions are tested. Missing terminals expire to unknown after two hours. Hook trust is never bypassed.
- Automated verification is offscreen only; no mouse/keyboard input or control of the user's running companion. The README includes the combined static state gallery and explains actual versus reserved triggers.

## Pre-Integration Snapshot

- Production remains the installed 0.3.0 WPF companion: native transparent window, window following/hiding, persistent launcher and quota panel.
- `PetView.Look` already receives normalized cursor intent through `CompanionWindow`'s 33 ms gaze timer. It caps inward eye travel and handles the approved simple orange eyes.
- `UsageClient` owns a separate app-server connection and receives real account quota. It does not observe the active Codex chat's reasoning, message completion or speech audio.
- The existing SessionStart hook starts the helper; it is not a general app-status feed.
- MotionLab is currently an offline SkiaSharp renderer. The 14 clips are parameter/rig authoring data, not GIFs to be overlaid in the live application.

## Runtime Shape

1. Extract/share a small renderer and pose-data library from MotionLab. Keep the WPF window, launcher, quota/settings UI and lifecycle unchanged in outward behavior.
2. Add only the Windows x64 Skia render dependency, not Electron/WebView/Cubism Core. The locally inspected native DLL is about 13 MB; its roughly 99 MB PDB must explicitly be excluded from publishing. Measure the final publish delta rather than estimating from the NuGet download size.
3. Crop transparent texture padding and store each part's source origin/UV rectangle. Current source textures decode to about 57 MB of RGBA; bounding-box cropping would reduce the raw pixel requirement to about 12 MB. This is a data estimate, not a measured resident-memory result. Cache textures, shaders, UVs and topology; update only small pose/vertex buffers per frame.
4. Feed the rendered CPU surface into the WPF view with a reusable bitmap buffer. Benchmark the live-sized surface before selecting a final 15/30 FPS policy. Rendering/timers stop when hidden or collapsed. Do not load GIF/WebP preview files or full sprite atlases into production.

The 1400-source-unit preview canvas has safety margins around the 1254-unit artwork. Do not simply squeeze it into the old window and shrink the character. Preserve the approved visual scale and paw/bottom anchor; reserve overscan in the internal view, and use the visual anchor for following/popup placement and hit testing. Bone motion must not animate the actual native window position or reset dragged offsets.

## Layered Controller

- **Base:** restrained breathing and independent blink scheduling.
- **Real gaze:** retain the existing cursor mapping and inward/outward limits. Authored gaze overrides blend in only for expressions explicitly looking away; do not replace normal cursor tracking with the preview's simulated path.
- **Reaction:** expression/body/head pose with smooth entry/exit, cooldown and nonstacking priority. Preserve single-click quota access and unlocked dragging.
- **Hair:** persistent global wind phase plus authored motion/head input, then the shared two-stage response. The ambient contribution fades down under existing hair gestures. Keep the same spring state across expression changes.
- **Render order:** rear hair → neck → face/eyes/brows/mouth → front hair → accessory/ribbons → front clothes/paws.

Add one styled **发丝微风** switch under the existing collapsed other-settings section, default on. Do not introduce many always-visible animation controls. Keep animation and instant-gaze preferences separate: the current `ReducedMotion` flag actually controls instantaneous gaze and must not silently be repurposed.

## Honest Event Mapping

| Source available now | Initial integration |
| --- | --- |
| Visible, expanded companion | Idle breath/blink with ambient hair |
| Real cursor | Gaze and restrained head follow |
| Locked character, held on head | Optional head-pat reaction after a hold threshold; short click still opens quota |
| Unlocked drag | Drag wins; no head-pat reaction during drag |
| First successful quota read / user-confirmed setting operation | Brief nod or smile, with cooldown rather than every periodic refresh |
| This plugin reading quota / quota failure / actual low-quota threshold | Optional small local feedback; do not label it as Codex reasoning |
| Hidden/minimized/collapsed | Suspend animations; resume without simulating the full hidden duration |

Thinking/success/error/speech states of the actual active Codex task are not currently known. Leave explicit interfaces for future verified hooks/events. Do not infer them from CPU use, window title or an unrelated owned app-server, and do not read private chat databases or capture audio to fake the feature. The talk clip remains a manual demonstration until real audio/viseme data is deliberately connected.

## Gates Before Installation

1. Correct the under-chin support and add seam tests; apply persistent wind in source previews. This is the current offline change, not a runtime rollout.
2. Share renderer/controller, crop parts, and benchmark cached rendering at actual size/DPI. Inspect publish contents and exclude symbols/other architectures.
3. Regress visual scale, hit areas, eye occlusion, inward limits, pinned paws, state transitions, hidden timers, popup bottom anchor and saved offsets. Start with the existing old renderer available as a fallback.
4. Publish a separately versioned plugin only after the runtime change is implemented and checked. Do not kill/restart the user's process; the user manually restarts and verifies window behavior and motion feel.
