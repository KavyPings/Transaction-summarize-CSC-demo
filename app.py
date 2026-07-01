import truststore
truststore.inject_into_ssl()

import json
import os
import sys

# Print UTF-8 to the terminal so summaries with characters like
# non-breaking hyphens don't crash on Windows (cp1252) consoles.
try:
    sys.stdout.reconfigure(encoding="utf-8")
except AttributeError:
    pass

# pyrefly: ignore [missing-import]
from dotenv import load_dotenv

from openai import AzureOpenAI

from filter_data import build_summary_payload
from prompt import SYSTEM_PROMPT
from evidence_extractor import extract_evidence_text

load_dotenv()


def create_client() -> tuple[AzureOpenAI, str]:
    endpoint = os.getenv("AZURE_OPENAI_ENDPOINT")
    api_key = os.getenv("AZURE_OPENAI_API_KEY")
    api_version = os.getenv("AZURE_OPENAI_API_VERSION", "2024-02-01")
    deployment = os.getenv("AZURE_OPENAI_DEPLOYMENT")

    missing = [
        name
        for name, value in {
            "AZURE_OPENAI_ENDPOINT": endpoint,
            "AZURE_OPENAI_API_KEY": api_key,
            "AZURE_OPENAI_DEPLOYMENT": deployment,
        }.items()
        if not value
    ]
    if missing:
        missing_text = ", ".join(missing)
        raise RuntimeError(
            f"Missing Azure OpenAI settings in .env: {missing_text}"
        )

    client = AzureOpenAI(
        azure_endpoint=endpoint,
        api_key=api_key,
        api_version=api_version,
    )
    return client, deployment

def main() -> None:
    client, deployment = create_client()

    with open("transaction.json", "r", encoding="utf-8-sig") as file:
        transaction = json.load(file)

    safe_payload = build_summary_payload(transaction)

    with open("sanitized_payload.json", "w", encoding="utf-8") as file:
        json.dump(safe_payload, file, indent=2)

    user_content = json.dumps(safe_payload, indent=2)

    evidence_path: str | None = transaction.get("evidence_file")
    if evidence_path:
        try:
            evidence_text = extract_evidence_text(evidence_path)
            from pathlib import Path
            user_content += f"\n\nEVIDENCE DOCUMENT ({Path(evidence_path).name}):\n{evidence_text}"
        except Exception as exc:
            user_content += f"\n\nEVIDENCE DOCUMENT: [Could not extract — {exc}]"

    response = client.chat.completions.create(
        model=deployment,
        temperature=0.2,
        messages=[
            {"role": "system", "content": SYSTEM_PROMPT},
            {"role": "user", "content": user_content},
        ],
    )

    summary = response.choices[0].message.content or ""

    print()
    print("=" * 60)
    print("TRANSACTION SUMMARY")
    print("=" * 60)
    print()
    print(summary)

    with open("summary.txt", "w", encoding="utf-8") as file:
        file.write(summary)


if __name__ == "__main__":
    main()
