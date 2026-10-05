using UnityEngine;

// =============================================================
// CLICKMOVEMARKER.CS — Feedback visuel au clic de déplacement
// Path : Assets/Scripts/Utils/ClickMoveMarker.cs
//
// Petit disque au sol qui rétrécit et s'efface au point cliqué — juice
// gratuit sur le clic-déplacement existant (PlayerController.HandleClick,
// AnyRPG research item #9, demande Florian 2026-10-03). Pas de prefab —
// construit entièrement en code (SpriteRenderer sur un GameObject jetable),
// Spawn() prend sprite/couleur/taille/durée en paramètre pour rester
// configurable depuis l'Inspector de PlayerController sans toucher ce
// fichier.
// =============================================================
public class ClickMoveMarker : MonoBehaviour
{
    private float          _duration;
    private float          _elapsed;
    private SpriteRenderer _renderer;
    private Vector3        _baseScale;

    /// <summary>Instancie un marqueur jetable à `position`, rétrécit+s'efface sur `duration`
    /// secondes puis se détruit. No-op si `sprite` est null (feedback optionnel).</summary>
    public static void Spawn(Vector3 position, Sprite sprite, Color color, float worldSize, float duration)
    {
        if (sprite == null) return;

        var go = new GameObject("ClickMoveMarker");
        // +0.02 pour éviter le Z-fighting avec le sol ; rotation à plat (le sprite est face
        // caméra par défaut, il doit regarder vers le haut pour se poser au sol).
        go.transform.position = position + Vector3.up * 0.02f;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color  = color;

        var marker = go.AddComponent<ClickMoveMarker>();
        marker._renderer  = sr;
        marker._duration  = Mathf.Max(0.01f, duration);
        marker._baseScale = Vector3.one * worldSize;
        go.transform.localScale = marker._baseScale;
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / _duration);

        transform.localScale = _baseScale * (1f - t);
        if (_renderer != null)
        {
            Color c = _renderer.color;
            c.a = 1f - t;
            _renderer.color = c;
        }

        if (t >= 1f) Destroy(gameObject);
    }
}
