using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.iOS;
using UnityEngine.Networking;
using UnityEngine.UI;

public class MapController : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerMoveHandler
{
    [Header("References")]
    [SerializeField] private RawImage raw;
    [SerializeField] private RectTransform rect;

    [Space]
    [SerializeField] private SirmedDataScriptable dataScriptable;

    [Space]
    [SerializeField] private RectTransform rootPoints;
    [SerializeField] private RectTransform point;
    [SerializeField] private RectTransform imagePoint;
    private bool pointsCreated;
    private RectTransform[] points;
    private RectTransform[] imagePoints;

    [Header("Editables")]
    [SerializeField] private string MapsStaticAPIKey = "AIzaSyDrLcl9TazRnbQe3QHLaDewmUUkd9B7K8w";
    [SerializeField] private float zoomDisplacementPow = 1.945f;
    //[SerializeField] private float[] earthCurvature;
    [SerializeField] private float zoomPower = 1;
    [SerializeField] private float mapZoom = 10;
    [SerializeField] private double latitude = 6.151457f;
    [SerializeField] private double longitude = -75.610904f;

    [Space]
    public bool touch1Down;
    public Vector2 touch1Position;
    public Vector2 touch1PositionPixels;
    public bool touch2Down;
    public Vector2 touch2Position;
    //public Vector2 touch2PositionPixels;
    public bool dragging;
    public bool zooming;

    [Space]
    public float startTouchDistance = -1;
    public float touchDistance;
    public float zoom;

    [Space]
    public float imageStartScale;

    [Space]
    public Vector2 rectOffset = Vector2.zero;

    [Space]
    public bool needUpdateZoom;
    public bool updatingMap = false;

    private const string urlMaps = "https://maps.googleapis.com/maps/api/staticmap?center={0},{1}" +
        "&zoom={2}" +
        "&size=640x640" +
        //"&scale=2" +
        "&maptype=hybrid" +
        "&style=feature:administrative|visibility:off" +
        "&style=feature:poi|visibility:off" +
        "&style=feature:transit|visibility:off" +
        "&key={3}";

    private readonly DownloadedTextureParams downloadedParams = new()
    {
        readable = false,
        mipmapChain = true
    };

    private readonly float[] earthCurvatures =
    {
        -61.44f,
        -30.72f,
        -15.36f,
        -7.68f,
        -3.84f,
        -1.92f,
        -0.96f,
        -0.48f,
        -0.24f,
        -0.12f,
        -0.06f,
        -0.035f,
        -0.005f,
        0.02f,
        0.05f,
        0.08f,
        0.11f,
        0.145f,
        0.175f,
        0.21f,
        0.24f,
        0.2755f,
    };

    private void Awake()
    {
        point.gameObject.SetActive(false);
        imagePoint.gameObject.SetActive(false);
    }

    private void Start()
    {
        DownloadImage().Forget();
    }

    private async UniTaskVoid DownloadImage()
    {
        //Debug.LogWarning("DownloadImage");

        updatingMap = true;

        //if (!pointsCreated)
        //{
        //    foreach (Datum item in dataScriptable.data.layer.data)
        //    {
        //        urlMaps += $"&markers=color:blue%7Clabel:S%7C{item.latitud},{item.longitud}";
        //    }
        //}

        using UnityWebRequest request = UnityWebRequestTexture.GetTexture(
            string.Format(urlMaps, latitude, longitude, mapZoom, MapsStaticAPIKey), downloadedParams);

        await request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
            Debug.Log(request.error);
        else
        {
            raw.texture = DownloadHandlerTexture.GetContent(request);
            rect.localScale = Vector2.one;
            rect.localPosition = Vector3.zero;

            UpdatePoints();
        }

        updatingMap = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.pointerId == 0)
            touch1Down = true;
        else if (eventData.pointerId == 1)
            touch2Down = true;

        if (touch1Down && !touch2Down || !touch1Down && touch2Down)
            dragging = true;
        else if (touch1Down && touch2Down)
            zooming = true;
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        Vector2 position = new(eventData.position.x / Screen.width, eventData.position.y / Screen.height);

        if (eventData.pointerId == 0)
        {
            touch1Position = position;
            touch1PositionPixels = eventData.position;
        }
        else if (eventData.pointerId == 1)
        {
            touch2Position = position;
            //touch2PositionPixels = eventData.position;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId == 0)
            touch1Down = false;
        if (eventData.pointerId == 1)
            touch2Down = false;

        if (!touch1Down)
            rectOffset = Vector2.zero;

        if (!touch1Down && !touch2Down)
        {
            dragging = false;

            mapZoom += zoom * zoomPower;
            mapZoom = Mathf.Clamp(Mathf.RoundToInt(mapZoom), 0, 21);

            Vector2 offset = Vector2.zero - (Vector2)rect.localPosition;
            Vector2 offsetMap = offset / Mathf.Pow(zoomDisplacementPow, mapZoom);
            longitude += offsetMap.x;
            latitude += offsetMap.y;

            DownloadImage().Forget();

            zoom = 0;
        }

        zooming = false;
        startTouchDistance = -1;
    }

    private void Update()
    {
        if (zooming)
        {
            touchDistance = Vector2.Distance(touch1Position, touch2Position);
            if (startTouchDistance == -1)
            {
                startTouchDistance = touchDistance;
                imageStartScale = rect.localScale.x;
            }

            zoom = touchDistance - startTouchDistance;
            rect.localScale = (imageStartScale + (zoom * zoomPower)) * Vector2.one;
        }
        else if (dragging)
        {
            if (rectOffset == Vector2.zero)
                rectOffset = (Vector2)rect.position - (touch1PositionPixels);

            rect.position = touch1PositionPixels + rectOffset;
        }
    }

    private void UpdatePoints()
    {
        rootPoints.localScale = Mathf.Pow(zoomDisplacementPow, mapZoom) * -Vector3.one;

        if (!pointsCreated)
        {
            int count = dataScriptable.data.layer.data.Count;
            points = new RectTransform[count];
            imagePoints = new RectTransform[count];

            for (int i = 0; i < count; i++)
            {
                Datum item = dataScriptable.data.layer.data[i];

                points[i] = Instantiate(point, point.parent);
                SetPointPosition(i, item.longitud, item.latitud);
                points[i].gameObject.SetActive(true);

                imagePoints[i] = Instantiate(imagePoint, imagePoint.parent);
                imagePoints[i].position = points[i].position;
                imagePoints[i].gameObject.SetActive(true);
            }

            pointsCreated = true;
        }
        else
        {
            for (int i = 0; i < points.Length; i++)
            {
                Datum item = dataScriptable.data.layer.data[i];
                SetPointPosition(i, item.longitud, item.latitud);
                imagePoints[i].position = points[i].position;
            }
        }
    }

    private void SetPointPosition(int index, double Longitud, double Latitud)
    {
        double Lo = longitude - Longitud;
        Lo += Lo * CurvatureMulti();

        double La = latitude - Latitud;
        La += La * CurvatureMulti();

        points[index].localPosition = new Vector2((float)Lo, (float)La);
    }

    private float CurvatureMulti()
    {
        return earthCurvatures[(int)mapZoom];
    }
}