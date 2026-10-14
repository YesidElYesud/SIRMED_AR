using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class CameraBgTexture : MonoBehaviour
{
    [SerializeField] private ARCameraManager m_CameraManager;
    private bool textureSet = false;

    private readonly int _DisplayMatrix = Shader.PropertyToID("_DisplayMatrix");
    private readonly int _CameraTexture = Shader.PropertyToID("_CameraTexture");

    private void OnEnable()
    {
        m_CameraManager.frameReceived += FrameChanged;
    }

    private void OnDisable()
    {
        m_CameraManager.frameReceived -= FrameChanged;
    }

    private void FrameChanged(ARCameraFrameEventArgs args)
    {
        if (!textureSet && args.textures.Count > 0)
        {
            Debug.LogWarning($"BgTexture count: {args.textures.Count}");
            for (int i = 0; i < args.textures.Count; i++)
            {
                Debug.LogWarning($"BgTexture count: {args.textures[i].name} {args.propertyNameIds[i]} {args.textures[i].isDataSRGB}");
            }

            if (args.displayMatrix != null)
                Shader.SetGlobalMatrix(_DisplayMatrix, args.displayMatrix.Value);

            Shader.SetGlobalTexture(_CameraTexture, args.textures[0]);

            textureSet = true;
        }
    }
}