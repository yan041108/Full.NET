import java.io.*;
import java.nio.file.*;
import org.apache.poi.hssf.usermodel.HSSFWorkbook;
import org.apache.poi.hslf.usermodel.*;
import org.apache.poi.xssf.usermodel.XSSFWorkbook;
import org.apache.poi.xslf.usermodel.*;
import java.awt.Rectangle;
import org.apache.poi.xwpf.usermodel.*;
import org.apache.poi.sl.usermodel.PictureData.PictureType;
import org.apache.poi.util.Units;

/** 自行生成包含中文、缓存公式、隐藏行列及幻灯片备注的真实 Office 文件。 */
public final class OfficeFixtures {
    public static void main(String[] args) throws Exception {
        var root = Path.of(args[0]); Files.createDirectories(root);
        var photo = Files.readAllBytes(root.resolve("source.png"));
        try (var doc = new XWPFDocument()) {
            doc.createParagraph().createRun().setText("Word 原生中文正文");
            var table = doc.createTable(2, 2); table.getRow(0).getCell(0).setText("表头"); table.getRow(1).getCell(1).setText("2026");
            doc.createParagraph().createRun().addPicture(new ByteArrayInputStream(photo), XWPFDocument.PICTURE_TYPE_PNG, "source.png", Units.toEMU(500), Units.toEMU(110));
            try (var output = Files.newOutputStream(root.resolve("source.docx"))) { doc.write(output); }
        }
        try (var book = new HSSFWorkbook()) {
            var sheet = book.createSheet("中文工作表"); var row = sheet.createRow(0);
            row.createCell(0).setCellValue("知识库标题"); row.createCell(1).setCellValue(7);
            var formula = row.createCell(2); formula.setCellFormula("B1+2"); formula.setCellValue(9);
            sheet.createRow(1).createCell(0).setCellValue("第二行内容");
            sheet.createRow(2).createCell(0).setCellValue("隐藏行内容"); sheet.getRow(2).setZeroHeight(true);
            var pictureIndex = book.addPicture(photo, HSSFWorkbook.PICTURE_TYPE_PNG);
            sheet.createDrawingPatriarch().createPicture(new org.apache.poi.hssf.usermodel.HSSFClientAnchor(0,0,0,0,(short)0,4,(short)4,8), pictureIndex);
            try (var output = Files.newOutputStream(root.resolve("source.xls"))) { book.write(output); }
        }
        try (var book = new XSSFWorkbook()) {
            var sheet = book.createSheet("中文工作表"); var row = sheet.createRow(0);
            row.createCell(0).setCellValue("知识库标题"); row.createCell(1).setCellValue(7);
            var formula = row.createCell(2); formula.setCellFormula("B1+2"); formula.setCellValue(9);
            sheet.createRow(1).createCell(0).setCellValue("第二行内容");
            var pictureIndex = book.addPicture(photo, XSSFWorkbook.PICTURE_TYPE_PNG);
            sheet.createDrawingPatriarch().createPicture(new org.apache.poi.xssf.usermodel.XSSFClientAnchor(0,0,0,0,0,4,4,8), pictureIndex);
            sheet.createRow(2).createCell(0).setCellValue("隐藏行内容"); sheet.getRow(2).setZeroHeight(true);
            try (var output = Files.newOutputStream(root.resolve("source.xlsx"))) { book.write(output); }
        }
        try (var show = new HSLFSlideShow()) {
            var slide = show.createSlide(); var text = new HSLFTextBox();
            text.setText("幻灯片中文内容"); text.setAnchor(new Rectangle(20,20,600,100)); slide.addShape(text);
            var picture = new HSLFPictureShape(show.addPicture(photo, PictureType.PNG)); picture.setAnchor(new Rectangle(20,150,500,110)); slide.addShape(picture);
            try (var output = Files.newOutputStream(root.resolve("source.ppt"))) { show.write(output); }
        }
        try (var show = new XMLSlideShow()) {
            var slide = show.createSlide(); var text = slide.createTextBox();
            text.setText("幻灯片中文内容"); text.setAnchor(new Rectangle(20,20,600,100));
            var notes = show.getNotesSlide(slide); var note = notes.createTextBox();
            note.setText("演讲者中文备注"); note.setAnchor(new Rectangle(20,20,600,100));
            slide.createPicture(show.addPicture(photo, PictureType.PNG)).setAnchor(new Rectangle(20,150,500,110));
            try (var output = Files.newOutputStream(root.resolve("source.pptx"))) { show.write(output); }
        }
        Files.writeString(root.resolve("source.txt"), "知识库标题\n\n旧版 Word 中文正文。\n");
        System.out.println("office-fixtures-created");
    }
}
