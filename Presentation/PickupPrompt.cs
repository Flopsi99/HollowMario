using System;
using System.Reflection;
using UnityEngine;

namespace HollowMario
{
    // This component exists only on the pickup panel. Use the game's own keycap/controller
    // objects, including native binding lookup and device switching. Place them from TMP's
    // actual marker quads, rather than estimating where text or a button name will end.
    internal sealed class PickupPrompt : MonoBehaviour
    {
        private const BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        private Component label;
        private GameObject[] buttons;
        private SpriteRenderer[] renderers;
        private Component[] keyLabels;
        private Material[] materials;
        private PickupReplacement entry;
        private string content;
        private string[] boundActions;
        private object handler;
        private Delegate changed;
        private bool dirty;

        private static object Read(object obj,string name)=>PickupMessages.Member(obj,name);
        private static void Set(object obj,string name,object value)=>PickupMessages.Set(obj,name,value);
        private static void EnumValue(object obj,string name,string value)
        {
            PropertyInfo property=obj.GetType().GetProperty(name,Any);
            property.SetValue(obj,Enum.Parse(property.PropertyType,value),null);
        }
        internal static void ArrangeParagraphs(GameObject panel,Type textType)
        {
            Component[] labels={panel.transform.Find("Msg 1")?.GetComponent(textType),
                panel.transform.Find("Msg 2")?.GetComponent(textType)};
            if(labels[0]==null||labels[1]==null)return;
            RectTransform[] rects={labels[0].GetComponent<RectTransform>(),labels[1].GetComponent<RectTransform>()};
            if(rects[0]==null||rects[1]==null)return;
            // Native rows supply font, width, scale and the available region. Their
            // color FSMs only fade; leave those, the heading and artwork untouched.
            float rowHeight=rects[0].rect.height*Mathf.Abs(rects[0].localScale.y);
            float gap=rowHeight*.25f;
            RectTransform press=panel.transform.Find("Press") as RectTransform;
            float top=press!=null?press.anchoredPosition.y+press.rect.height*Mathf.Abs(press.localScale.y)*.5f:
                rects[0].anchoredPosition.y+rowHeight*.5f;
            SpriteRenderer fleur=panel.transform.Find("Fleur")?.GetComponent<SpriteRenderer>();
            if(fleur!=null&&fleur.sprite!=null)
                top=Mathf.Min(top,fleur.transform.localPosition.y+fleur.sprite.bounds.min.y*Mathf.Abs(fleur.transform.localScale.y)-gap);
            float bottom=rects[1].anchoredPosition.y-rects[1].rect.height*Mathf.Abs(rects[1].localScale.y)*.5f;
            SpriteRenderer stop=panel.transform.Find("Stop")?.GetComponent<SpriteRenderer>();
            if(stop!=null&&stop.sprite!=null)
                bottom=Mathf.Max(bottom,stop.transform.localPosition.y+stop.sprite.bounds.max.y*Mathf.Abs(stop.transform.localScale.y)+gap);
            float available=top-bottom-gap;
            if(available<=0f)return;
            float[] sizes=new float[2],widths=new float[2];
            Vector2[] measured=new Vector2[2];
            MethodInfo preferred=textType.GetMethod("GetPreferredValues",Any,null,new[]{typeof(float),typeof(float)},null);
            for(int i=0;i<2;i++)
            {
                Component text=labels[i];sizes[i]=Convert.ToSingle(Read(text,"fontSize"));widths[i]=rects[i].rect.width;
                Set(text,"enableAutoSizing",false);Set(text,"enableWordWrapping",true);Set(text,"richText",true);
                // Normal font leading replaces the old forced 150% line-height tag.
                Set(text,"lineSpacing",Mathf.Max(0f,Convert.ToSingle(Read(text,"lineSpacing"))));
                Set(text,"paragraphSpacing",Mathf.Max(0f,Convert.ToSingle(Read(text,"paragraphSpacing"))));
                Set(text,"margin",Vector4.zero);EnumValue(text,"alignment","Center");EnumValue(text,"OverflowMode","Overflow");
            }
            // Choose the largest shared scale that fits the actual wrapped font metrics.
            // This runs once on acquisition, never as part of a gameplay update.
            float low=0f,high=1f,factor=1f;
            for(int attempt=0;attempt<10;attempt++)
            {
                bool fits=true;float height=0f;
                for(int i=0;i<2;i++)
                {
                    Set(labels[i],"fontSize",sizes[i]*factor);
                    measured[i]=(Vector2)preferred.Invoke(labels[i],new object[]{widths[i],float.PositiveInfinity});
                    height+=measured[i].y*Mathf.Abs(rects[i].localScale.y);
                    fits&=measured[i].x<=widths[i]+.01f;
                }
                fits&=height<=available;
                if(fits){low=factor;if(factor==1f)break;}else high=factor;
                factor=(low+high)*.5f;
            }
            for(int i=0;i<2;i++)
            {
                Set(labels[i],"fontSize",sizes[i]*low);
                measured[i]=(Vector2)preferred.Invoke(labels[i],new object[]{widths[i],float.PositiveInfinity});
                float height=measured[i].y;
                Component container=labels[i].GetComponent(textType.Assembly.GetType("TMPro.TextContainer",true));
                if(container!=null)
                {
                    Set(container,"isAutoFitting",false);Set(container,"pivot",new Vector2(.5f,.5f));
                    Set(container,"width",widths[i]);Set(container,"height",height);
                }
                rects[i].pivot=new Vector2(.5f,.5f);rects[i].sizeDelta=new Vector2(widths[i],height);
                float worldHeight=height*Mathf.Abs(rects[i].localScale.y);
                rects[i].anchoredPosition=new Vector2(0,top-worldHeight*.5f);top-=worldHeight+gap;
                labels[i].GetComponent<PickupPrompt>()?.Refresh();
            }
        }
        internal void Bind(Component target,Transform template,PickupReplacement definition,string paragraph,string[] actions)
        {
            if(entry!=null&&entry!=definition)throw new InvalidOperationException("Pickup panel reused before disposal");
            entry=definition;label=target;content=paragraph;boundActions=actions;if(label==null||template==null)return;
            Set(label,"text",content);
            if(buttons==null)
            {
                int count=actions.Length;
                buttons=new GameObject[count];renderers=new SpriteRenderer[count];
                keyLabels=new Component[count];materials=new Material[count];
                // Clone while inactive so copied PlayMaker animations cannot start before
                // they are disabled. Only ActionButtonIcon remains responsible for input art.
                template.gameObject.SetActive(false);
                for(int i=0;i<count;i++)MakeButton(template,i,actions[i]);
            }
            dirty=true;
            if(changed!=null)return;handler=PickupMessages.Handler();if(handler==null)return;
            changed=Delegate.CreateDelegate(PickupMessages.Changed.EventHandlerType,this,nameof(Refresh));
            PickupMessages.Changed.AddEventHandler(handler,changed);
        }
        private void MakeButton(Transform template,int index,string action)
        {
            GameObject clone=UnityEngine.Object.Instantiate(template.gameObject,label.transform,false);
            buttons[index]=clone;clone.name="Mario "+action+" prompt";
            Type fsm=HarmonyLib.AccessTools.TypeByName("PlayMakerFSM");
            foreach(Component state in clone.GetComponentsInChildren(fsm,true))((Behaviour)state).enabled=false;
            Type iconType=HarmonyLib.AccessTools.TypeByName("ActionButtonIcon");
            Component icon=clone.GetComponent(iconType);
            iconType.GetField("showForControllerOnly",Any).SetValue(icon,false);
            iconType.GetField("liveUpdate",Any).SetValue(icon,true);
            renderers[index]=clone.GetComponent<SpriteRenderer>();
            keyLabels[index]=iconType.GetField("label",Any).GetValue(icon) as Component;
            // Native animation starts the template's material transparent. Each new icon
            // needs its own opaque base material; its fade follows the description below.
            materials[index]=new Material(renderers[index].sharedMaterial);
            materials[index].color=Color.white;renderers[index].sharedMaterial=materials[index];
            renderers[index].color=new Color(1,1,1,0);
            // Assign before OnEnable refreshes the native icon, including our
            // registered custom cap action. Its numeric enum value is intentional.
            FieldInfo actionField=iconType.GetField("action",Any);
            actionField.SetValue(icon,Enum.Parse(actionField.FieldType,PickupMessages.PromptAction(action)));
            clone.SetActive(true);
            iconType.GetMethod("SetActionString",Any).Invoke(icon,new object[]{PickupMessages.PromptAction(action)});
        }
        public void Refresh(){dirty=true;}
        private void Layout()
        {
            // The game's native controller art has no stick-click glyph. Keep
            // its blank button and label it R3 (or the rebound button), rather
            // than leaving an empty cap-selection prompt on controllers.
            for(int i=0;i<boundActions.Length;i++)
                if(boundActions[i]=="CAP_SELECTION"&&keyLabels[i]!=null)
                {
                    object manager=CapAccess.Read(HarmonyLib.AccessTools.TypeByName("UIManager"),"instance");
                    Sprite blank=CapAccess.Read(CapAccess.Read(manager,"uiButtonSkins"),"blankKey") as Sprite;
                    if(blank!=null&&renderers[i].sprite==blank)Set(keyLabels[i],"text",CapControls.PromptName);
                }
            label.GetType().GetMethod("ForceMeshUpdate",Any,null,new[]{typeof(bool)},null).Invoke(label,new object[]{true});
            object info=Read(label,"textInfo");Array characters=Read(info,"characterInfo") as Array;
            int count=Convert.ToInt32(Read(info,"characterCount")),slot=0;
            if(count==0)return; // TMP/font initialization can complete later this frame.
            string source=content;
            for(int i=0;i<count&&slot<buttons.Length;i++)
            {
                object character=characters.GetValue(i);
                // Track original string positions, so even a font's missing-| fallback
                // glyph still locates the reserved cell correctly.
                int at=Convert.ToInt32(Read(character,"index"));
                if(at<0||at>=source.Length||source[at]!='|')continue;
                Vector3 bottom=(Vector3)Read(character,"bottomLeft"),top=(Vector3)Read(character,"topRight");
                Sprite sprite=renderers[slot].sprite;if(sprite==null)continue;
                float height=Mathf.Max(.1f,top.y-bottom.y)*.95f;
                float scale=height/Mathf.Max(.01f,sprite.bounds.size.y);
                // Keep wide keyboard keys within the same reserved cell too.
                scale=Mathf.Min(scale,1.9f/Mathf.Max(.01f,sprite.bounds.size.x));
                buttons[slot].transform.localScale=Vector3.one*scale;
                Vector3 centre=(bottom+top)*.5f-sprite.bounds.center*scale;centre.z=-.01f;
                buttons[slot].transform.localPosition=centre;
                slot++;
            }
            if(slot!=buttons.Length)throw new InvalidOperationException("Pickup instruction marker layout: "+entry.Id);
            dirty=false;
        }
        private void LateUpdate()
        {
            if(label==null||renderers==null)return;
            foreach(SpriteRenderer renderer in renderers)if(renderer==null)return;
            try
            {
                if(dirty)Layout();
                Color fade=(Color)Read(label,"color");
                for(int i=0;i<buttons.Length;i++)
                {
                    renderers[i].color=new Color(1,1,1,fade.a);
                    if(keyLabels[i]!=null)Set(keyLabels[i],"color",new Color(1,1,1,fade.a));
                }
            }
            catch(Exception error){dirty=false;PickupMessages.Warn(error);}
        }
        private void OnDestroy()
        {
            if(changed!=null)PickupMessages.Changed.RemoveEventHandler(handler,changed);
            if(materials!=null)foreach(Material material in materials)if(material!=null)UnityEngine.Object.Destroy(material);
        }
    }
}
