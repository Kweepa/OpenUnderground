using System.Collections.Generic;
using UnityEngine;

/// <summary>The controls of a pad that the control map points at.</summary>
public enum EPadControl
{
    LeftTrigger,
    LeftShoulder,
    LeftStick,
    DPad,
    Select,
    Start,
    RightStick,
    North,
    West,
    East,
    South,
    RightShoulder,
    RightTrigger,
}

/// <summary>
/// An outline of a pad for the control map of the game menu, drawn in code (<see cref="LineArt"/>), and where each
/// control stands on it.
/// </summary>
/// <remarks>
/// Traced from a plain black and white drawing of the most common pad, the user's choice of 1 November 2026: the body
/// is the outer edge of the drawing's black, followed point by point and smoothed, with the top under the shoulders
/// drawn anew; inside it the plate at the top with its round button, the side panels of the grips, the sticks, the
/// d-pad and the buttons, measured on the same drawing. The shoulders and the triggers stand apart above the body,
/// the user's request, so the callouts reach them without crossing it. The figures are in the drawing's own units,
/// 500 across, scaled onto a 640 x 500 grid.
/// The PlayStation pad (<see cref="PadGlyphs.PlayStation"/>) is a drawing of its own, traced the same way from a plain
/// drawing of that pad, the user's choice of 1 November 2026: the d-pad and the four symbols in the round heads of the
/// grips, the two sticks low between them, the two small buttons in the middle, the shoulders and triggers raised off
/// the body as on the other. It is measured in the same units and moved to sit where the other pad sits on the grid.
/// </remarks>
public static class PadOutline
{
    public const float GridWidth = 640f;
    public const float GridHeight = 500f;

    private const float LineWidth = 3f;

    // from the drawing's units to the grid
    private const float Scale = 1.18f;
    private static readonly Vector2 DrawingOrigin = new Vector2(250f, 95f);
    private static readonly Vector2 GridOrigin = new Vector2(320f, 108f);

    private static readonly Vector2 LeftStickAt = new Vector2(116f, 190f);
    private static readonly Vector2 DPadAt = new Vector2(183f, 264f);
    private static readonly Vector2 RightStickAt = new Vector2(315f, 265f);
    private static readonly Vector2 NorthAt = new Vector2(383.5f, 155f);
    private static readonly Vector2 WestAt = new Vector2(348f, 190f);
    private static readonly Vector2 EastAt = new Vector2(418.5f, 190f);
    private static readonly Vector2 SouthAt = new Vector2(383.5f, 223f);
    private static readonly Vector2 SelectAt = new Vector2(212.5f, 190f);
    private static readonly Vector2 StartAt = new Vector2(286.5f, 190f);
    private const float StickRing = 41f;
    private const float StickTop = 27f;
    private const float FaceRadius = 17f;
    private const float SmallRadius = 10f;

    // The outer edge of the drawing's black, clockwise from the left end of the plate's top; under the shoulders, where
    // the drawing's black is the shoulders', a line drawn anew, and the two sides the drawing cut off at its frame
    // rounded out.
    private static readonly Vector2[] Body =
    {
        new(174, 96), new(319, 97), new(326, 96), new(340, 97), new(365, 100), new(392, 108), new(420, 120), new(440, 132),
        new(451, 146), new(485, 250), new(495, 298), new(498, 325), new(498, 350), new(495, 372), new(493, 381), new(483, 398),
        new(474, 407), new(460, 415), new(451, 418), new(440, 417), new(423, 405), new(376, 355), new(362, 344), new(343, 336),
        new(158, 336), new(143, 340), new(133, 346), new(65, 414), new(55, 418), new(48, 418), new(35, 414), new(24, 407),
        new(10, 390), new(4, 373), new(1, 350), new(1, 325), new(4, 302), new(8, 276), new(16, 244), new(43, 160),
        new(52, 140), new(60, 132), new(80, 120), new(107, 108), new(134, 100), new(159, 97),
    };

    private static readonly Vector2[] Plate = { new(174, 97), new(325, 97), new(330, 118), new(300, 163), new(200, 163), new(169, 118) };

    // the inner edge of the left grip's side panel; the right one is its mirror
    private static readonly Vector2[] SidePanel =
    {
        new(60, 134), new(57, 150), new(53, 162), new(49, 174), new(44, 190), new(39, 210), new(35, 232), new(33, 256),
        new(33, 300), new(35, 330), new(39, 360), new(44, 380), new(51, 398), new(60, 412),
    };

    private static readonly LineArtCache XboxCache = new LineArtCache(GridWidth, t => Make(t, false));
    private static readonly LineArtCache PlayStationCache = new LineArtCache(GridWidth, t => Make(t, true));

