"""
Pipeline de assets de "Brinquedos vs. Monstros do Escuro".

Etapas:
  concepts  -> Nano Banana (Gemini) gera a arte conceitual de cada personagem/prop
               (fundo branco). Uma copia com fundo transparente vira o icone da UI.
  textures  -> Nano Banana gera texturas (chao, papel de parede, tapete) e a arte de titulo.
  models    -> Tripo AI (API v3, image-to-model) converte cada conceito em .glb texturizado (PBR).

Uso (a partir da raiz do projeto Unity):
  python Tools/AssetPipeline/generate_assets.py concepts
  python Tools/AssetPipeline/generate_assets.py textures
  python Tools/AssetPipeline/generate_assets.py models
  python Tools/AssetPipeline/generate_assets.py all
  python Tools/AssetPipeline/generate_assets.py models --only teddy_bear,rc_car --force

Variaveis de ambiente necessarias: GEMINI_API_KEY e TRIPO_API_KEY.
O estado das tarefas do Tripo fica em Tools/AssetPipeline/output/state.json, entao rodar de novo
retoma tarefas pendentes em vez de gastar creditos outra vez.
"""

import argparse
import base64
import json
import os
import sys
import threading
import time
import urllib.error
import urllib.request
import uuid
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent / "output"
CONCEPTS = OUT / "concepts"
PREVIEWS = OUT / "previews"
STATE_FILE = OUT / "state.json"

ART = ROOT / "Assets" / "SleepDefenders" / "Art"
MODELS_DIR = ART / "Models"
ICONS_DIR = ART / "Icons"
TEXTURES_DIR = ART / "Textures"
UI_DIR = ART / "UI"

GEMINI_MODEL = "gemini-3-pro-image"  # "Nano Banana Pro"
GEMINI_URL = "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"

TRIPO_BASE = "https://openapi.tripo3d.ai/v3"
TRIPO_MODEL = "v3.1-20260211"

# Estilo visual compartilhado por todos os conceitos, para os modelos ficarem coerentes entre si.
STYLE = (
    "Stylized 3D game asset render, chunky cartoon proportions, soft vinyl and plastic toy materials, "
    "vibrant colors, clean readable silhouette, single object centered and fully visible with margin, "
    "three-quarter front view, plain pure white background, soft even studio lighting, "
    "no ground shadow, no text, no logo, no frame."
)

MONSTER_STYLE = (
    "Cute-but-spooky creature for a children's game (not gory, not scary for kids), "
    "made of solid opaque materials (no smoke, no transparency, no particles). "
)

