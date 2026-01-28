# Media Duplicate Finder

A simple and efficient Windows Forms application to find and manage duplicate images using perceptual hashing.

## Features

* **Perceptual Hashing:** Uses advanced algorithms (via CoenM.ImageHash) to find images that look similar, not just byte-for-byte identical.
* **Fast Scanning:** Leverages multi-core processors for quick hash calculation.
* **Customizable Extensions:** Choose which file types to scan (e.g., .jpg, .png, .bmp, .webp, .tiff).
* **Easy Management:** Automatically moves duplicates to a dedicated subfolder, keeping your original collection clean.
* **User-Friendly UI:** Real-time progress tracking and detailed logging.

## How to Use

1. **Select Folder:** Click "Browse..." to choose the directory you want to scan.
2. **Select Extensions:** Check the file formats you are interested in.
3. **Start Scan:** Click "Start Scan". The app will find unique images and identify duplicates.
4. **Review:** See the found duplicates in the log list.
5. **Move Duplicates:** Click "Move Duplicates" to safely transfer all identified duplicates into a `duplicates` folder within your selected path.
6. **Open Folder:** Use "Open Duplicates Folder" to verify the moved files.

## Technical Details

* Built with **.NET 8.0**.
* Uses **SixLabors.ImageSharp** for image processing.
* Uses **CoenM.ImageSharp.ImageHash** for perceptual hashing.

## Installation

Download the latest release from the [Releases](https://github.com/xLinorx/a-media-duplicate-finder/releases) page. No installation required - just run the executable.
