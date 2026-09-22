using WorldGen.Core.Grid;
using Xunit;

namespace WorldGen.Core.Tests.Grid;

/// <summary>
/// ND-130: a <see cref="TileGeometry.ToFaceUV"/> a
/// <see cref="TileGeometry.PositionFromFaceUV"/> inverze, es a
/// <see cref="TileGeometry.FromPosition"/> elso fele. A ketto
/// KONZISZTENCIAJA a lenyeg: ha a hivo a ToFaceUV-bol szamolt tile-indexet
/// hasznalja, UGYANAZT a tile-t kell kapnia, mint a FromPosition-tol -
/// kulonben egy mezo-interpolacio rossz cellahoz igazodna.
/// </summary>
public class TileGeometryFaceUvTests
{
    /// <summary>
    /// Oda-vissza: tile-kozeppont -> pozicio -> (face,uc,vc) -> pozicio.
    /// Tolerancia-alapu (Math.Tan/Math.Atan, ND-23b/ND-24), nem bitpontos.
    /// </summary>
    [Fact]
    public void PositionFromFaceUvRoundTripsThroughToFaceUv()
    {
        for (int face = 0; face < 6; face++)
        {
            for (int level = 0; level <= 6; level += 2)
            {
                int n = 1 << level;
                for (uint u = 0; u < n; u += (uint)(n > 8 ? n / 8 : 1))
                {
                    for (uint v = 0; v < n; v += (uint)(n > 8 ? n / 8 : 1))
                    {
                        TileId id = TileId.FromFaceLevelUV(face, level, u, v);
                        TileGeometry.ToPosition(id, out double x, out double y, out double z);

                        TileGeometry.ToFaceUV(x, y, z, out int gotFace, out double uc, out double vc);
                        Assert.Equal(face, gotFace);

                        TileGeometry.PositionFromFaceUV(gotFace, uc, vc,
                            out double bx, out double by, out double bz);
                        Assert.Equal(x, bx, 12);
                        Assert.Equal(y, by, 12);
                        Assert.Equal(z, bz, 12);
                    }
                }
            }
        }
    }

    /// <summary>
    /// A ToFaceUV-bol ugyanazzal a keplettel szamolt tile-index MEGEGYEZIK a
    /// FromPosition eredmenyevel - minden lapon, tobb szinten, a tile
    /// kozepen ES a tile-on beluli eltolt pontokon is.
    /// </summary>
    [Fact]
    public void DerivedTileIndexMatchesFromPosition()
    {
        double[] offsets = { 0.05, 0.5, 0.95 };
        for (int face = 0; face < 6; face++)
        {
            for (int level = 1; level <= 7; level++)
            {
                int n = 1 << level;
                for (uint u = 0; u < n; u += (uint)(n > 6 ? n / 6 : 1))
                {
                    for (uint v = 0; v < n; v += (uint)(n > 6 ? n / 6 : 1))
                    {
                        TileId id = TileId.FromFaceLevelUV(face, level, u, v);
                        TileGeometry.GetContinuousBounds(id,
                            out double uMin, out double uMax, out double vMin, out double vMax);
                        foreach (double fu in offsets)
                        {
                            foreach (double fv in offsets)
                            {
                                TileGeometry.PositionFromFaceUV(face,
                                    uMin + (uMax - uMin) * fu, vMin + (vMax - vMin) * fv,
                                    out double x, out double y, out double z);

                                TileId expected = TileGeometry.FromPosition(x, y, z, level);
                                TileGeometry.ToFaceUV(x, y, z, out int gotFace, out double uc, out double vc);

                                long gu = (long)((uc + 1.0) / 2.0 * n);
                                long gv = (long)((vc + 1.0) / 2.0 * n);
                                gu = gu < 0 ? 0 : (gu > n - 1 ? n - 1 : gu);
                                gv = gv < 0 ? 0 : (gv > n - 1 ? n - 1 : gv);
                                TileId derived = TileId.FromFaceLevelUV(gotFace, level, (uint)gu, (uint)gv);

                                Assert.Equal(expected.Value, derived.Value);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Tisztasag: ismetelt hivas ugyanarra a pontra BITRE azonos.</summary>
    [Fact]
    public void IsPureWithinAProcess()
    {
        TileGeometry.ToPosition(TileId.FromFaceLevelUV(3, 5, 7, 19), out double x, out double y, out double z);
        TileGeometry.ToFaceUV(x, y, z, out int f1, out double u1, out double v1);
        for (int i = 0; i < 16; i++)
        {
            TileGeometry.ToFaceUV(x, y, z, out int f2, out double u2, out double v2);
            Assert.Equal(f1, f2);
            Assert.Equal(u1, u2); // bitpontos egyezes
            Assert.Equal(v1, v2);
        }
    }

    /// <summary>
    /// A lap-kozepponton (0,0)-t kell adnia, es a hat lap-normalis pontosan
    /// a sajat lapjara kell essen - ez fogja meg egy esetleges
    /// lap-index/elojel elcsuszast.
    /// </summary>
    [Theory]
    [InlineData(0, 1.0, 0.0, 0.0)]
    [InlineData(1, -1.0, 0.0, 0.0)]
    [InlineData(2, 0.0, 1.0, 0.0)]
    [InlineData(3, 0.0, -1.0, 0.0)]
    [InlineData(4, 0.0, 0.0, 1.0)]
    [InlineData(5, 0.0, 0.0, -1.0)]
    public void FaceNormalMapsToItsOwnFaceCenter(int face, double x, double y, double z)
    {
        TileGeometry.ToFaceUV(x, y, z, out int gotFace, out double uc, out double vc);
        Assert.Equal(face, gotFace);
        Assert.Equal(0.0, uc, 12);
        Assert.Equal(0.0, vc, 12);
    }
}
