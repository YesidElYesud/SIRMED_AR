using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

public class CameraBgTexture : MonoBehaviour
{
    [SerializeField] private ARCameraManager m_CameraManager;
    [SerializeField] private bool test;
    [SerializeField] private RawImage[] testImage;
    private bool textureSet = false;

    private readonly int _DisplayMatrix = Shader.PropertyToID("_DisplayMatrix");
#if UNITY_ANDROID
    private readonly int _CameraTexture = Shader.PropertyToID("_CameraTexture");
#elif UNITY_IOS
    private readonly int _CameraTextureY = Shader.PropertyToID("_CameraTextureY");
    private readonly int _CameraTextureCbCr = Shader.PropertyToID("_CameraTextureCbCr");
#endif

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
            if (args.displayMatrix != null)
                Shader.SetGlobalMatrix(_DisplayMatrix, args.displayMatrix.Value);

            if (test)
            {
                Debug.LogWarning($"BgTexture count: {args.textures.Count}");
                for (int i = 0; i < args.textures.Count; i++)
                {
                    Debug.LogWarning($"BgTexture {i} - {args.propertyNameIds[i]} - {args.textures[i].isDataSRGB} - {args.textures[i].format} - {args.textures[i].graphicsFormat} - {args.textures[i].graphicsTexture}");

                    if (testImage != null && testImage[i] != null)
                        testImage[i].texture = args.textures[i];
                }
            }

#if UNITY_ANDROID
            Shader.SetGlobalTexture(_CameraTexture, args.textures[0]);
#elif UNITY_IOS
            Shader.SetGlobalTexture(_CameraTextureY, args.textures[0]);
            Shader.SetGlobalTexture(_CameraTextureCbCr, args.textures[1]);
#endif

            textureSet = true;
        }
    }
}