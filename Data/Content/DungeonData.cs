using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================
// DUNGEONDATA.CS — ScriptableObject template de donjon
// Path : Assets/Scripts/Data/Content/DungeonData.cs
// AetherTree GDD v3.6 — §14.2.3
//
// Couvre Donjon Classique ET Donjon de Déblocage via dungeonType — pas deux
// classes séparées (les deux partagent toute la structure GDD, seules les
// valeurs par défaut de livesPerPlayer/deathLimit diffèrent, voir §14.3.3
// "Règles Spécifiques" du GDD).
//
// DungeonMapData.bossMob est une CHECKLIST pour le designer — DungeonData est un
// asset de PROJET, il ne peut pas référencer des instances placées dans une scène
// précise, donc rien ici n'est lu par le code. L'enforcement réel vit en scène,
// par référence DIRECTE (pas de string à faire matcher) : Portal.requiredMobs
// référence les Mob exacts, et Portal.requiredActivatables référence les
// Activatable exacts (voir Events/Portal.cs, gateType = RequiresConditions) —
// les deux glissés depuis la Hierarchy, directement sur le Portal concerné, pas
// besoin d'une checklist séparée ici pour ça. Le boss lui-même est identifié par
// son MobData (MobType.DungeonMob + DungeonRole.Boss, voir Mob.isDungeonBoss) —
// bossMob ci-dessous n'est donc qu'une référence de confort pour toi.
// =============================================================

public enum DungeonType
{
    Classic    = 0,
    Unlock     = 1,
    FactionPvP = 2,
}

public enum RoomType
{
    Couloir  = 0, // Salle normale.
    BossRoom = 1, // Contient le boss (DungeonRole.Boss, jamais de respawn) — peut aussi avoir des
                  // adds normaux, qui respawnent selon LEUR PROPRE Mob.respawnEnabled (voir plus
                  // bas — plus un réglage par salle, un réglage par mob placé).
}

[System.Serializable]
public class DungeonMapData
{
#if UNITY_EDITOR
    [Tooltip("Glisse la scène ici — mapID se remplit automatiquement avec son nom (voir " +
             "DungeonData.OnValidate()). Editor-only, n'existe pas en build : mapID reste le " +
             "champ réellement lu au runtime (SceneLoader.LoadMapWithSpawn compare des noms de " +
             "scène en string, jamais une référence SceneAsset).")]
    public SceneAsset mapScene;
#endif
    // Caché de l'Inspector — mapScene le pilote entièrement (OnValidate). Reste un champ public
    // normal : SceneName/SceneLoader le lisent tel quel au runtime, HideInInspector ne change
    // rien à la sérialisation ni à l'accès par code.
    [HideInInspector]
    public string mapID;
    [Tooltip("Nom affiché à l'entrée de la salle.")]
    public string displayName;
    [Tooltip("Couloir = mobRespawn/respawnDelay s'appliquent. BossRoom = jamais de respawn de " +
             "mob (à part une future compétence d'invocation, pas encore implémentée).")]
    public RoomType roomType;

    [Tooltip("Boss de cette salle — RÉFÉRENCE/CHECKLIST, pas lu par le code. L'enforcement réel " +
             "vient du MobType/DungeonRole de ce MobData (voir Mob.isDungeonBoss) sur l'instance " +
             "placée en scène.")]
    public MobData bossMob;
}