# id, categoria, prompt do conceito, face_limit no Tripo
ASSETS = [
    # --- Brinquedos (defensores) ---
    ("toy_lamp", "toy",
     "A cute toy bedside lamp character: chunky pastel-yellow plastic base shaped like a little mushroom, "
     "round warm-glowing lampshade with tiny star-shaped cutouts, small happy face on the base, stubby feet.", 25000),
    ("laser_robot", "toy",
     "A cute retro wind-up toy robot, glossy red and silver tin-and-plastic body, boxy head with one big round "
     "cyan visor eye, antenna with a red bulb, right arm is a chunky laser blaster cannon with a glowing cyan tip "
     "pointing forward, stubby legs, wind-up key on its back, heroic standing pose.", 30000),
    ("teddy_bear", "toy",
     "A big chubby fluffy brown teddy bear plush toy sitting upright, sewn fabric patches, shiny black button eyes, "
     "red bow tie, arms spread wide like a protective guardian, brave determined expression.", 30000),
    ("rc_car", "toy",
     "A cute chunky toy remote-control monster truck, bright orange and blue plastic body, oversized knobby black "
     "rubber wheels, small antenna with a little flag, yellow lightning bolt decal on the side.", 25000),
    # Ultima defesa de cada faixa (fica na beira da cama e varre a faixa uma vez).
    ("windup_mouse", "toy",
     "A cute vintage wind-up toy mouse, shiny silver-blue tin body, round pink ears, big friendly eyes, pink nose, "
     "little red wheels underneath, large golden wind-up key on its back, curly spring tail, eager dash pose.", 20000),

    # --- Monstros do Escuro (inimigos) ---
    ("shadow_creeper", "monster",
     MONSTER_STYLE +
     "A hunched crawling shadow creature made of dark indigo-purple velvet, long thin arms with three-finger "
     "claws reaching forward, big glowing yellow eyes, wide toothy grin, short stubby legs, walking pose.", 30000),
    ("closet_monster", "monster",
     MONSTER_STYLE +
     "A huge hulking furry closet monster, shaggy dark blue-purple fur, round heavy body, two small curved horns, "
     "one large glowing yellow eye, wide mouth with blunt teeth, long gorilla-like arms, a striped sock stuck on "
     "one horn and a wooden coat hanger hooked on the other, standing pose.", 30000),
    ("winged_nightmare", "monster",
     MONSTER_STYLE +
     "A small flying nightmare imp: round dark purple body, large spread bat-like leathery wings, glowing magenta "
     "eyes, two tiny fangs, curled pointy tail, little claws, hovering pose with wings spread wide.", 30000),
    # Devorador de Luz: se esgueira entre os brinquedos e come os abajures.
    ("light_eater", "monster",
     MONSTER_STYLE +
     "A sneaky slinky shadow creature with a long low weasel-like body made of dark navy-black velvet, four short "
     "legs, a big round greedy open mouth with blunt teeth, small glowing cyan eyes, a round belly painted with a "
     "warm yellow glow and the outlines of little swallowed light bulbs, a curly tail ending in a tiny light bulb, "
     "sneaking pose with the head low.", 30000),

    # --- Cenario do quarto ---
    ("bed", "prop",
     "A child's single bed, rounded wooden frame painted pastel blue, navy blue quilt with yellow stars and moons, "
     "fluffy white pillow, headboard shaped like a crescent moon, cozy toy-like proportions.", 30000),
    ("wardrobe", "prop",
     "A tall old wooden wardrobe closet, slightly crooked whimsical shape, dark purple-brown wood, both doors "
     "slightly ajar with pitch black darkness inside, round brass knobs, small carved moon on top.", 25000),
    ("toy_chest", "prop",
     "A wooden toy chest painted red with yellow stars, lid open, toys peeking out: a striped ball, colorful "
     "building blocks and a toy sword.", 25000),
    ("bookshelf", "prop",
     "A small wooden children's bookshelf painted mint green, three shelves with colorful picture books, "
     "a small globe and a toy rocket on top.", 25000),
    ("toy_blocks", "prop",
     "A small stack of colorful wooden alphabet building blocks, red, blue, yellow and green, slightly messy.", 15000),
]

# Texturas (sem Tripo). Os nomes de arquivo sao usados pelo construtor da cena na Unity.
TEXTURES = [
    ("floor_wood", "1:1",
     "Seamless tileable texture of warm honey-colored wooden floor planks, orthographic top-down view, "
     "flat even lighting, no shadows, no perspective, game texture.", True),
    ("wallpaper_night", "1:1",
     "Seamless tileable children's bedroom wallpaper pattern: deep navy blue background with small scattered "
     "yellow stars and tiny crescent moons, flat illustration, no perspective, game texture.", True),
    ("rug_wool", "1:1",
     "Seamless tileable texture of soft knitted wool rug fabric, light cream color, close-up top-down, "
     "flat even lighting, no shadows, game texture.", True),
    ("title_art", "16:9",
     "Key art for a cozy tower defense video game titled \"Brinquedos vs. Monstros do Escuro\" "
     "(write the title exactly like this, in a playful rounded font at the top). "
     "A child's bedroom at night lit by moonlight: brave toys (a retro red robot with a laser arm, a chubby teddy "
     "bear, a toy lamp and an orange RC monster truck) stand on a rug defending a bed against cute-but-spooky "
     "purple shadow monsters coming out of a wardrobe. Stylized 3D cartoon render, warm vs cool lighting.", False),
]

_print_lock = threading.Lock()


def log(*args):
    with _print_lock:
        print(*args, flush=True)