    /// <summary>
    /// The outline, of the PlayStation pad or the other, made at the density of a width on the screen, in screen
    /// pixels, and kept while that holds.
    /// </summary>
    public static Texture2D TextureFor(float screenWidth, bool playStation)
        => (playStation ? PlayStationCache : XboxCache).For(screenWidth);

    private static Vector2 ToGrid(Vector2 drawing) => GridOrigin + (drawing - DrawingOrigin) * Scale;

    private static Vector2 ToDrawing(Vector2 grid) => DrawingOrigin + (grid - GridOrigin) / Scale;

    /// <summary>
    /// Where a leader line of the map starts for a control, on the 640 x 500 grid: the edge of the control that faces
    /// its label, the left half of the pad labelled on the left, the right half on the right. On the pad of the style
    /// in force (<see cref="PadGlyphs.PlayStation"/>).
    /// </summary>
    public static Vector2 Anchor(EPadControl control)
    {
        if (PadGlyphs.PlayStation)
        {
            return PlayStationAnchor(control);
        }
        switch (control)
        {
            case EPadControl.LeftTrigger: return ToGrid(new Vector2(70f, 46f));
            case EPadControl.LeftShoulder: return ToGrid(new Vector2(54f, 80f));
            case EPadControl.LeftStick: return ToGrid(LeftStickAt - new Vector2(StickRing, 0f));
            case EPadControl.DPad: return ToGrid(DPadAt - new Vector2(38f, 0f));
            case EPadControl.Select: return ToGrid(SelectAt - new Vector2(SmallRadius, 0f));
            case EPadControl.Start: return ToGrid(StartAt + new Vector2(SmallRadius, 0f));
            case EPadControl.RightStick: return ToGrid(RightStickAt + new Vector2(StickRing, 0f));
            case EPadControl.North: return ToGrid(NorthAt + new Vector2(FaceRadius, 0f));
            // the side facing the label, as every other button (the user's, 1 November 2026)
            case EPadControl.West: return ToGrid(WestAt + new Vector2(FaceRadius, 0f));
            case EPadControl.East: return ToGrid(EastAt + new Vector2(FaceRadius, 0f));
            case EPadControl.South: return ToGrid(SouthAt + new Vector2(FaceRadius, 0f));
            case EPadControl.RightShoulder: return ToGrid(new Vector2(445f, 80f));
            default: return ToGrid(new Vector2(429f, 46f));
        }
    }

    /// <summary>True for the controls labelled on the left of the pad.</summary>
    public static bool OnLeft(EPadControl control)
    {
        return control == EPadControl.LeftTrigger || control == EPadControl.LeftShoulder || control == EPadControl.LeftStick
            || control == EPadControl.DPad || control == EPadControl.Select;
    }

    private static Texture2D Make(float texelsPerUnit, bool playStation)
    {
        if (playStation)
        {
            return MakePlayStation(texelsPerUnit);
        }

        LineArt art = new LineArt(GridWidth, GridHeight);

        // four rounds of smoothing: two left the segments' corners visible on a large screen; each segment is a
        // figure of its own, so a smoother line costs little more
        Vector2[] body = LineArt.Smooth(Body, 4, true);
        Vector2[] plate = LineArt.Smooth(Plate, 4, true);
        Vector2[] left = LineArt.Smooth(SidePanel, 4, false);
        Vector2[] right = new Vector2[left.Length];
        for (int i = 0; i < left.Length; i++)
        {
            right[i] = new Vector2(499f - left[i].x, left[i].y);
        }
        AddLine(art, body, true);
        AddLine(art, plate, true);
        AddLine(art, left, false);
        AddLine(art, right, false);

        AddCircle(art, new Vector2(250f, 128f), 18f);
        foreach (Vector2 stick in new[] { LeftStickAt, RightStickAt })
        {
            AddCircle(art, stick, StickRing);
            AddCircle(art, stick, StickTop);
        }
        Add(art, p => Mathf.Min(LineArt.RoundBox(p, DPadAt, new Vector2(38f, 12f), 4f), LineArt.RoundBox(p, DPadAt, new Vector2(12f, 37f), 4f)),
            new Rect(DPadAt - new Vector2(40f, 40f), new Vector2(80f, 80f)));
        foreach (Vector2 face in new[] { NorthAt, WestAt, EastAt, SouthAt })
        {
            AddCircle(art, face, FaceRadius);
        }
        AddCircle(art, SelectAt, SmallRadius);
        AddCircle(art, StartAt, SmallRadius);

        // the shoulders and, above them, the triggers, rising toward the middle
        AddCapsule(art, new Vector2(62f, 80f), new Vector2(160f, 58f), 9f);
        AddCapsule(art, new Vector2(437f, 80f), new Vector2(339f, 58f), 9f);
        AddCapsule(art, new Vector2(82f, 46f), new Vector2(148f, 32f), 12f);
        AddCapsule(art, new Vector2(417f, 46f), new Vector2(351f, 32f), 12f);

        return art.Make(texelsPerUnit, LineWidth);
    }

