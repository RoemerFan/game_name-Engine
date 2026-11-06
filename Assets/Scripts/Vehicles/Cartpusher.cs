using UnityEngine;

// Auf den Spieler haengen. Mit dem Fadenkreuz auf eine Schubkarre zielen und F druecken:
// Die Karre wird gegriffen und vor dem Spieler hergeschoben. Nochmal F laesst sie los.
//
// Waehrend man schiebt, ist man je nach Ladung langsamer und kann nicht springen.
// Gegenstaende nimmt man weiter mit E auf und legt sie mit E oder R in die Karre.
public class CartPusher : MonoBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private FirstPersonController controller;
    [SerializeField] private KeyCode grabKey = KeyCode.F;
    [SerializeField] private float reachDistance = 3.5f;

    private Wheelbarrow held;
    private Wheelbarrow aimed;
    private CharacterController character;

    // Mehrspieler: Auf die Bestaetigung des Hosts warten.
    private Wheelbarrow pending;
    private float pendingTimer;

    private void Start()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;

        if (controller == null)
            controller = GetComponent<FirstPersonController>();

        character = GetComponent<CharacterController>();

        if (playerCamera == null)
        {
            Debug.LogError("CartPusher: Kamera fehlt!", this);
            enabled = false;
        }
    }

    private void Update()
    {
        if (SettingsMenu.IsOpen)
            return;

        // Auf die Freigabe des Hosts warten.
        if (pending != null)
        {
            pendingTimer -= Time.deltaTime;
            NetItem net = pending.GetComponent<NetItem>();

            if (net != null && net.HeldByMe && held == null)
            {
                StartPushing(pending);
                pending = null;
            }
            else if (pendingTimer <= 0f)
            {
                pending = null;
            }
        }

        if (held != null)
        {
            aimed = null;

            if (GameInput.Pressed(GameAction.Interact, grabKey))
                StopPushing();

            return;
        }

        aimed = Cursor.lockState == CursorLockMode.Locked ? FindWheelbarrow() : null;

        if (aimed != null && pending == null && GameInput.Pressed(GameAction.Interact, grabKey))
            TryGrab(aimed);
    }

    private void LateUpdate()
    {
        if (held == null)
            return;

        // Karre verschwunden (abgerissen, Spielstand geladen ...): Zustand zuruecksetzen.
        if (!held.IsPushed)
        {
            StopPushing();
            return;
        }

        // Steht die Karre an einer Wand, wird der Spieler ein Stueck zurueckgesetzt.
        Vector3 pushBack = held.PushUpdate(
            FeetPosition(), playerCamera.transform.eulerAngles.y, Time.deltaTime);

        if (pushBack.sqrMagnitude > 0f && character != null && character.enabled)
            character.Move(pushBack);

        if (controller != null)
            controller.speedMultiplier = held.SpeedFactor;
    }

    private void OnDisable()
    {
        if (held != null)
            StopPushing();
    }

    private Vector3 FeetPosition()
    {
        if (character == null)
            return transform.position;

        return transform.position + character.center - Vector3.up * (character.height * 0.5f);
    }

    private Wheelbarrow FindWheelbarrow()
    {
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (!Physics.Raycast(ray, out RaycastHit hit, reachDistance, ~0, QueryTriggerInteraction.Ignore))
            return null;

        Wheelbarrow wb = hit.collider.GetComponentInParent<Wheelbarrow>();

        if (wb == null || !wb.enabled || wb.IsPushed)
            return null;

        // Hat sie gerade jemand anderes in der Hand?
        NetItem net = wb.GetComponent<NetItem>();

        if (net != null && net.HeldBy >= 0 && net.HeldBy != NetSync.LocalId)
            return null;

        return wb;
    }

    private void TryGrab(Wheelbarrow wb)
    {
        NetItem net = wb.GetComponent<NetItem>();

        if (net != null && NetSync.IsOnline)
        {
            if (!NetItems.BeginGrab(net))
            {
                pending = wb;
                pendingTimer = 1.5f;
                return;
            }
        }

        StartPushing(wb);
    }

    private void StartPushing(Wheelbarrow wb)
    {
        held = wb;
        wb.BeginPush(playerCamera.transform.eulerAngles.y, character);

        if (controller != null)
        {
            controller.speedMultiplier = wb.SpeedFactor;
            controller.jumpEnabled = false;
        }
    }

    private void StopPushing()
    {
        if (held == null)
            return;

        Wheelbarrow wb = held;
        held = null;

        if (wb != null)
        {
            wb.EndPush();

            NetItem net = wb.GetComponent<NetItem>();

            if (net != null)
                NetItems.Release(net);
        }

        if (controller != null)
        {
            controller.speedMultiplier = 1f;
            controller.jumpEnabled = true;
        }
    }

    private void OnGUI()
    {
        if (SettingsMenu.IsOpen || Cursor.lockState != CursorLockMode.Locked)
            return;

        string key = "[" + GameInput.Hint(GameAction.Interact, grabKey) + "]  ";

        if (held != null)
        {
            string text = key + Loc.T("cart.release");

            if (held.CargoKg > 0.01f)
                text += "   |   " + Loc.T("cart.load", held.CargoKg.ToString("F0"), held.maxLoadKg.ToString("F0"));

            GameSettings.DrawBoxBelowCenter(text);
        }
        else if (aimed != null)
        {
            GameSettings.DrawBoxBelowCenter(key + Loc.T("cart.grab"));
        }
    }
}