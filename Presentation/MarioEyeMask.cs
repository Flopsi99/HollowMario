using UnityEngine;

namespace HollowMario
{
    internal static class MarioEyeMask
    {
        // Find the opaque eye components that contain the original white sclera.
        // This includes the pupil inside each oval, but excludes the disconnected
        // eyebrows and transparent face rectangle. Closed eyes naturally blink off.
        internal static byte[] Build(Color32[] atlas)
        {
            const int width=704,tile=64;
            var mask=new byte[width*tile];
            if(atlas==null||atlas.Length!=mask.Length)return mask;
            var visited=new bool[tile*tile];var queue=new int[tile*tile];
            for(int slot=5;slot<=8;slot++)
            {
                System.Array.Clear(visited,0,visited.Length);
                for(int seed=0;seed<visited.Length;seed++)
                {
                    if(visited[seed])continue;
                    visited[seed]=true;
                    if(atlas[(seed/tile)*width+slot*tile+seed%tile].a==0)continue;
                    int head=0,count=1;queue[0]=seed;bool white=false;
                    while(head<count)
                    {
                        int current=queue[head++],x=current%tile,y=current/tile;
                        Color32 pixel=atlas[y*width+slot*tile+x];
                        white|=pixel.r>=192&&pixel.g>=192&&pixel.b>=192;
                        if(x>0)Visit(current-1,slot,atlas,visited,queue,ref count);
                        if(x<tile-1)Visit(current+1,slot,atlas,visited,queue,ref count);
                        if(y>0)Visit(current-tile,slot,atlas,visited,queue,ref count);
                        if(y<tile-1)Visit(current+tile,slot,atlas,visited,queue,ref count);
                    }
                    if(white)for(int i=0;i<count;i++)
                    {int index=(queue[i]/tile)*width+slot*tile+queue[i]%tile;mask[index]=atlas[index].a;}
                }
            }
            return mask;
        }
        private static void Visit(int cell,int slot,Color32[] atlas,bool[] visited,int[] queue,ref int count)
        {
            if(visited[cell])return;
            visited[cell]=true;
            if(atlas[(cell/64)*704+slot*64+cell%64].a!=0)queue[count++]=cell;
        }
    }
}
