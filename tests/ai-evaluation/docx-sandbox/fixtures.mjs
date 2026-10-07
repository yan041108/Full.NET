import { deflateRawSync } from 'node:zlib';

// 仓库原创 OOXML 包；仅在本地合成，ZIP 元数据与 UTF-8 字节均固定。
export function createDocx() {
  const word = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main';
  const paragraph = text => `<w:p><w:r><w:t>${text}</w:t></w:r></w:p>`;
  const cell = text => `<w:tc>${paragraph(text)}</w:tc>`;
  const entries = [
    ['[Content_Types].xml', '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>'],
    ['_rels/.rels', '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>'],
    ['word/document.xml', `<w:document xmlns:w="${word}"><w:body>${paragraph('合成制度😀')}${paragraph('')}<w:tbl><w:tr><w:trPr><w:tblHeader/></w:trPr>${cell('项目')}${cell('期限')}</w:tr><w:tr>${cell('申请')}${cell('三个工作日')}</w:tr></w:tbl>${paragraph('&lt;script&gt;原文&lt;/script&gt;')}<w:sectPr/></w:body></w:document>`]
  ];
  const local = [], central = []; let offset = 0;
  for (const [name, body] of entries) {
    const file = Buffer.from(name); const bytes = Buffer.from(body); const packed = deflateRawSync(bytes);
    let crc = 0xffffffff;
    for (const value of bytes) { crc ^= value; for (let i = 0; i < 8; i++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0); }
    crc = (crc ^ 0xffffffff) >>> 0;
    const header = Buffer.alloc(30); header.writeUInt32LE(0x04034b50); header.writeUInt16LE(20, 4); header.writeUInt16LE(8, 8);
    header.writeUInt32LE(crc, 14); header.writeUInt32LE(packed.length, 18); header.writeUInt32LE(bytes.length, 22); header.writeUInt16LE(file.length, 26);
    local.push(header, file, packed);
    const record = Buffer.alloc(46); record.writeUInt32LE(0x02014b50); record.writeUInt16LE(20, 4); record.writeUInt16LE(20, 6); record.writeUInt16LE(8, 10);
    record.writeUInt32LE(crc, 16); record.writeUInt32LE(packed.length, 20); record.writeUInt32LE(bytes.length, 24); record.writeUInt16LE(file.length, 28); record.writeUInt32LE(offset, 42);
    central.push(record, file); offset += header.length + file.length + packed.length;
  }
  const directory = Buffer.concat(central); const end = Buffer.alloc(22); end.writeUInt32LE(0x06054b50); end.writeUInt16LE(entries.length, 8); end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(directory.length, 12); end.writeUInt32LE(offset, 16);
  return Buffer.concat([...local, directory, end]);
}
