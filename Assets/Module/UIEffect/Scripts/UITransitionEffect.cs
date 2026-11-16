using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
using System;
using System.Collections;

namespace Coffee.UIEffects
{
    /// <summary>
    /// Transition effect.
    /// </summary>
    [AddComponentMenu("UI/UIEffects/UITransitionEffect", 5)]
    public class UITransitionEffect : BaseMaterialEffect
    {
        /// <summary>
        /// Effect mode.
        /// </summary>
        public enum EffectMode
        {
            Fade = 1,
            Cutoff = 2,
            Dissolve = 3,
        }

        private const uint k_ShaderId = 5 << 3;
        private static readonly int k_TransitionTexId = Shader.PropertyToID("_TransitionTex");
        private static readonly int k_EffectFactorId = Shader.PropertyToID("_EffectFactor");
        private static readonly int k_DissolveWidthId = Shader.PropertyToID("_DissolveWidth");
        private static readonly int k_DissolveSoftnessId = Shader.PropertyToID("_DissolveSoftness");
        private static readonly int k_DissolveColorId = Shader.PropertyToID("_DissolveColor");
        private static readonly int k_AlphaTexId = Shader.PropertyToID("_AlphaTex");
        private static readonly int k_UseSplitAlphaId = Shader.PropertyToID("_UseSplitAlpha");
        private static readonly ParameterTexture s_ParamTex = new ParameterTexture(8, 128, "_ParamTex");

        private bool _lastKeepAspectRatio;
        private bool _holdHidden;
        private int _playbackVersion;
        private float _playbackProgress;
        private Action _onPlaybackComplete;
        private static Texture _defaultTransitionTexture;
        private static Shader _directShader;
        private static bool _searchedDirectShader;
        private Material _directMaterial;
        private float? _scriptFactor;

        [Tooltip("Effect mode.")] [SerializeField]
        EffectMode m_EffectMode = EffectMode.Cutoff;

        [Tooltip("Effect factor between 0(hidden) and 1(shown).")] [SerializeField] [Range(0, 1)]
        float m_EffectFactor = 0.5f;

        [Tooltip("Transition texture (single channel texture).")] [SerializeField]
        Texture m_TransitionTexture;

        [Header("Advanced Option")] [Tooltip("The area for effect.")] [SerializeField]
        EffectArea m_EffectArea = EffectArea.RectTransform;

        [Tooltip("Keep effect aspect ratio.")] [SerializeField]
        bool m_KeepAspectRatio;

        [Tooltip("Dissolve edge width.")] [SerializeField] [Range(0, 1)]
        float m_DissolveWidth = 0.5f;

        [Tooltip("Dissolve edge softness.")] [SerializeField] [Range(0, 1)]
        float m_DissolveSoftness = 0.5f;

        [Tooltip("Dissolve edge color.")] [SerializeField] [ColorUsage(false)]
        Color m_DissolveColor = new Color(0.0f, 0.25f, 1.0f);

        [Tooltip("Disable the graphic's raycast target on hidden.")] [SerializeField]
        bool m_PassRayOnHidden;

        [Tooltip("Camera for this Graphic's root Screen Space - Camera Canvas. Leave empty to use the Canvas setting.")]
        public Camera m_RenderCamera;

        [Header("Effect Player")] [SerializeField]
        EffectPlayer m_Player;


        /// <summary>
        /// Visibility factor from 0 (hidden) to 1 (shown).
        /// </summary>
        public float effectFactor
        {
            get { return m_EffectFactor; }
            set
            {
                if (value > 0f) _holdHidden = false;
                SetEffectFactor(value);
            }
        }

        /// <summary>
        /// Transition texture.
        /// </summary>
        public Texture transitionTexture
        {
            get
            {
                return m_TransitionTexture
                    ? m_TransitionTexture
                    : defaultTransitionTexture;
            }
            set
            {
                if (m_TransitionTexture == value) return;
                m_TransitionTexture = value;
                SetMaterialDirty();
                if (m_KeepAspectRatio) SetVerticesDirty();
            }
        }

        private static Texture defaultTransitionTexture
        {
            get
            {
                return _defaultTransitionTexture
                    ? _defaultTransitionTexture
                    : (_defaultTransitionTexture = Resources.Load<Texture>("Default-Transition"));
            }
        }

