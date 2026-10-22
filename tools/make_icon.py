"""Генерирует src/Resources/app.ico: сетка с бочкообразной дисторсией на фирменном фоне ТУСУР (#3C388D)
и «точечная волна» (#9ABF38). Тот же рисунок — Theme.Logo в программе.
Запуск: python3 tools/make_icon.py src/Resources/app.ico"""
import math, sys
from PIL import Image, ImageDraw

OUT = sys.argv[1]
PRIMARY = (0x3C, 0x38, 0x8D, 255)
ACCENT = (0x9A, 0xBF, 0x38, 255)

def render(size):
    S = 1024
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    pad = 24
    d.rounded_rectangle([pad, pad, S - pad, S - pad], radius=190, fill=PRIMARY)
    cx, cy, half = S * 0.5, S * 0.40, S * 0.32
    def m(u, v):
        r2 = (u * u + v * v) / 2.0
        k = 1.0 / (1 + 0.42 * r2)
        return (cx + u * half * k, cy + v * half * k)
    N, STEPS = 4, 48
    width = max(2, int(S * 0.05)) if size <= 32 else int(S * 0.036)
    for i in range(N + 1):
        t = -1 + 2.0 * i / N
        a = [m(t, -1 + 2.0 * s / STEPS) for s in range(STEPS + 1)]
        b = [m(-1 + 2.0 * s / STEPS, t) for s in range(STEPS + 1)]
        d.line(a, fill=(255, 255, 255, 255), width=width, joint="curve")
        d.line(b, fill=(255, 255, 255, 255), width=width, joint="curve")
    n = 9
    for i in range(n):
        x = S * (0.15 + 0.70 * i / (n - 1))
        y = S * (0.82 + 0.05 * math.sin(i * 0.95))
        r = S * (0.030 + 0.012 * abs(math.cos(i * 0.6)))
        d.ellipse([x - r, y - r, x + r, y + r], fill=ACCENT)
    return im.resize((size, size), Image.LANCZOS)

sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
imgs = [render(s) for s in sizes]
imgs[-1].save(OUT, format="ICO", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
imgs[-1].save(OUT.replace(".ico", "_256.png"))
print("ok")
