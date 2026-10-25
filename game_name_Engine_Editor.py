import json
import os

from kivy.app import App
from kivy.uix.floatlayout import FloatLayout
from kivy.uix.widget import Widget
from kivy.uix.label import Label
from kivy.uix.textinput import TextInput
from kivy.graphics import Color, Rectangle
from kivy.core.window import Window
from kivy.metrics import dp
from kivy.clock import Clock


LAYOUT_FILENAME = "layout.json"
HANDLE_SIZE = dp(32)

NORMAL_FONT_FRACTION = 0.025
SMALL_FONT_FRACTION = 0.02


def scaled_font_size(fraction, min_size=10):
    return max(min_size, int(Window.height * fraction))

def get_layout_file_path():
    app = App.get_running_app()
    if app is not None:
        return os.path.join(app.user_data_dir, LAYOUT_FILENAME)
    return LAYOUT_FILENAME


def hex_to_rgba(hex_str, alpha=1.0):
    hex_str = hex_str.strip().lstrip('#')
    if len(hex_str) == 6:
        r = int(hex_str[0:2], 16) / 255
        g = int(hex_str[2:4], 16) / 255
        b = int(hex_str[4:6], 16) / 255
        return (r, g, b, alpha)
    if len(hex_str) == 8:
        r = int(hex_str[0:2], 16) / 255
        g = int(hex_str[2:4], 16) / 255
        b = int(hex_str[4:6], 16) / 255
        a = int(hex_str[6:8], 16) / 255
        return (r, g, b, a)
    raise ValueError("Invalid hex string; Colour must have 6 or 8 digits")


def rgba_to_hex(rgba):
    r, g, b = rgba[0], rgba[1], rgba[2]
    return "{:02x}{:02x}{:02x}".format(int(r * 255), int(g * 255), int(b * 255))


class draggable_box(Widget):
    def __init__(self, name, pos_hint=None, size_hint=(0.2, 0.08), color=(0.3, 0.6, 0.9, 1), font_size=20,
                 font_name="Roboto", text_color=(0, 0, 0, 1), select_callback=None, **kwargs):

        super().__init__(**kwargs)
        self.name = name
        self.box_color = color
        self.font_size = font_size
        self.font_name = font_name or "Roboto"
        self.text_color = text_color
        self.select_callback = select_callback

        self.size_hint = (None, None)
        self.pos_hint = {}

        self._dragging = False
        self._resizing = False
        self._touch_offset = (0, 0)

        with self.canvas:
            self._color_instruction = Color(*self.box_color)
            self._rect = Rectangle(pos=self.pos, size=self.size)
            Color(1, 1, 1, 0.6)
            self._handle = Rectangle(pos=self._handle_pos(), size=(HANDLE_SIZE, HANDLE_SIZE))

        self.label = Label(text=self.name, pos=self.pos, size=self.size, font_size=self.font_size,
                           font_name=self.font_name, color=self.text_color)

        self.add_widget(self.label)

        self.bind(pos=self._redraw, size=self._redraw)

    def _handle_pos(self):
        return self.x + self.width - HANDLE_SIZE, self.y

    def _on_change(self, *args):
        self._redraw()
        self._notify_select()

    def _redraw(self, *args):
        self._rect.pos = self.pos
        self._rect.size = self.size
        self._handle.pos = self._handle_pos()
        self._handle.size = (HANDLE_SIZE, HANDLE_SIZE)
        self.label.pos = self.pos
        self.label.size = self.size

    def _notify_select(self):
        if self.select_callback:
            self.select_callback(self)

    def scale_font(self, factor):
        self.font_size = max(6, int(self.font_size * factor))
        self.label.font_size = self.font_size

    def set_style(self, font_size=None, font_name=None, text_color=None, box_color=None):
        if font_size is not None:
            self.font_size = font_size
            self.label.font_size = font_size
        if font_name is not None:
            self.font_name = font_name
            self.label.font_name = font_name
        if text_color is not None:
            self.text_color = text_color
            self.label.color = text_color
        if box_color is not None:
            self.box_color = box_color
            self._color_instruction.rgba = box_color
        self._notify_select()

    def on_touch_down(self, touch):
        handle_rect = (self.x + self.width - HANDLE_SIZE, self.y, HANDLE_SIZE, HANDLE_SIZE)
        hx, hy, hw, hh = handle_rect
        if hx <= touch.x <= hx + hw and hy <= touch.y <= hy + hh:
            self._resizing = True
            touch.grab(self)
            self._notify_select()
            return True

        if self.collide_point(*touch.pos):
            self._dragging = True
            self._touch_offset = (touch.x - self.x, touch.y - self.y)
            touch.grab(self)
            self._notify_select()
            return True

        return super().on_touch_down(touch)

    def on_touch_move(self, touch):
        if touch.grab_current is not self:
            return super().on_touch_move(touch)

        if self._resizing:
            new_w = max(40, touch.x - self.x)
            new_h = max(30, touch.y - self.y)
            self.size = (new_w, new_h)
            self._notify_select()
        elif self._dragging:
            self.pos = (touch.x - self._touch_offset[0], touch.y - self._touch_offset[1])
            self._notify_select()
        return True

    def on_touch_up(self, touch):
        if touch.grab_current is self:
            touch.ungrab(self)
            self._dragging = False
            self._resizing = False
            return True
        return super().on_touch_up(touch)

    def export(self, parent_width, parent_height):
        return {
                "pos_hint": {
                             "x": round(self.x / parent_width, 4),
                             "y": round(self.y / parent_height, 4),
                             },
                "size_hint": [
                              round(self.width / parent_width, 4),
                              round(self.height / parent_height, 4),
                              ],
                "font_size": self.font_size,
                "font_name": self.font_name,
                "text_color": self.text_color,
                "color": list(self.box_color)
                }

