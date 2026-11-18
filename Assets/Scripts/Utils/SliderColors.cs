using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public class SliderColors : MonoBehaviour
{
    [SerializeField] private Slider slider;
    [SerializeField] private Image fill;
    [SerializeField] private Gradient gradient;

    private void Update()
    {
        fill.color = gradient.Evaluate(slider.normalizedValue);
    }
}