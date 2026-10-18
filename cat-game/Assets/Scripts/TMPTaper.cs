using UnityEngine;
using TMPro;

/// <summary>
/// TMP_Text'in bir ucunu daraltýr. Perspektif / Jet Set Radio tipi tipografi efekti.
///
/// NEDEN BU VERSIYON KASMAZ:
///  - _applying flag'i ile reentrancy guard. ForceMeshUpdate TMP'nin
///    TEXT_CHANGED_EVENT'ini fire ettiði için callback kendi update'imizi
///    yeniden tetiklerdi -> sonsuz döngü -> editor kilitleniyor.
///  - OnValidate direkt Apply çaðýrmýyor; EditorApplication.delayCall ile
///    sonraki editor frame'ine erteliyor. Böylece ExecuteAlways'in agresif
///    OnValidate çaðrýlarýyla patlamýyor.
///
/// Setup:
///  - TMP_Text objesinin yanýna ekle.
///  - startScale (ilk karakter boyu) ve endScale (son karakter boyu) ayarla.
///  - direction: hangi uca doðru daralsýn.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
[ExecuteAlways]
public class TMPTaper : MonoBehaviour
{
    public enum Direction { Right, Left, Center }

    [Header("Taper shape")]
    [Range(0.1f, 2f)] public float startScale = 1f;
    [Range(0.1f, 2f)] public float endScale = 0.5f;
    public Direction direction = Direction.Right;

    private TMP_Text _text;
    private bool _applying;    // reentrancy guard — kendi mesh update'imiz callback'i tekrar tetiklemesin
    private bool _subscribed;

    private void OnEnable()
    {
        _text = GetComponent<TMP_Text>();
        Subscribe();
        Apply();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
        _subscribed = false;
    }

    private void OnValidate()
    {
        // OnValidate'ten Apply'ý DOÐRUDAN çaðýrma — ExecuteAlways + TEXT_CHANGED_EVENT
        // kombinasyonuyla sonsuz döngüye girer ve editor kilitlenir.
        // Bunun yerine delayCall ile editor'ün bir sonraki boþ frame'ine ertele.
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += DeferredValidate;
#endif
    }

#if UNITY_EDITOR
    private void DeferredValidate()
    {
        UnityEditor.EditorApplication.delayCall -= DeferredValidate;
        if (this == null) return;
        if (!isActiveAndEnabled) return;
        _text = GetComponent<TMP_Text>();
        Apply();
    }
#endif

    private void OnTextChanged(Object obj)
    {
        // Guard: eðer kendi Apply'ýmýz ForceMeshUpdate yaptýðý için fire olduysa, atla.
        if (_applying) return;
        if (obj == _text) Apply();
    }

    private void Apply()
    {
        if (_applying || _text == null) return;

        _applying = true;
        try
        {
            _text.ForceMeshUpdate();
            var textInfo = _text.textInfo;
            if (textInfo == null || textInfo.characterCount == 0) return;

            int charCount = textInfo.characterCount;

            for (int i = 0; i < charCount; i++)
            {
                var charInfo = textInfo.characterInfo[i];
                if (!charInfo.isVisible) continue;

                float t = charCount > 1 ? (float)i / (charCount - 1) : 0f;

                float scale;
                switch (direction)
                {
                    case Direction.Right:
                        scale = Mathf.Lerp(startScale, endScale, t);
                        break;
                    case Direction.Left:
                        scale = Mathf.Lerp(endScale, startScale, t);
                        break;
                    case Direction.Center:
                        float dist = Mathf.Abs(t - 0.5f) * 2f;
                        scale = Mathf.Lerp(startScale, endScale, dist);
                        break;
                    default:
                        scale = 1f;
                        break;
                }

                int materialIndex = charInfo.materialReferenceIndex;
                int vertexIndex = charInfo.vertexIndex;
                var verts = textInfo.meshInfo[materialIndex].vertices;

                Vector3 center = (verts[vertexIndex + 0] + verts[vertexIndex + 2]) * 0.5f;

                for (int v = 0; v < 4; v++)
                {
                    Vector3 offset = verts[vertexIndex + v] - center;
                    offset *= scale;
                    verts[vertexIndex + v] = center + offset;
                }
            }

            for (int m = 0; m < textInfo.meshInfo.Length; m++)
            {
                var meshInfo = textInfo.meshInfo[m];
                meshInfo.mesh.vertices = meshInfo.vertices;
                _text.UpdateGeometry(meshInfo.mesh, m);
            }
        }
        finally
        {
            _applying = false;
        }
    }
}