"""
render_osm_minimap.py — genera el fondo del minimapa de un sitio SAT a partir de
OpenStreetMap (vías, edificios, agua, zonas verdes), renderizado offline a PNG.

Uso:
    python Tools/minimap/render_osm_minimap.py <nombre> <lat_centro> <lon_centro> [--size-m 600] [--px 2048]

Ejemplo:
    python Tools/minimap/render_osm_minimap.py SantaRita 6.190743 -75.651374

Salida:
    Assets/UI/Minimap/Maps/<nombre>.png   (textura del mapa, norte arriba)
    Assets/UI/Minimap/Maps/<nombre>.json  (centro + metros por píxel)

Los valores del .json se copian al asset MinimapMapData del sitio
(centerLatitude, centerLongitude, metersPerPixel). La proyección es
equirectangular local alrededor del centro, la misma que usa
MinimapMapData.LatLonToMeters() en C#, así que no hay deformación entre
el PNG y los anclajes.

Datos: © OpenStreetMap contributors (ODbL). La atribución debe mostrarse en la
app (créditos o texto pequeño junto al minimapa).

Solo usa la biblioteca estándar + Pillow (pip install pillow).
"""

import argparse
import json
import math
import os
import sys
import time
import urllib.parse
import urllib.request

from PIL import Image, ImageDraw

EARTH_RADIUS = 6378137.0
OVERPASS_URLS = [
    "https://overpass-api.de/api/interpreter",
    "https://overpass.private.coffee/api/interpreter",
    "https://overpass.kumi.systems/api/interpreter",
]
CACHE_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "cache")

# Estilo oscuro tipo "GTA", a juego con el MinimapRoot del HUD (azul marino).
STYLE = {
    "background": (27, 38, 56),
    "green": (36, 64, 52),
    "water": (40, 92, 150),
    "building": (58, 72, 96),
    "building_outline": (74, 90, 118),
    "railway": (90, 90, 100),
    # Ancho en metros por tipo de vía (se convierte a píxeles según la escala).
    "roads": [
        # (tipos highway, ancho_m, color) — se dibujan en este orden (menores primero)
        ({"footway", "path", "pedestrian", "steps", "cycleway", "track", "corridor"}, 2.0, (120, 128, 145)),
        ({"service", "living_street", "unclassified", "road"}, 4.5, (165, 172, 185)),
        ({"residential"}, 6.0, (190, 196, 206)),
        ({"tertiary", "tertiary_link"}, 8.0, (215, 220, 228)),
        ({"secondary", "secondary_link"}, 10.0, (235, 238, 242)),
        ({"primary", "primary_link", "trunk", "trunk_link", "motorway", "motorway_link"}, 13.0, (250, 214, 120)),
    ],
}


def meters_per_degree(lat0):
    k = math.pi / 180.0 * EARTH_RADIUS
    return k * math.cos(math.radians(lat0)), k  # (este, norte)


def fetch_osm(name, south, west, north, east, refresh=False):
    """Descarga de Overpass, con caché en Tools/minimap/cache/<name>.osm.json."""
    cache_path = os.path.join(CACHE_DIR, f"{name}.osm.json")
    if not refresh and os.path.exists(cache_path):
        print(f"  usando caché {cache_path} (--refresh para volver a descargar)")
        with open(cache_path, encoding="utf-8") as f:
            return json.load(f)
    bbox = f"{south},{west},{north},{east}"
    query = f"""
[out:json][timeout:90];
(
  way["highway"]({bbox});
  way["building"]({bbox});
  relation["building"]({bbox});
  way["waterway"]({bbox});
  way["natural"="water"]({bbox});
  relation["natural"="water"]({bbox});
  way["landuse"~"grass|forest|meadow|recreation_ground|village_green"]({bbox});
  way["leisure"~"park|pitch|garden|playground"]({bbox});
  way["natural"~"wood|scrub|grassland"]({bbox});
  way["railway"]({bbox});
);
out geom;
"""
    data = urllib.parse.urlencode({"data": query}).encode()
    last_error = None
    for attempt in range(3):
        for url in OVERPASS_URLS:
            try:
                req = urllib.request.Request(url, data=data, headers={"User-Agent": "SIRMED-AR-minimap/1.0"})
                with urllib.request.urlopen(req, timeout=180) as resp:
                    result = json.loads(resp.read().decode("utf-8"))
                os.makedirs(CACHE_DIR, exist_ok=True)
                with open(cache_path, "w", encoding="utf-8") as f:
                    json.dump(result, f)
                return result
            except Exception as e:  # probar el siguiente servidor
                last_error = e
                print(f"  Overpass fallo en {url}: {e}", file=sys.stderr)
        time.sleep(10 * (attempt + 1))
    raise RuntimeError(f"No se pudo descargar OSM: {last_error}")


