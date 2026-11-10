using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Start menu and in-game buttons for Assignment 2.
///
///   Start menu      Mono Mode / VR Mode / Instructions / Exit
///                   (the village slowly turns in the background)
///   Mono mode       "Menu" and "Quit" buttons in the top-right corner
///   VR mode         look down to see 3D FOV/IOC/Menu/Quit buttons;
///                   put the dot on one (it lights up) and tap the screen
///                   (through the Cardboard thumb hole).
///                   The Cardboard X button also goes back to the menu.
///
/// This component adds itself to the scene camera when the game starts,
/// so it does not have to be attached by hand.
/// </summary>
public class GameMenu : MonoBehaviour
{
    enum Mode { Menu, Instructions, Mono, StartingVR, VR }

    public static GameMenu Instance { get; private set; }

    [Header("Menu background")]
    [Tooltip("Degrees per second the view turns behind the menu.")]
    public float menuTurnSpeed = 4f;

    [Header("VR buttons")]
    [Tooltip("How far away and how far below the eyes the VR buttons float (metres).")]
    public float vrPanelDistance = 1.0f;
    public float vrPanelDrop = 1.3f;
    private float vrPanelYaw;

    private Mode mode = Mode.Menu;
    private bool confirmQuit;

    private MobileTouchMovement monoMovement;
    private CardboardVRController vrController;

    // VR world-space buttons.
    private Transform vrPanel;
    private Transform reticle;
    private TextMesh settingsText;
    private readonly Dictionary<Collider, VRButton> vrButtons = new Dictionary<Collider, VRButton>();
    private VRButton gazedButton;
    private float vrTouchStartTime;
    private Vector2 vrTouchStartPosition;

    // On-screen button rectangles of the current frame (GUI coordinates).
    private readonly List<Rect> activeButtonRects = new List<Rect>();

    // Styles.
    private GUIStyle titleStyle, buttonStyle, smallButtonStyle, textStyle, headingStyle;
    private Texture2D panelTexture, buttonTexture, buttonHoverTexture, buttonPressedTexture, accentTexture;
    private int stylesForHeight = -1;

    class VRButton
    {
        public Renderer background;
        public Transform label;
        public Color normalColor;
        public Vector3 normalScale;
        public System.Action onClick;
    }

    // ------------------------------------------------------------------
    // Adds this component to the camera automatically at start-up.
    // ------------------------------------------------------------------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AttachToSceneCamera()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (!sceneName.StartsWith("Assignment2") || Object.FindAnyObjectByType<GameMenu>() != null)
            return;

