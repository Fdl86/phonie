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
print("Tests générateur SIA OK")
