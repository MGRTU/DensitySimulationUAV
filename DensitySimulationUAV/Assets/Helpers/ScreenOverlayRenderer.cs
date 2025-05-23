using UnityEngine;

/// <summary>
/// Class that renders a screen space UAV overlay
/// </summary>
public class ScreenOverlayRenderer : MonoBehaviour
{
    public UAV Uav;
    public GameObject CanvasGameObject;
    public Camera MainCamera;
    private bool enabled2;

    public GameObject Image;
    // Start is called before the first frame update
    private void Start()
    {
        //There needs to be a canvas GameObject if anything is needed to be drawn in the screen space
        CanvasGameObject = TestScheduler.Instance.CanvasGameObject;
        Image.transform.SetParent(CanvasGameObject.transform);
        MainCamera = Camera.main;
    }

    private void OnBecameVisible()
    {
        enabled2 = true;
    }

    private void OnBecameInvisible()
    {
        enabled2 = false;
    }

    // Update is called once per frame
    private void Update()
    {
        //Render the overlay of a UAV only if it is in a camera frame
        if (enabled2)
        {
            //Render the overlay of a UAV only if it is a set distance away from the camera
            var distance = Vector3.Distance(MainCamera.transform.position, Uav.transform.position);
            if (distance > 1000f)
            {
                Image.SetActive(false);
                return;
            }
            Image.SetActive(true);
            var scale = 200 / distance;
            Image.transform.localScale = new Vector3(scale, scale, 1);
            Image.transform.position = MainCamera.WorldToScreenPoint(Uav.transform.position);
        }
        else
        {
            Image.SetActive(false);
        }
    }
}
