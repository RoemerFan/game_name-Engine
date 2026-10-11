


import pygame as pg
from game_name_Engine_screen import screen_manager, font_manager

from kivy.app import App
from kivy.uix.label import Label
from kivy.uix.screenmanager import Screen


class Main(App):
    def build(self):
        return Label(text="Hello, world!")

Main().run()


sm = screen_manager(width=900, height=700, resizable=True, caption="Engine Demo")


clock = pg.time.Clock()


# =====================================================================================================================
# All fonts (except Arial, which is there by default)
# ===================================================

font = sm.load_font(0.04)
times_new_roman = sm.font_manager.get("times_new_roman", fraction=0.05)



running = True
while running:
    for event in pg.event.get():
        if event.type == pg.QUIT:
            running = False
        elif sm.handle_event(event):
            font = sm.load_font(0.04)

    sm.screen.fill((15, 15, 20))

    pg.draw.circle(sm.screen, (220, 60, 60), sm.center, int(sm.height * 0.05))

    label = times_new_roman.render(f"{sm.width} x {sm.height}", True, (255, 255, 255))
    sm.screen.blit(label, label.get_rect(center=(sm.width // 2, 40)))






    fps_text = times_new_roman.render(f"FPS: {int(clock.get_fps())}", True, (255, 255, 255))
    sm.screen.blit(fps_text, fps_text.get_rect(center=(sm.width // 2, 100)))

    pg.display.flip()
    clock.tick(60)



pg.quit()

































