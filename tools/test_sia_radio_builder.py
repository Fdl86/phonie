#!/usr/bin/env python3
"""Offline regression checks for the AIXM parser and channel normalisation."""
from __future__ import annotations
import datetime as dt
import importlib.util
import sys
import tempfile
import json
from pathlib import Path

HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location("builder",HERE/"sia_radio_builder.py")
b=importlib.util.module_from_spec(spec); assert spec.loader; sys.modules[spec.name]=b; spec.loader.exec_module(b)
xml=b'''<?xml version="1.0" encoding="UTF-8"?>
<AIXM-Snapshot effective="2026-07-09T00:00:00Z" version="4.5">
 <Ahp><AhpUid mid="1"><codeId>LFZZ</codeId></AhpUid><txtName>TERRAIN TEST</txtName><codeIcao>LFZZ</codeIcao><geoLat>470000N</geoLat><geoLong>0010000W</geoLong></Ahp>
 <Uni><UniUid mid="10"><txtName>TEST TOUR</txtName></UniUid><AhpUid mid="1"><codeId>LFZZ</codeId></AhpUid><codeType>TWR</codeType></Uni>
 <Ser><SerUid mid="20"><UniUid mid="10"><txtName>TEST TOUR</txtName></UniUid><codeType>TWR</codeType><noSeq>1</noSeq></SerUid></Ser>
 <Sah><SahUid mid="30"><AhpUid mid="1"><codeId>LFZZ</codeId></AhpUid><SerUid mid="20"><UniUid mid="10"><txtName>TEST TOUR</txtName></UniUid><codeType>TWR</codeType><noSeq>1</noSeq></SerUid></SahUid></Sah>
 <Fqy><FqyUid mid="40"><SerUid mid="20"><UniUid mid="10"><txtName>TEST TOUR</txtName></UniUid><codeType>TWR</codeType><noSeq>1</noSeq></SerUid><valFreqTrans>118.505</valFreqTrans></FqyUid><uomFreq>MHZ</uomFreq><Ftt><codeWorkHr>H24</codeWorkHr></Ftt><Cdl><txtCallSign>TEST TOUR</txtCallSign><codeLang>FR</codeLang></Cdl></Fqy>
</AIXM-Snapshot>'''
cycle=b.Cycle("07/26",dt.date(2026,7,9),dt.date(2026,8,5),"")
airports=b.parse_aixm(xml,cycle,"fixture://aixm")
assert len(airports)==1 and airports[0]["icao"]=="LFZZ"
assert len(airports[0]["frequencies"])==1
f=airports[0]["frequencies"][0]
assert f["channelKhz"]==118505 and f["kind"]=="Tower" and f["interactive"]
assert b.carrier_hz(118505)==118500000
with tempfile.TemporaryDirectory() as temporary:
    root=Path(temporary)
    b.write_database(root,cycle,airports,"fixture","fixture://aixm","current",1,1)
    first=json.loads((root/"manifest.json").read_text(encoding="utf-8"))
    b.write_database(root,cycle,airports,"fixture","fixture://aixm","current",1,1)
    second=json.loads((root/"manifest.json").read_text(encoding="utf-8"))
    assert first["current"]["revision"]==second["current"]["revision"]
    changed=json.loads(json.dumps(airports))
    changed[0]["frequencies"][0]["callsign"]="TEST TOUR MODIFIEE"
    b.write_database(root,cycle,changed,"fixture","fixture://aixm","current",1,1)
    third=json.loads((root/"manifest.json").read_text(encoding="utf-8"))
    assert third["previous"] and third["previous"]["relativePath"]=="previous/airports-fr.json"
    assert (root/"previous"/"airports-fr.json").exists()

# Regression: current SIA catalogue uses moving download links and `page`
# pagination, while the builder must pin documents to the selected AIRAC DVD.
class FakeResponse:
    def __init__(self, text: str, url: str):
        self.text = text
        self.url = url
        self.content = text.encode("utf-8")

    def raise_for_status(self) -> None:
        return None


class FakeSession:
    def __init__(self) -> None:
        self.urls: list[str] = []

    def get(self, url: str, **_: object) -> FakeResponse:
        self.urls.append(url)
        if "page=2" in url:
            html = '<a href="/documents/download/2/">AIP - AD-2.LFAB.pdf</a>'
        elif "page=" in url:
            html = "<html><body>Aucun autre résultat</body></html>"
        else:
            html = (
                '<a href="/documents/download/1/">AIP - AD-2.LFAA.pdf</a>'
                '<a href="/catalogsearch/result/?c=8&amp;format=pdf&amp;q=AD-2&amp;page=2">Page Suivant</a>'
            )
        return FakeResponse(html, url)


