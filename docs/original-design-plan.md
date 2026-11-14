# Sand mandala web app: research and implementation plan

Research date: November 12, 2026. Scope: plan a separate application at `apps/mandala`; implementation and deployment are future work.

Implementation update: the user subsequently selected physical correction and requested the page at `/mandala`. The first playable version now lives in `apps/web/app/mandala`, within the existing Next.js application, rather than a separate workspace. It includes layered sand, local guide feedback, a vacuum that removes every color, a sweep brush, undo, browser-local saves, sound, and PNG export. The separate-workspace architecture below remains an earlier proposal. Production build, engine tests, and desktop/mobile-emulation browser checks passed; real-device performance and playtesting remain future validation.

## Recommendation

Build a short, guided sand art experience: choose a pattern, pour colored sand carefully along its guides, pause to appreciate the result, then sweep it into a mixed pile and clear the tray. Start with a single beautiful pattern and prove that both pouring and sweeping feel good before building a pattern library.

This is a promising design hypothesis, not a validated business. Existing products demonstrate sand play, guided coloring, and creation followed by release, including a direct mandala competitor. They do not establish demand for this particular combination. The distinctive proposition should be **meticulous placement with a flowing tool, followed by tactile sweeping**.

The main design tension is controllability: realistic sand can ruin a precise picture, while automatic filling can make the player's hand feel irrelevant. Offer assistance that preserves local, continuous placement. Never fill an entire region from a click.

## Research: comparable experiences

These findings come from developer accounts, store descriptions, and museum sources, not hands-on playtests. Descriptions of physics and sensory quality are the developers' claims unless stated otherwise. Availability can vary by storefront and region.

