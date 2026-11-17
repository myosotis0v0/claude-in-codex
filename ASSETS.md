# Claude companion: asset checkpoint

## Offline Motion Studies V1

The separate `assets/claude-motion-v1` study contains completed face/hair/accessory/neck layers, original-style eyes, extra mouths, a 13-bone weighted mesh rig and 14 six-second motions. It uses SkiaSharp only in the offline `tools/MotionLab` authoring tool. It does not replace this document's installed v2 runtime art or add a dependency to the companion project. See [the study README](assets/claude-motion-v1/README.md), `previews/motion-batch-v1/index.html`, and the two animated overviews in that directory. The asset ZIP is a data bundle, not an installable plugin.

## Version 0.3.0: linked upper lashes

Runtime source assets remain under `plugins/claude-in-codex/assets/claude-v2`; `lids/` adds independently packaged skin, upper-lash, crease, aperture and foreground-matte layers, included in the 0.3.0 release. The v2 flat orange eye sprite is unchanged. No sclera or lower eyelid was added. Actual desktop behavior requires manual verification after restarting the installed companion.

Official references:

- [Live2D: Deform Paths](https://docs.live2d.com/en/cubism-editor-manual/deformpath/) describes path-driven ArtMesh deformation.
- [Live2D: Adding Facial Expressions](https://docs.live2d.com/en/cubism-editor-tutorials/expression/) demonstrates eyelash deformation and clipping.
- [Live2D: Eyeball X/Y keyforms](https://docs.live2d.com/en/cubism-editor-manual/keyform-xydirection/) demonstrates parameterized eye direction and corner shapes.

These references establish the deformation/masking approach, not an automatic SDK feature coupling gaze to lashes. Our lightweight WPF implementation does not embed Cubism. The specific curve and gains are artistic choices for this character: a squared-sine bow fixes the two endpoints and moves the center, with maximum offsets of -8/+12 source pixels. Creases move at 0.3 strength. The same smoothed vertical gaze selects one of 33 baked poses; full-size artwork is not warped every timer tick.

### Layer contract

- `base.png` remains unchanged, including its original small alpha variation. Neutral and purely horizontal views use the original single-base compositor, preserving its exact rasterization.
- `lids/generated-clean-base.png` is the built-in image-generation result used only to obtain skin colors behind the old lashes. Its changes outside extracted pixels are never adopted.
- `lids/skin-base.png` copies the original full image, replacing only extracted lash/crease RGB pixels and their two-pixel antialias fringe. Original alpha is retained. Drawing a full base avoids the former independently resampled rectangular face-patch seams.
- `lids/{left,right}-lashes.png` and `*-crease.png` retain original visible lash/crease colors. Per-eye `*-skin.png`, `*-foreground.png` and `*-aperture.png` expose the separated parts and fixed hair matte for inspection.
- `*-pose-00..32.png` contain bowed lash/crease layers, clipped against the fixed foreground. `*-mask-00..32.png` are the corresponding pupil apertures: the top follows the lash, the bottom stays anchored. Runtime draws skin, clipped eye, then lash. Existing hair remains fixed.
- The neutral layer assembly reconstructs original RGB byte-for-byte. Extracted layers are internally opaque because the original nominally opaque art has alpha 253/254; using its original alpha twice would cause edge tint. Runtime's exact neutral branch also preserves original alpha and rendering.

Rebuild the baked assets without opening any windows:

```powershell
dotnet run --project tools/LashAssetBuilder/LashAssetBuilder.csproj -c Release -- plugins/claude-in-codex/assets/claude-v2 plugins/claude-in-codex/assets/claude-v2/lids/generated-clean-base.png plugins/claude-in-codex/assets/claude-v2/lids
```

The builder validates neutral RGB reconstruction. Offscreen WPF diagnostics additionally compare neutral output at 224/256/280/336 pixels, fixed foreground-matte pixels, protected areas, lash center travel and thickness. Preview: `previews/lash-before-after.gif`, left fixed lashes versus right linked lashes on identical eye motion. `--lash-preview` performs no cursor access, host discovery, native-window display, settings persistence, IPC, installation or running-companion control. Real desktop behavior remains a manual verification step.

Generation used the built-in image tool, not an API/CLI fallback. Its exact edit prompt is saved in `tools/lash-underlay-prompt.txt`. The generated skin is an intermediate only; extraction and deterministic pose baking preserve the existing authored art wherever it is not necessary to reveal skin under the moving lashes.

## Current revision: simplified eyes (v2)

The current user-approved direction supersedes the sclera-based design below. Use `assets/claude-v2/rig.json` and `tools/prepare_simple_eyes.py`. There are NO white eye sockets or lower eyelids. Two large orange eye shapes with dark edges move under the fixed upper lashes. Their fill is palette-normalized to solid orange, and the new 148 px width exceeds the previous 136 px width. The foreground and skin masks preserve hair and lashes. Preview: `previews/gaze-v2-preview.gif`. The earlier `assets/claude/` rig is retained only as a superseded draft, not for runtime integration.

Runtime correction in v0.2: draw the whole base ONCE, then the masked moving eyes. Do not separately resample and draw the complementary skin-underlay/foreground images: source-over blending creates faint seams at their rectangular cut boundaries. The motion masks exclude lashes/hair, so they already provide foreground occlusion. Offscreen WPF tests compare the no-eyes result to a single base image at 224, 256, 280 and 336 pixels; the former split-pass renderer differed in 7243 channel values, while the new renderer matches within one channel unit everywhere.

Plugin icon: built-in image generation derived a simplified head from the approved character reference. `tools/prepare_icon.py` only resizes this artwork and exports PNG/ICO formats. The icon is static; it does not replace or change the approved animated eye sprites.

This checkpoint prepares the layered character artwork, not the installed Windows plugin.

## Files

- `assets/claude/base.png`: transparent generated character with empty eye sockets.
- `assets/claude/eye-underlay.png`: isolated eye whites for the bottom runtime layer.
- `assets/claude/iris.png`: cropped complete generated iris, including hidden upper section.
- `assets/claude/left-aperture.png`, `right-aperture.png`: fixed eye-interior masks.
- `assets/claude/foreground.png`: fixed character artwork above the irises, with eye interiors transparent. This preserves lashes, outlines, and hair occlusion.
- `assets/claude/rig.json`: canvas coordinates and movement limits.
- `previews/claude-small.png`: 256 px neutral composite.
- `previews/gaze-contact-sheet.png`: left/center/right comparison.
- `previews/gaze-preview.gif`: 256 px animated compositor preview.
- `previews/validation.json`: automated asset validation results.

## Composition contract

Draw eye-underlay, each iris clipped to its fixed aperture, then foreground. Do not also draw the full base; that would double-composite semitransparent outer edges. Move only the iris, never the mask or lashes. Both eyes share the same normalized gaze target. Smooth that target and cap the movement at the rig's maxOffset. All source positions are in the 1254 px canvas coordinate system; scale the whole rig together at runtime. Do not mirror the character: her flower belongs on the viewer's right.

The preview drives a synthetic gaze trajectory. It does not prove mouse tracking, Windows DPI handling, window following, usage fetching, dragging, or plugin installation. Those features remain the next implementation stage.

## Artwork provenance

The user supplied the original orange-haired character. The built-in image generation tool produced a transparent empty-eye base and a complete orange iris. Mechanical cropping, masking and preview compositing use Pillow in `tools/prepare_assets.py`. The base is a generated derivative, not a promise of pixel-identical preservation of the original. Character rights remain with their respective owners; no license for the supplied artwork is inferred.

Generation brief: preserve the original frontal pose, half-lidded expression, orange hair, right flower, ribbons and hands; remove the bottom tabletop; preserve fixed eye outlines and lashes; replace original irises with white sclera; true transparent background. Generate a separate full orange oval iris with dark outline and matching shading, without highlights or an added pupil dot.

No application files or settings were changed. No plugin was installed and no repository push was performed at this checkpoint.
