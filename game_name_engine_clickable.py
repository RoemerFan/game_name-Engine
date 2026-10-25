
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


import functools



from game_name_Engine_screen import ScreenManager, FontManager




# =====================================================================================================================

# region button_kivy
class button_kivy(Widget):
    click_consumed = False

    def __init__(self, text, color=(0.2, 0.6, 0.9, 1), action=None,
                 font_manager=None, press_delay=0.15,
                 pos_hint=None, size_hint=(0.25, 0.08), **kwargs):
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
        self._last_window_size = tuple(Window.size)

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
            self._color_instruction = Color(*self.base_color)
            self.rect = Rectangle(pos=self.pos, size=self.size)

        self.bind(pos=self.update_graphics, size=self.update_graphics)
        Window.bind(mouse_pos=self.on_mouse_pos)
        Window.bind(size=self._on_window_resize)

        Clock.schedule_once(self._freeze_natural_layout, 0)

    def _freeze_natural_layout(self, dt):
        self._natural_size = tuple(self.size)
        self._natural_center = tuple(self.center)
        self.size_hint = (None, None)
        self.pos_hint = {}

    def _on_window_resize(self, window, new_size):
        old_w, old_h = self._last_window_size
        new_w, new_h = new_size
        self._last_window_size = (new_w, new_h)

        if self._natural_size is None or old_w <= 0 or old_h <= 0:
            return

        scale_x = new_w / old_w
        scale_y = new_h / old_h
        self._natural_size = (self._natural_size[0] * scale_x, self._natural_size[1] * scale_y)
        self._natural_center = (self._natural_center[0] * scale_x, self._natural_center[1] * scale_y)
        self.Label.font_size = self.font_manager.scaled_size(0.05)
        self._apply_natural_layout()

    def _apply_natural_layout(self):
        Animation.cancel_all(self, 'size', 'center')
        scale = 1.05 if (self.hovered and not self.pressed) else 1.0
        self.size = (self._natural_size[0] * scale, self._natural_size[1] * scale)
        self.center = self._natural_center

    def set_text(self, text):
        self.text = text
        self.Label.text = text

    def update_graphics(self, *args):
        self.rect.pos = self.pos
        self.rect.size = self.size
        self.Label.pos = self.pos
        self.Label.size = self.size

    def on_mouse_pos(self, window, pos):
        if not self.get_root_window():
            return
        if self.opacity == 0:
            if self.hovered:
                self.hovered = False
                self._update_visual_state()
            return
        inside = self.collide_point(*pos)
        if inside != self.hovered:
            self.hovered = inside
            self._update_visual_state()

    def _update_visual_state(self):
        if self.pressed or self._natural_size is None:
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
                                        min(max(b * factor, 0), 1), a)

        Animation.cancel_all(self, 'size', 'center')
        Animation(size=target_size, center=self._natural_center, duration=0.1).start(self)

    def on_touch_down(self, touch):
        if self.opacity == 0:
            return False
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
        factor = 1.3
        self._color_instruction.rgba = (min(r * factor, 1), min(g * factor, 1), min(b * factor, 1), a)

    def _play_press_animation(self):
        Animation.cancel_all(self, 'size', 'center')

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

def choose_direction(y, h, slot_count):
    down_fits = y - slot_count * h >= 0
    up_fits = y + h + slot_count * h <= 1
    if down_fits or not up_fits:
        return -1
    return 1


def slot_hints(header_hint, header_size, slot_count, direction):
    x, y = header_hint["x"], header_hint["y"]
    h = header_size[1]
    return [{"x": x, "y": y + direction * (i + 1) * h} for i in range(slot_count)]


def lighten(color, amount=0.25):
    r, g, b, a = color
    return (r + (1 - r) * amount, g + (1 - g) * amount, b + (1 - b) * amount, a)


