from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
ASSET_DIR = ROOT / "installer-assets"
PNG_PATH = ASSET_DIR / "app-icon.png"
ICO_PATH = ASSET_DIR / "app-icon.ico"
SIZE = 1024


def rounded_mask(box, radius):
    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle(box, radius=radius, fill=255)
    return mask


def add_glow(image, box, color, radius=30):
    glow = Image.new("RGBA", image.size, (0, 0, 0, 0))
    ImageDraw.Draw(glow).rounded_rectangle(box, radius=(box[3] - box[1]) // 2, fill=color)
    image.alpha_composite(glow.filter(ImageFilter.GaussianBlur(radius)))


def draw_icon():
    image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    tile_box = (88, 88, 936, 936)
    tile_mask = rounded_mask(tile_box, 205)

    tile = Image.new("RGBA", image.size, (0, 0, 0, 0))
    pixels = tile.load()
    for y in range(tile_box[1], tile_box[3] + 1):
        t = (y - tile_box[1]) / (tile_box[3] - tile_box[1])
        color = (
            round(20 - 13 * t),
            round(29 - 19 * t),
            round(47 - 28 * t),
            255,
        )
        for x in range(tile_box[0], tile_box[2] + 1):
            pixels[x, y] = color
    tile.putalpha(tile_mask)
    image.alpha_composite(tile)

    border = Image.new("RGBA", image.size, (0, 0, 0, 0))
    ImageDraw.Draw(border).rounded_rectangle(
        tile_box, radius=205, outline=(134, 162, 210, 92), width=14
    )
    image.alpha_composite(border)

    pills = [
        ((216, 276, 808, 390), (48, 218, 255, 255), 0.72),
        ((216, 455, 808, 569), (154, 105, 255, 255), 0.54),
        ((216, 634, 808, 748), (255, 114, 111, 255), 0.36),
    ]
    for box, color, fraction in pills:
        height = box[3] - box[1]
        radius = height // 2
        add_glow(image, box, (*color[:3], 65), radius=26)
        draw = ImageDraw.Draw(image)
        draw.rounded_rectangle(
            box,
            radius=radius,
            fill=(35, 45, 65, 245),
            outline=(143, 164, 199, 76),
            width=8,
        )
        inner = (box[0] + 16, box[1] + 16, box[2] - 16, box[3] - 16)
        inner_width = inner[2] - inner[0]
        fill_right = inner[0] + round(inner_width * fraction)
        fill_box = (inner[0], inner[1], fill_right, inner[3])
        draw.rounded_rectangle(fill_box, radius=(inner[3] - inner[1]) // 2, fill=color)
        highlight = (inner[0] + 18, inner[1] + 12, max(inner[0] + 22, fill_right - 18), inner[1] + 24)
        draw.rounded_rectangle(highlight, radius=6, fill=(255, 255, 255, 86))

    # A small refresh orbit makes the utility recognizable without adding text.
    draw = ImageDraw.Draw(image)
    orbit_box = (699, 675, 859, 835)
    draw.arc(orbit_box, start=38, end=318, fill=(229, 240, 255, 235), width=25)
    draw.polygon([(828, 692), (871, 704), (844, 742)], fill=(229, 240, 255, 235))
    draw.ellipse((755, 731, 803, 779), fill=(9, 15, 27, 255))

    return image


def main():
    ASSET_DIR.mkdir(parents=True, exist_ok=True)
    master = draw_icon()
    master.save(PNG_PATH, optimize=True)
    master.save(
        ICO_PATH,
        format="ICO",
        sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40),
               (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    print(PNG_PATH)
    print(ICO_PATH)


if __name__ == "__main__":
    main()
