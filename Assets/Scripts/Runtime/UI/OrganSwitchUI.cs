using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 器官类型切换 UI。
///
/// 鼠标进入插槽时开启对应器官的描边；
/// 子菜单打开期间持续保持描边；
/// 鼠标离开且菜单未打开时关闭描边；
/// 关闭子菜单时关闭描边。
/// </summary>
public class OrganSwitchSlot :
    MonoBehaviour,
    IPointerClickHandler,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private static readonly int OutlineEnabledId =
        Shader.PropertyToID("_OutlineEnabled");

    /// <summary>
    /// 当前处于打开状态的插槽。
    /// 所有 OrganSwitchSlot 实例共享该引用，
    /// 以保证同时只有一个子菜单处于打开状态。
    /// </summary>
    private static OrganSwitchSlot currentOpenSlot;

    [Header("绑定")]
    [SerializeField]
    private OrganUnit targetOrgan;

    [Header("器官描边")]
    [Tooltip(
        "需要开启描边的器官 SpriteRenderer。" +
        "未设置时会从 targetOrgan 子物体中自动查找。"
    )]
    [SerializeField]
    private SpriteRenderer outlineRenderer;

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

    public OrganUnit TargetOrgan => targetOrgan;

    private OrganController controller;

    private MaterialPropertyBlock propertyBlock;

    private bool isSubMenuOpen;
    private bool isPointerInside;

    private void Awake()
    {
        propertyBlock =
            new MaterialPropertyBlock();

        /*
         * Prefer the explicitly assigned renderer.
         * Fall back to the first SpriteRenderer under targetOrgan.
         */
        if (
            outlineRenderer == null &&
            targetOrgan != null
        )
        {
            outlineRenderer =
                targetOrgan.GetComponentInChildren<SpriteRenderer>();
        }

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
        /*
         * Prevent an object from retaining its outline
         * after this UI slot is disabled.
         */
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

        if (currentOpenSlot == this)
            currentOpenSlot = null;
    }

    public void OnPointerClick(
        PointerEventData eventData
    )
    {
        if (
            eventData.button !=
            PointerEventData.InputButton.Left
        )
        {
            return;
        }

        if (isSubMenuOpen)
            CloseSubMenu();
        else
            OpenSubMenu();
    }

    public void OnPointerEnter(
        PointerEventData eventData
    )
    {
        isPointerInside = true;

        if (slotImage != null)
            slotImage.color = hoverColor;

        SetOutline(true);
    }

    public void OnPointerExit(
        PointerEventData eventData
    )
    {
        isPointerInside = false;

        if (slotImage != null)
            slotImage.color = normalColor;

        /*
         * Leaving the slot does not remove the outline
         * while its submenu remains open.
         */
        RefreshOutlineState();
    }

    private void OpenSubMenu()
    {
        if (
            subMenuPanel == null ||
            targetOrgan == null
        )
        {
            return;
        }

        /*
         * Close the previously opened slot before opening this one.
         */
        if (
            currentOpenSlot != null &&
            currentOpenSlot != this
        )
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
         * According to the requested behavior,
         * closing the submenu disables the outline immediately.
         *
         * It can be enabled again by a new pointer-enter event.
         */
        SetOutline(false);
    }

    /// <summary>
    /// Enables the outline when either the pointer is inside
    /// or the submenu is open.
    /// </summary>
    private void RefreshOutlineState()
    {
        SetOutline(
            isPointerInside ||
            isSubMenuOpen
        );
    }

    /// <summary>
    /// Controls only this renderer through MaterialPropertyBlock.
    /// Other renderers sharing the same Material are not affected.
    /// </summary>
    public void SetOutline(bool enabled)
    {
        if (
            outlineRenderer == null ||
            propertyBlock == null
        )
        {
            return;
        }

        Material sharedMaterial =
            outlineRenderer.sharedMaterial;

        if (
            sharedMaterial == null ||
            !sharedMaterial.HasProperty(
                OutlineEnabledId
            )
        )
        {
            return;
        }

        /*
         * Always read the existing block first so other
         * per-renderer shader values are preserved.
         */
        outlineRenderer.GetPropertyBlock(
            propertyBlock
        );

        propertyBlock.SetFloat(
            OutlineEnabledId,
            enabled ? 1f : 0f
        );

        outlineRenderer.SetPropertyBlock(
            propertyBlock
        );
    }

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
        if (
            controller == null ||
            targetOrgan == null
        )
        {
            return;
        }

        controller.SwitchOrganType(
            targetOrgan,
            type
        );

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
        float alpha
    )
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