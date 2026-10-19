#!/usr/bin/env python3
"""Send admin commands to the local Meridian 59 server (port 9998, localhost only).
Usage: ./admin.py "create account admin joe pw" "create admin 1" "save game"
"""
import socket, sys, time
s = socket.create_connection(('127.0.0.1', 9998), timeout=5)
def rd(t=1.0):
    s.settimeout(t); out = b''
    try:
        while True:
            d = s.recv(4096)
            if not d: break
            out += d
    except Exception: pass
    return out.decode('latin-1')
rd(0.3)
for cmd in sys.argv[1:]:
    s.sendall((cmd + '\r').encode()); time.sleep(0.6); print(rd().strip())
s.sendall(b'quit\r')
