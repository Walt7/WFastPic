"""Ricerca per significato in foto e video (CLIP), tutta in locale.

  python ricerca_semantica.py indicizza <cartella> <cartella_indici> [--ogni=5] [--max-frame=120]
  python ricerca_semantica.py cerca "<testo>" <cartella_indici> [--cartella=C] [--solo=lista.txt] [--limite=12]

Le immagini passano dal modello clip-ViT-B-32; le domande in italiano (o altre lingue) dal modello
clip-ViT-B-32-multilingual-v1, che scrive nello stesso spazio. I video diventano un fotogramma ogni N secondi
(ffmpeg), così il risultato dice a che secondo compare la scena. Nulla esce dal PC: dopo il primo scarico dei
modelli gira anche senza rete. Scrive solo nella cartella degli indici, mai nelle foto.
"""
import hashlib, json, os, subprocess, sys, tempfile, time

import numpy as np

FOTO = ('.jpg', '.jpeg', '.png', '.bmp', '.webp')
VIDEO = ('.mp4', '.mov', '.avi', '.mkv', '.m4v', '.3gp', '.mts', '.wmv')


def opzione(nome, predefinito):
    for a in sys.argv:
        if a.startswith(f'--{nome}='):
            return a.split('=', 1)[1]
    return predefinito


def chiave(percorso):
    return hashlib.sha1(os.path.normcase(percorso).encode('utf-8')).hexdigest()[:16]


def file_indice(cartella_indici, radice):
    return os.path.join(cartella_indici, f'clip_{chiave(radice)}.npz')


def scrivi_stato(cartella_indici, **campi):
    campi['aggiornato'] = time.strftime('%Y-%m-%d %H:%M:%S')
    tmp = os.path.join(cartella_indici, 'stato_clip.json.tmp')
    with open(tmp, 'w', encoding='utf-8') as f:
        json.dump(campi, f, ensure_ascii=False)
    os.replace(tmp, os.path.join(cartella_indici, 'stato_clip.json'))


def elenco(radice):
    for base, _, nomi in os.walk(radice):
        for n in nomi:
            e = os.path.splitext(n)[1].lower()
            if e in FOTO or e in VIDEO:
                p = os.path.join(base, n)
                try:
                    st = os.stat(p)
                except OSError:
                    continue
                yield p, e in VIDEO, f'{int(st.st_mtime)}-{st.st_size}'


def fotogrammi(video, ogni, massimo, cartella_tmp):
    """Percorsi dei fotogrammi estratti da un video (uno ogni <ogni> secondi), con il secondo di ciascuno."""
    modello = os.path.join(cartella_tmp, 'f_%04d.jpg')
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', video, '-vf', f'fps=1/{ogni},scale=384:-2',
                    '-frames:v', str(massimo), modello], capture_output=True, timeout=600)
    fs = sorted(f for f in os.listdir(cartella_tmp) if f.startswith('f_'))
    return [(os.path.join(cartella_tmp, f), i * ogni) for i, f in enumerate(fs)]


