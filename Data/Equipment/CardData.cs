using UnityEngine;
using System.Collections.Generic;

// =============================================================
// CardData — ScriptableObject template de carte (consumable)
// Path : Assets/Scripts/Data/Inventory/Equipment/CardData.cs
// AetherTree GDD v3.5 — §5.x (Cartes)
//
// Consumable équipable qui octroie des bonus au joueur.
// TODO §5.x : définir le système de bonus des cartes :
//   - Bonus permanents vs temporaires ?
//   - Une seule carte active à la fois (cf. Player.equippedCardInstance) ?
//   - Stackable ? Consommée à l'utilisation ou persistante ?
//   - Interaction avec CharacterStats.RecalculateStats() ?
//   - Rareté / upgrade applicables ?
//
// En attendant, CardData et CardInstance sont des stubs compilables.
// =============================================================

[CreateAssetMenu(fileName = "NewCard", menuName = "AetherTree/Items/CardData")]
public class CardData : ScriptableObject
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public string cardName = "Card";
    public Sprite icon;

    // ── Niveau ────────────────────────────────────────────────
    [Header("Niveau")]
    [Tooltip("Niveau minimum du joueur requis pour utiliser cette carte.")]
    [Min(1)] public int requiredLevel = 1;

    // ── Bonus — TODO ──────────────────────────────────────────
    [Header("Bonus (TODO — système à définir dans le GDD §5.x)")]
    // TODO: définir le type de bonus octroyé par la carte.
    // Pistes possibles :
    //   public List<StatBonus> bonuses;
    //   public CardEffectType effectType;
    //   public float effectValue;
    //   public float duration; // 0 = permanent

    // ── Description ───────────────────────────────────────────
    [Header("Description")]
    [TextArea]
    public string description = "";

    // ── Utilitaires ───────────────────────────────────────────

    public CardInstance CreateInstance() => new CardInstance(this);
}

// =============================================================
// CardInstance — wrapper runtime d'une carte équipée / consommée
// GDD v3.5 — §5.x (Cartes)
// =============================================================
[System.Serializable]
public class CardInstance
{
    public CardData data;

    public CardInstance(CardData source) { data = source; }

    // ── Raccourcis SO ─────────────────────────────────────────
    public string CardName      => data != null ? data.cardName      : "Card";
    public Sprite Icon          => data != null ? data.icon          : null;
    public int    RequiredLevel => data != null ? data.requiredLevel : 1;

    // TODO: exposer ici les bonus calculés de la carte
    // ex: public List<StatBonus> Bonuses => data?.bonuses;
}
