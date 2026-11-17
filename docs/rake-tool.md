# Rake

Select **Rake** alongside Pour, Vacuum, and Sweep. Drag through loose sand to comb
seven parallel channels. The wooden handle and brass teeth lower onto the tray,
turn gradually with the stroke, and lift when released. Each tooth moves the
existing colored grains forward into a small bank ahead of its tip. As the bank
grows, grains spill into the shoulders of the groove. Completed, protected
sections stay intact; **Let go** releases them for free raking.

With Rake selected, choose **Spiral**, **Petals**, or **Waves** to animate a complete
pass across the tray. Paths use a steady travel speed and finish automatically.
Starting a pattern fits the full tray in view. **Stop** leaves the sand as it is;
touching the tray takes over with a new freehand stroke. **Undo** restores the sand
before the last freehand stroke or animated pass. Switching tools, opening a
dialog, fitting the tray, or suspending the app also stops a pattern. Empty trays
prompt you to pour sand first. Patterns rearrange deposited sand, so a broader
layer makes their channels easier to see.

The rake spans 48 simulation cells. Its illustration follows that footprint when
zoomed, and its seven tooth positions use the same heading as the simulation.
Groove sampling accounts for travel and rotation to keep fast or curved strokes
continuous. The existing settling, save format, protection, and sweep audio also
apply to raking.

## Validation

Run **Tools > Mandala > Run Rake Checks**, or use the connected Unity Editor:

```sh
unity command script eval --caller plugin --skill unity-cli \
  'return Mandala.Game.Editor.MandalaRakeChecks.Run();'
```

Unity 6000.6.9f1 checks cover forward transport and leading piles in five stroke
directions, protected sand ahead of the teeth, seven channels surviving settling, fast strokes,
stationary contact, gradual turns, exact conservation of every color, protected
cells, tray boundaries, snapshot restoration, all three complete paths, and
travel speed independent of frame rate. The existing gameplay, vacuum animation,
and rendering checks also pass.

An isolated runtime instance verified live rake movement, contact animation, real
sand movement, active pattern controls, Stop, and Undo restoring every height and
layer. All four tool buttons fit a 360-point phone panel with at least 44-point
touch targets. The existing painting and Play Mode session were preserved.
