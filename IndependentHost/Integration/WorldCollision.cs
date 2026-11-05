using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HollowMario.Host;
using UnityEngine;

namespace MarioHollow
{
    public static class MarioMath
    {
        public const float Scale=.0105f;
        public static Vector2 Origin;
        public static float LaneZ;
        public static Vector3 World(float x,float y,float z=0)=>new Vector3(Origin.x+x*Scale,Origin.y+y*Scale,LaneZ-z*Scale);
        public static Vector3 Native(Vector3 point)=>new Vector3((point.x-Origin.x)/Scale,(point.y-Origin.y)/Scale,(LaneZ-point.z)/Scale);
    }
    public static class Geometry
    {
        public static void Prism(Vector2[] points,float depth,List<SM64Surface> into)
        {
            if(points.Length<2)return;
            bool closed=points.Length>2&&(points[0]-points[points.Length-1]).sqrMagnitude<.000001f;
            if(closed)
            {
                Prisms(new[]{points},depth,into);
                return;
            }
            for(int i=1;i<points.Length;i++)
            {
                Vector3 a=MarioMath.Native(points[i-1]),b=MarioMath.Native(points[i]);
                // Open edges stay open. Both sides collide, with no invented
                // solid volume or bottom across a one-way platform's opening.
                var face=new SM64Surface{type=21,terrain=1,v0x=(int)a.x,v0y=(int)a.y,v0z=-(int)depth,
                    v1x=(int)a.x,v1y=(int)a.y,v1z=(int)depth,v2x=(int)b.x,v2y=(int)b.y,v2z=(int)depth};
                into.Add(face);Swap(ref face);into.Add(face);
                face=new SM64Surface{type=21,terrain=1,v0x=(int)a.x,v0y=(int)a.y,v0z=-(int)depth,
                    v1x=(int)b.x,v1y=(int)b.y,v1z=(int)depth,v2x=(int)b.x,v2y=(int)b.y,v2z=-(int)depth};
                into.Add(face);Swap(ref face);into.Add(face);
            }
        }
        public static void Prisms(IEnumerable<Vector2[]> paths,float depth,List<SM64Surface> into)
        {
            // All closed paths of ONE authored collider belong to the same
            // contour group. Classify cavities/islands together, not as separate
            // solid boxes. Independent colliders still call this separately.
            var outlines=new List<RoomOutline>();
            foreach(var points in paths)
            {
                if(points.Length<3)continue;
                int count=points.Length;
                if((points[0]-points[count-1]).sqrMagnitude<.000001f)count--;
                var outline=new RoomOutline{Owner=1,Group=1};
                for(int i=0;i<count;i++)outline.Points.Add(new WorldPoint(points[i].x,points[i].y));
                outlines.Add(outline);
            }
            foreach(var row in RoomSurfaces.Build(outlines,MarioMath.Origin.x,MarioMath.Origin.y,depth))
                into.Add(new SM64Surface{type=21,terrain=1,v0x=row.Ax,v0y=row.Ay,v0z=row.Az,v1x=row.Bx,v1y=row.By,v1z=row.Bz,v2x=row.Cx,v2y=row.Cy,v2z=row.Cz});
        }
        private static void Swap(ref SM64Surface f)
        {int x=f.v1x,y=f.v1y,z=f.v1z;f.v1x=f.v2x;f.v1y=f.v2y;f.v1z=f.v2z;f.v2x=x;f.v2y=y;f.v2z=z;}
        internal static List<Vector2[]> Paths(Collider2D collider)
        {
            var paths=new List<Vector2[]>();
            if(collider.usedByEffector&&collider.GetComponent<PlatformEffector2D>()!=null)
            {Bounds b=collider.bounds;paths.Add(new[]{new Vector2(b.min.x,b.max.y),new Vector2(b.max.x,b.max.y)});return paths;}
            if(collider is EdgeCollider2D edge)paths.Add(World(collider,edge.points,false));
            else if(collider is PolygonCollider2D polygon)
                for(int i=0;i<polygon.pathCount;i++)paths.Add(World(collider,polygon.GetPath(i),true));
            else if(collider is CompositeCollider2D composite)
                for(int i=0;i<composite.pathCount;i++){var path=new Vector2[composite.GetPathPointCount(i)];composite.GetPath(i,path);paths.Add(World(collider,path,composite.geometryType==CompositeCollider2D.GeometryType.Polygons));}
            else if(collider is BoxCollider2D box)
            {Vector2 s=box.size*.5f;paths.Add(World(collider,new[]{new Vector2(-s.x,-s.y),new Vector2(s.x,-s.y),s,new Vector2(-s.x,s.y)},true));}
            else if(collider is CircleCollider2D circle)
            {var p=new Vector2[24];for(int i=0;i<p.Length;i++){float a=i*Mathf.PI*2/p.Length;p[i]=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*circle.radius;}paths.Add(World(collider,p,true));}
            else if(collider is CapsuleCollider2D capsule)
            {
                bool vertical=capsule.direction==CapsuleDirection2D.Vertical;
                float radius=(vertical?capsule.size.x:capsule.size.y)*.5f;
                float straight=Mathf.Max(0,(vertical?capsule.size.y:capsule.size.x)*.5f-radius);
                var p=new Vector2[24];for(int i=0;i<p.Length;i++)
                {
                    float a=i*Mathf.PI*2/p.Length;float x=Mathf.Cos(a)*radius,y=Mathf.Sin(a)*radius;
                    if(vertical)y+=Mathf.Sign(y)*straight;else x+=Mathf.Sign(x)*straight;p[i]=new Vector2(x,y);
                }
                paths.Add(World(collider,p,true));
            }
            else throw new NotSupportedException("Independent collision shape: "+collider.GetType().Name);
            return paths;
        }
        private static Vector2[] World(Collider2D collider,Vector2[] points,bool close)
        {
            var result=new Vector2[points.Length+(close?1:0)];for(int i=0;i<points.Length;i++)result[i]=collider.transform.TransformPoint(points[i]+collider.offset);
            if(close&&points.Length>0)result[result.Length-1]=result[0];return result;
        }
    }
    public sealed class WorldCollision:IDisposable
    {
        public sealed class Entry
        {
            public uint Id;
            public Vector3 SeenAt;
            internal Collider2D Source;
            internal Matrix4x4 Matrix;
            internal Vector3 Position;
            internal Vector3 LastPosition;
            internal bool NeedsVelocityReset;
        }
        private readonly Dictionary<Collider2D,Entry> tracked=new Dictionary<Collider2D,Entry>();
        public static int Objects;
        private Collider2D[] overlap=new Collider2D[512];
        private readonly HashSet<Collider2D> seen=new HashSet<Collider2D>();
        private readonly List<Collider2D> remove=new List<Collider2D>();
        private float nextScan;
        private int failures;
        private uint? safetyFloor;
        internal void EnsureSafetyFloor(Vector3 at)
        {
            if(safetyFloor.HasValue)return;
            var floor=new[]{new SM64Surface{type=21,terrain=1,v0x=-2000,v0z=-2000,v1x=-2000,v1z=2000,v2x=2000,v2z=2000},
                new SM64Surface{type=21,terrain=1,v0x=-2000,v0z=-2000,v1x=2000,v1z=2000,v2x=2000,v2z=-2000}};
            var pin=GCHandle.Alloc(floor,GCHandleType.Pinned);
            try
            {
                var obj=new SM64SurfaceObject{transform=new SM64ObjectTransform{px=at.x,py=at.y-7000},surfaceCount=2,surfaces=pin.AddrOfPinnedObject()};
                uint id=Native.sm64_surface_object_create(ref obj);if(id==uint.MaxValue)throw new InvalidOperationException("Cannot prepare the native spawn safety plane.");safetyFloor=id;
            }
            finally{pin.Free();}
        }
        public Entry Create(Collider2D c)
        {
            if(c==null||!c.enabled||c.isTrigger||c.usedByComposite||!c.gameObject.activeInHierarchy)return null;
            var faces=new List<SM64Surface>();var paths=Geometry.Paths(c);
            if(c is PolygonCollider2D||(c is CompositeCollider2D composite&&composite.geometryType==CompositeCollider2D.GeometryType.Polygons))
                Geometry.Prisms(paths,160,faces);
            else foreach(Vector2[] path in paths)Geometry.Prism(path,160,faces);
            if(faces.Count==0)return null;
            var array=faces.ToArray();var pin=GCHandle.Alloc(array,GCHandleType.Pinned);
            try
            {
                var value=new SM64SurfaceObject{surfaceCount=(uint)array.Length,surfaces=pin.AddrOfPinnedObject()};
                uint id=Native.sm64_surface_object_create(ref value);if(id==uint.MaxValue)throw new InvalidOperationException("Native collision allocation failed.");
                return new Entry{Id=id,SeenAt=c.transform.position,Source=c,Matrix=c.transform.localToWorldMatrix,Position=c.transform.position,LastPosition=c.transform.position};
            }
            finally{pin.Free();}
        }
        internal static void UpdatePosition(Entry entry,Vector3 position)
        {
            Vector3 shift=(position-entry.Position)/MarioMath.Scale;
            var transform=new SM64ObjectTransform{px=shift.x,py=shift.y,pz=0};
            Native.sm64_surface_object_move(entry.Id,ref transform);
            entry.NeedsVelocityReset=position!=entry.LastPosition;
            entry.LastPosition=position;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]public void Tick(Vector2 centre,bool force)
        {
            remove.Clear();foreach(var pair in tracked)
            {
                Entry entry=pair.Value;Collider2D c=entry.Source;
                if(c==null||!c.enabled||!c.gameObject.activeInHierarchy||c.isTrigger||c.usedByComposite){remove.Add(pair.Key);continue;}
                Matrix4x4 matrix=c.transform.localToWorldMatrix;
                if(matrix==entry.Matrix){if(entry.NeedsVelocityReset)UpdatePosition(entry,c.transform.position);continue;}
                bool rotation=false;for(int row=0;row<3;row++)for(int col=0;col<3;col++)rotation|=matrix[row,col]!=entry.Matrix[row,col];
                if(rotation){remove.Add(pair.Key);force=true;continue;}
                UpdatePosition(entry,c.transform.position);
                // Position is the geometry's creation anchor, not the last tick.
                entry.Matrix=matrix;
            }
            foreach(Collider2D key in remove)Drop(key);
            if(!force&&Time.unscaledTime<nextScan)return;nextScan=Time.unscaledTime+.2f;
            int layers=LayerMask.GetMask("Terrain","Soft Terrain");
            int count;
            while(true)
            {
                count=Physics2D.OverlapBoxNonAlloc(centre,new Vector2(110,110),0,overlap,layers);
                if(count<overlap.Length)break;if(overlap.Length>=16384)throw new InvalidOperationException("Independent room collider capacity exceeded.");Array.Resize(ref overlap,overlap.Length*2);
            }
            seen.Clear();
            for(int i=0;i<count;i++)
            {
                Collider2D c=overlap[i];if(c==null||c.isTrigger||!c.enabled||c.usedByComposite||!c.gameObject.scene.IsValid())continue;
                seen.Add(c);if(tracked.ContainsKey(c))continue;
                try{Entry entry=Create(c);if(entry!=null)tracked.Add(c,entry);}
                catch(Exception error){if(failures++<8)UnityEngine.Debug.LogError("Hollow Mario independent world: "+error.Message);throw;}
            }
            remove.Clear();foreach(Collider2D key in tracked.Keys)if(!seen.Contains(key))remove.Add(key);foreach(Collider2D key in remove)Drop(key);
            Objects=tracked.Count;
        }
        public void Rebuild(Vector2 centre){Dispose();nextScan=0;Tick(centre,true);}
        private void Drop(Collider2D key){Native.sm64_surface_object_delete(tracked[key].Id);tracked.Remove(key);}
        public void Dispose()
        {
            if(Native.Engine!=null)
            {
                foreach(Entry entry in tracked.Values)Native.sm64_surface_object_delete(entry.Id);
                if(safetyFloor.HasValue)Native.sm64_surface_object_delete(safetyFloor.Value);
            }
            safetyFloor=null;tracked.Clear();Objects=0;
        }
    }
}
