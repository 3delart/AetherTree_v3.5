using System.Collections.Generic;
using UnityEngine;

// =============================================================
// GAMEEVENTS.CS — Toutes les structs d'événements du jeu
// Path : Assets/Scripts/Events/GameEvents.cs
// AetherTree GDD v31
// =============================================================

// ── Kill ─────────────────────────────────────────────────────
// Publié par : Mob.Die()
public struct MobKilledEvent
{
    public MobData      mob;
    public SkillData    killerSkill;
    public WeaponType   killerWeapon;
    public List<Player> eligiblePlayers;
    public Vector3      deathPosition;
    public bool         wasStealth;
    public bool         wasUnarmed;
    public bool         wasBoss;
    public string       locationID;
    public bool         isInParty;
}

// ── Damage ───────────────────────────────────────────────────
// Publié par : CombatSystem après calcul final
// IMPORTANT : publié APRÈS mise à jour des HP de la cible
//             (target.CurrentHP reflète déjà les dégâts reçus)
public struct DamageDealtEvent
{
    public float       amount;
    public ElementType element;
    public Entity      source;
    public Entity      target;
    public bool        isCrit;
    public bool        isOneHit;
}

// ── Skill utilisé ────────────────────────────────────────────
// Publié par : SkillSystem.Execute()
public struct SkillUsedEvent
{
    public SkillData   skill;
    public Entity      target;
    public Entity      caster;
    public ElementType primaryElement;
    public bool        isCombo;
    public string      locationID;
    public bool        isInParty;
}

// ── Mort du joueur ───────────────────────────────────────────
// Publié par : Player.Die()
public struct PlayerDeathEvent
{
    public ElementType  cause;
    public Entity       killer;       // null si environnemental
    public float        hpAtDeath;   // HP au moment de la mort (avant résurrection)
    public DeathContext context;
}
public enum DeathContext { OpenWorld, Dungeon, PvP }

// ── Level up joueur ──────────────────────────────────────────
// Publié par : Player.OnLevelUp()
public struct PlayerLevelUpEvent
{
    public int newLevel;
    public int previousLevel;
}

// ── Debuff reçu ──────────────────────────────────────────────
// Publié par : StatusEffectSystem.TryApplyDebuff()
public struct DebuffReceivedEvent
{
    public DebuffType debuffType;
    public Entity     source;
    public Entity     target;
}

// ── Interaction NPC / animal ─────────────────────────────────
// Publié par : NpcInteractionSystem
public struct NpcInteractEvent
{
    public string      npcID;
    public InteractType action;
}
public enum InteractType { Any = -1, Talk, Caress, Feed, Buy, Sell, Quest }

// ── Zone ─────────────────────────────────────────────────────
// Publié par : ZoneTrigger (tick périodique ET sortie de zone)
public struct ZoneEvent
{
    public string zoneID;

    // Durée depuis l'entrée dans la zone (tick courant)
    public float  timeSpentSeconds;

    // Durée totale passée dans la zone — rempli uniquement sur isFinalExit=true
    public float  totalTimeSeconds;

    // Durée AFK / nuit continues dans la zone (remises à zéro dès que la condition
    // casse) — indépendantes de timeSpentSeconds (temps brut, non filtré). Permet à
    // chaque ZoneChecker de choisir le bon compteur selon SES PROPRES mustBeAFK/
    // atNight, sans dépendre d'un filtre partagé posé sur le ZoneTrigger lui-même
    // (un même trigger peut être écouté par plusieurs conditions aux exigences différentes).
    public float  continuousAFKSeconds;
    public float  continuousNightSeconds;
    public float  continuousAFKAndNightSeconds;

    // True = event de sortie (fin de présence), False = tick périodique
    public bool   isFinalExit;

    public bool   isAFK;
    public bool   isDungeon;
    public bool   dungeonSolo;
    public bool   dungeonNoHit;
    public float  dungeonTimeSeconds;
}

// ── Item ─────────────────────────────────────────────────────
// Publié par : InventorySystem / CraftSystem
public enum ItemAction { Any = -1, Pickup, Craft, Use, Sell, Buy, Drop }
public struct ItemEvent
{
    public string     itemID;
    public ItemAction action;
    public int        quantity;
    public int        aerisAmount;
}

// ── Social ───────────────────────────────────────────────────
// Publié par : Player social methods
public enum SocialAction
{
    Any = -1, MeetPlayer, GroupUp, GroupLeader, Duel, DuelWin,
    GuildJoin, Revive, Trade, FirstServer
}
public struct SocialEvent
{
    public SocialAction action;
    public string       otherPlayerID;
    public bool         firstOnServer;
    public bool         isInParty;
}

// ── Pet ──────────────────────────────────────────────────────
// Publié par : PetSystem
public enum PetAction { Any = -1, Capture, Release, LevelUp, Feed, Talk, Revive }
public struct PetEvent
{
    public PetAction action;
    public MobData   mob;
    public string    npcID;
}

// ── Temps / Session ──────────────────────────────────────────
// Publié par : TimeSystem / ConnectionManager
//
// characterPlaytimeMinutes : temps joué sur CE personnage (sauvé dans character.json)
// accountPlaytimeMinutes   : temps joué cumulé sur tous les persos du compte
public enum TimeAction { Any = -1, Login, Logout, AFK, DayStart, NightStart, ConsecutiveDay }
public struct TimeEvent
{
    public TimeAction action;
    public float      afkMinutes;
    public int        consecutiveDays;
    public int        nightsPlayed;

    // Temps joué total — scope Character ou Account
    public float      characterPlaytimeMinutes;
    public float      accountPlaytimeMinutes;
}

// ── Serveur ──────────────────────────────────────────────────
// Publié par : ConnectionManager
public struct ServerEvent
{
    public bool firstConnection;
}

// ── Stats changées ───────────────────────────────────────────
// Publié par : CharacterStats.RecalculateStats()
public struct StatsChangedEvent
{
    public Player player;
}

// ── Quête ────────────────────────────────────────────────────
// Publié par : QuestSystem
public enum QuestAction { Accepted, ObjectiveUpdated, Completed, TurnedIn, Failed }
public struct QuestEvent
{
    public QuestData   quest;
    public QuestAction action;
    public int         objectiveIndex;
    public Player      player;
}

// ── Recette craftée ──────────────────────────────────────────
// Publié par : CraftSystem.ResolveCraft() → onComplete de la barre de canalisation
public struct RecipeCraftedEvent
{
    public RecipeData recipe;
    public Player      player;
    public int         quantity;
}
