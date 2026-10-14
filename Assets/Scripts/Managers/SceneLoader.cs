using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneLoader : MonoBehaviour
{
    [SerializeField] private Button btnInitial;

    private Button btn;

    private void Start()
    {
        btnInitial.gameObject.SetActive(false);

        for (int i = 1; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            btn = Instantiate(btnInitial, btnInitial.transform.parent);

            string sceneName = System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i));
            sceneName = sceneName.Replace("SAT_", string.Empty);
            sceneName = sceneName.Replace("_CoreTest", string.Empty);
            sceneName = Regex.Replace(sceneName, @"(?<!^)([A-Z])", " $1");

            btn.GetComponentInChildren<TextMeshProUGUI>().SetText(sceneName);

            SceneLoaderHolder holder = btn.gameObject.AddComponent<SceneLoaderHolder>();
            holder.index = i;

            btn.onClick.AddListener(() => holder.OpenScene());

            btn.gameObject.SetActive(true);
        }
    }
}

public class SceneLoaderHolder : MonoBehaviour
{
    public int index;

    public void OpenScene()
    {
        SceneManager.LoadScene(index);
    }
}