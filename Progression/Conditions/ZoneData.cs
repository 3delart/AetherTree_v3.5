using UnityEngine;

// =============================================================
// ZONEDATA.CS
// Path : Assets/_Game/Scripts/Progression/Conditions/Zone/ZoneData.cs
//
// ScriptableObject décrivant une zone de jeu.
// Référencé par ZoneTrigger (composant sur prefab).
// Le ZoneChecker filtre par zoneID.
//
// Création : clic droit → AetherTree/Zone/ZoneData
// =============================================================

[CreateAssetMenu(fileName = "zone_", menuName = "AetherTree/Progression/ZoneData")]
public class ZoneData : ScriptableObject
{
    [Header("Identifiant")]
    [Tooltip("ID unique utilisé par ZoneChecker. Ex : 'sous_arbre', 'fontaine_nord'")]
    public string zoneID;

    [Header("Propriétés")]
    public bool isOutdoor   = true;
    public bool isDungeon   = false;
    [Tooltip("True = zone PvP activé")]
    public bool isPvP       = false;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(zoneID))
            zoneID = name;
    }
#endif
}
