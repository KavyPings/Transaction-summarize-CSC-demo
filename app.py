import json
import os

from dotenv import load_dotenv
from openai import AzureOpenAI

from filter_data import build_summary_payload
from prompt import SYSTEM_PROMPT

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

    response = client.chat.completions.create(
        model=deployment,
        temperature=0.2,
        messages=[
            {
                "role": "system",
                "content": SYSTEM_PROMPT,
            },
            {
                "role": "user",
                "content": json.dumps(safe_payload, indent=2),
            },
        ],
    )

    summary = response.choices[0].message.content or ""

    with open("summary.txt", "w", encoding="utf-8") as file:
        file.write(summary)

    print()
    print("=" * 60)
    print("TRANSACTION SUMMARY")
    print("=" * 60)
    print()
    print(summary)


if __name__ == "__main__":
    main()
