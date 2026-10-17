using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class SirmedDataLoader : MonoBehaviour
{
    [SerializeField, TextArea] private string test;
    [SerializeField] private SirmedData data;

    private string responseBody;

    private const string url = "https://www.medellin.gov.co/sirmed-api/api/v1/capas-monitoreo/1";

    [ContextMenu("Clear Data")]
    public void ClearData()
    {
        data = null;
    }

    [ContextMenu("Set Test")]
    public void SetTest()
    {
        data = JsonUtility.FromJson<SirmedData>(test);
        data.layer.styles.ParseColors();
    }

    [ContextMenu("Get Data")]
    public void GetData()
    {
        GetSirmedData().Forget();
    }

    private async UniTaskVoid GetSirmedData()
    {
        using UnityWebRequest request = UnityWebRequest.Get(url);

        request.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        request.SetRequestHeader("Accept", "application/json, text/plain, */*");
        request.SetRequestHeader("Cache-Control", "no-cache");
        request.timeout = 10;

        Debug.LogWarning("SendWebRequest");

        try
        {
            await request.SendWebRequest();

            Debug.LogWarning(request.result);

            responseBody = request.downloadHandler.text;

            Debug.Log("Ok 200");
            Debug.Log(responseBody);
            Debug.Log("----------");
            Debug.Log(JsonUtility.ToJson(responseBody));

            data = JsonUtility.FromJson<SirmedData>(responseBody);
            data.layer.styles.ParseColors();
        }
        catch (UnityWebRequestException ex)
        {
            switch (ex.Result)
            {
                case UnityWebRequest.Result.ProtocolError:
                    long responseCode = request.responseCode;
                    responseBody = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;

                    string errorDetail = GetSirmedMeanings(responseCode, responseBody);
                    Debug.LogError($"[HTTP Error {responseCode}] {errorDetail}");
                    break;

                case UnityWebRequest.Result.ConnectionError:
                    Debug.LogError($"[Network error]: {request.error}");
                    break;

                case UnityWebRequest.Result.DataProcessingError:
                    Debug.LogError($"[Data error]: {request.error}");
                    break;

                default:
                    Debug.LogError($"[Error]: {request.error}");
                    break;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Unexpected error: {ex.Message}");
        }
    }

    private string GetSirmedMeanings(long code, string responseBody)
    {
        switch (code)
        {
            case 404:
                return "Not Found (404): Registry not found.";

            case 500:
                if (responseBody.Contains("Error al obtener"))
                {
                    return "Internal Server Error (500): Communication with the territorial microservice failed or the ID does not exist in the viewer.";
                }
                return "Internal Server Error (500).";

            case 403:
                return "Forbidden (403): Access denied.";

            default:
                return $"Unspecifically mapped HTTP code: {code}";
        }
    }
}