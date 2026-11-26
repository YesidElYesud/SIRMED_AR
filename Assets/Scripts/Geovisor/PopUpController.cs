using Cysharp.Threading.Tasks;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PopUpController : MonoBehaviour
{
    public static PopUpController Instance;

    [SerializeField] private SirmedDataScriptable dataScriptable;
    [SerializeField] private RectTransform popUp;
    [SerializeField] private CanvasGroup popUpCanvasGroup;
    [SerializeField] private float fadeSpeed = 5;

    [Space]
    [SerializeField, TextArea] private string reference;

    [Space]
    [SerializeField] private TextMeshProUGUI text;
    [SerializeField] private GameObject startBtn;

    private string sceneName;
    private CancellationTokenSource _cts = new();

    private void Awake()
    {
        Instance = this;

        popUp.gameObject.SetActive(false);
        startBtn.SetActive(false);
    }

    public void OpenPopUp(Vector2 position, bool on, int id, string _sceneName)
    {
        if (id == -1)
            return;

        sceneName = _sceneName;

        popUp.position = position;
        startBtn.SetActive(on);

        text.SetText(string.Format(reference,
            dataScriptable.data.layer.data[id].siata_nom,
            dataScriptable.data.layer.data[id].siata_cod,
            dataScriptable.data.layer.data[id].microcuenc));

        FadeIn().Forget();
    }

    public void ClosePopUp()
    {
        FadeOut().Forget();
    }

    public void OpenScene()
    {
        SceneManager.LoadScene(sceneName);
    }

    public void OpenDevScene()
    {
        SceneManager.LoadScene("Loader");
    }

    private async UniTaskVoid FadeIn()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        popUp.gameObject.SetActive(true);
        float timer = 0;

        while (timer < 1)
        {
            timer += Time.deltaTime * fadeSpeed;

            popUpCanvasGroup.alpha = timer;

            await UniTask.Yield(cancellationToken: _cts.Token);
        }

        popUpCanvasGroup.alpha = 1;
    }

    private async UniTaskVoid FadeOut()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        float timer = 1;

        while (timer > 0)
        {
            timer -= Time.deltaTime * fadeSpeed;

            popUpCanvasGroup.alpha = timer;

            await UniTask.Yield(cancellationToken: _cts.Token);
        }

        popUpCanvasGroup.alpha = 0;
        popUp.gameObject.SetActive(false);
    }
}