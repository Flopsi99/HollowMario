using UnityEngine;

namespace HollowMario
{
    // An interaction measurement, not another player. Its separate kinematic
    // body also prevents probe contacts being delivered to the Knight's FSMs.
    internal sealed class InteractionProbe:MonoBehaviour
    {
        private CrumblingFloorContact floors;
        internal static BoxCollider2D Create(Transform hero,BoxCollider2D body)
        {
            var child=new GameObject("Hollow Mario interaction probe");
            child.SetActive(false);child.tag="Untagged";child.layer=hero.gameObject.layer;
            child.transform.SetParent(hero,false);
            var physics=child.AddComponent<Rigidbody2D>();
            physics.bodyType=RigidbodyType2D.Kinematic;physics.gravityScale=0f;
            physics.useFullKinematicContacts=true;
            var shape=child.AddComponent<BoxCollider2D>();shape.enabled=false;shape.isTrigger=true;
            shape.size=new Vector2(body.size.x*.9f,.55f);
            shape.offset=new Vector2(body.offset.x,body.offset.y-body.size.y*.5f-.25f);
            child.AddComponent<InteractionProbe>().floors=hero.GetComponent<CrumblingFloorContact>();
            return shape;
        }
        private void OnTriggerEnter2D(Collider2D other)=>Touch(other);
        private void OnTriggerStay2D(Collider2D other)=>Touch(other);
        private void Touch(Collider2D other)
        {
            if(floors==null)floors=transform.parent?.GetComponent<CrumblingFloorContact>();
            floors?.Touch(other);
        }
    }
}
