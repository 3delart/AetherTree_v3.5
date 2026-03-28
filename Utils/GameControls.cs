using UnityEngine;

// =============================================================
// GAMECONTROLS — Toutes les actions de jeu
// AetherTree GDD v21
// Utilise KeyBindings comme source unique des touches
// =============================================================
public static class GameControls
{
    // ── Déplacement ───────────────────────────────────────────
    public static bool MoveHeld         => Input.GetMouseButton(1) && !Input.GetKey(KeyCode.LeftAlt);
    public static bool CameraRotateHeld => Input.GetMouseButton(1) &&  Input.GetKey(KeyCode.LeftAlt);

    // ── Ciblage ───────────────────────────────────────────────
    public static bool TargetClick => Input.GetMouseButtonDown(0) && !Input.GetKey(KeyCode.LeftAlt);
    public static bool Deselect    => KeyBindings.GetDown("Deselect");

    // ── Skills ────────────────────────────────────────────────
    public static bool Skill1  => KeyBindings.GetDown("Slot1");
    public static bool Skill2  => KeyBindings.GetDown("Slot2");
    public static bool Skill3  => KeyBindings.GetDown("Slot3");
    public static bool Skill4  => KeyBindings.GetDown("Slot4");
    public static bool Skill5  => KeyBindings.GetDown("Slot5");
    public static bool Skill6  => KeyBindings.GetDown("Slot6");
    public static bool Skill7  => KeyBindings.GetDown("Slot7");
    public static bool Skill8  => KeyBindings.GetDown("Slot8");
    public static bool Skill9  => KeyBindings.GetDown("Slot9");
    public static bool Ultime  => KeyBindings.GetDown("Ultime");
    public static bool Conso1  => KeyBindings.GetDown("Conso1");
    public static bool Conso2  => KeyBindings.GetDown("Conso2");
    public static bool Conso3  => KeyBindings.GetDown("Conso3");

    // ── Interfaces ────────────────────────────────────────────
    public static bool OpenSkillLibrary => KeyBindings.GetDown("OpenSkillLibrary");
    public static bool OpenInventory    => KeyBindings.GetDown("OpenInventory");
    public static bool OpenCharacter    => KeyBindings.GetDown("OpenCharacter");
    public static bool OpenMap          => KeyBindings.GetDown("OpenMap");
    public static bool OpenQuests       => KeyBindings.GetDown("OpenQuests");
    public static bool OpenSettings     => KeyBindings.GetDown("OpenSettings");
    public static bool OpenSocial       => KeyBindings.GetDown("OpenSocial");
    public static bool OpenMail         => KeyBindings.GetDown("OpenMail");
    public static bool OpenChat         => KeyBindings.GetDown("OpenChat");
    public static bool OpenGuild        => KeyBindings.GetDown("OpenGuild");
    public static bool OpenRecipe       => KeyBindings.GetDown("OpenRecipe");
    public static bool OpenStatPoints   => KeyBindings.GetDown("OpenStatPoints");

    // ── Interaction ───────────────────────────────────────────
    public static bool Interact         => KeyBindings.GetDown("Interact");
    public static bool AutoAttackToggle => KeyBindings.GetDown("AutoAttackToggle");

    // ── Utilitaires ───────────────────────────────────────────
    public static int GetSkillSlotPressed()
    {
        if (Skill1) return 0; if (Skill2) return 1; if (Skill3) return 2;
        if (Skill4) return 3; if (Skill5) return 4; if (Skill6) return 5;
        if (Skill7) return 6; if (Skill8) return 7; if (Skill9) return 8;
        if (Ultime) return 9;
        return -1;
    }

    // Alias — compatibilité SkillBar.cs
    public static int GetSkillIndex() => GetSkillSlotPressed();

    public static bool AnyUIToggle =>
        OpenSkillLibrary || OpenInventory || OpenCharacter ||
        OpenMap || OpenQuests || OpenSettings || OpenSocial || OpenRecipe ||
        OpenMail || OpenChat || OpenGuild || OpenStatPoints;
}
