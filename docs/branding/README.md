# Moushift visual assets

The approved mark combines two rounded mouse-button strokes into an M with a downward, star-shaped central cutout. The upper black bridge remains connected; the white star stops inside the upper V boundary. The application uses a theme-aware vector; the existing light/dark palettes are unchanged.

- `moushift-concept.png`: approved black/white reference, generated with the built-in image generation tool and selected on 2026-11-07.
- `moushift-mark.svg`, `moushift-mark.png`, `Moushift.ico`: production geometry authored in `src/Clickloom.App/BrandMark.cs`. Reproduce with `Moushift.exe --export-brand-assets <output-directory>`. The ICO includes 32, 64 and 256 px images; the executable uses the copy in `Assets/Moushift.ico`.
- `src/Clickloom.App/Assets/mouse-left.png`: original transparent side illustration generated using the approved `mouse.png` top view as a design reference. It is generic artwork, not an assertion about the physical layout of a particular brand. Existing saved marker coordinates are not rewritten.

## Image generation prompts

### Side artwork

Use case: identity-preserve product illustration. The input image is the approved TOP view of our original generic mouse, provided only to preserve its design language. Produce a new LEFT SIDE ELEVATION of this same mouse, nose/front points LEFT, rounded palm/back points RIGHT. It must look like a carefully drawn industrial product illustration, with believable low ergonomic silhouette, smoothly curved shell and generous thumb recess, two slim elongated silver thumb buttons near the front upper flank, a subtle small sniper paddle below them, and a silver wheel partially embedded at the top front (never floating above the shell). Show the entire mouse centered in a wide horizontal composition, roughly 10 percent transparent margins. Graphite grey satin shell, restrained soft shading, fine dark outlines, no noisy patterns, NO diagonal hatch lines, no oversized rooflike hump, no cartoon shoe shape. Match the simple polished grey illustration of the supplied top view rather than a photoreal render. True transparent background, no floor plane, no text, no branding, no circles, no button labels, no neon or glow. Clear separations of clickable button regions, intended as a functional diagram in a mouse configuration app.

### Initial logo exploration

Design a professional original logo symbol for Moushift, a desktop input customization app. Pure black ink on pure white background, exactly two colors. A compact ingenious abstract M: two thick rounded vertical mouse-button shapes tilted/offset slightly, cut by a sharp upward-right cursor-shaped negative space. The idea is switching controls with a click. Large confident black masses, very clear white negative spaces, optical balance, distinctive silhouette, instantly readable at tiny sizes. Flat 2D vector logo like an actual SVG rendered at high resolution. No text, no mockup, no glow, no gradient, no lighting, no texture, no shadows, no circles surrounding it, no clipart mouse. Centered symbol only with white margins.

### Approved refinement

Preserve the connected black rounded M silhouette and its unequal shoulder heights. Keep the central white star's two upper horns, small side points and downward tip. Extend the star upward toward the upper V boundary while retaining a continuous black band above it. The interior white star must not connect to the exterior background at the top. Preserve the lower waist and both rounded legs. Pure black and white, without effects or annotations.

The production vector traces the approved reference rather than shipping its white background. Header color follows the active theme; tray and executable assets use the existing muted rose accent. Exported PNG/ICO backgrounds are transparent.
