#!/usr/bin/env python3
"""Render the auditory assist mode's narrator lines (Docs/DESIGN.md §11, M18) to WAV clips.

Reads Assets/FruitGame/Audio/Narration/lines.tsv and writes Assets/FruitGame/Resources/Narration/<key>.wav.
A key containing {n} is rendered for every number 0-99.

  Google Cloud Text-to-Speech (the narrator's real voice):
    export GOOGLE_TTS_API_KEY=...        # an API key restricted to the Text-to-Speech API, or
    # sign in with `gcloud auth login` and leave the key unset (the script uses `gcloud auth print-access-token`)
    python3 Tools/render_narration.py --engine google [--voice ko-KR-Neural2-B] [--rate 1.5] [--pitch 5] [--force]

  Try a voice on one line first, or list the Korean voices:
    python3 Tools/render_narration.py --engine google --only intro --force --voice ko-KR-Neural2-B
    python3 Tools/render_narration.py --list-voices

  macOS preview (local testing only, not for the booth):
    python3 Tools/render_narration.py --engine say [--force]

Existing clips are kept unless --force is given, so it's cheap to re-run after adding lines.
"""
import argparse, base64, json, os, subprocess, sys, tempfile, time, urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LINES = os.path.join(ROOT, "Assets/FruitGame/Audio/Narration/lines.tsv")
OUT = os.path.join(ROOT, "Assets/FruitGame/Resources/Narration")
GOOGLE_URL = "https://texttospeech.googleapis.com/v1/text:synthesize"
VOICES_URL = "https://texttospeech.googleapis.com/v1/voices?languageCode=ko-KR"


def load_lines():
    lines = []
    with open(LINES, encoding="utf-8") as f:
        for row in f:
            row = row.rstrip("\n")
            if not row or row.startswith("#"):
                continue
            key, text = row.split("\t", 1)
            if "{n}" in key:
                lines += [(key.replace("{n}", str(n)), text.replace("{n}", str(n))) for n in range(100)]
            else:
                lines.append((key, text))
    return lines


def google_headers():
    key = os.environ.get("GOOGLE_TTS_API_KEY")
    if key:
        return {"X-Goog-Api-Key": key}
    token = subprocess.run(["gcloud", "auth", "print-access-token"], capture_output=True, text=True, check=True).stdout.strip()
    project = subprocess.run(["gcloud", "config", "get-value", "project"], capture_output=True, text=True).stdout.strip()
    headers = {"Authorization": f"Bearer {token}"}
    if project:
        headers["X-Goog-User-Project"] = project
    return headers


def render_google(text, path, voice, rate, pitch, headers):
    body = {
        "input": {"text": text},
        "voice": {"languageCode": "ko-KR", "name": voice},
        "audioConfig": {"audioEncoding": "LINEAR16", "sampleRateHertz": 24000, "speakingRate": rate, "pitch": pitch},
    }
    req = urllib.request.Request(GOOGLE_URL, data=json.dumps(body).encode(), headers={**headers, "Content-Type": "application/json"})
    with urllib.request.urlopen(req) as resp:
        audio = base64.b64decode(json.load(resp)["audioContent"])
    with open(path, "wb") as f:
        f.write(audio)  # LINEAR16 responses include a WAV header


def render_say(text, path, rate):
    with tempfile.TemporaryDirectory() as tmp:
        aiff = os.path.join(tmp, "line.aiff")
        subprocess.run(["say", "-v", "Yuna", "-r", str(int(190 * rate)), "-o", aiff, text], check=True)
        subprocess.run(["afconvert", "-f", "WAVE", "-d", "LEI16@24000", aiff, path], check=True)


def progress(n, total, key, start):
    """A one-line progress bar: [#####-----]  12/238  5%  0:42 left  intro"""
    width = 30
    frac = n / total if total else 1.0
    bar = "#" * int(width * frac) + "-" * (width - int(width * frac))
    left = ""
    if 0 < n < total:
        secs = (time.time() - start) / n * (total - n)
        left = f"  {int(secs // 60)}:{int(secs % 60):02d} left"
    sys.stdout.write(f"\r[{bar}] {n:>3}/{total}  {frac * 100:3.0f}%{left}  {key[:24]:<24}")
    sys.stdout.flush()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--engine", choices=["google", "say"], default="google")
    ap.add_argument("--voice", default="ko-KR-Neural2-B", help="Google voice name (e.g. ko-KR-Neural2-A, ko-KR-Wavenet-A)")
    ap.add_argument("--rate", type=float, default=1.5, help="speaking rate (1 = normal)")
    ap.add_argument("--pitch", type=float, default=5.0, help="Google pitch in semitones")
    ap.add_argument("--jobs", type=int, default=6, help="lines rendered at once (Google; the macOS voice runs 4)")
    ap.add_argument("--force", action="store_true", help="re-render clips that already exist")
    ap.add_argument("--only", nargs="+", help="render just these keys (e.g. intro score_now_12)")
    ap.add_argument("--list-voices", action="store_true", help="list Google's Korean voices and stop")
    args = ap.parse_args()

    if args.list_voices:
        with urllib.request.urlopen(urllib.request.Request(VOICES_URL, headers=google_headers())) as resp:
            for v in json.load(resp).get("voices", []):
                print(f"{v['name']:32} {v.get('ssmlGender', '')}")
        return

    os.makedirs(OUT, exist_ok=True)
    headers = google_headers() if args.engine == "google" else None
    todo, skipped = [], 0
    for key, text in load_lines():
        if args.only and key not in args.only:
            continue
        path = os.path.join(OUT, key + ".wav")
        if os.path.exists(path) and not args.force:
            skipped += 1
        else:
            todo.append((key, text, path))

    def render(item):
        key, text, path = item
        if args.engine == "google":
            render_google(text, path, args.voice, args.rate, args.pitch, headers)
        else:
            render_say(text, path, args.rate)
        return key

    done, failed, start = 0, [], time.time()
    progress(0, len(todo), "", start)
    jobs = max(1, args.jobs if args.engine == "google" else min(args.jobs, 4))
    with ThreadPoolExecutor(max_workers=jobs) as pool:
        futures = {pool.submit(render, item): item[0] for item in todo}
        for future in as_completed(futures):
            key = futures[future]
            try:
                future.result()
                done += 1
            except Exception as e:  # keep going; report at the end
                failed.append((key, e))
            progress(done + len(failed), len(todo), key, start)
    print()
    for key, e in failed:
        print(f"  failed: {key}: {e}")
    if args.only:
        print(f"rendered {done} of {', '.join(args.only)}")
        return 1 if failed else 0
    with open(os.path.join(OUT, "ENGINE.txt"), "w", encoding="utf-8") as f:
        f.write(f"{args.engine} {args.voice if args.engine == 'google' else 'Yuna'} rate {args.rate}\n")
    print(f"rendered {done}, kept {skipped} existing{f', {len(failed)} failed' if failed else ''}, into {OUT}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
