using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Line drawings made in code - the pad and the mouse of the control maps - as white lines on clear, to be tinted
/// when drawn: each figure is its distance from simple shapes, and a line is drawn where that distance is near 0.
/// </summary>
/// <remarks>
/// Distances rather than pictures, so a drawing has no asset to ship and stays sharp at the size it is made at.
/// Each figure is drawn only inside its own box, and along each row it skips ahead by its distance from the line,
/// so only the texels near a line are worked out: drawing every figure at every texel took a good part of a second
/// for the pad, the first time its page opened.
/// </remarks>
public sealed class LineArt
{
    private readonly float gridWidth;
    private readonly float gridHeight;
    private readonly List<Func<Vector2, float>> figures = new List<Func<Vector2, float>>();
    private readonly List<Rect> boxes = new List<Rect>();

    /// <param name="gridWidth">The drawing's width in its own units; rows are counted from the top.</param>
    public LineArt(float gridWidth, float gridHeight)
    {
        this.gridWidth = gridWidth;
        this.gridHeight = gridHeight;
    }

    /// <summary>A figure: its distance from a point of the grid, and the box, in grid units, its line stays in.</summary>
    public void Add(Func<Vector2, float> distance, Rect box)
    {
        figures.Add(distance);
        boxes.Add(box);
    }

    /// <summary>The drawing, <paramref name="texelsPerUnit"/> texels a unit, its lines <paramref name="lineWidth"/> units wide.</summary>
    public Texture2D Make(float texelsPerUnit, float lineWidth)
    {
        int w = Mathf.RoundToInt(gridWidth * texelsPerUnit);
        int h = Mathf.RoundToInt(gridHeight * texelsPerUnit);
        float[] alpha = new float[w * h];
        float half = lineWidth / 2f;
        float reach = half + 1f / texelsPerUnit;
        for (int f = 0; f < figures.Count; f++)
        {
            Func<Vector2, float> distance = figures[f];
            Rect box = boxes[f];
            int x0 = Mathf.Clamp(Mathf.FloorToInt((box.xMin - lineWidth) * texelsPerUnit), 0, w - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((box.xMax + lineWidth) * texelsPerUnit), 0, w - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt((box.yMin - lineWidth) * texelsPerUnit), 0, h - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt((box.yMax + lineWidth) * texelsPerUnit), 0, h - 1);
            for (int gy = y0; gy <= y1; gy++)
            {
                float y = (gy + 0.5f) / texelsPerUnit;
                // texture rows count from the bottom, the grid's from the top
                int row = (h - 1 - gy) * w;
                int gx = x0;
                while (gx <= x1)
                {
                    float d = Mathf.Abs(distance(new Vector2((gx + 0.5f) / texelsPerUnit, y)));
                    // a line along the edge, its sides smoothed over a texel
                    float a = Mathf.Clamp01((half - d) * texelsPerUnit + 0.5f);
                    if (a > alpha[row + gx])
                    {
                        alpha[row + gx] = a;
                    }
                    // nothing of this figure's line is nearer than d, less the line's reach: skip to there, with a
                    // margin for the shapes whose distance is only close to the true one
                    gx += Mathf.Max(1, (int)((d - reach) * texelsPerUnit * 0.8f));
                }
            }
        }

        // white, the line in the alpha; the bytes written at once, which is much quicker than SetPixels
        byte[] data = new byte[w * h * 4];
        for (int i = 0; i < alpha.Length; i++)
        {
            data[4 * i] = 255;
            data[4 * i + 1] = 255;
            data[4 * i + 2] = 255;
            data[4 * i + 3] = (byte)Mathf.RoundToInt(alpha[i] * 255f);
        }
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };
        t.LoadRawTextureData(data);
        t.Apply();
        return t;
    }

    public static float Circle(Vector2 p, Vector2 centre, float radius) => Vector2.Distance(p, centre) - radius;

    public static float RoundBox(Vector2 p, Vector2 centre, Vector2 half, float radius)
    {
        float qx = Mathf.Abs(p.x - centre.x) - half.x + radius;
        float qy = Mathf.Abs(p.y - centre.y) - half.y + radius;
        return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    /// <summary>A segment from a to b, made round and <paramref name="radius"/> thick; 0 for a plain line.</summary>
    public static float Capsule(Vector2 p, Vector2 a, Vector2 b, float radius)
    {
        Vector2 pa = p - a;
        Vector2 ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude - radius;
    }

    /// <summary>
    /// A line through points, closed back to the first or not, each segment a figure of its own with its own small box:
    /// the line is the nearest segment's, and a figure is worked out only inside its box, so a long line costs what its
    /// length costs and not what its whole box would.
    /// </summary>
    public void AddLine(Vector2[] points, bool closed, Func<Vector2, Vector2> toGrid)
    {
        int n = points.Length;
        for (int i = 0; i < (closed ? n : n - 1); i++)
        {
            Vector2 a = toGrid(points[i]);
            Vector2 b = toGrid(points[(i + 1) % n]);
            Add(p => Capsule(p, a, b, 0f), Bounds(new[] { a, b }, 1f));
        }
    }

    /// <summary>The distance from a line through the points, closed back to the first or not. Unsigned: a line needs no inside.</summary>
    public static float Polyline(Vector2 p, Vector2[] points, bool closed)
    {
        float best = float.MaxValue;
        int n = points.Length;
        for (int i = 0; i < (closed ? n : n - 1); i++)
        {
            best = Mathf.Min(best, Capsule(p, points[i], points[(i + 1) % n], 0f));
        }
        return best;
    }

    /// <summary>A line through points made smooth by cutting its corners (Chaikin), <paramref name="rounds"/> times.</summary>
    public static Vector2[] Smooth(Vector2[] points, int rounds, bool closed)
    {
        for (int r = 0; r < rounds; r++)
        {
            List<Vector2> next = new List<Vector2>();
            int n = points.Length;
            if (!closed)
            {
                next.Add(points[0]);
            }
            for (int i = 0; i < (closed ? n : n - 1); i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % n];
                next.Add(a * 0.75f + b * 0.25f);
                next.Add(a * 0.25f + b * 0.75f);
            }
            if (!closed)
            {
                next.Add(points[n - 1]);
            }
            points = next.ToArray();
        }
        return points;
    }

    /// <summary>The box of some points, grown by a margin.</summary>
    public static Rect Bounds(Vector2[] points, float margin)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (Vector2 p in points)
        {
            x0 = Mathf.Min(x0, p.x);
            y0 = Mathf.Min(y0, p.y);
            x1 = Mathf.Max(x1, p.x);
            y1 = Mathf.Max(y1, p.y);
        }
        return Rect.MinMaxRect(x0 - margin, y0 - margin, x1 + margin, y1 + margin);
    }
}

