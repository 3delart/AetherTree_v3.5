#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

// =============================================================
// DUNGEONMAPDATAPROPERTYDRAWER.CS — Rendu manuel de DungeonMapData
// Path : Assets/Scripts/Editor/DungeonMapDataPropertyDrawer.cs
//
// [ShowIf] (Utils/ShowIfAttribute.cs) marche sur des champs scalaires, mais un vrai drawer dédié
// à TOUTE la classe DungeonMapData reste le plus simple ici pour n'afficher bossMob que sur une
// BossRoom, cohérent avec le reste de l'Inspector.
//
// Pas de mobRespawn/respawnDelay ici — le respawn se règle directement sur chaque Mob placé en
// scène (Mob.respawnEnabled/respawnDelay), pas par salle. Un Objective/Special/Boss ne respawn
// jamais quel que soit ce réglage (garde en code, voir Mob.ShouldRespawn()).
//
// mapID n'est volontairement jamais dessiné ici — il est piloté par mapScene
// (SceneAsset, editor-only) via DungeonData.OnValidate(), rien à éditer à la
// main dessus.
// =============================================================
[CustomPropertyDrawer(typeof(DungeonMapData))]
public class DungeonMapDataPropertyDrawer : PropertyDrawer
{
    private const float Spacing = 2f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight; // ligne de repli
        if (!property.isExpanded) return height;

        height += Spacing + HeightOf(property, "mapScene");
        height += Spacing + HeightOf(property, "displayName");
        height += Spacing + HeightOf(property, "roomType");

        bool isBossRoom = property.FindPropertyRelative("roomType").enumValueIndex == (int)RoomType.BossRoom;
        if (isBossRoom)
            height += Spacing + HeightOf(property, "bossMob");
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var foldoutRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);

        if (property.isExpanded)
        {
            float y = position.y + EditorGUIUtility.singleLineHeight + Spacing;
            EditorGUI.indentLevel++;

            y = Draw(position, y, property, "mapScene");
            y = Draw(position, y, property, "displayName");
            y = Draw(position, y, property, "roomType");

            bool isBossRoom = property.FindPropertyRelative("roomType").enumValueIndex == (int)RoomType.BossRoom;
            if (isBossRoom)
                Draw(position, y, property, "bossMob");

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    private static float HeightOf(SerializedProperty parent, string relativeName)
    {
        var prop = parent.FindPropertyRelative(relativeName);
        return prop != null ? EditorGUI.GetPropertyHeight(prop, true) : 0f;
    }

    private static float Draw(Rect position, float y, SerializedProperty parent, string relativeName)
    {
        var prop = parent.FindPropertyRelative(relativeName);
        if (prop == null) return y;
        float h = EditorGUI.GetPropertyHeight(prop, true);
        EditorGUI.PropertyField(new Rect(position.x, y, position.width, h), prop, true);
        return y + h + Spacing;
    }
}
#endif
