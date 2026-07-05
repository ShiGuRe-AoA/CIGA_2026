using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Scene-level service locator for core runtime systems.
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    public static GameBootstrap Instance { get; private set; }

    public MapGrid MapGrid { get; private set; }

    public OrganController OrganController { get; private set; }

    public readonly List<Trigger> AllTriggers = new List<Trigger>();

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        MapGrid = FindObjectOfType<MapGrid>();
        OrganController = FindObjectOfType<OrganController>();

        if (MapGrid == null)
            Debug.LogError("[GameBootstrap] MapGrid not found in scene.", this);

        if (OrganController == null)
            Debug.LogError("[GameBootstrap] OrganController not found in scene.", this);
    }

    private void Start()
    {
        AudioPlayer.PlayBGM("MainSceneBGM");
    }

    private void Update()
    {
        PlayUIClickSfxIfNeeded();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private static void PlayUIClickSfxIfNeeded()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return;

        if (Input.GetMouseButtonDown(0) &&
            eventSystem.IsPointerOverGameObject())
        {
            AudioPlayer.PlayOneShot("SFX_Click");
            return;
        }

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            if (touch.phase != TouchPhase.Began)
                continue;

            if (eventSystem.IsPointerOverGameObject(touch.fingerId))
            {
                AudioPlayer.PlayOneShot("SFX_Click");
                return;
            }
        }
    }
}
