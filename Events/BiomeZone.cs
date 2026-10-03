using UnityEngine;

// =============================================================
// BIOMEZONE.CS — Trigger de zone de map (biome/région), affiche son nom sur la minimap
// Path : Assets/Scripts/Events/BiomeZone.cs
//
// DISTINCT de ZoneTrigger (Progression/Conditions/ZoneTrigger.cs) — celui-ci sert aux CONDITIONS,
// pas à l'affichage. BiomeZone ne fait qu'une chose : à l'entrée du joueur, affiche displayName
// sur la minimap. Une grande zone de nature ("Forêt Trouble") peut contenir plusieurs petits
// ZoneTrigger de conditions ("sous_arbre") sans conflit entre les deux systèmes.
//
// Pas d'asset séparé (pas de BiomeData) — chaque BiomeZone est une instance UNIQUE dans une
// scène (une position + une taille propres), le nom se tape directement dessus, pas besoin de le
// partager entre plusieurs endroits.
//
// À poser via le prefab Prefab_BiomeZone (menu AetherTree > Créer Prefab BiomeZone) — Sphere/Box
// Collider en trigger, dimensionné à la région voulue, Display Name rempli à la main. Glisser
// ensuite l'instance dans MapInfo.biomes (liste de recensement, voir MapInfo.cs).
//
// Pas de gestion d'imbrication : deux BiomeZone qui se chevauchent affichent le dernier entré, pas
// forcément le plus englobant — prévoir des zones non-chevauchantes pour un nom fiable en
// permanence (cas normal pour de grandes régions de map distinctes).
// =============================================================
[RequireComponent(typeof(Collider))]
public class BiomeZone : MonoBehaviour
{
    [Tooltip("Nom affiché sur la minimap à l'entrée du joueur dans ce BiomeZone.")]
    public string displayName = "";

    private void OnTriggerEnter(Collider other)
    {
        if (string.IsNullOrEmpty(displayName)) return;
        if (other.GetComponent<Player>() == null) return;

        MinimapUI.Instance?.SetZoneName(displayName);
    }
}
