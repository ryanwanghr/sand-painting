# Sand Painting!

An independent sand painting game built with Unity.

The game opens in the studio, where you can browse designs, try the daily
challenge, or return to a saved painting.

## Run

1. In Unity Hub, add this repository using **Add project from disk**.
2. Open it with Unity **6000.6.9f1**.
3. Open `Assets/Scenes/Main.unity` and press Play.

For the Neovim, Codex, and Unity CLI workflow, see [Development setup](docs/development.md).

## Add a design

1. Create an **880 × 880** RGBA PNG. Keep the design inside the circular tray,
   centered in the square. Leave roughly 24 pixels of space around the edge.
2. Use hard-edged, fully opaque fills from the palette below. Leave all areas
   without guides fully transparent. Use separate shapes to make separate
   sections of the same color, with enough space between them to pour and sweep.
3. In Unity, choose **Tools → Sand Painting → Import Design PNG…** and select
   the file. The importer validates the mask and selects the new design asset.
   Set its **Title** and **Collection** in the Inspector. It then appears in
   **Main** and enters the local **Daily** rotation.
4. Press Play, open the design, and check that its sections are easy to see
   and fill at phone size.

| Guide number | Sand color | Hex |
| --- | --- | --- |
| 1 | Marigold | `#D8A740` |
| 2 | Terracotta | `#BC6751` |
| 3 | Lagoon | `#558E88` |
| 4 | Ivory | `#F0E3BD` |
| 5 | Indigo | `#696E98` |

When changing a shipped design's shape, import it as a new design so existing
paintings keep their original guide.

## Layout

- `Assets/`: game scenes, scripts, and their Unity metadata.
- `Packages/`: Unity package dependencies and lockfile.
- `ProjectSettings/`: shared editor and player configuration.
- `docs/original-design-plan.md`: design and research snapshot from the web prototype.
- `docs/porting.md`: source locations and initial porting priorities.

Commit Unity `.meta` files alongside their assets. Caches, local editor settings,
and build outputs are excluded from Git. This project has no npm, Next.js, or
Supabase dependency.
