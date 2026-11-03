package net.full.connect;

import java.util.Map;
import org.apache.kafka.connect.data.Schema;
import org.apache.kafka.connect.data.SchemaBuilder;
import org.apache.kafka.connect.data.Struct;
import org.apache.kafka.connect.errors.DataException;
import org.apache.kafka.connect.source.SourceRecord;

/** 不依赖 Broker 的过滤契约；真实双库 CDC 验收另行验证连接器链路。 */
public final class FullNetOutboxStreamFilterTest {
    private static final String SOURCE = "source.database.outbox";
    private static final Schema ROW = SchemaBuilder.struct().optional()
        .field("MessageType", Schema.STRING_SCHEMA).field("SchemaVersion", Schema.INT32_SCHEMA).build();
    private static final Schema ENVELOPE = SchemaBuilder.struct().field("after", ROW).build();

    public static void main(String[] args) {
        var filter = new FullNetOutboxStreamFilter<SourceRecord>();
        filter.configure(Map.of("source.topic", SOURCE, "event.type", "fullnet.order.changed", "schema.version", 1));
        var accepted = event("fullnet.order.changed", 1);
        require(filter.apply(accepted) == accepted, "指定事件必须保留");
        require(filter.apply(event("fullnet.order.changed", 2)) == null, "不同版本必须过滤");
        require(filter.apply(event("fullnet.other.changed", 1)) == null, "不同类型必须过滤");
        var heartbeat = new SourceRecord(Map.of(), Map.of(), "heartbeat", Schema.STRING_SCHEMA, "heartbeat");
        require(filter.apply(heartbeat) == heartbeat, "心跳必须原样保留");
        var deletion = new SourceRecord(Map.of(), Map.of(), SOURCE, ENVELOPE, new Struct(ENVELOPE).put("after", null));
        require(filter.apply(deletion) == deletion, "异常源操作由后续 EventRouter 处理");
        var invalid = new SourceRecord(Map.of(), Map.of(), SOURCE, Schema.STRING_SCHEMA, "invalid");
        try { filter.apply(invalid); throw new AssertionError("源记录格式错误必须失败关闭"); }
        catch (DataException expected) { }
        filter.close();
        System.out.println("FullNetOutboxStreamFilterTest passed");
    }

    private static SourceRecord event(String type, int version) {
        var row = new Struct(ROW).put("MessageType", type).put("SchemaVersion", version);
        return new SourceRecord(Map.of(), Map.of(), SOURCE, ENVELOPE, new Struct(ENVELOPE).put("after", row));
    }
    private static void require(boolean condition, String message) { if (!condition) throw new AssertionError(message); }
}