assert b.cycle_path_tokens(cycle)[0] == "eAIP_09_JUL_2026"
fake = FakeSession()
documents = b.discover_vac_catalog_query(fake, cycle, "AD-2", 4)
assert sorted(documents) == ["LFAA", "LFAB"]
assert documents["LFAA"].url.endswith("/eAIP_09_JUL_2026/Atlas-VAC/PDF_AIPparSSection/VAC/AD/AD-2.LFAA.pdf")
assert any("page=2" in url for url in fake.urls)

print("Tests générateur SIA OK")

# Regression DEV0.4.1.4: deterministic direct-DVD fallback retained.
groups = b.direct_vac_candidates()
assert len(groups) == 26 and all(len(group) == 26 for group in groups)
assert groups[0][0] == "LFAA" and groups[-1][-1] == "LFZZ"

class FakeProbeResponse:
    def __init__(self, status_code: int, content: bytes):
        self.status_code = status_code
        self._content = content

    def raise_for_status(self) -> None:
        if self.status_code >= 400 and self.status_code not in {404, 410}:
            raise RuntimeError(f"HTTP {self.status_code}")

    def iter_content(self, chunk_size: int = 16):
        yield self._content[:chunk_size]

    def close(self) -> None:
        return None

class FakeProbeSession:
    def get(self, url: str, **_: object) -> FakeProbeResponse:
        if url.endswith("AD-2.LFAA.pdf"):
            return FakeProbeResponse(206, b"%PDF-1.7 fixture")
        return FakeProbeResponse(404, b"not found")

original_create_session = b.create_session
b.create_session = lambda: FakeProbeSession()
try:
    direct, probe_errors = b.probe_vac_pdf_group(cycle, ["LFAA", "LFAB"])
    assert sorted(direct) == ["LFAA"] and not probe_errors
finally:
    b.create_session = original_create_session

original_catalog = b.discover_vac_catalog_query
original_direct = b.discover_vac_documents_direct
b.discover_vac_catalog_query = lambda *_args, **_kwargs: {}
b.discover_vac_documents_direct = lambda *_args, **_kwargs: {
    f"LF{chr(65 + (index // 26))}{chr(65 + (index % 26))}": b.PdfDocument(
        f"LF{chr(65 + (index // 26))}{chr(65 + (index % 26))}",
        f"fixture://{index}.pdf",
    )
    for index in range(201)
}
try:
    fallback_docs = b.discover_vac_documents(fake, cycle, 4, 8)
    assert len(fallback_docs) == 201
finally:
    b.discover_vac_catalog_query = original_catalog
    b.discover_vac_documents_direct = original_direct

print("Tests fallback DVD DEV0.4.1.4 OK")

# Regression DEV0.4.1.4: the Atlas VAC catalogue can be complete while chart
# PDF text layers still lose most radio rows. The authoritative eAIP AD 2.18
# HTML/PDF table must therefore be the primary parser.
assert b.aip_airport_html_url(cycle, "LFBI").endswith(
    "/eAIP_09_JUL_2026/FRANCE/AIRAC-2026-07-09/html/eAIP/FR-AD-2.LFBI-fr-FR.html"
)
assert b.aip_airport_pdf_url(cycle, "LFOU").endswith(
    "/eAIP_09_JUL_2026/FRANCE/AIRAC-2026-07-09/pdf/FR-AD-2.LFOU-fr-FR.pdf"
)

lfbi_html = """
<html><body>
<h3>LFBI — POITIERS BIARD</h3>
<h4>AD 2 LFBI.AD 2.18 Moyens de radiocommunication ATS ATS radiocommunication facilities</h4>
<table>
<tr><th>Service</th><th>Indicatif d'appel</th><th>FREQ</th><th>HOR</th><th>Observations</th></tr>
<tr><td>FIS</td><td>POITIERS Information (FR)<br/>POITIERS Information (EN)</td><td>124.000 MHz</td><td>HO</td><td>Secteur BI 1</td></tr>
<tr><td>APP</td><td>POITIERS Approche (FR)<br/>POITIERS Approach (EN)</td><td>134.100 MHz</td><td>HO</td><td></td></tr>
<tr><td>TWR</td><td>POITIERS Tour (FR)<br/>POITIERS Tower (EN)</td><td>118.505 MHz</td><td>HO</td><td></td></tr>
<tr><td>ATIS</td><td>POITIERS (FR)<br/>POITIERS (EN)</td><td>121.780 MHz</td><td>HO</td><td></td></tr>
<tr><td>A/A</td><td>POITIERS (FR)</td><td>118.505 MHz</td><td>HX</td><td>Absence ATS</td></tr>
</table>
<h4>AD 2 LFBI.AD 2.19 Moyens radio de navigation et d'atterrissage</h4>
<table><tr><td>VOR-DME</td><td>113.300 MHz</td></tr></table>
</body></html>
"""
name, lfbi_rows = b.parse_aip_radio_html(lfbi_html, "LFBI", "fixture://lfbi-html")
assert name == "POITIERS BIARD"
assert len(lfbi_rows) == 5
assert {(row["channelKhz"], row["kind"]) for row in lfbi_rows} == {
    (118505, "SelfInformation"),
    (118505, "Tower"),
    (121780, "AutomaticBroadcast"),
    (124000, "FlightInformation"),
    (134100, "Approach"),
}
assert next(row for row in lfbi_rows if row["kind"] == "Tower")["callsign"] == "POITIERS Tour"
assert not next(row for row in lfbi_rows if row["kind"] == "AutomaticBroadcast")["interactive"]
assert not next(row for row in lfbi_rows if row["kind"] == "SelfInformation")["interactive"]

