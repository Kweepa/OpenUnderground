using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Editor-only overlay that displays the current Animator state of the selected object.
/// </summary>
[ExecuteAlways]
public class AnimatorHudOverlay : MonoBehaviour
{
    [Tooltip("Horizontal offset in pixels from screen center.")]
    public float horizontalOffset = 0f;

    [Tooltip("Vertical offset in pixels from screen bottom.")]
    public float verticalOffset = 72f;

    [Tooltip("Optional override target. If null, uses current editor selection.")]
    public Animator overrideAnimator;

    [Tooltip("Font size for overlay text.")]
    public int fontSize = 36;

    [Tooltip("Optional font for overlay text (defaults to skin label font).")]
    public Font font;

    [Tooltip("Padding inside background box.")]
    public Vector2 padding = new Vector2(12f, 6f);

    [Tooltip("Background color for overlay box.")]
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.6f);

    [Tooltip("Text color for overlay.")]
    public Color textColor = Color.white;

#if UNITY_EDITOR
    private GUIStyle _style;
    private Texture2D _backgroundTexture;

    private void OnDisable()
    {
        if (_backgroundTexture != null)
        {
            DestroyImmediate(_backgroundTexture);
            _backgroundTexture = null;
        }
    }

    private void EnsureResources()
    {
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.LowerCenter,
                fontSize = fontSize,
                wordWrap = false,
                normal = { textColor = textColor }
            };
        }
        else
        {
            _style.fontSize = fontSize;
            _style.normal.textColor = textColor;
            _style.wordWrap = false;
        }
        _style.font = font != null ? font : GUI.skin.label.font;

        if (_backgroundTexture == null)
        {
            _backgroundTexture = new Texture2D(1, 1);
            _backgroundTexture.hideFlags = HideFlags.HideAndDontSave;
        }
        _backgroundTexture.SetPixel(0, 0, backgroundColor);
        _backgroundTexture.Apply();
    }

    private void OnGUI()
    {
        if (!enabled)
            return;

        Animator animator = GetTargetAnimator();
        if (animator == null)
            return;

        var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        var clipInfos = animator.GetCurrentAnimatorClipInfo(0);

        string stateName = stateInfo.IsName("") ? "<No State>" : stateInfo.shortNameHash.ToString();
        float timeSeconds = 0f;

        if (clipInfos.Length > 0 && clipInfos[0].clip != null)
        {
            var clip = clipInfos[0].clip;
            stateName = clip.name;
            float normalized = stateInfo.normalizedTime % 1f;
            timeSeconds = clip.length * normalized;
        }

        string timeText = $"{timeSeconds:0.00}s";
        string message = $"{animator.gameObject.name} | {stateName} ({timeText})";

        EnsureResources();
        Vector2 textSize = _style.CalcSize(new GUIContent(message));
        Vector2 totalSize = textSize + new Vector2(padding.x * 2f, padding.y * 2f);

        float x = (Screen.width - totalSize.x) * 0.5f + horizontalOffset;
        float y = Screen.height - totalSize.y - verticalOffset;

        Rect backgroundRect = new Rect(x, y, totalSize.x, totalSize.y);
        GUI.DrawTexture(backgroundRect, _backgroundTexture);

        Rect textRect = new Rect(
            backgroundRect.x + padding.x,
            backgroundRect.y + padding.y,
            textSize.x,
            textSize.y);
        GUI.Label(textRect, message, _style);
    }

    private Animator GetTargetAnimator()
    {
        if (overrideAnimator != null)
            return overrideAnimator;

        GameObject target = Selection.activeGameObject;
        if (target == null)
            return null;

        return target.GetComponentInChildren<Animator>();
    }
#endif
}