# ----------------------------------------------------------------------------- HTTP helpers

def http_json(method, url, headers, body=None, timeout=120):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers=headers)
    if data is not None:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return json.loads(r.read())
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"HTTP {e.code} em {url}: {e.read()[:600]!r}") from None


def download(url, dest: Path):
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(dest.suffix + ".part")
    with urllib.request.urlopen(url, timeout=300) as r, open(tmp, "wb") as f:
        while True:
            chunk = r.read(1 << 16)
            if not chunk:
                break
            f.write(chunk)
    tmp.replace(dest)


def multipart_upload(url, headers, field, path: Path, mime):
    boundary = uuid.uuid4().hex
    payload = (
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="{field}"; filename="{path.name}"\r\n'
        f"Content-Type: {mime}\r\n\r\n"
    ).encode() + path.read_bytes() + f"\r\n--{boundary}--\r\n".encode()
    req = urllib.request.Request(url, data=payload, method="POST", headers=dict(headers))
    req.add_header("Content-Type", f"multipart/form-data; boundary={boundary}")
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            return json.loads(r.read())
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"HTTP {e.code} no upload: {e.read()[:600]!r}") from None


# ----------------------------------------------------------------------------- Nano Banana

def gemini_image(prompt, dest: Path, aspect="1:1", size="1K"):
    key = os.environ["GEMINI_API_KEY"]
    body = {
        "contents": [{"parts": [{"text": prompt}]}],
        "generationConfig": {
            "responseModalities": ["IMAGE"],
            "imageConfig": {"aspectRatio": aspect, "imageSize": size},
        },
    }
    for attempt in range(3):
        try:
            resp = http_json("POST", GEMINI_URL.format(model=GEMINI_MODEL), {"x-goog-api-key": key}, body, timeout=240)
            for cand in resp.get("candidates", []):
                for part in cand.get("content", {}).get("parts", []):
                    inline = part.get("inlineData") or part.get("inline_data")
                    if inline and inline.get("data"):
                        dest.parent.mkdir(parents=True, exist_ok=True)
                        raw = base64.b64decode(inline["data"])
                        tmp = dest.with_suffix(".raw")
                        tmp.write_bytes(raw)
                        Image.open(tmp).convert("RGB").save(dest)  # normaliza para PNG
                        tmp.unlink()
                        return dest
            raise RuntimeError(f"resposta sem imagem: {json.dumps(resp)[:400]}")
        except Exception as e:  # noqa: BLE001
            log(f"  [gemini] tentativa {attempt + 1} falhou para {dest.name}: {e}")
            time.sleep(4 * (attempt + 1))
    raise RuntimeError(f"Nao foi possivel gerar {dest.name}")


