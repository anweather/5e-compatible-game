using UnityEngine;

// A fixed-angle chase camera that trails the player.
public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0f, 9f, -7f);
    public float smoothing = 6f;

    void LateUpdate()
    {
        if (target == null) return;
        transform.position = Vector3.Lerp(transform.position, target.position + offset, smoothing * Time.deltaTime);
        transform.rotation = Quaternion.LookRotation(target.position + Vector3.up - transform.position);
    }
}
