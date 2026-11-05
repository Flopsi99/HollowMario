using System;
using System.IO;
using System.Reflection;
using UnityEngine;
class StompAnticipationChecks
{
    static BindingFlags S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static int Main(string[] args){try{
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string dir in new[]{TestEnvironment.Package,TestEnvironment.Managed}){string p=Path.Combine(dir,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
        var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var t=mod.GetType("HollowMario.StompContact",true);
        var predict=t.GetMethod("AnticipationEnd",S);var foot=t.GetMethod("AnticipationFoot",S);
        var test=t.GetMethod("TryStomp",S);var past=t.GetMethod("TryContact",S);var search=t.GetMethod("SearchStompBounds",S);
        var end=new Vector3(4,3,0);var body=new Bounds(end+new Vector3(0,.65f,0),new Vector3(.5f,1.3f,0));
        Func<Vector3,Vector3,Vector3,Bounds,Tuple<bool,float>> contact=(start,now,next,enemy)=>{
            object[] a={body,start,now,next,enemy,0f};bool found=(bool)test.Invoke(null,a);return Tuple.Create(found,(float)a[5]);};
        var region=(Bounds)foot.Invoke(null,new object[]{body,end});
        Check(Math.Abs(region.max.y-end.y)<.00001f&&region.min.y<end.y,"Stomp region starts at feet and extends below, never into the body");
        float lastDistance=0;
        foreach(float speed in new[]{-4f,-20f,-75f,-150f}){
            var next=(Vector3)predict.Invoke(null,new object[]{end,new Vector3(0,speed,0)});
            Check(Math.Abs(end.y-next.y+speed*.0105f)<.00001f,"Fall speed projects one actual native step, not another deltaTime factor");
            Check(end.y-next.y>lastDistance,"Faster falls increase downward anticipation");lastDistance=end.y-next.y;
            float top=next.y+.05f;var enemy=new Bounds(new Vector3(4,top-.4f,0),new Vector3(.6f,.8f,0));
            var result=contact(end+Vector3.up*.05f,end,next,enemy);
            Check(result.Item1,"Enemy below current feet is caught by anticipated step before hurtbox contact");
            var bounds=(Bounds)search.Invoke(null,new object[]{body,end+Vector3.up*.05f,end,next});Check(bounds.Intersects(enemy),"Broad query contains accepted prediction");
            if(speed<=-20f){object[] old={body,end+Vector3.up*.05f,end,enemy,0f};Check(!(bool)past.Invoke(null,old),"Regression reproduces old backward-only miss");Check(result.Item2>=1f,"Future-only hit cannot move Mario into a future position");}
            enemy.center-=Vector3.up*2;Check(!contact(end+Vector3.up*.05f,end,next,enemy).Item1,"No contact beyond predicted reach");
        }
        var below=new Bounds(new Vector3(4,1.8f,0),new Vector3(.6f,.8f,0));
        var crossed=contact(new Vector3(4,4,0),new Vector3(4,1.5f,0),new Vector3(4,1,0),below);
        Check(crossed.Item1&&crossed.Item2<=1f,"Already-crossed fast-fall top contact remains authoritative");
        Check(!contact(end-Vector3.up,end,end+Vector3.up,below).Item1,"Rising does not create a stomp");
        var side=new Bounds(new Vector3(6,3.4f,0),new Vector3(.5f,1.5f,0));Check(!contact(end+Vector3.up*.1f,end,end-Vector3.up,side).Item1,"Side contact remains damage");
        var deep=new Bounds(new Vector3(4,3.6f,0),new Vector3(.6f,.8f,0));Check(!contact(end+Vector3.up*.1f,end,end-Vector3.up,deep).Item1,"Enemy above Mario cannot become a stomp");
        var diagonalNext=new Vector3(6,1,0);var corner=new Bounds(new Vector3(4,1.2f,0),new Vector3(.1f,.1f,0));
        Check(!contact(end+Vector3.up*.1f,end,diagonalNext,corner).Item1,"Empty corner of a diagonal enclosing box is rejected");
        foreach(var bad in new[]{new Vector3(0,0,0),new Vector3(0,10,0),new Vector3(0,float.NaN,0),new Vector3(float.PositiveInfinity,-20,0)})
            Check((Vector3)predict.Invoke(null,new object[]{end,bad})==end,"Grounded/rising/invalid velocities cannot fabricate forward reach");
        Check(Vector3.Lerp(end+Vector3.up,end,Mathf.Clamp01(1.8f))==end,"Anticipatory bounce does not snap Mario down to the enemy");
        Console.WriteLine("PASS downward speed-scaled anticipation; old miss reproduced; actual swept contacts retained; shallow top/side/above/diagonal gates; broad-query coverage; future bounce position and invalid-speed handling. Gameplay timing remains a playtest check.");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
