# Native mobile mandala port

Source: `/home/ryanwanghr/Desktop/projects/ripplecompassion/apps/web/components/mandala-game.tsx`
and `lib/mandala-{sand,regions,audio,save}.ts`. This project contains native C# gameplay;
no website, JavaScript runtime, WebView, or npm dependency is included.

Open `Assets/Scenes/Main.unity` and press Play in Unity **6000.6.9f1**.
The scene is also the enabled build scene. Mobile orientation is portrait.
Rendering uses **URP 17.6.0** and input uses **Input System 1.20.1** exclusively.
`Assets/Settings/Rendering/MandalaURP.asset` is assigned in Graphics and all six
Quality levels. Its Forward renderer disables lighting, shadows, HDR, MSAA,
depth/opaque textures, and post-processing for this UI-only game.
The custom sand shader uses URP HLSL and renders its offscreen UI texture directly;
no camera effect or renderer feature is needed. The scene has no baked lighting,
lightmaps, reflection probes, or legacy post-processing to migrate.

`Assets/Settings/Input/MandalaUI.inputactions` supplies the project-wide `UI` map
for pointer, touch, wheel, and navigation events. UI Toolkit uses it directly;
the scene does not need an EventSystem or an Input System UI Input Module.
Active Input Handling is **Input System Package (New)**. Restart Unity after
changing this setting. `TrayGesture` continues to handle captured pointer events.

Use a portrait Game view. The UI layout reference is 430 × 900 logical points;
1080 × 2260 is the high-resolution preview used for rendering validation.
Choose **Tools > Mandala > Preview at 1080 x 2260** to create/select it for the
active platform. The command saves the fixed-resolution preset to Unity Editor
preferences, so it survives restarts, and reuses an existing matching preset.

## Controls

- One finger: pour, vacuum, or sweep. Pick one of five numbered colors.
- Two fingers: pan and pinch to zoom (1–4×). Painting stops when the second
  finger arrives and stays stopped until all fingers lift.
- Fit tray: reset zoom. Mouse dragging and wheel zoom work in the Editor.
- Undo: last eight strokes/actions, including clearing or releasing protection.
- Guides: pattern outlines, color hints and numbered sections.
- Let go: disable protection and freely dismantle the mandala.
- Clear: confirmation, then a fresh tray; reversible with Undo.

## Structure

- `Core/SandTray.cs`: 880 × 880 horizontal grid, 24 colored layers, seeded random
  emission, airborne reservations, interpolated brush contact, conservative sweep,
  vacuum, settling, snapshots. No GameObjects or Rigidbody per grain.
- `Core/MandalaProgress.cs`: 51 connected sections. Completion requires three or
  more settled layers and the guide color on top in at least 98% of each
  section's cells. Cleanup requires 98% global empty space and 95% enclosed-gap
  cleanliness.
  Victory requires one uninterrupted settled second and fires once per tray.
- `Core/MandalaSave.cs`: versioned compressed binary format with bounded reads,
  temporary-file replacement, previous-save backup, and migration from 440 × 440
  trays. Stored at
  `Application.persistentDataPath/mandala-v1.sand`; independent of browser saves.
- `Presentation/SandTexture.cs`: uploads color/height, section feedback/coverage, and
  airborne-grain fields from the simulation. Renders to a reusable
  viewport-sized RenderTexture, at physical display resolution (64–2048 pixels
  per side). Pan/zoom redraws the visible area without allocating a giant texture.
  Simulation changes refresh at up to 30 Hz; view/size changes refresh immediately.
- `Resources/MandalaSurface.shader`: analytic, antialiased guide curves and tray rim,
  composited over the 880 × 880 material grid. Thin sand resolves into irregular,
  tray-anchored grains; deeper layers become continuous. Height gradients light
  pile edges and cast short shadows. See [Sand depth rendering](sand-depth.md).
  The Resources asset ensures the shader is included in players.
- `UI/MandalaGuideLabels.cs`: real UI Toolkit text, positioned with the same
  viewport transform used for shading and input. Labels do not intercept touches.
- `Presentation/MandalaGame.cs`: fixed 60 Hz simulation, UI binding, capped undo,
  asynchronous/coalesced stroke saves, pause/focus-loss flush, safe-area layout.
- `Presentation/MandalaAudio.cs`: locally synthesized tool textures and completion
  chime, with a mute control. This adapts the browser synthesis rather than
  embedding Web Audio. No downloaded audio or mixer package.
- `UI/TrayGesture.cs`: captured UI Toolkit pointers and two-touch navigation.
- `UI/Mandala.uxml` and `.uss`: mobile controls and dialogs; no runtime IMGUI.
- `Editor/MandalaPortSetup.cs`: repeatable scene installation and Linux smoke build.
- `Editor/MandalaChecks.cs`: executable gameplay regression checks, accessible from
  **Tools > Mandala > Run Gameplay Checks** or the Unity CLI.

