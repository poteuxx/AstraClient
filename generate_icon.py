import PIL.Image as Image
import PIL.ImageDraw as ImageDraw

# Create a 256x256 image with transparent background
size = 256
img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)

# Background color: #8C9EFF (a nice periwinkle/light purple)
bg_color = (140, 158, 255, 255)
# Draw rounded rectangle
draw.rounded_rectangle([(16, 16), (240, 240)], radius=64, fill=bg_color)

def draw_star(draw, center, size, color):
    cx, cy = center
    pts = [
        (cx, cy - size),
        (cx + size * 0.25, cy - size * 0.25),
        (cx + size, cy),
        (cx + size * 0.25, cy + size * 0.25),
        (cx, cy + size),
        (cx - size * 0.25, cy + size * 0.25),
        (cx - size, cy),
        (cx - size * 0.25, cy - size * 0.25)
    ]
    draw.polygon(pts, fill=color)

# Large star
draw_star(draw, (100, 110), 50, (255, 255, 255, 255))
# Small star
draw_star(draw, (170, 170), 25, (255, 255, 255, 255))

img.save('src/AstraClient/Assets/astra_logo.ico', format='ICO', sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)])
img.save('src/AstraClient/Assets/astra_logo.png', format='PNG')
