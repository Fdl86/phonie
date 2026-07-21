#!/usr/bin/env python3
"""Build PHONIE's French radio database from official SIA publications.

Preferred input is the official SIA AIXM 4.5 XML export. When a direct XML
archive is not configured, the builder falls back to the official Atlas VAC
catalogue and extracts radio channels from the current PDF publications.
No operational frequency is embedded in this program.
"""
from __future__ import annotations

import argparse
import concurrent.futures
import datetime as dt
import hashlib
import io
import json
import os
import re
import shutil
import sys
import tempfile
import unicodedata
import urllib.parse
import zipfile
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable
import xml.etree.ElementTree as ET

import requests
from bs4 import BeautifulSoup
from pypdf import PdfReader

VERSION = "DEV0.4.1.0"
AIM_CATALOG = "https://www.sia.aviation-civile.gouv.fr/produits-numeriques-en-libre-disposition/les-bases-de-donnees-sia.html"
VAC_SEARCH = "https://www.sia.aviation-civile.gouv.fr/catalogsearch/result/"
SESSION_HEADERS = {"User-Agent": "PHONIE-SIA-Radio-Builder/0.4.1.0 (+https://github.com/Fdl86/phonie)"}
FREQ_RE = re.compile(r"(?<!\d)(1(?:1[89]|2\d|3[0-6]))[\.,](\d{2,3})(?!\d)")
ICAO_RE = re.compile(r"\b(?:LF|TF|FM|NT|NW)[A-Z0-9]{2}\b")
PDF_ICAO_RE = re.compile(r"AD[-_ ]?2[._-](LF[A-Z0-9]{2})\.PDF", re.I)

KIND_NAMES = {
    "Tower": "TOUR", "Ground": "SOL", "Clearance": "PRÉVOL",
    "Approach": "APPROCHE", "Departure": "DÉPART",
    "Information": "INFORMATION", "FlightInformation": "INFORMATION",
    "SelfInformation": "A/A", "AutomaticBroadcast": "ATIS",
    "RecordedMessage": "MESSAGE ENREGISTRÉ", "ControlledOther": "SERVICE",
}

@dataclass(frozen=True)
class Cycle:
    name: str
    start: dt.date
    end: dt.date
    product_url: str = ""

@dataclass(frozen=True)
class PdfDocument:
    icao: str
    url: str


def norm_text(value: str) -> str:
    value = unicodedata.normalize("NFKC", value or "")
    value = value.replace("\u00a0", " ").replace("\xad", "")
    return re.sub(r"[ \t]+", " ", value).strip()


def upper_ascii(value: str) -> str:
    value = unicodedata.normalize("NFKD", value or "")
    value = "".join(c for c in value if not unicodedata.combining(c))
    return value.upper()


def iso(value: dt.datetime | dt.date) -> str:
    if isinstance(value, dt.date) and not isinstance(value, dt.datetime):
        value = dt.datetime.combine(value, dt.time.min, tzinfo=dt.timezone.utc)
    if value.tzinfo is None:
        value = value.replace(tzinfo=dt.timezone.utc)
    return value.astimezone(dt.timezone.utc).isoformat()


def get(session: requests.Session, url: str, *, timeout: int = 45) -> requests.Response:
    response = session.get(url, timeout=timeout, allow_redirects=True)
    response.raise_for_status()
    return response


def parse_date_fr(value: str) -> dt.date:
    months = {
        "janvier":1,"fevrier":2,"février":2,"mars":3,"avril":4,"mai":5,"juin":6,
        "juillet":7,"aout":8,"août":8,"septembre":9,"octobre":10,"novembre":11,"decembre":12,"décembre":12,
    }
    clean = upper_ascii(value).lower()
    match = re.search(r"(\d{1,2})\s+([a-z]+)\s+(20\d{2})", clean)
    if not match or match.group(2) not in months:
        raise ValueError(f"Date SIA non reconnue: {value}")
    return dt.date(int(match.group(3)), months[match.group(2)], int(match.group(1)))


