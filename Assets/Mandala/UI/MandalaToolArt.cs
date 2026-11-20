using UnityEngine;
using UnityEngine.UIElements;

namespace Mandala.Game
{
    /// <summary>Simple full instruments, with an optional transparency fade toward their contact point.</summary>
    public static class MandalaToolArt
    {
        public static void Draw(Painter2D painter, SandTool tool, Vector2 origin, float scale,
            float angle, float phase, float contact, Color sand, float opacity = 1, float flex = 0,
            bool fadeTip = false)
        {
            float tipOpacity = tool == SandTool.Pour ? Mathf.Lerp(.2f, .75f, contact) : .2f;
            var art = new Illustration(painter, origin, scale, angle, opacity, fadeTip, tipOpacity);
            if (tool == SandTool.Pour) art.Pourer(phase, contact, sand);
            else if (tool == SandTool.Vacuum) art.Vacuum();
            else if (tool == SandTool.Rake) art.Rake(phase, contact);
            else art.Brush(flex * contact);
        }

        struct Illustration
        {
            readonly Painter2D p;
            readonly Vector2 origin;
            readonly float scale, sine, cosine, opacity, tipOpacity;
            readonly bool fadeTip;
            static readonly Color Outline = new Color32(73, 77, 57, 180);
            static readonly Color Brass = new Color32(196, 162, 96, 255);
            static readonly Color Green = new Color32(113, 140, 119, 255);
            static readonly Color Wood = new Color32(169, 124, 78, 255);
            static readonly Color Cream = new Color32(231, 215, 173, 255);

            public Illustration(Painter2D painter, Vector2 origin, float scale, float angle, float opacity, bool fadeTip, float tipOpacity)
            {
                p = painter; this.origin = origin; this.scale = scale;
                this.opacity = opacity; this.fadeTip = fadeTip; this.tipOpacity = tipOpacity;
                sine = Mathf.Sin(angle * Mathf.Deg2Rad); cosine = Mathf.Cos(angle * Mathf.Deg2Rad);
                p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            }
            Vector2 Point(float x, float y) => origin + new Vector2(x * cosine + y * sine, -x * sine + y * cosine) * scale;
            Color Tint(Color c, float strength = 1) { c.a *= opacity * strength; return c; }
            FillGradient Fade(Color c)
            {
                // Color keys do not carry alpha into a Unity Gradient; set alpha keys explicitly.
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(c, 0), new GradientColorKey(c, 1) },
                    new[] { new GradientAlphaKey(c.a * opacity * tipOpacity, 0), new GradientAlphaKey(c.a * opacity, 1) });
                return FillGradient.MakeLinearGradient(gradient, Point(0, 0), Point(0, 56), AddressMode.Clamp);
            }
            void ColorFill(Color c)
            {
                p.fillGradient = default;
                p.fillColor = fadeTip ? Color.white : Tint(c);
                if (fadeTip) p.fillGradient = Fade(c);
            }
            void ColorStroke(Color c, float width)
            {
                p.strokeGradient = default;
                p.strokeFillGradient = default;
                p.strokeColor = fadeTip ? Color.white : Tint(c);
                if (fadeTip) p.strokeFillGradient = Fade(c);
                p.lineWidth = width * scale;
            }
            void Begin() => p.BeginPath();
            void Move(float x, float y) => p.MoveTo(Point(x, y));
            void Line(float x, float y) => p.LineTo(Point(x, y));
            void Curve(float ax, float ay, float bx, float by, float x, float y) => p.BezierCurveTo(Point(ax, ay), Point(bx, by), Point(x, y));
            void Fill(Color c)
            {
                p.ClosePath(); ColorFill(c); p.Fill();
                ColorStroke(Outline, .8f); p.Stroke();
            }
            void Stroke(Color c, float width)
            {
                ColorStroke(c, width); p.Stroke();
            }
            void Ellipse(float x, float y, float rx, float ry, Color c)
            {
                Begin(); Move(x + rx, y);
                Curve(x + rx, y + ry * .5523f, x + rx * .5523f, y + ry, x, y + ry);
                Curve(x - rx * .5523f, y + ry, x - rx, y + ry * .5523f, x - rx, y);
                Curve(x - rx, y - ry * .5523f, x - rx * .5523f, y - ry, x, y - ry);
                Curve(x + rx * .5523f, y - ry, x + rx, y - ry * .5523f, x + rx, y);
                Fill(c);
            }