[CreateAssetMenu(fileName = "dgn_", menuName = "AetherTree/Contenu/DungeonData")]
public class DungeonData : ScriptableObject, IInstanceConfig
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    [Tooltip("Identifiant unique — snake_case, immuable.")]
    public string dungeonID = "";
    public string displayName = "";
    [Tooltip("Classic = Donjon Classique · Unlock = Donjon de Déblocage · FactionPvP = plus tard.")]
    public DungeonType dungeonType = DungeonType.Classic;
    public int levelMin = 1;

    [Tooltip("Palier de CE donjon — deux usages : (1) si dungeonType = Unlock, ajouté à " +
             "Player.unlockedTiers à la mort du boss (voir InstanceSession.OnBossKilled()) ; " +
             "(2) dans TOUS les cas, utilisé quand les vies du groupe sont épuisées pour choisir " +
             "le checkpoint de respawn (Player.ResolveCheckpoint) — un Classique de palier 2 " +
             "renvoie à la ville du palier 2 si elle a été atteinte, sinon remonte la chaîne.")]
    public int palier = 1;

    // ── Accès ─────────────────────────────────────────────────
    [Header("Accès")]
    [Tooltip("Capacité max — Classique : 15 (voir GDD §14.2.3). Sans effet observable en solo.")]
    public int maxPlayers = 15;
    [Tooltip("ConsumableData (consumableType = DungeonKey) requis pour entrer — résolu en sens " +
             "inverse par DungeonRegistry.ResolveByKey() quand ce consommable est utilisé, voir " +
             "ConsoBarUI.TryUseSlot().")]
    public ConsumableData requiredKey;

    // ── Structure ─────────────────────────────────────────────
    [Header("Structure")]
    [Tooltip("Salles dans l'ordre — 1 seule pour un donjon direct-boss.")]
    public List<DungeonMapData> maps = new List<DungeonMapData>();

    // ── Règles de mort — voir InstanceSession.OnPlayerDeath() ──
    [Header("Règles de mort")]
    [Tooltip("Vies individuelles par joueur. Classique : 2 (défaut). Déblocage : 1.")]
    public int livesPerPlayer = 2;
    [Tooltip("Vies communes du donjon : morts totales (tous joueurs) avant l'échec pour tout le " +
             "monde — 0 = illimité. Chaque mort retire aussi une vie au joueur concerné. Le donjon " +
             "échoue dès que ce total est atteint OU que plus aucun joueur n'a de vie. En solo, " +
             "livesPerPlayer s'épuise avant ce total, il ne devient déterminant qu'à partir de " +
             "3 joueurs (avec 2 vies chacun).")]
    public int deathLimit = 5;

    [Tooltip("Délai (secondes) entre une mort avec vie(s) restante(s) et le retour au point " +
             "d'entrée de la salle. Volontairement long — punit la mort et pénalise le groupe.")]
    public float respawnDelay = 30f;
    [Tooltip("Délai (secondes) de la fenêtre de grâce après la DERNIÈRE vie perdue — si le boss " +
             "meurt pendant ce temps, le run bascule en Succès au lieu d'expulser ce joueur les " +
             "mains vides. Volontairement court — ce joueur ne participe plus au run.")]
    public float deathExpulsionDelay = 10f;
    [Tooltip("Délai (secondes) entre un Succès (boss tué) et l'expulsion — GDD §14.2.4 : " +
             "\"expulsion automatique après 15 secondes\".")]
    public float successExitDelay = 15f;

    // ── Récompenses ───────────────────────────────────────────
    [Header("Récompenses")]
    [Tooltip("Coffre (ConsumableType.RewardChest) donné à chaque survivant à la mort du boss.")]
    public ConsumableData bossChest;
    [Tooltip("Prestige donné au(x) survivant(s) à la mort du boss — une fois par donjon par jour " +
             "réel (voir Player.TryClaimDailyDungeonPrestige, appelé par InstanceSession.OnBossKilled).")]
    public int prestigeReward = 100;

    // ── IInstanceConfig ───────────────────────────────────────
    public string InstanceID     => dungeonID;
    public string DisplayName    => displayName;
    public string SceneName      => maps.Count > 0 ? maps[0].mapID : null;
    public string EntryItemID    => requiredKey != null ? requiredKey.itemID : null;
    public int    LivesPerPlayer      => livesPerPlayer;
    public int    DeathLimit          => deathLimit;
    public float  RespawnDelay        => respawnDelay;
    public float  DeathExpulsionDelay => deathExpulsionDelay;
    public float  SuccessExitDelay    => successExitDelay;

#if UNITY_EDITOR
    // Synchronise mapID depuis mapScene (SceneAsset, editor-only) à chaque édition dans
    // l'Inspector — évite un mapID tapé/copié à la main qui diverge silencieusement du vrai
    // nom de scène après un renommage/déplacement de fichier .unity.
    private void OnValidate()
    {
        foreach (var map in maps)
        {
            if (map.mapScene != null) map.mapID = map.mapScene.name;

            if (map.bossMob != null &&
                (map.bossMob.mobType != MobType.DungeonMob || map.bossMob.dungeonRole != DungeonRole.Boss))
                Debug.LogWarning($"[DungeonData] {name} : {map.bossMob.mobName} a mobType = " +
                    $"{map.bossMob.mobType}/dungeonRole = {map.bossMob.dungeonRole}, attendu " +
                    "DungeonMob + DungeonRole.Boss pour bossMob.");
        }
    }
#endif
}