def discover_cycles(session: requests.Session) -> list[Cycle]:
    html = get(session, AIM_CATALOG).text
    soup = BeautifulSoup(html, "html.parser")
    text = norm_text(soup.get_text(" "))
    product_links = {norm_text(a.get_text(" ")): urllib.parse.urljoin(AIM_CATALOG, a.get("href", "")) for a in soup.select("a[href]")}
    pattern = re.compile(
        r"Donn[eé]es a[eé]ronautiques XML AIRAC\s+([0-9]{2}/[0-9]{2})(?:\s*-?CORRIGENDUM)?.{0,500}?"
        r"En vigueur du\s+(\d{1,2}/\d{1,2}/20\d{2})\s+au\s+(\d{1,2}/\d{1,2}/20\d{2})",
        re.I | re.S,
    )
    cycles: list[Cycle] = []
    for match in pattern.finditer(text):
        start = dt.datetime.strptime(match.group(2), "%d/%m/%Y").date()
        end = dt.datetime.strptime(match.group(3), "%d/%m/%Y").date()
        name = match.group(1)
        url = next((u for label,u in product_links.items() if f"AIRAC {name}" in upper_ascii(label)), "")
        cycles.append(Cycle(name, start, end, url))
    if not cycles:
        # Product cards can change markup; the current cycle can be supplied explicitly.
        raise RuntimeError("Impossible de lire les cycles AIRAC sur le catalogue SIA.")
    unique = {(c.name,c.start,c.end):c for c in cycles}
    return sorted(unique.values(), key=lambda c:c.start)


def choose_cycle(cycles: list[Cycle], target: str, today: dt.date) -> Cycle:
    if target and target.lower() not in {"auto","current","next"}:
        for cycle in cycles:
            if cycle.name == target:
                return cycle
        raise RuntimeError(f"Cycle AIRAC demandé absent du catalogue SIA: {target}")
    if target.lower() == "next":
        future = [c for c in cycles if c.start > today]
        if not future:
            raise RuntimeError("Aucun prochain cycle AIRAC publié par le SIA.")
        return future[0]
    current = [c for c in cycles if c.start <= today <= c.end]
    if current:
        return current[-1]
    past = [c for c in cycles if c.start <= today]
    if past:
        return past[-1]
    return cycles[0]


def try_discover_archive_url(session: requests.Session, cycle: Cycle) -> str:
    configured = os.environ.get("SIA_XML_SOURCE_URL", "").strip()
    if configured:
        return configured
    if not cycle.product_url:
        return ""
    html = get(session, cycle.product_url).text
    soup = BeautifulSoup(html, "html.parser")
    candidates: list[str] = []
    for tag in soup.select("a[href], [data-url], [data-download-url]"):
        for attr in ("href","data-url","data-download-url"):
            value = tag.get(attr)
            if value and ".zip" in value.lower():
                candidates.append(urllib.parse.urljoin(cycle.product_url, value))
    for match in re.finditer(r'https?:\\?/\\?/[^"\'<> ]+?\.zip(?:\?[^"\'<> ]*)?', html, re.I):
        candidates.append(match.group(0).replace("\\/", "/"))
    return candidates[0] if candidates else ""


def local_name(tag: str) -> str:
    return tag.rsplit("}",1)[-1]


def child_text(element: ET.Element, name: str) -> str:
    for child in element.iter():
        if local_name(child.tag) == name and child.text:
            return norm_text(child.text)
    return ""


def child_mid(element: ET.Element, name: str) -> str:
    for child in element.iter():
        if local_name(child.tag) == name:
            return child.attrib.get("mid", "")
    return ""


def parse_coord(value: str) -> float:
    raw = upper_ascii(value).replace(" ", "")
    if not raw:
        return float("nan")
    sign = -1 if raw.endswith(("S","W")) else 1
    raw = raw.rstrip("NSEW")
    if re.fullmatch(r"[+-]?\d+(?:\.\d+)?", raw):
        return sign * float(raw)
    match = re.fullmatch(r"(\d{2,3})(\d{2})(\d{2}(?:\.\d+)?)", raw)
    if not match:
        return float("nan")
    return sign * (int(match.group(1)) + int(match.group(2))/60 + float(match.group(3))/3600)


