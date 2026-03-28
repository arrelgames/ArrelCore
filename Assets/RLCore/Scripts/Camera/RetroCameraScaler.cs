using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class RetroCameraScaler : MonoBehaviour
{
    public enum ResolutionPreset
    {
        Original,   // Full native resolution
        PS0,        // 160x120
        PS1,        // 320x240
        PS2,        // 640x480
        ModernRetro // 960x540
    }

    [Header("Retro Resolution Settings")]
    [Tooltip("When off, the camera renders to the screen like a normal camera and the RawImage is cleared and disabled.")]
    public bool retroScalingEnabled = true;
    public ResolutionPreset resolutionPreset = ResolutionPreset.PS1;
    public bool applyOnStart = true;

    [Header("UI Output (Full-Screen RawImage)")]
    public RawImage targetRawImage;

    [HideInInspector]
    public Camera cam;

    private RenderTexture renderTexture;
    private ResolutionPreset lastAppliedPreset = ResolutionPreset.PS1;
    private bool lastAppliedRetroScalingEnabled;
    private bool hasAppliedOnce;

    void Awake()
    {
        cam = GetComponent<Camera>();
        TryResolveTargetRawImage();
    }

#if UNITY_EDITOR
    void OnEnable()
    {
        RefreshEditModePresentation();
    }

    /// <summary>
    /// Hides the presentation RawImage in edit mode so the Game view is not tinted by the placeholder RT.
    /// </summary>
    void RefreshEditModePresentation()
    {
        if (Application.isPlaying)
            return;

        TryResolveTargetRawImage();
        if (targetRawImage == null)
            return;

        targetRawImage.enabled = false;
        if (retroScalingEnabled)
            ApplyRawImageFullscreenLayout();
    }
#endif

    void Start()
    {
        TryResolveTargetRawImage();
        if (applyOnStart)
            ApplyPreset(true);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        ApplyPreset(true);
    }
#endif

    /// <summary>
    /// If <see cref="targetRawImage"/> is unset, finds a <see cref="RawImage"/> under the parent
    /// transform (typical retro rig) or under this transform.
    /// </summary>
    public RawImage TryResolveTargetRawImage()
    {
        if (targetRawImage != null)
            return targetRawImage;

        Transform searchRoot = transform.parent != null ? transform.parent : transform;
        targetRawImage = searchRoot.GetComponentInChildren<RawImage>(true);
        return targetRawImage;
    }

    void ApplyRawImageFullscreenLayout()
    {
        if (targetRawImage == null)
            return;

        RectTransform rt = targetRawImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Pixel size of the drawable area for the presentation canvas (matches Game view for overlay UI).
    /// Using <see cref="Screen"/> alone can diverge from the canvas and skew RT aspect vs the RawImage quad.
    /// </summary>
    void GetPresentationPixelDimensions(out int pixelW, out int pixelH)
    {
        TryResolveTargetRawImage();
        if (targetRawImage != null && targetRawImage.canvas != null)
        {
            Rect pr = targetRawImage.canvas.rootCanvas.pixelRect;
            pixelW = Mathf.Max(1, Mathf.RoundToInt(pr.width));
            pixelH = Mathf.Max(1, Mathf.RoundToInt(pr.height));
            if (pixelW <= 1 || pixelH <= 1)
            {
                pixelW = Mathf.Max(1, Screen.width);
                pixelH = Mathf.Max(1, Screen.height);
            }

            return;
        }

        pixelW = Mathf.Max(1, Screen.width);
        pixelH = Mathf.Max(1, Screen.height);
    }

    /// <summary>
    /// Applies the current resolution preset, or tears down to a normal camera when <see cref="retroScalingEnabled"/> is false.
    /// </summary>
    public void ApplyPreset(bool forceUpdate = false)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            RefreshEditModePresentation();
            return;
        }
#endif

        if (!retroScalingEnabled)
        {
            if (forceUpdate || (hasAppliedOnce && lastAppliedRetroScalingEnabled))
                ApplyDisabledState();
            lastAppliedRetroScalingEnabled = false;
            hasAppliedOnce = true;
            return;
        }

        if (!forceUpdate && hasAppliedOnce && lastAppliedRetroScalingEnabled && lastAppliedPreset == resolutionPreset)
            return;

        GetPresentationPixelDimensions(out int outW, out int outH);

        Vector2Int baseRes;

        if (resolutionPreset == ResolutionPreset.Original)
            baseRes = new Vector2Int(outW, outH);
        else
            baseRes = GetBaseResolution(resolutionPreset);

        float outputAspectRatio = outH > 0 ? (float)outW / outH : 1f;
        int targetWidth = Mathf.RoundToInt(baseRes.y * outputAspectRatio);
        int targetHeight = baseRes.y;

        if (targetWidth > outW || targetHeight > outH)
        {
            float scale = Mathf.Min(
                (float)outW / targetWidth,
                (float)outH / targetHeight
            );

            targetWidth = Mathf.RoundToInt(targetWidth * scale);
            targetHeight = Mathf.RoundToInt(targetHeight * scale);
        }

        hasAppliedOnce = true;
        lastAppliedRetroScalingEnabled = true;
        ApplyResolution(targetWidth, targetHeight);
        lastAppliedPreset = resolutionPreset;
    }

    void ApplyDisabledState()
    {
        if (cam == null)
            cam = GetComponent<Camera>();

        TryResolveTargetRawImage();

        if (renderTexture != null)
        {
            if (cam.targetTexture == renderTexture)
                cam.targetTexture = null;

            renderTexture.Release();
            Destroy(renderTexture);
            renderTexture = null;
        }
        else
        {
            cam.targetTexture = null;
        }

        if (targetRawImage != null)
        {
            targetRawImage.texture = null;
            targetRawImage.enabled = false;
        }
    }

    private Vector2Int GetBaseResolution(ResolutionPreset preset)
    {
        switch (preset)
        {
            case ResolutionPreset.PS0: return new Vector2Int(160, 120);
            case ResolutionPreset.PS1: return new Vector2Int(320, 240);
            case ResolutionPreset.PS2: return new Vector2Int(640, 480);
            case ResolutionPreset.ModernRetro: return new Vector2Int(960, 540);
            default: return new Vector2Int(640, 480);
        }
    }

    private void ApplyResolution(int width, int height)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return;
#endif

        if (cam == null)
            cam = GetComponent<Camera>();

        TryResolveTargetRawImage();

        if (renderTexture != null)
        {
            if (cam.targetTexture == renderTexture)
                cam.targetTexture = null;

            renderTexture.Release();
            Destroy(renderTexture);
        }

        renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
        {
            filterMode = FilterMode.Point,
            useMipMap = false,
            name = $"RetroRT_{width}x{height}"
        };
        renderTexture.Create();

        cam.targetTexture = renderTexture;

        if (targetRawImage != null)
        {
            targetRawImage.enabled = true;
            targetRawImage.texture = renderTexture;
            ApplyRawImageFullscreenLayout();
            targetRawImage.SetAllDirty();
        }

        Debug.Log($"[RetroCameraScaler] Resolution applied: {width}x{height} ({resolutionPreset})");
    }
}
