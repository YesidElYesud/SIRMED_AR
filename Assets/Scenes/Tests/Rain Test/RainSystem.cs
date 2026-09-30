using UnityEngine;

[ExecuteInEditMode]
public class RainSystem : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Transform cameraPlane;
    [SerializeField] private Transform screenQuad;
    [SerializeField] private Transform particles;
    [SerializeField] private ParticleSystem particleSystem;
    [SerializeField] private float particleShapeZ = 5;

    private ParticleSystem.ShapeModule shapeModule;
    private bool shapeModuleReady = false;

    private Vector3 targetEuler = Vector3.zero;
    private float planeAngle;
    private float pitch = 0;
    private float remap = 5;

    private void Reset()
    {
        shapeModuleReady = false;
    }

    private void Awake()
    {
        shapeModuleReady = false;
        remap = particleShapeZ;
    }

    private void Update()
    {
        if (!shapeModuleReady)
        {
            shapeModule = particleSystem.shape;
            shapeModule.position = new Vector3(shapeModule.position.x, shapeModule.position.y, remap);
            shapeModuleReady = true;
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
    }
}