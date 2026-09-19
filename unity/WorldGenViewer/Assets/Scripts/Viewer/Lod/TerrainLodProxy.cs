using System;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod
{
    /// <summary>
    /// ND-74: változatlan Build-snapshot, a statikus sarokminták sugarából.
    /// Csak LOD-távolságközelítés; nem a folytonos terep bizonyított korlátja.
    /// A sűrű elrendezés face/u/v, v a leggyorsabban változó index.
    /// </summary>
    public sealed class TerrainLodProxy
    {
        private readonly double[] _radii;
        private readonly double[][] _maxima;
        private readonly double _minimumRadius;
        public int BaseLevel { get; }
        public long StorageBytes { get; }

        /// <summary>
        /// A proxy legkisebb sugara (tengerszint vagy afölött). Horizont-
        /// vágásnál EZ a takaró gömb: a legkisebb takaró a legkevesebbet
        /// rejti el, tehát csak bizonyítottan nem látható csomópontot vágunk.
        /// </summary>
        public double MinimumRadius => _minimumRadius;

        /// <summary>
        /// A proxy legnagyobb sugara. Horizont-vágásnál a csomópontot ERRE a
        /// burokra tesszük - egy hegycsúcs átkukucskálhat a horizonton, és
        /// azt nem szabad kivágni.
        /// </summary>
        public double MaximumRadius { get; }

        public TerrainLodProxy(int baseLevel, double[] cornerRadii, double seaRadius)
        {
            if (baseLevel < 0 || baseLevel > 8) throw new ArgumentOutOfRangeException(nameof(baseLevel));
            if (cornerRadii == null) throw new ArgumentNullException(nameof(cornerRadii));
            if (!PositiveFinite(seaRadius)) throw new ArgumentOutOfRangeException(nameof(seaRadius));
            BaseLevel = baseLevel;
            int n = 1 << baseLevel, side = n + 1;
            if (cornerRadii.Length != 6 * side * side) throw new ArgumentException("Hiányos sarokrács.", nameof(cornerRadii));
            _radii = new double[cornerRadii.Length];
            _minimumRadius = double.PositiveInfinity;
            double maximumRadius = 0.0;
            for (int i = 0; i < _radii.Length; i++)
            {
                if (!PositiveFinite(cornerRadii[i])) throw new ArgumentOutOfRangeException(nameof(cornerRadii));
                _radii[i] = Math.Max(seaRadius, cornerRadii[i]);
                _minimumRadius = Math.Min(_minimumRadius, _radii[i]);
                maximumRadius = Math.Max(maximumRadius, _radii[i]);
            }
            MaximumRadius = maximumRadius;
            long count = _radii.Length;
            _maxima = new double[baseLevel + 1][];
            _maxima[baseLevel] = new double[6 * n * n];
            for (int face = 0; face < 6; face++)
                for (int u = 0; u < n; u++)
                    for (int v = 0; v < n; v++)
                    {
                        int i = face * side * side + u * side + v;
                        _maxima[baseLevel][face * n * n + u * n + v] = Math.Max(
                            Math.Max(_radii[i], _radii[i + 1]), Math.Max(_radii[i + side], _radii[i + side + 1]));
                    }
            count += _maxima[baseLevel].Length;
            for (int level = baseLevel - 1; level >= 0; level--)
            {
                int size = 1 << level, childSize = size * 2;
                double[] values = _maxima[level] = new double[6 * size * size];
                double[] children = _maxima[level + 1];
                count += values.Length;
                for (int face = 0; face < 6; face++)
                    for (int u = 0; u < size; u++)
                        for (int v = 0; v < size; v++)
                        {
                            int i = face * childSize * childSize + 2 * u * childSize + 2 * v;
                            values[face * size * size + u * size + v] = Math.Max(
                                Math.Max(children[i], children[i + 1]), Math.Max(children[i + childSize], children[i + childSize + 1]));
                        }
            }
            StorageBytes = count * sizeof(double);
        }

        public double RadiusAt(TileId tile)
        {
            tile.GetUV(out uint u, out uint v);
            if (tile.Level < BaseLevel)
            {
                int n = 1 << tile.Level;
                return _maxima[tile.Level][tile.Face * n * n + (int)u * n + (int)v];
            }
            // A tile középpontja a base-rács koordinátáiban. A kettőhatvány
            // osztás a támogatott TileId-szinteken pontosan reprezentálható.
            double factor = 1.0 / (1 << (tile.Level - BaseLevel));
            double x = (u + 0.5) * factor, y = (v + 0.5) * factor;
            int iu = (int)x, iv = (int)y, side = (1 << BaseLevel) + 1;
            double fu = x - iu, fv = y - iv;
            int i = tile.Face * side * side + iu * side + iv;
            double low = _radii[i] * (1 - fu) + _radii[i + side] * fu;
            double high = _radii[i + 1] * (1 - fu) + _radii[i + side + 1] * fu;
            return low * (1 - fv) + high * fv;
        }

        /// <summary>A cut és a morph közös metrikája; nem geometria-emisszió.</summary>
        public void GetMetric(TileId tile, double referenceRadius,
            out double x, out double y, out double z, out double footprint)
        {
            AdaptiveQuadTree.GetCenterAndBoundingRadius(tile, referenceRadius, out x, out y, out z, out footprint);
            ScaleMetric(tile, referenceRadius, ref x, ref y, ref z, ref footprint);
        }

        public void ScaleMetric(TileId tile, double referenceRadius,
            ref double x, ref double y, ref double z, ref double footprint)
        {
            if (!PositiveFinite(referenceRadius)) throw new ArgumentOutOfRangeException(nameof(referenceRadius));
            double scale = RadiusAt(tile) / referenceRadius;
            x *= scale; y *= scale; z *= scale; footprint *= scale;
        }

        /// <summary>
        /// ND-76: a proxy ismert sugarainak kiterjedése, nem csak középmagasság.
        /// A base-en belüli bilineáris mező szélsőértékei a négy sarokban vannak.
        /// A valódi, finomabb terep minták közti eltérésére ez nem szigorú korlát.
        /// </summary>
        public SurfaceLodBounds BoundsAt(TileId tile)
        {
            double min, max;
            if (tile.Level < BaseLevel) { min = _minimumRadius; max = RadiusAt(tile); }
            else
            {
                tile.GetUV(out uint u, out uint v);
                double factor = 1.0 / (1L << (tile.Level - BaseLevel));
                double a = RadiusAtGrid(tile.Face, u*factor, v*factor);
                double b = RadiusAtGrid(tile.Face, (u+1)*factor, v*factor);
                double c = RadiusAtGrid(tile.Face, (u+1)*factor, (v+1)*factor);
                double d = RadiusAtGrid(tile.Face, u*factor, (v+1)*factor);
                min = Math.Min(Math.Min(a,b),Math.Min(c,d)); max = Math.Max(Math.Max(a,b),Math.Max(c,d));
            }
            double middle = (min + max) * .5;
            AdaptiveQuadTree.GetCenterAndBoundingRadius(tile, middle, out double x, out double y, out double z, out double footprint);
            // Háromszög-egyenlőtlenség: gömbfolt-kiterjedés + radiális félintervallum.
            return new SurfaceLodBounds(x, y, z, footprint + (max-min)*.5);
        }

        private double RadiusAtGrid(int face, double x, double y)
        {
            int n = 1 << BaseLevel, side = n + 1;
            int u = Math.Min(n-1, (int)x), v = Math.Min(n-1, (int)y);
            double a = x-u, b = y-v;
            int i = face*side*side + u*side + v;
            return (_radii[i]*(1-a)+_radii[i+side]*a)*(1-b)
                + (_radii[i+1]*(1-a)+_radii[i+side+1]*a)*b;
        }

        public SurfaceQuad QuadAt(TileId tile)
        {
            if (tile.Level < BaseLevel) throw new ArgumentOutOfRangeException(nameof(tile));
            tile.GetUV(out uint u,out uint v);
            double factor=1.0/(1L<<(tile.Level-BaseLevel)), n=1<<BaseLevel;
            SurfacePoint Point(double x,double y)
            {
                TileGeometry.PositionFromFaceUV(tile.Face,x/n*2-1,y/n*2-1,out double px,out double py,out double pz);
                double radius=RadiusAtGrid(tile.Face,x,y);
                return new SurfacePoint(px*radius,py*radius,pz*radius);
            }
            return new SurfaceQuad(Point(u*factor,v*factor),Point((u+1)*factor,v*factor),
                Point((u+1)*factor,(v+1)*factor),Point(u*factor,(v+1)*factor));
        }

        private static bool PositiveFinite(double value) => value > 0 && !double.IsInfinity(value);
    }
}
