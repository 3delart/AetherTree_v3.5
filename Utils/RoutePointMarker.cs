using UnityEngine;

// =============================================================
// ROUTEPOINTMARKER.CS — Marqueur visuel pour points de patrol route PNJ
// Path : Assets/Scripts/Utils/RoutePointMarker.cs
//
// Composant purement cosmétique, AUCUNE logique de jeu — à poser sur les
// GameObjects vides glissés dans PNJ.patrolPoints. PNJ.OnDrawGizmosSelected()
// ne dessine la route QUE quand le PNJ lui-même est sélectionné dans la
// Scene view ; ce composant rend chaque point visible individuellement,
// sans avoir à sélectionner le PNJ. Demande Florian 2026-10-04.
// =============================================================
public class RoutePointMarker : MonoBehaviour
{
    [Tooltip("Rayon de la sphère affichée dans la Scene view — visuel uniquement, aucun effet en jeu.")]
    public float gizmoRadius = 0.3f;

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(transform.position, gizmoRadius);
    }
}
