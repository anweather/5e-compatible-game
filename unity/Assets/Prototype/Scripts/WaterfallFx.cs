using UnityEngine;

// The falls under the mountain, at the head of the river (built by
// ValleyBuilder.BuildWaterfall; its sheets of water pour by ScrollingTexture):
// throws up splashes where the water lands in the pond, a burst every few
// tenths of a second at a random spot round the foot, and roars, loudest
// close by. All of it is only the look: the pond and the
// river are chemistry water (river squares, a spring), the same on every
// machine. Splashes stop when the camera is far off.
public class WaterfallFx : MonoBehaviour
{
    [Tooltip("A splash played round the foot of the falls (the Particle Pack's BigSplash).")]
    public GameObject splash;
    [Tooltip("Where the falls meets the pond, and how far round it the splashes land (m).")]
    public Vector3 foot;
    public float footRadius = 2.5f;
    public float splashEvery = 0.3f, splashSeconds = 2.5f;
    [Tooltip("No splashes past this distance from the camera (m).")]
    public float splashRange = 220f;

    AudioSource roar;
    float nextSplash;

    void Start()
    {
        roar = new GameObject("Falls roar").AddComponent<AudioSource>();
        roar.transform.SetParent(transform, false);
        roar.transform.position = foot;
        roar.clip = ProceduralSounds.RainLoop;
        roar.loop = true;
        roar.pitch = 0.55f;
        roar.spatialBlend = 1f;
        roar.rolloffMode = AudioRolloffMode.Linear;
        roar.minDistance = 10f;
        roar.maxDistance = 150f;
        roar.Play();
    }

    void Update()
    {
        roar.volume = 0.9f * GameVolume.Ambience;

        var cam = Camera.main;
        if (splash == null || Time.time < nextSplash || cam == null || (cam.transform.position - foot).sqrMagnitude > splashRange * splashRange) return;
        nextSplash = Time.time + splashEvery * Random.Range(0.6f, 1.4f);
        var at = Random.insideUnitCircle * footRadius;
        var burst = Instantiate(splash, foot + new Vector3(at.x, 0f, at.y), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
        burst.transform.localScale = Vector3.one * Random.Range(0.8f, 1.6f);
        Destroy(burst, splashSeconds);
    }
}
