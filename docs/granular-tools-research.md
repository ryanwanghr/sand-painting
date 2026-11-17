# Research: less uniform raking and sweeping

Research date: 2026-11-17. This is a design comparison, not an implemented change.
Relative implementation costs below are engineering estimates for this project;
they are not measured performance results.

## What makes the current tools look unusually clean

- `SandTray.Rake` removes layers until a contacted column reaches zero whenever
  destinations remain available. Adding forward transport changed where grains
  go, but retained complete excavation of the contact region.
- `SandTray.Sweep` computes a remaining height of zero across the central 70% of
  its footprint. Most of the residual variation is at its outer edge.
- General settling skips columns below four layers and moves material only when
  neighboring heights differ by more than three layers. Shallow shoulders can
  therefore remain beside an empty groove without any backfill.
- Heights are integer layers. The renderer uses the top color across an occupied
  cell; the surface shader does not use the uploaded height alpha to represent
  sparse coverage. A thin deposit and a thick deposit differ mainly in lighting.
- Existing rake checks explicitly require seven empty channels after settling.
  Those checks would need to measure furrow depth and continuity instead.

An exposed tray is possible when a tool reaches the substrate. The design target
is conditional clearance: shallow, imperfect furrows in deeper deposits, scattered
residue under a brush, and occasional bare patches depending on contact and the
available sand. The references below do not establish a universal residual amount
for a particular mandala tool or tray.

## Algorithm options

### 1. Depth-limited height-field contact and local slumping

Treat each tooth or brush contact as a shape with a working height. Move material
that penetrates that shape, rather than excavating the entire column. Redistribute
the displaced grains forward and sideways, then allow unstable shoulders to
slump back into the furrow.

Sumner, O'Brien, and Hodgins model ground as columns with tool displacement and
erosion. Their parameters control mound slopes and irregularity. Beneš and
colleagues demonstrate interactive height-field manipulation using paired
displacement/deposition filters followed by slope-limited redistribution. These
are close structural matches to our existing grid. Their specific examples are
not rake or brush calibrations.

**Project fit:** low to moderate implementation cost; keeps the layered color
stacks, protection, undo, and save representation. Tool depth/contact estimation
requires tuning. A height field alone does not supply brush bristle mechanics.

