using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mandala.Game
{
    /// <summary>Translucent instruments fade toward the contact point to leave the moving sand visible.</summary>
    public sealed class TrayToolTip : VisualElement
    {
        const float PourAngle = 132;
        readonly Dictionary<Vector2Int, Color32> pourSpecks = new Dictionary<Vector2Int, Color32>();
        Vector2 tip;
        float radius, visibility, contact, phase, flex, angle = 22;
        bool working, hasPourGrains;
        SandTool tool;
        Color color;
        SandTray tray;
        Vector2 cellOrigin;
        float pixelsPerCell;

        // The pourer artwork puts the center of its opening at its origin.
        public Vector2 PourOutletOffset => new Vector2(0, -Mathf.Max(14, radius + 2));

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
            bool changed = tip != position || radius != footprint || color != sandColor || visibility != nextVisibility ||
                contact != nextContact || angle != heading || this.working != working;
            // Keep a fading touch tool at its last contact instead of following an out-of-tray pointer.
            if (visible) tip = position;
            radius = footprint; tool = selectedTool; color = sandColor; angle = heading;
            visibility = nextVisibility; contact = nextContact;
            this.working = working;
            if (working) phase += dt;
            if (changed || working || Mathf.Abs(flex) > .01f) MarkDirtyRepaint();
        }

        public void UpdatePourStream(SandTray tray, Vector2 cellOrigin, float pixelsPerCell)
        {
            bool airborne = false;
            foreach (var grain in tray.Grains)
            {
                if (!grain.Outlet.HasValue) continue;
                airborne = true;
                break;
            }
            if (airborne || hasPourGrains) MarkDirtyRepaint();
            this.tray = tray;
            this.cellOrigin = cellOrigin;
            this.pixelsPerCell = pixelsPerCell;
            hasPourGrains = airborne;
        }

        void Draw(MeshGenerationContext context)
        {
            if (contentRect.width < 1 || contentRect.height < 1) return;
            if (hasPourGrains) DrawPourStream(context);
            if (visibility <= 0) return;
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
            Vector2 origin = tool == SandTool.Pour ? tip + PourOutletOffset : tip;
            if (tool != SandTool.Pour || !working) origin.y -= lift;
            float artAngle = tool == SandTool.Pour ? PourAngle : tool == SandTool.Rake ? angle : 22 + (tool == SandTool.Sweep ? flex * .7f : 0);
            MandalaToolArt.Draw(painter, tool, origin, tool == SandTool.Rake ? radius / 24 : .96f, artAngle, phase, contact, color,
                visibility * .5f, flex, fadeTip: true);
        }

        void DrawPourStream(MeshGenerationContext context)
        {
            pourSpecks.Clear();
            float size = Mathf.Clamp(pixelsPerCell, .85f, 1.8f);
            foreach (var grain in tray.Grains)
            {
                if (!grain.Outlet.HasValue) continue;
                byte opacity = SandTexture.PourGrainOpacity(grain);
                if (opacity == 0) continue;
                Vector2 position = grain.Position;
                Vector2 panelPosition = cellOrigin + position * pixelsPerCell;
                if (!contentRect.Contains(panelPosition)) continue;
                Color32 tint = SandTexture.Palette[tray.DisplayColor(position, grain.Color)];
                tint.a = opacity;
                Vector2 sample = panelPosition / size - Vector2.one * .5f;
                Vector2Int corner = Vector2Int.FloorToInt(sample);
                float x = sample.x - corner.x, y = sample.y - corner.y;
                PlotPourSpeck(corner, tint, (1 - x) * (1 - y));
                PlotPourSpeck(corner + Vector2Int.right, tint, x * (1 - y));
                PlotPourSpeck(corner + Vector2Int.up, tint, (1 - x) * y);
                PlotPourSpeck(corner + Vector2Int.one, tint, x * y);
            }
            if (pourSpecks.Count == 0) return;

            var mesh = context.Allocate(pourSpecks.Count * 4, pourSpecks.Count * 6);
            ushort first = 0;
            foreach (var speck in pourSpecks)
            {
                Vector2 position = (Vector2)speck.Key * size;
                var vertex = new Vertex { tint = speck.Value, position = new Vector3(position.x, position.y, Vertex.nearZ) };
                mesh.SetNextVertex(vertex);
                vertex.position.x += size;
                mesh.SetNextVertex(vertex);
                vertex.position.y += size;
                mesh.SetNextVertex(vertex);
                vertex.position.x -= size;
                mesh.SetNextVertex(vertex);
                mesh.SetNextIndex(first);
                mesh.SetNextIndex((ushort)(first + 1));
                mesh.SetNextIndex((ushort)(first + 2));
                mesh.SetNextIndex(first);
                mesh.SetNextIndex((ushort)(first + 2));
                mesh.SetNextIndex((ushort)(first + 3));
                first += 4;
            }
        }

        void PlotPourSpeck(Vector2Int cell, Color32 tint, float weight)
        {
            // Overlapping grains retain the brightest speck instead of adding opacity.
            tint.a = (byte)Mathf.RoundToInt(tint.a * weight);
            if (tint.a > 0 && (!pourSpecks.TryGetValue(cell, out var current) || tint.a > current.a))
                pourSpecks[cell] = tint;
        }
    }
}