        /// <summary>
        /// Effect mode.
        /// </summary>
        public EffectMode effectMode
        {
            get { return m_EffectMode; }
            set
            {
                if (m_EffectMode == value) return;
                m_EffectMode = value;
                SetMaterialDirty();
                SetVerticesDirty();
                SetEffectParamsDirty();
            }
        }

        /// <summary>
        /// Keep aspect ratio.
        /// </summary>
        public bool keepAspectRatio
        {
            get { return m_KeepAspectRatio; }
            set
            {
                if (m_KeepAspectRatio == value) return;
                m_KeepAspectRatio = value;
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// Gets the parameter texture.
        /// </summary>
        public override ParameterTexture paramTex
        {
            get { return s_ParamTex; }
        }

        /// <summary>
        /// Dissolve edge width.
        /// </summary>
        public float dissolveWidth
        {
            get { return m_DissolveWidth; }
            set
            {
                value = Mathf.Clamp(value, 0, 1);
                if (Mathf.Approximately(m_DissolveWidth, value)) return;
                m_DissolveWidth = value;
                SetEffectParamsDirty();
            }
        }

        /// <summary>
        /// Dissolve edge softness.
        /// </summary>
        public float dissolveSoftness
        {
            get { return m_DissolveSoftness; }
            set
            {
                value = Mathf.Clamp(value, 0, 1);
                if (Mathf.Approximately(m_DissolveSoftness, value)) return;
                m_DissolveSoftness = value;
                SetEffectParamsDirty();
            }
        }

        /// <summary>
        /// Dissolve edge color.
        /// </summary>
        public Color dissolveColor
        {
            get { return m_DissolveColor; }
            set
            {
                if (m_DissolveColor == value) return;
                m_DissolveColor = value;
                SetEffectParamsDirty();
            }
        }

        /// <summary>
        /// Disable graphic's raycast target on hidden.
        /// </summary>
        public bool passRayOnHidden
        {
            get { return m_PassRayOnHidden; }
            set { m_PassRayOnHidden = value; }
        }

        public EffectPlayer effectPlayer
        {
            get { return m_Player ?? (m_Player = new EffectPlayer()); }
        }

        /// <summary>
        /// Restore full visibility and invalidate the current playback.
        /// </summary>
        public void ResetEffectFactor()
        {
            BindRenderCamera();
            CancelPlayback();
            _holdHidden = false;
            _scriptFactor = 1f;
            if (isActiveAndEnabled) SetEffectFactor(1f);
            else m_EffectFactor = 1f;
        }

        /// <summary>
        /// Show transition.
        /// </summary>
        public void Show(bool reset = true)
        {
            Show(null, reset);
        }

        /// <summary>
        /// Invoke onComplete once after the effect reaches full visibility.
        /// </summary>
        public void Show(Action onComplete, bool reset = true)
        {
            PlayTransition(false, reset, onComplete);
        }

        /// <summary>
        /// Hide transition.
        /// </summary>
        public void Hide(bool reset = true)
        {
            Hide(null, reset);
        }

        /// <summary>
        /// Invoke onComplete once after the effect reaches and holds zero.
        /// </summary>
        public void Hide(Action onComplete, bool reset = true)
        {
            if (_holdHidden)
            {
                onComplete?.Invoke();
                return;
            }

            PlayTransition(true, reset, onComplete);
        }

        private void PlayTransition(bool hide, bool reset, Action onComplete)
        {
            BindRenderCamera();
            CancelPlayback();
            _holdHidden = false;
            _playbackProgress = 0f;
            _scriptFactor = m_EffectFactor;
            _onPlaybackComplete = onComplete;
            int version = _playbackVersion;
            effectPlayer.loop = false;
            effectPlayer.Play(reset, progress => UpdatePlayback(version, hide, progress));
        }

        private void BindRenderCamera()
        {
            if (!isActiveAndEnabled || m_RenderCamera == null) return;
            Graphic targetGraphic = graphic;
            if (targetGraphic == null || targetGraphic.canvas == null) return;

            Canvas rootCanvas = targetGraphic.canvas.rootCanvas;
            if (rootCanvas == null || rootCanvas.renderMode != RenderMode.ScreenSpaceCamera) return;
            if (rootCanvas.worldCamera != m_RenderCamera) rootCanvas.worldCamera = m_RenderCamera;
        }

        private void UpdatePlayback(int version, bool hide, float progress)
        {
            if (version != _playbackVersion) return;
            progress = Mathf.Clamp01(progress);

            // Some player versions return to the first sample after a one-shot.
            if (progress >= 1f || progress < _playbackProgress)
            {
                _holdHidden = hide;
                SetEffectFactor(hide ? 0f : 1f);
                Action onComplete = _onPlaybackComplete;
                CancelPlayback();
                if (onComplete != null) StartCoroutine(InvokeCompletion(_playbackVersion, onComplete));
                return;
            }

            _playbackProgress = progress;
            SetEffectFactor(hide ? 1f - progress : progress);
        }

        private IEnumerator InvokeCompletion(int version, Action onComplete)
        {
            // Let the player finish its update before callbacks start another transition.
            yield return null;
            if (version == _playbackVersion) onComplete();
        }

        private void CancelPlayback()
        {
            _playbackVersion = unchecked(_playbackVersion + 1);
            _onPlaybackComplete = null;
        }

        private void SetEffectFactor(float value)
        {
            value = Mathf.Clamp01(value);
            if (_scriptFactor.HasValue) _scriptFactor = value;
            if (m_EffectFactor == value) return;
            m_EffectFactor = value;
            SetEffectParamsDirty();
        }


        private bool UseDirectDissolve(Graphic target)
        {
            if (m_EffectMode != EffectMode.Dissolve || !(target is Image || target is RawImage)) return false;
            if (!_searchedDirectShader)
            {
                _directShader = Resources.Load<Shader>("LoadingPageDissolve");
                _searchedDirectShader = true;
            }

            return _directShader != null && _directShader.isSupported;
        }

        private void ApplyDirectMaterial(Material material)
        {
            if (material == null) return;

            Texture mask = transitionTexture;
            Image image = graphic as Image;
            Sprite sprite = image != null ? image.overrideSprite : null;
            if (sprite == null && image != null) sprite = image.sprite;
            Texture splitAlpha = sprite != null ? sprite.associatedAlphaSplitTexture : null;
            material.SetTexture(k_TransitionTexId, mask != null ? mask : Texture2D.whiteTexture);
            material.SetTexture(k_AlphaTexId, splitAlpha != null ? splitAlpha : Texture2D.whiteTexture);
            material.SetFloat(k_UseSplitAlphaId, splitAlpha != null ? 1f : 0f);
            material.SetFloat(k_EffectFactorId, m_EffectFactor);
            material.SetFloat(k_DissolveWidthId, m_DissolveWidth);
            material.SetFloat(k_DissolveSoftnessId, m_DissolveSoftness);
            material.SetColor(k_DissolveColorId, m_DissolveColor);
        }

        private static void EnableDissolveUVs(Canvas canvas)
        {
            if (canvas == null) return;
            AdditionalCanvasShaderChannels channels = canvas.additionalShaderChannels;
            if ((channels & AdditionalCanvasShaderChannels.TexCoord1) == 0) canvas.additionalShaderChannels = channels | AdditionalCanvasShaderChannels.TexCoord1;
        }

        public override Hash128 GetMaterialHash(Material material)
        {
            if (!isActiveAndEnabled || !material || !material.shader)
                return k_InvalidHash;

            uint shaderVariantId = (uint)((int)m_EffectMode << 6);
            Texture mask = transitionTexture;
            uint resourceId = mask != null ? (uint)mask.GetInstanceID() : 0u;
            uint effectId = UseDirectDissolve(graphic) ? (uint)GetInstanceID() : 0u;
            return new Hash128((uint)material.GetInstanceID(), k_ShaderId + shaderVariantId, resourceId, effectId);
        }

        public override void ModifyMaterial(Material newMaterial, Graphic graphic)
        {
            if (UseDirectDissolve(graphic))
            {
                newMaterial.shader = _directShader;
                _directMaterial = newMaterial;
                ApplyDirectMaterial(_directMaterial);
                return;
            }

            newMaterial.shader = Shader.Find(string.Format("Hidden/{0} (UITransition)", newMaterial.shader.name));
            SetShaderVariants(newMaterial, m_EffectMode);

            newMaterial.SetTexture(k_TransitionTexId, transitionTexture);
            paramTex.RegisterMaterial(newMaterial);
        }

        /// <summary>
        /// Modifies the mesh.
        /// </summary>
        public override void ModifyMesh(VertexHelper vh, Graphic graphic)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            Texture tex = transitionTexture;
            float aspectRatio = m_KeepAspectRatio && tex ? (float)tex.width / tex.height : -1f;
            Rect rect = m_EffectArea.GetEffectArea(vh, rectTransform.rect, aspectRatio);
            bool directDissolve = UseDirectDissolve(graphic);
            if (directDissolve)
            {
                EnableDissolveUVs(graphic.canvas);
                if (graphic.canvas != null) EnableDissolveUVs(graphic.canvas.rootCanvas);
                ApplyDirectMaterial(_directMaterial);
            }

            float normalizedIndex = directDissolve ? 0f : paramTex.GetNormalizedIndex(this);

            // Set parameters to vertex.
            var vertex = default(UIVertex);
            var count = vh.currentVertCount;
            for (var i = 0; i < count; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                float x;
                float y;
                if (directDissolve)
                {
                    x = (vertex.position.x - rect.xMin) / Mathf.Max(rect.width, 0.001f);
                    y = (vertex.position.y - rect.yMin) / Mathf.Max(rect.height, 0.001f);
                    // Reserve zero for renderers without a secondary UV channel.
                    connector.SetExtraChannel(ref vertex, new Vector2(0.25f + 0.5f * x, 0.25f + 0.5f * y));
                }
                else
                {
                    connector.GetPositionFactor(m_EffectArea, i, rect, vertex.position, out x, out y);
                    vertex.uv0 = new Vector2(Packer.ToFloat(vertex.uv0.x, vertex.uv0.y), Packer.ToFloat(x, y, normalizedIndex));
                }
                vh.SetUIVertex(vertex, i);
            }
        }

        /// <summary>
        /// This function is called when the object becomes enabled and active.
        /// </summary>
        protected override void OnEnable()
        {
            if (_holdHidden) m_EffectFactor = 0f;
            base.OnEnable();
            BindRenderCamera();
            effectPlayer.OnEnable(null);
            effectPlayer.loop = false;
        }

        /// <summary>
        /// This function is called when the behaviour becomes disabled () or inactive.
        /// </summary>
        protected override void OnDisable()
        {
            CancelPlayback();
            base.OnDisable();
            _directMaterial = null;
            effectPlayer.OnDisable();
        }

        protected override void SetEffectParamsDirty()
        {
            if (UseDirectDissolve(graphic))
            {
                ApplyDirectMaterial(_directMaterial);
            }
            else
            {
                paramTex.SetData(this, 0, m_EffectFactor);
                if (m_EffectMode == EffectMode.Dissolve)
                {
                    paramTex.SetData(this, 1, m_DissolveWidth);
                    paramTex.SetData(this, 2, m_DissolveSoftness);
                    paramTex.SetData(this, 4, m_DissolveColor.r);
                    paramTex.SetData(this, 5, m_DissolveColor.g);
                    paramTex.SetData(this, 6, m_DissolveColor.b);
                }
            }

            // Disable graphic's raycastTarget on hidden.
            if (m_PassRayOnHidden)
            {
                graphic.raycastTarget = 0 < m_EffectFactor;
            }
        }

        protected override void SetVerticesDirty()
        {
            base.SetVerticesDirty();

            _lastKeepAspectRatio = m_KeepAspectRatio;
        }

        protected override void OnDidApplyAnimationProperties()
        {
            // Animator writes the serialized field directly. Preserve script playback once started.
            if (_holdHidden) m_EffectFactor = 0f;
            else if (_scriptFactor.HasValue) m_EffectFactor = _scriptFactor.Value;
            base.OnDidApplyAnimationProperties();

            if (_lastKeepAspectRatio != m_KeepAspectRatio)
                SetVerticesDirty();
        }
    }
}