def remove_white_background(src: Path, dest: Path, tolerance=28):
    """Remove o fundo branco por flood-fill a partir das bordas (preserva brancos internos do personagem)."""
    img = Image.open(src).convert("RGBA")
    w, h = img.size
    # Mascara: pixels "quase brancos" conectados a borda viram fundo.
    probe = img.convert("RGB").copy()
    marker = (255, 0, 255)
    for x, y in [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1), (w // 2, 0), (w // 2, h - 1), (0, h // 2), (w - 1, h // 2)]:
        r, g, b = probe.getpixel((x, y))
        if min(r, g, b) > 255 - tolerance * 2:
            ImageDraw.floodfill(probe, (x, y), marker, thresh=tolerance * 3)
    mask = Image.new("L", (w, h), 255)
    px_probe = probe.load()
    px_mask = mask.load()
    for y in range(h):
        for x in range(w):
            if px_probe[x, y] == marker:
                px_mask[x, y] = 0
    mask = mask.filter(ImageFilter.GaussianBlur(1.2))
    img.putalpha(mask)
    bbox = mask.getbbox()
    if bbox:
        # recorta com margem e deixa quadrado
        pad = int(max(w, h) * 0.04)
        l, t, r, b = bbox
        l, t, r, b = max(0, l - pad), max(0, t - pad), min(w, r + pad), min(h, b + pad)
        img = img.crop((l, t, r, b))
        side = max(img.size)
        square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        square.paste(img, ((side - img.size[0]) // 2, (side - img.size[1]) // 2))
        img = square.resize((512, 512), Image.LANCZOS)
    dest.parent.mkdir(parents=True, exist_ok=True)
    img.save(dest)


def make_seamless(path: Path):
    """Cruza a imagem com ela mesma deslocada meia volta para esconder as emendas do tile."""
    img = Image.open(path).convert("RGB")
    w, h = img.size
    shifted = Image.new("RGB", (w, h))
    shifted.paste(img.crop((w // 2, h // 2, w, h)), (0, 0))
    shifted.paste(img.crop((0, h // 2, w // 2, h)), (w - w // 2, 0))
    shifted.paste(img.crop((w // 2, 0, w, h // 2)), (0, h - h // 2))
    shifted.paste(img.crop((0, 0, w // 2, h // 2)), (w - w // 2, h - h // 2))
    # mascara: 255 no centro (imagem original), 0 nas bordas (versao deslocada, que e continua ali)
    mask = Image.new("L", (w, h))
    mp = mask.load()
    for y in range(h):
        dy = abs(y - h / 2) / (h / 2)
        for x in range(w):
            dx = abs(x - w / 2) / (w / 2)
            d = max(dx, dy)
            v = 1.0 - max(0.0, (d - 0.55) / 0.45)
            mp[x, y] = int(255 * max(0.0, min(1.0, v)))
    Image.composite(img, shifted, mask).save(path)


def run_concepts(only, force):
    jobs = [a for a in ASSETS if not only or a[0] in only]

    def job(asset):
        aid, kind, prompt, _ = asset
        concept = CONCEPTS / f"{aid}.png"
        if concept.exists() and not force:
            log(f"[concept] {aid}: ja existe, pulando")
        else:
            log(f"[concept] {aid}: gerando...")
            gemini_image(f"{prompt} {STYLE}", concept)
            log(f"[concept] {aid}: ok")
        if kind in ("toy", "monster"):
            remove_white_background(concept, ICONS_DIR / f"{aid}.png")

    with ThreadPoolExecutor(max_workers=4) as ex:
        for f in [ex.submit(job, a) for a in jobs]:
            f.result()


def run_textures(only, force):
    jobs = [t for t in TEXTURES if not only or t[0] in only]

    def job(tex):
        tid, aspect, prompt, seamless = tex
        dest = (UI_DIR if tid == "title_art" else TEXTURES_DIR) / f"{tid}.png"
        if dest.exists() and not force:
            log(f"[texture] {tid}: ja existe, pulando")
            return
        log(f"[texture] {tid}: gerando...")
        gemini_image(prompt, dest, aspect=aspect, size="2K" if tid == "title_art" else "1K")
        if seamless:
            make_seamless(dest)
        log(f"[texture] {tid}: ok")

    with ThreadPoolExecutor(max_workers=4) as ex:
        for f in [ex.submit(job, t) for t in jobs]:
            f.result()


# ----------------------------------------------------------------------------- Tripo AI

_state_lock = threading.Lock()


def load_state():
    if STATE_FILE.exists():
        return json.loads(STATE_FILE.read_text())
    return {}


def save_state(state):
    with _state_lock:
        OUT.mkdir(parents=True, exist_ok=True)
        STATE_FILE.write_text(json.dumps(state, indent=2))


def tripo_headers():
    return {"Authorization": "Bearer " + os.environ["TRIPO_API_KEY"]}


def tripo_balance():
    resp = http_json("GET", f"{TRIPO_BASE}/account/balance", tripo_headers())
    return resp["data"]


def run_models(only, force):
    state = load_state()
    jobs = [a for a in ASSETS if not only or a[0] in only]
    log(f"[tripo] saldo antes: {tripo_balance()}")

    def job(asset):
        aid, _kind, _prompt, face_limit = asset
        glb = MODELS_DIR / f"{aid}.glb"
        if glb.exists() and not force:
            log(f"[tripo] {aid}: .glb ja existe, pulando")
            return
        concept = CONCEPTS / f"{aid}.png"
        if not concept.exists():
            raise RuntimeError(f"conceito ausente para {aid}; rode a etapa 'concepts' antes")

        entry = state.get(aid, {})
        task_id = None if force else entry.get("task_id")
        if not task_id:
            up = multipart_upload(f"{TRIPO_BASE}/files", tripo_headers(), "file", concept, "image/png")
            token = up["data"]["file_token"]
            body = {
                "input": token,
                "model": TRIPO_MODEL,
                "texture": True,
                "pbr": True,
                "texture_quality": "standard",
                "face_limit": face_limit,
            }
            resp = http_json("POST", f"{TRIPO_BASE}/generation/image-to-model", tripo_headers(), body)
            if resp.get("code") != 0:
                raise RuntimeError(f"Tripo recusou {aid}: {resp}")
            task_id = resp["data"]["task_id"]
            with _state_lock:
                state[aid] = {"task_id": task_id, "status": "submitted"}
            save_state(state)
            log(f"[tripo] {aid}: tarefa {task_id} criada")
        else:
            log(f"[tripo] {aid}: retomando tarefa {task_id}")

        deadline = time.time() + 30 * 60
        last_progress = -1
        while time.time() < deadline:
            data = http_json("GET", f"{TRIPO_BASE}/tasks/{task_id}", tripo_headers())["data"]
            status = data.get("status")
            progress = data.get("progress", 0)
            if progress != last_progress and progress % 25 == 0:
                log(f"[tripo] {aid}: {status} {progress}%")
                last_progress = progress
            if status == "success":
                out = data.get("output", {})
                url = out.get("model_url") or out.get("pbr_model") or out.get("model")
                if not url:
                    raise RuntimeError(f"{aid}: saida sem model_url: {out}")
                download(url, glb)
                if out.get("rendered_image_url"):
                    try:
                        download(out["rendered_image_url"], PREVIEWS / f"{aid}.webp")
                    except Exception as e:  # noqa: BLE001
                        log(f"[tripo] {aid}: preview nao baixou ({e})")
                with _state_lock:
                    state[aid].update(status="done", credits=data.get("credits_consumed"))
                save_state(state)
                log(f"[tripo] {aid}: OK -> {glb.relative_to(ROOT)} ({glb.stat().st_size // 1024} KB)")
                return
            if status in ("failed", "cancelled", "banned", "expired"):
                with _state_lock:
                    state[aid].update(status=status, error=data.get("error_message"))
                save_state(state)
                raise RuntimeError(f"{aid}: tarefa terminou com status {status}: {data.get('error_message')}")
            time.sleep(6)
        raise RuntimeError(f"{aid}: tempo esgotado esperando o Tripo")

    errors = []
    with ThreadPoolExecutor(max_workers=6) as ex:
        futures = {ex.submit(job, a): a[0] for a in jobs}
        for f, aid in futures.items():
            try:
                f.result()
            except Exception as e:  # noqa: BLE001
                errors.append(f"{aid}: {e}")
                log(f"[tripo] ERRO {aid}: {e}")
    log(f"[tripo] saldo depois: {tripo_balance()}")
    if errors:
        sys.exit("Falhas:\n  " + "\n  ".join(errors))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("stage", choices=["concepts", "textures", "models", "all"])
    ap.add_argument("--only", default="", help="ids separados por virgula")
    ap.add_argument("--force", action="store_true", help="regera mesmo que o arquivo exista")
    args = ap.parse_args()
    only = {s.strip() for s in args.only.split(",") if s.strip()}

    for var in ("GEMINI_API_KEY", "TRIPO_API_KEY"):
        if var not in os.environ:
            sys.exit(f"Defina a variavel de ambiente {var}")

    if args.stage in ("concepts", "all"):
        run_concepts(only, args.force)
    if args.stage in ("textures", "all"):
        run_textures(only, args.force)
    if args.stage in ("models", "all"):
        run_models(only, args.force)


if __name__ == "__main__":
    main()
