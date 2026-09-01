using UnityEngine;

// =============================================================
// SHOWIFATTRIBUTE.CS — Masque un champ Inspector selon la valeur
// d'un champ frère (enum ou bool).
// Path : Assets/Scripts/Utils/ShowIfAttribute.cs
//
// Usage :
//   [ShowIf(nameof(buffType), BuffType.Stats)]
//   public StatModifierType buffStatType;
//
//   [ShowIf(nameof(action), DialogueAction.AcceptQuest, DialogueAction.TurnInQuest)]
//   public QuestData questData;
//
//   [ShowIf(nameof(isCapturable), true)]
//   public PetType petType;
//
//   // Header conditionnel — remplace un [Header] classique sur le PREMIER champ
//   // d'un groupe : un [Header] normal reste toujours affiché même si le champ
//   // qui suit est masqué (c'est un décorateur indépendant). Passer Header ici
//   // le fait dessiner par CE drawer, donc seulement quand le champ est visible.
//   [ShowIf(nameof(buffType), BuffType.Heal, Header = "Soin instantané (Heal)")]
//   public ModifierType healModifier;
//
// Le champ n'est affiché que si la valeur du champ "conditionField" (même
// classe, même profondeur) correspond à l'une des valeurs listées. Rend
// visuel ce que les tooltips "Utilisé par : X" du projet disaient déjà —
// voir Editor/ShowIfPropertyDrawer.cs pour le rendu (Editor-only).
//
// order = -1 : force Unity à choisir CE drawer plutôt qu'un autre attribut de
// dessin (Range, Min...) posé sur le même champ — un seul PropertyDrawer
// "gagne" par champ quand plusieurs sont empilés, et order plus bas gagne.
// Le drawer redessine lui-même le slider Range/Min s'il en détecte un sur le
// champ, donc rien n'est perdu visuellement en combinant les deux.
// =============================================================

[System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
public class ShowIfAttribute : PropertyAttribute
{
    public readonly string   conditionField;
    public readonly object[] values;

    /// <summary>Optionnel — texte de header à dessiner UNIQUEMENT quand le champ
    /// est visible (remplace un [Header] classique, toujours affiché sinon).</summary>
    public string Header;

    public ShowIfAttribute(string conditionField, params object[] values)
    {
        this.conditionField = conditionField;
        this.values         = values;
        order = -1;
    }
}