def element_rings(el):
    """Devuelve una lista de anillos [(lat, lon), ...] del elemento."""
    if el["type"] == "way" and "geometry" in el:
        return [[(p["lat"], p["lon"]) for p in el["geometry"]]]
    if el["type"] == "relation":
        rings = []
        for m in el.get("members", []):
            if m.get("role") in ("outer", "") and "geometry" in m:
                rings.append([(p["lat"], p["lon"]) for p in m["geometry"]])
        return rings
    return []


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("name")
    ap.add_argument("lat", type=float)
    ap.add_argument("lon", type=float)
    ap.add_argument("--size-m", type=float, default=600.0, help="lado del área cubierta, en metros")
    ap.add_argument("--px", type=int, default=2048, help="lado de la textura, en píxeles")
    ap.add_argument("--refresh", action="store_true", help="ignorar la caché y volver a descargar")
    ap.add_argument("--out", default=os.path.join("Assets", "UI", "Minimap", "Maps"))
    args = ap.parse_args()

    lat0, lon0 = args.lat, args.lon
    mpp = args.size_m / args.px  # metros por píxel
    m_east, m_north = meters_per_degree(lat0)
    half = args.size_m / 2.0
    margin = 1.15  # descargar un poco más para que las vías no se corten en el borde
    dlat = half * margin / m_north
    dlon = half * margin / m_east

    print(f"[{args.name}] descargando OSM alrededor de {lat0}, {lon0} ({args.size_m:.0f} m)...")
    osm = fetch_osm(args.name, lat0 - dlat, lon0 - dlon, lat0 + dlat, lon0 + dlon, args.refresh)
    elements = osm.get("elements", [])

    def to_px(lat, lon):
        x = (lon - lon0) * m_east / mpp + args.px / 2.0
        y = args.px / 2.0 - (lat - lat0) * m_north / mpp  # norte arriba
        return (x, y)

    # Supermuestreo x2 para bordes suaves.
    ss = 2
    size = args.px * ss
    img = Image.new("RGB", (size, size), STYLE["background"])
    draw = ImageDraw.Draw(img)

    def px_list(ring):
        return [(x * ss, y * ss) for (x, y) in (to_px(la, lo) for la, lo in ring)]

    def width_px(meters):
        return max(1, int(round(meters / mpp * ss)))

    greens, waters, waterways, buildings, rails, roads = [], [], [], [], [], []
    for el in elements:
        tags = el.get("tags", {})
        rings = element_rings(el)
        if not rings:
            continue
        if "building" in tags:
            buildings += rings
        elif "highway" in tags:
            roads.append((tags["highway"], rings))
        elif "waterway" in tags:
            waterways.append((tags["waterway"], rings))
        elif tags.get("natural") == "water":
            waters += rings
        elif "railway" in tags:
            rails += rings
        else:
            greens += rings

    for r in greens:
        if len(r) >= 3:
            draw.polygon(px_list(r), fill=STYLE["green"])
    for r in waters:
        if len(r) >= 3:
            draw.polygon(px_list(r), fill=STYLE["water"])
    for kind, rings in waterways:
        w = 8.0 if kind in ("river", "canal") else 3.5  # quebradas: stream/ditch/drain
        for r in rings:
            draw.line(px_list(r), fill=STYLE["water"], width=width_px(w), joint="curve")
    for r in buildings:
        if len(r) >= 3:
            draw.polygon(px_list(r), fill=STYLE["building"], outline=STYLE["building_outline"])
    for r in rails:
        draw.line(px_list(r), fill=STYLE["railway"], width=width_px(2.0))

    counts = {}
    for kinds, w, color in STYLE["roads"]:
        wp = width_px(w)
        for hw, rings in roads:
            if hw not in kinds:
                continue
            counts[hw] = counts.get(hw, 0) + 1
            for r in rings:
                pts = px_list(r)
                draw.line(pts, fill=color, width=wp, joint="curve")
                # tapas redondas en los extremos para que los cruces queden limpios
                rad = wp / 2.0
                for (x, y) in (pts[0], pts[-1]):
                    draw.ellipse((x - rad, y - rad, x + rad, y + rad), fill=color)

    img = img.resize((args.px, args.px), Image.LANCZOS)

    os.makedirs(args.out, exist_ok=True)
    png_path = os.path.join(args.out, f"{args.name}.png")
    json_path = os.path.join(args.out, f"{args.name}.json")
    img.save(png_path, optimize=True)
    meta = {
        "name": args.name,
        "centerLatitude": lat0,
        "centerLongitude": lon0,
        "metersPerPixel": mpp,
        "sizeMeters": args.size_m,
        "pixels": args.px,
        "attribution": "© OpenStreetMap contributors",
    }
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2, ensure_ascii=False)

    print(f"  edificios: {len(buildings)}, vías: {len(roads)} {counts}, "
          f"quebradas/ríos: {len(waterways)}, agua: {len(waters)}, verdes: {len(greens)}")
    print(f"  -> {png_path}\n  -> {json_path}")


if __name__ == "__main__":
    main()
