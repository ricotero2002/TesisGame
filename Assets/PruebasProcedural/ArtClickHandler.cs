using UnityEngine;

public class ArtClickHandler : MonoBehaviour
{
    public TileController tileController;

#if !(UNITY_ANDROID || UNITY_IOS)
    // Sólo en PC/consola se usa OnMouseDown
    void OnMouseDown()
    {
        if (tileController != null && tileController.IsPlayerNearby())
            tileController.InvokeOnTileClicked();
    }
#endif

#if UNITY_ANDROID || UNITY_IOS
    void Update()
    {
        // Comprueba un único touch al comienzo
        if (Input.touchCount > 0 && Input.touches[0].phase == TouchPhase.Began)
        {
            var touch = Input.touches[0];
            Ray ray = Camera.main.ScreenPointToRay(touch.position);

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                // ¿Este ArtClickHandler es el que recibió el raycast?
                var handler = hit.collider.GetComponentInParent<ArtClickHandler>();
                if (handler == this && tileController != null && tileController.IsPlayerNearby())
                {
                    tileController.InvokeOnTileClicked();
                }
            }
        }
    }
#endif
}
