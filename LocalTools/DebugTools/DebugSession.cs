using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HollowMarioDebug
{
    // Live values are ordinary PlayerData fields so Hollow Mario's direct reads
    // and native FSMs agree. BeforeSave patches a separate copy, never live data.
    internal sealed class DebugSession
    {
        private static readonly FieldInfo[] fields=typeof(PlayerData).GetFields(BindingFlags.Instance|BindingFlags.Public);
        private static readonly MethodInfo clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        private readonly Dictionary<FieldInfo,object> original=new Dictionary<FieldInfo,object>();
        private readonly HashSet<FieldInfo> edited=new HashSet<FieldInfo>();
        internal readonly PlayerData Data;
        internal DebugSession(PlayerData data)
        {
            Data=data;
            foreach(var field in fields)
                if(field.FieldType==typeof(bool)||field.FieldType==typeof(int)||field.FieldType==typeof(List<int>))original[field]=Copy(field.GetValue(data));
        }
        private static object Copy(object value)=>value is List<int> list?new List<int>(list):value;
        private static FieldInfo Field(string key)=>typeof(PlayerData).GetField(key,BindingFlags.Instance|BindingFlags.Public)
            ??throw new MissingFieldException(typeof(PlayerData).Name,key);
        internal int Get(string key)
        {
            object value=Field(key).GetValue(Data);return value is bool flag?(flag?1:0):Convert.ToInt32(value);
        }
        internal void Put(string key,int value)
        {
            var field=Field(key);edited.Add(field);
            field.SetValue(Data,field.FieldType==typeof(bool)?(object)(value!=0):value);
        }
        internal void Nail(int level){Put("nailSmithUpgrades",level);Put("nailDamage",5+4*level);}
        internal void Toggle(string key,int value)
        {
            Put(key,value);
            // Real cloak/claw pickups set the permission flag as well as ownership.
            // Mario's aerial long jump calls CanDash, which reads canDash.
            if(key=="hasDash")
            {Put("canDash",value);if(value==0)Put("hasShadowDash",0);}
            if(key=="hasShadowDash"&&value!=0){Put("hasDash",1);Put("canDash",1);}
            if(key=="hasWalljump")Put("canWallJump",value);
            if(key=="hasDreamGate"&&value!=0&&Dream()==0)Dream(1);
            if(key=="hasCyclone"||key=="hasUpwardSlash"||key=="hasDashSlash")Arts();
        }
        internal void Spell(string key,int level)
        {
            Put(key,level);Put("hasSpell",Get("fireballLevel")+Get("quakeLevel")+Get("screamLevel")>0?1:0);
        }
        internal void Arts()
        {
            int count=Get("hasCyclone")+Get("hasUpwardSlash")+Get("hasDashSlash");
            Put("hasNailArt",count>0?1:0);Put("hasAllNailArts",count==3?1:0);
        }
        internal int Dream()=>Get("hasDreamNail")==0?0:Get("dreamNailUpgraded")!=0?2:1;
        internal void Dream(int level)
        {
            Put("hasDreamNail",level>0?1:0);Put("dreamNailUpgraded",level==2?1:0);
            if(level==0)Put("hasDreamGate",0);
        }
        internal int Charm(int id)=>Get("equippedCharm_"+id)!=0?2:Get("gotCharm_"+id);
        internal void Charm(int id,int state)
        {
            if(id==36)
            {
                int form=Get("royalCharmState");
                if(state==0){Put("royalCharmState",0);Put("charmCost_36",5);}
                else
                {
                    if(form<3){form=3;Put("royalCharmState",form);}
                    Put("charmCost_36",form==4?0:5);
                    if(form==4)state=2; // Void Heart is always equipped.
                }
            }
            if(id==40)
            {
                if(state==0)Put("grimmChildLevel",0);
                else if(Get("grimmChildLevel")==0)Put("grimmChildLevel",1);
                Put("charmCost_40",Get("grimmChildLevel")==5?3:2);
            }
            SetCharm(id,state);
        }
        internal void Royal(int form)
        {
            bool equipped=Charm(36)==2;
            Put("royalCharmState",form);Put("charmCost_36",form==4?0:5);
            // A half is an inventory fragment, not a functional Kingsoul.
            SetCharm(36,form==4?2:form==3?(equipped?2:1):0);
        }
        private void SetCharm(int id,int state)
        {
            Put("gotCharm_"+id,state>0?1:0);Put("equippedCharm_"+id,state==2?1:0);
            if(state==2&&id>=23&&id<=25)Put("brokenCharm_"+id,0);
            if(state==2&&id==40&&Get("grimmChildLevel")==0)Put("grimmChildLevel",1);
            var equipped=Enumerable.Range(1,40).Where(i=>Get("equippedCharm_"+i)!=0).ToList();
            var list=Field("equippedCharms");edited.Add(list);list.SetValue(Data,equipped);
            Put("charmsOwned",Enumerable.Range(1,40).Count(i=>Get("gotCharm_"+i)!=0));
            Put("hasCharm",Get("charmsOwned")>0?1:0);
            Put("charmSlotsFilled",equipped.Sum(i=>Get("charmCost_"+i)));
            Put("overcharmed",Get("charmSlotsFilled")>Get("charmSlots")?1:0);
        }
        internal void Refresh(Action refresh)
        {
            // Capture derived stat changes made by the native charm refresh too.
            var before=original.Keys.Where(f=>f.FieldType!=typeof(List<int>)).ToDictionary(f=>f,f=>f.GetValue(Data));
            refresh();
            foreach(var pair in before)if(!Equals(pair.Value,pair.Key.GetValue(Data)))edited.Add(pair.Key);
        }
        internal PlayerData ForSave(PlayerData source=null)
        {
            var result=(PlayerData)clone.Invoke(source??Data,null);
            foreach(var field in edited)field.SetValue(result,Copy(original[field]));
            return result;
        }
        internal void Restore()
        {
            foreach(var field in edited)field.SetValue(Data,Copy(original[field]));
            edited.Clear();
        }
    }
}
