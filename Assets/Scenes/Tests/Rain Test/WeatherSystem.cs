using UnityEngine;

//[ExecuteInEditMode]
public class WeatherSystem : MonoBehaviour
{
    [Header("Rain")]
    [SerializeField] private Transform target;
    [SerializeField] private Transform cameraPlane;
    [SerializeField] private Transform screenQuad;

    [Space]
    [SerializeField] private Transform particles;
    [SerializeField] private ParticleSystem particleSystem;
    [SerializeField] private float particleShapeZ = 5;

    private ParticleSystem.ShapeModule shapeModule;

    private Vector3 targetEuler = Vector3.zero;
    private float planeAngle;
    private float pitch = 0;
    private float remap = 5;

    [Header("Clouds")]
    [SerializeField] private Material cloudsMaterial;
    [SerializeField] private float cloudsRotationSpeed = 0.001f;
    private float cloudsRotation = 0;

    private readonly int _Rotation = Shader.PropertyToID("_Rotation");

    [Header("Wet Floor")]
    [SerializeField] private Transform floorRoot;
    [SerializeField] private float floorOffsetY = -15;
    [Header("Tiling / Quad Scale")]
    [SerializeField] private float offsetMulti;
    [SerializeField] private Material wetFloorMaterial;

    private readonly int _Offset = Shader.PropertyToID("_Offset");

    [Header("Fog")]
    [SerializeField] private Transform fogRoot;

    private void Awake()
    {
        cloudsRotation = Random.Range(0f, 1f);
    }

    private void Update()
    {
        #region Clouds
        cloudsRotation = Mathf.Repeat(cloudsRotation + Time.deltaTime * cloudsRotationSpeed, 1f);
        cloudsMaterial.SetFloat(_Rotation, cloudsRotation);
        #endregion
    }

    private void LateUpdate()
    {
        #region Rain
        if (!shapeModule.enabled)
        {
            remap = particleShapeZ;
            shapeModule = particleSystem.shape;
            shapeModule.position = new Vector3(shapeModule.position.x, shapeModule.position.y, remap);
        }

        targetEuler = target.eulerAngles;
        planeAngle = 90 - screenQuad.localEulerAngles.x;

        pitch = targetEuler.x;
        if (pitch > 180f)
            pitch -= 360f;

        cameraPlane.position = target.position;

        if (pitch < 90 && pitch >= planeAngle)
            cameraPlane.eulerAngles = new Vector3(planeAngle, targetEuler.y, targetEuler.z);
        else
            cameraPlane.rotation = target.rotation;

        if (pitch < 0 && pitch >= -90)
        {
            remap = pitch.Remap(0, -90, particleShapeZ, 0);
            shapeModule.position = new Vector3(shapeModule.position.x, shapeModule.position.y, remap);
        }
        else
        {
            remap = pitch.Remap(0, 90, particleShapeZ, 0);
            shapeModule.position = new Vector3(shapeModule.position.x, shapeModule.position.y, remap);
        }

        particles.position = target.position;
        particles.eulerAngles = new Vector3(0, target.eulerAngles.y, 0);
        #endregion

        #region Wet Floor
        floorRoot.position = new Vector3(target.position.x, floorOffsetY, target.position.z);
        wetFloorMaterial.SetVector(_Offset, new Vector2(target.position.x * offsetMulti, target.position.z * offsetMulti));
        #endregion

        #region Fog
        fogRoot.position = new Vector3(target.position.x, 1.1176f /*value from XR Origin*/, target.position.z);
        #endregion
    }
}