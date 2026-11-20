using UnityEngine;

namespace Mandala.Game
{
    public enum RakePatternKind { Spiral, Petals, Waves }

    /// <summary>A continuous, distance-paced route; every point is inside the circular tray.</summary>
    public sealed class RakePattern
    {
        const int Samples = 1536;
        readonly Vector2[] points = new Vector2[Samples + 1];
        int next = 1;
        public Vector2 Position { get; private set; }
        public bool Complete => next > Samples;
        public float Length { get; private set; }

        public RakePattern(RakePatternKind kind)
        {
            for (int i = 0; i <= Samples; i++)
            {
                float t = (float)i / Samples;
                Vector2 offset;
                if (kind == RakePatternKind.Spiral)
                {
                    float a = t * Mathf.PI * 4.5f - Mathf.PI / 2;
                    offset = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Mathf.Lerp(30, 164, t);
                }
                else if (kind == RakePatternKind.Petals)
                {
                    float a = t * Mathf.PI * 2 - Mathf.PI / 2;
                    float r = 110 + 48 * Mathf.Cos(a * 6);
                    offset = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                }
                else
                {
                    // A serpentine with rounded returns keeps the rake down between wave bands.
                    float a = t * Mathf.PI * 6;
                    offset = new Vector2(-142 * Mathf.Cos(a), Mathf.Lerp(-104, 104, t) + 9 * Mathf.Sin(a * 2));
                }
                points[i] = Vector2.one * (SandTray.Size / 2f) + offset * SandTray.GridScale;
                if (i > 0) Length += Vector2.Distance(points[i - 1], points[i]);
            }
            Position = points[0];
        }

        public Vector2 Advance(float distance)
        {
            while (distance > 0 && !Complete)
            {
                float remaining = Vector2.Distance(Position, points[next]);
                if (remaining > distance)
                {
                    Position = Vector2.MoveTowards(Position, points[next], distance);
                    break;
                }
                Position = points[next++]; distance -= remaining;
            }
            return Position;
        }
    }
}
