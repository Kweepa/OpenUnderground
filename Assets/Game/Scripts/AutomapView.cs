using System;
using System.Collections.Generic;

/// <summary>
/// Which tiles the original's automap draws, from the player's eye once a frame: a transcription of
/// the frame code of UW.EXE (file offsets of the GOG executable).
/// </summary>
/// <remarks>
/// Every view draw of the original (0x2e0f1 -> 0x2e136, 0x2efba) walks one visible span, two edge rays
/// from the eye at the heading -/+ 0x2040, row by row in the frame of the heading's quadrant, until it
/// dies at tile sides that block or at the light's radius (SHADES.DAT word 4, 3 tiles in the dark to 7
/// in daylight). A non-solid tile in the span whose shade is below 8 - the light's detail radius: the
/// eye's tile alone in the dark, one tile with a torch, two with Night Vision - is drawn on the map
/// (0x21ebc's main path). The original also marks a band of cells around the view that the map never
/// draws, and pays exploration for those: that part is left out here, since the remake pays for the
/// tiles drawn (MapPage.Update()). Read whole and reproduced cell by cell on DOSBox saves of the original.
/// </remarks>
public class AutomapView
{
    // DS:0x444, a record of three words per quadrant: the index step of one tile to the right, of one
    // tile forward, and a third word not used here. Quadrant 0 looks to +y, 1 to +x, 2 to -y, 3 to -x.
    private static readonly int[] quadrantLateral = { 1, -64, -1, 64 };
    private static readonly int[] quadrantForward = { 64, 1, -64, -1 };

    // DS:0x45c, the heading at which each quadrant starts.
    private static readonly int[] quadrantHeading = { 0, 0x4000, 0x8000, 0xc000 };

    // DS:0x464, the tile type as seen from each quadrant: the diagonals and the slopes turn with the view.
    private static readonly byte[] rotatedType =
    {
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 0, 0, 0, 0, 0,
        0, 1, 4, 2, 5, 3, 9, 8, 6, 7, 0, 0, 0, 0, 0, 0,
        0, 1, 5, 4, 3, 2, 7, 6, 9, 8, 0, 0, 0, 0, 0, 0,
        0, 1, 3, 5, 2, 4, 8, 9, 7, 6, 0, 0, 0, 0, 0, 0,
    };

    // DS:0x4a5, the code byte of a grid cell, seven per rotated tile type, by where the cell lies from the
    // eye. Bit 7 set means "something to draw in here"; it is zero for the solid tile and six diagonal
    // views.
    private static readonly byte[] cellCode =
    {
        0, 0, 0, 0, 0, 0, 0,
        184, 152, 176, 152, 176, 152, 176,
        228, 196, 228, 196, 224, 196, 228,
        205, 205, 197, 201, 197, 205, 197,
        214, 210, 0, 214, 0, 214, 0,
        215, 0, 211, 0, 215, 0, 215,
        188, 156, 180, 156, 180, 156, 180,
        189, 157, 181, 157, 181, 157, 181,
        190, 158, 182, 158, 182, 158, 182,
        191, 159, 183, 159, 183, 159, 183,
        0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0,
    };

    // DS:0x1d8a, the sides a rotated tile type blocks: solid all four, the diagonals two, slopes bit 5.
    private static readonly byte[] blocking = { 30, 0, 19, 21, 11, 13, 32, 32, 32, 32, 0, 0, 0, 0, 0, 30 };

    // DS:0x535-0x543, four pairs of words the ray code indexes by its direction.
    private static readonly int[] d535 = { 16, 0 };
    private static readonly int[] d539 = { 2, 4 };
    private static readonly int[] d53d = { 2, 3 };
    private static readonly int[] d541 = { -1, 1 };

