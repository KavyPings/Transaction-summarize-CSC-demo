# Transaction Approval Summary Generator

This project loads a transaction payload, filters it with an allow-list, sends only the sanitized payload to Azure OpenAI, and generates a concise approval summary for a human reviewer.

## What it does

- Reads the raw transaction JSON locally
- Keeps only review-relevant fields
- Excludes identifiers, bank details, names, and internal references
- Sends only the sanitized payload to Azure OpenAI
- Saves both the sanitized payload and the generated summary locally

## Files

- `app.py` - main entry point
- `filter_data.py` - allow-list filtering logic
- `prompt.py` - system prompt for the LLM
- `transaction.json` - starter input file
- `.env` - Azure OpenAI settings

## Azure OpenAI setup

Update `.env` with your real values:

```env
AZURE_OPENAI_ENDPOINT=https://your-azure-openai-resource.openai.azure.com/
AZURE_OPENAI_API_KEY=your_azure_openai_api_key
AZURE_OPENAI_API_VERSION=2024-02-01
AZURE_OPENAI_DEPLOYMENT=your_model_deployment_name
```

`AZURE_OPENAI_DEPLOYMENT` should be the Azure deployment name, not just the base model family.

## How to start

1. Install Python 3.10 or newer if it is not already installed, then open a new terminal window.
2. Create a virtual environment:

```powershell
python -m venv .venv
```

If that fails on Windows during `ensurepip`, use this fallback:

```powershell
& 'C:\Users\100646133\AppData\Local\Programs\Python\Python314\python.exe' -m venv .venv --without-pip
& 'C:\Users\100646133\AppData\Local\Programs\Python\Python314\python.exe' -m pip --python .\.venv\Scripts\python.exe install -r requirements.txt
```

3. Activate it:

```powershell
.\.venv\Scripts\Activate.ps1
```

4. Install dependencies:

```powershell
pip install -r requirements.txt
```

5. Put your real payload into `transaction.json`, or point the app at another file.
6. Run the app:

```powershell
python app.py --input transaction.json
```

If your machine uses a different Python launcher, use that launcher in place of `python`.

## Output

After a run, the app will create:

- `sanitized_payload.json`
- `summary.txt`

It also prints the summary to the terminal.

## Notes

- The app never sends the original raw payload to the model.
- Checklist groups are preserved so their original grouping is not lost.
- If your exported payload contains minor JSON issues like blank values, the loader attempts a small local repair before parsing.
