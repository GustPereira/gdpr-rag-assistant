// Types mirroring the backend records.
export interface Citation {
  ref: number
  source: string
  title: string
  score: number
  text: string
}

export interface StructuredAnswer {
  answer: string
  cited_refs: number[]
}

export interface AnswerResponse {
  question: string
  answer: StructuredAnswer
  citations: Citation[]
}

// Asks the RAG. Nginx (prod) / Vite proxy (dev) forwards /api/ask -> api:8000/ask.
export async function ask(question: string, signal?: AbortSignal): Promise<AnswerResponse> {
  const res = await fetch('/api/ask', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ question }),
    signal,
  })

  if (!res.ok) {
    const body = await res.text()
    throw new Error(`Error ${res.status}: ${body}`)
  }

  return res.json()
}

// Callbacks for the streaming RAG (M5). Citations arrive first (immediately),
// then the answer flows in token-by-token, then onDone fires.
export interface StreamHandlers {
  onCitations: (citations: Citation[]) => void
  onToken: (token: string) => void
  onDone: () => void
  onError: (message: string) => void
}

// Streams the answer over Server-Sent Events. We use fetch + a ReadableStream
// reader (not EventSource, which only supports GET) and parse the SSE frames
// ("event:" / "data:" lines separated by a blank line) ourselves.
export async function askStream(
  question: string,
  handlers: StreamHandlers,
  signal?: AbortSignal,
): Promise<void> {
  const res = await fetch('/api/ask/stream', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ question }),
    signal,
  })

  if (!res.ok || !res.body) {
    const body = await res.text().catch(() => '')
    handlers.onError(`Error ${res.status}: ${body}`)
    return
  }

  const reader = res.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  // Dispatch one fully-received SSE frame (its "event" and "data" lines).
  const dispatch = (frame: string) => {
    let event = 'message'
    let data = ''
    for (const line of frame.split('\n')) {
      if (line.startsWith('event:')) event = line.slice(6).trim()
      else if (line.startsWith('data:')) data += line.slice(5).trim()
    }
    if (!data) return

    const payload = JSON.parse(data)
    if (event === 'citations') handlers.onCitations(payload as Citation[])
    else if (event === 'token') handlers.onToken(payload as string)
    else if (event === 'error') handlers.onError((payload as { message: string }).message)
    else if (event === 'done') handlers.onDone()
  }

  for (;;) {
    const { done, value } = await reader.read()
    if (done) break
    buffer += decoder.decode(value, { stream: true })

    // Frames are separated by a blank line ("\n\n").
    let sep: number
    while ((sep = buffer.indexOf('\n\n')) !== -1) {
      const frame = buffer.slice(0, sep)
      buffer = buffer.slice(sep + 2)
      if (frame.trim()) dispatch(frame)
    }
  }
}
