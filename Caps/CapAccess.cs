using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace HollowMario
{
    internal static class CapAccess
    {
        internal const BindingFlags Any=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly Dictionary<Type,Dictionary<string,MemberInfo>> members=new Dictionary<Type,Dictionary<string,MemberInfo>>();
        internal static object Read(object target,string name)
        {
            if(target==null)return null;
            Type t=target as Type??target.GetType();
            if(!members.TryGetValue(t,out Dictionary<string,MemberInfo> cache))members[t]=cache=new Dictionary<string,MemberInfo>();
            if(!cache.TryGetValue(name,out MemberInfo member))cache[name]=member=(MemberInfo)AccessTools.Field(t,name)??AccessTools.Property(t,name);
            return member is FieldInfo f?f.GetValue(target is Type?null:target):member is PropertyInfo p?p.GetValue(target is Type?null:target,null):null;
        }
        internal static void Write(object target,string name,object value)
        {
            Type t=target.GetType();FieldInfo f=AccessTools.Field(t,name);
            if(f!=null)f.SetValue(target,value);else AccessTools.Property(t,name)?.SetValue(target,value,null);
        }
        internal static object Call(object target,string name,params object[] args)
        {
            Type t=target as Type??target.GetType();
            foreach(MethodInfo m in t.GetMethods(Any))
            {
                if(m.Name!=name||m.GetParameters().Length!=args.Length)continue;
                ParameterInfo[] ps=m.GetParameters();bool match=true;
                for(int i=0;i<ps.Length;i++)if(args[i]!=null&&!ps[i].ParameterType.IsInstanceOfType(args[i])){match=false;break;}
                if(match)return m.Invoke(target is Type?null:target,args);
            }
            throw new MissingMethodException(t.FullName,name);
        }
        internal static bool Flag(object target,string name)=>Read(target,name) is bool yes&&yes;
        internal static float Number(object target,string name)=>Convert.ToSingle(Read(target,name)??0f);
    }
}
