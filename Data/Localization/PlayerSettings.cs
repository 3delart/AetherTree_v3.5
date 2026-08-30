using UnityEngine;

// =============================================================
// PlayerSettings — options joueur persistées (langue + futur)
// AetherTree — Settings
//
// Un seul point de persistance pour les options qui ne sont pas
// liées à la progression du personnage (langue, et plus tard :
// volume musique/SFX, affichage, contrôles...).
//
// Chargé au premier accès (via LocalizationManager.EnsureLoaded()
// ou explicitement via Load()), lu/modifié via PlayerSettings.Current,
// sauvegardé à chaque changement (Save()).
//
// Ajouter une option plus tard = ajouter un champ ici + son
// utilisation dans le système concerné. Ne pas ajouter de champs
// spéculatifs pour des options pas encore spécifiées.
// =============================================================

[System.Serializable]
public class PlayerSettings
{
    private const string PrefsKey = "aethertree_settings";

    public Language language = Language.FR;

    // Futurs champs (non spécifiés pour l'instant — volontairement absents
    // tant que le design des options d'affichage/son n'a pas été discuté) :
    // public float musicVolume = 1f;
    // public float sfxVolume   = 1f;
    // public bool  fullscreen  = true;

    /// <summary>Instance courante — null tant que Load() n'a pas été appelé.</summary>
    public static PlayerSettings Current { get; private set; }

    /// <summary>
    /// Charge (ou recharge) Current depuis PlayerPrefs. Rechargement destructif :
    /// écrase Current même s'il contenait déjà des modifications — à n'appeler
    /// que via LocalizationManager (bootstrap automatique) ou explicitement si
    /// une relecture forcée depuis le disque est nécessaire.
    /// </summary>
    public static void Load()
    {
        if (PlayerPrefs.HasKey(PrefsKey))
        {
            try
            {
                Current = JsonUtility.FromJson<PlayerSettings>(PlayerPrefs.GetString(PrefsKey));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PlayerSettings] Échec de lecture des settings sauvegardés, " +
                                  $"valeurs par défaut utilisées : {e.Message}");
            }
        }

        if (Current == null) Current = new PlayerSettings();
    }

    /// <summary>Persiste l'état courant. À appeler après toute modification d'un champ.</summary>
    public void Save()
    {
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
        PlayerPrefs.Save();
    }
}
