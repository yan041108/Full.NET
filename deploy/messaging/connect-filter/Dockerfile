FROM quay.io/debezium/connect@sha256:4f807bd409b47268077e1142524f368db170560d5caceaee48a6a7a7fd288570 AS build
USER root
COPY src/FullNetOutboxStreamFilter.java /tmp/FullNetOutboxStreamFilter.java
RUN mkdir -p /tmp/classes /tmp/service/META-INF/services \
    && java -m jdk.compiler/com.sun.tools.javac.Main -source 21 -target 21 -cp /kafka/libs/connect-api-4.1.1.jar:/kafka/libs/kafka-clients-4.1.1.jar -d /tmp/classes /tmp/FullNetOutboxStreamFilter.java \
    && printf 'net.full.connect.FullNetOutboxStreamFilter\n' > /tmp/service/META-INF/services/org.apache.kafka.connect.transforms.Transformation \
    && java -m jdk.jartool/sun.tools.jar.Main --create --date=2026-01-01T00:00:00Z --file /tmp/fullnet-outbox-stream-filter.jar -C /tmp/classes net/full/connect/FullNetOutboxStreamFilter.class -C /tmp/service META-INF/services/org.apache.kafka.connect.transforms.Transformation

FROM quay.io/debezium/connect@sha256:4f807bd409b47268077e1142524f368db170560d5caceaee48a6a7a7fd288570
COPY --from=build /tmp/fullnet-outbox-stream-filter.jar /kafka/connect/fullnet-stream-filter/fullnet-outbox-stream-filter.jar
