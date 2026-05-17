import os
import shutil
from PIL import Image

def convert_to_ico():
    source_png = "prep4jamb.png"
    target_root_ico = "appicon.ico"
    target_res_ico = os.path.join("src", "CbtExam.Desktop", "Resources", "appicon.ico")
    
    if not os.path.exists(source_png):
        print(f"Error: Source file {source_png} not found.")
        return
        
    print(f"Loading source image: {source_png}")
    img = Image.open(source_png)
    print(f"Original image size: {img.size}, mode: {img.mode}")
    
    # 1. Ensure the image is RGBA (has transparent channel)
    img = img.convert("RGBA")
    
    # 2. Make the image a perfect square by adding transparent padding
    width, height = img.size
    max_dim = max(width, height)
    
    print(f"Creating a transparent square canvas of size {max_dim}x{max_dim}...")
    square_img = Image.new("RGBA", (max_dim, max_dim), (0, 0, 0, 0))
    
    # Center the original image on the new canvas
    offset_x = (max_dim - width) // 2
    offset_y = (max_dim - height) // 2
    square_img.paste(img, (offset_x, offset_y))
    
    # 3. Create backup of existing icons if they exist
    for target in [target_root_ico, target_res_ico]:
        if os.path.exists(target):
            backup_path = target + ".bak"
            print(f"Creating backup of existing {target} at {backup_path}")
            shutil.copy2(target, backup_path)
            
    # 4. Save as ICO with multiple sizes
    sizes = [(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    
    print(f"Saving to {target_root_ico} with sizes {sizes}...")
    square_img.save(
        target_root_ico,
        format="ICO",
        sizes=sizes
    )
    print(f"Successfully generated {target_root_ico}")
    
    # Ensure target directory exists for WPF Resources
    os.makedirs(os.path.dirname(target_res_ico), exist_ok=True)
    
    print(f"Saving to {target_res_ico} with sizes {sizes}...")
    square_img.save(
        target_res_ico,
        format="ICO",
        sizes=sizes
    )
    print(f"Successfully generated {target_res_ico}")
    print("All tasks completed successfully!")

if __name__ == "__main__":
    convert_to_ico()
