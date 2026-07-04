using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 场景器官 Sprite 与 UI Sprite 的对应关系。
///
/// 例如：
/// 左手场景 Sprite -> 左手邮票 Sprite
/// 右手场景 Sprite -> 右手邮票 Sprite
/// </summary>
[Serializable]
public struct OrganSpriteUIBinding
{
    [Tooltip("场景中 OrganUnit 实际使用的 Sprite。")]
    public Sprite organSprite;

    [Tooltip("该场景 Sprite 对应的 UI 邮票 Sprite。")]
    public Sprite uiSprite;
}

/// <summary>
/// 器官切换 UI。
///
/// 功能：
/// 1. 根据目标器官当前实际使用的 Sprite，显示对应的 UI 图片；
/// 2. 鼠标进入时开启 Hover 描边；
/// 3. 子菜单打开期间保持 Hover 描边；
/// 4. 点击 Hand / Eye / Foot 后切换目标器官类型；
/// 5. 当没有可用器官或找不到 Sprite 映射时显示空 UI。
///
/// UI 不自行判断左手、右手、左脚、右脚等身份，
/// 而是直接根据场景对象当前实际使用的 Sprite 进行匹配。
/// </summary>
public class OrganSwitchSlot :
    MonoBehaviour,
    IPointerClickHandler,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private static readonly int HoverOutlineEnabledId =
        Shader.PropertyToID("_HoverOutlineEnabled");

    private static readonly int HoverOutlineColorId =
        Shader.PropertyToID("_HoverOutlineColor");

    /// <summary>
    /// 当前处于打开状态的插槽。
    /// 所有实例共享该引用，保证同时只有一个子菜单打开。
    /// </summary>
    private static OrganSwitchSlot currentOpenSlot;

    // ─────────── 目标器官 ───────────

    [Header("绑定")]

    [Tooltip("该 UI 插槽对应的场景器官。为空时显示空 UI。")]
    [SerializeField]
    private OrganUnit targetOrgan;

    [Tooltip(
        "目标器官中真正负责显示器官 Sprite 的 SpriteRenderer。" +
        "为空时会自动从 targetOrgan 子物体中查找。"
    )]
    [SerializeField]
    private SpriteRenderer targetOrganRenderer;

    // ─────────── UI Sprite 映射 ───────────

    [Header("器官 Sprite 对应 UI")]

    [Tooltip(
        "无可用器官、器官没有 Sprite，或者找不到对应关系时显示的空邮票。"
    )]
    [SerializeField]
    private Sprite emptyUISprite;

    [Tooltip(
        "场景器官 Sprite 与 UI 邮票 Sprite 的对应关系。" +
        "左右手、左右脚、左右眼分别配置一条映射。"
    )]
    [SerializeField]
    private OrganSpriteUIBinding[] organSpriteUIBindings;

    // ─────────── 器官描边 ───────────

    [Header("器官描边")]

    [Tooltip(
        "需要控制 Hover 描边的 SpriteRenderer。" +
        "为空时自动收集 targetOrgan 下的全部 SpriteRenderer。"
    )]
    [SerializeField]
    private SpriteRenderer[] outlineRenderers;

    [Tooltip("鼠标悬停或子菜单打开时使用的描边颜色。")]
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

    [Tooltip("用于显示邮票图片的 Image。")]
    [SerializeField]
    private Image slotImage;

    [SerializeField]
    private Color hoverColor =
        new Color(1f, 1f, 1f, 0.9f);

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

    private bool isSubMenuOpen;
    private bool isPointerInside;

    // ─────────── Unity 生命周期 ───────────

    private void Awake()
    {
        propertyBlock =
            new MaterialPropertyBlock();

        CollectTargetOrganRenderer();
        CollectOutlineRenderers();

        SetOutline(false);
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

        if (slotImage != null)
            slotImage.color = normalColor;

        SetOutline(false);

        /*
         * 初始化时根据场景对象当前实际使用的 Sprite，
         * 显示对应的 UI 邮票。
         */
        RefreshSlotImageFromObjectSprite();
    }

    private void OnEnable()
    {
        /*
         * 物体重新启用时重新刷新，
         * 避免器官状态在 UI 禁用期间发生变化。
         */
        if (Application.isPlaying)
            RefreshSlotImageFromObjectSprite();
    }

    private void OnDisable()
    {
        isPointerInside = false;
        isSubMenuOpen = false;

        SetOutline(false);

        if (subMenuPanel != null)
            subMenuPanel.SetActive(false);

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
        /*
         * Inspector 中修改目标器官后，
         * 自动清理不属于新目标器官的 Renderer 引用。
         */
        if (targetOrgan == null)
        {
            targetOrganRenderer = null;
            outlineRenderers =
                Array.Empty<SpriteRenderer>();
        }

        if (!Application.isPlaying)
            RefreshSlotImageFromObjectSprite();
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
         * 没有可用器官时不能打开类型切换菜单。
         */
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
        isPointerInside = true;

        if (slotImage != null)
            slotImage.color = hoverColor;

        if (HasAvailableTargetOrgan())
            SetOutline(true);
    }

    public void OnPointerExit(
        PointerEventData eventData)
    {
        isPointerInside = false;

        if (slotImage != null)
            slotImage.color = normalColor;

        RefreshOutlineState();
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

        /*
         * 鼠标仍位于插槽内时保留描边；
         * 鼠标已经离开则关闭描边。
         */
        RefreshOutlineState();
    }

    /// <summary>
    /// Hover 描边在以下任一条件成立时开启：
    /// 1. 鼠标位于插槽上；
    /// 2. 该插槽的子菜单已打开。
    /// </summary>
    private void RefreshOutlineState()
    {
        bool shouldEnable =
            HasAvailableTargetOrgan() &&
            (isPointerInside || isSubMenuOpen);

        SetOutline(shouldEnable);
    }

    // ─────────── 器官有效性 ───────────

    /// <summary>
    /// 判断当前插槽是否拥有可用的目标器官。
    /// </summary>
    private bool HasAvailableTargetOrgan()
    {
        return targetOrgan != null &&
               targetOrgan.gameObject.activeInHierarchy;
    }

    // ─────────── Renderer 收集 ───────────

    /// <summary>
    /// 自动寻找目标器官中用于显示实际器官 Sprite 的 Renderer。
    ///
    /// 如果器官下存在多个 SpriteRenderer，
    /// 推荐在 Inspector 中手动指定，避免自动选择到阴影、特效或辅助 Renderer。
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
    /// 自动收集目标器官下的全部 SpriteRenderer，
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
    /// 重新收集目标器官下的所有相关 Renderer。
    ///
    /// 当器官切换类型时会调用，
    /// 兼容切换过程中动态创建或销毁 Renderer 的情况。
    /// </summary>
    public void RefreshOrganRenderers()
    {
        if (targetOrgan == null)
        {
            targetOrganRenderer = null;
            outlineRenderers =
                Array.Empty<SpriteRenderer>();

            RefreshSlotImageFromObjectSprite();
            return;
        }

        /*
         * 重新寻找主 SpriteRenderer。
         *
         * 若 Inspector 已明确指定主 Renderer，
         * 且该 Renderer 仍然属于当前目标器官，则保留。
         */
        if (targetOrganRenderer == null ||
            !targetOrganRenderer.transform.IsChildOf(
                targetOrgan.transform
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

        RefreshOutlineState();
        RefreshSlotImageFromObjectSprite();
    }

    /// <summary>
    /// 保留旧接口名称，兼容其他脚本可能存在的调用。
    /// </summary>
    public void RefreshOutlineRenderers()
    {
        RefreshOrganRenderers();
    }

    // ─────────── UI Sprite 刷新 ───────────

    /// <summary>
    /// 根据场景中器官当前实际应用的 Sprite，
    /// 查找并显示对应的 UI 邮票 Sprite。
    ///
    /// 以下情况显示 emptyUISprite：
    /// 1. 没有目标器官；
    /// 2. 目标器官未激活；
    /// 3. 找不到负责显示的 SpriteRenderer；
    /// 4. 当前场景 Sprite 为空；
    /// 5. 映射表中没有对应项；
    /// 6. 对应的 UI Sprite 为空。
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

            slotImage.sprite =
                binding.uiSprite != null
                    ? binding.uiSprite
                    : emptyUISprite;

            slotImage.enabled =
                slotImage.sprite != null;

            return;
        }

        /*
         * 当前场景 Sprite 没有配置对应的 UI 图片。
         */
        SetEmptySlotImage();
    }

    /// <summary>
    /// 显示空邮票。
    /// 如果 emptyUISprite 本身为空，则直接隐藏 Image。
    /// </summary>
    private void SetEmptySlotImage()
    {
        if (slotImage == null)
            return;

        slotImage.sprite = emptyUISprite;
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
    /// 不会覆盖 MapGrid 设置的活动器官描边参数。
    /// </summary>
    public void SetOutline(bool enabled)
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
             * 先读取 Renderer 已有属性块，
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

    // ─────────── 类型切换 ───────────

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
         * OrganController.SwitchOrganType 会调用：
         *
         * targetOrgan.SwitchOrganType(type)
         *
         * 因此执行到这里时，场景器官的实际 Sprite
         * 应当已经完成更新。
         */
        RefreshOrganRenderers();

        /*
         * 读取场景对象最终使用的 Sprite，
         * 根据映射表更新 UI。
         */
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
    /// 运行时为该插槽设置新的目标器官。
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
    /// 外部系统在器官 Sprite 发生变化后可以调用该方法，
    /// 同步刷新 Renderer、描边和 UI 邮票。
    /// </summary>
    public void NotifyOrganSpriteChanged()
    {
        RefreshOrganRenderers();
    }
}