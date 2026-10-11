"""
PC-Programm (PyCharm) – PS5 Controller steuert Pico per WLAN
============================================================
Benoetigt:
    pip install pygame requests hid

Steuerung:
    X-Taste (Kreuz)  → Lauflicht EIN  (Controller leuchtet gruen)
    Dreieck-Taste    → Lauflicht AUS  (Controller leuchtet rot)
    Waehrend AN      → Controller blinkt weiss im Takt

Taste-Index auf DualSense (pygame):
    0 = Kreuz (X)
    3 = Dreieck
"""

import pygame
import requests
import threading
import time
import hid  # pip install hid

# ─── EINSTELLUNGEN ───────────────────────────────────────────────
PICO_IP = "192.168.178.65"   # <-- IP-Adresse des Pico eintragen (steht im Pico-Terminal)
BLINK_INTERVALL = 0.15     # Sekunden – sollte mit LAUFLICHT_DELAY uebereinstimmen

# TEST-MODUS:
#   True  → Kein Signal an den Pico, nur Controller-LEDs testen
#   False → Normalbetrieb, Befehle werden per WLAN an den Pico gesendet
TEST = False
# ─────────────────────────────────────────────────────────────────

PICO_URL_AN  = f"http://{PICO_IP}/an"
PICO_URL_AUS = f"http://{PICO_IP}/aus"

# DualSense USB Vendor/Product ID
DUALSENSE_VID = 0x054C
DUALSENSE_PID = 0x0CE6

# DualSense Lichtfarben (RGB 0-255)
FARBE_AN      = (0,   255, 0)    # Gruen
FARBE_AUS     = (255, 0,   0)    # Rot
FARBE_BLINK   = (255, 255, 255)  # Weiss
FARBE_AUS_DIM = (0,   0,   0)    # Aus (beim Blinken)

lauflicht_aktiv = False

# ─── DualSense LED per HID ───────────────────────────────────────

def oeffne_dualsense_hid():
    """Sucht und oeffnet den DualSense Controller ueber HID."""
    try:
        geraet = hid.device()
        geraet.open(DUALSENSE_VID, DUALSENSE_PID)
        geraet.set_nonblocking(True)
        print("[HID] DualSense HID-Verbindung hergestellt.")
        return geraet
    except Exception as e:
        print(f"[HID] Konnte DualSense nicht oeffnen: {e}")
        print("[HID] Tipp: Controller per USB verbinden oder als Admin ausfuehren.")
        return None


def set_controller_farbe(hid_geraet, r: int, g: int, b: int):
    """
    Setzt die Lightbar-Farbe des DualSense per HID-Report (USB).
    Report-ID 0x02, Byte 45 = R, 46 = G, 47 = B (USB-Modus).
    """
    if hid_geraet is None:
        print(f"[LED] Kein HID-Geraet – Farbe ({r},{g},{b}) nicht gesetzt.")
        return
    try:
        # 64-Byte Output-Report fuer DualSense (USB)
        report = [0] * 64
        report[0]  = 0x02          # Report-ID
        report[1]  = 0xFF          # Feature-Flags: alle Features aktiv
        report[2]  = 0x15          # Lightbar aktivieren
        report[45] = r
        report[46] = g
        report[47] = b
        hid_geraet.write(report)
    except Exception as e:
        print(f"[LED] Fehler beim Setzen der Farbe: {e}")


# ─── HTTP ────────────────────────────────────────────────────────

def sende_befehl(url: str) -> bool:
    """Sendet HTTP-GET an den Pico – oder simuliert es im TEST-Modus."""
    if TEST:
        print(f"[TEST] Kein Signal gesendet (TEST=True). Ziel waere: {url}")
        return True
    try:
        r = requests.get(url, timeout=2)
        print(f"[HTTP] → {url}  Antwort: {r.text}")
        return True
    except requests.exceptions.RequestException as e:
        print(f"[HTTP] Fehler: {e}")
        return False


# ─── Blink-Thread ────────────────────────────────────────────────

def blink_loop(hid_geraet):
    """Blinkt weiss im Takt, solange Lauflicht aktiv ist."""
    global lauflicht_aktiv
    hell = True
    while lauflicht_aktiv:
        if hell:
            set_controller_farbe(hid_geraet, *FARBE_BLINK)
        else:
            set_controller_farbe(hid_geraet, *FARBE_AUS_DIM)
        hell = not hell
        time.sleep(BLINK_INTERVALL)


# ─── Hauptprogramm ───────────────────────────────────────────────

def main():
    global lauflicht_aktiv

    pygame.init()
    pygame.joystick.init()

    if pygame.joystick.get_count() == 0:
        print("❌ Kein Controller gefunden! PS5 Controller per USB verbinden.")
        return

    joystick = pygame.joystick.Joystick(0)
    joystick.init()
    print(f"✅ Controller erkannt: {joystick.get_name()}")

    if TEST:
        print("⚠️  TEST-MODUS aktiv – kein Signal wird an den Pico gesendet!")
    else:
        print(f"🌐 Normalbetrieb – Pico IP: {PICO_IP}")

    # HID-Verbindung fuer LED
    hid_geraet = oeffne_dualsense_hid()

    # Startzustand: rot (AUS)
    set_controller_farbe(hid_geraet, *FARBE_AUS)

    print("\nSteuerung:")
    print("  X (Kreuz)   → Lauflicht EIN  (Controller gruen + weiss blinkend)")
    print("  Dreieck     → Lauflicht AUS  (Controller rot)")
    print("  ESC / Q     → Beenden\n")

    clock = pygame.time.Clock()
    taste_x_vorher       = False
    taste_dreieck_vorher = False

    running = True
    while running:
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                running = False
            if event.type == pygame.KEYDOWN:
                if event.key in (pygame.K_ESCAPE, pygame.K_q):
                    running = False

        pygame.event.pump()
        taste_x       = joystick.get_button(0)   # Kreuz
        taste_dreieck = joystick.get_button(3)   # Dreieck

        # X gedrueckt → EIN
        if taste_x and not taste_x_vorher:
            if not lauflicht_aktiv:
                print("▶  Lauflicht EIN")
                if sende_befehl(PICO_URL_AN):
                    lauflicht_aktiv = True
                    set_controller_farbe(hid_geraet, *FARBE_AN)
                    time.sleep(2)
                    t = threading.Thread(target=blink_loop, args=(hid_geraet,), daemon=True)
                    t.start()

        # Dreieck gedrueckt → AUS
        if taste_dreieck and not taste_dreieck_vorher:
            if lauflicht_aktiv:
                print("⏹  Lauflicht AUS")
                lauflicht_aktiv = False
                time.sleep(BLINK_INTERVALL * 2)  # Warten bis Blink-Thread fertig
                if sende_befehl(PICO_URL_AUS):
                    set_controller_farbe(hid_geraet, *FARBE_AUS)

        taste_x_vorher       = taste_x
        taste_dreieck_vorher = taste_dreieck

        clock.tick(60)

    # Aufraeumen
    lauflicht_aktiv = False
    time.sleep(BLINK_INTERVALL * 2)
    set_controller_farbe(hid_geraet, 0, 0, 0)
    if hid_geraet:
        hid_geraet.close()
    pygame.quit()
    print("Programm beendet.")


if __name__ == "__main__":
    main()
