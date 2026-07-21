#!/usr/bin/env python3
"""Lightweight SIA catalogue probe used by the scheduled workflow."""
from __future__ import annotations
import argparse, hashlib, json, re, sys
from pathlib import Path
import requests
from bs4 import BeautifulSoup

CATALOG = "https://www.sia.aviation-civile.gouv.fr/produits-numeriques-en-libre-disposition/les-bases-de-donnees-sia.html"
HEADERS = {"User-Agent": "PHONIE-SIA-Radio-Probe/0.4.1.0 (+https://github.com/Fdl86/phonie)"}

def snapshot() -> dict:
    r=requests.get(CATALOG,headers=HEADERS,timeout=30); r.raise_for_status()
    soup=BeautifulSoup(r.text,"html.parser")
    text=" ".join(soup.get_text(" ").split())
    products=[]
    pattern=re.compile(r"Donn[eé]es a[eé]ronautiques XML AIRAC\s+([0-9]{2}/[0-9]{2})(?:\s*-?CORRIGENDUM)?.{0,500}?En vigueur du\s+(\d{2}/\d{2}/20\d{2})\s+au\s+(\d{2}/\d{2}/20\d{2})",re.I|re.S)
    for m in pattern.finditer(text):
        products.append({"cycle":m.group(1),"from":m.group(2),"until":m.group(3),"corrigendum":"CORRIGENDUM" in m.group(0).upper()})
    if not products: raise RuntimeError("Aucun produit AIRAC détecté sur le catalogue SIA.")
    products=sorted({json.dumps(p,sort_keys=True):p for p in products}.values(),key=lambda p:p["from"])
    signature=hashlib.sha256(json.dumps(products,sort_keys=True,separators=(",",":")).encode()).hexdigest()
    return {"schemaVersion":1,"catalogUrl":CATALOG,"signature":signature,"products":products}

def main()->int:
    ap=argparse.ArgumentParser(); ap.add_argument("--root",default="."); ap.add_argument("--write",action="store_true"); ap.add_argument("--github-output",default=""); ap.add_argument("--force",action="store_true"); args=ap.parse_args()
    root=Path(args.root); path=root/"data/radio/france/source-state.json"; state=snapshot()
    previous={}
    if path.exists():
        try: previous=json.loads(path.read_text(encoding="utf-8"))
        except Exception: previous={}
    manifest={}
    mp=root/"data/radio/france/manifest.json"
    if mp.exists():
        try: manifest=json.loads(mp.read_text(encoding="utf-8"))
        except Exception: manifest={}
    changed=args.force or manifest.get("bootstrapRequired",True) or previous.get("signature")!=state["signature"]
    if args.write:
        path.parent.mkdir(parents=True,exist_ok=True)
        path.write_text(json.dumps(state,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    if args.github_output:
        with open(args.github_output,"a",encoding="utf-8") as f:
            f.write(f"changed={'true' if changed else 'false'}\n")
            f.write(f"signature={state['signature']}\n")
    print(f"SIA probe: {'mise à jour requise' if changed else 'aucun changement'} - {state['signature'][:12]}")
    return 0
if __name__=="__main__":
    try: raise SystemExit(main())
    except Exception as exc:
        print(f"ERREUR PROBE SIA: {exc}",file=sys.stderr); raise
