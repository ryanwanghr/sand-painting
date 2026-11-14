# Mandala

An independent sand mandala game built with Godot. Mandala is a working title.

This repository starts the standalone remake of Ripple Compassion's `/mandala`
experience. It currently contains a Godot starter scene and the original design
plan; the gameplay has not been ported yet.

## Run

1. Install Godot 4 (standard edition; C# is not required).
2. Import `project.godot` in the Godot project manager.
3. Open the project and press F6 to run the current scene, or F5 to run the game.

If Godot is on your PATH, use `godot --editor --path .` to open the project.

## Layout

- `project.godot`: standalone Godot project configuration.
- `scenes/main.tscn`: initial entry scene.
- `docs/original-design-plan.md`: design and research snapshot from the web prototype.
- `docs/porting.md`: source locations and initial porting priorities.

Godot caches and build exports are excluded from Git. This project has no npm,
Next.js, or Supabase dependency.
