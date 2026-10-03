using UnityEngine;

// =============================================================
// FOLLOWPOSITIONONLY.CS — Suit la position d'une cible, jamais sa rotation
// Path : Assets/Scripts/Utils/FollowPositionOnly.cs
//
// Pour un VFX attaché à une entité (ex: aura "chef de groupe" sur le
// joueur) qui doit rester droit dans le monde même quand la cible tourne —
// un simple parentage hérite rotation ET position, ce composant ne copie
// QUE la position. S'auto-détruit si la cible disparaît (LateUpdate, après
// que la cible ait bougé ce frame-là).
// =============================================================
public class FollowPositionOnly : MonoBehaviour
{
    public Transform target;
    public Vector3   offset = Vector3.zero;

    private void LateUpdate()
    {
        if (target == null) { Destroy(gameObject); return; }
        transform.position = target.position + offset;
    }
}
