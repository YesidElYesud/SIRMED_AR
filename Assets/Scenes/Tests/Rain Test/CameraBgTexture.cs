using UnityEngine;
//using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

public class CameraBgTexture : MonoBehaviour
{
    [SerializeField] private ARCameraManager m_CameraManager;
    //[SerializeField] private RawImage rawImage;
    private bool textureSet = false;

    [SerializeField] private Material rainMaterial;
    private readonly int _DisplayMatrix = Shader.PropertyToID("_DisplayMatrix");
    private readonly int _Background = Shader.PropertyToID("_Background");

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
            //rawImage.texture = args.textures[0];
            rainMaterial.SetMatrix(_DisplayMatrix, args.displayMatrix.Value);
            rainMaterial.SetTexture(_Background, args.textures[0]);

            textureSet = true;
        }

        //Debug.LogWarning($"Textures count: {args.textures.Count}");

        //for (int i = 0; i < args.textures.Count; i++)
        //{
        //    Debug.Log($"{args.textures[i].width}x{args.textures[i].height}");
        //    Debug.Log($"{args.displayMatrix.Value}");
        //}
    }
}