    // The sine table at 0x5624:0x4e0, a quarter of it: 256 steps a turn, amplitude 32767.
    private static readonly short[] sineQuarter =
    {
        0, 804, 1608, 2411, 3212, 4011, 4808, 5602, 6393, 7180, 7962, 8740, 9512, 10279, 11039, 11793,
        12540, 13279, 14010, 14733, 15447, 16151, 16846, 17531, 18205, 18868, 19520, 20160, 20788, 21403,
        22006, 22595, 23170, 23732, 24279, 24812, 25330, 25833, 26320, 26791, 27246, 27684, 28106, 28511,
        28899, 29269, 29622, 29957, 30274, 30572, 30853, 31114, 31357, 31581, 31786, 31972, 32138, 32286,
        32413, 32522, 32610, 32679, 32729, 32758, 32767,
    };

    // SHADES.DAT, words 1-4 of each light level's twelve bytes (0x81010): the shade's slope, its floor,
    // its offset, and the radius in tiles.
    private static readonly int[,] shades =
    {
        { 56, 5, -3, 3 }, { 46, 4, -3, 3 }, { 39, 2, -2, 4 }, { 30, 1, 0, 4 },
        { 28, 0, 0, 5 }, { 21, 0, 0, 6 }, { 18, 0, 0, 7 }, { 18, 0, 0, 7 },
    };

    private const int Rows = 17;
    private const int Columns = 33;

    private class Ray
    {
        public int b0, dx, dy, col, f6, row, f8, tile;
        public int cellRow, cellCol, commitRow, commitCol;
        public Ray Copy() => (Ray)MemberwiseClone();
    }

    // The grid's two bytes per cell: the shade (high) from the light, and the code (low) the rays write.
    // The original keeps both in DGROUP between frames, and so does this.
    private readonly int[,] shade = new int[Rows, Columns];
    private readonly int[,] code = new int[Rows, Columns];
    private int shadeLevel = -1;
    private int radius;

    private int[] types;
    private int q, lateral, forward, fx, fy, h, eye;
    private int depth;

    /// <summary>The view's depth in rows at the last draw.</summary>
    public int Depth => depth;

    /// <summary>The heading's quadrant at the last draw: 0 +y, 1 +x, 2 -y, 3 -x.</summary>
    public int Quadrant => q;

    /// <summary>
    /// One frame of the view: sets the tiles newly drawn in <paramref name="drawn"/> and lists them in
    /// <paramref name="newlyDrawn"/>.
    /// </summary>
    /// <param name="tileTypes">The 4096 tile types, index y * 64 + x.</param>
    /// <param name="eyeX">The eye in 1/256 tile.</param>
    /// <param name="eyeY">The eye in 1/256 tile.</param>
    /// <param name="heading">A turn is 0x10000, 0 looking to +y, 0x4000 to +x.</param>
    /// <param name="lightLevel">The original's light level, 0 to 7.</param>
    public void Draw(int[] tileTypes, int eyeX, int eyeY, int heading, int lightLevel, bool[] drawn, List<int> newlyDrawn)
    {
        SetUp(tileTypes, eyeX, eyeY, heading, lightLevel);
        Build();
        DrawTiles(drawn, newlyDrawn);
    }

    // 0x2e136: the eye's tile, the quadrant, the fine position and the heading in the quadrant's frame.
    private void SetUp(int[] tileTypes, int eyeX, int eyeY, int heading, int lightLevel)
    {
        types = tileTypes;
        int tx = eyeX >> 8, ty = eyeY >> 8;
        int x = eyeX & 0xff, y = eyeY & 0xff;
        heading &= 0xffff;
        q = (((heading >> 13) + 1) & 7) >> 1;
        lateral = quadrantLateral[q];
        forward = quadrantForward[q];
        switch (q)
        {
        case 1: fx = 0xff - y; fy = x; break;
        case 2: fx = 0xff - x; fy = 0xff - y; break;
        case 3: fx = y; fy = 0xff - x; break;
        default: fx = x; fy = y; break;
        }
        int hh = (heading - quadrantHeading[q]) & 0xffff;
        h = hh >= 0x8000 ? hh - 0x10000 : hh;
        eye = ty * 64 + tx;
        if (lightLevel != shadeLevel)
        {
            SetShades(Math.Clamp(lightLevel, 0, 7));
        }
    }

