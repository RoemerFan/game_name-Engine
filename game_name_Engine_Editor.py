import json
import os

from kivy.app import App
from kivy.uix.floatlayout import FloatLayout
from kivy.uix.widget import Widget
from kivy.uix.label import Label
from kivy.uix.textinput import TextInput
from kivy.graphics import Color, Rectangle
from kivy.core.window import Window


LAYOUT_FILE = ("layout.json")
HANDLE_SIZE = 24


class draggable_box(Widget):
    def __init__(self, name, pos_hint=None, size_hint=(0.2, 0.08), color=(0.3, 0.6, 0.9, 1), **kwargs):
        super().__init__(**kwargs)
        self.name = name
        self.size_hint = size_hint
        self.pos_hint = pos_hint or {"x": 0.1, "y": 0.1}

        self._dragging = False
        self._resizing = False
        self._touch_offset = (0, 0)

        with self.canvas:
            self._color_instruction = Color(*color)
            self._rect = Rectangle(pos=self.pos, size=self.size)
            Color(1, 1, 1, 0.6)
            self._handle = Rectangle(pos=self._handle_pos(), size=(HANDLE_SIZE, HANDLE_SIZE))

        self.label = Label(text=self.name, pos=self.pos, size=self.size, color=(0, 0, 0, 1))
        self.add_widget(self.label)

        self.bind(pos=self._redraw, size=self._redraw)

    def _handle_pos(self):
        return self.x + self.width - HANDLE_SIZE, self.y

    def _redraw(self, *args):
        self._rect.pos = self.pos
        self._rect.size = self.size
        self._handle,pos = self._handle_pos()
        self._handle.size = (HANDLE_SIZE, HANDLE_SIZE)
        self.label.pos = self.pos
        self.label.size = self.size

    def on_touch_down(self, touch):
        handle_rect = (self.x + self.width - HANDLE_SIZE, self.y, HANDLE_SIZE, HANDLE_SIZE)
        hx, hy, hw, hh = handle_rect
        if hx <= touch.x <= hx + hw and hy <= touch.y <= hy + hh:
            self._resizing = True
            touch.grab(self)
            return True

        if self.collide_point(*touch.pos):
            self._dragging = True
            self._touch_offset = (touch.x - self.x, touch.y - self.y)
            touch.grab(self)
            return True

        return super().on_touch_down(touch)

    def on_touch_move(self, touch):
        if touch.grab_current is not self:
            return super().on_touch_move(touch)

        if self._resizing:
            new_w = max(40, touch.x - self.x)
            new_h = max(30, self.y + self.height - touch.y if False else touch.y - self.y)
            new_h = max(30, touch.y - self.y)

            self.size = (new_w, new_h)

        elif self._dragging:
            self.pos = (touch.x - self._touch_offset[0], touch.y - self._touch_offset[1])
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
                }

class layout_root(FloatLayout):
    def __init__(self, **kwargs):
        super().__init__(**kwargs)
        self.boxes = {}
        self._box_counter = 0





























