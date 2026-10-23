"""固定 Java 子工具通过管道读取内存字节；不接受业务控制的 classpath、路径或 JVM 选项。"""
import json
import base64
import os
import selectors
import subprocess
import time
from protocol import Document, Rejected, MAX_WIRE, policy


def parse_office(source, kind, include_hidden=False):
    if kind not in ("doc", "xls", "xlsx", "ppt"):
        raise Rejected("unsupported_format")
    document = Document(source, {"doc": 7, "xls": 6, "xlsx": 6, "ppt": 8}[kind])
    document.policy = policy(kind, hidden=include_hidden if kind in ("xls", "xlsx", "ppt") else False)
    process = subprocess.Popen(["/usr/bin/java", "-Xmx256m", "-XX:ActiveProcessorCount=2",
                                "-Djava.awt.headless=true", "-Dlog4j2.configurationFile=/app/log4j2.xml",
                                "-cp", "/opt/java-libs/*:/app/classes", "OfficeExtract", kind,
                                "include" if include_hidden else "exclude"],
                               stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env={})
    output = bytearray(); errors = bytearray(); offset = 0; deadline = time.monotonic() + 20
    try:
        with selectors.DefaultSelector() as selector:
            for stream, events, label in ((process.stdin, selectors.EVENT_WRITE, "in"),
                                           (process.stdout, selectors.EVENT_READ, "out"),
                                           (process.stderr, selectors.EVENT_READ, "err")):
                os.set_blocking(stream.fileno(), False)
                selector.register(stream, events, label)
            while selector.get_map():
                if time.monotonic() >= deadline:
                    raise Rejected("office_timeout")
                for key, _ in selector.select(min(0.1, deadline - time.monotonic())):
                    if key.data == "in":
                        try:
                            offset += os.write(key.fd, memoryview(source)[offset:offset + 32768])
                        except BrokenPipeError:
                            raise Rejected("office_rejected")
                        if offset == len(source):
                            selector.unregister(key.fileobj); key.fileobj.close()
                    else:
                        raw = os.read(key.fd, 32768)
                        if not raw:
                            selector.unregister(key.fileobj); key.fileobj.close()
                            continue
                        target, limit = (output, MAX_WIRE) if key.data == "out" else (errors, 4096)
                        if len(raw) > limit - len(target):
                            raise Rejected("office_output_limit")
                        target.extend(raw)
        if process.wait(timeout=max(0.01, deadline - time.monotonic())) != 0 or errors or offset != len(source):
            raise Rejected("office_rejected")
        result = json.loads(output); blocks = result["blocks"]
        if not isinstance(blocks, list) or len(blocks) > 8192:
            raise Rejected("office_structure")
        for block in blocks:
            if block["sequence"] != len(document.blocks):
                raise Rejected("office_structure")
            document.add(block["text"], block["separator"], block["position"])
        from images import append_images
        image_sources = result["images"]
        if not isinstance(image_sources, list) or len(image_sources) > 256:
            raise Rejected("office_structure")
        append_images(document, [(base64.b64decode(item["data"], validate=True), item["kind"], item["parent"]) for item in image_sources])
        return document
    finally:
        if process.poll() is None:
            process.kill()
        process.wait(timeout=2)
        for stream in (process.stdin, process.stdout, process.stderr):
            if stream and not stream.closed:
                stream.close()