The source palette and procedural mandala are retained. Guidance now includes
faint target-color fills. Completion glow and synthesized sound are adapted to
Unity; browser photo download and browser persistence migration are outside this
mobile gameplay port. Vacuum uses surface removal without the source's decorative
flying debris. A fixed grid is appropriate for the underlying
tray simulation. The renderer uses GPU shading at display resolution. The simulation
runs on the CPU at 880 × 880. Continuous sand retains the cell texture, while
thin coverage, guide curves, rim, and UI text resolve at display resolution.

## Validation

- The URP migration's deterministic 1024 × 1024 guide/tray reference was
  pixel-identical to the Built-in output. All rendering, color-highlight,
  guide-label, gameplay, and vacuum checks passed under URP.
- After restarting with Input System only, queued device events verified mouse
  selection/painting/drag release, wheel zoom, touch painting, two-finger pinch
  and pan, suspension until all fingers lift, and keyboard submit. Play Mode
  frames advanced with zero console errors or warnings. These were simulated
  Input System device events, not physical touchscreen validation.
- Unity recompilation passes.
- Gameplay checks cover emission/landing conservation, sweep conservation, vacuum,
  airborne snapshots/restore, region protection, negative-space cleanup, interrupted win delay/one-shot behavior,
  persistence, backup creation, and rejection of corrupt saves.
- Play Mode frame advancement confirmed; portrait screen, pouring, undo, completed
  pattern and victory dialog inspected; no current console errors.
- Synthetic UI Toolkit touch events confirm painting with one finger, stopping on
  the second finger, and 2× pinch zoom. This is not a physical touchscreen test.
- The initial gameplay port built and launched as a Linux development player.
  The subsequent high-resolution renderer was validated in the Editor; that older
  player build does not contain the renderer update.
- Disabling/re-enabling the game component was verified in Play Mode.
- **Tools > Mandala > Run Rendering Checks** verifies native surface resolution,
  transparent tray corners, shader texture bindings, cropped-view/Y alignment,
  render-target reuse during zoom, uniform continuous sand within a cell at 4× zoom,
  restored GPU target state and bounded allocation.
- High-resolution rendering was inspected at 1080 × 2260 and 4× zoom. The visible
  tray uses a 1000 × 1000 render target in both views, while the grid remains 440 × 440.
  [Earlier fixed-resolution sand and sharp guides](screenshots/mandala-fixed-sand.png).
  That image predates the current [depth-dependent coverage](sand-depth.md).
- Android release AAB and signed APK builds are now available; see
  [mobile builds](mobile-builds.md) for setup, signing, artifacts, and validation.
  Hardware touch, safe-area behavior on notched devices, thermal behavior, and
  device profiling remain unverified. iOS settings are configured, but no iOS
  player has been built; final iOS signing/build requires the macOS toolchain.

## Visual review

Source palette: marigold, terracotta, lagoon, ivory, indigo; warm parchment tray and
muted green interface. No 3D lighting or character assets are involved.

Portrait [gameplay](screenshots/mandala-portrait.png) and [victory](screenshots/mandala-victory.png) captures were inspected. A second pass fixed capture
aspect ratio, removed obsolete API calls, and made completion audio explicitly 2D.
Scoped scores: palette 4/5, style 4/5, outline consistency 3/5, pattern readability
3/5, UI 4/5, feedback 3/5. Fine numbered guides benefit from pinch zoom.

References consulted: Unity CLI playmode-verification-loop; UI/ui-uitk skills and
USS, common-issues, pointer-manipulator references; craft-game-floors game-audio and
visual-scorecard references; installed Unity UIElements XML API documentation;
Unity Image.uv and AudioClip.Create API documentation. The craft skill's 3D look
floors do not apply; its mixer/music recipes were not added to this gameplay-only port.

## Completion sound and mobile performance

The completion chime now has a normalized 0.65 sample peak and 0.8 playback gain,
a 1.4-second tail, and a separate 2D feedback source. Tool sound drops to 22% for
0.7 seconds so continued pouring does not mask the chime. Generated clips are
recreated if a Play Mode script reload invalidates them. With Play Mode running
and Sound on, use **Tools > Mandala > Test Completion Sound** to test directly.
Playback, clip recovery, distinct source reuse, and tool ducking were checked in Unity.

The 880 × 880 grid has four times as many cells as the original 440 × 440 grid.
Each layer holds a quarter of its earlier volume per cell, so rates measured in
grains per second scale with area. Brush reach and motion scale with distance.
Settling scans the tiles affected by recent changes. Eight full undo snapshots
use about 160 MiB at 880 × 880, excluding the live simulation, rendering,
saves, and runtime overhead. Cross-device performance is still unmeasured.

Display resolution is a different budget. 1080 × 2260 is a preview baseline, not a
measured guarantee. The visible tray render target currently caps at 2048²; that
is an allocation cap, not an assertion of mobile performance. A 1024² tray cap is
a conservative starting candidate for a lower quality tier, to validate on hardware.
Target 60 fps (16.7 ms/frame), with a 30 fps option (33.3 ms/frame) where necessary.
Profile sustained pouring, sweeping, saving, and zooming on the oldest intended
phone and a large-screen tablet before raising either budget. Desktop Editor timings
do not establish phone CPU/GPU performance or sustained thermal behavior.