    // ---------------------------------------------------------------- the PlayStation pad

    // Measured on its drawing in the same units, 498 across, and moved 1 right and 60 down so it sits where the other
    // pad sits: the outer edge of the drawing's black, clockwise from the right end of the left shoulder's base, with
    // the top under the shoulders drawn anew as on the other pad.
    private static readonly Vector2[] PsBody =
    {
        new(164, 100), new(336, 100), new(341, 99), new(346, 95), new(354, 90), new(369, 87), new(389, 85),
        new(409, 87), new(424, 91), new(439, 102), new(451, 114), new(458, 124), new(463, 134), new(468, 152),
        new(499, 319), new(497, 338), new(488, 356), new(477, 368), new(458, 378), new(439, 380), new(421, 376),
        new(408, 368), new(394, 352), new(363, 285), new(357, 285), new(348, 291), new(336, 295), new(315, 295),
        new(302, 290), new(285, 276), new(275, 259), new(274, 251), new(272, 249), new(227, 249), new(218, 272),
        new(208, 283), new(191, 293), new(177, 296), new(163, 295), new(149, 290), new(144, 286), new(136, 286),
        new(106, 351), new(87, 371), new(71, 378), new(53, 380), new(41, 378), new(25, 370), new(11, 356), new(3, 341),
        new(1, 333), new(1, 309), new(32, 150), new(37, 134), new(47, 116), new(61, 102), new(76, 91), new(91, 87),
        new(111, 85), new(131, 87), new(146, 90), new(154, 95), new(159, 99),
    };

    // the inner edge of the left grip's round head, from the body's top down round the stick to the bottom between
    // the sticks; the right one is its mirror
    private static readonly Vector2[] PsHead =
    {
        new(164, 108), new(171, 118), new(179, 130), new(185, 145), new(187, 160), new(187, 172), new(186, 182), new(185, 190),
        new(188, 197), new(197, 201), new(205, 206), new(210, 210), new(214, 215), new(217, 221), new(220, 230), new(221, 240),
        new(222, 246),
    };

    private static readonly Vector2 PsDPadAt = new Vector2(112f, 168f);
    private static readonly Vector2 PsLeftStickAt = new Vector2(174f, 242f);
    private static readonly Vector2 PsRightStickAt = new Vector2(326f, 242f);
    private const float PsStick = 25f;
    private static readonly Vector2 PsTriangleTop = new Vector2(388f, 119f);
    private static readonly Vector2 PsTriangleLeft = new Vector2(376f, 145f);
    private static readonly Vector2 PsTriangleRight = new Vector2(400f, 145f);
    private static readonly Vector2 PsSquareAt = new Vector2(348.75f, 168f);
    private static readonly Vector2 PsCircleAt = new Vector2(427f, 168f);
    private static readonly Vector2 PsCrossAt = new Vector2(388f, 204f);
    private const float PsSymbol = 13.5f;
    private const float PsCrossArm = 14f;
    // Share and Options, two short bars
    private static readonly Vector2 PsShareFrom = new Vector2(214f, 167f);
    private static readonly Vector2 PsShareTo = new Vector2(234f, 167f);
    private static readonly Vector2 PsOptionsFrom = new Vector2(267f, 167f);
    private static readonly Vector2 PsOptionsTo = new Vector2(287f, 167f);
    private const float PsBar = 6f;

    private static Vector2 Mirror(Vector2 p) => new Vector2(500f - p.x, p.y);

    private static Vector2 PlayStationAnchor(EPadControl control)
    {
        switch (control)
        {
            case EPadControl.LeftTrigger: return ToGrid(new Vector2(79f, 20f));
            case EPadControl.LeftShoulder: return ToGrid(new Vector2(72f, 52f));
            case EPadControl.LeftStick: return ToGrid(PsLeftStickAt - new Vector2(PsStick, 0f));
            case EPadControl.DPad: return ToGrid(PsDPadAt - new Vector2(43f, 0f));
            case EPadControl.Select: return ToGrid(PsShareFrom - new Vector2(PsBar, 0f));
            case EPadControl.Start: return ToGrid(PsOptionsTo + new Vector2(PsBar, 0f));
            case EPadControl.RightStick: return ToGrid(PsRightStickAt + new Vector2(PsStick, 0f));
            // each symbol's side facing the label, as on the other pad
            case EPadControl.North: return ToGrid((PsTriangleTop + PsTriangleRight) / 2f);
            case EPadControl.West: return ToGrid(PsSquareAt + new Vector2(PsSymbol, 0f));
            case EPadControl.East: return ToGrid(PsCircleAt + new Vector2(PsSymbol, 0f));
            case EPadControl.South: return ToGrid(PsCrossAt + new Vector2(PsCrossArm, 0f));
            case EPadControl.RightShoulder: return ToGrid(new Vector2(428f, 52f));
            default: return ToGrid(new Vector2(421f, 20f));
        }
    }

