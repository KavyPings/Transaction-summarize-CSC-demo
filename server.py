import json

# pyrefly: ignore [missing-import]
from flask import Flask, jsonify, render_template

from app import create_client
from filter_data import build_summary_payload
from prompt import SYSTEM_PROMPT

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
        return jsonify({"summary": summary})
    except Exception as error:  # noqa: BLE001 - show any failure on the page
        return jsonify(
            {"error": f"Could not generate summary: {error}"}
        ), 500


if __name__ == "__main__":
    app.run(debug=True, port=5000)