        MobileTouchMovement movement = Object.FindAnyObjectByType<MobileTouchMovement>();
        Camera sceneCamera = movement != null ? movement.GetComponent<Camera>() : Camera.main;
        if (sceneCamera != null)
            sceneCamera.gameObject.AddComponent<GameMenu>();
    }

    void Awake()
    {
        Instance = this;
        monoMovement = GetComponent<MobileTouchMovement>();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        vrController = GetComponent<CardboardVRController>();
        if (vrController != null)
            vrController.CloseButtonPressed += ShowMenu;

        // Stand the player on the ground before the menu switches movement off.
        if (monoMovement != null)
            monoMovement.Initialize();

        ShowMenu();
    }

    void Update()
    {
        if (vrController == null)
        {
            vrController = GetComponent<CardboardVRController>();
            if (vrController != null)
                vrController.CloseButtonPressed += ShowMenu;
        }

        bool vrActive = vrController != null && vrController.IsVRActive;

        switch (mode)
        {
            case Mode.Menu:
            case Mode.Instructions:
                if (vrActive)
                    StartVRMode();
                else
                    TurnMenuBackground();
                break;

            case Mode.StartingVR:
                if (vrActive)
                    StartVRMode();
                else if (vrController == null || !vrController.IsStarting)
                    mode = Mode.Menu; // VR could not start - the reason is shown on screen.
                break;

            case Mode.VR:
                if (!vrActive)
                    ShowMenu();
                else
                    UpdateVRButtons();
                break;
        }
    }

    // ------------------------------------------------------------------
    // Switching modes
    // ------------------------------------------------------------------
    public void ShowMenu()
    {
        if (vrController != null && vrController.IsVRActive)
            vrController.ExitVR();

        DestroyVRButtons();
        SetMonoMovement(false);
        confirmQuit = false;
        mode = Mode.Menu;
    }

    void StartMonoMode()
    {
        SetMonoMovement(true);
        confirmQuit = false;
        mode = Mode.Mono;
    }

    void RequestVRMode()
    {
        SetMonoMovement(false);

        if (vrController == null)
            return;

        mode = Mode.StartingVR;
        vrController.EnterVR();
    }

    void StartVRMode()
    {
        mode = Mode.VR;
        SetMonoMovement(false);
        CreateVRButtons();
    }

    void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void SetMonoMovement(bool on)
    {
        if (monoMovement != null)
            monoMovement.enabled = on;
    }

    void TurnMenuBackground()
    {
        Vector3 angles = transform.eulerAngles;
        transform.rotation = Quaternion.Euler(5f, angles.y + menuTurnSpeed * Time.deltaTime, 0f);
    }

    /// <summary>
    /// True if a touch/mouse position (bottom-left origin) is on one of the
    /// menu's on-screen buttons, so the movement script should ignore it.
    /// </summary>
    public static bool BlocksTouch(Vector2 screenPosition)
    {
        if (Instance == null)
            return false;

        if (Instance.mode != Mode.Mono || Instance.confirmQuit)
            return true;

        var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        foreach (Rect rect in Instance.activeButtonRects)
            if (rect.Contains(guiPoint))
                return true;

        return false;
    }

    // ------------------------------------------------------------------
    // On-screen menu (start menu, instructions, mono-mode buttons)
    // ------------------------------------------------------------------
    void OnGUI()
    {
        if (mode == Mode.VR)
            return;

        BuildStyles();

        // Only rebuild the button list during the layout pass.
        if (Event.current.type == EventType.Layout)
            activeButtonRects.Clear();

        switch (mode)
        {
            case Mode.Menu:
            case Mode.StartingVR:
                DrawStartMenu();
                break;
            case Mode.Instructions:
                DrawInstructions();
                break;
            case Mode.Mono:
                DrawMonoButtons();
                break;
        }
    }

    void DrawStartMenu()
    {
        DimScreen(0.35f);

        float h = Screen.height;
        float width = Mathf.Min(Screen.width * 0.9f, h * 0.95f);
        float buttonHeight = h * 0.1f;
        float gap = h * 0.025f;
        float panelHeight = h * 0.17f + 4f * (buttonHeight + gap) + gap;

        var panel = new Rect((Screen.width - width) * 0.5f, (h - panelHeight) * 0.5f, width, panelHeight);
        DrawPanel(panel);

        GUI.Label(new Rect(panel.x, panel.y + h * 0.03f, panel.width, h * 0.1f), "Medieval Village", titleStyle);

        float buttonWidth = width * 0.75f;
        float x = panel.x + (width - buttonWidth) * 0.5f;
        float y = panel.y + h * 0.17f;

        bool starting = mode == Mode.StartingVR;

        if (MenuButton(new Rect(x, y, buttonWidth, buttonHeight), "Mono Mode", buttonStyle) && !starting)
            StartMonoMode();
        y += buttonHeight + gap;

        if (MenuButton(new Rect(x, y, buttonWidth, buttonHeight),
                starting ? "Starting VR..." : "VR Mode (Cardboard)", buttonStyle) && !starting)
            RequestVRMode();
        y += buttonHeight + gap;

        if (MenuButton(new Rect(x, y, buttonWidth, buttonHeight), "Instructions", buttonStyle) && !starting)
            mode = Mode.Instructions;
        y += buttonHeight + gap;

        if (MenuButton(new Rect(x, y, buttonWidth, buttonHeight), "Exit", buttonStyle) && !starting)
            QuitGame();
    }

    void DrawInstructions()
    {
        DimScreen(0.5f);

        float h = Screen.height;
        float margin = h * 0.05f;
        var panel = new Rect(margin, margin, Screen.width - 2f * margin, h - 2f * margin);
        DrawPanel(panel);

        GUI.Label(new Rect(panel.x, panel.y + h * 0.02f, panel.width, h * 0.09f), "Instructions", titleStyle);

        float columnTop = panel.y + h * 0.13f;
        float columnWidth = (panel.width - 3f * margin) * 0.5f;
        float columnHeight = panel.height - h * 0.3f;

        var monoColumn = new Rect(panel.x + margin, columnTop, columnWidth, columnHeight);
        var vrColumn = new Rect(monoColumn.xMax + margin, columnTop, columnWidth, columnHeight);

        GUI.Label(new Rect(monoColumn.x, monoColumn.y, monoColumn.width, h * 0.06f), "Mono Mode", headingStyle);
        GUI.Label(new Rect(monoColumn.x, monoColumn.y + h * 0.07f, monoColumn.width, columnHeight),
            "<b>Drag</b> - look around\n" +
            "<b>Hold</b> - walk forward (slide to steer)\n" +
            "<b>Keep holding</b> - run\n" +
            "<b>Tap</b> - walk to that spot\n" +
            "<b>Double tap</b> - reset view\n" +
            "<b>Pinch / spread</b> - zoom out / in\n" +
            "<b>Two-finger slide</b> - walk any direction\n" +
            "<b>Menu / Quit</b> - buttons top-right",
            textStyle);

        GUI.Label(new Rect(vrColumn.x, vrColumn.y, vrColumn.width, h * 0.06f), "VR Mode (Cardboard)", headingStyle);
        GUI.Label(new Rect(vrColumn.x, vrColumn.y + h * 0.07f, vrColumn.width, columnHeight),
            "Put the phone in the Cardboard viewer.\n" +
            "<b>Move your head</b> - look around\n" +
            "<b>Hold screen</b> (thumb hole) - walk forward\n" +
            "<b>Double tap + hold</b> - walk backward\n" +
            "<b>Swipe left / right</b> - turn\n" +
            "<b>Look down</b> - FOV, IOC, Menu and Quit buttons:\n" +
            "   put the dot on one (it lights up), then tap\n" +
            "<b>FOV -/+</b> - narrower / wider view\n" +
            "<b>IOC -/+</b> - eyes closer / further apart (3D depth)\n" +
            "<b>X</b> (top-left) - back to this menu\n" +
            "<b>Gear</b> (top-right) - scan viewer QR code",
            textStyle);

        float buttonHeight = h * 0.1f;
        float buttonWidth = h * 0.5f;
        if (MenuButton(new Rect(panel.center.x - buttonWidth * 0.5f, panel.yMax - buttonHeight - h * 0.04f,
                buttonWidth, buttonHeight), "Back", buttonStyle))
            mode = Mode.Menu;
    }

    void DrawMonoButtons()
    {
        float h = Screen.height;
        float buttonHeight = h * 0.085f;
        float buttonWidth = buttonHeight * 2.1f;
        float margin = h * 0.025f;

        var quitRect = new Rect(Screen.width - margin - buttonWidth, margin, buttonWidth, buttonHeight);
        var menuRect = new Rect(quitRect.x - margin - buttonWidth, margin, buttonWidth, buttonHeight);

        if (MenuButton(menuRect, "Menu", smallButtonStyle))
            ShowMenu();

        if (MenuButton(quitRect, "Quit", smallButtonStyle))
            confirmQuit = true;

        if (!confirmQuit)
            return;

        // "Are you sure?" box.
        DimScreen(0.4f);
        float width = h * 0.8f;
        float height = h * 0.38f;
        var box = new Rect((Screen.width - width) * 0.5f, (h - height) * 0.5f, width, height);
        DrawPanel(box);
        GUI.Label(new Rect(box.x, box.y + h * 0.04f, box.width, h * 0.1f), "Quit the game?", headingStyle);

        float bw = width * 0.38f;
        float bh = h * 0.1f;
        float by = box.yMax - bh - h * 0.05f;
        if (MenuButton(new Rect(box.x + width * 0.08f, by, bw, bh), "Yes", buttonStyle))
            QuitGame();
        if (MenuButton(new Rect(box.xMax - width * 0.08f - bw, by, bw, bh), "No", buttonStyle))
            confirmQuit = false;
    }

    bool MenuButton(Rect rect, string label, GUIStyle style)
    {
        if (Event.current.type == EventType.Layout)
            activeButtonRects.Add(rect);
        return GUI.Button(rect, label, style);
    }

    void DimScreen(float alpha)
    {
        GUI.color = new Color(0f, 0f, 0f, alpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    void DrawPanel(Rect rect)
    {
        GUI.DrawTexture(rect, panelTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, Mathf.Max(3f, Screen.height * 0.006f)), accentTexture);
    }

    void BuildStyles()
    {
        if (stylesForHeight == Screen.height && titleStyle != null)
            return;

        stylesForHeight = Screen.height;
        float h = Screen.height;

        if (panelTexture == null)
        {
            panelTexture = MakeTexture(new Color(0.09f, 0.11f, 0.15f, 0.88f));
            buttonTexture = MakeTexture(new Color(0.85f, 0.62f, 0.22f, 1f));
            buttonHoverTexture = MakeTexture(new Color(0.95f, 0.72f, 0.30f, 1f));
            buttonPressedTexture = MakeTexture(new Color(0.70f, 0.48f, 0.15f, 1f));
            accentTexture = MakeTexture(new Color(0.95f, 0.72f, 0.30f, 1f));
        }

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(h * 0.075f),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        titleStyle.normal.textColor = new Color(1f, 0.85f, 0.5f);

        headingStyle = new GUIStyle(titleStyle) { fontSize = Mathf.RoundToInt(h * 0.045f) };

        textStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(h * 0.032f),
            richText = true,
            wordWrap = true,
            alignment = TextAnchor.UpperLeft
        };
        textStyle.normal.textColor = new Color(0.92f, 0.94f, 0.96f);

        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = Mathf.RoundToInt(h * 0.045f),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        buttonStyle.normal.background = buttonTexture;
        buttonStyle.hover.background = buttonHoverTexture;
        buttonStyle.active.background = buttonPressedTexture;
        buttonStyle.normal.textColor = buttonStyle.hover.textColor = buttonStyle.active.textColor =
            new Color(0.12f, 0.08f, 0.03f);

        smallButtonStyle = new GUIStyle(buttonStyle) { fontSize = Mathf.RoundToInt(h * 0.038f) };
    }

    static Texture2D MakeTexture(Color color)
    {
        var texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    // ------------------------------------------------------------------
    // VR buttons (3D, chosen by looking at them and tapping)
    // ------------------------------------------------------------------
    void CreateVRButtons()
    {
        DestroyVRButtons();

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        vrPanel = new GameObject("VR Menu Buttons").transform;

        // Big buttons with gaps between them, so they are easy to aim at.
        var buttonSize = new Vector3(0.42f, 0.16f, 0.02f);
        var smallSize = new Vector3(0.2f, 0.13f, 0.02f);
        var settingColor = new Color(0.25f, 0.45f, 0.7f);

        // Bottom row: Menu and Quit.
        CreateVRButton("Menu", new Vector3(-0.24f, 0f, 0f), buttonSize, 0.013f,
            new Color(0.85f, 0.62f, 0.22f), font, ShowMenu);
        CreateVRButton("Quit", new Vector3(0.24f, 0f, 0f), buttonSize, 0.013f,
            new Color(0.75f, 0.25f, 0.2f), font, QuitGame);

        // Middle row: FOV and IOC.
        CreateVRButton("FOV -", new Vector3(-0.36f, 0.2f, 0f), smallSize, 0.009f, settingColor, font,
            () => ChangeViewerSetting(-5f, 0f));
        CreateVRButton("FOV +", new Vector3(-0.12f, 0.2f, 0f), smallSize, 0.009f, settingColor, font,
            () => ChangeViewerSetting(5f, 0f));
        CreateVRButton("IOC -", new Vector3(0.12f, 0.2f, 0f), smallSize, 0.009f, settingColor, font,
            () => ChangeViewerSetting(0f, -2f));
        CreateVRButton("IOC +", new Vector3(0.36f, 0.2f, 0f), smallSize, 0.009f, settingColor, font,
            () => ChangeViewerSetting(0f, 2f));

        // Top: current values and a hint.
        settingsText = CreateVRText("", new Vector3(0f, 0.33f, 0f), 0.009f, font);
        CreateVRText("Put the dot on a button and tap", new Vector3(0f, 0.42f, 0f), 0.006f, font);
        UpdateSettingsText();

        // Small white dot in the middle of the view for aiming.
        reticle = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
        reticle.name = "VR Reticle";
        Destroy(reticle.GetComponent<Collider>());
        reticle.SetParent(transform, false);
        reticle.localPosition = new Vector3(0f, 0f, 1.2f);
        reticle.localScale = Vector3.one * 0.012f;
        Renderer reticleRenderer = reticle.GetComponent<Renderer>();
        reticleRenderer.material.color = Color.white;
        reticleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        PlaceVRPanel(true);
    }

    void ChangeViewerSetting(float fovDegrees, float iocMillimetres)
    {
        if (vrController == null)
            return;

        if (fovDegrees != 0f)
            vrController.ChangeFieldOfView(fovDegrees);
        if (iocMillimetres != 0f)
            vrController.ChangeInterOcular(iocMillimetres);

        UpdateSettingsText();
    }

    void UpdateSettingsText()
    {
        if (settingsText != null && vrController != null)
            settingsText.text = $"FOV {vrController.fieldOfView:0}°     IOC {vrController.interOcularMm:0} mm";
    }

    void CreateVRButton(string label, Vector3 localPosition, Vector3 size, float textSize, Color color,
        Font font, System.Action onClick)
    {
        GameObject background = GameObject.CreatePrimitive(PrimitiveType.Cube);
        background.name = label + " Button";
        background.transform.SetParent(vrPanel, false);
        background.transform.localPosition = localPosition;
        background.transform.localScale = size;

        // A trigger, so the player never bumps into the buttons.
        Collider collider = background.GetComponent<Collider>();
        collider.isTrigger = true;

        Renderer renderer = background.GetComponent<Renderer>();
        renderer.material.color = color;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        TextMesh text = CreateVRText(label, localPosition + new Vector3(0f, 0f, -0.012f), textSize, font);

        vrButtons[collider] = new VRButton
        {
            background = renderer,
            label = text.transform,
            normalColor = color,
            normalScale = size,
            onClick = onClick
        };
    }

    void SetHighlight(VRButton button, bool on)
    {
        button.background.material.color = on
            ? Color.Lerp(button.normalColor, Color.white, 0.5f)
            : button.normalColor;

        // The selected button grows a little so it is obvious what a tap will press.
        float grow = on ? 1.15f : 1f;
        button.background.transform.localScale = new Vector3(
            button.normalScale.x * grow, button.normalScale.y * grow, button.normalScale.z);
        button.label.localScale = Vector3.one * grow;
    }

    TextMesh CreateVRText(string text, Vector3 localPosition, float characterSize, Font font)
    {
        var textObject = new GameObject(text + " Text");
        textObject.transform.SetParent(vrPanel, false);
        textObject.transform.localPosition = localPosition;

        TextMesh mesh = textObject.AddComponent<TextMesh>();
        mesh.text = text;
        mesh.font = font;
        mesh.fontSize = 64;
        mesh.characterSize = characterSize;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.color = Color.white;
        textObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        return mesh;
    }

    void DestroyVRButtons()
    {
        if (vrPanel != null)
            Destroy(vrPanel.gameObject);
        if (reticle != null)
            Destroy(reticle.gameObject);

        vrPanel = null;
        reticle = null;
        gazedButton = null;
        vrButtons.Clear();
    }

    void UpdateVRButtons()
    {
        if (vrPanel == null)
            CreateVRButtons();

        PlaceVRPanel(false);

        // ---- Which button is the dot on? ----
        VRButton looking = null;
        float nearest = float.MaxValue;
        var ray = new Ray(transform.position, transform.forward);
        foreach (RaycastHit hit in Physics.RaycastAll(ray, 5f, ~0, QueryTriggerInteraction.Collide))
        {
            if (hit.distance < nearest && vrButtons.TryGetValue(hit.collider, out VRButton button))
            {
                nearest = hit.distance;
                looking = button;
            }
        }

        if (looking != gazedButton)
        {
            if (gazedButton != null)
                SetHighlight(gazedButton, false);
            if (looking != null)
                SetHighlight(looking, true);
            gazedButton = looking;

            // The dot turns bigger and yellow when a tap would press a button.
            if (reticle != null)
            {
                reticle.localScale = Vector3.one * (looking != null ? 0.02f : 0.012f);
                reticle.GetComponent<Renderer>().material.color = looking != null ? new Color(1f, 0.85f, 0.2f) : Color.white;
            }
        }

        // ---- A short tap while looking at a button presses it ----
        if (Input.touchCount == 0)
            return;

        Touch touch = Input.GetTouch(0);
        if (touch.phase == TouchPhase.Began)
        {
            vrTouchStartTime = Time.unscaledTime;
            vrTouchStartPosition = touch.position;
        }
        else if (touch.phase == TouchPhase.Ended &&
                 Time.unscaledTime - vrTouchStartTime < 0.4f &&
                 (touch.position - vrTouchStartPosition).magnitude < Screen.width * 0.05f &&
                 gazedButton != null)
        {
            System.Action action = gazedButton.onClick;
            action();
        }
    }

    // The buttons float low down in front of the player (about 50 degrees
    // below eye level), so the view ahead stays clear; look down to use them.
    void PlaceVRPanel(bool snap)
    {
        if (vrPanel == null)
            return;

        float headYaw = transform.eulerAngles.y;

        // Follow the way you face, but hold still while you look down at the
        // buttons so they are easy to aim at.
        bool lookingDown = transform.forward.y < -0.5f;
        if (snap)
            vrPanelYaw = headYaw;
        else if (!lookingDown)
            vrPanelYaw = Mathf.MoveTowardsAngle(vrPanelYaw, headYaw, 180f * Time.deltaTime);

        Vector3 direction = Quaternion.Euler(0f, vrPanelYaw, 0f) * Vector3.forward;
        vrPanel.position = transform.position + direction * vrPanelDistance + Vector3.down * vrPanelDrop;

        // Face the player.
        vrPanel.rotation = Quaternion.LookRotation(vrPanel.position - transform.position);
    }
}
