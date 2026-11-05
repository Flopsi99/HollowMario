using System;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // A Unity presentation of SM64's paged dialogue format. Uses only the
    // engine's pinned, validated ROM; no font file or extracted asset is shipped.
    internal sealed class LakituDialogue:MonoBehaviour
    {
        private enum Phase{Turning,Opening,Reading,Scrolling,Closing,Release}
        private static LakituDialogue instance;
        internal static bool Active {get;private set;}
        private Sm64DialogueText text;
        private Texture2D atlas;
        private object hero,controller,game;
        private bool queued,ownsControl,waitForRelease;
        private int page;
        private float elapsed,scroll;
        private Phase phase;
        private MethodInfo playSound;
        internal static bool OwnsHero(object value)=>Active&&instance!=null&&instance.ownsControl&&ReferenceEquals(instance.hero,value);
        private void Awake(){instance=this;enabled=false;}
        internal static void Queue()
        {
            if(instance==null)return;
            instance.queued=true;instance.enabled=true;
            // Acquire the scripted handoff in the same frame, before any Knight
            // movement/input can leak between the landing and conversation.
            try{instance.TryOpen();}
            catch(Exception error){instance.Close();Plugin.Instance?.Log.LogWarning("Lakitu's introduction could not start: "+error.GetBaseException().Message);}
        }
        internal static void Cancel(){if(instance!=null)instance.Close();}
        private static bool Tutorial=>UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="Tutorial_01";
        private static object Actions=>CapAccess.Read(CapAccess.Read(AccessTools.TypeByName("InputHandler"),"Instance"),"inputActions");
        private bool Paused=>CapAccess.Flag(game,"isPaused");
        private static bool Pressed(object actions,string member)=>CapAccess.Flag(CapAccess.Read(actions,member),"WasPressed");
        private static bool Held(object actions,string member)=>CapAccess.Flag(CapAccess.Read(actions,member),"IsPressed");
        private void Update()
        {
            try
            {
                if(!Tutorial){Close();return;}
                if(queued){TryOpen();return;}
                if(!Active||Paused)return;
                if(CapAccess.Flag(CapAccess.Read(hero,"cState"),"dead")||CapAccess.Flag(CapAccess.Read(hero,"cState"),"transitioning"))
                {Close();return;}
                object actions=Actions;
                bool held=Held(actions,"jump")||Held(actions,"attack");
                if(waitForRelease&&!held)waitForRelease=false;
                elapsed+=Time.unscaledDeltaTime;
                switch(phase)
                {
                    case Phase.Turning:
                        OpeningMario.TurnForConversation(Mathf.Clamp01(elapsed/.5f));
                        if(elapsed>=.5f){phase=Phase.Opening;elapsed=0;Sound(0x70040081u);}
                        break;
                    case Phase.Opening:if(elapsed>=.4f){phase=Phase.Reading;elapsed=0;}break;
                    case Phase.Reading:
                        if(!waitForRelease&&(Pressed(actions,"jump")||Pressed(actions,"attack")))
                        {
                            bool next=page+1<text.Pages.Length;
                            phase=next?Phase.Scrolling:Phase.Closing;elapsed=0;waitForRelease=true;
                            Sound(next?0x70130081u:0x70050081u);
                        }
                        break;
                    case Phase.Scrolling:
                        scroll=Mathf.Min(96f,elapsed*360f); // Native six-line page: eight 30 Hz steps.
                        if(scroll>=96f){page++;scroll=0;phase=Phase.Reading;elapsed=0;}
                        break;
                    case Phase.Closing:if(elapsed>=.3f)phase=Phase.Release;break;
                    case Phase.Release:if(!held)Close(true);break;
                }
            }
            catch(Exception error){Close();Plugin.Instance?.Log.LogWarning("Lakitu's introduction could not display: "+error.GetBaseException().Message);}
        }
        private void TryOpen()
        {
            var follow=Plugin.Instance?.knightFollow;
            controller=follow?.Controller();
            if(controller!=null&&!CapAccess.Flag(controller,"Wanted")){Close();return;}
            if(controller==null||!OpeningMario.ReadyForDialogue)return;
            game=CapAccess.Read(AccessTools.TypeByName("GameManager"),"UnsafeInstance");
            if(Paused)return;
            if(text==null)
            {
                Type sm64=controller.GetType().Assembly.GetType("MarioHollow.Sm64",true);
                var pin=(GCHandle)AccessTools.Field(sm64,"romHandle").GetValue(null);
                if(!pin.IsAllocated||!(pin.Target is byte[] rom))throw new InvalidOperationException("Mario's ROM is not ready.");
                text=new Sm64DialogueText(rom,Sm64DialogueText.Introduction);
            }
            if(atlas==null)BuildAtlas();
            hero=OpeningMario.Hero;queued=false;page=0;elapsed=scroll=0;
            phase=Phase.Turning;waitForRelease=true;
            playSound=AccessTools.Method(controller.GetType().Assembly.GetType("MarioHollow.Native",true),"sm64_play_sound_global");
            CapControls.Close();
            Active=true;
            ownsControl=true;AccessTools.Method(hero.GetType(),"RelinquishControl",Type.EmptyTypes).Invoke(hero,null);
            OpeningMario.BeginConversation();
        }
        private void Sound(uint sound)
        {
            try{playSound?.Invoke(null,new object[]{sound});}
            catch(Exception error){Plugin.Instance?.Log.LogWarning("Lakitu textbox sound unavailable: "+error.GetBaseException().Message);}
        }
        private void BuildAtlas()
        {
            var pixels=new Color32[128*256];
            foreach(var glyph in text.Glyphs.Values)
                for(int y=0;y<16;y++)for(int x=0;x<8;x++)
                {
                    int src=(y*8+x)*4,dst=(255-glyph.Id/16*16-y)*128+glyph.Id%16*8+x;
                    pixels[dst]=new Color32(glyph.Rgba[src],glyph.Rgba[src+1],glyph.Rgba[src+2],glyph.Rgba[src+3]);
                }
            atlas=new Texture2D(128,256,TextureFormat.RGBA32,false)
                {name="Player ROM dialogue font",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            atlas.SetPixels32(pixels);atlas.Apply(false,true);
        }
        private void OnGUI()
        {
            if(!Active||atlas==null||Paused||phase==Phase.Release||phase==Phase.Turning)return;
            Matrix4x4 matrix=GUI.matrix;Color color=GUI.color;int depth=GUI.depth;
            try
            {
                float fit=Mathf.Min(Screen.width/320f,Screen.height/240f),scale=fit>=1?Mathf.Floor(fit):Mathf.Max(.1f,fit);
                GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-320*scale)*.5f,(Screen.height-240*scale)*.5f,0),Quaternion.identity,new Vector3(scale,scale,1));
                GUI.depth=-100;
                float fade=phase==Phase.Opening?Mathf.Clamp01(elapsed/.4f):phase==Phase.Closing?1-Mathf.Clamp01(elapsed/.3f):1;
                GUI.color=new Color(0,0,0,fade*150f/255f);GUI.DrawTexture(new Rect(24,28,144,112),Texture2D.whiteTexture);
                GUI.color=new Color(1,1,1,fade);
                GUI.BeginGroup(new Rect(32,36,Sm64DialogueText.TextWidth,96));
                try{DrawPage(page,-scroll);if(phase==Phase.Scrolling)DrawPage(page+1,96-scroll);}
                finally{GUI.EndGroup();}
                if(phase==Phase.Reading&&page+1<text.Pages.Length&&((int)(Time.unscaledTime*30)&8)==0)
                    for(int row=0;row<4;row++)GUI.DrawTexture(new Rect(152+row,134+row,7-row*2,1),Texture2D.whiteTexture);
            }
            finally{GUI.matrix=matrix;GUI.color=color;GUI.depth=depth;}
        }
        private void DrawPage(int index,float top)
        {
            if(index>=text.Pages.Length)return;
            for(int line=0;line<text.Pages[index].Length;line++)
            {
                float x=0;
                foreach(char c in text.Pages[index][line])
                {
                    var glyph=text.Glyphs[c];
                    if(c!=' ')GUI.DrawTextureWithTexCoords(new Rect(x,top+line*16,8,16),atlas,
                        new Rect(glyph.Id%16*8/128f,(256-(glyph.Id/16+1)*16)/256f,8/128f,16/256f),true);
                    x+=glyph.Advance;
                }
            }
        }
        private void Close(bool resumeGameplay=false)
        {
            queued=false;Active=false;enabled=false;
            bool handedBack=false;
            try
            {
                if(ownsControl&&hero is Component component&&component!=null&&Tutorial
                    &&ReferenceEquals(CapAccess.Read(AccessTools.TypeByName("HeroController"),"instance"),hero)
                    &&!CapAccess.Flag(CapAccess.Read(hero,"cState"),"dead")&&!CapAccess.Flag(CapAccess.Read(hero,"cState"),"transitioning"))
                {
                    AccessTools.Method(hero.GetType(),"RegainControl",Type.EmptyTypes).Invoke(hero,null);
                    handedBack=resumeGameplay;
                }
            }
            finally
            {
                ownsControl=false;hero=controller=game=null;
                if(handedBack)OpeningMario.ReleaseToGameplay();else OpeningMario.Stop();
            }
        }
        private void OnDestroy(){Close();if(atlas!=null)Destroy(atlas);if(instance==this)instance=null;}
    }
}