Sources: [Animating Sand, Mud, and Snow (1999)](https://www.ri.cmu.edu/pub_files/pub4/sumner_robert_1999_1/sumner_robert_1999_1.pdf),
[Granular Material Interactive Manipulation (2006)](https://www.cs.purdue.edu/homes/bbenes/papers/Benes06WSCG.pdf).

### 2. Cellular automaton with incomplete, variable grain entrainment

Keep discrete colored grains in the existing grid, but move only a bounded amount
under each contact. Pla-Castells and colleagues describe interactive granular
cellular automata with external-force and pressure-distribution rules.

The following are proposed adaptations for this game, rather than rules validated
by that paper for sweeping:

- Give neighboring regions correlated variation in resistance and tool contact.
  This produces small patches and streaks rather than independent pixel noise.
- Represent the brush with overlapping bristle bundles of differing reach and
  contact strength. Represent rake teeth with slightly different penetration.
- Make the last layers harder to entrain, while allowing additional passes to
  move them. Keep all residue as actual colored simulation material.
- Accumulate transport from traveled distance or a defined contact impulse. A
  fresh random trial on every render frame would change behavior with frame rate
  and can erase residue just because the contact is oversampled.

**Project fit:** low to moderate implementation cost. This is the smallest useful
experiment, and complements option 1. On its own it can still feel arbitrary if
contact shape, depth, and backfill remain unchanged.

Source: [Interactive Terrain Simulation and Force Distribution Models in Sand Piles (2006)](https://robotica.uv.es/~ignacio/papers/ACRI06/pla-castells_marta.pdf).

### 3. Static and mobile layers with erosion and deposition

Track resting material separately from a moving surface layer. A tool mobilizes
some grains; moving grains travel, entrain other grains, and settle again. The
BCRE family of granular surface-flow models distinguishes these populations.
A subsequent paper proposes corrections to ensure mass conservation and address
irregular static surfaces. Transferring actual colored grains between the two
states would be important in this project.

This could provide a more persistent moving bank ahead of a tool and deposition
after it passes. Applying the model to horizontal, tool-driven motion would be
our extension of slope-driven surface-flow research.

**Project fit:** moderate to substantial implementation cost. Requires mobile
state, transport rules, and a decision about how snapshots/save files settle or
store that state. It improves transport behavior, but still needs realistic tool
contact to determine how deeply a rake cuts.

Sources: [Some Considerations about the BCRE Model (2001)](https://arxiv.org/abs/cond-mat/0108209),
[Frictional Hysteresis and Particle Deposition in Granular Free-Surface Flows (2019)](https://www.cambridge.org/core/journals/journal-of-fluid-mechanics/article/frictional-hysteresis-and-particle-deposition-in-granular-freesurface-flows/48758AE4E4BE2C41458F240451158274).

### 4. Material Point Method with frictional plasticity

MPM exchanges information between material particles and a background grid.
Klár and colleagues use a Drucker–Prager material law to reproduce sand's
frictional yielding and interactions with solids. This offers a much more general
physical basis for piling and deformation than our redistribution rules. The
material particles need not correspond to individual visible grains.

**Project fit:** substantial rewrite of simulation, rendering integration,
colored-material tracking, and persistence. Tool geometry and contact still need
to be modeled; a sophisticated bulk solver alone does not determine brush reach
or guarantee residual sand. Mobile feasibility would need a prototype and device
profiling. The paper is not evidence that an appropriate solver will meet this
game's phone performance budget.

Sources: [Drucker–Prager Elastoplasticity for Sand Animation (2016)](https://www.cs.ucr.edu/~craigs/papers/2016-sand/paper.pdf),
[author-provided implementation supplement](https://www.math.ucla.edu/~cffjiang/research/sand/tech-doc.pdf).

## Recommended first prototype

Combine options 1 and 2, using the existing grid:

1. Give the rake finite penetration and the brush several contacts with varied
   strength and reach. Estimate contact height from a neighborhood around the
   tool so it responds to the sand surface rather than always reaching the tray.
2. Bound entrainment per traveled distance and use reproducible spatial variation
   for resistance/contact. Preserve every grain's color and redistribute exactly
   the amount removed.
3. Improve local settling near recently disturbed material. Consider eight
   neighbors with diagonal distances accounted for, plus distinct thresholds for
   starting and stopping flow. Maintain steeper stable sides instead of smoothing
   the entire tray flat. Separate starting/stopping thresholds are motivated by
   granular frictional hysteresis, not a calibrated model of this tray.
4. Let repeated passes change the residue. Keep bare patches possible when contact
   reaches the tray; a permanently unmovable bottom layer would be a separate
   artistic constraint.
5. Decide how thin residue should look. Keeping the current cell style means
   using scattered occupied cells. A continuous faint film requires an explicit
   coverage/rendering design, and potentially finer material quantities. Visual
   residue must remain consistent with material that actually exists.

Source for hysteresis: [Friction Law and Hysteresis in Granular Materials (2017)](https://arxiv.org/abs/1704.00740).

## Evaluation

Compare shallow and deep deposits, repeated passes, crossing strokes, and turns.
Measure residual mass and spatial coverage separately: many shallow occupied
cells can look different from a few tall piles with the same mass. Verify that
grooves remain coherent, edges are irregular, shoulders partly refill them, and
forward transport survives settling.

Continue checking exact conservation of each color, protection, boundaries, undo,
and behavior independent of render frame rate and stroke sampling density.
Replace tests that require empty grooves with relative-depth and residual-coverage
checks, with tuning bounds established from an agreed visual reference.

The completion logic currently counts any occupied gap cell as stray sand, with
2% overall and 5% per-enclosed-gap limits. Increasing realistic residue therefore
also needs a gameplay decision: retain vacuum as precise cleanup, or evaluate
cleanliness using meaningful residue amount/coverage. A persistent residual layer
should not accidentally make completion impossible.
