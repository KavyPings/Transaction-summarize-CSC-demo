from __future__ import annotations

import os

_model = None


def _get_embedder():
    global _model
    if _model is None:
        try:
            from sentence_transformers import SentenceTransformer  # type: ignore
        except ImportError:
            raise RuntimeError("Install sentence-transformers: pip install sentence-transformers")
        _model = SentenceTransformer("all-MiniLM-L6-v2")
    return _model


def _embed(texts: list[str]) -> list[list[float]]:
    return [v.tolist() for v in _get_embedder().encode(texts, convert_to_numpy=True)]


DIMENSION = 384  # all-MiniLM-L6-v2


def _get_index():
    try:
        from pinecone import Pinecone, ServerlessSpec  # type: ignore
    except ImportError:
        raise RuntimeError("Install pinecone: pip install pinecone")

    pc = Pinecone(api_key=os.getenv("PINECONE_API_KEY"))
    index_name = os.getenv("PINECONE_INDEX_NAME", "transaction-rag")

    existing = {idx.name: idx for idx in pc.list_indexes()}

    if index_name in existing:
        if existing[index_name].dimension != DIMENSION:
            pc.delete_index(index_name)
            existing = {}

    if index_name not in existing:
        pc.create_index(
            name=index_name,
            dimension=DIMENSION,
            metric="cosine",
            spec=ServerlessSpec(cloud="aws", region="us-east-1"),
        )

    return pc.Index(index_name)


def upsert_chunks(chunks: list[dict], namespace: str) -> None:
    index = _get_index()
    embeddings = _embed([c["text"] for c in chunks])

    vectors = [
        {
            "id": f"{namespace}_{c['id']}",
            "values": emb,
            "metadata": {
                "text": c["text"],
                "type": c.get("type", ""),
                "label": c.get("label", ""),
            },
        }
        for c, emb in zip(chunks, embeddings)
    ]
    index.upsert(vectors=vectors, namespace=namespace)


def query_context(question: str, namespace: str, top_k: int = 5) -> list[str]:
    index = _get_index()
    question_vec = _embed([question])[0]

    # Core transaction chunks (transaction, risk_flags, checklist, additional) are always
    # included so the LLM has the full transaction picture — not just whatever the XLSX
    # evidence chunks happen to score highest on similarity.
    tx_results = index.query(
        vector=question_vec,
        top_k=10,
        namespace=namespace,
        include_metadata=True,
        filter={"type": {"$ne": "evidence"}},
    )
    tx_texts = [m.metadata["text"] for m in tx_results.matches]

    # Separately retrieve the most semantically relevant evidence chunks
    ev_results = index.query(
        vector=question_vec,
        top_k=3,
        namespace=namespace,
        include_metadata=True,
        filter={"type": {"$eq": "evidence"}},
    )
    ev_texts = [m.metadata["text"] for m in ev_results.matches]

    return tx_texts + ev_texts
