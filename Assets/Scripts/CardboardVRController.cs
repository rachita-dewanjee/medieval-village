using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using CardboardApi = Google.XR.Cardboard.Api;

/// <summary>
/// Assignment 2, Part II: Google Cardboard VR mode.
///
/// The app starts at the menu (GameMenu). Choosing "VR Mode" there calls
/// EnterVR(), which starts the Cardboard XR plugin:
///   - the scene is displayed in stereo,
///   - head movements look around,
///   - the screen (reached through the Cardboard thumb hole) is the only button:
///       hold                      = walk forward where you are looking
///       double-tap and hold       = walk backward
///       swipe left / swipe right  = turn 30 degrees
///   - the Cardboard X button (top-left) returns to the menu
///     (or to mono mode if nothing listens to CloseButtonPressed),
///   - the Cardboard gear button (top-right) scans a viewer QR code.
///
/// This component adds itself to the scene camera when the game starts,
/// so it does not have to be attached by hand.
/// </summary>
public class CardboardVRController : MonoBehaviour
{
    [Header("VR movement")]
    public float moveSpeed = 3f;
    public float snapTurnDegrees = 30f;

    [Header("Viewer settings (changed with the FOV / IOC buttons in VR)")]
    [Tooltip("Field of view of each eye, in degrees (each side of the centre).")]
    public float fieldOfView = 60f;
    public float minFieldOfView = 30f;
    public float maxFieldOfView = 60f;
    [Tooltip("Inter-ocular distance: how far apart the two eye views are, in millimetres.")]
    public float interOcularMm = 64f;
    public float minInterOcularMm = 50f;
    public float maxInterOcularMm = 80f;

    [Header("Thumb-hole gestures")]
    // A touch must last this long before it counts as "hold to walk".
    public float holdDelay = 0.15f;
    // Two touches closer together than this count as a double-tap.
    public float doubleTapWindow = 0.35f;
    // A touch shorter than this counts as a tap.
    public float tapMaxDuration = 0.25f;
    // Horizontal travel (fraction of screen width) that counts as a swipe.
    public float swipeFraction = 0.12f;

    private Camera cam;
    private CharacterController cameraController;
    private MobileTouchMovement monoMovement;

    private Transform rig;
    private CharacterController rigController;

    private bool vrActive;
    private bool vrStarting;
    private float savedFieldOfView;

    // Gesture state.
    private bool touchTracked;
    private bool touchIsSwipe;
    private bool touchIsBackward;
    private float touchStartTime;
    private Vector2 touchStartPosition;
    private float lastTapEndTime = -10f;

    private string message = "";
    private float messageUntil;

    public bool IsVRActive => vrActive;
    public bool IsStarting => vrStarting;

    // Raised when the Cardboard X button is pressed in VR.
    public event System.Action CloseButtonPressed;

    private static readonly List<InputDevice> headDevices = new List<InputDevice>();

    // ------------------------------------------------------------------
    // Adds this component to the camera automatically at start-up.
    // ------------------------------------------------------------------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToSceneCamera()
    {
        // Only for the assignment scenes, not for the Cardboard sample scenes.
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (!sceneName.StartsWith("Assignment2"))
        {
            return;
        }

        if (Object.FindAnyObjectByType<CardboardVRController>() != null)
        {
            return;
        }

        Camera sceneCamera = Camera.main;
        if (sceneCamera == null)
        {
            sceneCamera = Object.FindAnyObjectByType<Camera>();
        }

        if (sceneCamera != null)
        {
            sceneCamera.gameObject.AddComponent<CardboardVRController>();
        }
    }

    private void Awake()
    {
        cam = GetComponent<Camera>();
        cameraController = GetComponent<CharacterController>();
        monoMovement = GetComponent<MobileTouchMovement>();
    }

    private void OnEnable()
    {
        Application.onBeforeRender += ApplyHeadPose;
    }

    private void OnDisable()
    {
        Application.onBeforeRender -= ApplyHeadPose;
    }

