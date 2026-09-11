using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod
{
    public readonly struct SurfacePoint
    {
        public readonly double X, Y, Z;
        public SurfacePoint(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static SurfacePoint operator +(SurfacePoint a, SurfacePoint b)
            => new SurfacePoint(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static SurfacePoint operator *(SurfacePoint a, double f)
            => new SurfacePoint(a.X * f, a.Y * f, a.Z * f);
        public static SurfacePoint Lerp(SurfacePoint a, SurfacePoint b, double t) => a * (1 - t) + b * t;
    }

    public readonly struct SurfaceQuad
    {
        public readonly SurfacePoint P00, P10, P11, P01;
        public SurfaceQuad(SurfacePoint p00, SurfacePoint p10, SurfacePoint p11, SurfacePoint p01)
        { P00 = p00; P10 = p10; P11 = p11; P01 = p01; }

        /// <summary>Az AddQuad 00–11 átlójának két sík háromszöge, nem bilineáris patch.</summary>
        public SurfacePoint At(double u, double v) => u >= v
            ? P00 * (1 - u) + P10 * (u - v) + P11 * v
            : P00 * (1 - v) + P11 * u + P01 * (v - u);
    }

    /// <summary>
    /// Kérésenként cache-elt közös csúcsok. A nyers quad a viewer teljes
    /// modelljéből és morphjából jön. A feloldás sem új domborzatot, sem zajt
    /// nem generál: a legdurvább szomszéd már renderelt élére illeszt (ND-70).
    /// </summary>
    public sealed class LodCornerResolver
    {
        private readonly int _baseLevel;
        private readonly LodCoverage _coverage;
        private readonly Func<TileId, SurfaceQuad> _rawQuad;
        private readonly Dictionary<TileId, SurfaceQuad> _quads = new Dictionary<TileId, SurfaceQuad>();
        private readonly Dictionary<(int Face, int Level, uint U, uint V), SurfacePoint> _corners
            = new Dictionary<(int, int, uint, uint), SurfacePoint>();
        public int CornerCount => _corners.Count;

        public LodCornerResolver(int baseLevel, LodCoverage coverage, Func<TileId, SurfaceQuad> rawQuad)
        { _baseLevel = baseLevel; _coverage = coverage; _rawQuad = rawQuad; }

        /// <summary>ND-77: ugyanaz a sarokgazda, mint a feloldásnál; nincs modellmintavétel.</summary>
        public TileId CornerOwner(int face, int level, uint u, uint v) => FindOwner(face,level,u,v);

        public SurfacePoint Corner(int face, int level, uint u, uint v)
        {
            var key = (face, level, u, v);
            if (_corners.TryGetValue(key, out SurfacePoint cached)) return cached;
            double side = 1L << level;
            double uc = u / side * 2 - 1, vc = v / side * 2 - 1;
            TileId owner = FindOwner(face, level, u, v);
            TileGeometry.PositionFromFaceUV(face, uc, vc, out double x, out double y, out double z);
            FaceUV(owner.Face, x, y, z, out double ou, out double ov);
            owner.GetUV(out uint iu, out uint iv);
            double ownerSide = 1L << owner.Level;
            double ru = SnapUnit((ou + 1) * .5 * ownerSide - iu);
            double rv = SnapUnit((ov + 1) * .5 * ownerSide - iv);
            SurfacePoint result;
            if (owner.Level == level)
            {
                if ((ru != 0 && ru != 1) || (rv != 0 && rv != 1))
                    throw new InvalidOperationException("Az azonos szintű közös pontnak saroknak kell lennie.");
                if (!_quads.TryGetValue(owner, out SurfaceQuad quad))
                { quad = _rawQuad(owner); _quads.Add(owner, quad); }
                result = ru == 0 ? (rv == 0 ? quad.P00 : quad.P01) : (rv == 0 ? quad.P10 : quad.P11);
            }
            else
            {
                // Csak SZIGORÚAN durvább szinten rekurzív: nem keletkezhet
                // kör a szomszédos tile-ok kölcsönös csúcshivatkozásaiból.
                if (owner.Level >= level) throw new InvalidOperationException("Hibás sarokgazda-szint.");
                if (ru == 0 || ru == 1)
                {
                    uint cu = iu + (uint)ru;
                    result = SurfacePoint.Lerp(Corner(owner.Face, owner.Level, cu, iv),
                        Corner(owner.Face, owner.Level, cu, iv + 1), rv);
                }
                else if (rv == 0 || rv == 1)
                {
                    uint cv = iv + (uint)rv;
                    result = SurfacePoint.Lerp(Corner(owner.Face, owner.Level, iu, cv),
                        Corner(owner.Face, owner.Level, iu + 1, cv), ru);
                }
                else throw new InvalidOperationException("Finom levél sarka nem lehet durva levél belsejében.");
            }
            _corners.Add(key, result);
            return result;
        }

        private TileId FindOwner(int face, int level, uint cornerU, uint cornerV)
        {
            bool found = false;
            TileId best = default;
            void Consider(TileId node)
            {
                while (node.Level > _baseLevel && !_coverage.Leaves.Contains(node)) node = node.Parent();
                if (node.Level == _baseLevel && _coverage.Roots.Contains(node)) return; // finomabb szomszéd
                if (!found || node.Level < best.Level || (node.Level == best.Level && node.Value < best.Value))
                { best = node; found = true; }
            }
            uint side = 1u << level;
            if (cornerU > 0 && cornerU < side && cornerV > 0 && cornerV < side)
            {
                // A lap belsejében egzakt egész koordináták: nincs tan/atan,
                // és nincs cellahatáron kerekítési bizonytalanság sem.
                for (uint a = cornerU - 1; a <= cornerU; a++)
                    for (uint b = cornerV - 1; b <= cornerV; b++)
                        Consider(TileId.FromFaceLevelUV(face, level, a, b));
            }
            else
            {
                double u = (double)cornerU / side * 2 - 1, v = (double)cornerV / side * 2 - 1;
                double step = .25 / side;
                // A face-váltás nyírja az UV-koordinátákat. A puszta ±1/±1
                // átlós minták ilyenkor cellahatárra eshetnek (L20 regresszió).
                // Két eltérő meredekség mindkét szomszédos ék belsejét eléri.
                for (int a = -1; a <= 1; a += 2) for (int b = -1; b <= 1; b += 2)
                for (int slope = 0; slope < 2; slope++)
                {
                    TileGeometry.PositionFromFaceUV(face, u + a * step * (slope == 0 ? 1 : 2),
                        v + b * step * (slope == 0 ? 2 : 1), out double x, out double y, out double z);
                    Consider(TileGeometry.FromPosition(x, y, z, level));
                }
            }
            if (!found) throw new InvalidOperationException("A renderpartíció nem fedi a kért sarkot.");
            return best;
        }

        private static double SnapUnit(double value)
        {
            if (Math.Abs(value) < 1e-6) return 0;
            if (Math.Abs(value - 1) < 1e-6) return 1;
            return value;
        }

        // A Core TileGeometry face-axis konvenciójának inverze. Kizárólag
        // viewer-geometria, nem szimulációs numerika. A face-éleket teszt fedi.
        private static void FaceUV(int face, double x, double y, double z, out double u, out double v)
        {
            double right, up, normal;
            switch (face)
            {
                case 0: right = -z; up = y; normal = x; break;
                case 1: right = z; up = y; normal = -x; break;
                case 2: right = x; up = z; normal = y; break;
                case 3: right = x; up = -z; normal = -y; break;
                case 4: right = x; up = y; normal = z; break;
                case 5: right = -x; up = y; normal = -z; break;
                default: throw new ArgumentOutOfRangeException(nameof(face));
            }
            u = Math.Atan(right / normal) * (4 / Math.PI);
            v = Math.Atan(up / normal) * (4 / Math.PI);
        }
    }
}
