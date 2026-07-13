import truststore
truststore.inject_into_ssl()

import json
import os
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8")
except AttributeError:
    pass

# pyrefly: ignore [missing-import] 
from dotenv import load_dotenv
from openai import AzureOpenAI

from filter_data import build_summary_payload
from prompt import SYSTEM_PROMPT
from evidence_extractor import build_user_message

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
        raise RuntimeError(f"Missing Azure OpenAI settings in .env: {', '.join(missing)}")

    client = AzureOpenAI(
        azure_endpoint=endpoint,
        api_key=api_key,
        api_version=api_version,
    )
    return client, deployment


def main() -> None:
    client, deployment = create_client()

    with open("transaction.json", "r", encoding="utf-8-sig") as f:
        transaction = json.load(f)

    user_content = json.dumps(build_summary_payload(transaction), indent=2)

    evidence_files = transaction.get("evidence_files") or []
    user_message = build_user_message(user_content, evidence_files)

    response = client.chat.completions.create(
        model=deployment,
        temperature=0.2,
        messages=[
            {"role": "system", "content": SYSTEM_PROMPT},
            {"role": "user", "content": user_message},
        ],
    )

    summary = response.choices[0].message.content or ""
    print(f"\n{'=' * 60}\nTRANSACTION SUMMARY\n{'=' * 60}\n\n{summary}")

    with open("summary.txt", "w", encoding="utf-8") as f:
        f.write(summary)


if __name__ == "__main__":
    main()
