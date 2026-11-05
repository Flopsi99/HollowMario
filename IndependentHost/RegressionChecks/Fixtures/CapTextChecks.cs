// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Linq;
class CapTextChecks
{
    class Selection { public static Selection Instance=new Selection(); public bool Wanted=true; }
    static object languageCode;
    static object CurrentLanguage()=>languageCode;
    static int count;
    static void Check(bool ok,string message){count++;if(!ok)throw new Exception(message);}
    static int Main(string[] args)
    {
        try{
            string root=Path.GetFullPath(args[0]);
            string game=TestEnvironment.Managed;

            AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
                string name=new AssemblyName(e.Name).Name+".dll";
                string support=TestEnvironment.Package;
                foreach(string folder in new[]{Path.GetDirectoryName(Path.GetFullPath(args[1])),support,game}){
                    string path=Path.Combine(folder,name);if(File.Exists(path))return Assembly.LoadFrom(path);
                }
                return null;
            };
            var asm=Assembly.LoadFrom(Path.GetFullPath(args[1]));
            var type=asm.GetType("HollowMario.CapDescriptions",true);
            var find=type.GetMethod("Find",BindingFlags.NonPublic|BindingFlags.Static);
            string[] suffix={"CYCLONE","UPPER","DASH"};
            string[] titles={"Wing Cap","Vanish Cap","Metal Cap"};
            string[] exact={
                "Use [R3] and press left.\nThe signature cap of Nailmaster Mato.\nTake flight and reach new heights.",
                "Use [R3] and press up.\nThe signature cap of Nailmaster Oro.\nSneak in the shadows, unseen by all.",
                "Use [R3] and press right.\nThe signature cap of Nailmaster Sheo.\nBreak your foes and remain stainless."
            };
            for(int i=0;i<3;i++){
                object[] a={"INV_DESC_ART_"+suffix[i],"UI","EN",true,"Update Text","Inv","R3"};
                Check((string)find.Invoke(null,a)==exact[i],"Exact creator description: "+titles[i]);
                a[0]="INV_NAME_ART_"+suffix[i];Check((string)find.Invoke(null,a)==titles[i],"Native inventory key: "+titles[i]);
                a[0]="INV_DESC_ART_"+suffix[i];
                a[3]=false;Check(find.Invoke(null,a)==null,"Knight text retained");a[3]=true;
                a[2]="DE";Check(find.Invoke(null,a)==null,"Other languages retained");a[2]="EN";
                a[4]="UI Inventory";Check(find.Invoke(null,a)==null,"Other FSMs retained");a[4]="Update Text";
                a[5]="Charms";Check(find.Invoke(null,a)==null,"Charms retained");a[5]="Inv";
                a[1]="Prompts";Check(find.Invoke(null,a)==null,"Unrelated sheets retained");a[1]="UI";
                a[6]="Left Shift";Check(((string)find.Invoke(null,a)).Contains("Use [Left Shift] and press "),"Binding is substituted");
            }
            var registry=asm.GetType("HollowMario.PickupReplacements",true);
            var lookup=registry.GetMethod("Find",BindingFlags.NonPublic|BindingFlags.Static);
            string[] states={"Set NA Cyclone","Set NA D Slash","Set NA G Slash"};
            string[] directions={"left","up","right"};
            string[] bodies={"Hold UP to gain height, or DOWN to dive toward the ground.","While vanished, your attacks will pass harmlessly through enemies as well.","Though beware of its high SOUL drain."};
            for(int i=0;i<3;i++){
                object item=lookup.Invoke(null,new object[]{states[i]});
                var f=BindingFlags.NonPublic|BindingFlags.Instance;
                Check((string)item.GetType().GetField("Title",f).GetValue(item)==titles[i],"Real reward popup matches cap");
                Check((string)item.GetType().GetField("Template",f).GetValue(item)==bodies[i]+"\n\nUse {CAP_SELECTION} and press "+directions[i]+".","Only requested unlock line appended");
                Check(((string[])item.GetType().GetField("Buttons",f).GetValue(item)).SequenceEqual(new[]{"CAP_SELECTION"}),"One real binding slot");
            }
            var native=Assembly.LoadFrom(Path.Combine(game,"Assembly-CSharp.dll"));
            var arm=type.GetMethod("Arm",BindingFlags.NonPublic|BindingFlags.Static);
            var parameters=arm.GetParameters();
            object harmony=Activator.CreateInstance(parameters[0].ParameterType,new object[]{"cap-ui-checks"});
            object logger=Activator.CreateInstance(parameters[1].ParameterType,new object[]{"cap-ui-checks"});
            arm.Invoke(null,new[]{harmony,logger});
            foreach(string name in new[]{"GetLanguageString","SetTextMeshProText","ActivateGameObject"}){
                var method=native.GetType("HutongGames.PlayMaker.Actions."+name,true).GetMethod("OnEnter");
                object info=parameters[0].ParameterType.GetMethod("GetPatchInfo").Invoke(null,new object[]{method});
                Check(info!=null,"Native UI hook was installed: "+name);
                var patches=(System.Collections.IEnumerable)info.GetType().GetField("Postfixes").GetValue(info);
                bool own=false;foreach(object patch in patches)own|=(string)patch.GetType().GetField("owner").GetValue(patch)=="cap-ui-checks";
                Check(own,"Native postfix signature accepted: "+name);
            }
            var unlock=asm.GetType("HollowMario.UnlockText",true);
            var selected=asm.GetType("HollowMario.PickupArtwork",true);
            var statics=BindingFlags.NonPublic|BindingFlags.Static;
            selected.GetField("instance",statics).SetValue(null,typeof(Selection).GetField("Instance"));
            selected.GetField("wanted",statics).SetValue(null,typeof(Selection).GetField("Wanted"));
            var language=native.GetType("Language.Language",true);
            // Language's static initializer needs Unity Resources. Supply the
            // real enum through a test method without starting Unity's engine.
            var codeType=language.GetMethod("CurrentLanguage").ReturnType;
            unlock.GetField("currentLanguage",statics).SetValue(null,typeof(CapTextChecks).GetMethod("CurrentLanguage",statics));
            var after=unlock.GetMethod("After",statics);
            var entries=(System.Collections.Generic.Dictionary<string,string>)unlock.GetField("Entries",statics).GetValue(null);
            foreach(var entry in entries){
                int slash=entry.Key.IndexOf('/');
                foreach(string lang in new[]{"EN","DE"})foreach(bool mario in new[]{true,false}){
                    languageCode=Enum.Parse(codeType,lang);Selection.Instance.Wanted=mario;
                    object[] call={entry.Key.Substring(slash+1),entry.Key.Substring(0,slash),"native wording"};
                    after.Invoke(null,call);
                    Check((string)call[2]==(lang=="EN"&&mario?entry.Value:"native wording"),"Unlock language code and mode: "+entry.Key+"/"+lang+"/"+mario);
                }
            }
            object dash=lookup.Invoke(null,new object[]{"Set Dash"});
            var fields=BindingFlags.NonPublic|BindingFlags.Instance;
            Check((string)dash.GetType().GetField("Headline",fields).GetValue(dash)=="Mario regains his Long Jump.","Exact Long Jump headline");
            Check((string)dash.GetType().GetField("Template",fields).GetValue(dash)=="On the ground, press {DASH} and {JUMP}.\nWhile airborne, press {DASH} and {ATTACK}.\nAllows Mario to turn around quickly when used in the air.","Exact creator Long Jump instructions");
            Check(((string[])dash.GetType().GetField("Buttons",fields).GetValue(dash)).SequenceEqual(new[]{"DASH","JUMP","DASH","ATTACK"}),"All four binding-aware Long Jump buttons");
            parameters[0].ParameterType.GetMethod("UnpatchSelf").Invoke(harmony,null);
            Console.WriteLine(count+" cap text/scope/reward checks passed for "+Path.GetDirectoryName(args[1]));return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