class layout_root(FloatLayout):
    def __init__(self, **kwargs):
        super().__init__(**kwargs)
        self.boxes = {}
        self._box_counter = 0
        self.selected_box = None
        self._last_window_size = tuple(Window.size)
        Window.bind(size=self._on_window_resize)

        editor_font_size = scaled_font_size(NORMAL_FONT_FRACTION)

        self.name_input = TextInput(
                                    text="", hint_text="Name (e.g. save_button)",
                                    size_hint=(0.25, 0.05), pos_hint={"x": 0.0, "y": 0.95},
                                    multiline=False, font_size=editor_font_size
                                    )

        self.add_widget(self.name_input)

        self.add_btn = Label(text="[ref=add]+ Element[/ref]", markup=True, size_hint=(0.12, 0.05),
                        pos_hint={"x": 0.26, "y": 0.95}, color=(0.3, 1, 0.3, 1), font_size=editor_font_size)

        self.add_btn.bind(on_ref_press=lambda *a: self.add_box())
        self.add_widget(self.add_btn)

        self.save_btn = Label(text="[ref=save]Save[/ref]", markup=True, size_hint=(0.12, 0.05),
                         pos_hint={"x": 0.40, "y": 0.95},
                         color=(1, 0.8, 0.3, 1), font_size=editor_font_size
                         )

        self.save_btn.bind(on_ref_press=lambda *a: self.save_layout())
        self.add_widget(self.save_btn)

        self.status_label = Label(text="", size_hint=(0.3, 0.05), pos_hint={"x": 0.55, "y": 0.95},
                                  color=(1, 1, 1, 1), font_size=editor_font_size
                                  )
        self.add_widget(self.status_label)

        self.coords_label = Label(text="No Element selected", size_hint=(0.6, 0.04),
                                  pos_hint={"x": 0.0, "y": 0.9}, color=(0.8, 0.8, 0.8, 1),
                                  halign="left", valign="middle", font_size=scaled_font_size(SMALL_FONT_FRACTION)
                                  )
        self.coords_label.bind(size=lambda *a: setattr(self.coords_label, "text_size", self.coords_label.size))
        self.add_widget(self.coords_label)


        self.font_size_input = TextInput(text="", hint_text="Font size", size_hint=(0.14, 0.04),
                                         pos_hint={"x": 0.0, "y": 0.85}, multiline=False, input_filter="int",
                                         font_size=editor_font_size)
        self.add_widget(self.font_size_input)


        self.font_name_input = TextInput(text="", hint_text="Font", size_hint=(0.18, 0.04),
                                         pos_hint={"x": 0.15, "y": 0.85}, multiline=False,
                                         font_size=editor_font_size
                                         )
        self.add_widget(self.font_name_input)


        self.text_color_input = TextInput(text="", hint_text="Text colour (Hex or RGB)", size_hint=(0.18, 0.04),
                                          pos_hint={"x": 0.34, "y": 0.85}, multiline=False,
                                          font_size=editor_font_size
                                          )
        self.add_widget(self.text_color_input)


        self.box_color_input = TextInput(text="", hint_text="Box colour (Hex or RGB and/or Alpha",
                                         size_hint=(0.18, 0.04), pos_hint={"x": 0.53, "y": 0.85}, multiline=False,
                                         font_size=editor_font_size
                                         )
        self.add_widget(self.box_color_input)


        self.apply_btn = Label(text="[ref=apply]Use[/ref]", markup=True, size_hint=(0.14, 0.04),
                          pos_hint={"x": 0.72, "y": 0.85}, color=(0.3, 1, 0.3, 1),
                          font_size=editor_font_size
                          )
        self.apply_btn.bind(on_ref_press=lambda *a: self.apply_style())
        self.add_widget(self.apply_btn)


        self.load_layout()


    def add_box(self, name=None, pos_hint=None, size_hint=(0.2, 0.08), font_size=20, font_name=None,
                text_color=(0, 0, 0, 1), color=(0.3, 0.6, 0.9, 1)):

        self._box_counter += 1
        box_name = name or (self.name_input.text.strip() or f"element_{self._box_counter}")
        box = draggable_box(name=box_name, font_size=font_size, font_name=font_name, text_color=tuple(text_color),
                            color=tuple(color), select_callback=self.on_box_selected)
        if pos_hint:
            box.pos = (pos_hint.get("x", 0.1) * Window.width, pos_hint.get("y", 0.1) * Window.height)
        else:
            box.pos = (Window.width * 0.1, Window.height * 0.3)
        box.size = (size_hint[0] * Window.width, size_hint[1] * Window.height)

        self.add_widget(box)
        self.boxes[box_name] = box
        self.name_input.text = ""

    def _on_window_resize(self, window, new_size):
        old_w, old_h = self._last_window_size
        new_w, new_h = new_size
        self._last_window_size = (new_w, new_h)

        if old_w <= 0 or old_h <= 0:
            return

        scale_x = new_w / old_w
        scale_y = new_h / old_h

        for box in self.boxes.values():
            box.pos = (box.x * scale_x, box.y * scale_y)
            box.size = (box.width * scale_x, box.height * scale_y)
            box.scale_font(scale_y)

        self._rescale_ui_fonts()

        if self.selected_box is not None:
            self.on_box_selected(self.selected_box)

    def _rescale_ui_fonts(self):
        size = scaled_font_size(NORMAL_FONT_FRACTION)
        small_size = scaled_font_size(SMALL_FONT_FRACTION)

        self.name_input.font_size = size
        self.font_name_input.font_size = size
        self.text_color_input.font_size = size
        self.box_color_input.font_size = size
        self.font_size_input.font_size = size
        self.status_label.font_size = size
        self.add_btn.font_size = size
        self.save_btn.font_size = size
        self.apply_btn.font_size = size
        self.coords_label.font_size = small_size



    def on_box_selected(self, box):
        self.selected_box = box
        self.coords_label.text = (
                                  f"{box.name}: x= {round(box.x)} y={round(box.y)} "
                                  f"w={round(box.width)} h={round(box.height)}"
                                  )

        self.font_size_input.text = str(box.font_size)
        self.font_name_input.text = box.font_name or ""
        self.text_color_input.text = rgba_to_hex(box.text_color)
        self.box_color_input.text = rgba_to_hex(box.box_color)

    def apply_style(self):
        if self.selected_box is None:
            self.status_label.text = "No Element selected"
            return

        font_size=None
        if self.font_size_input.text.strip():
            try:
                font_size = int(self.font_size_input.text.strip())
            except ValueError:
                self.status_label.text = "Invalid Font Size"
                return

        font_name = self.font_name_input.text.strip() or None

        text_color = None
        if self.text_color_input.text.strip():
            try:
                text_color = hex_to_rgba(self.text_color_input.text.strip())
            except ValueError:
                self.status_label.text = "Invalid Text Colour"
                return

        box_color = None
        if self.box_color_input.text.strip():
            try:
                box_color = hex_to_rgba(self.box_color_input.text.strip())
            except ValueError:
                self.status_label.text = "Invalid Box Colour"
                return

        self.selected_box.set_style(font_size=font_size, font_name=font_name, text_color=text_color,
                                    box_color=box_color
                                    )
        self.status_label.text = f"Style used: {self.selected_box.name}"


    def save_layout(self):
        data = {name: box.export(Window.width, Window.height) for name, box in self.boxes.items()}
        with open(get_layout_file_path(), "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2, ensure_ascii=False)
        self.status_label.text = f"Saved: {len(data)} Elements"

    def load_layout(self):
        layout_path = get_layout_file_path()
        if not os.path.exists(layout_path):
            return
        with open(layout_path, "r", encoding="utf-8") as f:
            data = json.load(f)
        for name, entry in data.items():
            self.add_box(name=name,
                         pos_hint=entry["pos_hint"],
                         size_hint=tuple(entry["size_hint"]),
                         font_size=entry.get("font_size", 20),
                         font_name=entry.get("font_name", "Roboto"),
                         text_color=tuple(entry.get("text_color", [0, 0, 0, 1])),
                         color=tuple(entry.get("color", [0.3, 0.6, 0.9, 1])),
                         )
        self.status_label.text = f"Loaded: {len(data)} Elements"

class layout_builder_app(App):
    def build(self):
        Window.size = (1000, 700)
        return layout_root()

if __name__ == "__main__":
    layout_builder_app().run()






























