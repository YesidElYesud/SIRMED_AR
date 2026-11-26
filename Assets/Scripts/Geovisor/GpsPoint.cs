using UnityEngine;

public class GpsPoint : MonoBehaviour
{
    public bool hasScene = false;
    public string sceneName;
    public int id = -1;

    public void OnClick()
    {
        PopUpController.Instance.OpenPopUp(transform.position, hasScene, id, sceneName);
    }
}