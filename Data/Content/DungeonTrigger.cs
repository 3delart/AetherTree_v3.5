// =============================================================
// DUNGEONTRIGGER.CS — Condition de déverrouillage intra-donjon
// Path : Assets/Scripts/Data/Content/DungeonTrigger.cs
// AetherTree GDD v3.6 — §14.2.3 (champ triggers/lockedUntil sur DungeonMapData)
//
// Mécanisme minimal : un triggerID (string) que InstanceSession.NotifyTriggerMet()/
// IsTriggerMet() track pour LE RUN EN COURS (jamais persisté). Un Portal (voir
// Events/Portal.cs, gateType = RequiresTrigger) référence ce même triggerID pour
// se verrouiller/déverrouiller. Ce fichier reste purement déclaratif pour ce
// chantier — aucun système ne lit encore DungeonMapData.triggers, le vrai verrou
// vit sur Portal.requiredTriggerID (une simple string, pas ce type) — voir
// docs/superpowers/specs/2026-09-23-donjon-entry-flow-design.md §3 Non-objectifs.
// =============================================================

public enum DungeonTriggerType
{
    MobKilled      = 0,
    LeverActivated = 1,
}

[System.Serializable]
public class DungeonTrigger
{
    [Tooltip("Identifiant référencé par Portal.requiredTriggerID.")]
    public string triggerID;
    public DungeonTriggerType triggerType;
    [Tooltip("MobKilled uniquement — quel Mob doit mourir pour satisfaire ce trigger.")]
    public MobData requiredMob;
}
