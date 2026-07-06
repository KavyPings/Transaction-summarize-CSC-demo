import json

# pyrefly: ignore [missing-import]
from flask import Flask, jsonify, render_template, request

from app import create_client
from filter_data import build_summary_payload
from prompt import SYSTEM_PROMPT
from evidence_extractor import build_user_message

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

        # Page agent sends pre-filtered payload as JSON body; fall back to server-side filtering
        payload = request.get_json(silent=True) or build_summary_payload(transaction)

        evidence_files = transaction.get("evidence_files") or []
        user_message = build_user_message(json.dumps(payload, indent=2), evidence_files)

        client, deployment = create_client()
        response = client.chat.completions.create(
            model=deployment,
            temperature=0.2,
            messages=[
                {"role": "system", "content": SYSTEM_PROMPT},
                {"role": "user", "content": user_message},
            ],
 )
        summary = response.choices[0].message.content or ""

        return jsonify({"summary": summary})
    except Exception as error:  # noqa: BLE001
        return jsonify({"error": f"Could not generate summary: {error}"}), 500


if __name__ == "__main__":
    app.run(debug=True, port=5000)