def page_count(item_count, per_page):
    return max(1, -(-item_count // per_page))

class dropdown_kivy(Widget):

    def __init__(self, values=None, placeholder="Auswählen",
                 color=(0.2, 0.6, 0.9, 1), item_color=None, action=None,
                 font_manager=None, press_delay=0.15,
                 pos_hint=None, size_hint=(0.25, 0.08), max_visible=5, **kwargs):
        super().__init__(**kwargs)

        self.size_hint = (None, None)
        self.size = (0, 0)

        self.values = list(values or [])
        self.placeholder = placeholder
        self.value = None
        self.action = action
        self.max_visible = max_visible
        self.is_open = False
        self._page = 0

        header_hint = dict(pos_hint or {"x": 0.1, "y": 0.5})
        header_size = tuple(size_hint)
        item_color = item_color or lighten(color)

        slot_count = max_visible + 1
        direction = choose_direction(header_hint["y"], header_size[1], slot_count)
        hints = slot_hints(header_hint, header_size, slot_count, direction)

        self.header = Button(text=placeholder, color=color, action=self.toggle,
                             font_manager=font_manager, press_delay=press_delay,
                             pos_hint=header_hint, size_hint=header_size)

        self.item_buttons = []
        for slot in range(max_visible):
            self.item_buttons.append(Button(
                text="", color=item_color,
                action=functools.partial(self._on_item_pressed, slot),
                font_manager=font_manager, press_delay=press_delay,
                pos_hint=hints[slot], size_hint=header_size, opacity=0))

        half_size = (header_size[0] / 2, header_size[1])
        paging_hint = hints[max_visible]
        self.prev_button = Button(
            text="<", color=item_color, action=functools.partial(self._change_page, -1),
            font_manager=font_manager, press_delay=press_delay,
            pos_hint=dict(paging_hint), size_hint=half_size, opacity=0)
        self.next_button = Button(
            text=">", color=item_color, action=functools.partial(self._change_page, 1),
            font_manager=font_manager, press_delay=press_delay,
            pos_hint={"x": paging_hint["x"] + half_size[0], "y": paging_hint["y"]},
            size_hint=half_size, opacity=0)

        self._refresh()

    def _all_buttons(self):
        return [self.header] + self.item_buttons + [self.prev_button, self.next_button]

    def on_parent(self, instance, parent):
        for button in self._all_buttons():
            if button.parent is not None:
                button.parent.remove_widget(button)
        if parent is not None:
            for button in self._all_buttons():
                parent.add_widget(button)

    def set_values(self, values):
        self.values = list(values)
        self._page = 0
        if self.value not in self.values:
            self.value = None
            self.header.set_text(self.placeholder)
        if not self.values:
            self.is_open = False
        self._refresh()

    def toggle(self):
        if self.is_open:
            self.close()
        else:
            self.open()

    def open(self):
        if not self.values:
            return
        self.is_open = True
        self._refresh()

    def close(self):
        self.is_open = False
        self._refresh()

    def select(self, value):
        self.value = value
        self.header.set_text(value)
        self.close()
        if self.action:
            self.action(value)

    def _on_item_pressed(self, slot):
        index = self._page * self.max_visible + slot
        if index < len(self.values):
            self.select(self.values[index])

    def _change_page(self, delta):
        pages = page_count(len(self.values), self.max_visible)
        self._page = (self._page + delta) % pages
        self._refresh()

    def _refresh(self):
        for slot, button in enumerate(self.item_buttons):
            index = self._page * self.max_visible + slot
            if self.is_open and index < len(self.values):
                button.set_text(self.values[index])
                button.opacity = 1
            else:
                button.opacity = 0

        show_paging = self.is_open and len(self.values) > self.max_visible
        self.prev_button.opacity = 1 if show_paging else 0
        self.next_button.opacity = 1 if show_paging else 0

    def on_touch_down(self, touch):
        if self.is_open:
            inside = any(button.opacity > 0 and button.collide_point(*touch.pos)
                         for button in self._all_buttons())
            if not inside:
                self.close()
        return False


# =====================================================================================================================


if using_kivy:
    Button = button_kivy

else:
    Button = button




























