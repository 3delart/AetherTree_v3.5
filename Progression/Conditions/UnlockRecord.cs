using UnityEngine;

// =============================================================
// UNLOCKRECORD.CS
// Path : Assets/_Game/Scripts/Progression/Unlock/UnlockRecord.cs
//
// Historique d'un déblocage — créé au moment de l'Unlock().
// Sauvegardé dans character.json (scope Character) ou account.json.
// =============================================================

public class UnlockRecord
{
    public string          conditionID;
    public System.DateTime unlockedAt;
    public int             playerLevel;
    public int             killsTotal;
    public float           timePlayed;

    public string GetDateString()  => unlockedAt.ToString("dd/MM/yyyy HH:mm");
    public string GetLevelString() => $"Niveau {playerLevel}";
    public string GetKillsString() => $"{killsTotal} kills";
    public string GetTimeString()
    {
        int h = Mathf.FloorToInt(timePlayed / 60f);
        int m = Mathf.FloorToInt(timePlayed % 60f);
        return h > 0 ? $"{h}h {m:00}" : $"{m} min";
    }
}
