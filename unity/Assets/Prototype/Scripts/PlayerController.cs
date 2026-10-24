using UnityEngine;
using UnityEngine.InputSystem;

// WASD / arrow-key movement on the ground plane, relative to the camera.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public float speed = 5f;
    public float gravity = -20f;

    CharacterController body;
    float verticalVelocity;

    void Awake() => body = GetComponent<CharacterController>();

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        var input = Vector2.zero;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1;
        input = Vector2.ClampMagnitude(input, 1f);

        var cam = Camera.main.transform;
        var forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        var right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
        var move = (forward * input.y + right * input.x) * speed;

        if (move.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(move), 15f * Time.deltaTime);

        verticalVelocity = body.isGrounded ? -1f : verticalVelocity + gravity * Time.deltaTime;
        move.y = verticalVelocity;
        body.Move(move * Time.deltaTime);
    }
}
