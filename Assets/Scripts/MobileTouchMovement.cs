using UnityEngine;

/// <summary>
/// Part I - navigate the village in mono mode with finger gestures.
///
///   One finger drag            look around
///   One finger hold            walk forward, then run (slide left/right to steer)
///   Tap                        walk to the tapped spot
///   Double tap                 reset zoom and level the view
///   Two finger pinch / spread  zoom out / in (field of view)
///   Two finger slide           walk forward / back / sideways (like a joystick)
///
/// PC testing in the Editor: WASD to walk, left-drag to look, click to walk
/// to a spot, mouse wheel to zoom, double-click to reset.
///
/// Distances are measured in inches of finger movement (using the screen DPI),
/// so the controls feel the same on any phone.
/// </summary>
public class MobileTouchMovement : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 5f;
    [Tooltip("Keep moving for a while and you speed up to this.")]
    public float runSpeed = 9f;
    [Tooltip("Seconds of moving before you start speeding up to run speed.")]
    public float runAfterSeconds = 1.5f;
    [Tooltip("Height of the eyes above the ground, in metres.")]
    public float eyeHeight = 1.6f;
    public float gravity = -15f;

    [Header("Look")]
    [Tooltip("Degrees the view turns per inch of finger movement.")]
    public float lookDegreesPerInch = 70f;
    public float mouseSensitivity = 2f;
    [Tooltip("Finger movement (inches) before a touch counts as a drag.")]
    public float dragThresholdInches = 0.08f;

    [Header("Tap and Hold")]
    [Tooltip("Hold this long (seconds) without moving to start walking.")]
    public float holdDelay = 0.25f;
    public float doubleTapTime = 0.3f;
    public float maxTapWalkDistance = 40f;

    [Header("Two Finger Walk")]
    [Tooltip("Slide two fingers this far (inches) for full speed.")]
    public float twoFingerFullSpeedInches = 0.6f;
    public float twoFingerDeadZoneInches = 0.1f;

    [Header("Zoom")]
    public float minFOV = 30f;
    public float maxFOV = 90f;
    [Tooltip("Degrees of zoom per inch of pinch.")]
    public float pinchDegreesPerInch = 25f;

    private CharacterController controller;
    private Camera playerCamera;

    private float pitch;
    private float yaw;
    private float defaultFOV;
    private float verticalVelocity;
    private Vector3 startPosition;

    // Movement requested this frame; applied once at the end of Update.
    private Vector3 frameMove;

    // One finger state.
    private int fingerId = -1;
    private Vector2 fingerStart;
    private Vector2 fingerLast;
    private float fingerStartTime;
    private bool fingerDragged;
    private bool fingerHolding;

    // After two fingers touch, ignore one-finger gestures until all
    // fingers are lifted (stops the view jumping after a pinch).
    private bool waitForAllFingersUp;

    // Two finger state.
    private bool twoFingerActive;
    private Vector2 twoFingerStartMid;

    // Tap / double tap.
    private bool tapPending;
    private Vector2 tapPosition;
    private float tapTime;

    // Walk-to-target from a tap.
    private bool hasWalkTarget;
    private Vector3 walkTarget;
    private float walkStuckTime;
    private GameObject targetMarker;

    // Mouse (Editor testing).
    private Vector3 mouseDownPosition;
    private bool mouseDragged;

    private float movingTime;

    private bool initialized;

    void Start()
    {
        Initialize();
    }

    /// <summary>
    /// One-time setup: stand on the ground at eye height. Also called by
    /// GameMenu, because the menu switches this component off before its
    /// own Start would run.
    /// </summary>
    public void Initialize()
    {
        if (initialized)
            return;
        initialized = true;

        controller = GetComponent<CharacterController>();
        playerCamera = GetComponent<Camera>();
        defaultFOV = playerCamera.fieldOfView;

        PlaceOnGround();
        startPosition = transform.position;
        CreateTargetMarker();
    }

    void OnEnable()
    {
        // The VR mode moves the camera; continue from where it is now.
        Vector3 rotation = transform.eulerAngles;
        pitch = rotation.x > 180f ? rotation.x - 360f : rotation.x;
        yaw = rotation.y;

        ResetTouchState();
        hasWalkTarget = false;
        verticalVelocity = 0f;
        movingTime = 0f;
    }

    void OnDisable()
    {
        if (targetMarker != null)
            targetMarker.SetActive(false);
    }

    void Update()
    {
        frameMove = Vector3.zero;

        HandleTouchInput();
        HandlePendingTap();

        if (!Application.isMobilePlatform && Input.touchCount == 0)
            HandlePCInput();

        WalkToTarget();
        ApplyMovement();
    }

    // ==========================================
    // TOUCH INPUT
    // ==========================================
    void HandleTouchInput()
    {
        int count = Input.touchCount;

        if (count == 0)
        {
            ResetTouchState();
            return;
        }

        // Any new touch stops walking to a tapped spot.
        for (int i = 0; i < count; i++)
        {
            if (Input.GetTouch(i).phase == TouchPhase.Began && !IsOverMenuButton(Input.GetTouch(i).position))
                hasWalkTarget = false;
        }

        if (count >= 2)
        {
            waitForAllFingersUp = true;
            fingerId = -1;
            HandleTwoFingers(Input.GetTouch(0), Input.GetTouch(1));
            return;
        }

        twoFingerActive = false;

        if (!waitForAllFingersUp)
            HandleOneFinger(Input.GetTouch(0));
    }

    void HandleOneFinger(Touch touch)
    {
        if (touch.phase == TouchPhase.Began)
        {
            // Leave the "Enter VR" button to the VR script.
            if (IsOverMenuButton(touch.position))
                return;

            fingerId = touch.fingerId;
            fingerStart = touch.position;
            fingerLast = touch.position;
            fingerStartTime = Time.time;
            fingerDragged = false;
            fingerHolding = false;
            return;
        }

        if (touch.fingerId != fingerId)
            return;

        if (!fingerDragged && !fingerHolding &&
            PixelsToInches((touch.position - fingerStart).magnitude) > dragThresholdInches)
        {
            fingerDragged = true;
        }

        if (touch.phase == TouchPhase.Ended)
        {
            if (!fingerDragged && !fingerHolding)
                RegisterTap(touch.position);

            fingerId = -1;
            return;
        }

        if (touch.phase == TouchPhase.Canceled)
        {
            fingerId = -1;
            return;
        }

        // Drag = look around.
        if (fingerDragged)
        {
            Vector2 delta = touch.position - fingerLast;
            Look(PixelsToInches(delta.x) * lookDegreesPerInch,
                 PixelsToInches(delta.y) * lookDegreesPerInch);
        }
        // Hold still = walk forward. While walking, sliding the finger
        // left/right steers.
        else if (Time.time - fingerStartTime > holdDelay)
        {
            if (fingerHolding)
            {
                Vector2 delta = touch.position - fingerLast;
                Look(PixelsToInches(delta.x) * lookDegreesPerInch, 0f);
            }

            fingerHolding = true;
            tapPending = false;
            frameMove += FlatForward() * walkSpeed;
        }

        fingerLast = touch.position;
    }

    void HandleTwoFingers(Touch touch1, Touch touch2)
    {
        Vector2 mid = (touch1.position + touch2.position) * 0.5f;

        if (!twoFingerActive || touch1.phase == TouchPhase.Began || touch2.phase == TouchPhase.Began)
        {
            twoFingerActive = true;
            twoFingerStartMid = mid;
            tapPending = false;
            return;
        }

        // ---- Pinch = zoom ----
        Vector2 previous1 = touch1.position - touch1.deltaPosition;
        Vector2 previous2 = touch2.position - touch2.deltaPosition;
        float pinch = Vector2.Distance(touch1.position, touch2.position) -
                      Vector2.Distance(previous1, previous2);

        SetFOV(playerCamera.fieldOfView - PixelsToInches(pinch) * pinchDegreesPerInch);

        // ---- Slide = walk, like a joystick from where the fingers started ----
        Vector2 offset = new Vector2(
            PixelsToInches(mid.x - twoFingerStartMid.x),
            PixelsToInches(mid.y - twoFingerStartMid.y));

        float forward = JoystickAxis(offset.y);
        float sideways = JoystickAxis(offset.x);

        frameMove += (FlatForward() * forward + FlatRight() * sideways) * walkSpeed;
    }

    float JoystickAxis(float inches)
    {
        float size = Mathf.Abs(inches);
        if (size < twoFingerDeadZoneInches)
            return 0f;

        float amount = (size - twoFingerDeadZoneInches) /
                       Mathf.Max(0.01f, twoFingerFullSpeedInches - twoFingerDeadZoneInches);
        return Mathf.Sign(inches) * Mathf.Clamp01(amount);
    }

    void ResetTouchState()
    {
        fingerId = -1;
        fingerDragged = false;
        fingerHolding = false;
        twoFingerActive = false;
        waitForAllFingersUp = false;
    }

    // ==========================================
    // TAP AND DOUBLE TAP
    // ==========================================
    void RegisterTap(Vector2 screenPosition)
    {
        bool closeToLastTap = PixelsToInches((screenPosition - tapPosition).magnitude) < 0.4f;

        if (tapPending && Time.time - tapTime < doubleTapTime && closeToLastTap)
        {
            tapPending = false;
            OnDoubleTap();
            return;
        }

        // Wait a moment in case a second tap follows.
        tapPending = true;
        tapPosition = screenPosition;
        tapTime = Time.time;
    }

    void HandlePendingTap()
    {
        if (tapPending && Time.time - tapTime >= doubleTapTime)
        {
            tapPending = false;
            OnSingleTap(tapPosition);
        }
    }

    void OnSingleTap(Vector2 screenPosition)
    {
        Ray ray = playerCamera.ScreenPointToRay(screenPosition);

        RaycastHit[] hits = Physics.RaycastAll(ray, maxTapWalkDistance, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        bool found = false;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == controller || hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            walkTarget = hit.point;
            found = true;
        }

        if (!found)
            return;

        hasWalkTarget = true;
        walkStuckTime = 0f;

        if (targetMarker != null)
        {
            targetMarker.transform.position = walkTarget + Vector3.up * 0.03f;
            targetMarker.SetActive(true);
        }
    }

    void OnDoubleTap()
    {
        hasWalkTarget = false;
        SetFOV(defaultFOV);
        pitch = 0f;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void WalkToTarget()
    {
        if (!hasWalkTarget)
        {
            if (targetMarker != null && targetMarker.activeSelf)
                targetMarker.SetActive(false);
            return;
        }

        Vector3 toTarget = walkTarget - transform.position;
        toTarget.y = 0f;

        if (toTarget.magnitude < 0.6f)
        {
            hasWalkTarget = false;
            return;
        }

        // Turn smoothly to face the target while walking.
        float targetYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
        yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, 120f * Time.deltaTime);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        frameMove += toTarget.normalized * walkSpeed;
    }

    // ==========================================
    // PC CONTROLS FOR UNITY TESTING
    // ==========================================
    void HandlePCInput()
    {
        // W/A/S/D
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        if (horizontal != 0f || vertical != 0f)
        {
            hasWalkTarget = false;
            frameMove += (FlatForward() * vertical + FlatRight() * horizontal) * walkSpeed;
        }

        // Right click = print the name of the object under the mouse
        // (Editor helper for finding objects in the village).
        if (Input.GetMouseButtonDown(1))
            LogObjectUnderMouse();

        // Mouse wheel = zoom
        float scroll = Input.mouseScrollDelta.y;
        if (scroll != 0f)
            SetFOV(playerCamera.fieldOfView - scroll * 5f);

        // Left drag = look, left click = walk to spot, double click = reset
        if (Input.GetMouseButtonDown(0))
        {
            if (IsOverMenuButton(Input.mousePosition))
                return;

            mouseDownPosition = Input.mousePosition;
            mouseDragged = false;
            hasWalkTarget = false;
        }

        if (Input.GetMouseButton(0))
        {
            if ((Input.mousePosition - mouseDownPosition).magnitude > 5f)
                mouseDragged = true;

            if (mouseDragged)
            {
                Look(Input.GetAxis("Mouse X") * mouseSensitivity,
                     Input.GetAxis("Mouse Y") * mouseSensitivity);
            }
        }

        if (Input.GetMouseButtonUp(0) && !mouseDragged && !IsOverMenuButton(Input.mousePosition))
            RegisterTap(Input.mousePosition);
    }

    void LogObjectUnderMouse()
    {
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 200f, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == controller)
                continue;

            string path = hit.collider.name;
            for (Transform t = hit.collider.transform.parent; t != null; t = t.parent)
                path = t.name + " / " + path;

            string enabledNote = hit.collider.enabled ? "" : " (collider off)";
            Debug.Log($"[Picked] {path}  ({hit.distance:0.0} m away){enabledNote}", hit.collider.gameObject);
            return;
        }

        Debug.Log("[Picked] nothing with a collider under the mouse");
    }

    // ==========================================
    // MOVEMENT AND GROUND
    // ==========================================
    void ApplyMovement()
    {
        if (controller == null || !controller.enabled)
            return;

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;
        else
            verticalVelocity += gravity * Time.deltaTime;

        // Keep moving and you speed up from walking to running.
        movingTime = frameMove.sqrMagnitude > 0.01f ? movingTime + Time.deltaTime : 0f;
        float run = Mathf.Clamp01((movingTime - runAfterSeconds) / 1f);
        float speedFactor = Mathf.Lerp(1f, runSpeed / Mathf.Max(0.01f, walkSpeed), run);

        Vector3 move = frameMove * speedFactor;
        move.y = verticalVelocity;

        Vector3 before = transform.position;
        controller.Move(move * Time.deltaTime);

        // Stop walking to a target if something blocks the way.
        if (hasWalkTarget)
        {
            Vector3 moved = transform.position - before;
            moved.y = 0f;
            walkStuckTime = moved.magnitude < walkSpeed * Time.deltaTime * 0.2f
                ? walkStuckTime + Time.deltaTime
                : 0f;

            if (walkStuckTime > 0.5f)
                hasWalkTarget = false;
        }

        // Fell off the world: go back to the start.
        if (transform.position.y < startPosition.y - 50f)
        {
            controller.enabled = false;
            transform.position = startPosition;
            controller.enabled = true;
            verticalVelocity = 0f;
        }
    }

    // Put the camera at eye height above the ground, and size the
    // character's capsule so its feet touch the ground.
    void PlaceOnGround()
    {
        if (controller == null)
            return;

        if (!FindGroundBelow(transform.position + Vector3.up * 0.5f, out float groundY))
            return;

        float radius = Mathf.Min(controller.radius, 0.3f);
        float height = Mathf.Max(eyeHeight + 0.1f, radius * 2f + 0.01f);

        controller.enabled = false;
        controller.radius = radius;
        controller.height = height;
        controller.center = new Vector3(0f, height * 0.5f - eyeHeight, 0f);
        controller.stepOffset = Mathf.Min(controller.stepOffset, height * 0.5f);
        transform.position = new Vector3(transform.position.x, groundY + eyeHeight, transform.position.z);
        controller.enabled = true;
    }

    bool FindGroundBelow(Vector3 origin, out float groundY)
    {
        groundY = 0f;
        float nearest = float.MaxValue;
        bool found = false;

        foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, 100f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == controller || hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            groundY = hit.point.y;
            found = true;
        }
        return found;
    }

    // ==========================================
    // HELPERS
    // ==========================================
    void Look(float yawDegrees, float pitchDegrees)
    {
        yaw += yawDegrees;
        pitch = Mathf.Clamp(pitch - pitchDegrees, -80f, 80f);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void SetFOV(float fov)
    {
        playerCamera.fieldOfView = Mathf.Clamp(fov, minFOV, maxFOV);
    }

    Vector3 FlatForward()
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return forward.normalized;
    }

    Vector3 FlatRight()
    {
        Vector3 right = transform.right;
        right.y = 0f;
        return right.normalized;
    }

    static float PixelsToInches(float pixels)
    {
        float dpi = Screen.dpi > 0f ? Screen.dpi : 160f;
        return pixels / dpi;
    }

    // Touches on the menu's on-screen buttons (Menu, Quit...) are not gestures.
    bool IsOverMenuButton(Vector2 screenPosition)
    {
        return GameMenu.BlocksTouch(screenPosition);
    }

    void CreateTargetMarker()
    {
        targetMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        targetMarker.name = "Tap Target Marker";
        Destroy(targetMarker.GetComponent<Collider>());
        targetMarker.transform.localScale = new Vector3(0.5f, 0.01f, 0.5f);

        Renderer markerRenderer = targetMarker.GetComponent<Renderer>();
        markerRenderer.material.color = new Color(1f, 0.85f, 0.2f);
        markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        targetMarker.SetActive(false);
    }
}
