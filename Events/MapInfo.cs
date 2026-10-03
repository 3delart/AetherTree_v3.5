using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

// =============================================================
// MAPINFO.CS — Métadonnées de la scène (monde ouvert)
// Path : Assets/Scripts/Events/MapInfo.cs
//
// À poser sur TOUTE scène (monde ouvert, waiting room, salle de donjon) — sert de fiche
// d'identité : palier + biomes recensés (nom affiché sur la minimap, voir BiomeZone). Palier lu
// par RespawnSystem pour savoir depuis quel palier remonter la chaîne de checkpoints à la mort
// (mort en monde ouvert uniquement — une mort en donjon passe par InstanceSession, qui lit
// DungeonData.palier, pas ce champ).
//
// checkpointSpawnPoint : à laisser VIDE partout sauf sur la VRAIE scène de ville d'un palier.
// MapInfo étant maintenant posé sur toutes les scènes (waiting room, salles de donjon incluses),
// rien n'empêche STRUCTURELLEMENT une salle de donjon de désigner un point par erreur — seule la
// discipline "vide partout sauf en ville" protège du bug (une scène de donjon dont
// checkpointSpawnPoint resterait vide ne peut jamais confirmer de checkpoint, quel que soit le
// Type des MapSpawnPoint qu'elle contient).
// =============================================================
public class MapInfo : MonoBehaviour
{
    [Tooltip("Palier de CETTE scène — Map_01 = 1, etc. Lu par RespawnSystem pour la chaîne de " +
             "checkpoints (monde ouvert uniquement).")]
    public int palier = 1;

    [Tooltip("LE point qui confirme le checkpoint du joueur quand il marche dedans (voir " +
             "MapSpawnPoint.OnTriggerEnter) — référence DIRECTE, pas une recherche par type. " +
             "À LAISSER VIDE sur toute scène qui n'est pas la vraie ville d'un palier (waiting " +
             "room, salle de donjon...) : une scène sans ce champ assigné ne peut jamais confirmer " +
             "de checkpoint, quel que soit le Type des MapSpawnPoint qu'elle contient.")]
    public MapSpawnPoint checkpointSpawnPoint;

    [Tooltip("Recensement des BiomeZone de cette scène — glisser chaque instance ici après " +
             "l'avoir placée (voir Events/BiomeZone.cs). Pure liste de vue d'ensemble, jamais lue " +
             "par le code : chaque BiomeZone fonctionne seul, indépendamment d'être listé ici.")]
    public List<BiomeZone> biomes = new List<BiomeZone>();

    /// <summary>MapInfo de la scène active — null si absente (scène de donjon, waiting room...).</summary>
    public static MapInfo Current
    {
        get
        {
            Scene active = SceneManager.GetActiveScene();
            foreach (var info in FindObjectsOfType<MapInfo>())
                if (info.gameObject.scene == active) return info;
            return null;
        }
    }
}
