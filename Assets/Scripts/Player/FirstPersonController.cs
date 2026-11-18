using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Spielerbewegung (Ich-Perspektive). Neu gegenueber der alten Version:
//  - Rechter Stick: umschauen (Controller).
//  - Linker Stick: laufen (zusaetzlich zu WASD).
//  - Leitern: einfach hinlaufen und vorwaerts druecken, dann klettert man "fliessend"
//    (siehe Ladder.cs). Oben laeuft man ueber die oberste Sprosse auf die Plattform,
//    unten steht man wieder auf dem Boden.
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float jumpHeight = 1.5f;
    public float gravity = -20f;

    // Von anderen Skripten gesetzt (z. B. Schubkarre): Tempo-Faktor und Sprung erlaubt?
    [HideInInspector] public float speedMultiplier = 1f;
    [HideInInspector] public bool jumpEnabled = true;

    [Header("Mouse Look")]
    public Transform playerCamera;
    public float mouseSensitivity = 2f;
    public float maxLookAngle = 85f;

    [Header("Controller")]
    [Tooltip("Drehgeschwindigkeit des rechten Sticks bei voller Auslenkung (Grad pro Sekunde).")]
    public float stickDegreesPerSecond = 140f;

    [Tooltip("Stick-Bewegungen darunter werden ignoriert (gegen Drift).")]
    public float stickDeadzone = 0.15f;

    [Tooltip("Hoch/runter beim rechten Stick umkehren.")]
    public bool invertStickY = false;

    [Header("Jump Feel")]
    [Tooltip("So lange (Sekunden) darf man nach dem Verlassen des Bodens noch springen.")]
    public float coyoteTime = 0.12f;

    [Tooltip("So lange (Sekunden) vor der Landung wird ein Sprung-Tastendruck noch gemerkt.")]
    public float jumpBufferTime = 0.15f;

    private CharacterController controller;
    private Vector3 velocity;
    private float cameraPitch;
    private bool grounded;
    private float coyoteTimer;
    private float jumpBufferTimer;

    // ---------- Leiter ----------
    private Ladder ladder;            // Leiter, in deren Zone wir gerade stehen
    private bool climbing;            // Wir haengen gerade an der Leiter.
    private float climbCooldown;      // Kurze Sperre, damit man nicht sofort wieder einsteigt.
    private bool invertUntilRelease;  // Einstieg von oben: Taste "vorwaerts" bedeutet erst mal "runter".
    private Vector3 pushVelocity;     // Schub beim Absprung / beim Ueberstieg oben.
    private float pushTimer;

    void Start()
    {
        controller = GetComponent<CharacterController>();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        LookAround();
        MovePlayer();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Input.GetMouseButtonDown(0) &&
            Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // Wird von der Leiter (Ladder.cs) aufgerufen, wenn wir in ihre Zone laufen / sie verlassen.
    public void EnterLadderZone(Ladder l)
    {
        ladder = l;
    }

    public void ExitLadderZone(Ladder l)
    {
        if (ladder != l)
            return;

        ladder = null;
        climbing = false;
    }

    void LookAround()
    {
        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        float mouseX = Input.GetAxis("Mouse X")
                       * mouseSensitivity;

        float mouseY = Input.GetAxis("Mouse Y")
                       * mouseSensitivity;

#if ENABLE_INPUT_SYSTEM
        // Rechter Stick (Controller): wirkt wie eine Mausbewegung pro Sekunde.
        Gamepad pad = Gamepad.current;

        if (pad != null)
        {
            Vector2 stick = pad.rightStick.ReadValue();
            float magnitude = stick.magnitude;

            if (magnitude > stickDeadzone)
            {
                // Totzone abziehen, danach weich bei 0 anfangen.
                stick = stick.normalized * ((magnitude - stickDeadzone) / (1f - stickDeadzone));

                float degrees = stickDegreesPerSecond * Time.deltaTime;

                mouseX += stick.x * degrees;
                mouseY += (invertStickY ? -stick.y : stick.y) * degrees;
            }
        }
#endif

        transform.Rotate(Vector3.up * mouseX);

        cameraPitch -= mouseY;
        cameraPitch = Mathf.Clamp(
            cameraPitch, -maxLookAngle, maxLookAngle
        );

        playerCamera.localRotation =
            Quaternion.Euler(cameraPitch, 0f, 0f);
    }

    // Laufen: Tastatur (WASD) und linker Stick, zusammen auf Laenge 1 begrenzt.
    Vector2 ReadMoveInput()
    {
        Vector2 input = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical"));

#if ENABLE_INPUT_SYSTEM
        Gamepad pad = Gamepad.current;

        if (pad != null)
        {
            Vector2 stick = pad.leftStick.ReadValue();

            if (stick.magnitude > 0.2f)
                input += stick;
        }
#endif

        return Vector2.ClampMagnitude(input, 1f);
    }

    bool ReadJumpPressed()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            return true;

#if ENABLE_INPUT_SYSTEM
        Gamepad pad = Gamepad.current;

        if (pad != null && pad.buttonSouth.wasPressedThisFrame)
            return true;
#endif

        return false;
    }

    void MovePlayer()
    {
        Vector2 input = ReadMoveInput();
        float x = input.x;
        float z = input.y;
        bool jumpPressed = ReadJumpPressed();

        climbCooldown -= Time.deltaTime;

        // ----- An der Leiter? -----
        if (ladder != null && ladder.IsValid)
        {
            if (!climbing)
                TryStartClimb(z);

            if (climbing)
            {
                ClimbLadder(x, z, jumpPressed);
                return;
            }
        }

        // ----- Normales Laufen -----
        Vector3 move =
            Vector3.ClampMagnitude(transform.right * x + transform.forward * z, 1f);

        // Schub vom Absprung / Ueberstieg oben (laeuft nach kurzer Zeit aus).
        if (pushTimer > 0f)
        {
            pushTimer -= Time.deltaTime;
            move += pushVelocity / moveSpeed;
        }

        float dt = Time.deltaTime;

        // Sprung-Puffer: Ein Tastendruck kurz VOR der Landung zaehlt noch.
        if (jumpPressed)
            jumpBufferTimer = jumpBufferTime;
        else
            jumpBufferTimer -= dt;

        // Boden-Toleranz: Kurz nach dem Verlassen des Bodens (Kante, Huckel) darf man noch springen.
        if (grounded)
            coyoteTimer = coyoteTime;
        else
            coyoteTimer -= dt;

        // Am Boden leicht nach unten druecken, damit man am Boden "kleben" bleibt.
        if (grounded && velocity.y < 0f)
        {
            velocity.y = -2f;
        }

        if (jumpEnabled && jumpBufferTimer > 0f && coyoteTimer > 0f)
        {
            velocity.y = Mathf.Sqrt(
                jumpHeight * -2f * gravity
            );

            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
        }

        velocity.y += gravity * dt;

        // WICHTIG: Nur EINE Bewegung pro Bild (waagerecht + senkrecht zusammen). Bei zwei
        // getrennten Move-Aufrufen meldet isGrounded oft "false", und der Sprung geht verloren.
        controller.Move((move * moveSpeed * speedMultiplier + velocity) * dt);

        grounded = controller.isGrounded;
    }

    // ---------- Klettern ----------

    // Einstieg: Von unten reicht es, auf die Leiter zuzulaufen. Von oben laeuft man
    // rueckwaerts bzw. zur Kante hin und steigt dann an der Kante ein.
    void TryStartClimb(float z)
    {
        if (climbCooldown > 0f || z <= 0.1f)
            return;

        float feetY = controller.bounds.min.y;

        Vector3 flat = transform.forward;
        flat.y = 0f;
        flat.Normalize();

        float facingDot = Vector3.Dot(flat, ladder.Facing);

        // Abstand zur Sprossenlinie in Richtung Wand: negativ = vor der Leiter, positiv = oben auf der Plattform.
        float d = Vector3.Dot(transform.position - ladder.BottomPosition, ladder.Facing);

        bool fromBelow = facingDot > 0.5f
                         && d < 0f
                         && feetY < ladder.TopY - 0.3f
                         && feetY > ladder.BottomY - 0.3f;

        bool fromAbove = facingDot < -0.5f
                         && d < 0.25f && d > -0.8f
                         && feetY >= ladder.TopY - 0.3f;

        if (!fromBelow && !fromAbove)
            return;

        climbing = true;
        velocity = Vector3.zero;
        pushTimer = 0f;
        grounded = false;
        coyoteTimer = 0f;

        if (fromAbove)
        {
            // Zur Leiter umdrehen; die gedrueckte "vorwaerts"-Taste bedeutet jetzt "runter",
            // bis sie einmal losgelassen wurde.
            transform.rotation = Quaternion.LookRotation(ladder.Facing, Vector3.up);
            invertUntilRelease = true;
        }
        else
        {
            invertUntilRelease = false;
        }
    }

    // Beim Klettern: Vorwaerts = hoch, rueckwaerts = runter (egal wohin man schaut). Ohne Eingabe
    // haengt man fest an der Leiter. Springen = abspringen.
    void ClimbLadder(float x, float z, bool jumpPressed)
    {
        float dt = Time.deltaTime;

        // Nach dem Einstieg von oben: gehaltene Vorwaerts-Taste = weiter nach unten.
        if (invertUntilRelease)
        {
            if (z > 0.1f)
                z = -z;
            else
                invertUntilRelease = false;
        }

        // Abspringen: kleiner Hopser von der Leiter weg.
        if (jumpPressed)
        {
            climbing = false;
            climbCooldown = 0.5f;
            velocity.y = 3f;
            pushVelocity = -ladder.Facing * 2.5f;
            pushTimer = 0.4f;
            return;
        }

        float feetY = controller.bounds.min.y;

        // Oben angekommen: ueber die oberste Sprosse auf die Plattform steigen.
        if (z > 0.1f && feetY >= ladder.TopY - 0.05f)
        {
            climbing = false;
            climbCooldown = 0.5f;
            velocity.y = 3f;
            pushVelocity = ladder.Facing * (moveSpeed * 0.6f);
            pushTimer = 0.5f;
            return;
        }

        // Unten angekommen: loslassen, man steht wieder auf dem Boden.
        if (z < -0.1f && feetY <= ladder.BottomY + 0.05f)
        {
            climbing = false;
            climbCooldown = 0.3f;
            velocity.y = -2f;
            return;
        }

        // Weich an der Leiterlinie bleiben (Abstand zu den Sprossen), seitlich darf man etwas rutschen.
        Vector3 stand = ladder.StandPoint(controller.radius);
        float error = Vector3.Dot(stand - transform.position, ladder.Facing);

        Vector3 move =
            ladder.Facing * (error * Mathf.Clamp01(10f * dt))
            + transform.right * (x * moveSpeed * 0.3f * dt)
            + Vector3.up * (z * ladder.ClimbSpeed * dt);

        controller.Move(move);
        velocity.y = 0f;
    }
}