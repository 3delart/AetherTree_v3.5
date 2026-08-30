using System;

// =============================================================
// GAMEEVENTBUS.CS — Bus d'événements central
// Path : Assets/Scripts/Events/GameEventBus.cs
// AetherTree GDD v31
//
// Publishers  → GameEventBus.Publish(monEvent)
// Subscribers → GameEventBus.OnXxx += MonHandler  (OnEnable)
//               GameEventBus.OnXxx -= MonHandler  (OnDisable)
//
// Reset() appelé par SceneLoader à chaque changement de map.
// Après Reset(), chaque singleton appelle Resubscribe() lui-même.
// =============================================================

public static class GameEventBus
{
    // ── Events ───────────────────────────────────────────────

    public static event Action<MobKilledEvent>      OnMobKilled;
    public static event Action<DamageDealtEvent>    OnDamageDealt;
    public static event Action<SkillUsedEvent>      OnSkillUsed;
    public static event Action<PlayerDeathEvent>    OnPlayerDeath;
    public static event Action<PlayerLevelUpEvent>  OnPlayerLevelUp;
    public static event Action<DebuffReceivedEvent> OnDebuffReceived;
    public static event Action<NpcInteractEvent>    OnNpcInteract;
    public static event Action<ZoneEvent>           OnZoneEntered;
    public static event Action<ItemEvent>           OnItemAction;
    public static event Action<SocialEvent>         OnSocialAction;
    public static event Action<PetEvent>            OnPetAction;
    public static event Action<TimeEvent>           OnTimeAction;
    public static event Action<MetierEvent>         OnMetierAction;
    public static event Action<ServerEvent>         OnServerEvent;
    public static event Action<StatsChangedEvent>   OnStatsChanged;
    public static event Action<QuestEvent>          OnQuestAction;
    public static event System.Action OnSaveLoaded;

    // ── Publish ──────────────────────────────────────────────

    public static void Publish(MobKilledEvent e)      => OnMobKilled?.Invoke(e);
    public static void Publish(DamageDealtEvent e)    => OnDamageDealt?.Invoke(e);
    public static void Publish(SkillUsedEvent e)      => OnSkillUsed?.Invoke(e);
    public static void Publish(PlayerDeathEvent e)    => OnPlayerDeath?.Invoke(e);
    public static void Publish(PlayerLevelUpEvent e)  => OnPlayerLevelUp?.Invoke(e);
    public static void Publish(DebuffReceivedEvent e) => OnDebuffReceived?.Invoke(e);
    public static void Publish(NpcInteractEvent e)    => OnNpcInteract?.Invoke(e);
    public static void Publish(ZoneEvent e)           => OnZoneEntered?.Invoke(e);
    public static void Publish(ItemEvent e)           => OnItemAction?.Invoke(e);
    public static void Publish(SocialEvent e)         => OnSocialAction?.Invoke(e);
    public static void Publish(PetEvent e)            => OnPetAction?.Invoke(e);
    public static void Publish(TimeEvent e)           => OnTimeAction?.Invoke(e);
    public static void Publish(MetierEvent e)         => OnMetierAction?.Invoke(e);
    public static void Publish(ServerEvent e)         => OnServerEvent?.Invoke(e);
    public static void Publish(StatsChangedEvent e)   => OnStatsChanged?.Invoke(e);
    public static void Publish(QuestEvent e)          => OnQuestAction?.Invoke(e);
    public static void PublishSaveLoaded() => OnSaveLoaded?.Invoke();

    // ── Reset ────────────────────────────────────────────────

    public static void Reset()
    {
        OnMobKilled      = null;
        OnDamageDealt    = null;
        OnSkillUsed      = null;
        OnPlayerDeath    = null;
        OnPlayerLevelUp  = null;
        OnDebuffReceived = null;
        OnNpcInteract    = null;
        OnZoneEntered    = null;
        OnItemAction     = null;
        OnSocialAction   = null;
        OnPetAction      = null;
        OnTimeAction     = null;
        OnMetierAction   = null;
        OnServerEvent    = null;
        OnStatsChanged   = null;
        OnQuestAction    = null;

        // Chaque singleton se réabonne lui-même
        UnlockManager.Instance?.Resubscribe();
        XPSystem.Instance?.Resubscribe();
        LootManager.Instance?.Resubscribe();
        AerisSystem.Instance?.Resubscribe();
        QuestSystem.Instance?.Resubscribe();
        CharacterPanelUI.Instance?.Resubscribe();
    }
}
