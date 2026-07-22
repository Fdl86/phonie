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

# Regression DEV0.4.1.2: deterministic direct-DVD fallback.
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

print("Tests fallback DVD DEV0.4.1.2 OK")
