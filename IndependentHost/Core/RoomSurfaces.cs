using System;
using System.Collections.Generic;

namespace HollowMario.Host
{
    public struct WorldPoint
    {
        public double X,Y;
        public WorldPoint(double x,double y){X=x;Y=y;}
    }
    public sealed class RoomOutline
    {
        public int Owner,Group;
        public readonly List<WorldPoint> Points=new List<WorldPoint>();
    }
    public static class RoomSurfaces
    {
        // Reuse our actual joined-contour/hole converter unchanged. Only this
        // final surface encoding adapter is new; no original WorldCollision.
        public static Surface[] Build(IEnumerable<RoomOutline> outlines,double originX=0,double originY=0,double laneDepth=100)
        {
            if(outlines==null)throw new ArgumentNullException(nameof(outlines));
            var input=new List<ConnectedTerrainMesh.Outline>();
            foreach(var outline in outlines)
            {
                var points=new List<ConnectedTerrainMesh.Point>();
                foreach(var point in outline.Points)points.Add(new ConnectedTerrainMesh.Point(point.X,point.Y));
                input.Add(new ConnectedTerrainMesh.Outline{Owner=outline.Owner,Group=outline.Group,Points=points});
            }
            ConnectedTerrainMesh.ClassifyHoles(input);
            var mesh=ConnectedTerrainMesh.Build(input);var result=new List<Surface>();
            foreach(var part in mesh.Parts.Values)
                foreach(var row in ConnectedTerrainMesh.Encode(part,originX,originY,laneDepth))
                    result.Add(new Surface{Terrain=1,Ax=row[0],Ay=row[1],Az=row[2],Bx=row[3],By=row[4],Bz=row[5],Cx=row[6],Cy=row[7],Cz=row[8]});
            return result.ToArray();
        }
    }
}
