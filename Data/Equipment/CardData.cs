using UnityEngine;

// =============================================================
// CardData — ScriptableObject template de carte (consumable)
// Path : Assets/Scripts/Data/Equipment/CardData.cs
// AetherTree GDD v3.6 — §5.x (Cartes)
//
// Hérite de ItemData (itemID, displayName, description, icon,
// requiredLevel... — voir ItemData). Pas de EquipmentConfig pour
// l'instant — TODO §5.x : définir le système de bonus des cartes :
//   - Bonus permanents vs temporaires ?
//   - Une seule carte active à la fois (cf. Player.equippedCardInstance) ?
//   - Stackable ? Consommée à l'utilisation ou persistante ?
//   - Interaction avec CharacterStats.RecalculateStats() ?
//   - Rareté / upgrade applicables ?
//
// En attendant, CardData et CardInstance sont des stubs compilables.
// =============================================================

[CreateAssetMenu(fileName = "NewCard", menuName = "AetherTree/Items/CardData")]
public class CardData : ItemData
{
    // ── Bonus — TODO ──────────────────────────────────────────
    // TODO: définir le type de bonus octroyé par la carte.
    // Pistes possibles :
    //   public List<StatBonus> bonuses;
    //   public CardEffectType effectType;
    //   public float effectValue;
    //   public float duration; // 0 = permanent

    // ── Utilitaires ───────────────────────────────────────────

    public CardInstance CreateInstance() => new CardInstance(this);
}

// =============================================================
// CardInstance — wrapper runtime d'une carte équipée / consommée
// GDD v3.6 — §5.x (Cartes)
// =============================================================
[System.Serializable]
public class CardInstance
{
    public CardData data;

    public CardInstance(CardData source) { data = source; }

    // ── Raccourcis SO ─────────────────────────────────────────
    public string ItemId       => data != null ? data.itemID : "unknown_card";
    public string CardName      => data != null ? data.displayName.Get(LocalizationManager.CurrentLanguage) : "Card";
    public Sprite Icon          => data != null ? data.icon          : null;
    public int    RequiredLevel => data != null ? data.requiredLevel : 1;

    // TODO: exposer ici les bonus calculés de la carte
    // ex: public List<StatBonus> Bonuses => data?.bonuses;
}
