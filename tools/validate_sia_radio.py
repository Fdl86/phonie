#!/usr/bin/env python3
from __future__ import annotations
import argparse, hashlib, json, re, sys
from pathlib import Path

FREQ_LITERAL=re.compile(r"(?<![\w.])(?:11[89]|12\d|13[0-6])\.\d{2,3}(?![\w.])")

def load(path:Path):
    return json.loads(path.read_text(encoding="utf-8"))

def main():
    ap=argparse.ArgumentParser(); ap.add_argument("--root",default="."); ap.add_argument("--min-airports",type=int,default=200); ap.add_argument("--min-frequencies",type=int,default=150); args=ap.parse_args()
    root=Path(args.root); base=root/'data/radio/france'; manifest=load(base/'manifest.json')
    assert manifest['schemaVersion']==2 and manifest['authority']=='SIA' and not manifest.get('bootstrapRequired'), 'Manifest SIA non publié.'
    desc=manifest.get('current'); assert desc, 'Descripteur current absent.'
    path=base/desc['relativePath']; raw=path.read_bytes(); assert hashlib.sha256(raw).hexdigest()==desc['sha256'], 'SHA current incorrect.'
    data=json.loads(raw); assert data['schemaVersion']==2 and data['authority']=='SIA'
    airports=data['airports']; freqs=[f for a in airports for f in a['frequencies']]
    assert len(airports)>=args.min_airports and len(freqs)>=args.min_frequencies, f'Couverture insuffisante {len(airports)}/{len(freqs)}.'
    assert len({a['icao'] for a in airports})==len(airports), 'ICAO dupliqué.'
    for a in airports:
        assert re.fullmatch(r'(?:LF|TF|FM|NT|NW)[A-Z0-9]{2}',a['icao']), f"ICAO SIA français invalide {a['icao']}"
        for f in a['frequencies']:
            assert 117975<=int(f['channelKhz'])<=137000, f"Canal hors bande {a['icao']} {f['channel']}"
            assert f['sourceUrl'] and f['sourceReference'], f"Source absente {a['icao']}"
    forbidden=[]
    for path in (root/'src').rglob('*.cs'):
        for n,line in enumerate(path.read_text(encoding='utf-8').splitlines(),1):
            if FREQ_LITERAL.search(line): forbidden.append(f'{path.relative_to(root)}:{n}: {line.strip()}')
    assert not forbidden, 'Fréquences opérationnelles codées en dur dans src:\n'+'\n'.join(forbidden)
    old=root/'data/radio/france-official.json'
    if old.exists():
        tombstone=load(old)
        assert tombstone.get('deprecated') is True and tombstone.get('usedByPhonie') is False, 'Ancienne table radio non neutralisée.'
        assert 'frequencies' not in tombstone and not FREQ_LITERAL.search(old.read_text(encoding='utf-8')), 'Fréquence résiduelle dans la table neutralisée.'
    print(f"Validation SIA OK - {len(airports)} aérodromes, {len(freqs)} fréquences, aucune fréquence codée dans src.")
if __name__=='__main__':
    try: main()
    except Exception as exc:
        print(f'ERREUR VALIDATION SIA: {exc}',file=sys.stderr); raise
