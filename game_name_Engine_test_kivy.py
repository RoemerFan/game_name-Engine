
from game_name_Engine_masterswitch import using_kivy


if using_kivy:

    from kivy.app import App
    from kivy.uix.label import Label
    from kivy.uix.screenmanager import Screen
    from kivy.uix.floatlayout import FloatLayout
    from kivy.core.window import Window
    from kivy.clock import Clock

else:

    import pygame as pg


from game_name_Engine_screen import ScreenManager, FontManager
from game_name_engine_clickable import Button


# =====================================================================================================================

sm = ScreenManager(width=900, height=700, resizable=True, caption="Engine Demo")


class TestScreen(FloatLayout):
    def __init__(self, **kwargs):
        super().__init__(**kwargs)

        self.FontManager = FontManager(self)

        self.ids = {}
        self.ids["status_label"] = Label(
                                         text="Waiting for click...",
                                         pos=(Window.width * 0.35, Window.height * 0.3),
                                         size_hint=(None, None),
                                         size=(Window.width * 0.3, Window.height * 0.1),
                                         font_name=self.FontManager.fonts.get("font"),
                                         font_size=self.FontManager.scaled_size(0.05)
                                         )

        self.add_widget(self.ids["status_label"])


        btn = Button(text="Test Button", color=(0.3, 0.6, 0.9, 1), font_manager=self.FontManager, action=self.on_button_click)

        btn.pos = (Window.width * 0.35, Window.height * 0.45)
        self.add_widget(btn)



    def on_button_click(self):
        print("Button clicked")
        self.ids["status_label.text"] = "Button clicked"


class TestApp(App):
    def build(self):
        return TestScreen()

if __name__ == "__main__":
    TestApp().run()





































