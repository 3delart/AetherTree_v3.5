using UnityEngine;
using System.Collections.Generic;

// =============================================================
// KEYBINDINGS — Source unique de toutes les touches du jeu
// AetherTree GDD v21
// Sauvegarde via PlayerPrefs — persistant entre les sessions
// =============================================================
public static class KeyBindings
{
    private static readonly Dictionary<string, KeyCode> defaults = new Dictionary<string, KeyCode>
    {
        // Skills actifs
        { "Slot1",  KeyCode.Alpha1 },
        { "Slot2",  KeyCode.Alpha2 },
        { "Slot3",  KeyCode.Alpha3 },
        { "Slot4",  KeyCode.Alpha4 },
        { "Slot5",  KeyCode.Alpha5 },
        { "Slot6",  KeyCode.Alpha6 },
        { "Slot7",  KeyCode.Alpha7 },
        { "Slot8",  KeyCode.Alpha8 },
        { "Slot9",  KeyCode.Alpha9 },

        // Ultime & Consommables
        { "Ultime", KeyCode.R  },
        { "Conso1", KeyCode.F1 },
        { "Conso2", KeyCode.F2 },
        { "Conso3", KeyCode.F3 },

        // Déplacement
        { "Move",         KeyCode.Mouse1 },
        { "CameraRotate", KeyCode.Mouse1 }, // + LeftAlt

        // Ciblage
        { "Target",   KeyCode.Mouse0 },
        { "Deselect", KeyCode.Escape },

        // Interfaces
        { "OpenSkillLibrary", KeyCode.K },
        { "OpenInventory",    KeyCode.I },
        { "OpenCharacter",    KeyCode.C },
        { "OpenMap",          KeyCode.M },
        { "OpenQuests",       KeyCode.J },
        { "OpenSettings",     KeyCode.O },
        { "OpenSocial",       KeyCode.L },
        { "OpenMail",        KeyCode.U },
        { "OpenChat",        KeyCode.V },
        { "OpenGuild",       KeyCode.G },
        { "OpenRecipe",       KeyCode.N },
        { "OpenStatPoints",   KeyCode.S },

        // Interaction
        { "Interact",         KeyCode.F   },
        { "AutoAttackToggle", KeyCode.Tab },
    };

    private static Dictionary<string, KeyCode> current = null;

    // ── Chargement ────────────────────────────────────────────
    public static void Load()
    {
        current = new Dictionary<string, KeyCode>();
        foreach (var kvp in defaults)
        {
            string saved = PlayerPrefs.GetString($"Key_{kvp.Key}", "");
            if (!string.IsNullOrEmpty(saved) && System.Enum.TryParse(saved, out KeyCode k))
                current[kvp.Key] = k;
            else
                current[kvp.Key] = kvp.Value;
        }
    }

    // ── Sauvegarde ────────────────────────────────────────────
    public static void Save()
    {
        if (current == null) return;
        foreach (var kvp in current)
            PlayerPrefs.SetString($"Key_{kvp.Key}", kvp.Value.ToString());
        PlayerPrefs.Save();
    }

    public static void ResetToDefaults()
    {
        current = new Dictionary<string, KeyCode>(defaults);
        Save();
    }

    // ── Getter / Setter ───────────────────────────────────────
    public static KeyCode Get(string action)
    {
        if (current == null) Load();
        return current.TryGetValue(action, out KeyCode k) ? k : KeyCode.None;
    }

    public static void Set(string action, KeyCode key)
    {
        if (current == null) Load();
        current[action] = key;
    }

    public static string GetConflict(string action, KeyCode key)
    {
        if (current == null) Load();
        foreach (var kvp in current)
            if (kvp.Key != action && kvp.Value == key) return kvp.Key;
        return null;
    }

    // ── Helpers Input ─────────────────────────────────────────
    public static bool GetDown(string action) => Input.GetKeyDown(Get(action));
    public static bool GetHeld(string action) => Input.GetKey(Get(action));
    public static bool GetUp(string action)   => Input.GetKeyUp(Get(action));

    public static IEnumerable<string> AllActions => defaults.Keys;

    // ── Nom lisible pour l'UI Paramètres ─────────────────────
    public static string GetLabel(string action) => action switch
    {
        "Slot1"  => "Skill 1", "Slot2" => "Skill 2", "Slot3" => "Skill 3",
        "Slot4"  => "Skill 4", "Slot5" => "Skill 5", "Slot6" => "Skill 6",
        "Slot7"  => "Skill 7", "Slot8" => "Skill 8", "Slot9" => "Skill 9",
        "Ultime" => "Ultime",
        "Conso1" => "Consommable 1", "Conso2" => "Consommable 2", "Conso3" => "Consommable 3",
        "Move"             => "Déplacement",
        "Target"           => "Cibler",
        "Deselect"         => "Désélectionner",
        "OpenSkillLibrary" => "Bibliothèque de Skills",
        "OpenInventory"    => "Inventaire",
        "OpenCharacter"    => "Personnage",
        "OpenMap"          => "Carte",
        "OpenQuests"       => "Quêtes",
        "OpenSettings"     => "Paramètres",
        "OpenSocial"       => "Social",
        "OpenMail"         => "Messagerie",
        "OpenChat"         => "Chat",
        "OpenGuild"        => "Guilde",
        "OpenRecipe"       => "Recettes",
        "OpenStatPoints"   => "Points de Stats",
        "Interact"         => "Interagir",
        "AutoAttackToggle" => "Auto-Attaque",
        _ => action
    };
}