    // 0x81010 -> 0x2e24b: the shade of every grid cell, by its distance from the eye, 15 beyond the
    // radius. Done only when the light level changes, as there.
    private void SetShades(int level)
    {
        shadeLevel = level;
        int w1 = Math.Max(shades[level, 0], 1), w2 = shades[level, 1], w3 = shades[level, 2];
        radius = shades[level, 3];
        int[] tab = new int[16];
        for (int i = 0; i < 16; ++i)
        {
            if (i > radius)
            {
                tab[i] = 15;
                continue;
            }
            int v = ISqrt(2 * (8 * i) * (8 * i));
            tab[i] = Math.Min(14, Math.Max(0, (v * w1 >> 6) + w3) + w2);
        }
        for (int r = 0; r < Rows; ++r)
        {
            for (int c = 0; c < Columns; ++c)
            {
                int d = ISqrt((16 - c) * (16 - c) + r * r);
                shade[r, c] = d > radius ? 15 : tab[d];
            }
        }
    }

    private static int ISqrt(int n)
    {
        int r = (int)Math.Sqrt(n);
        while (r * r > n) --r;
        while ((r + 1) * (r + 1) <= n) ++r;
        return r;
    }

    private static int SignedByte(int v)
    {
        v &= 0xff;
        return v >= 128 ? v - 256 : v;
    }

    // 0x22fd8: sine and cosine by linear interpolation, 256 steps a turn.
    private static void SinCos(int a, out int s, out int c)
    {
        a &= 0xffff;
        int i = a >> 8, f = a & 0xff;
        s = Sine(i) + (((Sine(i + 1) - Sine(i)) * f) >> 8);
        c = Sine(i + 64) + (((Sine(i + 65) - Sine(i + 64)) * f) >> 8);
    }

    private static int Sine(int i)
    {
        i &= 0xff;
        if (i <= 64) return sineQuarter[i];
        if (i <= 128) return sineQuarter[128 - i];
        if (i <= 192) return -sineQuarter[i - 128];
        return -sineQuarter[256 - i];
    }

    // Off the map counts as solid.
    private int TileType(int idx) => idx >= 0 && idx < 4096 ? types[idx] : 0;

    private int RotatedType(int idx) => rotatedType[q * 16 + (TileType(idx) & 0xf)];

    private int Shade(int row, int col) => row >= 0 && row < Rows && col >= 0 && col < Columns ? shade[row, col] : 15;

    private void SetCode(int row, int col, int v)
    {
        if (row >= 0 && row < Rows && col >= 0 && col < Columns) code[row, col] = v;
    }

    // 0x2e41f and 0x2e445: one tile to the right or to the left.
    private void StepRight(Ray r)
    {
        r.col = SignedByte(r.col + 1);
        r.tile += lateral;
        ++r.cellCol;
    }

    private void StepLeft(Ray r)
    {
        r.col = SignedByte(r.col - 1);
        r.tile -= lateral;
        --r.cellCol;
    }

    // 0x2e46b: the code byte of the ray's cell, and whether the ray moves sideways past it. The wall-face
    // bits are left out: they never touch bit 7.
    private int Write(Ray r, int step, int mask)
    {
        int t = RotatedType(r.tile);
        int c;
        if (r.col == 0)
        {
            c = 0;
        }
        else
        {
            c = (r.col > 0 ? 1 : 0) + 1;
            int row = Math.Abs(SignedByte(r.row));
            if (Math.Abs(r.col) > row) c += 2;
            if (Math.Abs(r.col) == row) c += 4;
        }
        int v = cellCode[t * 7 + c];
        SetCode(r.cellRow, r.cellCol, v);
        if (v == 0 || step == 0)
        {
            return 0;
        }
        int tf = RotatedType(r.tile + forward);
        bool move = false;
        if ((blocking[tf] & 8) == mask && (blocking[t] & 0x10) == d535[mask == 8 ? 1 : 0])
        {
            move = true;
        }
        if (!move && (blocking[t] & 1) == 1 && (blocking[t] & 0x10) == d535[mask == 0 ? 1 : 0])
        {
            move = true;
        }
        if (!move)
        {
            return 0;
        }
        if (step == 1)
        {
            StepRight(r);
            r.f6 = 0;
        }
        else
        {
            StepLeft(r);
            r.f6 = 0xff;
        }
        return 1;
    }