def normalize_service(code: str, callsign: str, remarks: str, hours: str) -> tuple[str,str,bool,str]:
    context = upper_ascii(" ".join([code,callsign,remarks]))
    if any(token in context for token in ("ATIS","AWOS","ATIS/VOLMET","METAR AUTO","STAP")):
        return "AutomaticBroadcast","Local",False,"NotApplicable"
    if any(token in context for token in ("REPONDEUR","MESSAGE ENREGISTRE","RECORDED")):
        return "RecordedMessage","Local",False,"NotApplicable"
    if any(token in context for token in ("A/A","AUTO INFORMATION","AUTO-INFO","CTAF","UNICOM")):
        return "SelfInformation","Local",False,"NotApplicable"
    if any(token in context for token in ("GROUND"," GND"," SOL")) or code.upper() in {"GND","GROUND"}:
        return "Ground","Local",True,"Always"
    if any(token in context for token in ("CLEARANCE","PREVOL","PRE-VOL")) or code.upper() in {"CLD","DEL","CLR"}:
        return "Clearance","Local",True,"Always"
    if any(token in context for token in ("TOWER","TOUR")) or code.upper() in {"TWR","TOWER"}:
        return "Tower","Local",True,"Always"
    if "APPRO" in context or code.upper() == "APP":
        return "Approach","Regional",True,"Always"
    if "DEPART" in context or code.upper() == "DEP":
        return "Departure","Regional",True,"Always"
    if any(token in context for token in ("FIS","SIV","INFO")) and "AFIS" not in context:
        return "FlightInformation","Regional",True,"Always"
    if "AFIS" in context or code.upper() in {"AFIS","INFO"}:
        schedule = "Always" if "H24" in upper_ascii(hours) else "PublishedNotEvaluated"
        return "Information","Local",True,schedule
    return "Unknown","Local",False,"NotApplicable"


def extract_archive_bytes(session: requests.Session, archive_arg: str, archive_url: str) -> bytes | None:
    if archive_arg:
        return Path(archive_arg).read_bytes()
    if archive_url:
        return get(session, archive_url, timeout=120).content
    return None


def find_aixm_xml(archive_bytes: bytes) -> tuple[bytes,str]:
    if archive_bytes.lstrip().startswith(b"<"):
        return archive_bytes, "SIA XML/AIXM"
    with zipfile.ZipFile(io.BytesIO(archive_bytes)) as archive:
        names = [name for name in archive.namelist() if name.lower().endswith(".xml")]
        preferred = sorted(names, key=lambda n:("aixm4.5" not in n.lower(), "aixm" not in n.lower(), len(n)))
        if not preferred:
            raise RuntimeError("Archive SIA sans export XML.")
        return archive.read(preferred[0]), f"SIA XML/AIXM 4.5 - {preferred[0]}"


