using System;
using System.Collections.Generic;
using LibTessDotNet.Double;

namespace HollowMario
{
    // Pure geometry: world-space contours in, a welded extruded boundary out. Keeping this
    // independent of Unity lets the exact production converter run against room fixtures.
    internal static class ConnectedTerrainMesh
    {
        internal const double Grid = .0001; // Below 1/100 of one native collision unit.
        internal struct Point
        {
            internal double X, Y;
            internal Point(double x, double y) { X = x; Y = y; }
        }
        internal struct Vertex
        {
            internal double X, Y, Z;
            internal Vertex(Point p, double z) { X = p.X; Y = p.Y; Z = z; }
        }
        internal struct Triangle
        {
            internal Vertex A, B, C;
            internal Triangle(Vertex a, Vertex b, Vertex c) { A = a; B = b; C = c; }
        }
        internal sealed class Outline
        {
            internal int Owner;
            internal int Group;
            internal bool Hole;
            internal List<Point> Points;
        }
        internal sealed class Result
        {
            internal readonly Dictionary<int, List<Triangle>> Parts = new Dictionary<int, List<Triangle>>();
            internal readonly List<List<Point>> Boundary = new List<List<Point>>();
            internal int InputEdges, BoundaryEdges;
            internal double Area;
        }

        internal static double SignedArea(List<Point> p)
        {
            double area = 0;
            for (int i = 0; i < p.Count; i++) { Point a = p[i], b = p[(i+1)%p.Count]; area += a.X*b.Y-b.X*a.Y; }
            return area*.5;
        }
        private static double Cross(Point a, Point b, Point p) => (b.X-a.X)*(p.Y-a.Y)-(b.Y-a.Y)*(p.X-a.X);
        private static double Distance2(Point a, Point b) => (a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y);
        private static Point Snap(Point p) => new Point(Math.Round(p.X/Grid)*Grid, Math.Round(p.Y/Grid)*Grid);
        internal static bool Contains(List<Point> polygon, Point p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count-1; i < polygon.Count; j=i++)
            {
                Point a=polygon[i], b=polygon[j];
                if ((a.Y>p.Y)!=(b.Y>p.Y) && p.X<(b.X-a.X)*(p.Y-a.Y)/(b.Y-a.Y)+a.X) inside=!inside;
            }
            return inside;
        }
        internal static void ClassifyHoles(List<Outline> sources)
        {
            // Tile meshes can store every edge loop with the same winding. Nesting within
            // the SAME authored mesh identifies cavities; a different overlapping chunk
            // must remain an independent solid. Follow the nearest containing loop.
            var parents=new Dictionary<Outline,Outline>();
            foreach(Outline source in sources)
            {
                double area=Math.Abs(SignedArea(source.Points)),best=double.MaxValue;Outline parent=null;
                foreach(Outline other in sources)
                {
                    double otherArea=Math.Abs(SignedArea(other.Points));
                    if(other==source||other.Group!=source.Group||otherArea<=area+Grid*Grid||otherArea>=best)continue;
                    bool contained=true;
                    foreach(Point p in source.Points)if(!Contains(other.Points,p)){contained=false;break;}
                    if(contained){parent=other;best=otherArea;}
                }
                parents[source]=parent;
            }
            foreach(Outline source in sources)
            {
                int depth=0;Outline at=parents[source];
                while(at!=null){depth++;at=parents[at];}
                source.Hole=(depth&1)!=0;
            }
        }
        private static List<Point> Clean(List<Point> input)
        {
            var p = new List<Point>();
            foreach (Point raw in input)
            {
                if (double.IsNaN(raw.X) || double.IsNaN(raw.Y) || double.IsInfinity(raw.X) || double.IsInfinity(raw.Y))
                    throw new ArgumentException("Non-finite terrain coordinate");
                Point next=Snap(raw);
                if (p.Count==0 || Distance2(p[p.Count-1],next)>Grid*Grid*.01) p.Add(next);
            }
            if (p.Count>1 && Distance2(p[0],p[p.Count-1])<Grid*Grid*.01) p.RemoveAt(p.Count-1);
            if (p.Count<3 || Math.Abs(SignedArea(p))<Grid*Grid) throw new ArgumentException("Degenerate terrain outline");
            return p;
        }
        private static void AddContour(Tess tess, List<Point> points)
        {
            var vertices=new ContourVertex[points.Count];
            for(int i=0;i<vertices.Length;i++) vertices[i].Position=new Vec3 {X=points[i].X,Y=points[i].Y,Z=0};
            tess.AddContour(vertices,ContourOrientation.Original);
        }
        private static List<Point> SplitEdge(Point a, Point b, List<Point> cuts)
        {
            double dx=b.X-a.X, dy=b.Y-a.Y, length2=dx*dx+dy*dy;
            var positions=new List<double>{0,1};
            foreach(Point p in cuts)
            {
                double t=((p.X-a.X)*dx+(p.Y-a.Y)*dy)/length2;
                if(t>1e-9 && t<1-1e-9 && Math.Abs(Cross(a,b,p))<=Math.Sqrt(length2)*Grid*.05) positions.Add(t);
            }
            positions.Sort();
            var split=new List<Point>();double previous=-1;
            foreach(double t in positions)
                if(previous<0 || (t-previous)*Math.Sqrt(length2)>Grid*.05)
                {split.Add(new Point(a.X+dx*t,a.Y+dy*t));previous=t;}
            return split;
        }
        private static int EdgeOwner(List<Outline> sources, Point a, Point b)
        {
            Point mid=new Point((a.X+b.X)*.5,(a.Y+b.Y)*.5);
            double best=double.MaxValue;int owner=-1;
            foreach(Outline source in sources)
            for(int i=0;i<source.Points.Count;i++)
            {
                Point p=source.Points[i],q=source.Points[(i+1)%source.Points.Count];
                double length2=Distance2(p,q);
                double t=((mid.X-p.X)*(q.X-p.X)+(mid.Y-p.Y)*(q.Y-p.Y))/length2;
                if(t<-.000001 || t>1.000001) continue;
                double distance=Math.Abs(Cross(p,q,mid))/Math.Sqrt(length2);
                if(distance<best){best=distance;owner=source.Owner;}
            }
            if(best>Grid*2) throw new InvalidOperationException("Union boundary lost its source collider");
            return owner;
        }
        private static int CapOwner(List<Outline> sources, Point a, Point b, Point c)
        {
            Point centre=new Point((a.X+b.X+c.X)/3,(a.Y+b.Y+c.Y)/3);
            foreach(Outline source in sources) if(!source.Hole && Contains(source.Points,centre)) return source.Owner;
            throw new InvalidOperationException("Terrain cap outside source solids");
        }
        private static void Cap(Result result, List<Outline> sources, Point a, Point b, Point c)
        {
            double cross=Cross(a,b,c);
            if(Math.Abs(cross)<Grid*Grid*.01) return;
            if(cross<0){Point temp=b;b=c;c=temp;}
            int owner=CapOwner(sources,a,b,c);
            result.Parts[owner].Add(new Triangle(new Vertex(a,1),new Vertex(b,1),new Vertex(c,1)));
            result.Parts[owner].Add(new Triangle(new Vertex(a,-1),new Vertex(c,-1),new Vertex(b,-1)));
            result.Area+=Math.Abs(cross)*.5;
        }
        internal static Result Build(List<Outline> input)
        {
            var result=new Result();var sources=new List<Outline>();var allPoints=new List<Point>();var union=new Tess();
            foreach(Outline raw in input)
            {
                List<Point> p=Clean(raw.Points);
                if((SignedArea(p)>0)==raw.Hole) p.Reverse();
                sources.Add(new Outline{Owner=raw.Owner,Hole=raw.Hole,Points=p});
                if(!result.Parts.ContainsKey(raw.Owner)) result.Parts.Add(raw.Owner,new List<Triangle>());
                result.InputEdges+=p.Count;allPoints.AddRange(p);AddContour(union,p);
            }
            if(sources.Count==0) return result;
            union.Tessellate(WindingRule.Positive,ElementType.BoundaryContours,normal:new Vec3{Z=1});
            for(int contour=0;contour<union.ElementCount;contour++)
            {
                int start=union.Elements[contour*2], count=union.Elements[contour*2+1];
                var path=new List<Point>();
                for(int i=0;i<count;i++)
                {
                    Vec3 v=union.Vertices[start+i].Position;Vec3 w=union.Vertices[start+(i+1)%count].Position;
                    List<Point> edge=SplitEdge(new Point(v.X,v.Y),new Point(w.X,w.Y),allPoints);
                    for(int n=0;n<edge.Count-1;n++) path.Add(edge[n]);
                }
                result.Boundary.Add(path);
                for(int i=0;i<path.Count;i++)
                {
                    Point a=path[i],b=path[(i+1)%path.Count];
                    int owner=EdgeOwner(sources,a,b);List<Triangle> mesh=result.Parts[owner];
                    mesh.Add(new Triangle(new Vertex(a,-1),new Vertex(b,-1),new Vertex(b,1)));
                    mesh.Add(new Triangle(new Vertex(a,-1),new Vertex(b,1),new Vertex(a,1)));
                    result.BoundaryEdges++;
                }
            }
            var tess=new Tess();var boundaryPoints=new List<Point>();
            foreach(List<Point> path in result.Boundary){AddContour(tess,path);boundaryPoints.AddRange(path);}
            tess.Tessellate(WindingRule.NonZero,ElementType.Polygons,3,normal:new Vec3{Z=1});
            for(int i=0;i<tess.ElementCount;i++)
            {
                int ai=tess.Elements[i*3],bi=tess.Elements[i*3+1],ci=tess.Elements[i*3+2];
                if(ai==Tess.Undef || bi==Tess.Undef || ci==Tess.Undef) continue;
                Vec3 av=tess.Vertices[ai].Position,bv=tess.Vertices[bi].Position,cv=tess.Vertices[ci].Position;
                Point a=new Point(av.X,av.Y),b=new Point(bv.X,bv.Y),c=new Point(cv.X,cv.Y);
                var perimeter=new List<Point>();Point[] corners={a,b,c};
                for(int edge=0;edge<3;edge++)
                {
                    List<Point> split=SplitEdge(corners[edge],corners[(edge+1)%3],boundaryPoints);
                    for(int j=0;j<split.Count-1;j++) perimeter.Add(split[j]);
                }
                if(perimeter.Count==3) Cap(result,sources,a,b,c);
                else
                {
                    Point centre=new Point((a.X+b.X+c.X)/3,(a.Y+b.Y+c.Y)/3);
                    for(int j=0;j<perimeter.Count;j++) Cap(result,sources,centre,perimeter[j],perimeter[(j+1)%perimeter.Count]);
                }
            }
            double boundaryArea=0;foreach(List<Point> path in result.Boundary) boundaryArea+=SignedArea(path);
            if(boundaryArea<=0 || Math.Abs(result.Area-boundaryArea)>Math.Max(.00001,boundaryArea*.000001))
                throw new InvalidOperationException("Terrain cap area does not match the joined boundary");
            return result;
        }

