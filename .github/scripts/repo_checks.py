#!/usr/bin/env python3
"""Repository checks for Neon Rift (no Unity, no LFS download needed).

1. Every relative link and image in the tracked Markdown files resolves to a tracked file or folder.
2. No tracked file is larger than GitHub's 100 MB limit.
3. Files whose type is routed to Git LFS (.gitattributes) are committed as LFS pointers, not raw binaries.
4. Local-only assets (unverified licences / macOS System Voices) are not tracked.
5. Every asset folder under Assets/ThirdParty carries an ATTRIBUTION.txt.
Exit code 1 lists every problem found.
"""
import os
import re
import subprocess
import sys
import urllib.parse

ROOT = subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True, check=True).stdout.strip()
os.chdir(ROOT)
tracked = subprocess.run(["git", "ls-files", "-z"], capture_output=True, check=True).stdout.decode().split("\0")
tracked = [t for t in tracked if t]
tracked_set = set(tracked)
tracked_dirs = {os.path.dirname(t) for t in tracked}
for t in list(tracked_dirs):
    while t:
        t = os.path.dirname(t)
        tracked_dirs.add(t)
problems = []

# 1. Markdown links and images.
link = re.compile(r"!?\[[^\]]*\]\(([^)\s]+)(?:\s+\"[^\"]*\")?\)")
html_img = re.compile(r"<img[^>]+src=\"([^\"]+)\"")
markdown = [t for t in tracked if t.endswith(".md") and not t.startswith("Assets/") and not t.startswith("Packages/")]
checked = 0
for md in markdown:
    with open(md, encoding="utf-8", errors="replace") as f:
        text = f.read()
    text = re.sub(r"```.*?```", "", text, flags=re.S)  # ignore code blocks
    for target in link.findall(text) + html_img.findall(text):
        if re.match(r"^[a-z]+:", target) or target.startswith("#") or target.startswith("mailto:"):
            continue
        path = urllib.parse.unquote(target.split("#", 1)[0])
        if not path:
            continue
        resolved = os.path.normpath(os.path.join(os.path.dirname(md), path))
        checked += 1
        if resolved not in tracked_set and resolved not in tracked_dirs:
            problems.append(f"broken link in {md}: {target}")
print(f"links: {checked} relative links in {len(markdown)} Markdown files")

# 2. File sizes (LFS pointers are tiny; a raw file above 100 MB is rejected by GitHub).
for t in tracked:
    if os.path.isfile(t) and os.path.getsize(t) > 100 * 1024 * 1024:
        problems.append(f"file over 100 MB: {t}")

# 3. LFS routing.
attrs = subprocess.run(["git", "check-attr", "filter", "--stdin", "-z"], input="\0".join(tracked).encode(),
                       capture_output=True, check=True).stdout.decode().split("\0")
lfs_paths = [attrs[i] for i in range(0, len(attrs) - 2, 3) if attrs[i + 2] == "lfs"]
not_pointers = 0
for p in lfs_paths:
    if not os.path.isfile(p):
        continue
    with open(p, "rb") as f:
        head = f.read(40)
    if not head.startswith(b"version https://git-lfs.github.com/spec/"):
        # A local working copy with LFS installed shows the real file; check what is actually committed instead.
        blob = subprocess.run(["git", "cat-file", "-p", f"HEAD:{p}"], capture_output=True).stdout[:40]
        if blob and not blob.startswith(b"version https://git-lfs.github.com/spec/"):
            not_pointers += 1
            problems.append(f"committed without LFS: {p}")
print(f"lfs: {len(lfs_paths)} files routed to LFS, {not_pointers} committed raw")

# 4. Local-only assets must never be tracked.
local_only = [
    r"^Assets/_NeonRift/Levels/HarborRain/Imported/Vehicles/",
    r"^Assets/_NeonRift/Levels/HarborRain/Generated/Vehicles/PF_HR_Ferrari",
    r"^Assets/_NeonRift/Levels/HarborRain/Imported/Ships/DivingBoat",
    r"^Assets/ThirdParty/Sketchfab/Vehicles/(ferrari-f40-liberty-walk|2026-ferrari-296-speciale-a)\.zip",
    r"/HR_VO_[^/]*$",
    r"/Campaign_VO_[^/]*$",
    r"\.apk$",
]
for t in tracked:
    for pattern in local_only:
        if re.search(pattern, t):
            problems.append(f"local-only asset is tracked: {t}")

# 5. Attribution files for third-party asset folders.
asset_folders = set()
for t in tracked:
    parts = t.split("/")
    if len(parts) >= 5 and parts[0] == "Assets" and parts[1] == "ThirdParty" and parts[2] == "Sketchfab":
        if not parts[3].endswith(".meta"):
            folder = "/".join(parts[:4]) if parts[3] == "Ships" else "/".join(parts[:5])
            if not folder.endswith(".meta") and os.path.basename(folder) != "ATTRIBUTION.txt":
                asset_folders.add(folder)
    if len(parts) >= 4 and parts[:3] == ["Assets", "ThirdParty", "SoundAudio"]:
        asset_folders.add("Assets/ThirdParty/SoundAudio")
for folder in sorted(asset_folders):
    if f"{folder}/ATTRIBUTION.txt" not in tracked_set:
        problems.append(f"missing ATTRIBUTION.txt: {folder}")
print(f"attribution: {len(asset_folders)} third-party asset folders")

if problems:
    print(f"\n{len(problems)} problem(s):")
    for p in problems:
        print("  - " + p)
    sys.exit(1)
print("\nall repository checks passed")
