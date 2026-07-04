using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 器官切换插槽。
///
/// 功能：
/// 1. 根据场景器官当前实际应用的 Sprite，显示对应 UI 图片；
/// 2. 鼠标仅悬停 Slot 区域时改变 Slot 颜色；
/// 3. 鼠标移动到 SubMenu 时，Slot 恢复 normalColor；
/// 4. 子菜单打开期间保持器官 Hover 描边；
/// 5. 点击 Hand / Eye / Foot 切换目标器官类型；
/// 6. 没有可用器官或找不到映射时显示空 UI。
/// </summary>
public class OrganSwitchSlot :
    MonoBehaviour,
    IPointerClickHandler,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerMoveHandler
{
    /// <summary>
    /// 场景器官 Sprite 与 UI 邮票 Sprite 的映射。
    ///
    /// 嵌套在 MonoBehaviour 内，避免 Unity 将其识别为独立组件。
    /// </summary>
    [Serializable]
    private struct OrganSpriteUIBinding
    {
        [Tooltip("场景中器官实际使用的 Sprite。")]
        public Sprite organSprite;

        [Tooltip("该器官 Sprite 对应的 UI 图片。")]
        public Sprite uiSprite;
    }

    private static readonly int HoverOutlineEnabledId =
        Shader.PropertyToID("_HoverOutlineEnabled");

    private static readonly int HoverOutlineColorId =
        Shader.PropertyToID("_HoverOutlineColor");

    /// <summary>
    /// 当前打开子菜单的插槽。
    /// 所有实例共享，保证同时只有一个子菜单打开。
    /// </summary>
    private static OrganSwitchSlot currentOpenSlot;

    // ─────────── 目标器官 ───────────

    [Header("绑定")]

    [Tooltip("该插槽对应的场景器官。为空时显示空 UI。")]
    [SerializeField]
    private OrganUnit targetOrgan;

    [Tooltip(
        "目标器官真正负责显示器官图片的 SpriteRenderer。" +
        "为空时会从 targetOrgan 子物体中自动查找。"
    )]
    [SerializeField]
    private SpriteRenderer targetOrganRenderer;

    // ─────────── UI Sprite 映射 ───────────

    [Header("器官 Sprite 对应 UI")]

    [Tooltip(
        "没有器官、器官无 Sprite，或找不到映射时显示的空 UI 图片。" +
        "如果此字段为空，则会禁用 slotImage。"
    )]
    [SerializeField]
    private Sprite emptyUISprite;

    [Tooltip(
        "场景器官 Sprite 与 UI 图片的映射关系。" +
        "左右手、左右脚、左右眼、心脏分别配置。"
    )]
    [SerializeField]
    private OrganSpriteUIBinding[] organSpriteUIBindings;

    // ─────────── 悬停检测 ───────────

    [Header("悬停检测")]

    [Tooltip(
        "只有鼠标真正位于该区域时，Slot 才使用 hoverColor。" +
        "请拖入 Slot 图片自身的 RectTransform，" +
        "不要拖入包含 SubMenu 的父节点。"
    )]
    [SerializeField]
    private RectTransform slotHoverRect;

    // ─────────── 器官描边 ───────────

    [Header("器官描边")]

    [Tooltip(
        "需要控制 Hover 描边的 SpriteRenderer。" +
        "为空时自动收集 targetOrgan 下的全部 SpriteRenderer。"
    )]
    [SerializeField]
    private SpriteRenderer[] outlineRenderers;

    [Tooltip("鼠标悬停 Slot 或子菜单打开时使用的描边颜色。")]
    [SerializeField]
    private Color hoverOutlineColor =
        Color.yellow;

    // ─────────── 子菜单 ───────────

    [Header("子菜单")]

    [SerializeField]
    private GameObject subMenuPanel;

    [SerializeField]
    private Button handButton;

    [SerializeField]
    private Button eyeButton;

    [SerializeField]
    private Button footButton;

    // ─────────── UI 反馈 ───────────

    [Header("UI 反馈")]

    [Tooltip("显示器官邮票图片的 Image。")]
    [SerializeField]
    private Image slotImage;

    [Tooltip("鼠标真正悬停 Slot 区域时的颜色。")]
    [SerializeField]
    private Color hoverColor =
        new Color(1f, 1f, 1f, 0.9f);

    [Tooltip("鼠标不在 Slot 区域时的颜色。")]
    [SerializeField]
    private Color normalColor =
        new Color(1f, 1f, 1f, 0.5f);

    // ─────────── 公开属性 ───────────

    public OrganUnit TargetOrgan =>
        targetOrgan;

    public SpriteRenderer TargetOrganRenderer =>
        targetOrganRenderer;

    // ─────────── 运行时状态 ───────────

    private OrganController controller;
    private MaterialPropertyBlock propertyBlock;

    /// <summary>
    /// 当前子菜单是否打开。
    /// </summary>
    private bool isSubMenuOpen;

    /// <summary>
    /// 鼠标是否真正位于 slotHoverRect 内。
    ///
    /// 不是简单依赖整个父节点的 PointerEnter，
    /// 因此鼠标进入 SubMenu 时不会继续保持 Slot Hover 颜色。
    /// </summary>
    private bool isPointerInsideSlot;

    // ─────────── Unity 生命周期 ───────────

    private void Awake()
    {
        propertyBlock =
            new MaterialPropertyBlock();

        /*
         * 默认没有手动指定悬停区域时，
         * 使用 slotImage 自身的 RectTransform。
         */
        if (slotHoverRect == null &&
            slotImage != null)
        {
            slotHoverRect =
                slotImage.rectTransform;
        }

        CollectTargetOrganRenderer();
        CollectOutlineRenderers();

        SetOutline(false);
        RefreshSlotColor();
    }

    private void Start()
    {
        controller =
            GameBootstrap.Instance?.OrganController;

        if (subMenuPanel != null)
            subMenuPanel.SetActive(false);

        if (handButton != null)
        {
            handButton.onClick.AddListener(
                SwitchToHand
            );
        }

        if (eyeButton != null)
        {
            eyeButton.onClick.AddListener(
                SwitchToEye
            );
        }

        if (footButton != null)
        {
            footButton.onClick.AddListener(
                SwitchToFoot
            );
        }

        isSubMenuOpen = false;
        isPointerInsideSlot = false;

        RefreshSlotColor();
        SetOutline(false);

        /*
         * 根据场景器官当前实际应用的 Sprite 初始化 UI。
         */
        RefreshSlotImageFromObjectSprite();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying)
            return;

        /*
         * UI 重新启用时刷新一次，
         * 防止 UI 禁用期间器官 Sprite 已发生改变。
         */
        RefreshSlotColor();
        RefreshSlotImageFromObjectSprite();
        RefreshOutlineState();
    }

    private void OnDisable()
    {
        isPointerInsideSlot = false;
        isSubMenuOpen = false;

        if (subMenuPanel != null)
            subMenuPanel.SetActive(false);

        RefreshSlotColor();
        SetOutline(false);

        if (currentOpenSlot == this)
            currentOpenSlot = null;
    }

    private void OnDestroy()
    {
        if (handButton != null)
        {
            handButton.onClick.RemoveListener(
                SwitchToHand
            );
        }

        if (eyeButton != null)
        {
            eyeButton.onClick.RemoveListener(
                SwitchToEye
            );
        }

        if (footButton != null)
        {
            footButton.onClick.RemoveListener(
                SwitchToFoot
            );
        }

        SetOutline(false);

        if (currentOpenSlot == this)
            currentOpenSlot = null;
    }

