import os
import re
import sys
import termios
import tty
from enum import Enum

class SemanticColorType(Enum):
    FUNCTION = "function"
    CLASS = "class"
    INTERFACE = "interface"
    MODULE = "module"
    NAMESPACE = "namespace"
    VARIABLE = "variable"
    KEYWORD = "keyword"

class PerceptualColorEngine:
    def __init__(self):
        self.terminal_info = self._detect_terminal_colors()
        self.harmony = ColorHarmony(self.terminal_info["background_color"])

    def generate_semantic_color(self, seed: str, color_type: SemanticColorType) -> tuple[int, int, int]:
        if color_type == SemanticColorType.FUNCTION:
            return self.harmony.generate_burnt_orange()
        elif color_type == SemanticColorType.CLASS:
            return self.harmony.generate_green()
        elif color_type == SemanticColorType.INTERFACE:
            return self.harmony.generate_analogous(0.6)
        elif color_type == SemanticColorType.MODULE:
            return self.harmony.generate_complementary(0.4)
        else:
            return self.harmony.generate_analogous(0.6)

    def _detect_terminal_colors(self) -> dict:
        bg_color = self._try_detect_background_color()
        if bg_color is None:
            bg_color = self._estimate_from_environment()
        
        is_dark = self._is_color_dark(bg_color)
        return {"background_color": bg_color, "is_dark": is_dark}

    def _try_detect_background_color(self) -> tuple[int, int, int] | None:
        # This is a complex task in Python, and it's not guaranteed to work on all terminals.
        # For now, we will just return None and rely on the environment estimation.
        return None

    def _estimate_from_environment(self) -> tuple[int, int, int]:
        term = os.environ.get("TERM", "").lower()
        colorterm = os.environ.get("COLORTERM", "").lower()
        term_program = os.environ.get("TERM_PROGRAM", "").lower()
        terminal_emulator = os.environ.get("TERMINAL_EMULATOR", "").lower()
        session_type = os.environ.get("XDG_SESSION_TYPE", "").lower()

        if os.environ.get("COLORFGBG", "").startswith("15;0"):
            return (255, 255, 255)

        if "iterm" in term_program:
            return self._detect_iterm_theme()
        elif "alacritty" in term:
            return (29, 32, 33)
        elif "kitty" in terminal_emulator:
            return (45, 45, 45)
        elif "windowsterminal" in term_program:
            return (12, 12, 12)
        elif "wezterm" in term_program:
            return (40, 40, 40)
        elif "vscode" in term_program:
            return (30, 30, 30)
        elif "hyper" in term_program:
            return (0, 0, 0)
        elif "screen" in term:
            return (0, 0, 0)
        elif "xterm" in term and session_type == "x11":
            return (46, 52, 64)
        elif os.environ.get("KONSOLE_VERSION") is not None:
            return (35, 38, 39)
        elif "xterm" in term:
            return (0, 0, 0)
        else:
            return (12, 12, 12)

    def _detect_iterm_theme(self) -> tuple[int, int, int]:
        iterm_profile = os.environ.get("ITERM_PROFILE", "").lower()
        if "light" in iterm_profile:
            return (255, 255, 255)
        elif "solarized light" in iterm_profile:
            return (253, 246, 227)
        elif "solarized dark" in iterm_profile:
            return (0, 43, 54)
        else:
            return (40, 44, 52)

    def _is_color_dark(self, color: tuple[int, int, int]) -> bool:
        r, g, b = color
        luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b
        return luminance < 128

class ColorHarmony:
    def __init__(self, background_color: tuple[int, int, int]):
        self.base_color = background_color
        self.base_hsl = self._rgb_to_hsl(background_color)
        self.is_dark_background = self._is_color_dark(background_color)

    def generate_green(self) -> tuple[int, int, int]:
        optimal_green_hue = self._compute_optimal_green_hue()
        saturation = 0.4 if self.is_dark_background else 0.6
        lightness = 0.25 if self.is_dark_background else 0.7
        return self._hsl_to_rgb((optimal_green_hue, saturation, lightness))

    def generate_burnt_orange(self) -> tuple[int, int, int]:
        optimal_orange_hue = self._compute_optimal_orange_hue()
        saturation = 0.45 if self.is_dark_background else 0.65
        lightness = 0.3 if self.is_dark_background else 0.65
        return self._hsl_to_rgb((optimal_orange_hue, saturation, lightness))

    def generate_complementary(self, saturation_target: float) -> tuple[int, int, int]:
        complementary_hue = (self.base_hsl[0] + 180) % 360
        saturation = max(0.3, min(0.8, saturation_target))
        lightness = 0.65 if self.is_dark_background else 0.35
        return self._hsl_to_rgb((complementary_hue, saturation, lightness))

    def generate_analogous(self, saturation_target: float) -> tuple[int, int, int]:
        analogous_offset = 30.0
        final_hue = (self.base_hsl[0] + analogous_offset) % 360
        if final_hue < 0:
            final_hue += 360
        saturation = saturation_target
        lightness = 0.7 if self.is_dark_background else 0.3
        return self._hsl_to_rgb((final_hue, saturation, lightness))

    def _compute_optimal_green_hue(self) -> float:
        bg_hue = self.base_hsl[0]
        if 0 <= bg_hue <= 60:
            return 140
        elif 60 < bg_hue <= 120:
            return 160
        elif 120 < bg_hue <= 180:
            return 90
        elif 180 < bg_hue <= 240:
            return 120
        elif 240 < bg_hue <= 300:
            return 100
        else:
            return 130

    def _compute_optimal_orange_hue(self) -> float:
        bg_hue = self.base_hsl[0]
        if 0 <= bg_hue <= 60:
            return 25
        elif 60 < bg_hue <= 120:
            return 15
        elif 120 < bg_hue <= 180:
            return 30
        elif 180 < bg_hue <= 240:
            return 20
        elif 240 < bg_hue <= 300:
            return 35
        else:
            return 25

    def _is_color_dark(self, color: tuple[int, int, int]) -> bool:
        r, g, b = color
        luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b
        return luminance < 128

    def _rgb_to_hsl(self, rgb: tuple[int, int, int]) -> tuple[float, float, float]:
        r, g, b = rgb
        r /= 255.0
        g /= 255.0
        b /= 255.0
        max_val = max(r, g, b)
        min_val = min(r, g, b)
        delta = max_val - min_val
        l = (max_val + min_val) / 2.0
        if delta == 0:
            return (0, 0, l)
        s = l / (2 - max_val - min_val) if l > 0.5 else delta / (max_val + min_val)
        if max_val == r:
            h = (g - b) / delta + (6 if g < b else 0)
        elif max_val == g:
            h = (b - r) / delta + 2
        else:
            h = (r - g) / delta + 4
        h *= 60
        return (h, s, l)

    def _hsl_to_rgb(self, hsl: tuple[float, float, float]) -> tuple[int, int, int]:
        h, s, l = hsl
        c = (1 - abs(2 * l - 1)) * s
        x = c * (1 - abs((h / 60) % 2 - 1))
        m = l - c / 2.0
        if 0 <= h < 60:
            r, g, b = (c, x, 0)
        elif 60 <= h < 120:
            r, g, b = (x, c, 0)
        elif 120 <= h < 180:
            r, g, b = (0, c, x)
        elif 180 <= h < 240:
            r, g, b = (0, x, c)
        elif 240 <= h < 300:
            r, g, b = (x, 0, c)
        else:
            r, g, b = (c, 0, x)
        return (int(round((r + m) * 255)), int(round((g + m) * 255)), int(round((b + m) * 255)))
