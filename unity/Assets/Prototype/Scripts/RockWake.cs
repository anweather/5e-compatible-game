using UnityEngine;

// A rock standing in the river (ValleyBuilder lays them; Andrew's playtest #33):
// white water where the current meets its upstream face, carried off
// downstream on the surface, so the current is seen to break round it. The
// river's push itself (WaterCurrents) doesn't go round it yet. View only, and
// only near the camera.
public class RockWake : MonoBehaviour
{
    [Tooltip("The way the river runs past the rock (world, level).")]
    public Vector3 downstream = Vector3.forward;
    [Tooltip("The river's surface here (world y), and the rock's half width at it (m).")]
    public float waterY, radius = 0.8f;

    // Foam a second, how fast it's carried off (m/s), and how near the camera it's drawn (m).
    const float Rate = 22f, Carry = 1.4f, Near = 70f;
    ParticleSystem foam;
    float owed;

    void Start() => foam = WaterSplashes.MakeSystem(gameObject, "River rock foam", 0f);

    void Update()
    {
        var cam = Camera.main;
        if (foam == null || cam == null || (cam.transform.position - transform.position).sqrMagnitude > Near * Near) return;
        owed += Rate * Time.deltaTime;
        var across = Vector3.Cross(Vector3.up, downstream);
        for (; owed >= 1f; owed -= 1f)
        {
            // Round the upstream half of the rock, then off downstream, spreading a little.
            float a = Random.Range(-1f, 1f) * Mathf.PI * 0.5f;
            var round = -downstream * Mathf.Cos(a) + across * Mathf.Sin(a);
            var at = new Vector3(transform.position.x, waterY + 0.03f, transform.position.z) + round * radius;
            var velocity = downstream * (Carry * Random.Range(0.7f, 1.2f)) + across * (Mathf.Sin(a) * 0.35f);
            foam.Emit(new ParticleSystem.EmitParams
            {
                position = at,
                velocity = velocity,
                startSize = Random.Range(0.2f, 0.45f),
                startLifetime = Random.Range(1f, 2f),
                startColor = new Color(1f, 1f, 1f, 0.75f),
            }, 1);
        }
    }
}