def parse_aixm(xml_bytes: bytes, cycle: Cycle, source_url: str) -> list[dict]:
    root = ET.fromstring(xml_bytes)
    airports: dict[str,dict] = {}
    airport_by_mid: dict[str,str] = {}
    units: dict[str,dict] = {}
    services: dict[str,dict] = {}
    service_airports: dict[str,set[str]] = {}

    for element in root.iter():
        tag = local_name(element.tag)
        if tag == "Ahp":
            uid = next((c for c in element if local_name(c.tag)=="AhpUid"), None)
            if uid is None: continue
            mid = uid.attrib.get("mid","")
            icao = child_text(element,"codeIcao") or child_text(uid,"codeId")
            icao = upper_ascii(icao)
            if not ICAO_RE.fullmatch(icao): continue
            record = {
                "icao":icao,"name":child_text(element,"txtName") or icao,
                "latitude":parse_coord(child_text(element,"geoLat")),
                "longitude":parse_coord(child_text(element,"geoLong")),
                "sourceReference":f"SIA AIXM 4.5 AIRAC {cycle.name}","sourceUrl":source_url,"frequencies":[]}
            airports[icao]=record
            if mid: airport_by_mid[mid]=icao
        elif tag == "Uni":
            uid = next((c for c in element if local_name(c.tag)=="UniUid"), None)
            if uid is None: continue
            mid=uid.attrib.get("mid","")
            units[mid]={"name":child_text(uid,"txtName") or child_text(element,"txtName"),
                        "airport_mid":child_mid(element,"AhpUid"),"type":child_text(element,"codeType")}
        elif tag == "Ser":
            uid = next((c for c in element if local_name(c.tag)=="SerUid"), None)
            if uid is None: continue
            mid=uid.attrib.get("mid","")
            services[mid]={"unit_mid":child_mid(uid,"UniUid"),"code":child_text(uid,"codeType"),
                           "hours":child_text(element,"txtRmkWorkHr") or child_text(element,"codeWorkHr"),
                           "remarks":child_text(element,"txtRmk")}
        elif tag == "Sah":
            uid = next((c for c in element if local_name(c.tag)=="SahUid"), None)
            if uid is None: continue
            am=child_mid(uid,"AhpUid"); sm=child_mid(uid,"SerUid")
            if am and sm: service_airports.setdefault(sm,set()).add(am)

    for element in root.iter():
        if local_name(element.tag)!="Fqy": continue
        uid=next((c for c in element if local_name(c.tag)=="FqyUid"),None)
        if uid is None: continue
        service_mid=child_mid(uid,"SerUid")
        service=services.get(service_mid,{})
        unit=units.get(service.get("unit_mid",""),{})
        airport_mids=set(service_airports.get(service_mid,set()))
        if unit.get("airport_mid"): airport_mids.add(unit["airport_mid"])
        value=child_text(uid,"valFreqTrans") or child_text(element,"valFreqTrans")
        uom=upper_ascii(child_text(element,"uomFreq"))
        try: mhz=float(value.replace(",","."))
        except Exception: continue
        if uom in {"KHZ","K HZ"}: mhz/=1000
        if not 117.975 <= mhz <= 137.0: continue
        channel_khz=round(mhz*1000)
        callsigns=[norm_text(c.text or "") for c in element.iter() if local_name(c.tag)=="txtCallSign" and norm_text(c.text or "")]
        callsign=next((c for c in callsigns if True), unit.get("name", ""))
        code=service.get("code","")
        hours=child_text(element,"txtRmkWorkHr") or child_text(element,"codeWorkHr") or service.get("hours","")
        remarks=" ".join(filter(None,[service.get("remarks",""),child_text(element,"txtRmk")]))
        kind,scope,interactive,schedule=normalize_service(code,callsign,remarks,hours)
        if kind=="Unknown": continue
        for airport_mid in airport_mids:
            icao=airport_by_mid.get(airport_mid,"")
            airport=airports.get(icao)
            if airport is None: continue
            airport["frequencies"].append(make_frequency(channel_khz,code,callsign or f"{airport['name']} {KIND_NAMES[kind]}",kind,scope,interactive,schedule,hours,remarks,source_url,f"Fqy:{uid.attrib.get('mid','')}",1.0))
    return finalize_airports(airports.values())


def cycle_path_tokens(cycle: Cycle) -> tuple[str, str]:
    month = cycle.start.strftime("%b").upper()
    dvd = f"eAIP_{cycle.start.day:02d}_{month}_{cycle.start.year}"
    airac = f"AIRAC-{cycle.start:%Y-%m-%d}"
    return dvd, airac


