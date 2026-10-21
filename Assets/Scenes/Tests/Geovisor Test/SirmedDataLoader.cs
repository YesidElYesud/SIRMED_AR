using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class SirmedDataLoader : MonoBehaviour
{
    [SerializeField] private bool ready = false;
    [SerializeField] private SirmedDataScriptable dataScriptable;

    private const string urlSirmed = "https://www.medellin.gov.co/sirmed-api/api/v1/capas-monitoreo/1";

    private void Awake()
    {
        ResetReady();
    }

    [ContextMenu("Clear Data")]
    public void ClearData()
    {
        dataScriptable.data = null;
    }

    [ContextMenu("Get Data")]
    public void GetData()
    {
        ResetReady();
        GetSirmedData().Forget();
    }

    private void ResetReady()
    {
        dataScriptable.isUpToDate = false;
        ready = false;
    }

    private async UniTaskVoid GetSirmedData()
    {
        using UnityWebRequest request = UnityWebRequest.Get(urlSirmed);

        request.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        request.SetRequestHeader("Accept", "application/json, text/plain, */*");
        request.SetRequestHeader("Cache-Control", "no-cache");
        request.timeout = 10;

        Debug.LogWarning("SendWebRequest");

        try
        {
            await request.SendWebRequest();

            ParseData(true, request.downloadHandler.text);
        }
        catch (UnityWebRequestException ex)
        {
            switch (ex.Result)
            {
                case UnityWebRequest.Result.ProtocolError:
                    long responseCode = request.responseCode;

                    string errorDetail = GetSirmedMeanings(responseCode,
                        request.downloadHandler != null ? request.downloadHandler.text : string.Empty);
                    Debug.LogError($"[HTTP code {responseCode} error]: {errorDetail}");
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

            ParseData(false);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Unexpected error: {ex.Message}");

            ParseData(false);
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

    private void ParseData(bool ok, string data = null)
    {
        if (ok)
        {
            Debug.Log("Using updated data");

            Debug.Log("Ok 200");
            Debug.Log(data);

            dataScriptable.data = JsonUtility.FromJson<SirmedData>(data);
            dataScriptable.data.layer.styles.ParseColors();
            dataScriptable.isUpToDate = true;

            if (Application.isEditor)
                Debug.Log(JsonUtility.ToJson(dataScriptable.data, true));
        }
        else
        {
            Debug.LogWarning("Using offline data");
        }

        ready = true;
    }
}