using UnityEngine;

/// <summary>
/// �Ӷ����ء�
///
/// �����ͬʱ�е�����ְ��
///
/// 1. ��Ϊ Mechanism��
///    ���Ա���ť�����˵����� Trigger ��ƽ��
///
/// 2. ��Ϊ�¼�Դ��
///    ���״α�Ϊ����ƽ״̬ʱ���������� filledTrigger.Fire()��
///    �Ӷ������š�ǽ��ƽ̨�Ⱥ������ء�
///
/// �����Ƿ��ܹ�������� MapGrid �� PushContext �жϣ�
/// �ӱ���ֻ����״̬�仯��������ش�����
/// </summary>
public class PitMechanism : Mechanism
{
    [Header("�Ӿ�")]
    [Tooltip("����δ��ƽʱ��ʾ�Ķ���")]
    [SerializeField] private GameObject emptyVisual;

    [Tooltip("���Ѿ���ƽʱ��ʾ�Ķ���")]
    [SerializeField] private GameObject filledVisual;

    [Header("��ƽ�¼�")]
    [Tooltip(
        "�Ӵ�δ��ƽ��Ϊ����ƽʱ��" +
        "���ø� Trigger.Fire() ����������ء�" +
        "���齫�� Trigger ����Ϊ Interact ģʽ��"
    )]
    [SerializeField] private Trigger filledTrigger;

    [Header("��ʼ״̬")]
    [Tooltip("������ʼʱ���Ƿ��Ѿ���ƽ��")]
    [SerializeField] private bool initiallyFilled;

    [Header("�ⲿ����")]
    [Tooltip(
        "���������� Trigger ���������Ϊ Mechanism ����ʱ��" +
        "��ֱ����ƽ�ÿӡ�"
    )]
    [SerializeField] private bool fillWhenTriggered = true;

    [Header("����")]
    [SerializeField] private bool showDebugLog = true;

    // ���������������������� ����ʱ״̬ ����������������������

    private Vector3Int gridPos;
    private bool isFilled;
    private bool registered;

    /// <summary>�Ӷ����ڸ��ӡ�</summary>
    public Vector3Int GridPos => gridPos;

    /// <summary>�Ӷ���ǰ�Ƿ��Ѿ���ƽ��</summary>
    public bool IsFilled => isFilled;

    private MapGrid Grid =>
        GameBootstrap.Instance?.MapGrid;

    // ���������������������� �������� ����������������������

    private void Start()
    {
        InitializePit();
    }

    private void OnDestroy()
    {
        UnregisterFromGrid();
    }

    /// <summary>
    /// ��ʼ���Ӷ����ӡ���ʼ״̬���Ӿ��͵�ͼע�ᡣ
    /// </summary>
    private void InitializePit()
    {
        if (Grid == null)
        {
            Debug.LogError(
                $"[PitMechanism] {name} δ�ҵ� MapGrid��",
                this
            );
            return;
        }

        gridPos =
            Grid.WorldToCell(transform.position);

        transform.position =
            Grid.CellToWorld(gridPos);

        isFilled = initiallyFilled;

        Grid.RegisterPit(this);
        registered = true;

        RefreshVisual();
    }

    private void UnregisterFromGrid()
    {
        if (!registered)
            return;

        if (Grid != null)
            Grid.UnregisterPit(this);

        registered = false;
    }

    // ���������������������� Mechanism ��� ����������������������

    /// <summary>
    /// ����ť�����˵����� Trigger ����ʱ���á�
    /// </summary>
    public override void OnTriggered(Trigger source)
    {
        base.OnTriggered(source);

        if (!fillWhenTriggered)
            return;

        Fill();
    }

    // ���������������������� ����ж� ����������������������

    /// <summary>
    /// �ж�ָ�����ƶ����ܷ���ƽ��ǰ�Ӷ���
    /// </summary>
    public bool CanBeFilledBy(PushableObject pushable)
    {
        if (isFilled || pushable == null)
            return false;

        if (pushable is not IPitFiller filler)
            return false;

        return filler.CanFillPit(this);
    }

    /// <summary>
    /// ʹ��ָ�����ƶ�����ƽ�Ӷ���
    ///
    /// ���� true ��ʾ��ӳɹ������÷��ɽ�������ע�������ء�
    /// </summary>
    public bool FillWith(PushableObject pushable)
    {
        if (!CanBeFilledBy(pushable))
            return false;

        IPitFiller filler =
            (IPitFiller)pushable;

        // ��֪ͨ������壬���䲥����Ч���¼״̬��
        filler.OnFilledPit(this);

        SetFilled(true);

        Log(
            $"{pushable.name} ��ƽ�˿Ӷ� {name}��"
        );

        return true;
    }

    // ���������������������� ״̬���� ����������������������

    /// <summary>
    /// ֱ�ӽ��Ӷ���ƽ��
    /// �����������ػ�ű����á�
    /// </summary>
    public void Fill()
    {
        SetFilled(true);
    }

    /// <summary>
    /// ���Ӷ����»ָ�Ϊδ��ƽ״̬��
    ///
    /// ֻ�ָ���״̬���Ӿ���
    /// �����Զ��ָ���ǰ�����ĵ����ӡ�
    /// </summary>
    public void Reopen()
    {
        SetFilled(false);
    }

    /// <summary>
    /// ���ÿӶ�״̬��
    ///
    /// ֻ�д�δ��ƽ��Ϊ����ƽʱ��
    /// �Ż��������� filledTrigger��
    /// </summary>
    public void SetFilled(bool filled)
    {
        if (isFilled == filled)
            return;

        bool wasFilled = isFilled;

        isFilled = filled;
        RefreshVisual();

        if (!wasFilled && isFilled)
        {
            // ʹ�õ�ǰ Trigger ���еĹ����ֶ�������ڡ�
            filledTrigger?.Fire();
        }

        Log(
            $"״̬�л�Ϊ��{(isFilled ? "����ƽ" : "δ��ƽ")}��"
        );
    }

    /// <summary>
    /// ���ݿ�״̬�л��Ӿ���
    /// </summary>
    private void RefreshVisual()
    {
        if (emptyVisual != null)
            emptyVisual.SetActive(!isFilled);

        if (filledVisual != null)
            filledVisual.SetActive(isFilled);
    }

    // ���������������������� Gizmos ����������������������

    private void OnDrawGizmos()
    {
        Color color =
            isFilled
                ? new Color(0.2f, 0.8f, 0.3f, 0.35f)
                : new Color(0.3f, 0.1f, 0.05f, 0.5f);

        Gizmos.color = color;

        Gizmos.DrawCube(
            transform.position,
            Vector3.one * 0.85f
        );

        Gizmos.DrawWireCube(
            transform.position,
            Vector3.one * 0.9f
        );
    }

    private void Log(string message)
    {
        if (!showDebugLog)
            return;

        Debug.Log(
            $"[PitMechanism] {name}��{message}",
            this
        );
    }
}
