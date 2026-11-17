# Touch tool tips

Pour, sweep, rake, and vacuum act 64 UI panel points above a touch contact. The clearance
is fixed throughout a stroke and remains the same on screen when zoomed. Mouse and
pen input remain directly under their pointers.

A faint contact outline shows the actual tool radius. Simple full silhouettes show
a brass pourer with a rubbing rod, a green vacuum with a short hose, and a wooden
brush with a few bristle lines. Decorative texture, hardware, and shadows are
omitted. The on-tray tools are translucent throughout: fills and outlines fade
from about 50% opacity at the body to about 10% at the working tip. The fade follows
the tool's angle and stays fixed in screen space when zooming. Overlapping edges
can be slightly darker. The selector uses the same simplified shapes at full opacity.

The simulation's falling grains provide the pouring motion; the rod rubs gently
while pouring. The brush leans and
its bristles flex during movement, keeping their working edge at the contact point.
Tools lower into contact when pressed and lift/fade over 120 milliseconds when
dismissed. Mouse and pen hovering
show the selected instrument without modifying sand; touch shows it during a
stroke. Second-finger gestures, cancellation, dialogs, and leaving the tray dismiss
the tool. The overlay ignores pointer input and sits below dialogs, outside the
tray's clipping boundary so handles can extend into the gutter.

The tray reserves a matching gutter below its image so a touch can reach the bottom
edge. Input is captured by the surrounding tray space, while coordinates and zoom
remain relative to the image. Leaving the image pauses the action; reentering starts
a fresh stroke without drawing across the outside segment. After a pinch, painting
stays suspended until all fingers lift.

Tune **Touch Tip Offset** on the MandalaGame component (40–100 panel points). The
gutter uses the same value. Window or orientation changes cancel the current stroke
before resizing the tray.

The rake adds seven brass teeth on a wooden head. Its full silhouette turns with
the stroke and scales with its sand footprint when zoomed. Automatic patterns
drive the same overlay directly across the tray; see [Rake](rake-tool.md).

Earlier interaction validation in Unity 6000.6.9f1: compilation; isolated live-panel pointer checks for
touch mapping, motion, mouse/pen input, all four edges, bottom-gutter entry,
out-of-bounds pause/reentry, pinch zoom, constant screen clearance, fit tray,
pointer cancellation and capture loss; runtime gutter/overlay wiring; distinct
rendered illustrations for all three tools in an isolated runtime render target;
mouse/pen hover without painting, hover dismissal, tool selector illustrations,
contact lowering, stationary pourer animation, moving bristle flex, release fade,
and clearing on tool changes; existing gameplay and vacuum-animation checks.
The final console check found no current errors or warnings. Physical phone
comfort still needs a device playtest.

The simplified translucent revision replaces the tip-only version. Unity
compilation, live overlay wiring, and an isolated panel render of all three full
instruments were checked. Rendered alpha was measured at the tips and bodies to
verify the opacity fade. Gesture mapping, tool radii, and sand simulation are
unchanged. The earlier interaction checks above used the original detailed art.
