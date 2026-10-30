using SIRMED.Gameplay.Hotspots;
using UnityEngine;
using UnityEngine.EventSystems;

public class HotspotsActivator : MonoBehaviour
{
    private Camera mainCamera;
    private Vector2 screenPosition;
    private Ray ray;
    private HotspotController lastHotspot;

    private void Awake()
    {
        mainCamera = Camera.main;
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
                    lastHotspot = controller;
                    lastHotspot.SwitchState();

                    lastHotspot.Activated += Check;
                }
                else
                {
                    lastHotspot.Activated -= Check;
                    lastHotspot = null;
                }
            }
        }
        else if (lastHotspot != null)
        {
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