    private static bool SameSide(Ray r, int di) => (r.b0 & 0x80) == (di << 7);

    private static void Commit(Ray r)
    {
        r.commitRow = r.cellRow;
        r.commitCol = r.cellCol;
    }

    // 0x2ea39: one row of a ray's walk, a DDA on the tile map.
    private void RayStep(Ray r)
    {
        int di = r.dx >= 0 ? 1 : 0;
        int frac = di == 1 ? 0x100 - r.f6 : r.f6;
        if (SameSide(r, di)) Commit(r);
        int s = d541[di];
        bool flag;
        if (r.dy == 0) flag = true;
        else if (r.dx == 0) flag = false;
        else flag = (s * r.dx) * (0x100 - r.f8) > frac * r.dy;
        while (flag)
        {
            int t = RotatedType(r.tile);
            if ((blocking[t] & d539[di]) != 0)
            {
                Blocked(r, di, t);
                return;
            }
            int tn = RotatedType(r.tile + s * lateral);
            if ((blocking[tn] & d539[(di + 1) % 2]) != 0)
            {
                Blocked(r, di, t);
                return;
            }
            int p = s * r.dx, qq = r.dy * frac;
            r.f8 = (r.f8 + qq / p) & 0xff;
            r.f6 = ((di + 1) % 2) * 0xff;
            frac = 0x100;
            if (SameSide(r, di)) Write(r, 0, 0);
            if (s == 1) StepRight(r);
            else StepLeft(r);
            if (Shade(r.cellRow, r.cellCol) == 15 || Math.Abs(r.col) > 16)
            {
                if (d541[(di + 1) % 2] == 1) StepRight(r);
                else StepLeft(r);
                r.f6 = di * 0xff;
                r.f8 = 0xff;
                if (!SameSide(r, di)) Commit(r);
                return;
            }
            flag = r.dy == 0 || (s * r.dx) * (0x100 - r.f8) > frac * r.dy;
        }
        // 0x2ed42, the forward finish
        int pf = (s * r.dx) * (0xff - r.f8);
        int qv = r.dy != 0 ? pf / r.dy : 0;
        r.f6 = (r.f6 + s * qv) & 0xff;
        r.f8 = 0xff;
        if (!SameSide(r, di)) Commit(r);
    }

    // 0x2ec73: the ray met a blocking side.
    private static void Blocked(Ray r, int di, int t)
    {
        r.f8 = 0xff;
        if ((blocking[t] & d539[di]) == d539[di] && d53d[di] == t)
        {
            r.f6 = ((di + 1) % 2) * 0xff;
        }
        else
        {
            r.f6 = di * 0xff;
        }
        if (!SameSide(r, di)) Commit(r);
    }