| Example and source | What is established | What to study for this app |
| --- | --- | --- |
| [Thisissand — creators' introduction](https://thisissand.wordpress.com/2008/05/12/play_with_us/) and [browser experience](https://thisissand.com/) | Colored digital sand as an art-making material; creators explicitly mention mandalas, color, sound, and repeatable creation. The live experience could not be fetched by the research tool. | Continuous pouring, color selection, and open-ended play. Its falling-sand presentation is a reference, not a model for gravity on a horizontal mandala tray. |
| [Sandspiel — developer's technical account](https://maxbittker.com/making-sandspiel/) and [play](https://sandspiel.club/) | Browser falling-sand sandbox; developer describes Rust/Wasm simulation, JS/GLSL fluid simulation, React UI, and WebGL rendering. | Clear material reactions and separation of simulation from UI. Multiple reactive elements are unnecessary for our first release. |
| [Impermanence: Sand Ritual — App Store](https://apps.apple.com/us/app/impermanence-sand-ritual/id6792363062?platform=vision) | Listing describes finger-drawn mirrored mandalas and a press-and-hold release that disperses the design. It explicitly adopts the creation/impermanence premise. Verified through the indexed store listing; direct page fetch was unavailable. | Closest conceptual comparison. Differentiate through actual pouring, gradual depth, guided regions, and a brush that moves material. Creation followed by destruction alone is already represented. |
| [Drizzl — developer overview](https://pranavgarg.in/drizzl/index.html) and [audio explanation](https://pranavgarg.in/drizzl/guides/live-asmr-sound/) | iPhone/iPad sand and fluid sandbox; developer describes live sound responding to particle motion, slicing/scooping, tilt, and supported-device haptics. | Sound should follow movement and settling, including after input stops. Compare quiet trickles, larger pours, and brush contact. Native Metal performance and iPhone haptics do not establish browser performance. |
| [Sandkeeper — Steam](https://store.steampowered.com/app/5220800/) | Unreleased listing currently gives March 9, 2027; colored sand bottle craft includes pouring, leaning, tapping, and keeping the results. Also describes sand boards and a symmetrical drawing tray. | Slow material manipulation and accessible completion. Its collecting/keeping loop differs from a release-focused session. Search previews returned an older Q4 2026 date; prefer the directly opened page, and recheck before publication. |
| [Graviton — Steam](https://store.steampowered.com/app/2137280/) | Released October 28, 2022, listed as Early Access; interactive falling-sand art with customizable material rules. | Freeform material play and authoring options. The small visible review count is insufficient evidence of a large market. |
| [Coloring Pixels — Steam](https://store.steampowered.com/app/897330/) | Numbered pixels correspond to palette colors; structured coloring with different image sizes and complexity. | Clear next actions, readable progress, and stopping points. Replace discrete pixel clicking with progressive sand coverage. |
| [A Little to the Left — Steam](https://store.steampowered.com/app/1629520/A_Little_to_the_Left/?l=english) | Sorting, stacking, and arranging everyday objects into pleasing configurations. | Legible completion and satisfying small actions; an adjacent interaction reference rather than a sand simulation. |

The supplied conversation's “Sand Mandala by Magikstudio” could not be corroborated through targeted searches. Do not present it as a confirmed competitor. There are other apps using “Sand Mandala,” including sound-visualization and fidget experiences; a matching title does not establish the described gameplay. The earlier “Zen Brick Breaker” claim is not needed for the recommendation and has not been verified here.

### What the evidence supports

There is precedent for every component of this idea. My inference is that combining precise guided craft with material movement could serve people who enjoy structured coloring but want a more tactile process. This research does not support claims that the combination is unique, clinically calming, or commercially proven.

Before committing to a large build, play the available browser references and, where devices permit, the native references. Compare startup friction, time until the first rewarding action, control, audio fatigue, completion friction, and desire to repeat. Keep unreleased games in a separate watchlist rather than treating their trailers as validated gameplay.

## Preserve the process thoughtfully

The [Smithsonian's account](https://asia-archive.si.edu/exhibition/questions-and-answers/) documents a drawn guide filled using a chak-pur, with a rod rubbed against its ridges to release material through vibration. It describes sweeping and dispersal into flowing water, and the religious meaning of impermanence. It also notes that the guide lines were drawn rather than engraved. [Hampton Arts](https://www.hamptonarts.org/news/detail/tools-of-the-tibetan-monks) describes controlled release and using the tool to straighten displaced grains.

Design implications:

- Preserve tracing, slow deposition, color changes, tool contact, and deliberate dissolution.
- Treat magnetic guidance, perfect edges, mirrored drawing, and wind clearing as game assists or artistic choices. They are not documented traditional techniques.
- Use original geometric and botanical patterns initially. Describe the game as inspired by sand mandala making; avoid presenting invented patterns as traditional sacred diagrams.
- Add an optional, brief explanation of the inspiration with source links. If educational authenticity becomes a product promise, involve a knowledgeable Tibetan Buddhist practitioner or cultural advisor in pattern and copy review.
- Let the player choose when to release. A photo and an unfinished local save can coexist with the release theme. Mandatory deletion is an interaction hypothesis to test, not a requirement of authenticity.

## Design options

| Direction | Strength | Main tradeoff | Recommendation |
| --- | --- | --- | --- |
| Guided sand coloring | Clear goals and forgiving placement | Can feel like ordinary coloring if too automatic | Default mode |
| Freehand craft | Strong ownership and physical expression | Difficult to produce intricate patterns | Add after guided pouring works |
| Mirrored sand drawing | Beautiful results quickly | Reduces the sense of placing each section; overlaps the direct competitor | Optional later assist |
| Detailed tool/process simulator | More deliberate craft and learning | Precision and control overhead narrow accessibility | Prototype later, if players ask for it |

Use one simulation with assistance settings rather than separate engines. Ship guided mode first, with a simple assistance toggle. Freehand, symmetry, and tool refilling can follow.

## The pouring mechanic

### Default interaction

1. Select a color and see its corresponding guide regions highlighted with symbols as well as color.
2. Move a small funnel tip above the tray. A faint footprint shows where grains will land.
3. Hold the mouse button or touch and drag to pour. Sand appears continuously beneath the tip, settles, and builds a shallow ridge.
4. Move slowly for a dense line; move faster for a lighter line. Pause to build a small mound. Change flow using visible low/medium/high controls; mouse wheel is an optional shortcut while the canvas is engaged.
5. Release to stop the source immediately, with a brief settling tail from grains already in flight. Switch color and continue.

The hand should control the result: approximately, deposited mass per unit distance is `flow rate / tip speed`. Avoid a huge mound at zero speed by using a gentle local height limit in guided mode and reducing flow as that local area fills. Display that response through the stream and sound. Freehand mode can allow larger piles.

Start tuning around a visible stream within 50 ms, an input ramp of roughly 50–100 ms, and a settling tail of 100–250 ms. These are prototype targets, not measured performance or claims about physical sand. Responsiveness takes priority over decorative tool animation.

### Assistance without automatic painting

- Bias landing positions only a small distance toward the selected region near its boundary. Do not pull grains across the tray or teleport them into distant gaps.
- In guided mode, restrict deposition into a neighboring color region and taper flow over already-filled cells. Slightly uneven edges and grain texture should remain visible.
- Derive progress from adequately covered area at the intended depth, not total poured mass. Repeatedly pouring one spot must not complete a region.
- Use a forgiving coverage threshold; offer a visible finishing assist for tiny remaining gaps. Do not require the player to hunt invisible pixels.
- Provide a small correction brush and a last-stroke undo. Neither should reset a whole pattern accidentally.

Do not claim that overfill naturally spilling into another color zone is self-correcting: it could damage that zone. Confinement and assistance must be explicit design choices.

### Audio and controls

Use Web Audio to combine a soft falling-grain texture, landing/settling noise, and a restrained tool scrape. Modulate these from emitted mass, movement, and contact, rather than one sound event per grain. Short varied samples and filtered noise are sufficient to test the idea before custom granular synthesis.

Start audio after an explicit user interaction, following [browser autoplay rules](https://developer.mozilla.org/en-US/docs/Web/Media/Guides/Autoplay). Offer independent tool/ambient volume and mute. Recorded tool sounds should be licensed or made for the app. Avoid an unvarying loud scrape during long holds.

Use [Pointer Events](https://developer.mozilla.org/en-US/docs/Web/API/Pointer_events) for mouse, pen, and touch. Pen pressure may enhance flow when available; the core interaction must work without pressure. Touch should use one finger and a visible flow control, with an optional offset tip for visibility. Avoid requiring two simultaneous fingers. Include toggle-to-pour and a keyboard cursor with pour/flow shortcuts for users who cannot hold and drag comfortably. Gamepad triggers and browser haptics are later enhancements, not MVP dependencies.

## Make sweeping a second satisfying mechanic

Use a deliberate transition from creating to releasing, with a clear “Begin sweeping” action. Allow a photo first and show that sweeping changes the work before the first destructive stroke.

The brush should push sand, leave a cleared path, collect a ridge along its leading edge, and scatter a few grains. Repeated passes bring separate colors together into a visibly mixed pile. Preserve colored speckles rather than immediately averaging everything to gray. Brush speed and displaced mass should affect sound and scatter.

Let players gather the pile into a tray edge or bowl. A final pour-out can clear the surface; an optional stylized breeze can be explored later. A single alpha fade is insufficient for the main interaction. Offer assisted sweeping for accessibility and let release pause when the player stops.

## Browser simulation and architecture

The board is a horizontal surface seen from above. Gravity acts into the board; it must not make all grains fall toward the bottom of the screen as in a side-view falling-sand game.

| Approach | Benefits | Costs | Decision |
| --- | --- | --- | --- |
| Canvas 2D decorative grains | Fast interaction sketch | Weak depth and pile displacement | Useful fallback and early comparison |
| Side-view cellular automata | Good precedent and simple local rules | Wrong geometry for a horizontal tray | Reference only |
| Shallow heightfield plus active grains | Local piles, slopes, shading, and brush transport within a bounded budget | Approximation; needs careful color transport | Recommended |
| Full 3D granular physics | More granular material behavior | Much larger simulation, tuning, and device burden | Defer |

Proposed simulation, to validate in the first prototype:

- A typed-array grid stores height and per-palette material mass; start with a fixed palette and a 256×256 grid. Increase resolution only after profiling.
- A bounded set of visible airborne grains lands and deposits mass. Surface detail can be procedural; the number of rendered dots need not equal the amount of simulated sand.
- Local transfers relax steep neighboring height differences, approximating an angle of repose. Transfer the corresponding color mass with each movement.
- Sweep strokes move mass directionally into adjacent cells, building a leading ridge. Bound transfers by available mass and handle board edges explicitly. Brush-induced motion and static slope relaxation are different processes.
- Per-color amounts retain a mixed pile's composition. Seeded material sampling can show speckles; a single averaged RGB field would lose this information. This approximation will not preserve every buried layer or support tunnels.
- A fixed simulation step and interpolated pointer paths keep deposition and sweeping consistent across frame rates. Pause on hidden tabs; cap catch-up work when resuming.
- Render height-derived lighting and stable grain texture through WebGL2. Detect capability; use a lower-resolution Canvas fallback if WebGL2 fails. Test fallback export and saving as well as initial rendering.

[WebGL2](https://developer.mozilla.org/en-US/docs/Web/API/WebGL2RenderingContext) provides the rendering surface. [WebGPU](https://developer.mozilla.org/en-US/docs/Web/API/WebGPU_API) is a possible later acceleration path, with capability detection; do not make it a launch requirement. These APIs are infrastructure, not ready-made sand solvers. D3 is unnecessary here, and a Win32/XAudio2 engine would not fit the requested browser app.

## Separate app in this repository

The repository already uses npm workspaces matching `apps/*` and `packages/*`. It contains an Expo mobile app, a Next.js web app, and shared domain/brand packages. Its root build/typecheck scripts currently name existing workspaces explicitly.

Recommended new workspace: `apps/mandala`, package `@ripplecompassion/mandala`, using React, TypeScript, and Vite. A static client suits the initial game and can deploy independently. [Vite supports React/TypeScript templates and static builds](https://vite.dev/guide/). Check the installed Node version and compatible dependency versions when implementing; do not assume the latest toolchain fits automatically.

Alternative: another Next.js workspace if framework consistency or server features become a priority. Neither SSR nor a backend improves the core sand loop.

Suggested structure:

```text
apps/mandala/
  package.json
  index.html
  vite.config.ts
  tsconfig.json
  README.md
  public/audio/
  src/
    app/                 # screens, controls, session state
    engine/              # grid, deposition, relaxation, brush, fixed clock
    render/              # WebGL2, Canvas fallback, textures
    audio/               # activity-driven sound graph
    patterns/            # original geometry, regions, palette, validation
    persistence/         # versioned local session and settings
    input/               # pointer and keyboard mapping
  tests/
```

Keep engine state outside React rendering. Define a small API for pour, sweep, tick, snapshot, and restore. Pattern files should contain vector geometry plus region identifiers, intended colors, and target depth; rasterize masks at the chosen simulation resolution. Separate narrow line regions from broad fill regions for useful completion behavior.

Add root `dev:mandala`, `build:mandala`, and a workspace typecheck entry when implementing, then include the new build in the aggregate build. Give the dev server its own port. Keep the app's deployment root and output separate from `apps/web`. Shared brand tokens are optional; Ripple's letter-exchange domain, auth, moderation, and Supabase configuration are not needed for this game.

Use local IndexedDB for a versioned sand snapshot and small local settings storage. Save at debounced checkpoints and lifecycle boundaries, outside the hot simulation loop. Include pattern version, material grid, palette, and phase so restore reproduces the actual work. Handle missing/failed storage gracefully. Export a PNG from the render pipeline. Accounts, cloud sync, multiplayer, and a public gallery are later scope.

## MVP and delivery sequence

### 1. Validate material feel

Build a standalone tray with three colors, variable pouring, shallow settling, a brush, and responsive audio. Include one curved guide and one broad region. Compare assistance on/off and Canvas/WebGL rendering where useful.

Exit when a tester can make a deliberate line and push it into a pile, and both actions remain responsive through a ten-minute session on representative devices. If sweeping cannot produce a convincing leading ridge, resolve the material model before adding content.

### 2. Complete one session

Add one original radial pattern, guided coverage, color switching, correction/undo, an appreciation pause, optional photo, and deliberate sweeping to completion. Target a first session of approximately 5–10 minutes; adjust after observation.

Exit when new players can discover pouring, finish without chasing stray pixels, and understand that release is their choice.

### 3. Make the separate app robust

Add a small pattern picker with three patterns of different complexity, local resume, touch layout, keyboard support, mute, reduced motion, and capability fallback. Patterns should be authored so features remain readable at the simulation's lowest supported resolution.

Exit when the app builds/deploys independently and a saved session survives reload with its sand, palette, and phase intact.

### 4. Validate before expanding

Observe 5–8 target users across structured-coloring players and people who enjoy material toys. This is formative usability research, not a statistically reliable market study. Ask them to pour a line, fill a region, recover from a mistake, and release a finished piece. Compare assistance levels in varied order and ask which interaction they would repeat.

Record time to first successful pour, control problems, perceived effort, whether pouring feels like sand or a paintbrush, whether sweeping feels rewarding or regrettable, and voluntary second-session interest. If people like pouring but avoid release, keep release optional. If they like sweeping most, offer shorter patterns. If physical spill frustrates them, strengthen guidance rather than expanding physics complexity.

A provisional planning range is 3–5 engineer-weeks for these phases and polish, assuming original simple patterns and an experienced web developer. This is an estimate with substantial uncertainty; re-estimate after the material prototype. Custom audio, cultural collaboration, wider device support, and full 3D physics can increase scope considerably.

## Verification and release criteria

- Engine checks: mass conservation during settling and brushing except at explicit removal sinks; nonnegative amounts; symmetric behavior on a flat board; comparable results across frame rates; bounded particle count.
- Guided checks: pouring one cell cannot complete a region; correct-color coverage governs progress; narrow features remain finishable; undo restores material and progress together.
- Session checks: snapshot/restore round trip, schema mismatch handling, reload during creation/release, PNG output, and storage unavailable behavior.
- Input checks: pointer capture, leaving the canvas, `pointercancel`, focus loss, touch scrolling outside the canvas, keyboard operation, resizing, and display scaling.
- Browser checks: current Chrome, Firefox, and Safari; iOS Safari and Android Chrome on real devices where available. Test WebGL context loss and fallback. Do not claim browser coverage from desktop emulation alone.
- Performance targets: aim for 60 FPS on a representative laptop and at least 30 FPS on a representative midrange phone during pouring and sweeping, with no unbounded memory growth over ten minutes. Measure actual devices; reduce grid size, pixel ratio, and airborne grain budget before sacrificing input responsiveness.
- Accessibility: labeled controls, palette symbols, visible focus, toggle pouring, keyboard shortcuts, volume controls, optional restrained motion, and assisted sweeping. Canvas drawing remains a limitation for nonvisual play and should be described honestly.
- Scope exclusions: no payments, advertisements, compulsory login, leaderboards, daily streak pressure, or social infrastructure in the first prototype. Evaluate a free browser demo and optional paid pattern packs only after repeat-use interest exists.

## First implementation milestone

Create `apps/mandala` and deliver one tray where the player can **pour a textured colored curve, watch it settle, and sweep it into a visibly mixed ridge with sound that follows the motion**. That result is the decision point for investing in the full game.