/// <summary>
/// A drawing kept at the density it is shown at, so that it is as sharp on a 4K screen as at 1366 x 768: made again
/// when the size it is drawn at needs more or fewer texels, in steps of a quarter of a texel a unit.
/// </summary>
public sealed class LineArtCache
{
    private readonly Func<float, Texture2D> make;
    private readonly float gridWidth;
    private Texture2D texture;
    private float density;

    /// <param name="make">Makes the drawing at a number of texels a grid unit.</param>
    public LineArtCache(float gridWidth, Func<float, Texture2D> make)
    {
        this.gridWidth = gridWidth;
        this.make = make;
    }

    /// <summary>The drawing for a width on the screen, in screen pixels.</summary>
    public Texture2D For(float screenWidth)
    {
        float wanted = Mathf.Max(0.5f, Mathf.Ceil(screenWidth / gridWidth * 4f) / 4f);
        if (texture == null || wanted != density)
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
            texture = make(wanted);
            density = wanted;
        }
        return texture;
    }
}

/// <summary>
/// The mouse of the keyboard map, traced from a common icon, the user's choice of 1 November 2026: the cord, the
/// wheel between the two buttons and the arc under them.
/// </summary>
public static class MouseOutline
{
    public const float GridWidth = 200f;
    public const float GridHeight = 300f;

    // drawn about 120 to 170 reference pixels high, so a line of 5 units is two or three pixels
    private const float LineWidth = 5f;

    private static readonly LineArtCache Cache = new LineArtCache(GridWidth, Make);

    /// <summary>The mouse, made at the density of a width on the screen, in screen pixels.</summary>
    public static Texture2D TextureFor(float screenWidth) => Cache.For(screenWidth);

    private static Texture2D Make(float texelsPerUnit)
    {
        LineArt art = new LineArt(GridWidth, GridHeight);
        Vector2 centre = new Vector2(100f, 170f);
        Vector2 half = new Vector2(70f, 115f);
        art.Add(p => LineArt.RoundBox(p, centre, half, 66f), new Rect(centre - half, half * 2f));
        art.Add(p => LineArt.Capsule(p, new Vector2(100f, 55f), new Vector2(100f, 12f), 0f), Rect.MinMaxRect(95f, 8f, 105f, 60f));
        art.Add(p => LineArt.RoundBox(p, new Vector2(100f, 55f), new Vector2(7f, 4f), 2f), Rect.MinMaxRect(90f, 48f, 110f, 62f));
        art.Add(p => LineArt.RoundBox(p, new Vector2(100f, 98f), new Vector2(9f, 22f), 9f), Rect.MinMaxRect(88f, 74f, 112f, 122f));
        art.Add(p => LineArt.Capsule(p, new Vector2(100f, 59f), new Vector2(100f, 76f), 0f), Rect.MinMaxRect(95f, 56f, 105f, 79f));
        art.Add(p => LineArt.Capsule(p, new Vector2(100f, 120f), new Vector2(100f, 143f), 0f), Rect.MinMaxRect(95f, 117f, 105f, 146f));
        // the buttons' edge, a wide arc across the body, inside it only: outside, the distance from the body, which
        // is never more than the distance from the arc's part inside it, so the skipping stays safe
        art.Add(p => Mathf.Max(Mathf.Abs(LineArt.Circle(p, new Vector2(100f, 455f), 312f)), LineArt.RoundBox(p, centre, half, 66f)),
            Rect.MinMaxRect(30f, 130f, 170f, 160f));
        return art.Make(texelsPerUnit, LineWidth);
    }
}
