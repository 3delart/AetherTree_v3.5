#if UNITY_EDITOR
using UnityEditor;

// =============================================================
// PNJDATAEDITOR.CS — Opt-in de PNJData dans ShowIfEditor (voir son commentaire d'en-tête) pour
// que ses champs List<>/array (skills, availableQuests, onHitReceivedEffects...) respectent
// [ShowIf] correctement, pas juste les champs scalaires.
// Path : Assets/Scripts/Editor/PNJDataEditor.cs
// =============================================================
[CustomEditor(typeof(PNJData))]
public class PNJDataEditor : ShowIfEditor { }
#endif
