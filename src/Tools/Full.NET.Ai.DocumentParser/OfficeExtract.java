import java.io.*;
import java.util.*;
import org.apache.poi.hssf.usermodel.*;
import org.apache.poi.xssf.usermodel.XSSFCell;
import org.apache.poi.xssf.usermodel.XSSFSheet;
import org.apache.poi.hwpf.HWPFDocument;
import org.apache.poi.hwpf.usermodel.*;
import org.apache.poi.hslf.usermodel.*;
import org.apache.poi.ss.usermodel.*;
import org.apache.poi.poifs.filesystem.POIFSFileSystem;
import org.apache.poi.poifs.filesystem.DirectoryEntry;
import org.apache.poi.util.IOUtils;
import org.apache.poi.ddf.EscherPropertyTypes;
import com.fasterxml.jackson.databind.ObjectMapper;

/** 只提取 OLE 原生结构；不执行公式、宏、OLE 对象、外链或桌面 Office。 */
public final class OfficeExtract {
    private static final List<Map<String,Object>> blocks = new ArrayList<>();
    private static final List<Map<String,Object>> images = new ArrayList<>();
    private static int imageBytes = 0;
    private static int characters = 0;
    private static final int MAX_SOURCE = 8 * 1024 * 1024;
    private static int entries = 0;
    private static void checkDirectory(DirectoryEntry directory, int depth) throws IOException {
        require(depth <= 32);
        for (var entry : directory) {
            require(++entries <= 256);
            var name = entry.getName().toLowerCase(Locale.ROOT);
            require(!name.contains("vba") && !name.equals("macros") && !name.equals("objectpool") && !name.startsWith("mbd") && !name.equals("encryptedpackage"));
            if (entry instanceof DirectoryEntry child) checkDirectory(child, depth+1);
        }
    }
    private static void require(boolean condition) throws IOException {
        if (!condition) throw new IOException("fn_ai_parse_office_structure");
    }
    private static void add(String text, String separator, Map<String,Object> position) throws IOException {
        require(text.indexOf(0) < 0 && blocks.size() < 8192 && (characters += text.length() + separator.length()) <= 2 * 1024 * 1024);
        position.put("length", text.length());
        var block = new LinkedHashMap<String,Object>();
        block.put("sequence", blocks.size()); block.put("text", text); block.put("separator", separator); block.put("position", position);
        blocks.add(block);
    }
    private static Map<String,Object> position(int kind) {
        var p = new LinkedHashMap<String,Object>();
        p.put("kind", kind); p.put("offset", 0); p.put("length", 0);
        for (String key : List.of("pageNumber", "blockSequence", "paragraphNumber", "tableNumber", "rowNumber", "cellNumber")) p.put(key, null);
        p.put("cellParagraphCount", 0); p.put("isHeader", false);
        for (String key : List.of("grid", "slide", "htmlPath", "image")) p.put(key, null);
        return p;
    }
    private static void image(byte[] data, String kind, Map<String,Object> parent) throws IOException {
        require(data.length > 0 && data.length <= MAX_SOURCE && images.size() < 256 && (imageBytes += data.length) <= 32 * 1024 * 1024);
        var anchor = new LinkedHashMap<>(parent); anchor.put("length", 0);
        images.add(Map.of("data", Base64.getEncoder().encodeToString(data), "kind", kind, "parent", anchor));
    }
    private static void pictures(HWPFDocument document, Paragraph paragraph, Map<String,Object> parent) throws IOException {
        for (int index = 0; index < paragraph.numCharacterRuns(); index++) {
            var run = paragraph.getCharacterRun(index);
            if (document.getPicturesTable().hasPicture(run)) {
                var picture = document.getPicturesTable().extractPicture(run, false);
                image(picture.getContent(), picture.suggestFileExtension().toLowerCase(Locale.ROOT), parent);
            }
        }
    }
    private static String cached(Cell cell, CellType type) throws IOException {
        return switch(type) {
            case STRING -> cell.getStringCellValue();
            case NUMERIC -> { var value = cell.getNumericCellValue(); require(Double.isFinite(value)); yield Double.toString(value); }
            case BOOLEAN -> cell.getBooleanCellValue() ? "1" : "0";
            case ERROR -> Byte.toString(cell.getErrorCellValue());
            case BLANK, _NONE -> "";
            default -> throw new IOException("fn_ai_parse_cell_type");
        };
    }
    private static String cellKind(Cell cell, CellType type) {
        return switch(type) { case STRING -> "text"; case NUMERIC -> DateUtil.isCellDateFormatted(cell) ? "date" : "number";
            case BOOLEAN -> "boolean"; case ERROR -> "error"; default -> "blank"; };
    }
    private static void excel(byte[] source, boolean hidden) throws Exception {
        try (var workbook = WorkbookFactory.create(new ByteArrayInputStream(source))) {
            var formatter = new DataFormatter(Locale.ROOT); formatter.setUseCachedValuesForFormulaCells(true);
            require(workbook.getNumberOfSheets() > 0 && workbook.getNumberOfSheets() <= 128);
            for (int number = 0; number < workbook.getNumberOfSheets(); number++) {
                if (!hidden && (workbook.isSheetHidden(number) || workbook.isSheetVeryHidden(number))) continue;
                var sheet = workbook.getSheetAt(number);
                var positions = new HashMap<String,Map<String,Object>>();
                require(sheet.getNumMergedRegions() <= 1024);
                var mergedRegions = sheet.getMergedRegions();
                for (int i = 0; i < mergedRegions.size(); i++) {
                    var first = mergedRegions.get(i);
                    require(first.getFirstRow() >= 0 && first.getFirstColumn() >= 0 && first.getLastRow() < 1048576 && first.getLastColumn() < 16384);
                    for (int j = i + 1; j < mergedRegions.size(); j++) require(!first.intersects(mergedRegions.get(j)));
                }
                for (var row : sheet) {
                    if (!hidden && row.getZeroHeight()) continue;
                    for (var cell : row) {
                        if (!hidden && sheet.isColumnHidden(cell.getColumnIndex())) continue;
                        var type = cell.getCellType(); var actual = type == CellType.FORMULA ? cell.getCachedFormulaResultType() : type;
                        // OOXML 的空数值缓存不能由 POI 默认的 0 冒充已计算结果；显式空字符串缓存仍合法。
                        var present = actual != CellType._NONE && (!(cell instanceof XSSFCell xssf) || xssf.getRawValue() != null
                            && (actual == CellType.STRING || !xssf.getRawValue().isBlank()));
                        var grid = new LinkedHashMap<String,Object>();
                        grid.put("sheetNumber", number + 1); grid.put("sheetName", sheet.getSheetName()); grid.put("row", cell.getRowIndex() + 1); grid.put("column", cell.getColumnIndex() + 1);
                        grid.put("cellType", type == CellType.FORMULA ? "formula" : cellKind(cell, actual));
                        grid.put("formula", type == CellType.FORMULA ? cell.getCellFormula() : null);
                        grid.put("cacheState", type == CellType.FORMULA ? (present ? "present-unverified" : "missing") : null);
                        grid.put("rawValue", type == CellType.FORMULA ? present ? cached(cell, actual) : null
                            : actual == CellType.STRING ? null : cached(cell, actual));
                        grid.put("numberFormat", cell.getCellStyle().getDataFormatString());
                        grid.put("cachedValueType", type == CellType.FORMULA && present ? cellKind(cell, actual) : null);
                        String merged = null;
                        for (var span : mergedRegions) if (span.isInRange(cell)) {
                            require(merged == null);
                            merged = (span.getFirstRow()+1)+":"+(span.getFirstColumn()+1)+":"+(span.getLastRow()+1)+":"+(span.getLastColumn()+1);
                        }
                        grid.put("mergedRange", merged);
                        var p = position(5); p.put("grid", grid);
                        if (sheet instanceof XSSFSheet xssf) for (var table : xssf.getTables()) {
                            var headers = table.getCTTable().getHeaderRowCount(); require(headers == 0 || headers == 1);
                            if (headers == 1 && cell.getRowIndex() == table.getStartRowIndex()
                                && cell.getColumnIndex() >= table.getStartColIndex() && cell.getColumnIndex() <= table.getEndColIndex()) p.put("isHeader", true);
                        }
                        add(type == CellType.FORMULA && !present ? "" : formatter.formatCellValue(cell), "\n", p);
                        positions.put(cell.getRowIndex()+":"+cell.getColumnIndex(), p);
                    }
                }
                // 合并区域本身就是来源结构；即使左上角没有物理单元格，也保存空锚点而不捏造正文。
                for (var span : mergedRegions) {
                    var key = span.getFirstRow()+":"+span.getFirstColumn();
                    var sourceRow = sheet.getRow(span.getFirstRow());
                    if (positions.containsKey(key) || !hidden && (sheet.isColumnHidden(span.getFirstColumn()) || sourceRow != null && sourceRow.getZeroHeight())) continue;
                    var g = new LinkedHashMap<String,Object>();
                    g.put("sheetNumber", number+1); g.put("sheetName", sheet.getSheetName()); g.put("row", span.getFirstRow()+1); g.put("column", span.getFirstColumn()+1);
                    g.put("cellType", "blank"); g.put("formula", null); g.put("cacheState", null);
                    g.put("mergedRange", (span.getFirstRow()+1)+":"+(span.getFirstColumn()+1)+":"+(span.getLastRow()+1)+":"+(span.getLastColumn()+1));
                    g.put("rawValue", null); g.put("numberFormat", null); g.put("cachedValueType", null);
                    var p = position(5); p.put("grid", g); add("", "\n", p); positions.put(key, p);
                }
                if (sheet instanceof HSSFSheet hssf && hssf.getDrawingPatriarch() != null) for (var shape : hssf.getDrawingPatriarch().getChildren()) {
                    if (shape instanceof HSSFComment) continue;
                    require(shape instanceof HSSFPicture && shape.getAnchor() instanceof HSSFClientAnchor);
                    var anchor = (HSSFClientAnchor)shape.getAnchor(); var row = sheet.getRow(anchor.getRow1());
                    if (!hidden && (sheet.isColumnHidden(anchor.getCol1()) || row != null && row.getZeroHeight())) continue;
                    var key = anchor.getRow1()+":"+anchor.getCol1(); var p = positions.get(key);
                    if (p == null) {
                        var grid = new LinkedHashMap<String,Object>(); grid.put("sheetNumber", number+1); grid.put("sheetName", sheet.getSheetName());
                        grid.put("row", anchor.getRow1()+1); grid.put("column", anchor.getCol1()+1); grid.put("cellType", "blank");
                        grid.put("formula", null); grid.put("cacheState", null); grid.put("mergedRange", null);
                        grid.put("rawValue", null); grid.put("numberFormat", null); grid.put("cachedValueType", null);
                        p = position(5); p.put("grid", grid); add("", "\n", p); positions.put(key, p);
                    }
                    var data = ((HSSFPicture)shape).getPictureData(); image(data.getData(), data.suggestFileExtension().toLowerCase(Locale.ROOT), p);
                }
            }
        }
    }
    private static String paragraph(Paragraph p) throws IOException {
        var text = p.text();
        // 段落和单元格终止符是结构，不作为可引用正文；其余字段/图片标记必须明确拒绝。
        while (text.endsWith("\r") || text.endsWith("\u0007")) text = text.substring(0, text.length()-1);
        text = text.replace("\u0001", "").replace('\u000b', '\n');
        require(text.chars().noneMatch(c -> c < 32 && c != 9 && c != 10));
        return text;
    }
    private static void word(byte[] source) throws Exception {
        try (var document = new HWPFDocument(new ByteArrayInputStream(source))) {
            var stories = new HeaderStories(document);
            for (var story : new String[]{stories.getEvenHeader(), stories.getOddHeader(), stories.getFirstHeader(), stories.getEvenFooter(), stories.getOddFooter(), stories.getFirstFooter()})
                require(story == null || story.isBlank());
            var range = document.getRange(); int paragraph = 1, tableNumber = 0, index = 0, offset = 0;
            while (index < range.numParagraphs()) {
                var item = range.getParagraph(index);
                if (!item.isInTable()) {
                    var text = paragraph(item); var p = position(6);
                    p.put("offset", offset); p.put("blockSequence", blocks.size()); p.put("paragraphNumber", paragraph++);
                    add(text, "\n", p); pictures(document, item, p); offset += text.length()+1; index++;
                } else {
                    var table = range.getTable(item); require(++tableNumber <= 128 && table.numRows() <= 4096);
                    int consumed = 0;
                    for (int r = 0; r < table.numRows(); r++) {
                        var row = table.getRow(r); require(row.numCells() <= 64);
                        for (int c = 0; c < row.numCells(); c++) {
                            var cell = row.getCell(c); require(cell.numParagraphs() > 0 && !cell.isMerged() && !cell.isVerticallyMerged());
                            var texts = new ArrayList<String>();
                            for (int n = 0; n < cell.numParagraphs(); n++) texts.add(paragraph(cell.getParagraph(n)));
                            var text = String.join("\n", texts); var p = position(7);
                            p.put("offset", offset); p.put("blockSequence", blocks.size()); p.put("paragraphNumber", paragraph);
                            p.put("tableNumber", tableNumber); p.put("rowNumber", r+1); p.put("cellNumber", c+1); p.put("cellParagraphCount", texts.size());
                            add(text, c+1 == row.numCells() ? "\n" : "\t", p);
                            for (int n = 0; n < cell.numParagraphs(); n++) pictures(document, cell.getParagraph(n), p);
                            offset += text.length()+1; paragraph += texts.size(); consumed += texts.size();
                        }
                    }
                    require(consumed > 0);
                    while (index < range.numParagraphs() && range.getParagraph(index).getStartOffset() < table.getEndOffset()) index++;
                }
            }
        }
    }
    private static Map<String,Object> slideLocation(int slide, int shape, Integer row, Integer column, int rows, int columns) {
        var location = new LinkedHashMap<String,Object>();
        location.put("slideNumber", slide); location.put("shapeNumber", shape); location.put("rowNumber", row); location.put("columnNumber", column);
        location.put("rowSpan", rows); location.put("columnSpan", columns); return location;
    }
    private static boolean hiddenShape(HSLFShape shape) {
        if (shape.getEscherOptRecord() == null) return false;
        int flags = shape.getEscherProperty(EscherPropertyTypes.GROUPSHAPE__FLAGS);
        // OfficeArt 的值位与使用位须同时设置；隐藏组的状态继续传给全部子形状。
        return shape.getEscherProperty(EscherPropertyTypes.GROUPSHAPE__HIDDEN) != 0 || (flags & 0x00020002) == 0x00020002;
    }
    private static void shapes(List<HSLFShape> shapes, int slide, boolean notes, int[] number, boolean includeHidden, boolean parentHidden) throws Exception {
        for (var shape : shapes) {
            boolean invisible = parentHidden || hiddenShape(shape);
            if (shape instanceof HSLFGroupShape group && !(shape instanceof HSLFTable)) { shapes(group.getShapes(), slide, notes, number, includeHidden, invisible); continue; }
            require(++number[0] <= 8192);
            if (invisible && !includeHidden) continue;
            if (shape instanceof HSLFTable table) {
                require(table.getNumberOfRows() <= 4096 && table.getNumberOfColumns() <= 64);
                for (int row = 0; row < table.getNumberOfRows(); row++) for (int col = 0; col < table.getNumberOfColumns(); col++) {
                    var cell = table.getCell(row, col); if (cell == null) continue;
                    var p = position(notes ? 9 : 8); p.put("slide", slideLocation(slide, number[0], row+1, col+1, cell.getRowSpan(), cell.getGridSpan()));
                    add(cell.getText() == null ? "" : cell.getText(), "\n", p);
                }
            } else if (shape instanceof HSLFPictureShape picture) {
                var clip = picture.getClipping(); require(clip == null || clip.top == 0 && clip.left == 0 && clip.bottom == 0 && clip.right == 0);
                var p = position(notes ? 9 : 8); p.put("slide", slideLocation(slide, number[0], null, null, 1, 1)); add("", "\n", p);
                var data = picture.getPictureData(); image(data.getData(), data.getType().name().toLowerCase(Locale.ROOT), p);
            } else if (shape instanceof HSLFTextShape textShape) {
                var text = textShape.getText();
                if (text != null && !text.isBlank()) {
                    var p = position(notes ? 9 : 8); p.put("slide", slideLocation(slide, number[0], null, null, 1, 1)); add(text, "\n", p);
                }
            }
        }
    }
    private static void powerpoint(byte[] source, boolean hidden) throws Exception {
        try (var document = new HSLFSlideShow(new ByteArrayInputStream(source))) {
            require(document.getSlides().size() > 0 && document.getSlides().size() <= 128);
            int number = 0;
            for (var slide : document.getSlides()) {
                number++; if (slide.isHidden() && !hidden) continue;
                shapes(slide.getShapes(), number, false, new int[]{0}, hidden, false);
                if (slide.getNotes() != null) shapes(slide.getNotes().getShapes(), number, true, new int[]{0}, hidden, false);
            }
        }
    }
    public static void main(String[] args) {
        try {
            require(args.length == 2 && List.of("doc", "xls", "xlsx", "ppt").contains(args[0]) && List.of("include", "exclude").contains(args[1]));
            IOUtils.setByteArrayMaxOverride(32 * 1024 * 1024);
            var source = System.in.readNBytes(MAX_SOURCE + 1); require(source.length > 0 && source.length <= MAX_SOURCE);
            if (!args[0].equals("xlsx")) try (var filesystem = new POIFSFileSystem(new ByteArrayInputStream(source))) {
                var root = filesystem.getRoot();
                checkDirectory(root, 1);
                require(!root.hasEntry("_VBA_PROJECT_CUR") && !root.hasEntry("Macros") && !root.hasEntry("ObjectPool") && !root.hasEntry("EncryptedPackage"));
                require(root.hasEntry(switch(args[0]) { case "doc" -> "WordDocument"; case "xls" -> "Workbook"; default -> "PowerPoint Document"; }));
            }
            switch(args[0]) { case "doc" -> word(source); case "xls", "xlsx" -> excel(source, args[1].equals("include")); default -> powerpoint(source, args[1].equals("include")); }
            require(!images.isEmpty() || blocks.stream().anyMatch(b -> !((String)b.get("text")).isBlank()));
            byte[] output = new ObjectMapper().writeValueAsBytes(Map.of("blocks", blocks, "images", images)); require(output.length <= 16 * 1024 * 1024);
            System.out.write(output); System.out.flush();
        } catch (Exception error) { System.err.print("fn_ai_parse_office_rejected\n"); System.exit(1); }
    }
}
