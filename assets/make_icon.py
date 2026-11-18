from PIL import Image, ImageDraw

S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# Rounded square background with a soft diagonal gradient.
grad = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(grad)
for y in range(S):
    t = y / (S - 1)
    r = int(18 + (77 - 18) * t)
    g = int(112 + (70 - 112) * t)
    b = int(235 + (206 - 235) * t)
    gd.line([(0, y), (S, y)], fill=(r, g, b, 255))
mask = Image.new("L", (S, S), 0)
md = ImageDraw.Draw(mask)
md.rounded_rectangle([20, 20, S - 20, S - 20], radius=230, fill=255)
img.paste(grad, (0, 0), mask)

# Subtle top highlight.
hl = Image.new("RGBA", (S, S), (0, 0, 0, 0))
hd = ImageDraw.Draw(hl)
hd.ellipse([-250, -520, S + 250, 360], fill=(255, 255, 255, 38))
img = Image.alpha_composite(img, Image.composite(hl, Image.new("RGBA", (S, S), (0, 0, 0, 0)), mask))

d = ImageDraw.Draw(img)
# White context-menu card.
card = [218, 226, 806, 774]
d.rounded_rectangle(card, radius=88, fill=(255, 255, 255, 250))
# Three menu rows: dot + rounded line.
rows = [338, 466, 594]
lengths = [342, 270, 210]
for i, (yy, ln) in enumerate(zip(rows, lengths)):
    d.ellipse([294, yy - 17, 328, yy + 17], fill=(16, 100, 214, 255))
    d.rounded_rectangle([366, yy - 17, 366 + ln, yy + 18], radius=18, fill=(24, 49, 92, 255))
# Accent toggle on the right edge of the card.
d.rounded_rectangle([702, 704, 772, 746], radius=21, fill=(255, 166, 48, 255))
d.ellipse([744, 700, 780, 750], fill=(255, 255, 255, 255))

# Save a PNG for documentation, and multi-resolution ICO.
img.resize((512, 512), Image.LANCZOS).save("assets/app.png")
sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)]
img.save("assets/app.ico", sizes=sizes)
print("wrote assets/app.ico and assets/app.png")
