# Sand Painting!

An independent sand painting game built with Unity.

This repository starts the standalone remake of Ripple Compassion's `/mandala`
experience. It contains a Unity starter project and the original design plan;
the gameplay has not been ported yet.

## Run

1. In Unity Hub, add this repository using **Add project from disk**.
2. Open it with Unity **6000.2.10f1**, the installed version used to create it.
3. Open `Assets/Scenes/Main.unity` and press Play.

The starter scene contains a camera and a placeholder welcome screen.

## Layout

- `Assets/`: game scenes, scripts, and their Unity metadata.
- `Packages/`: Unity package dependencies and lockfile.
- `ProjectSettings/`: shared editor and player configuration.
- `docs/original-design-plan.md`: design and research snapshot from the web prototype.
- `docs/porting.md`: source locations and initial porting priorities.

Commit Unity `.meta` files alongside their assets. Caches, local editor settings,
and build outputs are excluded from Git. This project has no npm, Next.js, or
Supabase dependency.
