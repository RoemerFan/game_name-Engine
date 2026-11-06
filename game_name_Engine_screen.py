


from game_name_Engine_masterswitch import using_kivy

if using_kivy:
    from kivy.core.window import Window
    from kivy.graphics import Rectangle, Color
    from kivy.uix.widget import Widget
    from kivy.clock import Clock
    from kivy.core.text import Label as KivyLabel
    from kivy.uix.label import Label

else:
    import pygame as pg






# =====================================================================================================================

class KivyScreenManager(Widget):
    def __init__(self, width=1280, height=720, resizable=True, fullscreen=False, caption="game_name_Engine",
                 min_width=640, min_height=480, **kwargs):

        super().__init__(**kwargs)

        self.resizable = resizable
        self.min_width = min_width
        self.min_height = min_height

        if fullscreen:
            from kivy.core.window import Window as _W
            width, height = _W.system_size

        Window.size = (width, height)
        self.width, self.height = Window.size

        Clock.schedule_interval(self.update, 1/60)

    def update(self, dt):
        pass

    @property
    def size(self):
        return self.width, self.height

    @property
    def center(self):
        return self.width // 2, self.height // 2




# =====================================================================================================================


class screen_manager:
    def __init__(self, width=1280, height=720, resizable=True, fullscreen=False,
                 caption="game_name_Engine", min_width=640, min_height=480):

        pg.init()

        self.resizable = resizable
        self.min_width = min_width
        self.min_height = min_height

        flags = 0
        if resizable:
            flags |= pg.RESIZABLE

        if fullscreen:
            info = pg.display.Info()
            width, height = info.current_w, info.current_h
            flags |= pg.FULLSCREEN

        self._flags = flags
        self.width = width
        self.height = height
        self.screen = pg.display.set_mode((self.width, self.height), flags)
        pg.display.set_caption(caption)

        self.font_manager = font_manager(self)
        # Add fonts here
        self.font_manager.add("times_new_roman", "system:Times New Roman")


    def handle_event(self, event):
        if self.resizable and event.type == pg.VIDEORESIZE:
            self.width = max(self.min_width, event.w)
            self.height = max(self.min_height, event.h)
            self.screen = pg.display.set_mode((self.width, self.height), self._flags)
            return True
        return False

    @property
    def size(self):
        return self.width, self.height

    @property
    def center(self):
        return self.width // 2, self.height // 2

    def scaled_font_size(self, fraction_of_height, min_size=10):
        return max(min_size, int(self.height * fraction_of_height))

    def load_font(self, fraction_of_height, name=None, min_size=10):
        size = self.scaled_font_size(fraction_of_height, min_size)
        return pg.font.SysFont(name, size)


# =====================================================================================================================

class KivyFontManager:
    def __init__(self, screen_manager):
        self.screen_manager = screen_manager
        self.fonts = {
                      "font": "Arial",
                      }

    def add(self, name, path):
        self.fonts[name] = path

    def scaled_size(self, fraction, min_size=10):
        return max(int(Window.height * fraction), min_size)

    def get(self, name="font"):

        return self.fonts.get(name, "Arial")

        if not path:
            path = "Arial"


# =====================================================================================================================

# region Font_Manager
class font_manager:
    def __init__(self, screen_manager):

        self.screen_manager = screen_manager

        self.fonts = {
                      "font": "Arial",
                      }

    def add(self, name, path):
        self.fonts[name] = path

    def scaled_size(self, fraction, min_size=10):
        return max(int(self.screen_manager.height * fraction), min_size)

    def get(self, name="font", fraction=0.05, min_size=10):
        size = self.scaled_size(fraction, min_size)
        path = self.fonts.get(name)

        if path is None:
            return pg.font.SysFont("Arial", size)

        if isinstance(path, str) and path.startswith("system:"):
            font_name = path.split("system:")[1]
            return pg.font.SysFont(font_name, size)


        try:
            return pg.font.Font(path, size)
        except FileNotFoundError:
            return pg.font.SysFont("Arial", size)

    def exists(self, name):
        return name in self.fonts
# endregion


# =====================================================================================================================

if using_kivy:
    ScreenManager = KivyScreenManager
    FontManager = KivyFontManager

else:
    ScreenManager = screen_manager
    FontManager = font_manager









