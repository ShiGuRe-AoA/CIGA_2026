using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 器官类型切换 UI。
///
/// 鼠标进入插槽时开启对应器官的 Hover 描边；
/// 子菜单打开期间持续保持 Hover 描边；
/// 鼠标离开且子菜单未打开时关闭 Hover 描边。
///
/// Hover 描边与 MapGrid 管理的 Active 描边互不覆盖。
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

    [Header("绑定")]
    [SerializeField]
    private OrganUnit targetOrgan;

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

    [Header("子菜单")]
    [SerializeField]
    private GameObject subMenuPanel;

    [SerializeField]
    private Button handButton;

    [SerializeField]
    private Button eyeButton;

    [SerializeField]
    private Button footButton;

    [Header("UI 反馈")]
    [SerializeField]
    private Image slotImage;

    [SerializeField]
    private Color hoverColor =
        new Color(1f, 1f, 1f, 0.9f);

    [SerializeField]
    private Color normalColor =
        new Color(1f, 1f, 1f, 0.5f);

    public OrganUnit TargetOrgan =>
        targetOrgan;

    private OrganController controller;
    private MaterialPropertyBlock propertyBlock;

    private bool isSubMenuOpen;
    private bool isPointerInside;

    private void Awake()
    {
        propertyBlock =
            new MaterialPropertyBlock();

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
    }

    private void OnDisable()
    {
        isPointerInside = false;
        isSubMenuOpen = false;

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

    public void OnPointerClick(
        PointerEventData eventData)
    {
        if (eventData.button !=
            PointerEventData.InputButton.Left)
        {
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
            targetOrgan == null)
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
         * 如果鼠标仍停留在插槽上，保留 Hover 描边；
         * 鼠标已经离开则关闭。
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
        SetOutline(
            isPointerInside ||
            isSubMenuOpen
        );
    }

    // ─────────── 描边 ───────────

    /// <summary>
    /// 自动收集目标器官下的全部 SpriteRenderer。
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
                System.Array.Empty<SpriteRenderer>();

            return;
        }

        outlineRenderers =
            targetOrgan.GetComponentsInChildren<SpriteRenderer>(
                includeInactive: true
            );
    }

    /// <summary>
    /// 重新收集目标器官下的 SpriteRenderer。
    ///
    /// 如果器官切换类型时动态创建、删除了 Renderer，
    /// 可以在切换完成后调用。
    /// </summary>
    public void RefreshOutlineRenderers()
    {
        if (targetOrgan == null)
            return;

        outlineRenderers =
            targetOrgan.GetComponentsInChildren<SpriteRenderer>(
                includeInactive: true
            );

        RefreshOutlineState();
    }

    /// <summary>
    /// 设置 Hover 描边。
    ///
    /// 只修改：
    /// _HoverOutlineEnabled
    /// _HoverOutlineColor
    ///
    /// 不会修改 MapGrid 设置的活动描边参数。
    /// </summary>
    public void SetOutline(bool enabled)
    {
        if (propertyBlock == null ||
            outlineRenderers == null)
        {
            return;
        }

        foreach (SpriteRenderer spriteRenderer in outlineRenderers)
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
             * 先读取 Renderer 已有属性块，保留：
             * _ActiveOutlineEnabled
             * _ActiveOutlineColor
             * 以及其他逐 Renderer 参数。
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

    private void SwitchTo(OrganType type)
    {
        if (controller == null ||
            targetOrgan == null)
        {
            return;
        }

        controller.SwitchOrganType(
            targetOrgan,
            type
        );

        /*
         * 如果 SwitchOrganType 只是替换 Sprite，
         * 原 Renderer 引用仍然有效。
         *
         * 如果它会创建或销毁 SpriteRenderer，
         * 则需要重新收集。
         */
        RefreshOutlineRenderers();

        CloseSubMenu();
    }

    private void HighlightCurrentType()
    {
        if (targetOrgan == null)
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

        color.a =
            alpha;

        graphic.color =
            color;
    }
}