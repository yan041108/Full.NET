package net.full.connect;

import java.util.Map;
import org.apache.kafka.common.config.AbstractConfig;
import org.apache.kafka.common.config.ConfigDef;
import org.apache.kafka.connect.connector.ConnectRecord;
import org.apache.kafka.connect.data.Struct;
import org.apache.kafka.connect.errors.DataException;
import org.apache.kafka.connect.transforms.Transformation;

/** 在 EventRouter 前按原始行的事件类型和版本过滤，避免同表其它流进入目标 Topic。 */
public final class FullNetOutboxStreamFilter<R extends ConnectRecord<R>> implements Transformation<R> {
    private static final ConfigDef CONFIG = new ConfigDef()
        .define("source.topic", ConfigDef.Type.STRING, ConfigDef.Importance.HIGH, "Captured outbox source topic")
        .define("event.type", ConfigDef.Type.STRING, ConfigDef.Importance.HIGH, "Exact event type")
        .define("schema.version", ConfigDef.Type.INT, ConfigDef.NO_DEFAULT_VALUE, ConfigDef.Range.atLeast(1), ConfigDef.Importance.HIGH, "Exact schema version");
    private String sourceTopic;
    private String eventType;
    private int schemaVersion;

    @Override public void configure(Map<String, ?> properties) {
        var config = new AbstractConfig(CONFIG, properties);
        sourceTopic = config.getString("source.topic");
        eventType = config.getString("event.type");
        schemaVersion = config.getInt("schema.version");
        if (sourceTopic.isBlank() || eventType.isBlank()) throw new DataException("Source topic and event type are required.");
    }

    @Override public R apply(R record) {
        // 心跳与其它控制记录不属于捕获表，原样交给后续处理。
        if (!sourceTopic.equals(record.topic()) || record.value() == null) return record;
        if (!(record.value() instanceof Struct envelope) || envelope.schema().field("after") == null)
            throw new DataException("Captured outbox event has no structured after image.");
        Object value = envelope.get("after");
        // 无 after 的记录由后续 EventRouter 按其操作类型处理。
        if (value == null) return record;
        if (!(value instanceof Struct row) || row.schema().field("MessageType") == null || row.schema().field("SchemaVersion") == null)
            throw new DataException("Captured outbox event lacks stream identity.");
        Object type = row.get("MessageType");
        Object version = row.get("SchemaVersion");
        if (!(type instanceof String name) || !(version instanceof Integer number) || number < 1)
            throw new DataException("Captured outbox event has invalid stream identity.");
        return eventType.equals(name) && schemaVersion == number ? record : null;
    }

    @Override public ConfigDef config() { return CONFIG; }
    @Override public void close() { }
}
