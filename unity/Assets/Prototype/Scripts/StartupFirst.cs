using UnityEngine;

// The first of the first frame's Starts and Updates (the earliest execution order), for the start-up timeline: it splits
// the time from the scene's Awakes to the first frame into the Starts and the Updates. Made by StartupTimeline once the
// scene has loaded; gone after the first frame.
[DefaultExecutionOrder(-32000)]
public sealed class StartupFirst : MonoBehaviour
{
    void Start() => StartupTimeline.Mark("first frame: Starts begin");

    void FixedUpdate() => StartupTimeline.Mark("first frame: first FixedUpdate");

    void Update()
    {
        StartupTimeline.Mark("first frame: Starts done, Updates begin");
        Destroy(gameObject);
    }
}
