from __future__ import annotations

import os
from pathlib import Path

EXCEL_EXTENSIONS = {".xlsx", ".xls"}


def _extract_excel(path: str) -> str:
    try:
        import openpyxl  # type: ignore
    except ImportError:
        raise RuntimeError("Install openpyxl: pip install openpyxl")

    wb = openpyxl.load_workbook(path, data_only=True)
    sections: list[str] = []

    for sheet_name in wb.sheetnames:
        ws = wb[sheet_name]
        rows = [r for r in ws.iter_rows(values_only=True) if any(c is not None for c in r)]
        if not rows:
            continue
        lines = [f"Sheet: {sheet_name}"]
        for row in rows:
            cells = [str(c) if c is not None else "" for c in row]
            while cells and not cells[-1]:
                cells.pop()
            if cells:
                lines.append("  |  ".join(cells))
        sections.append("\n".join(lines))

    return "\n\n".join(sections) if sections else "(empty workbook)"


def _extract_pdf(path: str) -> str:
    try:
        import pdfplumber  # type: ignore
    except ImportError:
        raise RuntimeError("Install pdfplumber: pip install pdfplumber")

    pages: list[str] = []
    with pdfplumber.open(path) as pdf:
        for i, page in enumerate(pdf.pages, 1):
            text = page.extract_text()
            if text and text.strip():
                pages.append(f"--- Page {i} ---\n{text.strip()}")

    return "\n\n".join(pages) if pages else "(no text found in PDF)"


def _extract_msg(path: str) -> str:
    try:
        import extract_msg  # type: ignore
    except ImportError:
        raise RuntimeError("Install extract-msg: pip install extract-msg")

    msg = extract_msg.Message(path)
    parts: list[str] = []
    if msg.subject:
        parts.append(f"Subject: {msg.subject}")
    if msg.sender:
        parts.append(f"From: {msg.sender}")
    if msg.date:
        parts.append(f"Date: {msg.date}")
    if msg.body:
        parts.append(f"\nBody:\n{msg.body.strip()}")
    return "\n".join(parts)


def extract_evidence_text(path: str) -> str:
    """Extract plain text from an evidence document for LLM consumption."""
    if not os.path.exists(path):
        raise FileNotFoundError(f"Evidence file not found: {path}")

    suffix = Path(path).suffix.lower()

    if suffix in EXCEL_EXTENSIONS:
        return _extract_excel(path)
    if suffix == ".pdf":
        return _extract_pdf(path)
    if suffix == ".msg":
        return _extract_msg(path)
    if suffix in (".png", ".jpg", ".jpeg", ".bmp", ".tiff"):
        raise ValueError("Image files require OCR — not supported in text-extraction mode.")

    raise ValueError(f"Unsupported file type for extraction: {suffix}")