def discover_vac_documents(session: requests.Session, cycle: Cycle, max_pages: int) -> list[PdfDocument]:
    """Discover the official VAC set from the SIA eAIP AD 0.6 index.

    The catalogue search is kept only as a secondary discovery path because its
    HTML and pagination change more often than the stable eAIP publication tree.
    """
    dvd, airac = cycle_path_tokens(cycle)
    root = f"https://www.sia.aviation-civile.gouv.fr/media/dvd/{dvd}"
    index_url = f"{root}/FRANCE/{airac}/pdf/FR-AD-0.6-fr-FR.pdf"
    found: dict[str, PdfDocument] = {}
    try:
        index_pdf = get(session, index_url, timeout=90).content
        if index_pdf.startswith(b"%PDF"):
            index_text = extract_pdf_text(index_pdf, max_pages=200)
            for icao in sorted(set(ICAO_RE.findall(upper_ascii(index_text)))):
                pdf_url = f"{root}/Atlas-VAC/PDF_AIPparSSection/VAC/AD/AD-2.{icao}.pdf"
                found[icao] = PdfDocument(icao, pdf_url)
    except Exception as exc:
        print(f"Index officiel AD 0.6 indisponible: {exc}", file=sys.stderr)

    if len(found) >= 100:
        return sorted(found.values(), key=lambda d: d.icao)

    empty = 0
    for page in range(1, max_pages + 1):
        params = {"c": "8", "format": "pdf", "q": "AD-2.LF", "limit": "50", "p": str(page)}
        url = VAC_SEARCH + "?" + urllib.parse.urlencode(params)
        html = get(session, url).text
        soup = BeautifulSoup(html, "html.parser")
        before = len(found)
        for anchor in soup.select("a[href]"):
            label = norm_text(anchor.get_text(" "))
            href = urllib.parse.urljoin(url, anchor.get("href", ""))
            combined = f"{label} {href}"
            match = PDF_ICAO_RE.search(combined)
            if not match:
                continue
            icao = match.group(1).upper()
            found[icao] = PdfDocument(icao, href)
        empty = empty + 1 if len(found) == before else 0
        if empty >= 2:
            break
    if len(found) < 100:
        raise RuntimeError(f"Catalogue VAC incomplet: seulement {len(found)} documents AD-2 détectés.")
    return sorted(found.values(), key=lambda d: d.icao)


def extract_pdf_text(content: bytes, max_pages: int = 5) -> str:
    reader=PdfReader(io.BytesIO(content),strict=False)
    pages=[]
    for page in reader.pages[:max_pages]:
        try: pages.append(page.extract_text() or "")
        except Exception: continue
    return "\n".join(pages)


def infer_airport_name(text: str, icao: str) -> str:
    lines=[norm_text(line) for line in text.splitlines() if norm_text(line)]
    rejects=("AIP FRANCE","© SIA","SERVICE DE L'INFORMATION","ALT / HGT","AD 2 ","VAC")
    for line in lines[:120]:
        up=upper_ascii(line)
        if any(token in up for token in rejects): continue
        if 3<=len(line)<=60 and sum(c.isalpha() for c in line)>=3 and up==upper_ascii(line):
            if not FREQ_RE.search(line) and not re.search(r"\d{2}\s+[A-Z]{3}\s+20\d{2}",up):
                return line.title()
    return icao


def classify_context(context: str) -> tuple[str,str,bool,str,str] | None:
    up=upper_ascii(context)
    if any(token in up for token in ("ILS","VOR","DME","NDB","TACAN","LOCALIZER","GLIDE","RADIAL")):
        return None
    if any(token in up for token in ("TELEPHONE"," TEL "," FAX ","CARBURANT","FUEL","HANDLING","AVIAVIP","ACB ")):
        return None
    code=""
    for candidate in ("ATIS","AWOS","AFIS","A/A","CTAF","UNICOM","TWR","TOUR","GND","SOL","APP","APPROCHE","DEP","DEPART","FIS","SIV","INFO","INFORMATION","PREVOL","CLEARANCE","OPS","RADIO"):
        if candidate in up:
            code=candidate; break
    kind,scope,interactive,schedule=normalize_service(code,context,context,context)
    if kind=="Unknown": return None
    if kind=="Information" and "H24" not in up:
        schedule="PublishedNotEvaluated"
    return kind,scope,interactive,schedule,code


