using UnityEngine;

// =============================================================
// Language / LocalizationManager — état global de langue courante
// AetherTree — Système multilangue
//
// Rôle : source de vérité unique pour "quelle langue est active",
// et point de notification pour que tous les panels UI ouverts se
// rafraîchissent quand le joueur change de langue dans les options.
//
// Stockage délégué à PlayerSettings (voir PlayerSettings.cs) —
// LocalizationManager ne gère pas son propre PlayerPrefs, pour
// éviter deux mécanismes de persistance séparés.
//
// Auto-initialisé via [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] —
// pas d'appel manuel requis au bootstrap. Garantit que CurrentLanguage
// est correct dès le premier Awake()/OnEnable() de n'importe quel script
// de la première scène chargée, sans dépendre de l'ordre d'exécution
// entre scripts.
// =============================================================

public enum Language
{
    FR = 0,
    EN = 1,
    // Ajouter ici les futures langues (ES, DE, ...) — voir aussi LocalizedText.cs
}

public static class LocalizationManager
{
    public static Language CurrentLanguage => EnsureLoaded().language;

    /// <summary>
    /// Levé après chaque changement de langue. Tout panel UI affichant du texte
    /// localisé doit s'abonner (OnEnable) / se désabonner (OnDisable) à cet event
    /// et se relire (Refresh()) quand il est levé.
    /// </summary>
    public static event System.Action OnLanguageChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoInit() => EnsureLoaded();

    /// <summary>
    /// Recharge explicitement la langue depuis PlayerSettings. Pas nécessaire au
    /// bootstrap (AutoInit s'en charge) — utile uniquement si un appelant a une
    /// raison explicite de forcer une relecture depuis le disque.
    /// </summary>
    public static void LoadSavedLanguage() => PlayerSettings.Load();

    /// <summary>À appeler depuis le menu Options (dropdown / boutons de langue).</summary>
    public static void SetLanguage(Language newLang)
    {
        var settings = EnsureLoaded();
        if (newLang == settings.language) return; // évite un rafraîchissement inutile de toute l'UI

        settings.language = newLang;
        settings.Save();
        OnLanguageChanged?.Invoke();
    }

    private static PlayerSettings EnsureLoaded()
    {
        if (PlayerSettings.Current == null) PlayerSettings.Load();
        return PlayerSettings.Current;
    }
}
