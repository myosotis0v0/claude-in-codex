# Claude companion: asset checkpoint

## Current revision: simplified eyes (v2)

The current user-approved direction supersedes the sclera-based design below. Use `assets/claude-v2/rig.json` and `tools/prepare_simple_eyes.py`. There are NO white eye sockets or lower eyelids. Two large orange eye shapes with dark edges move under the fixed upper lashes. Their fill is palette-normalized to solid orange, and the new 148 px width exceeds the previous 136 px width. The foreground and skin masks preserve hair and lashes. Preview: `previews/gaze-v2-preview.gif`. The earlier `assets/claude/` rig is retained only as a superseded draft, not for runtime integration.

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
