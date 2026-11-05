using UnityEngine;

namespace HollowMario
{
    // Enabled only while the selector or a short failure message is visible. No Update loop.
    internal sealed class CapHud:MonoBehaviour
    {
        private static CapHud instance;
        private static string message;
        private static float until;
        private GUIStyle notice;
        private void Awake()
        {instance=this;CapSystem.Changed+=Refresh;CapControls.Changed+=Refresh;enabled=false;}
        private void OnDestroy(){CapSystem.Changed-=Refresh;CapControls.Changed-=Refresh;if(instance==this)instance=null;}
        private static void Refresh(){if(instance!=null)instance.enabled=CapControls.Open||Time.unscaledTime<until;}
        internal static void Message(string text){message=text;until=Time.unscaledTime+2.5f;Refresh();}
        private void OnGUI()
        {
            float scale=Mathf.Clamp(Screen.height/1080f,.65f,1.5f);
            Matrix4x4 old=GUI.matrix;Color color=GUI.color;
            GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new Vector3(scale,scale,1));
            float width=Screen.width/scale,height=Screen.height/scale;
            try
            {
                if(CapControls.Open)
                {
                    Box(new Rect(0,0,width,height),new Color(0,0,0,.62f));
                    float x=width*.5f,y=height*.5f;
                    Icon(x,y-138,CapKind.Vanish);
                    Icon(x-138,y,CapKind.Wing);
                    Icon(x+138,y,CapKind.Metal);
                    Icon(x,y+138,CapKind.None);
                }
                else if(Time.unscaledTime<until)
                {
                    if(notice==null)notice=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontSize=18,wordWrap=true};
                    Rect toast=new Rect(width/2-250,height-100,500,50);Box(toast,new Color(0,0,0,.85f));
                    GUI.color=Color.white;GUI.Label(toast,message,notice);
                }
            }
            finally{GUI.color=color;GUI.matrix=old;Refresh();}
        }
        private static void Icon(float x,float y,CapKind cap)
        {
            CapDefinition definition=CapDefinition.For(cap);
            bool available=cap==CapKind.None||(definition.Implemented&&CapSystem.Unlocked(cap));
            Texture2D icon=available?CapIcons.Texture(cap):CapIcons.LockedTexture;
            if(icon==null)return;
            const float size=124f;
            GUI.color=available?Color.white:new Color(.65f,.65f,.65f,1);
            GUI.DrawTexture(new Rect(x-size*.5f,y-size*.5f,size,size),icon,ScaleMode.ScaleToFit,true);
        }
        private static void Box(Rect rect,Color color){GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);}
    }
}