    private static Texture2D MakePlayStation(float texelsPerUnit)
    {
        LineArt art = new LineArt(GridWidth, GridHeight);

        Vector2[] body = LineArt.Smooth(PsBody, 4, true);
        Vector2[] left = LineArt.Smooth(PsHead, 4, false);
        Vector2[] right = new Vector2[left.Length];
        for (int i = 0; i < left.Length; i++)
        {
            right[i] = Mirror(left[i]);
        }
        AddLine(art, body, true);
        AddLine(art, left, false);
        AddLine(art, right, false);

        Add(art, p => Mathf.Min(LineArt.RoundBox(p, PsDPadAt, new Vector2(43f, 13f), 4f), LineArt.RoundBox(p, PsDPadAt, new Vector2(13f, 44f), 4f)),
            new Rect(PsDPadAt - new Vector2(46f, 47f), new Vector2(92f, 94f)));
        AddCircle(art, PsLeftStickAt, PsStick);
        AddCircle(art, PsRightStickAt, PsStick);

        // the four symbols are drawn on the pad itself, as the drawing has them, with no button round them
        AddCapsule(art, PsTriangleTop, PsTriangleLeft, 0f);
        AddCapsule(art, PsTriangleLeft, PsTriangleRight, 0f);
        AddCapsule(art, PsTriangleRight, PsTriangleTop, 0f);
        Add(art, p => LineArt.RoundBox(p, PsSquareAt, new Vector2(PsSymbol, PsSymbol), 1.5f),
            new Rect(PsSquareAt - new Vector2(PsSymbol + 2f, PsSymbol + 2f), new Vector2(2f * PsSymbol + 4f, 2f * PsSymbol + 4f)));
        AddCircle(art, PsCircleAt, PsSymbol);
        AddCapsule(art, PsCrossAt - new Vector2(PsCrossArm, PsCrossArm), PsCrossAt + new Vector2(PsCrossArm, PsCrossArm), 0f);
        AddCapsule(art, PsCrossAt + new Vector2(-PsCrossArm, PsCrossArm), PsCrossAt + new Vector2(PsCrossArm, -PsCrossArm), 0f);

        AddCapsule(art, PsShareFrom, PsShareTo, PsBar);
        AddCapsule(art, PsOptionsFrom, PsOptionsTo, PsBar);

        // the shoulders and, above them, the triggers, level as the drawing has them
        AddCapsule(art, new Vector2(81f, 52f), new Vector2(151f, 52f), 9f);
        AddCapsule(art, new Vector2(349f, 52f), new Vector2(419f, 52f), 9f);
        AddCapsule(art, new Vector2(91f, 20f), new Vector2(141f, 20f), 12f);
        AddCapsule(art, new Vector2(359f, 20f), new Vector2(409f, 20f), 12f);

        return art.Make(texelsPerUnit, LineWidth);
    }

    /// <summary>A figure in the drawing's units: its distance scaled onto the grid, its box carried there.</summary>
    private static void Add(LineArt art, System.Func<Vector2, float> drawingDistance, Rect drawingBox)
    {
        Vector2 min = ToGrid(drawingBox.min);
        Vector2 max = ToGrid(drawingBox.max);
        art.Add(p => drawingDistance(ToDrawing(p)) * Scale, Rect.MinMaxRect(min.x, min.y, max.x, max.y));
    }

    private static void AddLine(LineArt art, Vector2[] points, bool closed)
    {
        art.AddLine(points, closed, ToGrid);
    }

    private static void AddCircle(LineArt art, Vector2 centre, float radius)
    {
        Add(art, p => LineArt.Circle(p, centre, radius), new Rect(centre - new Vector2(radius, radius), new Vector2(2f * radius, 2f * radius)));
    }

    private static void AddCapsule(LineArt art, Vector2 a, Vector2 b, float radius)
    {
        Add(art, p => LineArt.Capsule(p, a, b, radius), LineArt.Bounds(new[] { a, b }, radius));
    }
}