def indicizza(radice, cartella_indici):
    from PIL import Image, ImageFile
    from sentence_transformers import SentenceTransformer
    ImageFile.LOAD_TRUNCATED_IMAGES = True
    ogni, massimo = int(opzione('ogni', 5)), int(opzione('max-frame', 120))
    os.makedirs(cartella_indici, exist_ok=True)
    out = file_indice(cartella_indici, radice)
    vecchi = {}
    if os.path.exists(out):
        d = np.load(out, allow_pickle=False)
        for v, p, t, k in zip(d['v'], d['p'], d['t'], d['k']):
            vecchi.setdefault(str(p), []).append((v, int(t), str(k)))
    files = list(elenco(radice))
    scrivi_stato(cartella_indici, radice=radice, fase='lettura', fatti=0, totale=len(files), finita=False)
    modello = SentenceTransformer('clip-ViT-B-32', device='cpu')
    V, P, T, K = [], [], [], []
    fatti = errori = 0

    def salva():
        if V:
            np.savez(out, v=np.array(V, dtype=np.float16), p=np.array(P), t=np.array(T), k=np.array(K), radice=np.array(radice))

    for p, e_video, k in files:
        fatti += 1
        if p in vecchi and all(x[2] == k for x in vecchi[p]):
            for v, t, kk in vecchi[p]:
                V.append(v); P.append(p); T.append(t); K.append(kk)
            continue
        try:
            if e_video:
                with tempfile.TemporaryDirectory() as tmp:
                    fr = fotogrammi(p, ogni, massimo, tmp)
                    if not fr:
                        raise RuntimeError('nessun fotogramma')
                    ims = [Image.open(f).convert('RGB') for f, _ in fr]
                    emb = modello.encode(ims, normalize_embeddings=True, convert_to_numpy=True, batch_size=32)
                    for (_, sec), v in zip(fr, emb):
                        V.append(v); P.append(p); T.append(sec); K.append(k)
            else:
                im = Image.open(p)
                im.draft('RGB', (512, 512))
                v = modello.encode([im.convert('RGB')], normalize_embeddings=True, convert_to_numpy=True)[0]
                V.append(v); P.append(p); T.append(-1); K.append(k)
        except Exception as ex:  # file rotto: si salta, l'indice resta valido
            errori += 1
            print('SALTATO', p, ex, file=sys.stderr, flush=True)
        if fatti % 50 == 0:
            salva()
            scrivi_stato(cartella_indici, radice=radice, fase='calcolo', fatti=fatti, totale=len(files), errori=errori, finita=False)
    salva()
    scrivi_stato(cartella_indici, radice=radice, fase='finita', fatti=fatti, totale=len(files), errori=errori, finita=True, vettori=len(V))
    print(json.dumps({'file': len(files), 'vettori': len(V), 'errori': errori}))


def cerca(testo, cartella_indici):
    from sentence_transformers import SentenceTransformer
    limite = int(opzione('limite', 12))
    prefisso = opzione('cartella', None)
    solo = None
    if (f := opzione('solo', None)):
        with open(f, encoding='utf-8') as fh:
            solo = {os.path.normcase(r.strip()) for r in fh if r.strip()}
    modello = SentenceTransformer('sentence-transformers/clip-ViT-B-32-multilingual-v1', device='cpu')
    q = modello.encode(testo, normalize_embeddings=True, convert_to_numpy=True)
    miglior = {}  # percorso -> (punteggio, secondo)
    for nome in sorted(os.listdir(cartella_indici)):
        if not (nome.startswith('clip_') and nome.endswith('.npz')):
            continue
        d = np.load(os.path.join(cartella_indici, nome), allow_pickle=False)
        radice = str(d['radice'])
        if prefisso:
            a, b = os.path.normcase(os.path.abspath(prefisso)), os.path.normcase(os.path.abspath(radice))
            if not (a.startswith(b) or b.startswith(a)):
                continue
        s = d['v'].astype(np.float32) @ q
        for punt, p, t in zip(s, d['p'], d['t']):
            p = str(p)
            n = os.path.normcase(p)
            if prefisso and not n.startswith(os.path.normcase(os.path.abspath(prefisso))):
                continue
            if solo is not None and n not in solo:
                continue
            if p not in miglior or punt > miglior[p][0]:
                miglior[p] = (float(punt), int(t))
    ordine = sorted(miglior.items(), key=lambda x: -x[1][0])[:limite]
    print(json.dumps([{'percorso': p, 'punteggio': round(s, 3), 'secondo': (t if t >= 0 else None)} for p, (s, t) in ordine],
                     ensure_ascii=False))


if __name__ == '__main__':
    a = [x for x in sys.argv[1:] if not x.startswith('--')]
    if a[0] == 'indicizza':
        indicizza(a[1], a[2])
    elif a[0] == 'cerca':
        cerca(a[1], a[2])
    else:
        sys.exit('comando sconosciuto')
