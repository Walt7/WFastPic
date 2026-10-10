"""Rigenera l'icona e il logo di WFastPic da icone.html (sole-obiettivo sopra le immagini "base").

Serve Microsoft Edge (c'e' gia' in Windows) e Python 3. Scrive in assets/:
icon-512.png, icon-256.png, logo.png e icon.ico (16, 24, 32, 48, 64, 128, 256 px, ognuna una PNG).

    python assets/sorgenti/genera-icone.py
"""
import os
import pathlib
import struct
import subprocess
import tempfile

SORGENTI = pathlib.Path(__file__).resolve().parent
ASSETS = SORGENTI.parent
PAGINA = (SORGENTI / 'icone.html').as_uri()
DIMENSIONI_ICO = [16, 24, 32, 48, 64, 128, 256]


def trova_edge():
    for base in (os.environ.get('ProgramFiles(x86)'), os.environ.get('ProgramFiles')):
        if base:
            p = pathlib.Path(base) / 'Microsoft' / 'Edge' / 'Application' / 'msedge.exe'
            if p.exists():
                return str(p)
    raise SystemExit('Microsoft Edge non trovato')


EDGE = trova_edge()


def render(query, larghezza, altezza, destinazione, scala=1.0):
    destinazione = pathlib.Path(destinazione)
    if destinazione.exists():
        destinazione.unlink()
    subprocess.run(
        [EDGE, '--headless', '--disable-gpu', '--hide-scrollbars', f'--force-device-scale-factor={scala}',
         '--default-background-color=00000000', f'--window-size={larghezza},{altezza}',
         f'--screenshot={destinazione}', f'{PAGINA}?{query}'],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=120, check=False)
    if not destinazione.exists():
        raise SystemExit(f'render fallito: {destinazione}')


def scrivi_ico(percorso, png_per_dimensione):
    n = len(png_per_dimensione)
    testa = struct.pack('<HHH', 0, 1, n)
    offset = 6 + 16 * n
    voci, dati = b'', b''
    for d, png in png_per_dimensione:
        lato = 0 if d >= 256 else d
        voci += struct.pack('<BBBBHHII', lato, lato, 0, 0, 1, 32, len(png), offset)
        dati += png
        offset += len(png)
    pathlib.Path(percorso).write_bytes(testa + voci + dati)


def main():
    render('m=icona&s=512', 512, 512, ASSETS / 'icon-512.png')
    render('m=icona&s=256', 256, 256, ASSETS / 'icon-256.png')
    render('m=logo', 1600, 480, ASSETS / 'logo.png')

    png = []
    with tempfile.TemporaryDirectory() as tmp:
        for d in DIMENSIONI_ICO:
            f = pathlib.Path(tmp) / f'{d}.png'
            # Finestra da 512 px e fattore di scala: Edge non fa screenshot di finestre minuscole (16 px).
            render('m=icona&s=512', 512, 512, f, scala=d / 512)
            png.append((d, f.read_bytes()))
    scrivi_ico(ASSETS / 'icon.ico', png)
    print('Icone rigenerate in', ASSETS)


if __name__ == '__main__':
    main()
