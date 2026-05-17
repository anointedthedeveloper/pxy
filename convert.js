const fs = require("fs");
const pngToIco = require("png-to-ico");

(async () => {
  try {
    // Note: This expects separate resized files (icon-16.png, icon-32.png, etc.) 
    // to be present in the directory.
    const buf = await pngToIco([
      "icon-16.png",
      "icon-32.png",
      "icon-48.png",
      "icon-64.png",
      "icon-128.png",
      "icon-256.png"
    ]);

    fs.writeFileSync("appicon.ico", buf);
    console.log("ICO file created successfully");
  } catch (err) {
    console.error("Error:", err);
  }
})();
