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

#  for aws key
# pyrefly: ignore [missing-import]
from openai import OpenAI
# pyrefly: ignore [missing-import]
from aws_bedrock_token_generator import provide_token
# for aws key

# --- OLD (Azure) 
# from openai import AzureOpenAI

from filter_data import build_summary_payload
from prompt import SYSTEM_PROMPT

load_dotenv()


# for aws key
def create_client() -> tuple[OpenAI, str]:
    base_url = os.getenv("BEDROCK_BASE_URL")
    model = os.getenv("BEDROCK_MODEL")
    project = os.getenv("BEDROCK_PROJECT")
    region = os.getenv("AWS_REGION") or os.getenv("AWS_DEFAULT_REGION")

    missing = [
        name
        for name, value in {
            "BEDROCK_BASE_URL": base_url,
            "BEDROCK_MODEL": model,
        }.items()
        if not value
    ]
    if missing:
        missing_text = ", ".join(missing)
        raise RuntimeError(
            f"Missing AWS Bedrock settings in .env: {missing_text}"
        )

    # Use a ready Bedrock API key if provided, otherwise generate a
    # short-lived bearer token from the AWS access key/secret in .env.
    api_key = os.getenv("AWS_BEARER_TOKEN_BEDROCK") or provide_token(
        region=region
    )

    client = OpenAI(
        base_url=base_url,
        api_key=api_key,
        project=project,
    )
    return client, model
# ===== for aws key =====


# --- OLD (Azure)
# def create_client() -> tuple[AzureOpenAI, str]:
#     endpoint = os.getenv("AZURE_OPENAI_ENDPOINT")
#     api_key = os.getenv("AZURE_OPENAI_API_KEY")
#     api_version = os.getenv("AZURE_OPENAI_API_VERSION", "2024-02-01")
#     deployment = os.getenv("AZURE_OPENAI_DEPLOYMENT")
#
#     missing = [
#         name
#         for name, value in {
#             "AZURE_OPENAI_ENDPOINT": endpoint,
#             "AZURE_OPENAI_API_KEY": api_key,
#             "AZURE_OPENAI_DEPLOYMENT": deployment,
#         }.items()
#         if not value
#     ]
#     if missing:
#         missing_text = ", ".join(missing)
#         raise RuntimeError(
#             f"Missing Azure OpenAI settings in .env: {missing_text}"
#         )
#
#     client = AzureOpenAI(
#         azure_endpoint=endpoint,
#         api_key=api_key,
#         api_version=api_version,
#     )
#     return client, deployment


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
