# -*- coding: utf-8 -*-
# 下载台球厅场景所需 Poly Haven 免费资源（CC0）到 assets\hall\
# 贴图 2k（diff/nor_gl/rough）、HDRI 2k、模型 gltf 1k（含 bin/贴图）
import json, os, urllib.request

UA = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"}
OUT = r"E:\Snooker\assets\hall"


def api(path):
    req = urllib.request.Request("https://api.polyhaven.com" + path, headers=UA)
    with urllib.request.urlopen(req, timeout=40) as r:
        return json.loads(r.read().decode())


def fetch(url, dest):
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    if os.path.exists(dest) and os.path.getsize(dest) > 0:
        return
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=120) as r, open(dest + ".part", "wb") as f:
        while True:
            chunk = r.read(1 << 16)
            if not chunk:
                break
            f.write(chunk)
    os.replace(dest + ".part", dest)
    print("ok", os.path.relpath(dest, OUT), os.path.getsize(dest))


# ---- 贴图（2k jpg）----
for tex_id in ["wood_floor_worn", "painted_plaster_wall"]:
    files = api("/files/" + tex_id)
    for map_key in ["Diffuse", "nor_gl", "Rough"]:
        entry = files[map_key]["2k"]["jpg"]
        fetch(entry["url"], os.path.join(OUT, "tex_" + tex_id, map_key.lower() + "_2k.jpg"))

# ---- HDRI（2k hdr）----
hd = api("/files/abandoned_games_room_01")["hdri"]["2k"]
fetch(hd["hdr"]["url"], os.path.join(OUT, "games_room_01_2k.hdr"))

# ---- 模型（gltf 1k + include 文件）----
for mid in ["GreenChair_01", "ArmChair_01", "anthurium_botany_01", "calathea_orbifolia_01"]:
    g = api("/files/" + mid)["gltf"]["1k"]["gltf"]
    base = os.path.join(OUT, "model_" + mid)
    main = g["url"]
    main_name = main.split("/")[-1]
    fetch(main, os.path.join(base, main_name))
    for inc_path, inc in g.get("include", {}).items():
        fetch(inc["url"], os.path.join(base, inc_path.replace("/", os.sep)))

print("ALL DONE")
