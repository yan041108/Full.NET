import java.awt.Rectangle;
import java.io.OutputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import org.apache.poi.ddf.EscherPropertyTypes;
import org.apache.poi.ddf.EscherContainerRecord;
import org.apache.poi.ddf.EscherOptRecord;
import org.apache.poi.hslf.usermodel.*;

/** 真实旧版 PPT 的独立隐藏形状及隐藏组，验证默认策略不会把隐藏正文混入索引。 */
public final class PptVisibilityFixtures {
    public static void main(String[] args) throws Exception {
        try (var document = new HSLFSlideShow()) {
            var slide = document.createSlide();
            var visible = new HSLFTextBox(); visible.setText("VISIBLE_SHAPE"); visible.setAnchor(new Rectangle(10, 10, 200, 40)); slide.addShape(visible);
            var hidden = new HSLFTextBox(); hidden.setText("PRIVATE_SHAPE_20261023"); hidden.setAnchor(new Rectangle(10, 70, 200, 40));
            hidden.setEscherProperty(EscherPropertyTypes.GROUPSHAPE__FLAGS, 0x00020002); slide.addShape(hidden);
            var group = new HSLFGroupShape(); group.setAnchor(new Rectangle(10, 120, 220, 80));
            var properties = new EscherOptRecord(); properties.setRecordId(EscherOptRecord.RECORD_ID);
            ((EscherContainerRecord)group.getSpContainer().getChild(0)).addChildRecord(properties);
            group.setEscherProperty(EscherPropertyTypes.GROUPSHAPE__FLAGS, 0x00020002); slide.addShape(group);
            var child = new HSLFTextBox(group); child.setText("PRIVATE_GROUP_20261023"); child.setAnchor(new Rectangle(0, 0, 200, 40)); group.addShape(child);
            try (OutputStream output = Files.newOutputStream(Path.of(args[0]))) { document.write(output); }
        }
    }
}