        // Clip triangles rather than clamping individual vertices, which can manufacture
        // walls or fold a large terrain polygon when the native origin is recentered.
        internal static List<int[]> Encode(List<Triangle> triangles, double originX, double originY, double depth)
        {
            const double k=.0105;const int limit=7999;
            var output=new List<int[]>();
            foreach(Triangle t in triangles)
            {
                var p=new List<Vertex>{t.A,t.B,t.C};
                p=Clip(p,0,originX-limit*k,true);p=Clip(p,0,originX+limit*k,false);
                p=Clip(p,1,originY-limit*k,true);p=Clip(p,1,originY+limit*k,false);
                for(int i=1;i+1<p.Count;i++)
                {
                    Vertex[] v={p[0],p[i],p[i+1]};var row=new int[9];
                    for(int j=0;j<3;j++)
                    {row[j*3]=(int)Math.Round((v[j].X-originX)/k);row[j*3+1]=(int)Math.Round((v[j].Y-originY)/k);row[j*3+2]=(int)Math.Round(v[j].Z*depth);}
                    long ax=row[3]-(long)row[0],ay=row[4]-(long)row[1],az=row[5]-(long)row[2];
                    long bx=row[6]-(long)row[0],by=row[7]-(long)row[1],bz=row[8]-(long)row[2];
                    if(ay*bz-az*by!=0 || az*bx-ax*bz!=0 || ax*by-ay*bx!=0) output.Add(row);
                }
            }
            return output;
        }
        private static List<Vertex> Clip(List<Vertex> input,int axis,double plane,bool greater)
        {
            var output=new List<Vertex>();if(input.Count==0)return output;
            Vertex a=input[input.Count-1];double da=(axis==0?a.X:a.Y)-plane;
            foreach(Vertex b in input)
            {
                double db=(axis==0?b.X:b.Y)-plane;bool ia=greater?da>=0:da<=0,ib=greater?db>=0:db<=0;
                if(ia!=ib)
                {
                    double t=da/(da-db);
                    output.Add(new Vertex(new Point(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t),a.Z+(b.Z-a.Z)*t));
                }
                if(ib)output.Add(b);a=b;da=db;
            }
            return output;
        }
    }
}