lfou_pdf_text = """
Indicateur d'emplacement - nom de l'aérodrome Aerodrome location indicator - nameAD 2 LFOU.1
LFOU - CHOLET LE PONTREAU
AD 2 LFOU.18
Observations Remarks HOR FREQ Indicatif d'appel (langue) Call-sign (language) Service
Exploitant: Cholet Agglomération. HO120.405 MHzCHOLET Information (FR)
CHOLET Information (EN)AFIS
Absence AFIS. HX120.405 MHzCHOLET (FR)A/A
AD 2 LFOU.19
Moyens radio de navigation et d'atterrissage
VOR 116.200 MHz
"""
lfou_rows = b.parse_aip_radio_table(lfou_pdf_text, "LFOU", "fixture://lfou-pdf")
assert b.extract_aip_airport_name(lfou_pdf_text, "LFOU") == "CHOLET LE PONTREAU"
assert len(lfou_rows) == 2
assert {(row["channelKhz"], row["kind"]) for row in lfou_rows} == {
    (120405, "Information"),
    (120405, "SelfInformation"),
}
assert all(row["channelKhz"] != 116200 for row in lfou_rows)

print("Tests eAIP AD 2.18 DEV0.4.1.4 OK")

# Explicit Service column must win over words contained in free-text remarks.
lfpb_pdf_text = """
LFPB - PARIS LE BOURGET
AD 2 LFPB.18
HO123.835 MHzLE BOURGET Information (FR)
LE BOURGET Information (EN)FIS
FREQ pour service d'approche uniquement sur AD Pontoise.
HO118.805 MHzLE BOURGET Approche (FR)
LE BOURGET Approach (EN)APP
HO121.955 MHzLE BOURGET Prevol (FR)
LE BOURGET Delivery (EN)TWR
HO121.905 MHzLE BOURGET Sol (FR)
LE BOURGET Ground (EN)TWR
HO120.005 MHzLE BOURGET (FR)
LE BOURGET (EN)ATIS
H24NILLE BOURGET (FR)
LE BOURGET (EN)D-ATIS
AD 2 LFPB.19
"""
lfpb_rows = b.parse_aip_radio_table(lfpb_pdf_text, "LFPB", "fixture://lfpb-pdf")
by_channel = {row["channelKhz"]: row for row in lfpb_rows}
assert by_channel[123835]["kind"] == "FlightInformation"
assert by_channel[118805]["kind"] == "Approach"
assert by_channel[121955]["kind"] == "Clearance"
assert by_channel[121905]["kind"] == "Ground"
assert by_channel[120005]["serviceCode"] == "ATIS"

print("Tests priorité colonne Service DEV0.4.1.4 OK")

class FakeOfficialResponse:
    def __init__(self, text: str, url: str, content_type: str = "text/html; charset=utf-8"):
        self.text = text
        self.url = url
        self.content = text.encode("utf-8")
        self.headers = {"Content-Type": content_type}

    def raise_for_status(self) -> None:
        return None


class FakeOfficialSession:
    def get(self, url: str, **_: object) -> FakeOfficialResponse:
        return FakeOfficialResponse(lfbi_html, url)


lfbi_official = b.parse_official_airport_document(
    FakeOfficialSession(),
    cycle,
    b.PdfDocument("LFBI", "fixture://atlas-lfbi.pdf"),
)
assert lfbi_official["sourceReference"] == "SIA eAIP HTML AD 2"
assert lfbi_official["name"] == "POITIERS BIARD"
assert len(lfbi_official["frequencies"]) == 5

print("Tests sélection source eAIP HTML DEV0.4.1.4 OK")
