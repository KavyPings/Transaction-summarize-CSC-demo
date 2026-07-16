class PageAgent {
  getPage() {
    return {
      pageType: document.body.dataset.pageType || 'unknown',
      txnId:    document.body.dataset.txnId    || null,
    };
  }

  async start() {
    const res = await fetch('/agent/start', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ page: this.getPage() }),
    });
    return res.json();
  }

  async ask(question, chatHistory) {
    const res = await fetch('/agent/chat', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({
        question,
        page:         this.getPage(),
        chat_history: chatHistory,
      }),
    });
    return res.json();
  }
}
