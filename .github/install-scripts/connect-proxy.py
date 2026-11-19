"""Tiny HTTPS CONNECT proxy for the install-scripts workflow. Logs each CONNECT target to the log file.

usage: python connect-proxy.py <port> <logfile> [user:password]
With user:password, requests without matching Proxy-Authorization get 407.
"""
import base64
import socket
import sys
import threading

port = int(sys.argv[1])
log_path = sys.argv[2]
auth = sys.argv[3] if len(sys.argv) > 3 else None
expected = ("Basic " + base64.b64encode(auth.encode()).decode()) if auth else None
lock = threading.Lock()


def log(line):
    with lock, open(log_path, "a", encoding="utf-8") as f:
        f.write(line + "\n")


def pipe(a, b):
    try:
        while True:
            data = a.recv(65536)
            if not data:
                break
            b.sendall(data)
    except OSError:
        pass
    finally:
        for s in (a, b):
            try:
                s.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass


def handle(client):
    try:
        head = b""
        while b"\r\n\r\n" not in head:
            chunk = client.recv(4096)
            if not chunk:
                return
            head += chunk
        lines = head.split(b"\r\n\r\n", 1)[0].decode("latin-1").split("\r\n")
        method, target, _ = lines[0].split(" ", 2)
        headers = {}
        for h in lines[1:]:
            k, _, v = h.partition(":")
            headers[k.strip().lower()] = v.strip()
        if method != "CONNECT":
            log(f"REFUSED {method} {target}")
            client.sendall(b"HTTP/1.1 405 Method Not Allowed\r\nContent-Length: 0\r\n\r\n")
            return
        if expected and headers.get("proxy-authorization") != expected:
            log(f"407 {target}")
            client.sendall(b'HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm="test"\r\nContent-Length: 0\r\n\r\n')
            return
        host, _, p = target.rpartition(":")
        upstream = socket.create_connection((host, int(p)), timeout=30)
        upstream.settimeout(None)
        log(f"CONNECT {target}")
        client.sendall(b"HTTP/1.1 200 Connection Established\r\n\r\n")
        t = threading.Thread(target=pipe, args=(upstream, client), daemon=True)
        t.start()
        pipe(client, upstream)
        t.join()
    except Exception as e:  # noqa: BLE001 - test helper
        log(f"ERROR {e!r}")
    finally:
        client.close()


server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
server.bind(("127.0.0.1", port))
server.listen(64)
log(f"listening {port} auth={'yes' if auth else 'no'}")
while True:
    c, _ = server.accept()
    threading.Thread(target=handle, args=(c,), daemon=True).start()