def make_frequency(channel_khz:int, code:str, callsign:str, kind:str, scope:str, interactive:bool, schedule:str, hours:str, remarks:str, url:str, record_id:str, confidence:float) -> dict:
    channel=f"{channel_khz/1000:.3f}"
    return {"channel":channel,"channelKhz":channel_khz,"carrierHz":carrier_hz(channel_khz),
            "serviceCode":code or KIND_NAMES[kind],"callsign":norm_text(callsign),"kind":kind,"scope":scope,
            "interactive":interactive,"scheduleState":schedule,"hoursText":norm_text(hours),"remarks":norm_text(remarks),
            "sourceReference":"SIA Atlas VAC","sourceUrl":url,"sourceRecordId":record_id,"confidence":confidence}


def carrier_hz(channel_khz:int) -> int:
    mhz=channel_khz//1000; within=channel_khz%1000; hundred=(within//100)*100; rem=within-hundred
    offsets={0:0,5:0,10:8333,15:16667,25:25000,30:25000,35:33333,40:41667,50:50000,55:50000,60:58333,65:66667,75:75000,80:75000,85:83333,90:91667}
    return mhz*1_000_000+hundred*1000+offsets.get(rem,rem*1000)


def parse_vac_document(session: requests.Session, doc: PdfDocument) -> dict:
    response=get(session,doc.url,timeout=90)
    content=response.content
    if not content.startswith(b"%PDF"):
        soup=BeautifulSoup(response.text,"html.parser")
        link=next((urllib.parse.urljoin(response.url,a.get("href","")) for a in soup.select("a[href]") if ".pdf" in a.get("href","").lower()),"")
        if not link: raise RuntimeError(f"Le document {doc.icao} n'est pas un PDF.")
        response=get(session,link,timeout=90); content=response.content
    text=extract_pdf_text(content)
    lines=[norm_text(line) for line in text.splitlines() if norm_text(line)]
    frequencies=[]
    for index,line in enumerate(lines):
        for match in FREQ_RE.finditer(line):
            digits=match.group(2)
            channel_khz=int(match.group(1))*1000+int(digits.ljust(3,"0"))
            context=" | ".join(lines[max(0,index-2):min(len(lines),index+3)])
            classified=classify_context(context)
            if classified is None: continue
            kind,scope,interactive,schedule,code=classified
            callsign=re.sub(FREQ_RE," ",line)
            callsign=re.sub(r"\bMHZ\b"," ",callsign,flags=re.I)
            callsign=norm_text(callsign.strip(" :-/"))
            if len(callsign)<2 or len(callsign)>90:
                callsign=f"{doc.icao} {KIND_NAMES[kind]}"
            frequencies.append(make_frequency(channel_khz,code,callsign,kind,scope,interactive,schedule,context,"",response.url,f"PDF:{doc.icao}:{index}:{match.start()}",0.82))
    # A/A lines are sometimes split by PDF extraction. If an A/A marker is near one sole channel, keep it.
    if not any(f["kind"]=="SelfInformation" for f in frequencies) and "A/A" in upper_ascii(text):
        all_channels=[]
        for match in FREQ_RE.finditer(text):
            all_channels.append(int(match.group(1))*1000+int(match.group(2).ljust(3,"0")))
        unique=sorted(set(all_channels))
        if len(unique)==1:
            frequencies.append(make_frequency(unique[0],"A/A",f"{infer_airport_name(text,doc.icao)} A/A","SelfInformation","Local",False,"NotApplicable","","",response.url,f"PDF:{doc.icao}:AA",0.72))
    return {"icao":doc.icao,"name":infer_airport_name(text,doc.icao),"latitude":float("nan"),"longitude":float("nan"),
            "sourceReference":"SIA Atlas VAC","sourceUrl":response.url,"frequencies":dedupe_frequencies(frequencies)}


