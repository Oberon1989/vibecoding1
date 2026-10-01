using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class SimplePlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float verticalSpeed = 10f;

    [Header("Camera")]
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private Transform cameraTransform;

    [Header("Planet")]
    [SerializeField] private Transform planetCenter;
    [SerializeField] private float planetMass = 5.972e24f;
    [SerializeField] private float surfaceGravity = 9.81f;

    [Header("Player")]
    [SerializeField] private float playerMass = 80f;
    [SerializeField] private float rotationSpeed = 10f;

    [Header("Surface")]
    [SerializeField] private float surfaceRayDistance = 10f;

    [Header("Jump / Fly")]
    [SerializeField] private bool useFly = false;
    [SerializeField] private float jumpForce = 600f;

    private Rigidbody rb;

    private float cameraPitch;
    private bool jumpRequested;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        rb.mass = playerMass;
        rb.useGravity = false;

        rb.collisionDetectionMode =
            CollisionDetectionMode.ContinuousDynamic;

        rb.interpolation =
            RigidbodyInterpolation.Interpolate;

        rb.constraints =
            RigidbodyConstraints.FreezeRotation;
    }

    private void Update()
    {
        Look();

        if (!useFly &&
            Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            jumpRequested = true;
        }
    }

    private void FixedUpdate()
    {
        if (!useFly)
        {
            ApplyGravity();
        }

        AlignToPlanet();
        Move();
        DebugSurfaceNormal();

        if (!useFly)
        {
            Jump();
        }
    }

    private void ApplyGravity()
    {
        Vector3 gravityDirection =
            (
                planetCenter.position -
                rb.position
            ).normalized;

        rb.AddForce(
            gravityDirection *
            surfaceGravity,
            ForceMode.Acceleration
        );
    }

    private void AlignToPlanet()
    {
        Vector3 up =
            (
                rb.position -
                planetCenter.position
            ).normalized;

        Vector3 forward =
            Vector3.ProjectOnPlane(
                transform.forward,
                up
            );

        if (forward.sqrMagnitude < 0.001f)
        {
            forward =
                Vector3.ProjectOnPlane(
                    transform.right,
                    up
                );
        }

        forward.Normalize();

        Quaternion targetRotation =
            Quaternion.LookRotation(
                forward,
                up
            );

        Quaternion rotation =
            Quaternion.Slerp(
                rb.rotation,
                targetRotation,
                rotationSpeed *
                Time.fixedDeltaTime
            );

        rb.MoveRotation(
            rotation
        );
    }

    private void Move()
    {
        if (Keyboard.current == null)
            return;

        Vector3 up =
            (
                rb.position -
                planetCenter.position
            ).normalized;

        Vector3 forward =
            Vector3.ProjectOnPlane(
                transform.forward,
                up
            ).normalized;

        Vector3 right =
            Vector3.Cross(
                up,
                forward
            ).normalized;

        Vector2 input =
            new Vector2(
                GetAxis(
                    Key.D,
                    Key.A
                ),
                GetAxis(
                    Key.W,
                    Key.S
                )
            );

        Vector3 horizontalDirection =
            right * input.x +
            forward * input.y;

        if (horizontalDirection.sqrMagnitude > 1f)
        {
            horizontalDirection.Normalize();
        }

        Vector3 movement =
            horizontalDirection *
            moveSpeed;

        movement +=
            MoveVertical(
                up
            );

        rb.MovePosition(
            rb.position +
            movement *
            Time.fixedDeltaTime
        );
    }

    private Vector3 MoveVertical(
        Vector3 up)
    {
        if (!useFly)
            return Vector3.zero;

        float verticalInput = 0f;

        if (Keyboard.current.spaceKey.isPressed)
        {
            verticalInput += 1f;
        }

        if (Keyboard.current.leftCtrlKey.isPressed)
        {
            verticalInput -= 1f;
        }

        return up *
               verticalInput *
               verticalSpeed;
    }

    private void Jump()
    {
        if (!jumpRequested)
            return;

        jumpRequested = false;

        Vector3 up =
            (
                rb.position -
                planetCenter.position
            ).normalized;

        rb.AddForce(
            up *
            jumpForce,
            ForceMode.Impulse
        );
    }

    private void DebugSurfaceNormal()
    {
        CapsuleCollider capsule =
            GetComponent<CapsuleCollider>();

        Vector3 up =
            (
                rb.position -
                planetCenter.position
            ).normalized;

        Vector3 rayOrigin =
            rb.position -
            up *
            (capsule.height * 0.5f);

        Ray ray =
            new Ray(
                rayOrigin,
                -up
            );

        if (Physics.Raycast(
            ray,
            out RaycastHit hit,
            surfaceRayDistance))
        {
            Debug.DrawRay(
                hit.point,
                hit.normal * 10f,
                Color.green
            );

            Debug.DrawRay(
                hit.point,
                up * 10f,
                Color.red
            );
        }
    }

    private float GetAxis(
        Key positive,
        Key negative)
    {
        float value = 0f;

        if (Keyboard.current[positive].isPressed)
        {
            value += 1f;
        }

        if (Keyboard.current[negative].isPressed)
        {
            value -= 1f;
        }

        return value;
    }

    private void Look()
    {
        if (Mouse.current == null)
            return;

        Vector2 mouseDelta =
            Mouse.current.delta.ReadValue();

        float mouseX =
            mouseDelta.x *
            mouseSensitivity *
            0.01f;

        float mouseY =
            mouseDelta.y *
            mouseSensitivity *
            0.01f;

        transform.Rotate(
            Vector3.up,
            mouseX,
            Space.Self
        );

        cameraPitch -= mouseY;

        cameraPitch =
            Mathf.Clamp(
                cameraPitch,
                -89f,
                89f
            );

        cameraTransform.localRotation =
            Quaternion.Euler(
                cameraPitch,
                0f,
                0f
            );
    }
}