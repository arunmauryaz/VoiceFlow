"""Generate GitHub-safe SVG documentation graphics (Python standard library only)."""
from pathlib import Path
from html import escape

DEST = Path(__file__).resolve().parents[1] / "readme"
DEST.mkdir(exist_ok=True)
FONT = 'font-family="Segoe UI,Arial,sans-serif"'

def text(x, y, value, size=18, color="#a9b3c3", weight=400):
    return f'<text x="{x}" y="{y}" {FONT} font-size="{size}" font-weight="{weight}" fill="{color}">{escape(value)}</text>'

def svg(name, width, height, title, desc, body):
    content = f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-labelledby="title desc">
<title id="title">{escape(title)}</title><desc id="desc">{escape(desc)}</desc>
<rect x="1" y="1" width="{width-2}" height="{height-2}" rx="22" fill="#11161e" stroke="#303947"/>
{body}
</svg>\n'''
    (DEST / name).write_text(content, encoding="utf-8")

body = text(32, 45, "A starting point for your PC", 27, "#f2f5fa", 650)
body += text(32, 74, "No dedicated GPU required. Choose based on memory headroom and language.", 17)
cards = [
    (32, "TIGHT ON RAM", "4 GB class", "Gemini cloud", "No local model load", "Internet + your API key", "#d1d7e2"),
    (322, "EVERYDAY LAPTOP", "8 GB class", "Whisper Tiny / Base", "Start with CPU", "Multilingual, including Hindi", "#d1d7e2"),
    (612, "MORE HEADROOM", "16 GB+ class", "Small Q5 / Bark", "Bark: English only", "Several GB free RAM for Bark", "#d1d7e2"),
]
for x, label, ram, model, line1, line2, color in cards:
    body += f'<rect x="{x}" y="103" width="270" height="190" rx="15" fill="#1c2430" stroke="#354153"/>'
    body += text(x+18, 134, label, 13, "#9caec9", 650)
    body += text(x+18, 177, ram, 29, "#fff", 650)
    body += text(x+18, 216, model, 19, color, 600)
    body += text(x+18, 250, line1, 15)
    body += text(x+18, 275, line2, 14)
body += text(32, 333, "RAM bands are recommendations, not validated minimums or speed benchmarks.", 15)
body += text(32, 358, "Local Whisper also needs Windows 11 + AVX2 / FMA / F16C. Full requirements below.", 15)
svg("pc-guide.svg", 914, 385, "Which PC can use VoiceFlow?", "Starting recommendations: limited-memory 4 GB class PCs should try Gemini cloud, 8 GB class PCs should start with Whisper Tiny or Base, and 16 GB or more provides headroom for Small Q5 or English-only Bark. These are not tested minimums. Local Whisper requires Windows 11 and supported CPU instructions.", body)

body = text(32, 45, "Small downloads. Different workloads.", 27, "#f2f5fa", 650)
body += text(32, 74, "Four local options, downloaded when you select them.", 17)
rows = [
    ("Whisper Tiny", 78, "Multilingual · lightest local choice"),
    ("Whisper Base", 148, "Multilingual · everyday starting point"),
    ("Whisper Small Q5", 190, "Multilingual · more local compute"),
    ("Bark 3.0 Clean", 180, "English · built-in filler cleanup"),
]
for i, (label, mb, detail) in enumerate(rows):
    y = 122 + i*72
    body += text(32, y, label, 18, "#edf1f7", 600)
    body += f'<rect x="249" y="{y-17}" width="400" height="18" rx="7" fill="#283241"/>'
    body += f'<rect x="249" y="{y-17}" width="{400*mb/200:.1f}" height="18" rx="7" fill="#aebed3"/>'
    body += text(670, y, f"~{mb} MB", 18, "#edf1f7", 600)
    body += text(249, y+25, detail, 15)
body += text(32, 422, "Gemini cloud: no local weights • Requires internet and your API key", 17, "#edf1f7")
body += text(32, 458, "Bars compare download size, not RAM, total disk usage, speed, or accuracy.", 15)
body += text(32, 483, "Bark adds a Python runtime and an optional ~2.5 GB decoded cache.", 15)
svg("model-downloads.svg", 914, 510, "VoiceFlow model download sizes", "Whisper Tiny approximately 78 MB, Base 148 MB, Small Q5 190 MB, and Underdog Bark Clean 180 MB. Gemini needs no local model weights. Download size is not memory usage or total installed size; Bark has a separate runtime and decoded cache.", body)

body = text(32, 43, "One shortcut. Two ways to transcribe.", 26, "#f2f5fa", 650)
for x,w,label,sub in [(32,210,"SPEAK","Hold or toggle"),(304,250,"LOCAL OR CLOUD","Whisper / Bark / Gemini"),(610,270,"YOUR TEXT FIELD","Finished or phrase-by-phrase")]:
    body += f'<rect x="{x}" y="75" width="{w}" height="85" rx="14" fill="#1c2430" stroke="#354153"/>'
    body += text(x+18,106,label,17,"#f2f5fa",650) + text(x+18,136,sub,15)
body += '<path d="M251 116h42m-9-6 9 6-9 6 M563 116h36m-9-6 9 6-9 6" stroke="#8c9bb1" stroke-width="2" fill="none"/>'
body += text(32,225,"Local mode keeps recognition on your PC. Cloud mode sends audio to Gemini.",16)
svg("dictation-flow.svg",914,250,"VoiceFlow dictation workflow","Press a hold or toggle shortcut, speak, choose local Whisper or Bark or Gemini cloud, and insert a complete transcript or recognized phrases into the focused text field.",body)
print("Generated 3 README SVGs")
