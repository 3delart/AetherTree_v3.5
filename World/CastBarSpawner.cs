using UnityEngine;
using UnityEngine.UI;
using TMPro;

// =============================================================
// CASTBARSPAWNER.CS — Barre de canalisation world-space, une instance par
// canalisation active (Mob/PNJ). Path : Assets/Scripts/World/CastBarSpawner.cs
//
// Contrairement à ProgressBarUI (singleton à 1 slot, utilisé par la
// canalisation Player + harvest + craft), plusieurs Mob/PNJ peuvent canaliser
// simultanément — chaque canalisation obtient sa propre instance de ce
// composant, sur un GameObject dédié qui se détruit lui-même en fin de vie.
// Réutilise le MÊME prefab visuel que ProgressBarUI (lu dynamiquement via
// ProgressBarUI.Instance) — aucune nouvelle assignation Inspector requise.
// =============================================================

public class CastBarSpawner : MonoBehaviour
{
    private GameObject      _visual;
    private Image           _fill;
    private Transform       _followTarget;
    private float           _duration;
    private float           _elapsed;
    private System.Action   _onComplete;
    private System.Action   _onCancel;
    private bool            _finished;

    /// <summary>Crée une nouvelle barre de canalisation world-space et la lance. Retourne
    /// l'instance pour permettre un Cancel() externe (interrupt). Ne fait rien de visible si
    /// ProgressBarUI.Instance ou son progressBarPrefab ne sont pas encore assignés — dégrade en
    /// simple minuteur invisible (même comportement que ProgressBarUI.RunProgressNoVisual).</summary>
    public static CastBarSpawner Show(string label, float duration, Transform followTarget,
        System.Action onComplete, System.Action onCancel = null)
    {
        var go      = new GameObject($"CastBar_{followTarget.name}");
        var spawner = go.AddComponent<CastBarSpawner>();
        spawner.Init(label, duration, followTarget, onComplete, onCancel);
        return spawner;
    }

    private void Init(string label, float duration, Transform followTarget,
        System.Action onComplete, System.Action onCancel)
    {
        _followTarget = followTarget;
        _duration     = duration;
        _onComplete   = onComplete;
        _onCancel     = onCancel;

        var progressBarUI = ProgressBarUI.Instance;
        if (progressBarUI == null || progressBarUI.progressBarPrefab == null)
        {
            Debug.LogWarning("[CastBarSpawner] ProgressBarUI.Instance ou son progressBarPrefab " +
                              "non assigné — canalisation sans visuel.", this);
            return;
        }

        Vector3 spawnPos = followTarget.position + Vector3.up * progressBarUI.heightOffset;
        _visual = Instantiate(progressBarUI.progressBarPrefab, spawnPos, Quaternion.identity);

        Transform fillT  = FindChildByName(_visual, "Fill");
        Transform labelT = FindChildByName(_visual, "Label");
        _fill = fillT != null ? fillT.GetComponent<Image>() : null;
        var labelComp    = labelT != null ? labelT.GetComponent<TextMeshProUGUI>() : null;

        if (_fill != null) { _fill.fillAmount = 0f; _fill.color = progressBarUI.colorCast; }
        if (labelComp != null) labelComp.text = label;
    }

    private void Update()
    {
        if (_finished) return;

        if (_followTarget == null) { Cancel(); return; }   // caster détruit sous nos pieds

        if (_visual != null)
        {
            _visual.transform.position = _followTarget.position +
                Vector3.up * (ProgressBarUI.Instance != null ? ProgressBarUI.Instance.heightOffset : 2.5f);
            if (Camera.main != null)
                _visual.transform.forward = Camera.main.transform.forward;
        }

        _elapsed += Time.deltaTime;
        if (_fill != null) _fill.fillAmount = Mathf.Clamp01(_elapsed / _duration);

        if (_elapsed >= _duration)
        {
            _finished = true;
            System.Action complete = _onComplete;
            DestroySelf();
            complete?.Invoke();
        }
    }

    /// <summary>Annule la canalisation avant son terme (interrupt) — appelé par
    /// Mob.InterruptChannelCast()/PNJ.InterruptChannelCast(), jamais par ce composant lui-même
    /// sauf si le followTarget disparaît.</summary>
    public void Cancel()
    {
        if (_finished) return;
        _finished = true;
        System.Action cancel = _onCancel;
        DestroySelf();
        cancel?.Invoke();
    }

    private void DestroySelf()
    {
        if (_visual != null) Destroy(_visual);
        Destroy(gameObject);
    }

    private Transform FindChildByName(GameObject parent, string childName)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return t;
        return null;
    }
}
