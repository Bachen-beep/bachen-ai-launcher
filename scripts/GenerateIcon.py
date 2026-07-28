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

nodes = [(148, 168), (364, 168), (148, 344), (364, 344), (256, 256)]
links = [(0, 4), (1, 4), (2, 4), (3, 4), (0, 1), (2, 3)]
for start, end in links:
    draw.line(
        [(scaled(nodes[start][0]), scaled(nodes[start][1])), (scaled(nodes[end][0]), scaled(nodes[end][1]))],
        fill="#60d4c5",
        width=scaled(24),
    )
for index, (x, y) in enumerate(nodes):
    radius = scaled(38 if index == 4 else 25)
    draw.ellipse(
        (scaled(x) - radius, scaled(y) - radius, scaled(x) + radius, scaled(y) + radius),
        fill="#ef7b66" if index == 4 else "#60d4c5",
    )

icon = canvas.resize((SIZE, SIZE), Image.Resampling.LANCZOS)
output_path = Path(__file__).resolve().parents[1] / "Assets" / "BaChenLauncherIcon.ico"
icon.save(output_path, format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print(output_path)
