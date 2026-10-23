FROM ubuntu:24.04@sha256:68ee22004a9109b44973313b02ff7b1de250f1de0f5ecb628d73f579aaff33ac AS buildbase
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends openjdk-21-jdk-headless maven python3 python3-venv ca-certificates && rm -rf /var/lib/apt/lists/*
FROM buildbase AS builder
WORKDIR /build
COPY pom.xml OfficeExtract.java ./
RUN mvn --batch-mode -q compile dependency:copy-dependencies -DoutputDirectory=/opt/java-libs
COPY requirements.lock ./
RUN python3 -m venv /opt/ocr && /opt/ocr/bin/python -m pip install --no-cache-dir --require-hashes -r requirements.lock
COPY model-assets.lock.json fetch_assets.py ./
RUN python3 fetch_assets.py
RUN /opt/ocr/bin/python -m pip uninstall -y pip

FROM ubuntu:24.04@sha256:68ee22004a9109b44973313b02ff7b1de250f1de0f5ecb628d73f579aaff33ac
COPY --from=buildbase /etc/ssl/certs/ca-certificates.crt /etc/ssl/certs/ca-certificates.crt
RUN sed -i 's|http://|https://|g' /etc/apt/sources.list.d/ubuntu.sources && apt-get -o Acquire::Retries=3 -o Acquire::https::Timeout=30 update && DEBIAN_FRONTEND=noninteractive apt-get -o Acquire::Retries=3 -o Acquire::https::Timeout=30 install -y --no-install-recommends python3 openjdk-21-jre-headless libreoffice-draw librsvg2-bin libjxl-tools libgl1 libglib2.0-0t64 libgomp1 fonts-noto-cjk ca-certificates && rm -rf /var/lib/apt/lists/*
COPY --from=builder /opt/ocr /opt/ocr
COPY --from=builder /opt/java-libs /opt/java-libs
COPY --from=builder /opt/models /opt/models
COPY --from=builder /build/target/classes /app/classes
COPY *.py log4j2.xml /app/
COPY licenses/ /app/licenses/
COPY requirements.lock model-assets.lock.json java-assets.lock.json pom.xml /app/assets/
RUN cp /opt/ocr/lib/python3.12/site-packages/paddleocr-3.7.0.dist-info/LICENSE /opt/models/LICENSE-APACHE-2.0 && /opt/ocr/bin/python -B /app/verify_assets.py && dpkg-query -W > /app/assets/debian-packages.txt && find /opt/java-libs -type f -name '*.jar' -exec sha256sum {} + > /app/assets/java-sha256.txt
ENV PATH=/opt/ocr/bin:/usr/bin:/bin HOME=/tmp LANG=C.UTF-8
USER 65532:65532
WORKDIR /app
ENTRYPOINT []
CMD ["/opt/ocr/bin/python", "-B", "/app/parser.py"]
