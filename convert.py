from PIL import Image

# Load your main PNG (prep4jamb.png)
img = Image.open("prep4jamb.png")

# Ensure it's RGBA
img = img.convert("RGBA")

# Save as ICO with multiple sizes
img.save(
    "appicon.ico",
    format="ICO",
    sizes=[(16,16), (32,32), (48,48), (64,64), (128,128), (256,256)]
)

print("ICO file created successfully")
