using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================
// MAPSPAWNPOINT.CS — Point d'apparition posé dans une map
// Path : Assets/Scripts/Events/MapSpawnPoint.cs
//
// À poser n'importe où dans une scène de map (GameObject vide + ce composant, ou le prefab
// Prefab_MapSpawnPoint — menu AetherTree > Créer Prefab MapSpawnPoint). Porte toujours un
// SphereCollider (trigger, ajouté automatiquement) — utilisé uniquement par le type Palier
// (voir plus bas), mais présent sur tous pour rester un seul et même prefab réutilisable.
//
//   type = Palier : point de contrôle ville/monde ouvert, PUREMENT documentaire/visuel (couleur du
//     gizmo) — la confirmation réelle du checkpoint ne dépend PAS de ce type, voir OnTriggerEnter.
//   type = Dungeon : point de réapparition d'une salle de donjon. Utilisé automatiquement dès que
//     la scène est chargée (InstanceSession/SceneLoader), aucune confirmation nécessaire — un
//     donjon n'est jamais un lieu de checkpoint permanent.
//   type = Special : waiting room (point d'atterrissage précis à la sortie d'un donjon) ou spawn
//     de debug/téléportation manuelle — même comportement immédiat que Dungeon.
//
// Confirmation de checkpoint (OnTriggerEnter) : ne dépend QUE de MapInfo.checkpointSpawnPoint de
// la scène active — "suis-je LE point que cette scène a explicitement désigné ?", jamais d'un
// champ Type. MapInfo étant posé sur TOUTE scène (y compris salles de donjon/waiting room, pour
// leur palier + leurs biomes), la protection vient de la DISCIPLINE "checkpointSpawnPoint vide
// partout sauf la vraie ville d'un palier" (voir MapInfo.cs), pas d'une garantie structurelle —
// mais un Type mal réglé (valeur par défaut Palier) ne suffit plus À LUI SEUL à causer le bug :
// bug réel rencontré (2026-09-28), un point de donjon resté sur Type = Palier par défaut avait
// enregistré la scène du donjon elle-même comme checkpoint, simplement parce que le Type seul
// suffisait alors à confirmer.
//
//   isDefault = true : LE point utilisé automatiquement pour son (scène, type) — un seul par
//     couple scène/type. FindDefault() ne filtre pas par type (chaque scène ne contient en
//     pratique qu'un seul type pertinent : ville → Palier, salle de donjon → Dungeon, etc.).
//   spawnID : identifiant d'un point nommé, pour les téléportations choisies par le joueur —
//     enregistré mais pas encore consommé (voir FindByID()).
// =============================================================
public enum SpawnPointType
{
    Palier,  // Ville / point de contrôle monde ouvert — doit être ATTEINT (trigger) pour devenir le checkpoint.
    Dungeon, // Réapparition dans une salle de donjon — automatique, aucune confirmation.
    Special, // Waiting room / debug / téléportation manuelle — automatique, aucune confirmation.
}

[RequireComponent(typeof(SphereCollider))]
public class MapSpawnPoint : MonoBehaviour
{
    [Header("Type")]
    [Tooltip("Palier = doit être ATTEINT (marcher dans le trigger) pour devenir le point de " +
             "respawn du joueur en monde ouvert. Dungeon/Special = utilisé automatiquement dès " +
             "que la scène est chargée, sans confirmation.")]
    public SpawnPointType type = SpawnPointType.Palier;

    [Tooltip("LE point utilisé automatiquement pour son (scène, type) — un seul par couple.")]
    public bool isDefault = true;

    [Tooltip("Identifiant du point, unique dans la scène — bouton « Générer » : Spawn_{NomDeLaScène} " +
             "(suffixe _2, _3... si déjà pris) + renomme le GameObject. Sert aux téléportations " +
             "choisies par le joueur ; facultatif pour le point par défaut.")]
    public string spawnID = "";

    private void Awake()
    {
        GetComponent<SphereCollider>().isTrigger = true;
    }

    private void Start()
    {
        if (!isDefault) return;

        int duplicates = 0;
        foreach (var point in FindObjectsOfType<MapSpawnPoint>())
        {
            if (point.isDefault && point.type == type && point.gameObject.scene == gameObject.scene) duplicates++;
        }
        if (duplicates > 1)
            Debug.LogWarning($"[SPAWN] Scène '{gameObject.scene.name}' : {duplicates} MapSpawnPoint par " +
                             $"défaut de type {type} — un seul est attendu, le premier trouvé sera utilisé.", this);
    }

    // Confirme un checkpoint UNIQUEMENT si je suis LE point désigné par le MapInfo de la scène
    // active — jamais basé sur "type", voir le commentaire en tête de fichier. Une scène de
    // donjon n'a pas de MapInfo : MapInfo.Current est alors null, la condition échoue toujours,
    // quel que soit mon Type.
    private void OnTriggerEnter(Collider other)
    {
        var mapInfo = MapInfo.Current;
        if (mapInfo == null || mapInfo.checkpointSpawnPoint != this) return;

        var player = other.GetComponent<Player>();
        if (player == null) return;

        player.ConfirmCheckpoint(mapInfo.palier, gameObject.scene.name);
    }

    /// <summary>Point par défaut de la scène active (la map courante) — null s'il n'y en a pas.
    /// Ne filtre pas par type : chaque scène ne contient en pratique qu'un seul type pertinent.</summary>
    public static MapSpawnPoint FindDefault()
    {
        Scene active = SceneManager.GetActiveScene();
        foreach (var point in FindObjectsOfType<MapSpawnPoint>())
        {
            if (point.isDefault && point.gameObject.scene == active) return point;
        }
        return null;
    }

    /// <summary>Point nommé de la scène active — null si absent. Pas encore appelé : prévu pour
    /// les téléportations choisies par le joueur (fast-travel).</summary>
    public static MapSpawnPoint FindByID(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Scene active = SceneManager.GetActiveScene();
        foreach (var point in FindObjectsOfType<MapSpawnPoint>())
        {
            if (point.spawnID == id && point.gameObject.scene == active) return point;
        }
        return null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = type switch
        {
            SpawnPointType.Palier  => new Color(0.3f, 0.9f, 0.4f, 0.9f),
            SpawnPointType.Dungeon => new Color(0.3f, 0.7f, 1f, 0.9f),
            _                      => new Color(0.85f, 0.5f, 0.95f, 0.9f),
        };
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.5f);
        Gizmos.DrawLine(transform.position + Vector3.up * 1.5f, transform.position + transform.forward * 0.7f + Vector3.up * 1.2f);

        string label = isDefault ? $"{type} (défaut)" : $"{type} : {spawnID}";
        Handles.Label(transform.position + Vector3.up * 1.7f, label);
    }
#endif
}