            public void Pourer(float phase, float contact, Color sand)
            {
                Begin(); Move(-1.65f, 0); Line(1.65f, 0); Line(14, 56);
                Curve(14, 62, -14, 62, -14, 56); Line(-1.65f, 0); Fill(Color.Lerp(Brass, sand, .15f));
                Ellipse(0, 0, 1.65f, .85f, new Color32(94, 72, 43, 255));
                float rub = Mathf.Sin(phase * 32) * contact;
                Begin(); Move(-14, 33 + rub); Line(19, 39 + rub); Stroke(Wood, 2.2f);
            }

            public void Vacuum()
            {
                Begin(); Move(0, 51); Curve(-2, 59, 0, 66, 12, 70);
                Stroke(new Color32(78, 104, 89, 255), 7);
                Begin(); Move(-10, 0); Curve(-12, 5, -7, 10, -6, 17);
                Line(-6, 43); Curve(-6, 49, 6, 49, 6, 43); Line(6, 17);
                Curve(7, 10, 12, 5, 10, 0); Line(-10, 0); Fill(Green);
                Ellipse(0, 0, 10.5f, 3.5f, new Color32(51, 72, 58, 255));
            }

            public void Brush(float flex)
            {
                float back = -flex * .5f;
                Begin(); Move(-4 + back, 27); Line(4 + back, 27); Line(5 + back, 63);
                Curve(5 + back, 72, -5 + back, 72, -5 + back, 63); Line(-4 + back, 27); Fill(Wood);
                // The bristle edge stays at the working point as the back flexes.
                Begin(); Move(-10 + back, 24); Line(10 + back, 24);
                Curve(13 + back, 14, 17, 6, 20, 1);
                Curve(10, -.5f, -10, -.5f, -20, 1);
                Curve(-17, 6, -13 + back, 14, -10 + back, 24); Fill(Cream);
                for (int i = 0; i < 7; i++)
                {
                    float t = i / 6f, x = Mathf.Lerp(-8, 8, t), end = Mathf.Lerp(-17, 17, t);
                    Begin(); Move(x + back, 23); Curve(x + back, 14, end * .9f, 6, end, 2);
                    Stroke(new Color32(146, 126, 88, 150), .7f);
                }
                Begin(); Move(-10 + back, 24); Line(10 + back, 24);
                Line(10 + back, 29); Line(-10 + back, 29); Fill(Brass);
            }

            public void Rake(float phase, float contact)
            {
                float tremble = Mathf.Sin(phase * 24) * contact * .3f;
                Begin(); Move(-3, 17); Line(3, 17); Line(4, 63);
                Curve(4, 69, -4, 69, -4, 63); Line(-3, 17); Fill(Wood);
                Begin(); Move(-1, 26); Line(-1, 61); Stroke(Cream, .8f);
                for (int i = 0; i < SandTray.RakeTeeth; i++)
                {
                    float x = Mathf.Lerp(-24, 24, (float)i / (SandTray.RakeTeeth - 1));
                    Begin(); Move(x - 1.25f, 15 + tremble); Line(x + 1.25f, 15 + tremble);
                    Line(x + .8f, 0); Curve(x + .5f, -1, x - .5f, -1, x - .8f, 0);
                    Line(x - 1.25f, 15 + tremble); Fill(Brass);
                }
                Begin(); Move(-27, 12 + tremble); Line(27, 12 + tremble);
                Line(27, 19 + tremble); Line(-27, 19 + tremble); Fill(Wood);
                Begin(); Move(-26, 13 + tremble); Line(26, 13 + tremble); Stroke(Cream, .8f);
                Begin(); Move(-4, 19); Line(4, 19); Line(4, 26); Line(-4, 26); Fill(Brass);
            }
        }
    }

    public sealed class TrayToolIcon : VisualElement
    {
        public TrayToolIcon(SandTool tool)
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("tool-icon");
            generateVisualContent += context =>
            {
                if (contentRect.width < 1 || contentRect.height < 1) return;
                float scale = Mathf.Min(contentRect.width / 68, contentRect.height / 82);
                Vector2 origin = contentRect.center - new Vector2(10, 32) * scale;
                MandalaToolArt.Draw(context.painter2D, tool, origin, scale, 22, 0, 0, SandTexture.Palette[0]);
            };
        }
    }
}
