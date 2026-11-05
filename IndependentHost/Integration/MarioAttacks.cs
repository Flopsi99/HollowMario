using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace MarioHollow
{
    public struct AttackRow
    {
        public string Id;
        public uint Action;
        public float Radius,Forward,Up,Damage;
        public AttackRow(string id,uint action,float damage)
        {Id=id;Action=action;Radius=Forward=Up=0;Damage=damage;}
    }
    public struct ControlRow{public string Field;public string[] HkInputs;}
    public static class Sheets
    {
        // Public upstream action IDs; geometry is supplied by the shared move policy.
        public static AttackRow[] Attacks={
            new AttackRow("punch",0x00800380,1),
            new AttackRow("move_punch",0x00800457,1),
            new AttackRow("jump_kick",0x018008AC,1),
            new AttackRow("slide_kick",0x018008AA,1),
            new AttackRow("slide_kick_slide",0x0080045A,1),
            new AttackRow("dive",0x0188088A,2),
            new AttackRow("dive_slide",0x00880456,2),
            new AttackRow("ground_pound",0x008008A9,2),
            new AttackRow("ground_pound_land",0x0080023C,2)};
        public static ControlRow[] Controls={new ControlRow{Field="buttonZ",HkInputs=new[]{"dash"}}};
    }
    public sealed class MarioAttacks
    {
        public static Func<object,object,string,float,Bounds> CandidateBounds;
        private uint lastAction;
        private readonly Collider2D[] contacts=new Collider2D[128];
        private readonly HashSet<int> struck=new HashSet<int>();
        private int queryTraces;
        [MethodImpl(MethodImplOptions.NoInlining)]public void Tick(MarioController controller,HeroController hero)
        {
            uint action=controller.State.action;if(action!=lastAction){struck.Clear();queryTraces=0;lastAction=action;HostTrace.Write("attack episode action="+action.ToString("X")+", anim="+controller.State.animID.ToString("X")+"/"+controller.State.animFrame);}
            foreach(AttackRow row in Sheets.Attacks)
            {
                if(row.Action!=action)continue;
                float direction=Mathf.Sin(controller.State.faceAngle)>=0?1:-1;
                if(CandidateBounds==null)throw new InvalidOperationException("Shared attack geometry is not bound.");
                Bounds reach=CandidateBounds(controller,hero,row.Id,direction);
                int count=Physics2D.OverlapBoxNonAlloc(reach.center,reach.size,0,contacts,(1<<11)|(1<<12)|(1<<17));
                if(queryTraces++<3)HostTrace.Write("attack query "+row.Id+": shared bounds="+reach+", overlaps="+count+", anim="+controller.State.animID.ToString("X")+"/"+controller.State.animFrame);
                for(int i=0;i<count;i++)
                {
                    var target=contacts[i]?.GetComponentInParent<HealthManager>();if(target==null||struck.Contains(target.GetInstanceID()))continue;
                    int receiver=target.GetInstanceID(),before=target.hp;bool blocked=target.IsInvincible;
                    Strike(target,hero,direction,row);
                    if(target==null||target.hp<before||blocked)struck.Add(receiver);
                }
                return;
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]public void Strike(IHitResponder target,HeroController hero,float dir,AttackRow row)
        {
            var hit=new HitInstance{
                Source=hero.gameObject,AttackType=AttackTypes.Nail,
                DamageDealt=Mathf.Max(1,Mathf.RoundToInt(hero.playerData.nailDamage*row.Damage)),
                Direction=dir>0?0f:180f,Multiplier=1f,MagnitudeMultiplier=1f,
                CircleDirection=false,MoveAngle=0,MoveDirection=false,IgnoreInvulnerable=false};
            var manager=target as HealthManager;int health=manager!=null?manager.hp:0;target.Hit(hit);
            HostTrace.Write("strike "+row.Id+": dir="+dir+", damage="+hit.DamageDealt+", target="+(manager!=null?manager.name:target.GetType().Name)+", HP="+health+"->"+(manager!=null?manager.hp:0));
            // HealthManager owns SoulGain, including enemy exclusions, charms
            // and other mods. A successful native nail hit already awards it.
        }
    }
}
