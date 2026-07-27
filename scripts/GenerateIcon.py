from pathlib import Path

from PIL import Image, ImageDraw


SCALE = 4
SIZE = 512
canvas = Image.new("RGBA", (SIZE * SCALE, SIZE * SCALE), (0, 0, 0, 0))
draw = ImageDraw.Draw(canvas)

def scaled(value: int) -> int:
    return value * SCALE


draw.rounded_rectangle(
    (0, 0, scaled(SIZE), scaled(SIZE)),
    radius=scaled(96),
    fill="#123d3a",
)

waveform = [(96, 256), (140, 256), (168, 164), (214, 348), (262, 112), (308, 400), (352, 256), (416, 256)]
draw.line(
    [(scaled(x), scaled(y)) for x, y in waveform],
    fill="#60d4c5",
    width=scaled(34),
    joint="curve",
)
for x, y in waveform:
    radius = scaled(17)
    draw.ellipse(
        (scaled(x) - radius, scaled(y) - radius, scaled(x) + radius, scaled(y) + radius),
        fill="#60d4c5",
    )

accent_radius = scaled(20)
draw.ellipse(
    (
        scaled(416) - accent_radius,
        scaled(256) - accent_radius,
        scaled(416) + accent_radius,
        scaled(256) + accent_radius,
    ),
    fill="#ef7b66",
)

icon = canvas.resize((SIZE, SIZE), Image.Resampling.LANCZOS)
output_path = Path(__file__).resolve().parents[1] / "Assets" / "BachenAudioIcon.ico"
icon.save(output_path, format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print(output_path)