    private IEnumerator Start()
    {
        // If "Initialize XR on Startup" is switched on, XR is already running.
        yield return null;
        XRManagerSettings manager = GetXRManager();
        if (manager != null && manager.isInitializationComplete && manager.activeLoader != null)
        {
            OnVRStarted();
        }
    }

    private void Update()
    {
        if (!vrActive)
        {
            return;
        }

        // Cardboard on-screen buttons.
        if (CardboardApi.IsCloseButtonPressed)
        {
            if (CloseButtonPressed != null)
            {
                CloseButtonPressed();
            }
            else
            {
                ExitVR();
            }
            return;
        }

        if (CardboardApi.IsGearButtonPressed)
        {
            CardboardApi.ScanDeviceParams();
        }

        if (CardboardApi.HasNewDeviceParams())
        {
            CardboardApi.ReloadDeviceParams();
        }

        CardboardApi.UpdateScreenParams();

        ApplyHeadPose();
        HandleThumbHoleInput();
    }

    // ------------------------------------------------------------------
    // Entering and leaving VR
    // ------------------------------------------------------------------
    public void EnterVR()
    {
        if (vrActive || vrStarting)
        {
            return;
        }

        if (Application.isEditor)
        {
            ShowMessage("VR mode only runs on the phone build.");
            return;
        }

        StartCoroutine(StartXR());
    }

    private IEnumerator StartXR()
    {
        vrStarting = true;

        XRManagerSettings manager = GetXRManager();
        if (manager == null)
        {
            ShowMessage("XR Plug-in Management is not set up.");
            vrStarting = false;
            yield break;
        }

        if (manager.activeLoader == null)
        {
            yield return manager.InitializeLoader();
        }

        if (manager.activeLoader == null)
        {
            ShowMessage("Could not start Cardboard.");
            vrStarting = false;
            yield break;
        }

        manager.StartSubsystems();
        vrStarting = false;
        OnVRStarted();
    }

    private void OnVRStarted()
    {
        if (vrActive)
        {
            return;
        }

        vrActive = true;
        savedFieldOfView = cam.fieldOfView;

        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Screen.brightness = 1.0f;

        // The finger controls of Part I would fight the head tracking.
        if (monoMovement != null)
        {
            monoMovement.enabled = false;
        }

        if (cameraController != null)
        {
            cameraController.enabled = false;
        }

        // In XR the camera is placed relative to its parent, so give it a
        // "body" (rig) standing where the camera is now, facing the same way.
        Vector3 startPosition = transform.position;
        float startYaw = transform.eulerAngles.y;

        GameObject rigObject = new GameObject("VR Rig");
        rig = rigObject.transform;
        rig.SetPositionAndRotation(startPosition, Quaternion.Euler(0f, startYaw, 0f));

        if (cameraController != null)
        {
            rigController = rigObject.AddComponent<CharacterController>();
            rigController.height = cameraController.height;
            rigController.radius = cameraController.radius;
            rigController.center = cameraController.center;
            rigController.slopeLimit = cameraController.slopeLimit;
            rigController.stepOffset = cameraController.stepOffset;
            rigController.skinWidth = cameraController.skinWidth;
        }

        transform.SetParent(rig, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        touchTracked = false;

        // Use our viewer settings (with the current FOV and IOC) instead of
        // asking for the viewer's QR code.
        ApplyViewerSettings();
    }

    // ------------------------------------------------------------------
    // FOV and IOC
    // ------------------------------------------------------------------
    public void ChangeFieldOfView(float degrees)
    {
        fieldOfView = Mathf.Clamp(fieldOfView + degrees, minFieldOfView, maxFieldOfView);
        ApplyViewerSettings();
    }

    public void ChangeInterOcular(float millimetres)
    {
        interOcularMm = Mathf.Clamp(interOcularMm + millimetres, minInterOcularMm, maxInterOcularMm);
        ApplyViewerSettings();
    }

    // Cardboard reads its lens settings from "viewer parameters" (normally
    // from the QR code on the viewer). We build the same parameters for a
    // Cardboard v2 viewer, with our own field of view and eye distance,
    // and give them to Cardboard as if they came from a QR code.
    private void ApplyViewerSettings()
    {
        if (!vrActive)
        {
            return;
        }

        var bytes = new List<byte>();
        WriteText(bytes, 1, "Google, Inc.");                     // vendor
        WriteText(bytes, 2, "Cardboard v2 (custom FOV/IOC)");    // model
        WriteFloat(bytes, 3, 0.039f);                            // screen to lens distance (m)
        WriteFloat(bytes, 4, interOcularMm / 1000f);             // inter-lens (eye) distance (m)
        WriteFloats(bytes, 5, fieldOfView, fieldOfView, fieldOfView, fieldOfView); // FOV angles
        WriteFloat(bytes, 6, 0.035f);                            // tray to lens distance (m)
        WriteFloats(bytes, 7, 0.34f, 0.55f);                     // lens distortion
        WriteNumber(bytes, 11, 0);                               // vertical alignment: bottom
        WriteNumber(bytes, 12, 2);                               // button type: touch

        string data = System.Convert.ToBase64String(bytes.ToArray())
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        CardboardApi.SaveDeviceParams("https://google.com/cardboard/cfg?p=" + data);
        CardboardApi.ReloadDeviceParams();
    }

    // Small helpers that write the parameters in the QR code's format (protobuf).
    private static void WriteKey(List<byte> bytes, int field, int type)
    {
        WriteVarint(bytes, (field << 3) | type);
    }

    private static void WriteVarint(List<byte> bytes, int value)
    {
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }
        bytes.Add((byte)value);
    }

