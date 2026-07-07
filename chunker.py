from __future__ import annotations


def chunk_transaction(payload: dict) -> list[dict]:
    """Convert the filtered transaction payload into labelled text chunks."""
    chunks = []

    txn = payload.get("transaction", {})
    chunks.append({
        "id": "txn",
        "text": "TRANSACTION DETAILS:\n" + "\n".join(
            f"{k}: {v}" for k, v in txn.items() if v is not None
        ),
        "type": "transaction",
    })

    flags = payload.get("risk_flags", {})
    chunks.append({
        "id": "risk_flags",
        "text": "RISK FLAGS:\n" + "\n".join(f"{k}: {v}" for k, v in flags.items()),
        "type": "risk_flags",
    })

    checklist = payload.get("checklist_results", {})
    if checklist:
        chunks.append({
            "id": "checklist",
            "text": "CHECKLIST RESULTS:\n" + "\n".join(f"{k}: {v}" for k, v in checklist.items()),
            "type": "checklist",
        })

    info = payload.get("additional_information", {})
    info_lines = [f"{k}: {v}" for k, v in info.items() if v]
    if info_lines:
        chunks.append({
            "id": "additional",
            "text": "ADDITIONAL INFORMATION:\n" + "\n".join(info_lines),
            "type": "additional",
        })

    return chunks


def chunk_evidence(text: str, label: str, max_chars: int = 1200) -> list[dict]:
    """Split evidence text into chunks of ~max_chars, preserving line boundaries."""
    lines = text.split("\n")
    chunks: list[dict] = []
    current: list[str] = []
    current_len = 0
    idx = 0

    for line in lines:
        if current_len + len(line) + 1 > max_chars and current:
            chunks.append({
                "id": f"{label}_chunk_{idx}",
                "text": f"{label}:\n" + "\n".join(current),
                "type": "evidence",
                "label": label,
            })
            current = []
            current_len = 0
            idx += 1
        current.append(line)
        current_len += len(line) + 1

    if current:
        chunks.append({
            "id": f"{label}_chunk_{idx}",
            "text": f"{label}:\n" + "\n".join(current),
            "type": "evidence",
            "label": label,
        })

    return chunks
