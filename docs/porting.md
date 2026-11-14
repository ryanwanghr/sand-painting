# Porting the web prototype

## Source

Original repository: `../ripplecompassion` (sibling checkout).
Source HEAD when this repository was created: `9cddb5a`.
The source checkout also had uncommitted mandala changes at that time, so this
commit alone does not represent the entire current prototype.

Reference files relative to that repository:

- `apps/web/components/mandala-game.tsx`: game interactions and UI.
- `apps/web/components/mandala-game.module.css`: visual presentation.
- `apps/web/lib/mandala-sand.ts`: sand simulation.
- `apps/web/lib/mandala-regions.ts`: guided regions.
- `apps/web/lib/mandala-audio.ts`: procedural audio.
- `apps/web/lib/mandala-save.ts`: local saves.
- `apps/web/tests/mandala-*.test.cjs`: existing behavior checks.

`original-design-plan.md` is a copied planning document, not a specification of
what is already implemented. Research and storefront information in it is a
historical snapshot and should be rechecked when relevant.

## First playable milestone

1. Build a sand tray and pointer/touch pouring in Unity.
2. Port one pattern, its region boundaries, and a small color palette.
3. Tune grain movement and rendering before expanding pattern content.
4. Add brush interaction, movement-driven audio, and deliberate dissolution.
5. Add local saves and exports appropriate to the standalone game.

Use the web prototype to compare behavior. Reimplement the simulation and UI in
Unity using C#; TypeScript and browser-specific APIs are reference material.

Choose release platforms and a permanent title before configuring distribution.