    private static void WriteNumber(List<byte> bytes, int field, int value)
    {
        WriteKey(bytes, field, 0);
        WriteVarint(bytes, value);
    }

    private static void WriteFloat(List<byte> bytes, int field, float value)
    {
        WriteKey(bytes, field, 5);
        bytes.AddRange(System.BitConverter.GetBytes(value));
    }

    private static void WriteFloats(List<byte> bytes, int field, params float[] values)
    {
        WriteKey(bytes, field, 2);
        WriteVarint(bytes, values.Length * 4);
        foreach (float value in values)
        {
            bytes.AddRange(System.BitConverter.GetBytes(value));
        }
    }

    private static void WriteText(List<byte> bytes, int field, string text)
    {
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(text);
        WriteKey(bytes, field, 2);
        WriteVarint(bytes, utf8.Length);
        bytes.AddRange(utf8);
    }

    public void ExitVR()
    {
        if (!vrActive)
        {
            return;
        }

        vrActive = false;

        XRManagerSettings manager = GetXRManager();
        if (manager != null)
        {
            manager.StopSubsystems();
            manager.DeinitializeLoader();
        }

        // Put the camera back in the world, level, where the player is standing.
        Vector3 position = transform.position;
        Vector3 angles = transform.eulerAngles;

        transform.SetParent(null, true);
        transform.SetPositionAndRotation(position, Quaternion.Euler(angles.x, angles.y, 0f));

        if (rig != null)
        {
            Destroy(rig.gameObject);
            rig = null;
            rigController = null;
        }

        cam.ResetAspect();
        cam.ResetProjectionMatrix();
        cam.fieldOfView = savedFieldOfView;

        if (cameraController != null)
        {
            cameraController.enabled = true;
        }

        if (monoMovement != null)
        {
            monoMovement.enabled = true;
        }

        Screen.sleepTimeout = SleepTimeout.SystemSetting;
    }

    private static XRManagerSettings GetXRManager()
    {
        XRGeneralSettings settings = XRGeneralSettings.Instance;
        return settings != null ? settings.Manager : null;
    }

    // ------------------------------------------------------------------
    // Head tracking
    // ------------------------------------------------------------------
    private void ApplyHeadPose()
    {
        if (!vrActive || rig == null)
        {
            return;
        }

        Vector3 headPosition;
        Quaternion headRotation;
        if (TryGetHeadPose(out headPosition, out headRotation))
        {
            transform.localPosition = headPosition;
            transform.localRotation = headRotation;
        }
    }

