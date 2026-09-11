using System;
using System.Collections.Generic;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod
{
    /// <summary>
    /// ND-75: a feltöltött quadok vetítésének ritka képernyőmintázása.
    /// Nincs LOD-cél, modellmintavétel vagy Unity-függőség.
    /// A clip-tér a CPU kamera -w..w mélységi konvencióját használja.
    /// </summary>
    public sealed class RenderedTileDiagnostics
    {
        public readonly struct ClipPoint
        {
            public readonly double X, Y, Z, W;
            public ClipPoint(double x, double y, double z, double w) { X=x; Y=y; Z=z; W=w; }
            public static ClipPoint Lerp(ClipPoint a, ClipPoint b, double t)
                => new ClipPoint(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t,a.Z+(b.Z-a.Z)*t,a.W+(b.W-a.W)*t);
            public bool Finite => FiniteNumber(X) && FiniteNumber(Y) && FiniteNumber(Z) && FiniteNumber(W);
        }

        public readonly struct Hit
        {
            public readonly bool Found;
            public readonly int Surface, Quad, Mesh;
            public readonly double Depth, WidthPx, HeightPx, DiameterPx, FullDiameterPx;
            public Hit(int surface, int quad, double depth, double width, double height, double diameter, double fullDiameter, int mesh=0)
            { Found=true; Surface=surface; Quad=quad; Mesh=mesh; Depth=depth; WidthPx=width; HeightPx=height; DiameterPx=diameter; FullDiameterPx=fullDiameter; }
        }

        public int Columns { get; }
        public int Rows { get; }
        public int Width { get; }
        public int Height { get; }
        public Hit[] Hits { get; }
        public int TestedQuads { get; private set; }
        public int InvalidQuads { get; private set; }
        private readonly ClipPoint[] _quad = new ClipPoint[4];
        private readonly ClipPoint[] _first = new ClipPoint[12], _second = new ClipPoint[12], _scratch = new ClipPoint[12];
        private readonly double[] _px = new double[24], _py = new double[24];

        public RenderedTileDiagnostics(int width, int height, int columns=17, int rows=9)
        {
            if (width<=0 || height<=0 || columns<=0 || rows<=0) throw new ArgumentOutOfRangeException(nameof(width));
            Width=width; Height=height; Columns=columns; Rows=rows;
            Hits=new Hit[checked(columns*rows)];
        }

        public double SampleX(int column) => (column+.5)*Width/Columns;
        public double SampleY(int row) => (row+.5)*Height/Rows;

        /// <summary>Indexelt quad: a két háromszög windingja a tényleges indexbufferé. Cull: 0=Off, 1=Front, 2=Back.</summary>
        public void AddQuad(ClipPoint p0, ClipPoint p1, ClipPoint p2, ClipPoint p3,
            int a, int b, int c, int d, int e, int f, int surface, int quad, int cullMode=2, int mesh=0)
        {
            TestedQuads++;
            _quad[0]=p0; _quad[1]=p1; _quad[2]=p2; _quad[3]=p3;
            if (!p0.Finite || !p1.Finite || !p2.Finite || !p3.Finite)
            { InvalidQuads++; return; }
            // A mintarácsot el nem érő, kamera előtti quadokhoz nem kell
            // clipping/rasterizálás. Ez nem láthatósági közelítés: a vetített
            // befoglaló téglalap egyetlen mérőpontot sem tartalmaz.
            if (p0.W>0 && p1.W>0 && p2.W>0 && p3.W>0)
            {
                double left=double.PositiveInfinity,right=double.NegativeInfinity,bottom=double.PositiveInfinity,top=double.NegativeInfinity;
                for(int i=0;i<4;i++)
                {
                    double x=(_quad[i].X/_quad[i].W+1)*Columns*.5-.5;
                    double y=(_quad[i].Y/_quad[i].W+1)*Rows*.5-.5;
                    left=Math.Min(left,x); right=Math.Max(right,x); bottom=Math.Min(bottom,y); top=Math.Max(top,y);
                }
                if(Math.Ceiling(Math.Max(0,left))>Math.Floor(Math.Min(Columns-1,right))
                    || Math.Ceiling(Math.Max(0,bottom))>Math.Floor(Math.Min(Rows-1,top))) return;
            }
            int n1=ClipTriangle(_quad[a],_quad[b],_quad[c],_first,cullMode);
            int n2=ClipTriangle(_quad[d],_quad[e],_quad[f],_second,cullMode);
            if (n1+n2==0) return;
            int count=0;
            double minX=double.PositiveInfinity,minY=double.PositiveInfinity,maxX=double.NegativeInfinity,maxY=double.NegativeInfinity;
            Include(_first,n1,ref count,ref minX,ref minY,ref maxX,ref maxY);
            Include(_second,n2,ref count,ref minX,ref minY,ref maxX,ref maxY);
            double diameter=Diameter(_px,_py,count);
            double fullDiameter=double.NaN;
            if (p0.W>0 && p1.W>0 && p2.W>0 && p3.W>0 && p0.Z>=-p0.W && p1.Z>=-p1.W && p2.Z>=-p2.W && p3.Z>=-p3.W)
            {
                for(int i=0;i<4;i++) { _px[i]=(_quad[i].X/_quad[i].W+1)*Width*.5; _py[i]=(_quad[i].Y/_quad[i].W+1)*Height*.5; }
                fullDiameter=Diameter(_px,_py,4);
            }
            Rasterize(_first,n1,surface,quad,maxX-minX,maxY-minY,diameter,fullDiameter,mesh);
            Rasterize(_second,n2,surface,quad,maxX-minX,maxY-minY,diameter,fullDiameter,mesh);
        }

        private void Include(ClipPoint[] polygon,int n,ref int count,ref double minX,ref double minY,ref double maxX,ref double maxY)
        {
            for(int i=0;i<n;i++)
            {
                double x=(polygon[i].X/polygon[i].W+1)*Width*.5;
                double y=(polygon[i].Y/polygon[i].W+1)*Height*.5;
                _px[count]=x; _py[count++]=y;
                minX=Math.Min(minX,x); minY=Math.Min(minY,y); maxX=Math.Max(maxX,x); maxY=Math.Max(maxY,y);
            }
        }

        private int ClipTriangle(ClipPoint a,ClipPoint b,ClipPoint c,ClipPoint[] destination,int cull)
        {
            destination[0]=a; destination[1]=b; destination[2]=c;
            int count=3;
            ClipPoint[] input=destination, output=_scratch;
            for(int plane=0;plane<6;plane++)
            {
                int outside=0;
                for(int i=0;i<count;i++) if (Distance(input[i],plane)<0) outside++;
                if(outside==count) return 0;
                if(outside==0) continue;
                int next=0;
                ClipPoint previous=input[count-1]; double pd=Distance(previous,plane);
                for(int i=0;i<count;i++)
                {
                    ClipPoint current=input[i]; double cd=Distance(current,plane);
                    if((pd>=0)!=(cd>=0)) output[next++]=ClipPoint.Lerp(previous,current,pd/(pd-cd));
                    if(cd>=0) output[next++]=current;
                    previous=current; pd=cd;
                }
                var swap=input; input=output; output=swap; count=next;
            }
            if(count<3) return 0;
            if(input!=destination) Array.Copy(input,destination,count);
            double area=0;
            for(int i=0;i<count;i++)
            {
                ClipPoint p=destination[i],q=destination[(i+1)%count];
                if(p.W<=1e-12 || q.W<=1e-12) return 0;
                area+=(p.X/p.W)*(q.Y/q.W)-(q.X/q.W)*(p.Y/p.W);
            }
            // Unity CPU-kameravetítésben az óramutató járásával egyező
            // képernyő-winding az elülső lap (Y felfelé).
            if(Math.Abs(area)<1e-18 || (cull==2 && area>=0) || (cull==1 && area<=0)) return 0;
            return count;
        }

        private void Rasterize(ClipPoint[] polygon,int count,int surface,int quad,double width,double height,double diameter,double fullDiameter,int mesh)
        {
            for(int t=1;t<count-1;t++)
            {
                ClipPoint a=polygon[0],b=polygon[t],c=polygon[t+1];
                double ax=(a.X/a.W+1)*Width*.5,ay=(a.Y/a.W+1)*Height*.5;
                double bx=(b.X/b.W+1)*Width*.5,by=(b.Y/b.W+1)*Height*.5;
                double cx=(c.X/c.W+1)*Width*.5,cy=(c.Y/c.W+1)*Height*.5;
                int x0=Math.Max(0,(int)Math.Ceiling(Math.Min(ax,Math.Min(bx,cx))*Columns/Width-.5));
                int x1=Math.Min(Columns-1,(int)Math.Floor(Math.Max(ax,Math.Max(bx,cx))*Columns/Width-.5));
                int y0=Math.Max(0,(int)Math.Ceiling(Math.Min(ay,Math.Min(by,cy))*Rows/Height-.5));
                int y1=Math.Min(Rows-1,(int)Math.Floor(Math.Max(ay,Math.Max(by,cy))*Rows/Height-.5));
                double det=(by-cy)*(ax-cx)+(cx-bx)*(ay-cy);
                if(Math.Abs(det)<1e-15) continue;
                for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++)
                {
                    double px=SampleX(x),py=SampleY(y);
                    double wa=((by-cy)*(px-cx)+(cx-bx)*(py-cy))/det;
                    double wb=((cy-ay)*(px-cx)+(ax-cx)*(py-cy))/det;
                    double wc=1-wa-wb;
                    if(wa < -1e-9 || wb < -1e-9 || wc < -1e-9) continue;
                    // A z/w képernyőn lineáris; nem világmélységet interpolálunk.
                    double depth=wa*a.Z/a.W+wb*b.Z/b.W+wc*c.Z/c.W;
                    int index=y*Columns+x; Hit previous=Hits[index];
                    if(!previous.Found || depth<previous.Depth)
                        Hits[index]=new Hit(surface,quad,depth,width,height,diameter,fullDiameter,mesh);
                }
            }
        }

        private static double Diameter(double[] x,double[] y,int count)
        {
            double max=0;
            for(int i=0;i<count;i++) for(int j=i+1;j<count;j++)
            { double dx=x[i]-x[j],dy=y[i]-y[j]; max=Math.Max(max,dx*dx+dy*dy); }
            return Math.Sqrt(max);
        }
        private static double Distance(ClipPoint p,int plane)
        {
            switch(plane) { case 0:return p.W+p.X; case 1:return p.W-p.X; case 2:return p.W+p.Y;
                case 3:return p.W-p.Y; case 4:return p.W+p.Z; default:return p.W-p.Z; }
        }
        private static bool FiniteNumber(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
    }

    /// <summary>Emissziós sorrend → konkatenált quad-index, geometriai visszabecslés nélkül.</summary>
    public sealed class TerrainQuadIdentity
    {
        private readonly TileId[] _tiles;
        private readonly int[] _next, _end;
        public TerrainQuadIdentity(IReadOnlyList<int> bucketQuadCounts)
        {
            _next=new int[bucketQuadCounts.Count]; _end=new int[bucketQuadCounts.Count];
            int total=0;
            for (int i=0;i<_next.Length;i++)
            {
                if (bucketQuadCounts[i]<0) throw new ArgumentOutOfRangeException(nameof(bucketQuadCounts));
                _next[i]=total; total=checked(total+bucketQuadCounts[i]); _end[i]=total;
            }
            _tiles=new TileId[total];
        }
        public void Add(int bucket, TileId tile)
        {
            if ((uint)bucket >= (uint)_next.Length || _next[bucket]>=_end[bucket])
                throw new InvalidOperationException("A tile-azonosság nem illeszkedik a mesh bucketéhez.");
            _tiles[_next[bucket]++]=tile;
        }
        public TileId[] Complete()
        {
            for (int i=0;i<_next.Length;i++) if (_next[i]!=_end[i])
                throw new InvalidOperationException("Hiányos terrain quad-azonosság.");
            return _tiles;
        }
    }
}
