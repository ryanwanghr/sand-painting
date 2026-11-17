# Vacuum animation

Sand retains its existing curved pull, acceleration, original colors, short trails, and release tail. Removal still favors the center of the brush. The dark intake, spinning air currents, pressure shadow, and their shader animation controls have been removed. The simulation in `SandTray.cs` is unchanged by this simplification.

[Animated preview](screenshots/vacuum-preview.gif) · [Mid-stroke detail](screenshots/vacuum-detail.png)

The updated preview uses a separate five-color test patch at 2x zoom, rendered in Unity with the actual simulation and surface shader. It does not change the saved painting.

Suction requests rendering at 60 Hz while grains move. A final redraw clears the last airborne grain after the release tail finishes, then the surface returns to its normal redraw schedule.

## Validation

Unity 6000.6.9f1 compiled without errors or warnings. Existing vacuum animation and rendering checks passed, including curl, attraction, original colors, moving paths, bounded particles, release tails, save exclusion, undo, clear, protected layers, viewport zoom, and transparency. The updated 108-frame preview completed successfully. The final console check found zero current errors or warnings.

This simplification changes `MandalaSurface.shader`, `SandTexture.cs`, and `MandalaGame.cs`, plus these preview artifacts and notes. It preserves the existing sand physics and user project changes.
