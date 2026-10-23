"""私有工具入口；参数是可信控制器协议，不接受路径或网址。"""
import argparse
import os
import sys
import subprocess
from protocol import MAX_SOURCE, Rejected
from text_formats import parse_csv, parse_html
from ooxml import parse_xlsx, parse_pptx, parse_docx
from office import parse_office
from images import FORMATS, parse_image
from pdf import parse_pdf


def main():
    options = argparse.ArgumentParser()
    options.add_argument("--format", required=True, choices=("csv", "html", "xlsx", "pptx", "docx", "doc", "xls", "ppt", "pdf", *FORMATS))
    options.add_argument("--encoding", choices=("utf-8", "gb18030"), default="utf-8")
    options.add_argument("--delimiter", choices=("comma", "tab", "semicolon"), default="comma")
    options.add_argument("--header", choices=("yes", "no"), default="no")
    options.add_argument("--hidden", choices=("exclude", "include"), default="exclude")
    args = options.parse_args()
    os.environ["OMP_NUM_THREADS"] = "1"
    os.environ["OPENBLAS_NUM_THREADS"] = "2"
    os.environ["MKL_NUM_THREADS"] = "2"
    # 第三方 Python/C++/JVM 日志不能污染私有正文协议；两个专用句柄只用于最终正文或固定错误码。
    output_stream = os.fdopen(os.dup(sys.stdout.fileno()), "wb")
    error_stream = os.fdopen(os.dup(sys.stderr.fileno()), "w", encoding="ascii")
    with open(os.devnull, "wb") as quiet:
        os.dup2(quiet.fileno(), sys.stdout.fileno()); os.dup2(quiet.fileno(), sys.stderr.fileno())
    try:
        source = sys.stdin.buffer.read(MAX_SOURCE + 1)
        if args.format == "csv":
            document = parse_csv(source, args.encoding, {"comma": ",", "tab": "\t", "semicolon": ";"}[args.delimiter], args.header == "yes")
        elif args.format == "html":
            document = parse_html(source, args.encoding)
        elif args.format == "xlsx":
            document = parse_xlsx(source, args.hidden == "include")
        elif args.format == "pptx":
            document = parse_pptx(source, args.hidden == "include")
        elif args.format == "docx":
            document = parse_docx(source)
        elif args.format in ("doc", "xls", "ppt"):
            document = parse_office(source, args.format, args.hidden == "include")
        elif args.format == "pdf":
            document = parse_pdf(source)
        else:
            document = parse_image(source, args.format)
        # 必须先构造全部字节，任何后置完整性拒绝也不能泄漏部分 stdout。
        output = document.encode()
        output_stream.write(output)
        output_stream.flush()
        return 0
    except Exception as error:
        code = str(error) if isinstance(error, Rejected) else "invalid_document"
        if isinstance(error, subprocess.TimeoutExpired):
            code = "parser_timeout"
        try:
            error_stream.write("fn_ai_parse_" + code + "\n"); error_stream.flush()
        except OSError:
            pass
        return 1
    finally:
        output_stream.close(); error_stream.close()


if __name__ == "__main__":
    raise SystemExit(main())
