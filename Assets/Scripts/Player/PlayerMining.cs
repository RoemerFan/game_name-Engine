 
using UnityEngine;
 
public class PlayerMining : MonoBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float miningRange = 3f;
 
    private CoalMiningPoint currentPoint;
 
    private void Update()
    {
        if (playerCamera == null)
            return;
 
        if (!Input.GetMouseButton(0))
        {
            currentPoint = null;
            return;
        }
 
        Ray ray = playerCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f)
        );
 
        if (Physics.Raycast(
            ray,
            out RaycastHit hit,
            miningRange))
        {
            CoalMiningPoint point =
                hit.collider.GetComponentInParent<CoalMiningPoint>();
 
            if (point != currentPoint)
                currentPoint = point;
 
            if (currentPoint != null)
                currentPoint.Mine(Time.deltaTime);
        }
        else
        {
            currentPoint = null;
        }
    }
}
 
