using UnityEngine;
using UnityEngine.UIElements;

namespace Mandala.Game
{
    /// <summary>Translucent instruments fade toward the contact point to leave the moving sand visible.</summary>
    public sealed class TrayToolTip : VisualElement
    {
        Vector2 tip;
        float radius, visibility, contact, phase, flex, angle = 22;
        SandTool tool;
        Color color;

        public TrayToolTip()
        {
            name = "toolTip";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = style.top = style.right = style.bottom = 0;
            generateVisualContent += Draw;
        }

        public void UpdateTip(Vector2 position, float footprint, float fingerOffset, SandTool selectedTool, Color sandColor, bool visible, bool working = false, float heading = 22)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
            bool toolChanged = tool != selectedTool;
            if (toolChanged)
            {
                visibility = 0; contact = 0; flex = 0; tool = selectedTool;
                MarkDirtyRepaint();
            }
            float nextVisibility = Mathf.MoveTowards(visibility, visible ? 1 : 0, dt / .12f);
            float nextContact = Mathf.MoveTowards(contact, working ? 1 : 0, dt / .08f);
            if (!visible && visibility <= 0) return;
            float targetFlex = working && !toolChanged && visibility > 0 ? Mathf.Clamp((position.x - tip.x) * .55f, -5, 5) : 0;
            flex = Mathf.Lerp(flex, targetFlex, 1 - Mathf.Exp(-dt * 18));
            bool changed = tip != position || radius != footprint || color != sandColor || visibility != nextVisibility || contact != nextContact || angle != heading;
            // Keep a fading touch tool at its last contact instead of following an out-of-tray pointer.
            if (visible) tip = position;
            radius = footprint; tool = selectedTool; color = sandColor; angle = heading;
            visibility = nextVisibility; contact = nextContact;
            if (working) phase += dt;
            if (changed || working || Mathf.Abs(flex) > .01f) MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext context)
        {
            if (visibility <= 0 || contentRect.width < 1 || contentRect.height < 1) return;
            var painter = context.painter2D;
            painter.lineCap = LineCap.Round;
            painter.lineWidth = 1;
            painter.strokeColor = new Color(.98f, .94f, .82f, visibility * .28f);
            painter.BeginPath();
            if (tool == SandTool.Rake)
            {
                float radians = angle * Mathf.Deg2Rad;
                Vector2 across = new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians)) * radius;
                painter.MoveTo(tip - across); painter.LineTo(tip + across);
            }
            else painter.Arc(tip, radius, Angle.Degrees(0), Angle.Degrees(360));
            painter.Stroke();
            float lift = (1 - contact) * 4 + (1 - visibility) * 8;
            MandalaToolArt.Draw(painter, tool, tip + new Vector2(0, -lift), tool == SandTool.Rake ? radius / 24 : .96f,
                tool == SandTool.Rake ? angle : 22 + (tool == SandTool.Sweep ? flex * .7f : 0), phase, contact, color,
                visibility * .5f, flex, fadeTip: true);
        }
    }
}
