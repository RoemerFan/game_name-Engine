
from game_name_Engine_masterswitch import using_kivy

if using_kivy:
    from kivy.core.window import Window
    from kivy.graphics import Rectangle, Color
    from kivy.uix.widget import Widget
    from kivy.uix.label import Label
    from kivy.metrics import dp
    from kivy.clock import Clock
    from kivy.animation import Animation

else:
    import pygame as pg



from game_name_Engine_screen import ScreenManager, FontManager




# =====================================================================================================================

# region button_kivy
class button_kivy(Widget):

    click_consumed = False

    def __init__(self, text, color=(0.2, 0.6, 0.9, 1), action=None, font_manager=None, press_delay=0.15, pos_hint=None,
                 size_hint=(0.25, 0.08), **kwargs):

        super().__init__(**kwargs)
        self.text = text
        self.base_color = color
        self.action = action
        self.is_just_clicked = False
        self.font_manager = font_manager or FontManager(None)
        self.press_delay = press_delay
        self.hovered = False
        self.pressed = False
        self._natural_size = None
        self._natural_center = None

        self.size_hint = size_hint
        if pos_hint:
            self.pos_hint = pos_hint


        self.Label = Label(
                           text=self.text,
                           font_name=self.font_manager.get("font"),
                           font_size=self.font_manager.scaled_size(0.05),
                           color=(0, 0, 0, 1),
                           pos=self.pos,
                           size=self.size,
                           halign="center",
                           valign="middle",
                           )

        self.add_widget(self.Label)

        with self.canvas:
            self._color_instruction =Color(*self.base_color)
            self.rect = Rectangle(pos=self.pos, size=self.size)

        self.bind(pos=self.update_graphics, size=self.update_graphics)
        Window.bind(mouse_pos=self.on_mouse_pos)

        Clock.schedule_once(self._freeze_natural_layout, 0)

    def _freeze_natural_layout(self, dt):
        self._natural_size = tuple(self.size)
        self._natural_center = tuple(self.center)
        self.size_hint = (None, None)
        self.pos_hint = {}

    def on_mouse_pos(self, window, pos):
        if not self.get_root_window():
            return
        inside = self.collide_point(*pos)
        if inside != self.hovered:
            self.hovered = inside
            self._update_visual_state()

    def _update_visual_state(self):
        if self.pressed:
            return

        r, g, b, a = self.base_color
        if self.hovered:
            factor = 0.8
            target_size = (self._natural_size[0] * 1.05, self._natural_size[1] * 1.05)
        else:
            factor = 1.0
            target_size = self._natural_size

        self._color_instruction.rgba = (min(max(r * factor, 0), 1),
                                        min(max(g * factor, 0), 1),
                                        min(max(b * factor, 0), 1),
                                        a
                                        )

        if self._natural_size is not None:
            Animation.cancel_all(self, "size", "center")
            Animation(size=target_size, center=self._natural_center, duration=0.1).start(self)

    def update_graphics(self, *args):
        self.rect.pos = self.pos
        self.rect.size = self.size
        self.Label.pos = self.pos
        self.Label.size = self.size

    def on_touch_down(self, touch):
        if self.collide_point(*touch.pos):
            if not button_kivy.click_consumed:
                button_kivy.click_consumed = True
                self.pressed = True
                self._apply_press_color()
                self._play_press_animation()
                Clock.schedule_once(self._trigger_action, self.press_delay)
            return True
        return super().on_touch_down(touch)

    def _apply_press_color(self):
        r, g, b, a = self.base_color
        factor = 1.2
        self._color_instruction.rgba = (min(r * factor, 1), min(g * factor, 1), min(b * factor, 1), a)

    def _play_press_animation(self):
        Animation.cancel_all(self, "size", "center")

        if self._natural_size is None:
            self._natural_size = tuple(self.size)
            self._natural_center = tuple(self.center)

        shrink_size = (self._natural_size[0] * 0.92, self._natural_size[1] * 0.92)

        shrink = Animation(size=shrink_size, center=self._natural_center, duration=0.08)
        grow = Animation(size=self._natural_size, center=self._natural_center, duration=0.08)
        (shrink + grow).start(self)

    def _trigger_action(self, dt):
        self.pressed = False
        self._update_visual_state()
        self.is_just_clicked = True
        if self.action:
            self.action()

    def on_touch_up(self, touch):
        if button_kivy.click_consumed:
            button_kivy.click_consumed = False
        return super().on_touch_up(touch)
# endregion


# =====================================================================================================================

class button():
    1


# =====================================================================================================================

if using_kivy:
    Button = button_kivy

else:
    Button = button




























