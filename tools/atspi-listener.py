#!/usr/bin/env python3
"""Log the AT-SPI events Chromium emits for the terminal's live regions — what Orca actually receives.

Usage:  python3 tools/atspi-listener.py /tmp/atspi-events.jsonl &
        (drive the app in a Chromium launched WITHOUT NO_AT_BRIDGE and with
         --force-renderer-accessibility; then kill the listener and read the file)

One JSON object per line: wall-clock time, event type, the element's html id, its role,
container-live / container-atomic, the event's detail1/detail2 and any_data (the inserted or
deleted text), and the element's current text. Filters to applications whose name contains
"hrom". Written 2026-09-04 to measure the order in which the status bar's polite live region
and the speech regions' assertive live regions reach the bus (docs/TODO.md, ninth pass, A0).
"""
import gi, time, sys, json
gi.require_version('Atspi', '2.0')
from gi.repository import Atspi

out = open(sys.argv[1], 'a', buffering=1)

def cb(e):
    try:
        src = e.source
        app = src.get_application()
        appname = app.get_name() if app else '?'
        if 'hrom' not in appname:
            return
        attrs = src.get_attributes() or {}
        rec = {'t': round(time.time(), 4), 'type': e.type, 'id': attrs.get('id', ''),
               'role': src.get_role_name(), 'live': attrs.get('container-live', ''),
               'atomic': attrs.get('container-atomic', ''), 'd1': e.detail1, 'd2': e.detail2,
               'data': str(e.any_data)[:80]}
        try:
            rec['text'] = Atspi.Text.get_text(src, 0, 120) if src.get_text_iface() else ''
        except Exception:
            rec['text'] = '(no text)'
        out.write(json.dumps(rec) + '\n')
    except Exception as ex:
        out.write(json.dumps({'err': str(ex)}) + '\n')

for t in ('object:text-changed', 'object:children-changed'):
    Atspi.EventListener.new(cb).register(t)
out.write(json.dumps({'t': round(time.time(), 4), 'listener': 'started'}) + '\n')
Atspi.event_main()