def dedupe_frequencies(records: Iterable[dict]) -> list[dict]:
    best={}
    for record in records:
        key=(record["channelKhz"],record["kind"],record["scope"],upper_ascii(record["callsign"]))
        if key not in best or record["confidence"]>best[key]["confidence"]: best[key]=record
    return sorted(best.values(),key=lambda r:(r["channelKhz"],r["kind"],r["callsign"]))


def finalize_airports(records: Iterable[dict]) -> list[dict]:
    result=[]
    for airport in records:
        airport["frequencies"]=dedupe_frequencies(airport.get("frequencies",[]))
        if isinstance(airport.get("latitude"),float) and airport["latitude"]!=airport["latitude"]: airport["latitude"]=None
        if isinstance(airport.get("longitude"),float) and airport["longitude"]!=airport["longitude"]: airport["longitude"]=None
        result.append(airport)
    return sorted(result,key=lambda a:a["icao"])


def build_from_vac(session: requests.Session, cycle: Cycle, workers: int, max_pages: int) -> list[dict]:
    documents=discover_vac_documents(session,cycle,max_pages)
    print(f"SIA VAC: {len(documents)} documents détectés.")
    airports=[]; errors=[]
    def task(doc:PdfDocument):
        local=requests.Session(); local.headers.update(SESSION_HEADERS)
        return parse_vac_document(local,doc)
    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as executor:
        future_map={executor.submit(task,doc):doc for doc in documents}
        for number,future in enumerate(concurrent.futures.as_completed(future_map),1):
            doc=future_map[future]
            try: airports.append(future.result())
            except Exception as exc: errors.append(f"{doc.icao}: {exc}")
            if number%25==0: print(f"SIA VAC: {number}/{len(documents)} traités.")
    if errors:
        print("Documents VAC en échec:",*errors[:20],sep="\n- ",file=sys.stderr)
    return finalize_airports(airports)


def descriptor(path:Path,dataset:dict,relative:str)->dict:
    data=path.read_bytes()
    freqs=sum(len(a["frequencies"]) for a in dataset["airports"])
    interactive=sum(1 for a in dataset["airports"] for f in a["frequencies"] if f["interactive"])
    return {"relativePath":relative.replace("\\","/"),"sha256":hashlib.sha256(data).hexdigest(),"revision":dataset["revision"],
            "airacCycle":dataset["airacCycle"],"effectiveFrom":dataset["effectiveFrom"],"effectiveUntil":dataset["effectiveUntil"],
            "airportCount":len(dataset["airports"]),"frequencyCount":freqs,"interactiveCount":interactive,"silentCount":freqs-interactive}


def stable_json(value:dict)->bytes:
    return (json.dumps(value,ensure_ascii=False,sort_keys=True,separators=(",",":"),allow_nan=False)+"\n").encode("utf-8")