    private static bool TryGetHeadPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        headDevices.Clear();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.HeadMounted, headDevices);

        for (int i = 0; i < headDevices.Count; i++)
        {
            InputDevice device = headDevices[i];
            if (!device.isValid)
            {
                continue;
            }

            bool hasRotation =
                device.TryGetFeatureValue(CommonUsages.centerEyeRotation, out rotation) ||
                device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);

            if (!hasRotation)
            {
                continue;
            }

            if (!device.TryGetFeatureValue(CommonUsages.centerEyePosition, out position) &&
                !device.TryGetFeatureValue(CommonUsages.devicePosition, out position))
            {
                position = Vector3.zero;
            }

            return true;
        }

        rotation = Quaternion.identity;
        return false;
    }

    // ------------------------------------------------------------------
    // Thumb-hole navigation (the whole screen acts as one button)
    // ------------------------------------------------------------------
    private void HandleThumbHoleInput()
    {
        if (Input.touchCount == 0)
        {
            touchTracked = false;
            return;
        }

        Touch touch = Input.GetTouch(0);

        if (touch.phase == TouchPhase.Began)
        {
            // The top strip of the screen holds the Cardboard X and gear buttons.
            if (touch.position.y > Screen.height * 0.8f)
            {
                touchTracked = false;
                return;
            }

            touchTracked = true;
            touchIsSwipe = false;
            touchStartTime = Time.unscaledTime;
            touchStartPosition = touch.position;
            touchIsBackward = (Time.unscaledTime - lastTapEndTime) <= doubleTapWindow;
            return;
        }

        if (!touchTracked)
        {
            return;
        }

        float heldFor = Time.unscaledTime - touchStartTime;
        float travelX = touch.position.x - touchStartPosition.x;

        if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
        {
            if (touchIsSwipe)
            {
                // Swipe right turns right, swipe left turns left.
                float direction = travelX > 0f ? 1f : -1f;
                rig.Rotate(0f, direction * snapTurnDegrees, 0f, Space.World);
                lastTapEndTime = -10f;
            }
            else if (heldFor <= tapMaxDuration)
            {
                // A short tap: remember it so the next touch can be a double-tap.
                lastTapEndTime = Time.unscaledTime;
            }
            else
            {
                lastTapEndTime = -10f;
            }

            touchTracked = false;
            return;
        }

        if (!touchIsSwipe && Mathf.Abs(travelX) > Screen.width * swipeFraction)
        {
            touchIsSwipe = true;
        }

        if (!touchIsSwipe && heldFor >= holdDelay)
        {
            Walk(touchIsBackward ? -1f : 1f);
        }
    }

    private void Walk(float direction)
    {
        // Walk where the head is looking, but stay on the ground.
        Vector3 forward = transform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
        {
            return;
        }

        forward.Normalize();
        Vector3 step = forward * (direction * moveSpeed * Time.deltaTime);

        if (rigController != null)
        {
            rigController.Move(step);
        }
        else
        {
            rig.position += step;
        }
    }

    // ------------------------------------------------------------------
    // Status messages (e.g. "VR mode only runs on the phone build").
    // VR is started from the menu (GameMenu), not from a button here.
    // ------------------------------------------------------------------
    private void OnGUI()
    {
        if (vrActive || Time.unscaledTime >= messageUntil)
        {
            return;
        }

        // Draw on top of the menu.
        GUI.depth = -10;

        float lineHeight = Screen.height * 0.07f;
        float margin = Screen.height * 0.03f;

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = Mathf.RoundToInt(lineHeight * 0.5f);
        labelStyle.alignment = TextAnchor.MiddleCenter;
        labelStyle.normal.textColor = Color.white;

        Rect labelRect = new Rect(0f, Screen.height - lineHeight - margin, Screen.width, lineHeight);

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(labelRect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(labelRect, message, labelStyle);
    }

    private void ShowMessage(string text)
    {
        message = text;
        messageUntil = Time.unscaledTime + 3f;
        Debug.Log("[CardboardVRController] " + text);
    }
}
