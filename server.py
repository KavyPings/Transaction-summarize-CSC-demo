import json

# pyrefly: ignore [missing-import]
from flask import Flask, jsonify, render_template

from app import create_client
from filter_data import build_summary_payload
from prompt import SYSTEM_PROMPT
from doc_intelligence import get_document_overview, get_excel_content, EXCEL_EXTENSIONS
from pathlib import Path

app = Flask(__name__)

TRANSACTION_FILE = "transaction.json"


def load_transaction() -> dict:
    with open(TRANSACTION_FILE, "r", encoding="utf-8-sig") as file:
        return json.load(file)


@app.route("/")
def index():
    transaction = load_transaction()
    return render_template("index.html", transaction=transaction)


@app.route("/summarize", methods=["POST"])
def summarize():
    try:
        transaction = load_transaction()
        payload = build_summary_payload(transaction)

        
        client, deployment = create_client()
        response = client.chat.completions.create(
            model=deployment,
            temperature=0.2,
            messages=[
                {"role": "system", "content": SYSTEM_PROMPT},
                {"role": "user", "content": json.dumps(payload, indent=2)},
            ],
        )
        summary = response.choices[0].message.content or ""
        
        summary = "[LLM summary disabled for testing]"

        result: dict = {"summary": summary}

        evidence_path = transaction.get("evidence_file")
        if evidence_path:
            result["doc_file"] = evidence_path
            is_excel = Path(evidence_path).suffix.lower() in EXCEL_EXTENSIONS
            try:
                if is_excel:
                    result["doc_content"] = get_excel_content(evidence_path)
                    result["doc_source"] = "Spreadsheet (openpyxl)"
                else:
                    result["doc_content"] = get_document_overview(evidence_path)
                    result["doc_source"] = "Document Intelligence"
            except Exception as exc:  # noqa: BLE001
                result["doc_error"] = str(exc)

        return jsonify(result)
    except Exception as error:  # noqa: BLE001 - show any failure on the page
        return jsonify(
            {"error": f"Could not generate summary: {error}"}
        ), 500


if __name__ == "__main__":
    app.run(debug=True, port=5000)