def write_database(output:Path,cycle:Cycle,airports:list[dict],source_kind:str,source_url:str,slot:str,min_airports:int,min_frequencies:int)->None:
    frequency_count=sum(len(a["frequencies"]) for a in airports)
    if len(airports)<min_airports or frequency_count<min_frequencies:
        raise RuntimeError(f"Couverture nationale insuffisante: {len(airports)} aérodromes, {frequency_count} fréquences (minimum {min_airports}/{min_frequencies}).")
    generated=dt.datetime.now(dt.timezone.utc)
    dataset={"schemaVersion":2,"datasetId":"phonie-france-radio-sia","revision":"","authority":"SIA","sourceKind":source_kind,
             "airacCycle":cycle.name,"effectiveFrom":iso(cycle.start),"effectiveUntil":iso(cycle.end+dt.timedelta(days=1)),
             "generatedAt":iso(generated),"generatorVersion":VERSION,"sourceUrl":source_url,"airports":airports}
    revision_payload=dict(dataset)
    revision_payload["generatedAt"]=""
    digest=hashlib.sha256(stable_json(revision_payload)).hexdigest()
    dataset["revision"]=digest
    current_manifest={}
    manifest_path=output/"manifest.json"
    if manifest_path.exists():
        try: current_manifest=json.loads(manifest_path.read_text(encoding="utf-8"))
        except Exception: current_manifest={}
    previous=current_manifest.get("previous")
    current=current_manifest.get("current")
    next_desc=current_manifest.get("next")
    if slot=="current" and current and current.get("revision")!=digest:
        old_relative=current.get("relativePath","")
        old_path=output/old_relative
        if old_relative and old_path.exists():
            previous_target=output/"previous"/"airports-fr.json"
            previous_target.parent.mkdir(parents=True,exist_ok=True)
            shutil.copyfile(old_path,previous_target)
            old_dataset=json.loads(previous_target.read_text(encoding="utf-8"))
            previous=descriptor(previous_target,old_dataset,"previous/airports-fr.json")
    target=output/slot/"airports-fr.json"; target.parent.mkdir(parents=True,exist_ok=True)
    target.write_bytes(json.dumps(dataset,ensure_ascii=False,indent=2,allow_nan=False).encode("utf-8")+b"\n")
    new_descriptor=descriptor(target,dataset,f"{slot}/airports-fr.json")
    if slot=="current":
        current=new_descriptor
    elif slot=="next": next_desc=new_descriptor
    elif slot=="previous": previous=new_descriptor
    manifest={"schemaVersion":2,"datasetId":"phonie-france-radio-sia","datasetRevision":current.get("revision","") if current else new_descriptor["revision"],
              "authority":"SIA","generatedAt":iso(generated),"generatorVersion":VERSION,"sourceCatalogUrl":AIM_CATALOG,
              "bootstrapRequired":False,"previous":previous,"current":current,"next":next_desc}
    manifest_path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    report={"generatorVersion":VERSION,"cycle":cycle.name,"sourceKind":source_kind,"airportCount":len(airports),"frequencyCount":frequency_count,
            "interactiveCount":sum(1 for a in airports for f in a["frequencies"] if f["interactive"]),
            "silentCount":sum(1 for a in airports for f in a["frequencies"] if not f["interactive"]),
            "airportsWithoutFrequency":[a["icao"] for a in airports if not a["frequencies"]],"revision":digest}
    (output/"generation-report.json").write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print(f"Base SIA {cycle.name}: {len(airports)} aérodromes, {frequency_count} fréquences, révision {digest[:12]}.")


def main()->int:
    parser=argparse.ArgumentParser()
    parser.add_argument("--output",default="data/radio/france")
    parser.add_argument("--source-archive",default="")
    parser.add_argument("--source-url",default="")
    parser.add_argument("--cycle",default="current")
    parser.add_argument("--slot",choices=("current","next","previous"),default="current")
    parser.add_argument("--today",default="")
    parser.add_argument("--workers",type=int,default=8)
    parser.add_argument("--max-pages",type=int,default=30)
    parser.add_argument("--min-airports",type=int,default=200)
    parser.add_argument("--min-frequencies",type=int,default=150)
    args=parser.parse_args()
    today=dt.date.fromisoformat(args.today) if args.today else dt.datetime.now(dt.timezone.utc).date()
    session=requests.Session(); session.headers.update(SESSION_HEADERS)
    cycles=discover_cycles(session); cycle=choose_cycle(cycles,args.cycle,today)
    archive_url=args.source_url or try_discover_archive_url(session,cycle)
    archive_bytes=extract_archive_bytes(session,args.source_archive,archive_url)
    if archive_bytes:
        xml_bytes,source_kind=find_aixm_xml(archive_bytes)
        airports=parse_aixm(xml_bytes,cycle,archive_url or str(Path(args.source_archive).resolve()))
        source_url=archive_url or AIM_CATALOG
    else:
        source_kind="SIA Atlas VAC officiel (secours lorsque l'export XML direct n'est pas accessible)"
        airports=build_from_vac(session,cycle,args.workers,args.max_pages)
        source_url=VAC_SEARCH
    write_database(Path(args.output),cycle,airports,source_kind,source_url,args.slot,args.min_airports,args.min_frequencies)
    return 0

if __name__=="__main__":
    raise SystemExit(main())