    // 0x2e891: both edges to the next row, re-aimed from the eye and widened a little.
    private bool Advance(Ray l, Ray r)
    {
        l.row = SignedByte(l.row + 1);
        if (l.row > 16) return false;
        while (Shade(l.cellRow + 1, l.cellCol) == 15)
        {
            StepRight(l);
            l.f6 = 0;
            Write(l, 0, 0);
            if (l.col > r.col) return false;
        }
        if (l.row > 1 || Math.Abs(l.f6 - fx) + Math.Abs(l.f8 - fy) > 16)
        {
            l.dx = ((l.col << 8) + l.f6) - fx;
            l.dx -= Math.Abs(l.dx) / 50 + 2;
            l.dy = (l.row << 8) - fy;
        }
        l.tile += forward;
        ++l.cellRow;
        l.f8 = 0;
        r.row = SignedByte(r.row + 1);
        while (Shade(r.cellRow + 1, r.cellCol) == 15)
        {
            StepLeft(r);
            r.f6 = 0xff;
            Write(r, 0, 0);
            if (l.col > r.col) return false;
        }
        if (r.row > 1 || Math.Abs(r.f6 - fx) + Math.Abs(r.f8 - fy) > 16)
        {
            r.dx = ((r.col << 8) + r.f6) - fx;
            r.dx += Math.Abs(r.dx) / 50 + 2;
            r.dy = (r.row << 8) - fy - 1;
        }
        r.f8 = 0;
        ++r.cellRow;
        r.tile += forward;
        return true;
    }

    // 0x2edb0: the cells of the span between the two edges, false once the edges cross.
    private bool Span(Ray l, Ray r)
    {
        bool Crossed() => ((l.col << 8) + l.f6) > ((r.col << 8) + r.f6);
        while (Write(l, 1, 8) != 0)
        {
            if (Crossed()) return false;
        }
        if (l.col < r.col)
        {
            while (Write(r, -1, 8) != 0)
            {
            }
        }
        Ray temp = l.Copy();
        if (!Advance(l, r)) return false;
        if (l.col == r.col) return true;
        // the walker, 0x2ee9e-0x2eef8
        StepRight(temp);
        while (r.col > temp.col)
        {
            if (Write(temp, 1, 0) != 0) continue;
            if (r.col > temp.col)
            {
                StepRight(temp);
                while (Write(temp, 1, 8) != 0 && r.col > temp.col)
                {
                }
                StepRight(temp);
            }
        }
        return true;
    }

    // 0x2e354 and 0x2eefe: the two edge rays, then row by row the span, zeroing the code of every cell
    // outside it, until the span dies. The row it dies in is the depth.
    private void Build()
    {
        bool alive = (TileType(eye) & 0xf) != 0;
        Ray l = null, r = null;
        if (alive)
        {
            l = NewRay(0x81, h - 0x2040);
            r = NewRay(0x0f, h + 0x2040);
        }
        depth = -1;
        while (true)
        {
            ++depth;
            int row = depth;
            if (row >= Rows) break;
            int p = 0;
            if (alive)
            {
                RayStep(l);
                RayStep(r);
                int lc = l.commitRow == row ? l.commitCol : (l.commitRow < row ? 0 : Columns);
                for (; p < lc; ++p) code[row, p] = 0;
                p = r.commitRow == row ? r.commitCol + 1 : (r.commitRow > row ? Columns : 0);
                alive = Span(l, r);
            }
            for (; p < Columns; ++p) code[row, p] = 0;
            if (!alive) break;
        }
    }

    private Ray NewRay(int b0, int angle)
    {
        SinCos(angle, out int sn, out int cs);
        return new Ray
        {
            b0 = b0, col = 0, row = 0, f6 = fx, f8 = fy, tile = eye,
            cellRow = 0, cellCol = 16, commitRow = 0, commitCol = 16,
            dx = sn >> 4, dy = cs >> 4,
        };
    }

    // 0x21626 with 0x21ebc per cell: a cell of the span (bit 7 of its code) whose shade is below 8 is drawn.
    private void DrawTiles(bool[] drawn, List<int> newlyDrawn)
    {
        for (int row = Math.Min(depth, Rows - 1); row >= 0; --row)
        {
            int rowBase = eye + row * forward;
            for (int ci = 0; ci < Columns; ++ci)
            {
                int idx = rowBase + (ci - 16) * lateral;
                if (idx < 0 || idx >= 4096) continue;
                if ((code[row, ci] & 0x80) != 0 && shade[row, ci] < 8 && !drawn[idx])
                {
                    drawn[idx] = true;
                    newlyDrawn.Add(idx);
                }
            }
        }
    }
}
