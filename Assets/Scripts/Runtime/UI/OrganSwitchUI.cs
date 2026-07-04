using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 器官类型切换 UI — 一个 OrganUnit 的 UI 插槽。
/// 挂载在 Image/Button 所在 GameObject 上，负责左键弹出子菜单、右键收起。
/// </summary>
public class OrganSwitchSlot : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    /// <summary>
    /// 当前处于打开状态的插槽。
    /// static 确保所有 OrganSwitchSlot 实例共享同一个引用。
    /// </summary>
    private static OrganSwitchSlot currentOpenSlot;

    [Header("绑定")]
    [SerializeField] private OrganUnit targetOrgan;

    [Header("子菜单")]
    [SerializeField] private GameObject subMenuPanel;
    [SerializeField] private Button handButton;
    [SerializeField] private Button eyeButton;
    [SerializeField] private Button footButton;

    [Header("反馈")]
    [SerializeField] private Image slotImage;       // 可选：插槽自身背景图
    [SerializeField] private Color hoverColor = new Color(1f, 1f, 1f, 0.9f);
    [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.5f);

    public OrganUnit TargetOrgan => targetOrgan;

    private OrganController controller;
    private bool isSubMenuOpen;

    private void Start()
    {
        controller = GameBootstrap.Instance?.OrganController;

        // 初始隐藏子菜单
        if (subMenuPanel != null)
            subMenuPanel.SetActive(false);

        // 绑定按钮事件
        if (handButton != null)
            handButton.onClick.AddListener(() => SwitchTo(OrganType.Hand));
        if (eyeButton != null)
            eyeButton.onClick.AddListener(() => SwitchTo(OrganType.Eye));
        if (footButton != null)
            footButton.onClick.AddListener(() => SwitchTo(OrganType.Foot));

        // 设置初始颜色
        if (slotImage != null)
            slotImage.color = normalColor;
    }

    private void OnDestroy()
    {
        // 清理按钮监听
        if (handButton != null) handButton.onClick.RemoveAllListeners();
        if (eyeButton != null) eyeButton.onClick.RemoveAllListeners();
        if (footButton != null) footButton.onClick.RemoveAllListeners();

        // 当前被销毁的插槽正好是已打开插槽时，清空共享引用。
        if (currentOpenSlot == this)
            currentOpenSlot = null;
    }

    private void Update()
    {
        // 右键收起子菜单
        //if (isSubMenuOpen && Input.GetMouseButtonDown(1))
        //{
        //    CloseSubMenu();
        //}
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            if (isSubMenuOpen)
            {
                // 子菜单已开，左键再次点击 → 不做特殊操作（由子按钮处理）
                CloseSubMenu();
            }
            else
            {
                OpenSubMenu();
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (slotImage != null)
            slotImage.color = hoverColor;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (slotImage != null)
            slotImage.color = normalColor;
    }

    // ─────────── 内部 ───────────

    private void OpenSubMenu()
    {
        if (subMenuPanel == null || targetOrgan == null)
            return;

        // 当前存在其他已打开的插槽时，先将其关闭。
        if (currentOpenSlot != null && currentOpenSlot != this)
            currentOpenSlot.CloseSubMenu();

        subMenuPanel.SetActive(true);
        isSubMenuOpen = true;

        currentOpenSlot = this;

        HighlightCurrentType();
    }

    private void CloseSubMenu()
    {
        if (subMenuPanel != null)
            subMenuPanel.SetActive(false);

        isSubMenuOpen = false;

        if (currentOpenSlot == this)
            currentOpenSlot = null;
    }

    private void SwitchTo(OrganType type)
    {
        if (controller == null || targetOrgan == null) return;

        controller.SwitchOrganType(targetOrgan, type);
        CloseSubMenu();
    }

    private void HighlightCurrentType()
    {
        if (targetOrgan == null) return;

        var current = targetOrgan.OrganType;

        SetButtonAlpha(handButton, current == OrganType.Hand ? 1f : 0.5f);
        SetButtonAlpha(eyeButton,  current == OrganType.Eye  ? 1f : 0.5f);
        SetButtonAlpha(footButton, current == OrganType.Foot ? 1f : 0.5f);
    }

    private static void SetButtonAlpha(Button btn, float alpha)
    {
        if (btn == null) return;
        var img = btn.targetGraphic;
        if (img != null)
        {
            var c = img.color;
            c.a = alpha;
            img.color = c;
        }
    }
}