#if UNITY_EDITOR

    private void OnValidate()
    {
        if (slotHoverRect == null &&
            slotImage != null)
        {
            slotHoverRect =
                slotImage.rectTransform;
        }

        if (targetOrgan == null)
        {
            targetOrganRenderer = null;
            outlineRenderers =
                Array.Empty<SpriteRenderer>();
        }

        if (!Application.isPlaying)
        {
            RefreshSlotColor();
            RefreshSlotImageFromObjectSprite();
        }
    }

#endif

    // ─────────── 鼠标事件 ───────────

    public void OnPointerClick(
        PointerEventData eventData)
    {
        if (eventData.button !=
            PointerEventData.InputButton.Left)
        {
            return;
        }

        /*
         * 只有点击真正的 Slot 区域才打开或关闭菜单。
         *
         * 点击 SubMenu 按钮时，
         * 事件可能冒泡到父节点，因此这里必须再次检查位置。
         */
        if (!IsPointerInsideSlot(eventData))
            return;

        if (!HasAvailableTargetOrgan())
        {
            CloseSubMenu();
            RefreshSlotImageFromObjectSprite();
            return;
        }

        if (isSubMenuOpen)
            CloseSubMenu();
        else
            OpenSubMenu();
    }

    public void OnPointerEnter(
        PointerEventData eventData)
    {
        RefreshPointerState(eventData);
    }

    public void OnPointerMove(
        PointerEventData eventData)
    {
        RefreshPointerState(eventData);
    }

    public void OnPointerExit(
        PointerEventData eventData)
    {
        /*
         * PointerExit 可能只是从 Slot 父节点移动到 SubMenu 子节点，
         * 因此不能直接设为 false，而是继续根据屏幕坐标判断。
         */
        RefreshPointerState(eventData);
    }

    /// <summary>
    /// 根据当前鼠标屏幕位置，
    /// 判断鼠标是否真正位于 Slot 区域。
    /// </summary>
    private void RefreshPointerState(
        PointerEventData eventData)
    {
        isPointerInsideSlot =
            IsPointerInsideSlot(eventData);

        RefreshSlotColor();
        RefreshOutlineState();
    }

    /// <summary>
    /// 判断事件中的鼠标位置是否位于 slotHoverRect 内。
    /// </summary>
    private bool IsPointerInsideSlot(
        PointerEventData eventData)
    {
        if (slotHoverRect == null ||
            eventData == null)
        {
            return false;
        }

        Camera eventCamera =
            eventData.enterEventCamera;

        /*
         * Overlay Canvas 下 eventCamera 通常为 null，
         * RectangleContainsScreenPoint 可以正常处理。
         */
        return RectTransformUtility
            .RectangleContainsScreenPoint(
                slotHoverRect,
                eventData.position,
                eventCamera
            );
    }

    // ─────────── Slot 颜色 ───────────

    /// <summary>
    /// 只有鼠标真正位于 Slot 区域时使用 hoverColor。
    ///
    /// 鼠标移动到 SubMenu 上时，
    /// isPointerInsideSlot 会变为 false，
    /// 因此恢复 normalColor。
    /// </summary>
    private void RefreshSlotColor()
    {
        if (slotImage == null)
            return;

        slotImage.color =
            isPointerInsideSlot
                ? hoverColor
                : normalColor;
    }

    // ─────────── 子菜单 ───────────

    private void OpenSubMenu()
    {
        if (subMenuPanel == null ||
            !HasAvailableTargetOrgan())
        {
            return;
        }

        if (currentOpenSlot != null &&
            currentOpenSlot != this)
        {
            currentOpenSlot.CloseSubMenu();
        }

        subMenuPanel.SetActive(true);

        isSubMenuOpen = true;
        currentOpenSlot = this;

        /*
         * Slot 颜色只由真实鼠标位置决定，
         * 子菜单打开本身不会影响 Slot 颜色。
         */
        RefreshSlotColor();

        /*
         * 子菜单打开期间保持器官描边。
         */
        RefreshOutlineState();

        HighlightCurrentType();
    }

    private void CloseSubMenu()
    {
        if (subMenuPanel != null)
            subMenuPanel.SetActive(false);

        isSubMenuOpen = false;

        if (currentOpenSlot == this)
            currentOpenSlot = null;

        RefreshSlotColor();
        RefreshOutlineState();
    }

    /// <summary>
    /// 器官描边开启条件：
    /// 1. 鼠标真正位于 Slot；
    /// 2. 子菜单当前已打开。
    ///
    /// 因此鼠标进入 SubMenu 后：
    /// Slot 颜色恢复 normalColor，
    /// 但器官描边仍然保留。
    /// </summary>
    private void RefreshOutlineState()
    {
        bool shouldEnable =
            HasAvailableTargetOrgan() &&
            (
                isPointerInsideSlot ||
                isSubMenuOpen
            );

        SetOutline(shouldEnable);
    }

    // ─────────── 器官有效性 ───────────

    /// <summary>
    /// 当前是否存在可用目标器官。
    /// </summary>
    private bool HasAvailableTargetOrgan()
    {
        return targetOrgan != null &&
               targetOrgan.gameObject.activeInHierarchy;
    }

    // ─────────── Renderer 收集 ───────────

    /// <summary>
    /// 自动查找目标器官中用于显示实际器官 Sprite 的 Renderer。
    ///
    /// 如果器官包含多个 SpriteRenderer，
    /// 推荐在 Inspector 中手动指定，
    /// 避免自动选中阴影、特效或辅助 Renderer。
    /// </summary>
    private void CollectTargetOrganRenderer()
    {
        if (targetOrganRenderer != null)
            return;

        if (targetOrgan == null)
            return;

        targetOrganRenderer =
            targetOrgan.GetComponentInChildren<SpriteRenderer>(
                includeInactive: true
            );
    }

    /// <summary>
    /// 自动收集目标器官下全部 SpriteRenderer，
    /// 用于控制 Hover 描边。
    /// </summary>
    private void CollectOutlineRenderers()
    {
        if (outlineRenderers != null &&
            outlineRenderers.Length > 0)
        {
            return;
        }

        if (targetOrgan == null)
        {
            outlineRenderers =
                Array.Empty<SpriteRenderer>();

            return;
        }

        outlineRenderers =
            targetOrgan.GetComponentsInChildren<SpriteRenderer>(
                includeInactive: true
            );
    }

    /// <summary>
    /// 重新收集器官 Renderer 并刷新 UI。
    ///
    /// 兼容切换器官时动态创建或销毁 Renderer 的情况。
    /// </summary>
    public void RefreshOrganRenderers()
    {
        if (targetOrgan == null)
        {
            targetOrganRenderer = null;

            outlineRenderers =
                Array.Empty<SpriteRenderer>();

            RefreshSlotImageFromObjectSprite();
            RefreshOutlineState();

            return;
        }

        /*
         * 如果当前主 Renderer 不存在，
         * 或已经不属于目标器官，则重新查找。
         */
        if (targetOrganRenderer == null ||
            (
                targetOrganRenderer.transform !=
                    targetOrgan.transform &&
                !targetOrganRenderer.transform.IsChildOf(
                    targetOrgan.transform
                )
            ))
        {
            targetOrganRenderer =
                targetOrgan.GetComponentInChildren<SpriteRenderer>(
                    includeInactive: true
                );
        }

        outlineRenderers =
            targetOrgan.GetComponentsInChildren<SpriteRenderer>(
                includeInactive: true
            );

        RefreshSlotImageFromObjectSprite();
        RefreshOutlineState();
    }

    /// <summary>
    /// 保留旧接口名，兼容已有外部调用。
    /// </summary>
    public void RefreshOutlineRenderers()
    {
        RefreshOrganRenderers();
    }

    // ─────────── UI Sprite 刷新 ───────────

    /// <summary>
    /// 根据场景器官当前实际使用的 Sprite，
    /// 查找并应用对应的 UI Sprite。
    ///
    /// 以下情况显示空 UI：
    /// 1. 没有目标器官；
    /// 2. 目标器官未激活；
    /// 3. 找不到 SpriteRenderer；
    /// 4. Obj 当前 Sprite 为空；
    /// 5. 映射表中没有对应项；
    /// 6. 对应 UI Sprite 为空。
    /// </summary>
    public void RefreshSlotImageFromObjectSprite()
    {
        if (slotImage == null)
            return;

        if (!HasAvailableTargetOrgan())
        {
            SetEmptySlotImage();
            return;
        }

        if (targetOrganRenderer == null)
            CollectTargetOrganRenderer();

        if (targetOrganRenderer == null ||
            targetOrganRenderer.sprite == null)
        {
            SetEmptySlotImage();
            return;
        }

        Sprite currentObjectSprite =
            targetOrganRenderer.sprite;

        if (organSpriteUIBindings == null ||
            organSpriteUIBindings.Length == 0)
        {
            SetEmptySlotImage();
            return;
        }

        foreach (OrganSpriteUIBinding binding
                 in organSpriteUIBindings)
        {
            if (binding.organSprite !=
                currentObjectSprite)
            {
                continue;
            }

            Sprite targetUISprite =
                binding.uiSprite;

            if (targetUISprite == null)
            {
                SetEmptySlotImage();
                return;
            }

            slotImage.sprite =
                targetUISprite;

            slotImage.enabled = true;

            return;
        }

        /*
         * Obj 当前 Sprite 没有配置对应 UI。
         */
        SetEmptySlotImage();
    }

    /// <summary>
    /// 显示空 UI。
    ///
    /// 如果 emptyUISprite 为空，
    /// 则关闭 Image 组件显示。
    /// </summary>
    private void SetEmptySlotImage()
    {
        if (slotImage == null)
            return;

        slotImage.sprite =
            emptyUISprite;

        slotImage.enabled =
            emptyUISprite != null;
    }

    // ─────────── 描边 ───────────

    /// <summary>
    /// 设置 Hover 描边。
    ///
    /// 只修改：
    /// _HoverOutlineEnabled
    /// _HoverOutlineColor
    ///
    /// 不修改 MapGrid 使用的活动器官描边参数。
    /// </summary>
    public void SetOutline(
        bool enabled)
    {
        if (propertyBlock == null ||
            outlineRenderers == null)
        {
            return;
        }

        foreach (SpriteRenderer spriteRenderer
                 in outlineRenderers)
        {
            if (spriteRenderer == null)
                continue;

            Material sharedMaterial =
                spriteRenderer.sharedMaterial;

            if (sharedMaterial == null)
                continue;

            if (!sharedMaterial.HasProperty(
                    HoverOutlineEnabledId))
            {
                continue;
            }

            /*
             * 读取现有 PropertyBlock，
             * 保留 Active 描边及其他逐 Renderer 参数。
             */
            propertyBlock.Clear();

            spriteRenderer.GetPropertyBlock(
                propertyBlock
            );

            propertyBlock.SetFloat(
                HoverOutlineEnabledId,
                enabled ? 1f : 0f
            );

            if (sharedMaterial.HasProperty(
                    HoverOutlineColorId))
            {
                propertyBlock.SetColor(
                    HoverOutlineColorId,
                    hoverOutlineColor
                );
            }

            spriteRenderer.SetPropertyBlock(
                propertyBlock
            );
        }

        propertyBlock.Clear();
    }

    // ─────────── 器官类型切换 ───────────

    private void SwitchToHand()
    {
        SwitchTo(OrganType.Hand);
    }

    private void SwitchToEye()
    {
        SwitchTo(OrganType.Eye);
    }

    private void SwitchToFoot()
    {
        SwitchTo(OrganType.Foot);
    }

    private void SwitchTo(
        OrganType type)
    {
        if (controller == null)
        {
            controller =
                GameBootstrap.Instance?.OrganController;
        }

        if (controller == null ||
            !HasAvailableTargetOrgan())
        {
            SetEmptySlotImage();
            CloseSubMenu();

            return;
        }

        bool switched =
            controller.SwitchOrganType(
                targetOrgan,
                type
            );

        if (!switched)
            return;

        /*
         * SwitchOrganType 内部已经更新 Obj 的 Sprite。
         * 此处重新读取 Obj 最终实际应用的 Sprite，
         * 再根据映射表刷新 UI。
         */
        RefreshOrganRenderers();
        RefreshSlotImageFromObjectSprite();

        CloseSubMenu();
    }

    // ─────────── 按钮状态 ───────────

    private void HighlightCurrentType()
    {
        if (!HasAvailableTargetOrgan())
            return;

        OrganType current =
            targetOrgan.OrganType;

        SetButtonAlpha(
            handButton,
            current == OrganType.Hand
                ? 1f
                : 0.5f
        );

        SetButtonAlpha(
            eyeButton,
            current == OrganType.Eye
                ? 1f
                : 0.5f
        );

        SetButtonAlpha(
            footButton,
            current == OrganType.Foot
                ? 1f
                : 0.5f
        );
    }

    private static void SetButtonAlpha(
        Button button,
        float alpha)
    {
        if (button == null)
            return;

        Graphic graphic =
            button.targetGraphic;

        if (graphic == null)
            return;

        Color color =
            graphic.color;

        color.a = alpha;

        graphic.color = color;
    }

    // ─────────── 外部接口 ───────────

    /// <summary>
    /// 运行时设置新的目标器官。
    ///
    /// 传入 null 时显示空 UI。
    /// </summary>
    public void SetTargetOrgan(
        OrganUnit organ)
    {
        SetOutline(false);

        targetOrgan = organ;
        targetOrganRenderer = null;

        outlineRenderers =
            Array.Empty<SpriteRenderer>();

        if (targetOrgan == null)
        {
            CloseSubMenu();
            SetEmptySlotImage();
            return;
        }

        CollectTargetOrganRenderer();
        CollectOutlineRenderers();

        RefreshSlotImageFromObjectSprite();
        RefreshOutlineState();
    }

    /// <summary>
    /// 外部系统在器官 Sprite 改变后调用，
    /// 用于同步刷新 Renderer、UI 图片和描边状态。
    /// </summary>
    public void NotifyOrganSpriteChanged()
    {
        RefreshOrganRenderers();
    }
}