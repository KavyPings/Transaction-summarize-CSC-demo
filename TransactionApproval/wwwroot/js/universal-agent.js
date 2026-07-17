class PageAgent {
  getResource() {
    return {
      resourceType: document.body.dataset.resourceType || 'unknown',
      resourceId:   document.body.dataset.resourceId   || null,
    };
  }

  async start() {
    const res = await fetch('/agent/start', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ resource: this.getResource() }),
    });
    return res.json();
  }

  async ask(question, chatHistory) {
    const res = await fetch('/agent/chat', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({
        question,
        resource:     this.getResource(),
        chat_history: chatHistory,
      }),
    });
    return res.json();
  }
}
