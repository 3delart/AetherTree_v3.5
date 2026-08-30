using System.Linq;
using UnityEngine;

// =============================================================
// LocalizedText — bloc de texte traduit, réutilisable sur tout SO
// (ItemData et ses sous-types, QuestData, ConditionData, ...)
// AetherTree — Système multilangue
//
// Usage sur un SO (hérité via ItemData.displayName/description,
// ou déclaré directement pour un champ localisé indépendant) :
//   public LocalizedText someText = new LocalizedText();
//
// Lecture au runtime :
//   someText.Get(LocalizationManager.CurrentLanguage)
//
// Ajouter une langue = modifier UNIQUEMENT ce fichier (un champ + le switch
// dans Get()) — aucun autre script de données à toucher.
// =============================================================

[System.Serializable]
public class LocalizedText
{
    [TextArea] public string fr;
    [TextArea] public string en;
    // Ajouter ici les futures langues, ex :
    // [TextArea] public string es;
    // [TextArea] public string de;

    public LocalizedText() { }

    /// <summary>Construction directe utile pour composer un texte à la volée
    /// (ex: préfixe fixe de mail + nom de condition) sans passer par l'inspecteur.</summary>
    public LocalizedText(string fr, string en)
    {
        this.fr = fr;
        this.en = en;
    }

    /// <summary>
    /// Retourne le texte dans la langue demandée.
    /// Fallback sur le FR si la traduction cible est vide (traduction manquante),
    /// pour éviter un texte blanc en jeu plutôt qu'un texte dans la mauvaise langue.
    /// </summary>
    public string Get(Language lang)
    {
        switch (lang)
        {
            case Language.EN:
                return string.IsNullOrEmpty(en) ? Fallback("EN") : en;
            // Ajouter ici les futurs cases, ex :
            // case Language.ES:
            //     return string.IsNullOrEmpty(es) ? Fallback("ES") : es;
            case Language.FR:
            default:
                return fr;
        }
    }

    private string Fallback(string missingLangLabel)
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(fr))
            Debug.LogWarning($"[LocalizedText] Traduction {missingLangLabel} et FR toutes deux vides.");
#endif
        return fr; // FR = langue de référence du projet, toujours la valeur de secours
    }

    /// <summary>True si aucune langue n'a de texte saisi — pratique pour les champs
    /// optionnels (ex: description de condition) qui testaient auparavant
    /// string.IsNullOrEmpty(xxx) sur un string brut.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(fr) && string.IsNullOrEmpty(en);

    /// <summary>
    /// Concatène plusieurs LocalizedText langue par langue (ex: description de
    /// condition + description de reward, pour composer un corps de mail).
    /// Les parts null ou dont le FR est vide sont ignorées, pour reproduire le
    /// comportement "si vide, ne pas ajouter cette partie ni le séparateur".
    /// </summary>
    public static LocalizedText Join(string separator, params LocalizedText[] parts)
    {
        if (parts == null) return new LocalizedText();

        var valid = parts.Where(p => p != null && !string.IsNullOrEmpty(p.fr)).ToList();
        return new LocalizedText
        {
            fr = string.Join(separator, valid.Select(p => p.fr)),
            en = string.Join(separator, valid.Select(p => string.IsNullOrEmpty(p.en) ? p.fr : p.en)),
        };
    }

    /// <summary>
    /// Lecture sûre même si la référence elle-même est null (champ jamais assigné
    /// sur un SO fraîchement créé sans passer par l'Inspector). Préférer
    /// text.Get(lang) quand le champ est garanti non-null (cas normal sur un SO
    /// dont displayName/description ont un défaut "= new LocalizedText()" — voir
    /// ItemData) ; utiliser cette méthode statique aux points d'entrée externes
    /// (ex: donnée venant d'un ancien save, d'un mail restauré) où la garantie
    /// n'existe pas.
    /// </summary>
    public static string GetSafe(LocalizedText text, Language lang, string fallback = "")
        => text?.Get(lang) ?? fallback;
}
