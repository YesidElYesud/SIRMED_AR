using SIRMED.Gameplay.Hotspots;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Canvas))]
public class HotspotsActivator : MonoBehaviour
{
    public static HotspotsActivator Instance;

    [SerializeField] private Graphic blackImage;

    private Camera mainCamera;
    private Vector2 screenPosition;
    private Ray ray;
    private HotspotController lastHotspot;

    private void Awake()
    {
        Instance = this;

        mainCamera = Camera.main;

        Canvas canvas = GetComponent<Canvas>();
        canvas.worldCamera = mainCamera;
        canvas.planeDistance = 25;

        blackImage.enabled = true;
        blackImage.CrossFadeAlpha(0, 0, false);
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            screenPosition = Input.mousePosition;
        }
        else if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            if (EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId))
                return;

            screenPosition = Input.GetTouch(0).position;
        }
        else
        {
            return;
        }

        ray = mainCamera.ScreenPointToRay(screenPosition);
        Debug.DrawRay(ray.origin, ray.direction * 25, Color.white, 2f);

        if (Physics.Raycast(ray, out RaycastHit hit, 25))
        {
            if (hit.collider.TryGetComponent(out HotspotController controller))
            {
                if (lastHotspot != null)
                {
                    lastHotspot.Activated -= Check;
                    lastHotspot.SwitchState();
                }

                if (lastHotspot != controller)
                {
                    blackImage.CrossFadeAlpha(1, 0.5f, false);

                    lastHotspot = controller;
                    lastHotspot.SwitchState();

                    lastHotspot.Activated += Check;
                }
                else
                {
                    blackImage.CrossFadeAlpha(0, 0.5f, false);
                    lastHotspot.Activated -= Check;
                    lastHotspot = null;
                }
            }
        }
        else if (lastHotspot != null)
        {
            blackImage.CrossFadeAlpha(0, 0.5f, false);
            lastHotspot.Activated -= Check;
            lastHotspot.SwitchState();
            lastHotspot = null;
        }
    }

    public void DeactivateLastHotspot()
    {
        if (lastHotspot != null)
        {
            blackImage.CrossFadeAlpha(0, 0.2f, false);
            lastHotspot.Activated -= Check;
            lastHotspot.SwitchState();
            lastHotspot = null;
        }
    }

    private void Check(HotspotController controller)
    {
        controller.SwitchState();
        controller.Activated -= Check;
        lastHotspot = null;
    }
}