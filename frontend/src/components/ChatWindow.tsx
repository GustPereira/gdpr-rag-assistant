import { useEffect, useRef, useState } from 'react'
import { askStream, type Citation } from '../lib/api'
import ArticleCard from './ArticleCard'

// One conversation turn. The backend is stateless (each question is independent);
// this history is visual only.
interface Turn {
  id: number
  question: string
  answerText: string // accumulates the streamed tokens
  citations?: Citation[]
  error?: string
  loading: boolean // true until the first token (or citations) arrive
  done: boolean // true once the stream has finished
}

const EXAMPLES = [
  'Do I need consent to process personal data?',
  "What are the data subject's rights?",
  'What is the right to be forgotten?',
]

export default function ChatWindow() {
  const [turns, setTurns] = useState<Turn[]>([])
  const [input, setInput] = useState('')
  const [busy, setBusy] = useState(false)
  const bottomRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [turns])

  async function submit(question: string) {
    const q = question.trim()
    if (!q || busy) return

    const id = Date.now()
    setTurns((prev) => [...prev, { id, question: q, answerText: '', loading: true, done: false }])
    setInput('')
    setBusy(true)

    const patch = (p: Partial<Turn>) =>
      setTurns((prev) => prev.map((t) => (t.id === id ? { ...t, ...p } : t)))

    try {
      await askStream(q, {
        // Citations arrive first — show the article cards immediately.
        onCitations: (citations) => patch({ citations }),
        // Append each token as it streams in.
        onToken: (token) =>
          setTurns((prev) =>
            prev.map((t) =>
              t.id === id ? { ...t, answerText: t.answerText + token, loading: false } : t,
            ),
          ),
        onDone: () => patch({ loading: false, done: true }),
        onError: (message) => patch({ error: message, loading: false, done: true }),
      })
    } catch (e) {
      patch({ error: String(e), loading: false, done: true })
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex h-screen flex-col bg-slate-950 text-slate-100">
      {/* Header */}
      <header className="border-b border-slate-800 px-6 py-4">
        <div className="mx-auto flex max-w-3xl items-center justify-between">
          <div>
            <h1 className="text-xl font-bold tracking-tight">GDPR Assistant ⚖️</h1>
            <p className="text-sm text-slate-400">
              Ask questions about the EU General Data Protection Regulation
            </p>
          </div>
          <span className="rounded-full border border-emerald-700 bg-emerald-900/30 px-3 py-1 text-xs text-emerald-300">
            100% local · free
          </span>
        </div>
      </header>

      {/* Conversation */}
      <main className="flex-1 overflow-y-auto px-6 py-6">
        <div className="mx-auto max-w-3xl space-y-6">
          {turns.length === 0 && <EmptyState onPick={submit} />}

          {turns.map((turn) => (
            <div key={turn.id} className="space-y-3">
              {/* Question (right) */}
              <div className="flex justify-end">
                <div className="max-w-[85%] rounded-2xl rounded-br-sm bg-indigo-600 px-4 py-2.5 text-sm">
                  {turn.question}
                </div>
              </div>

              {/* Answer (left) */}
              <div className="flex justify-start">
                <div className="w-full max-w-[95%] space-y-3">
                  <div className="rounded-2xl rounded-bl-sm border border-slate-800 bg-slate-900 px-4 py-3 text-sm leading-relaxed">
                    {turn.loading && <Thinking />}
                    {turn.error && <span className="text-rose-400">{turn.error}</span>}
                    {turn.answerText && (
                      <p className="whitespace-pre-wrap">
                        {turn.answerText}
                        {!turn.done && (
                          <span className="ml-0.5 inline-block animate-pulse">▋</span>
                        )}
                      </p>
                    )}
                  </div>

                  {/* Consulted GDPR articles (shown as soon as they arrive) */}
                  {turn.citations && turn.citations.length > 0 && (
                    <CitedArticles citations={turn.citations} />
                  )}
                </div>
              </div>
            </div>
          ))}
          <div ref={bottomRef} />
        </div>
      </main>

      {/* Input */}
      <footer className="border-t border-slate-800 px-6 py-4">
        <form
          className="mx-auto flex max-w-3xl gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            submit(input)
          }}
        >
          <input
            value={input}
            onChange={(e) => setInput(e.target.value)}
            placeholder="Ask something about the GDPR…"
            disabled={busy}
            className="flex-1 rounded-xl border border-slate-700 bg-slate-900 px-4 py-2.5 text-sm outline-none placeholder:text-slate-500 focus:border-indigo-500 disabled:opacity-50"
          />
          <button
            type="submit"
            disabled={busy || !input.trim()}
            className="rounded-xl bg-indigo-600 px-5 py-2.5 text-sm font-medium transition hover:bg-indigo-500 disabled:cursor-not-allowed disabled:opacity-40"
          >
            {busy ? '…' : 'Send'}
          </button>
        </form>
      </footer>
    </div>
  )
}

// Shows the articles consulted to ground the answer (the retrieved top-k). We
// show all of them — a small local model is unreliable at tracking which exact
// snippet number it used, so listing every consulted article is more honest.
function CitedArticles({ citations }: { citations: Citation[] }) {
  if (citations.length === 0) return null

  return (
    <div>
      <p className="mb-1.5 text-xs font-medium uppercase tracking-wide text-slate-500">
        Legal basis (articles consulted)
      </p>
      <div className="space-y-2">
        {citations.map((c) => (
          <ArticleCard key={c.ref} citation={c} />
        ))}
      </div>
    </div>
  )
}

function EmptyState({ onPick }: { onPick: (q: string) => void }) {
  return (
    <div className="mt-10 text-center">
      <p className="text-slate-400">Ask a question to get started. Examples:</p>
      <div className="mt-4 flex flex-wrap justify-center gap-2">
        {EXAMPLES.map((ex) => (
          <button
            key={ex}
            onClick={() => onPick(ex)}
            className="rounded-full border border-slate-700 bg-slate-900 px-3 py-1.5 text-sm text-slate-300 transition hover:border-indigo-500 hover:text-white"
          >
            {ex}
          </button>
        ))}
      </div>
    </div>
  )
}

function Thinking() {
  return (
    <span className="inline-flex items-center gap-2 text-slate-400">
      <span className="flex gap-1">
        <Dot delay="0ms" />
        <Dot delay="150ms" />
        <Dot delay="300ms" />
      </span>
      Thinking… (local generation may take a while)
    </span>
  )
}

function Dot({ delay }: { delay: string }) {
  return (
    <span
      className="h-1.5 w-1.5 animate-bounce rounded-full bg-slate-400"
      style={{ animationDelay: delay }}
    />
  )
}
