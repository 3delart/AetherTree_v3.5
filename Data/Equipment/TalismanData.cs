using UnityEngine;

// =============================================================
// TalismanData — ScriptableObject template de talisman
// Path : Assets/Scripts/Data/Equipment/TalismanData.cs
// AetherTree GDD v3.5
//
// Hérite de ItemData (itemID, displayName, description, icon,
// requiredLevel, vendorPrice... — voir ItemData).
//
// Un talisman applique un BuffData tant qu'il est équipé (retiré
// du CharacterPanel si le talisman est retiré) et a une durée de
// vie totale qui démarre au tout premier équipement — indépendante
// du fait d'être équipé ou non, s'écoule même hors ligne (voir
// TalismanInstance.activatedAt, timestamp réel — pas un compteur de
// temps de jeu). Une fois expiré, l'objet est détruit. Pas de
// rareté, pas d'upgrade — item simple à palier fixe.
// =============================================================

[CreateAssetMenu(fileName = "talisman_", menuName = "AetherTree/Inventaire/Equipement/TalismanData")]
public class TalismanData : ItemData
{
    [Header("Effet")]
    [Tooltip("Buff appliqué tant que le talisman est équipé — retiré immédiatement si le\n" +
             "talisman est retiré du CharacterPanel (voir Player.UnequipTalisman).")]
    public BuffData buffToApply;

    [Header("Durée de vie")]
    [Tooltip("Durée totale de vie du talisman en secondes, temps RÉEL — démarre au premier\n" +
             "équipement, continue de s'écouler même déconnecté ou une fois retiré. À\n" +
             "expiration, l'objet est détruit (voir TalismanInstance.IsExpired).")]
    public float durationSeconds = 3600f;

    // ── Utilitaires ───────────────────────────────────────────

    public TalismanInstance CreateInstance() => new TalismanInstance(this);
}

// =============================================================
// TalismanInstance — wrapper runtime d'un talisman
// =============================================================
[System.Serializable]
public class TalismanInstance
{
    public TalismanData data;

    /// <summary>Timestamp réel du premier équipement — null = jamais équipé, jamais démarré.
    /// Ré-équiper un talisman déjà activé ne modifie PAS cette valeur (voir Activate()).</summary>
    public System.DateTime? activatedAt;

    public TalismanInstance(TalismanData source) { data = source; }

    // ── Raccourcis SO ─────────────────────────────────────────
    public string ItemId       => data != null ? data.itemID : "unknown_talisman";
    public string TalismanName => data != null ? data.displayName.Get(LocalizationManager.CurrentLanguage) : "Talisman";
    public Sprite Icon         => data != null ? data.icon          : null;
    public int    RequiredLevel => data != null ? data.requiredLevel : 1;

    // ── État ──────────────────────────────────────────────────

    public bool IsActivated => activatedAt.HasValue;

    /// <summary>True une fois la durée de vie totale écoulée depuis le premier équipement —
    /// calculé sur du temps réel (System.DateTime.Now), pas un compteur en jeu.</summary>
    public bool IsExpired => activatedAt.HasValue && data != null
        && (System.DateTime.Now - activatedAt.Value).TotalSeconds >= data.durationSeconds;

    /// <summary>Temps réel restant avant expiration — utilisé pour synchroniser la durée du
    /// buff affiché (voir StatusEffectSystem.ApplyBuffWithDuration). Durée totale si jamais
    /// activé (le premier équipement s'apprête à démarrer le chrono).</summary>
    public float RemainingSeconds
    {
        get
        {
            if (data == null) return 0f;
            if (!activatedAt.HasValue) return data.durationSeconds;
            float elapsed = (float)(System.DateTime.Now - activatedAt.Value).TotalSeconds;
            return Mathf.Max(0f, data.durationSeconds - elapsed);
        }
    }

    /// <summary>Démarre le chrono au tout premier équipement — idempotent, un ré-équipement
    /// ultérieur n'a aucun effet si déjà activé.</summary>
    public void Activate()
    {
        if (!activatedAt.HasValue)
            activatedAt = System.DateTime.Now;
    }
}
